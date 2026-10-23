# Quickstart: validação do REQ-08

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/consumidor-estoque.md](./contracts/consumidor-estoque.md)

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com os testes de estoque executados contra SQL Server e RabbitMQ
reais (Testcontainers):
- reserva, falta de estoque e pedido ignorado, pelo Worker;
- disputa pelo mesmo produto (SC-002);
- devolução no cancelamento, pela API;
- schema de `estoque.insuficiente`.

## 2. Local: API e Worker

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1 (aplica a migration ReservaEstoque)
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

**Esperado**: no painel do RabbitMQ (`http://localhost:15672`), as filas `oms.estoque`,
`oms.estoque.dlq` e as três de espera existem.

Crie a fila `teste.estoque` e ligue-a a `oms.eventos` com a chave `estoque.*`.

Para conferir o estoque, use o SQL Server do ambiente local:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT Sku, QuantidadeEstoque, Ativo FROM Produtos ORDER BY Sku"
```

## 3. Reserva

Crie um pedido com um produto em estoque, como no
[quickstart do REQ-05](../005-criacao-pedidos/quickstart.md) §3.

**Esperado**:
- em segundos, `teste.estoque` recebe `estoque.reservado` com o pedido e os itens;
- o estoque do produto caiu a quantidade do item;
- o pedido continua `Pendente` na consulta.

> **Com o pagamento (REQ-09)**: o Worker também cobra o pedido reservado, que vai para
> `Processando` (aprovado) ou `PagamentoRejeitado` (recusado, com o estoque devolvido). Para
> repetir este roteiro, suba o Worker com `$env:GatewayPagamento__TaxaAprovacaoPercentual = "100"`:
> o pedido termina em `Processando`, com o estoque ainda abatido.

## 4. Disputa pelo último item

1. Pare o Worker (Ctrl+C). Com um produto de estoque 1 (por exemplo `CAT-008`, se ainda
   tiver 1), crie dois pedidos de 1 unidade com clientes ou chaves de idempotência
   diferentes. A criação confere sem reservar, então os dois recebem 201.
2. Suba o Worker de novo.

**Esperado**:
- um pedido recebe `estoque.reservado`, e o outro fica `Cancelado`, com
  `estoque.insuficiente` em `teste.estoque`;
- o estoque do produto termina em 0.

## 5. Devolução no cancelamento

Cancele, como Cliente dono, o pedido reservado no §3. Com o pagamento ligado (REQ-09), o
pedido já está em `Processando`, e só a operação o cancela: use o token do Admin ou do
OperadorLogistica, com o mesmo resultado no estoque.

```http
PATCH /api/v1/pedidos/{id}/status
{ "status": "Cancelado", "motivo": "Desisti" }
```

**Esperado**: 200, como antes, e o estoque do produto volta ao valor de antes da reserva.
Cancelar o pedido do §4 que foi cancelado por falta de estoque não é possível (status
final), e nenhum estoque muda.

## 6. Pedido cancelado antes da reserva

1. Pare o Worker, crie um pedido e cancele-o pela API.
2. Suba o Worker.

**Esperado**: nenhum evento em `teste.estoque`, o estoque não muda, e o log do Worker
mostra `PedidoIgnoradoNaReserva` (EventId 6003).

## 7. Demonstração

```powershell
docker compose --profile demo up -d --wait --build
```

**Esperado**: `api` e `worker` ficam `healthy`, e o job "Demonstração" do CI continua
verde.
