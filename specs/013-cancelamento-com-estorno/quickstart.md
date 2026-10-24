# Quickstart: validação do REQ-13

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/estorno-e-cancelamento.md](./contracts/estorno-e-cancelamento.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- cancelamento pela API de pedido pago, com e sem ordem: resposta igual à de hoje, estoque
  devolvido, estorno pendente, ordem com `canceladaEm`;
- rodada de estornos: confirmação, falha com nova tentativa, alerta 7008, dois Workers com
  um estorno só no gateway;
- corridas do cancelamento com a cobrança (inclusive a cobrança aprovada para pedido
  cancelado), com o despacho, com a criação da ordem e com outro cancelamento;
- esgotamento da etapa de entrega na reconciliação;
- idempotência do gateway simulado na cobrança e no estorno;
- os testes do REQ-01 ao REQ-12 continuam verdes, inclusive a conformidade do
  `openapi-v1.yaml` com a resposta real.

## 2. Local: cancelar um pedido pago (US1 e US2)

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

Crie um pedido como Cliente (`POST /api/v1/pedidos`) e espere ele chegar a
`ProntoParaEnvio` (com `GatewayPagamento:TaxaAprovacaoPercentual` = 100 para não depender de
sorte). Cancele como operação:

```http
PATCH /api/v1/pedidos/{id}/status
Authorization: Bearer <token do operador>
Content-Type: application/json

{ "status": "Cancelado", "motivo": "Cliente desistiu depois do pagamento." }
```

**Esperado**:
- 200 com o pedido `Cancelado` e `entrega.canceladaEm` preenchido;
- no log da API, 4003 e 7005 (`EstornoSolicitado`, origem `Operacao`) e 8004;
- em até 1 minuto, no log do Worker, 7006 (`EstornoConfirmado`) e o resumo 7012;
- no banco:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT TOP 5 PedidoId, Situacao, EstornoSolicitadoEm, EstornadoEm, TentativasEstorno FROM Pagamentos ORDER BY AtualizadoEm DESC"
```

O pagamento aparece `Estornado`. Tentar registrar o rastreio desse pedido responde 409, e
tentar cancelar um pedido `Despachado` também.

## 3. Local: estorno com o Worker fora do ar (US1 cenário 6)

Pare o Worker, cancele outro pedido pago e confira que a API responde 200 normalmente e que
o pagamento fica `EstornoPendente`. Suba o Worker: na primeira rodada, o estorno é
confirmado.

Falha e alerta do gateway (cenários 6 e 7) são validados pelos testes de integração, com um
gateway falso que falha ou recusa: o simulado sempre confirma.

## 4. Local: esgotamento da etapa de entrega (US3)

Como no REQ-12, o esgotamento é validado pelo teste de integração com a criação da ordem
forçada a falhar. No banco, o resultado esperado é: pedido `Cancelado`, sem autor, com o
motivo `Não foi possível criar a ordem de entrega.`, estoque devolvido, pagamento
`EstornoPendente` e, na rodada seguinte, `Estornado`; logs 9002 e 7005 (origem
`Reconciliacao`).

## 5. Pedidos pagos e cancelados antes do REQ-13

O REQ-13 não estorna sozinho os pedidos que já estavam cancelados com pagamento aprovado
(research R-10). Para encontrá-los:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT p.Id, p.Numero, g.Valor FROM Pedidos p JOIN Pagamentos g ON g.PedidoId = p.Id WHERE p.Status = 'Cancelado' AND g.Situacao = 'Aprovado'"
```

A decisão de estorná-los é da operação.
