# Quickstart: validação do REQ-28

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/estornos.md](./contracts/estornos.md) |
**Eventos**: [contracts/eventos](./contracts/eventos/)

## 1. Testes automatizados

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet build
dotnet test PedidoFulfillment.sln
dotnet format --verify-no-changes
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers), cobrindo:

- listagem, consulta, tentativa forçada e baixa: sucesso, 400, 401, 403 (Cliente em todas; o
  OperadorLogistica e o Admin desativado na baixa), 404 e 409;
- a baixa contra a rodada segurando o lock: gravada quando o lock sai antes de 5 s; 409
  `ESTORNO_RODADA_EM_ANDAMENTO` quando não sai; e a rodada seguinte sem chamar o gateway;
- a tentativa forçada na frente da fila, com o lote menor que a fila, e a marca mantida com o
  circuito aberto;
- o `pagamento.estorno-atrasado` uma única vez, com 10 falhas seguidas e dois Workers;
- o `pagamento.estorno-baixado` e o aviso `pedido.estorno-confirmado` na mesma gravação da baixa,
  sem `pagamento.estornado`;
- a conformidade dos dois schemas novos e do `openapi-v1.yaml` com as respostas reais;
- os testes dos REQs anteriores continuam verdes.

## 2. Ambiente local

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1

# terminal 2: alerta e rodada rápidos para o roteiro
$env:Estornos__IntervaloSegundos = "10"
$env:Estornos__LimiteAlertaSegundos = "30"
$env:Estornos__TentativasAlerta = "2"
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http
```

A API usa os padrões de alerta (24 h e 3 tentativas), então a listagem só mostra
`alemDoLimiteAlerta: true` com a mesma configuração nos dois processos (research R-09). Para o
roteiro, defina as mesmas variáveis no terminal 1 também.

Usuários: `admin@oms.local` / `AdminDev2026` e `operador@oms.local` / `OperadorDev2026`
([fluxo 1](../../docs/fluxos/01-autenticacao.md)).

## 3. Roteiro manual

### 3.1 Estorno travado (US1 e US3)

1. Pare a API de pagamento simulada: `docker compose stop pagamentos`.
2. Cancele como operador um pedido já pago (como no
   [quickstart do REQ-13](../013-cancelamento-com-estorno/quickstart.md#2-local-cancelar-um-pedido-pago-us1-e-us2)).
3. Espere duas rodadas. No log do Worker: 7007, depois 7008 e **um** 7024; nas rodadas seguintes,
   só 7008.
4. Liste como operador:

   ```http
   GET /api/v1/estornos/pendentes
   Authorization: Bearer <token do operador>
   ```

   **Esperado**: o estorno com `tentativas` ≥ 2, `ultimoErro` "Falha do gateway: …", sem
   identificadores do gateway, `alemDoLimiteAlerta: true` e `atrasadoEm` preenchido.
5. No painel do RabbitMQ, uma única mensagem `pagamento.estorno-atrasado` para esse estorno.

### 3.2 Tentativa forçada (US4)

1. Com o gateway ainda parado, `POST /api/v1/estornos/{id}/tentativas` como operador: 202, com
   `tentativaForcadaEm`; um segundo pedido devolve o mesmo valor.
2. Suba o gateway: `docker compose start pagamentos`. Na rodada seguinte, 7006: o estorno é
   confirmado e sai da listagem; a consulta mostra `situacao: Confirmado` e
   `tentativaForcadaEm: null`.

### 3.3 Baixa manual (US2)

1. Repita o 3.1 com outro pedido, deixando o gateway parado.
2. Como operador, `POST /api/v1/estornos/{id}/baixa`: 403.
3. Como Admin:

   ```http
   POST /api/v1/estornos/{id}/baixa
   Authorization: Bearer <token do Admin>
   Content-Type: application/json

   { "motivo": "Devolvido por transferência pelo financeiro.", "referenciaExterna": "TED-778812" }
   ```

   **Esperado**: 200 com `situacao: Baixado`, o autor, o motivo e a referência; no log da API, 7022
   sem o motivo; no RabbitMQ, `pagamento.estorno-baixado` com `referenciaExterna`; um aviso
   `pedido.estorno-confirmado` no SSE do Cliente dono do pedido.
4. Suba o gateway e espere duas rodadas: nenhum 7006, 7007 ou 7008 para esse pedido.
5. Repita a baixa: 409 `ESTORNO_NAO_PENDENTE`.

### 3.4 Métrica

Com o painel de telemetria (`docker compose --profile demo up -d --wait`), o medidor
`oms.estornos.pendentes` mostra os pendentes por `oms.alerta`, e muda a cada rodada.

## 4. Documentação

Conferir os fluxos [10](../../docs/fluxos/10-estorno.md), [8](../../docs/fluxos/08-publicacao-e-consumo-de-eventos.md),
[12](../../docs/fluxos/12-rastreabilidade-e-metricas.md) e [13](../../docs/fluxos/13-notificacao-de-status.md):
rotas, códigos, logs 7021 a 7024, eventos novos, medidor, e as duas limitações do fluxo 10 trocadas
pela #57.
