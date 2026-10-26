# Quickstart: validação do REQ-15

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/eventos-cancelamento-e-estorno.md](./contracts/eventos-cancelamento-e-estorno.md) | **Modelo**: [data-model.md](./data-model.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- conformidade dos schemas `pedido.cancelado` e `pagamento.estornado` (válidos e inválidos);
- um `pedido.cancelado` por cancelamento, em cada caminho da tabela do
  [data-model](./data-model.md#pedidocancelado--pedidocancelado), com os campos certos;
- um `pagamento.estornado` por estorno confirmado, inclusive depois de falhas e de conflito;
- cancelamentos simultâneos com um só evento; falha na gravação sem evento;
- a guarda do armazenamento recusando um cancelamento sem `pedido.cancelado` (research R-03);
- os consumidores internos sem receber os tipos novos;
- os testes do REQ-01 ao REQ-14 continuam verdes.

## 2. Local: receber os eventos numa fila própria

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

No [painel do RabbitMQ](http://localhost:15672) (`oms` / `oms-dev-local`), crie a fila
`sistema-externo.teste` e ligue-a à troca `oms.eventos` com as chaves `pedido.cancelado` e
`pagamento.estornado`. Essa fila faz o papel de um sistema de fora.

## 3. Cancelamento pelo Cliente e pela operação (US1, cenários 1 a 3)

- Crie um pedido como Cliente (`POST /api/v1/pedidos`) e cancele-o logo, ainda em `Pendente`
  (`PATCH /api/v1/pedidos/{id}/status` com `{ "status": "Cancelado", "motivo": "..." }`).
  **Esperado**: na fila, um `pedido.cancelado` com `motivo` `Cliente`, `etapaEsgotada` nulo e
  `estornoPendente` falso; o texto do motivo digitado não aparece.
- Com `GatewayPagamento:TaxaAprovacaoPercentual` = 100, crie outro pedido, espere
  `ProntoParaEnvio` e cancele como operação. **Esperado**: `pedido.cancelado` com `motivo`
  `Operacao`, `estoqueDevolvido`, `estornoPendente` e `ordemEntregaCancelada` verdadeiros; em
  até 1 minuto, um `pagamento.estornado` com o valor do pedido e os identificadores do gateway.

## 4. Falta de estoque (US1, cenário 4)

Crie um pedido com quantidade acima do estoque de um produto. **Esperado**: o pedido fica
`Cancelado` e, na fila, um `pedido.cancelado` com `motivo` `EstoqueInsuficiente` e todas as
flags falsas. O `estoque.insuficiente` continua saindo (só aparece na fila se ela também for
ligada a essa chave).

## 5. Reconciliação (US1, cenários 5 a 7)

O esgotamento é validado pelos testes de integração, com a etapa forçada a falhar, como nos
REQ-12 e REQ-13. Esperado no evento: `motivo` `ReconciliacaoEsgotada` e `etapaEsgotada`
`Estoque`, `Pagamento` ou `Entrega`, com as flags da tabela do data-model.

## 6. Conferir o outbox

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -I -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT TOP 10 Id, Tipo, CriadoEm, EnviadoEm FROM EventosSaida WHERE Tipo IN ('pedido.cancelado', 'pagamento.estornado') ORDER BY Id DESC"
```

**Esperado**: uma linha `pedido.cancelado` por pedido cancelado e uma `pagamento.estornado` por
estorno confirmado, todas com `EnviadoEm` preenchido, com ou sem a fila de teste ligada.

## 7. Pedido com pagamento recusado (FR-005)

Com `GatewayPagamento:TaxaAprovacaoPercentual` = 0, crie um pedido. **Esperado**: o pedido vai
para `PagamentoRejeitado`, sai `pagamento.recusado` como hoje e **nenhum** `pedido.cancelado`.
