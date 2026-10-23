# Quickstart: validação do REQ-09

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/consumidor-pagamento.md](./contracts/consumidor-pagamento.md)

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com os testes de pagamento executados contra SQL Server e
RabbitMQ reais (Testcontainers):
- aprovação e recusa pelo Worker, com a taxa fixada em 100% ou 0%;
- devolução do estoque na recusa e pedido ignorado (cancelado ou já pago);
- proporção do gateway simulado em 10.000 decisões, entre 88% e 92% (SC-002);
- mensagens publicadas válidas pelos schemas `pagamento.aprovado` e `pagamento.recusado`;
- os testes do REQ-08 continuam verdes, sem o consumidor de pagamento (research R-09).

## 2. Local: API e Worker

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

**Esperado**: no painel do RabbitMQ (`http://localhost:15672`), as filas `oms.pagamento`,
`oms.pagamento.dlq` e as três de espera existem.

Crie a fila `teste.pagamento`, durável (o RabbitMQ 4 não aceita fila transitória não exclusiva), e ligue-a a `oms.eventos` com a chave `pagamento.*`.

Para conferir pedidos, pagamentos e estoque, use o SQL Server do ambiente local:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT p.Numero, p.Status, p.EstoqueReservado, g.Situacao, g.Valor, g.IdentificadorTransacao FROM Pedidos p LEFT JOIN Pagamentos g ON g.PedidoId = p.Id ORDER BY p.CriadoEm DESC"
```

## 3. Pagamento aprovado

Pare o Worker e suba-o de novo com a taxa em 100%:

```powershell
$env:GatewayPagamento__TaxaAprovacaoPercentual = "100"
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http
```

Crie um pedido com um produto em estoque, como no
[quickstart do REQ-05](../005-criacao-pedidos/quickstart.md) §3.

**Esperado**:
- em segundos, `teste.pagamento` recebe `pagamento.aprovado` com o pedido, o valor total e
  o identificador `SIM-...`;
- o pedido aparece em `Processando`, com a entrada `Pagamento aprovado.` no histórico;
- o estoque continua abatido (a reserva segue para o envio).

> **Com o REQ-10**: o Worker completo também tem o consumidor de entrega, e o pedido
> aprovado segue logo depois para `ProntoParaEnvio`, com a entrada `Ordem de entrega criada.`
> ([quickstart do REQ-10](../010-fulfillment/quickstart.md) §3). Para ver o pedido parado em
> `Processando`, confira o histórico: a entrada `Pagamento aprovado.` vem antes.

## 4. Pagamento recusado

Repita o §3 com `$env:GatewayPagamento__TaxaAprovacaoPercentual = "0"`, anotando antes o
estoque do produto.

**Esperado**:
- `teste.pagamento` recebe `pagamento.recusado` com o motivo
  `Pagamento recusado pelo emissor.`;
- o pedido aparece em `PagamentoRejeitado`, e o pagamento está `Recusado`;
- o estoque do produto voltou ao valor anotado;
- `PATCH /api/v1/pedidos/{id}/status` com `Cancelado` responde 409
  `PEDIDO_TRANSICAO_INVALIDA` (status final).

## 5. Pedido cancelado antes do pagamento

1. Pare o Worker, crie um pedido e cancele-o pela API.
2. No painel do RabbitMQ, publique na troca `oms.eventos`, com a chave
   `estoque.reservado`, o [exemplo](../008-estoque/contracts/consumidor-estoque.md#exemplos)
   de `estoque.reservado` com um `idEvento` novo e o `pedidoId` do pedido cancelado. Nas
   propriedades, use `message_id` igual ao `idEvento`, `type` = `estoque.reservado` e
   `delivery_mode` = 2.
3. Suba o Worker.

**Esperado**: nenhum pagamento é gravado, nenhum evento chega a `teste.pagamento`, e o log
do Worker mostra `PedidoIgnoradoNoPagamento` (EventId 7003).

## 6. Configuração inválida

```powershell
$env:GatewayPagamento__TaxaAprovacaoPercentual = "150"
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http
```

**Esperado**: o Worker não sobe, e o erro cita `GatewayPagamento:TaxaAprovacaoPercentual`
sem mostrar o valor. Remova a variável depois
(`Remove-Item Env:GatewayPagamento__TaxaAprovacaoPercentual`).

## 7. Demonstração

```powershell
docker compose --profile demo up -d --wait --build
```

**Esperado**: `api` e `worker` ficam `healthy`, com a taxa padrão (90%), e o job
"Demonstração" do CI continua verde.
