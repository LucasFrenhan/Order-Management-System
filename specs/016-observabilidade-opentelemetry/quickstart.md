# Quickstart: validação do REQ-16

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/telemetria.md](./contracts/telemetria.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- trace único da criação do pedido até o consumidor de entrega, com o broker fora por um tempo,
  com nova tentativa e com ida para a DLQ (US1);
- mensagem reenviada da DLQ no trace original e ligada ao pedido de reenvio (US2);
- rodadas de reconciliação, estornos e limpeza com o pedido ligado ao trace de criação, e sem
  trace quando a rodada não pega o lock (US3);
- logs com o trace id e os mesmos IDs de evento (US4);
- as seis métricas (US5);
- telemetria desligada e destino inválido sem efeito nos fluxos; nenhum dado sensível
  exportado (US6, SC-006);
- os testes do REQ-01 ao REQ-15 continuam verdes, sem mudança.

## 2. Demo: painel junto com a aplicação (US6)

```powershell
docker compose --profile demo up -d --wait --build
```

Abra `http://localhost:18888` (painel; o aviso de acesso sem autenticação é esperado no uso
local). Em **Resources** aparecem `oms-api` e `oms-worker`.

Crie um pedido como Cliente (como no quickstart do REQ-05) e espere a ordem de entrega.

**Esperado** em **Traces**: um trace `POST /api/v1/pedidos` com, em ordem, a gravação, `outbox
publicar pedido.criado`, `publish`, `deliver` do estoque, `outbox publicar estoque.reservado`,
`deliver` do pagamento, `outbox publicar pagamento.aprovado` e `deliver` da entrega. Clicando em
um `deliver`, os atributos mostram `oms.consumidor`, `oms.pedido.id` e `oms.resultado =
processado`. Em **Structured logs**, filtrando pelo trace, aparecem os logs 4001, 5001, 5006,
6001, 7001 e 8001 dos dois serviços.

## 3. Outbox parado e métricas (US1, US5)

```powershell
docker compose stop rabbitmq
# crie dois ou três pedidos
docker compose start rabbitmq
```

**Esperado**: em **Metrics** do `oms-worker`, `oms.outbox.eventos_pendentes` sobe e
`oms.outbox.pendente_mais_antigo.idade` cresce enquanto o broker está parado, e voltam a 0 em
até 1 min depois que ele volta. O trace de cada pedido mostra o intervalo de espera entre a
requisição e o `outbox publicar`.

## 4. DLQ e reenvio (US2, US5)

Leve uma mensagem de um pedido à DLQ (por exemplo, como no quickstart do REQ-14) e peça o
reenvio como Admin.

**Esperado**:
- `oms.mensageria.dlq.mensagens{oms.consumidor=...}` mostra a fila em até 1 min (mais o
  intervalo de exportação);
- o trace do pedido ganha `reenviar {tipo}` e o `deliver` da cópia, com link para
  `reenviar_dlq {consumidor}`, que por sua vez tem link para o `POST` do Admin;
- o log 5011 continua no máximo a cada 5 min.

## 5. API pelo SDK (US6)

```powershell
docker compose --profile observabilidade up -d --wait
$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"
dotnet run --project src/PedidoFulfillment.API
```

**Esperado**: `oms-api` aparece no painel. Sem a variável, a API sobe e funciona como antes,
sem telemetria.

## 6. Destino fora do ar (US6)

```powershell
docker compose stop aspire-dashboard
# crie pedidos
```

**Esperado**: pedidos criados e processados normalmente, sem erro na API nem no Worker; a
telemetria desse intervalo se perde.
