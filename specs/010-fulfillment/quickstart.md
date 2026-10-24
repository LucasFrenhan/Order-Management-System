# Quickstart: validação do REQ-10

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/consumidor-entrega.md](./contracts/consumidor-entrega.md)

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com os testes de entrega executados contra SQL Server e RabbitMQ
reais (Testcontainers):
- pedido aprovado chega a `ProntoParaEnvio`, com uma ordem sem dados de despacho e
  `entrega.criada` válido pelo schema;
- reentrega de `pagamento.aprovado` sem segunda ordem nem segundo evento;
- pedido ignorado (cancelado, despachado, já com ordem) e pedido já em `ProntoParaEnvio`;
- serialização, ida e volta e recusa de mensagem fora do schema de `entrega.criada`;
- os testes do REQ-08 e do REQ-09 continuam verdes, sem o consumidor de entrega (research R-09).

## 2. Local: API e Worker

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

Para ver todos os pedidos aprovados, suba o Worker com
`$env:GatewayPagamento__TaxaAprovacaoPercentual = "100"`.

**Esperado**: no painel do RabbitMQ (`http://localhost:15672`), as filas `oms.entrega`,
`oms.entrega.dlq` e as três de espera existem. Desde o REQ-12, o despachante declara essas
filas antes de publicar, e um pedido pago que ficou sem ordem é recuperado pela
reconciliação; não é mais preciso esperar a fila existir para criar pedidos.

Crie a fila `teste.entrega`, durável, e ligue-a a `oms.eventos` com a chave
`entrega.criada`.

Para conferir pedidos e ordens:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT p.Numero, p.Status, g.Situacao, o.Id AS Ordem, o.Transportadora, o.CodigoRastreio, o.DespachadoEm FROM Pedidos p LEFT JOIN Pagamentos g ON g.PedidoId = p.Id LEFT JOIN OrdensEntrega o ON o.PedidoId = p.Id ORDER BY p.CriadoEm DESC"
```

## 3. Pedido pago fica pronto para envio (US1)

Crie um pedido como Cliente (`POST /api/v1/pedidos`) com produtos em estoque.

**Esperado**, em até 10 s (SC-005):
- o pedido aparece em `ProntoParaEnvio` no `GET /api/v1/pedidos/{id}`;
- no histórico, `Processando → ProntoParaEnvio` sem autor, com `Ordem de entrega criada.`;
- uma linha em `OrdensEntrega` sem transportadora, rastreio e data de despacho;
- `teste.entrega` recebeu `entrega.criada` com o pedido, o número, a ordem e os itens,
  como no [exemplo](./contracts/consumidor-entrega.md#exemplo).

## 4. Reentrega (US1 cenário 4)

No painel, republique em `oms.eventos`, com a chave `pagamento.aprovado`, a mensagem do
pedido do passo 3 (mesmo `idEvento`; o corpo está em `EventosSaida`).

**Esperado**: nenhuma ordem nova, status sem mudança e nenhum `entrega.criada` novo.

## 5. Pedido cancelado antes do fulfillment (US3)

O intervalo entre o pagamento e o fulfillment é curto demais para cancelar à mão. Use o
teste de integração `PedidoIgnoradoNaEntregaTestes`, que cancela o pedido pela operação e
depois publica o `pagamento.aprovado` dele.

**Esperado**: nenhuma ordem, status `Cancelado` e nenhum `entrega.criada`; log 8002 com o
motivo.

## 6. Contrato HTTP

```powershell
dotnet test PedidoFulfillment.sln --filter "FullyQualifiedName~ConformidadeOpenApi"
```

**Esperado**: verde; o OpenAPI v1 não muda (FR-012).
