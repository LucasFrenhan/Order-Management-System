---

description: "Lista de tarefas do REQ-16 — Rastreabilidade de ponta a ponta do pedido (traces, métricas e logs correlacionados)"
---

# Tasks: Rastreabilidade de ponta a ponta do pedido

**Input**: Design documents from `/specs/016-observabilidade-opentelemetry/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/telemetria.md](./contracts/telemetria.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição (Princípio V) e os critérios de aceite da issue #37
pedem testes de integração com banco e broker reais da propagação pelo outbox e pelo
RabbitMQ; o SC-006 pede a varredura de dados sensíveis. Os testes de cada fase vêm antes da
implementação e devem falhar primeiro.

**Organization**: por história de usuário, depois de uma fundação com os pacotes, o registro
do SDK, o amostrador, as opções e o coletor de teste. A US1 (trace de ponta a ponta) é o MVP.
A US2 e a US3 dependem da migration da US1; a US4 e a US5 dependem só da fundação; a US6 fecha
o ambiente local e os casos de desligado e fora do ar.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US6)

## Convenções válidas para todas as tarefas

- Nomes em português; tudo que conhece o SDK fica em `src/PedidoFulfillment.Infrastructure/Observabilidade/`;
  os serviços existentes usam só `TelemetriaOms` (BCL: `ActivitySource`, `Meter`).
- Nomes de spans, atributos, valores e métricas exatamente como em `contracts/telemetria.md`,
  definidos como constantes em `TelemetriaOms`; nenhum texto solto nos serviços.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Quatro pacotes novos (T002), nenhum outro. Uma migration só (`ContextoRastreio`, T013).
- Envelope v1, schemas e OpenAPI inalterados: `SchemaEventosTestes` e os testes de contrato
  verdes sem edição.
- IDs, nomes, níveis e mensagens de log inalterados (FR-015); nenhum log novo com ID novo.
- Telemetria desligada por padrão nas fábricas de teste; só os testes de `Observabilidade/` a
  ligam. Os testes existentes do REQ-01 ao REQ-15 passam sem afrouxar asserções.
- Nada sensível em atributos: nunca e-mail, token, corpo, `Authorization` nem valores de SQL.
- Testcontainers e `OMS_EXIGIR_DOCKER` (REQ-07); nenhuma espera fixa em teste (esperas por
  condição, como nos testes existentes).
- Todos os testes de `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/` ficam na
  coleção `ColecaoObservabilidade`, com `DisableParallelization = true` (analyze U1): os
  ouvintes de `ActivitySource` valem para o processo inteiro.
- Commits: `tipo(escopo): descrição [REQ-16]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-16-observabilidade-opentelemetry` e registrar em Notes a contagem de testes como linha de base (1.033 no fim do REQ-15).
- [X] T002 Pacotes (plan, Justificativa das dependências novas): em `Directory.Packages.props`, grupo novo `<ItemGroup Label="Observabilidade">` com comentário citando `specs/016-observabilidade-opentelemetry/plan.md` e a regra das duas semanas (1.20.x recente demais): `OpenTelemetry.Extensions.Hosting` 1.19.1, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1, `OpenTelemetry.Instrumentation.AspNetCore` 1.19.0, `OpenTelemetry.Instrumentation.SqlClient` 1.19.0; atualizar o comentário do topo com o plano do REQ-16; referências em `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`. Conferir com `dotnet list package --vulnerable --include-transitive` que nenhum transitivo vulnerável entrou (com `CentralPackageTransitivePinningEnabled`, fixar o que for preciso, como o `System.Security.Cryptography.Pkcs`).

---

## Phase 2: Foundational (Blocking Prerequisites)

### Testes da fundação

- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Observabilidade/AmostradorSemSqlRaizTestes.cs` (R-09, contrato › Amostragem): span raiz da fonte `OpenTelemetry.Instrumentation.SqlClient` → `Drop`; span de SQL com pai amostrado → `RecordAndSample`; com pai não amostrado → `Drop`; raiz de outra fonte com taxa 1,0 → amostrada, com taxa 0,0 → descartada; a decisão de um filho segue o pai mesmo com taxa 0,0.
- [X] T004 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Observabilidade/ContextoRastreioTestes.cs` (data-model, FR-013): `Atual()` devolve o `Activity.Current.Id` W3C de 55 caracteres e nulo sem activity ou com `IdFormat` hierárquico; `TentarLer` aceita um `traceparent` válido e recusa nulo, vazio, tamanho diferente de 55, versão `ff`, trace id só de zeros e caracteres fora de hex, sem lançar; `DoCabecalho` lê `traceparent` dos cabeçalhos do RabbitMQ como `byte[]` UTF-8 ou `string` e devolve nulo quando ausente ou inválido.
- [X] T005 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesObservabilidadeTestes.cs` e no `OpcoesReenvioDlqTestes.cs` existente (data-model › Configuração), como `OpcoesEstornosTestes`: padrões válidos (`Habilitada` false, `TaxaAmostragem` 1,0, `IntervaloAmostragemOutboxSegundos` 30, `MetricaIntervaloSegundos` 60); limites e fora deles com a mensagem citando a chave ("Observabilidade:TaxaAmostragem deve estar entre 0 e 1.", "Observabilidade:IntervaloAmostragemOutboxSegundos deve estar entre 5 e 300.", "ReenvioDlq:MetricaIntervaloSegundos deve estar entre 10 e 300."); `Ligada(configuracao)` verdadeiro com `OTEL_EXPORTER_OTLP_ENDPOINT` preenchido ou `Habilitada = true`, falso sem os dois.

### Implementação da fundação

- [X] T006 Telemetria comum em `src/PedidoFulfillment.Infrastructure/Observabilidade/` (contrato):
  - `TelemetriaOms.cs`: `public static class TelemetriaOms` com `Nome = "PedidoFulfillment"`, `ActivitySource Fonte`, `Meter Medidor`, constantes dos nomes de spans (`outbox publicar {tipo}` etc. como métodos que montam o nome), dos atributos (`oms.pedido.id`, `oms.evento.tipo`, `oms.consumidor`, `oms.tentativa`, `oms.resultado`, `oms.rotina`, `oms.etapa`, `oms.reenvio.*`, `oms.limpeza.*`, `oms.outbox.espera_ms`, `user.id`) e dos valores enumerados; os instrumentos entram nas fases de cada história;
  - `ContextoRastreio.cs`: `Atual()`, `TentarLer(string?, out ActivityContext)` (via `ActivityContext.TryParse`, tamanho exato 55) e `DoCabecalho(IDictionary<string, object?>?)`;
  - `AmostradorSemSqlRaiz.cs`: `Sampler` que descarta raiz da fonte do SqlClient e delega a `ParentBasedSampler(TraceIdRatioBasedSampler(taxa))`.

  T003 e T004 devem passar.
- [X] T007 Opções e registro (R-01, R-08, R-11):
  - `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesObservabilidade.cs` (seção `Observabilidade`, validador, `Ligada`) e `MetricaIntervaloSegundos` em `OpcoesReenvioDlq.cs` (só a opção; o uso entra na US5);
  - `src/PedidoFulfillment.Infrastructure/Observabilidade/ExtensoesObservabilidade.cs`: `AdicionarObservabilidade(this IHostApplicationBuilder builder, string nomeServico)`. Sempre: `ActivityTrackingOptions = TraceId | SpanId` e `IncludeScopes` no console. Só se `Ligada`: `AddOpenTelemetry()` com `ConfigureResource(AddService(nomeServico))` (o `OTEL_SERVICE_NAME` sobrescreve), tracing com `AddSource(TelemetriaOms.Nome, "RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber")`, `AddAspNetCoreInstrumentation` (filtro de `/health` e `/health/pronto`, sem enriquecer com corpo nem cabeçalhos), `AddSqlClientInstrumentation` (texto parametrizado, sem parâmetros), `SetSampler(new AmostradorSemSqlRaiz(taxa))`; métricas com `AddMeter(TelemetriaOms.Nome, "System.Runtime", "Microsoft.AspNetCore.Hosting", "Microsoft.AspNetCore.Server.Kestrel")`; logs com `builder.Logging.AddOpenTelemetry` (`IncludeFormattedMessage`, `IncludeScopes`); `UseOtlpExporter()` só com `OTEL_EXPORTER_OTLP_ENDPOINT` preenchido; intervalo de envio das métricas de 30 000 ms por padrão (FR-017, analyze I1), respeitando `OTEL_METRIC_EXPORT_INTERVAL` quando definida;
  - chamada em `src/PedidoFulfillment.API/Program.cs` (`"oms-api"`) e em `src/PedidoFulfillment.Worker/ProgramaWorker.cs` (`"oms-worker"`); seção `Observabilidade` comentada em `src/PedidoFulfillment.Worker/appsettings.json` e na API, como as demais.

  T005 deve passar.
- [X] T008 Infraestrutura de teste (R-12): `[CollectionDefinition(nameof(ColecaoObservabilidade), DisableParallelization = true)]` em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/ColecaoObservabilidade.cs`, com os contêineres de banco e broker como as coleções existentes; `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ColetorTelemetria.cs` com um `BaseProcessor<Activity>` (guarda spans terminados), um `BaseProcessor<LogRecord>` (guarda registros com `TraceId`, `SpanId`, `EventId`, atributos) e um `MeterListener` para o meter `PedidoFulfillment`, com consultas por trace id e esperas por condição; `FabricaApi` e `FabricaWorker` ganham a opção `ComTelemetria(ColetorTelemetria)`, que liga `Observabilidade:Habilitada` e registra os processadores pelo `ConfigureOpenTelemetryTracerProvider`/`LoggerProvider`; sem a opção, nada muda.
- [X] T009 Validação do risco do SqlClient (plan › Complexity Tracking): em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/InstrumentacaoBaseTestes.cs` (novo), um `GET /api/v1/pedidos/{id}` com a telemetria ligada gera um span de requisição com `http.route` e ao menos um span de SQL filho dele, com o texto parametrizado e sem o valor do id; um `GET /health/pronto` não gera span; o despachante ocioso por 3 ciclos não gera nenhum trace (R-09). Se o span de SQL não aparecer com o Microsoft.Data.SqlClient 7.1.1, parar e registrar em Notes antes de seguir (remover o pacote e registrar a limitação na spec).

**Checkpoint**: suíte inteira verde, com a contagem da T001 mais os testes novos. Commit `feat(observabilidade): registra o OpenTelemetry com OTLP, amostragem e logs correlacionados [REQ-16]`.

---

## Phase 3: User Story 1 - Seguir um pedido de ponta a ponta num único trace (Priority: P1) 🎯 MVP

**Goal**: o pedido aparece num único trace da rota de criação até o consumidor de entrega, atravessando o outbox e o broker, com tentativas e DLQ (FR-001 a FR-006, FR-013).

**Independent Test**: criar um pedido com a telemetria ligada e conferir no coletor um único trace com a requisição, as três publicações e os três consumos.

### Testes da US1

- [X] T010 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/TraceFluxoPedidoTestes.cs` (aceites da US1, contrato), com API e Worker ligados ao mesmo coletor: (1) criar um pedido e esperar a ordem de entrega → um único trace id contém `POST /api/v1/pedidos`, `outbox publicar pedido.criado`, `outbox publicar estoque.reservado`, `outbox publicar pagamento.aprovado` e três `deliver` com `oms.consumidor` `estoque`, `pagamento` e `entrega`, `oms.resultado = processado`, `oms.pedido.id` e `messaging.message.id`; cada `outbox publicar` de um evento gravado por consumidor é descendente do `deliver` que o gravou; (2) broker parado entre a criação e a publicação → mesmo trace, `oms.outbox.espera_ms` maior que o tempo parado; (3) manipulador falso que falha uma vez → dois `deliver` do mesmo consumidor no trace, o primeiro com status de erro, `error.type` e `oms.resultado = nova_tentativa`, o segundo `processado`; (4) falha sempre → o último `deliver` com `oms.resultado = dlq` e erro; (5) mensagem repetida → `deliver` com `oms.resultado = duplicado`, sem erro; (6) `Observabilidade:TaxaAmostragem = 0` nos dois processos → o pedido chega à ordem de entrega e nenhum span com o `oms.pedido.id` dele é exportado em nenhum dos dois coletores (FR-022, analyze C1); o span de requisição tem `user.id` igual ao `sub` do token e `oms.pedido.id`.
- [X] T011 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ContextoRastreioPersistenciaTestes.cs` (novo; data-model; na `ColecaoObservabilidade`), com uma activity W3C ativa de um `ActivitySource` próprio do teste, com ouvinte só dele (analyze U1), gravar um pedido com evento e um pedido de reenvio → as três linhas têm `ContextoRastreio` igual ao `Activity.Id`; sem activity → nulo; atualizar a linha com outra activity ativa não troca o valor; um evento com contexto inválido gravado por fora é publicado normalmente num trace novo (FR-013).

### Implementação da US1

- [X] T012 [US1] Interceptor (R-03, data-model): `src/PedidoFulfillment.Infrastructure/Observabilidade/InterceptorContextoRastreio.cs` (`SaveChangesInterceptor`, síncrono e assíncrono, só entradas `Added` de `EventoSaida`, `Pedido` e `PedidoReenvioDlq`, valor de `ContextoRastreio.Atual()`); propriedade de sombra `ContextoRastreio` "`varchar(55) NULL`" (`HasMaxLength(55)`, `IsUnicode(false)`) em `ConfiguracaoEventoSaida.cs`, `ConfiguracaoPedido.cs` e `ConfiguracaoPedidoReenvioDlq.cs`; registro do interceptor no `AddDbContext` de `ExtensoesInfraestrutura.cs`.
- [X] T013 [US1] Migration `ContextoRastreio` (`dotnet ef migrations add ContextoRastreio --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`), conferindo que só adiciona as três colunas nulas, sem índice; a migration nova nas listas de `ModeloSincronizadoTestes.cs` e `MigracaoBancoTestes.cs`. T011 deve passar.
- [X] T014 [US1] Despachante (R-03): em `src/PedidoFulfillment.Infrastructure/Mensageria/DespachanteEventosSaida.cs`, ler a propriedade de sombra do evento e, com contexto válido, abrir `TelemetriaOms.Fonte.StartActivity(nome, ActivityKind.Internal, contexto)` em volta do `BasicPublishAsync`, com `oms.evento.tipo`, `messaging.message.id` e `oms.outbox.espera_ms` (agora − `CriadoEm`); sem contexto, abrir a mesma activity como raiz; falha de publicação marca a activity com erro. Nenhum span fora do evento publicado.
- [X] T015 [US1] Consumidor (R-02, R-06): em `src/PedidoFulfillment.Infrastructure/Mensageria/ConsumidorEventos.cs`, marcar `Activity.Current` (o `deliver` do cliente) com `oms.consumidor`, `oms.evento.tipo`, `oms.pedido.id` (do envelope), `oms.tentativa` e `oms.resultado` (`processado`, `duplicado`, `nova_tentativa`, `dlq`, `irrecuperavel`), e `SetStatus(Error)` com `error.type` nos três últimos; nenhuma activity própria.
- [X] T016 [US1] API (FR-001): `src/PedidoFulfillment.API/Observabilidade/AtributosRequisicao.cs`, middleware registrado em `Program.cs` depois do `UseAuthentication`, que marca o span da requisição com `user.id` (claim `sub`) e, nas rotas `/api/v1/pedidos/{id}`, com `oms.pedido.id`; na criação, `EndpointsPedidos` marca `oms.pedido.id` com o id criado, sem mudar a resposta. T010 deve passar.

**Checkpoint**: suíte inteira verde. Commit `feat(observabilidade): propaga o trace do pedido pelo outbox e pelo RabbitMQ [REQ-16]`.

---

## Phase 4: User Story 2 - Mensagem reenviada da DLQ ligada ao pedido e ao pedido de reenvio (Priority: P1)

**Goal**: a cópia reenviada continua o trace de origem, com link para a execução, que tem link para a requisição do Admin (FR-007, FR-008).

**Independent Test**: levar uma mensagem à DLQ, registrar e executar o reenvio, e conferir os links e o trace.

### Testes da US2

- [X] T017 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/TraceReenvioDlqTestes.cs` (aceites da US2), com o manipulador falso que falha até ser liberado: a mensagem vai para a DLQ no trace T; o Admin registra o reenvio (trace R); o executor roda → span `reenviar_dlq estoque` raiz com `oms.reenvio.pedido_id`, contagens e link para um span de R; `reenviar {tipo}` no trace T com link para `reenviar_dlq`; o `deliver` da cópia no trace T, filho do `publish` do reenvio, `processado`. Mensagem fora do filtro ou acima do limite não gera `reenviar {tipo}`. Mensagem na DLQ sem `traceparent` (publicada com a telemetria desligada) → `reenviar {tipo}` filho de `reenviar_dlq`, e a cópia é processada.

### Implementação da US2

- [X] T018 [US2] Em `src/PedidoFulfillment.Infrastructure/Mensageria/ExecutorReenviosDlq.cs`: span raiz `reenviar_dlq {consumidor}` por pedido executado (só com o lock), com link para o `ContextoRastreio` do pedido de reenvio (propriedade de sombra), `oms.reenvio.pedido_id`, `oms.consumidor`, contagens e `oms.resultado` (`concluido` ou `falhou`, com erro).
- [X] T019 [US2] Em `src/PedidoFulfillment.Infrastructure/Mensageria/ReenvioDlq.cs`: para cada mensagem selecionada, ler `ContextoRastreio.DoCabecalho` **antes** de montar a cópia e abrir `reenviar {tipo}` com esse pai (ou como filho do `reenviar_dlq`, se ausente) e link para o `reenviar_dlq`, em volta do `BasicPublishAsync`. T017 deve passar.

**Checkpoint**: suíte inteira verde. Commit `feat(observabilidade): liga o reenvio da DLQ ao trace do pedido e ao pedido do Admin [REQ-16]`.

---

## Phase 5: User Story 3 - Rotinas periódicas visíveis e ligadas ao pedido (Priority: P2)

**Goal**: cada rodada efetiva é um trace; cada pedido tratado é um passo com link para a criação dele (FR-009 a FR-011).

**Independent Test**: um pedido parado reconciliado, um estorno confirmado e uma limpeza, cada um com a rodada e os passos no coletor.

### Testes da US3

- [X] T020 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/TraceRotinasTestes.cs` (aceites da US3), chamando as rodadas direto como os testes do REQ-12 a REQ-14: reconciliação de um pedido parado criado com a telemetria ligada → `reconciliar` raiz e `reconciliar pedido` filho com `oms.pedido.id`, `oms.etapa`, `oms.resultado` e link para o trace de criação; o `outbox publicar` do evento gravado pela rodada é descendente do `reconciliar pedido`; estorno pendente confirmado → `processar_estornos` e `estornar pedido` com link e `oms.resultado = confirmado`; limpeza → `limpar` e um `limpar {alvo}` por alvo com `oms.limpeza.linhas_apagadas`; pedido criado com a telemetria desligada → passo sem link; rodada sem o lock (segunda instância segurando `sp_getapplock`) → nenhum span.

### Implementação da US3

- [X] T021 [US3] Reconciliação: `ContextoRastreio` (`string?`) em `src/PedidoFulfillment.Application/Reconciliacao/PedidoParado.cs`, projetado por `EF.Property<string?>` em `src/PedidoFulfillment.Infrastructure/Reconciliacao/ConsultaPedidosParados.cs`; em `ReconciliadorPedidos.cs`, span `reconciliar` só depois de pegar o lock, e `reconciliar pedido` em volta de cada `ReconciliarAsync`, com link quando o contexto é válido e o resultado no atributo.
- [X] T022 [US3] Estornos: `IConsultaEstornosPendentes.ListarAsync` passa de `Task<IReadOnlyList<Guid>> ListarAsync(int quantidade, CancellationToken)` para `Task<IReadOnlyList<EstornoPendente>> ListarAsync(int quantidade, CancellationToken)`, com `sealed record EstornoPendente(Guid PedidoId, string? ContextoRastreio)` em `src/PedidoFulfillment.Application/Pagamentos/EstornoPendente.cs` (data-model), com a projeção em `ConsultaEstornosPendentes.cs` e o ajuste em quem a usa (`ProcessadorEstornos.cs` e `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs`, sem mudar asserções de regra); em `ProcessadorEstornos.cs`, `processar_estornos` depois do lock e `estornar pedido` por estorno, com link e resultado.
- [X] T023 [US3] Limpeza: em `src/PedidoFulfillment.Infrastructure/Mensageria/LimpezaMensageria.cs`, `limpar` depois do lock e `limpar {alvo}` por alvo (`enviados`, `processados`, `chaves`, `pedidos_reenvio`) com as linhas apagadas. T020 deve passar.

**Checkpoint**: suíte inteira verde. Commit `feat(observabilidade): registra as rodadas das rotinas ligadas aos pedidos [REQ-16]`.

---

## Phase 6: User Story 4 - Logs ligados ao trace (Priority: P2)

**Goal**: todo log dentro de um span carrega o trace e o passo, no destino e no console, sem mudar os logs (FR-014, FR-015).

**Independent Test**: criar um pedido e conferir os logs 4001, 5001, 5006, 6001, 7001 e 8001 no coletor com o trace id do pedido.

- [X] T024 [P] [US4] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/LogsCorrelacionadosTestes.cs` (aceites da US4): os logs 4001 (API), 5001, 5006, 6001, 7001 e 8001 (Worker) de um pedido saem no coletor com o mesmo `TraceId` do trace do pedido e o `SpanId` de um span desse trace, com `EventId`, nível e mensagem formatada iguais aos de hoje (comparar com a `CapturaLogs` existente); no console (provedor de teste do formatador simples com escopos), um log dentro de um span mostra o `TraceId`.
- [X] T025 [US4] Ajustar `ExtensoesObservabilidade.cs` (T007) só se a T024 apontar falta (por exemplo, escopos no exportador ou no console); registrar em Notes se nada precisou mudar.

**Checkpoint**: suíte inteira verde. Commit `test(observabilidade): comprova os logs correlacionados ao trace [REQ-16]`.

---

## Phase 7: User Story 5 - Métricas da mensageria e das rotinas (Priority: P2)

**Goal**: as seis métricas do contrato, com o atraso máximo do FR-017 e o 5011 no ritmo de hoje (FR-016 a FR-018).

**Independent Test**: broker parado com pedidos criados e mensagens na DLQ; conferir os gauges e os contadores pelo `MeterListener`.

### Testes da US5

- [X] T026 [P] [US5] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/MetricasTestes.cs` (aceites da US5, contrato › Métricas), com intervalos curtos por configuração: `oms.outbox.eventos_pendentes` e `oms.outbox.pendente_mais_antigo.idade` sobem com o despachante parado e voltam a 0 depois; `oms.mensageria.dlq.mensagens{oms.consumidor}` reflete a DLQ; `oms.mensageria.mensagens` conta `processado`, `duplicado`, `nova_tentativa` e `dlq`; `oms.rotinas.itens` conta reconciliação (`recuperado`, `esgotado`), estornos (`confirmado`, `falhou`) e reenvio (`concluido`); `oms.limpeza.linhas_apagadas{oms.limpeza.alvo}` soma as linhas; nomes e unidades exatamente os do contrato.
- [X] T027 [P] [US5] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/MonitorDlqTestes.cs` (existente; clarify, pergunta 2): com a telemetria ligada, a DLQ é lida a cada `MetricaIntervaloSegundos` e o 5011 sai no máximo uma vez por `AvisoIntervaloSegundos` por consumidor; com a telemetria desligada, o comportamento de hoje (casos existentes sem mudança); com `AvisoHabilitado = false` e a telemetria ligada, lê sem logar.

### Implementação da US5

- [X] T028 [US5] Instrumentos em `TelemetriaOms.cs` (contrato: nomes, unidades UCUM e tipos) e uso: contador 4 em `ConsumidorEventos.cs` junto do `oms.resultado` da T015; contador 5 em `ReconciliadorPedidos.cs`, `ProcessadorEstornos.cs` e `ExecutorReenviosDlq.cs`; contador 6 em `LimpezaMensageria.cs`.
- [X] T029 [US5] `src/PedidoFulfillment.Infrastructure/Observabilidade/AmostradorOutbox.cs` (R-07): `BackgroundService` registrado no Worker só com a telemetria ligada; a cada `IntervaloAmostragemOutboxSegundos`, `SELECT COUNT_BIG(*), MIN(CriadoEm) FROM EventosSaida WHERE EnviadoEm IS NULL` e guarda os valores; gauges 1 e 2 leem o último valor (idade 0 sem pendentes; sem leitura ainda, sem ponto); falha mantém o último valor, com log em Debug sem ID novo.
- [X] T030 [US5] `src/PedidoFulfillment.Infrastructure/Mensageria/MonitorDlq.cs`: com a telemetria ligada, leitura a cada `MetricaIntervaloSegundos`, última contagem por consumidor num dicionário concorrente lido pelo gauge 3, e 5011 limitado a um por `AvisoIntervaloSegundos` por consumidor (contado desde o último aviso); com o aviso desligado e a telemetria ligada, lê só para a métrica; com a telemetria desligada, como hoje. `MetricaIntervaloSegundos` em `src/PedidoFulfillment.Worker/appsettings.json`, com comentário. T026 e T027 devem passar.

**Checkpoint**: suíte inteira verde. Commit `feat(observabilidade): publica as metricas do outbox, da DLQ, do consumo e das rotinas [REQ-16]`.

---

## Phase 8: User Story 6 - Ver tudo localmente e trocar de destino por configuração (Priority: P3)

**Goal**: painel local na demo e no perfil próprio; desligado e fora do ar sem efeito; nada sensível exportado (FR-019 a FR-025).

**Independent Test**: subir a demo e ver o trace no painel; derrubar o painel e continuar criando pedidos.

### Testes da US6

- [X] T031 [P] [US6] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/TelemetriaDesligadaTestes.cs` (aceites 3 e 4 da US6): sem `OTEL_EXPORTER_OTLP_ENDPOINT` e sem `Habilitada`, um pedido percorre o fluxo, nenhuma linha ganha `ContextoRastreio` e nenhuma mensagem leva `traceparent`; com `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:1` (fechado), o pedido percorre o fluxo no mesmo prazo dos testes existentes, sem exceção nos logs da aplicação; com a telemetria ligada só na API e desligada no Worker, o pedido percorre o fluxo e a mensagem sai sem `traceparent` (caso de borda, analyze C3).
- [X] T032 [P] [US6] Em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/DadosSensiveisTestes.cs` (SC-006, FR-023): registrar um Cliente, fazer login, criar e consultar um pedido, cancelar outro e levar uma mensagem à DLQ; nenhum atributo de span, de log exportado ou de métrica contém o e-mail, a senha, o token JWT, o cabeçalho `Authorization` nem os valores de parâmetros de SQL (o id do pedido só pode aparecer em `oms.pedido.id`, `url.path` e `messaging.*`).

### Implementação da US6

- [X] T033 [US6] `docker-compose.yml` (R-10): serviço `aspire-dashboard` com `image: mcr.microsoft.com/dotnet/aspire-dashboard:13.6.0` (comentário: 2026-10-01; a 13.6.1 tinha menos de duas semanas), `profiles: [demo, observabilidade]`, `ASPIRE_DASHBOARD_UNSECURED_ALLOW_ANONYMOUS: "true"` com comentário "só local; dados em memória", portas `${OMS_DASHBOARD_PORTA:-18888}:18888` e `${OMS_OTLP_PORTA:-4317}:18889`; `OTEL_EXPORTER_OTLP_ENDPOINT: http://aspire-dashboard:18889` na `api` e no `worker`; cabeçalho do arquivo com o comando do perfil `observabilidade`. `.env.example` com `OMS_DASHBOARD_PORTA` e `OMS_OTLP_PORTA`. `src/PedidoFulfillment.API/appsettings.Development.json` e o do Worker sem endpoint fixo (a variável liga; comentário apontando o quickstart §5). Validar com `docker compose config` e `docker compose --profile demo up -d --wait --build`. T031 e T032 devem passar.

**Checkpoint**: suíte inteira verde. Commit `feat(observabilidade): adiciona o painel local de telemetria ao compose [REQ-16]`.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T034 [P] Desempenho (SC-005, analyze C2): rodar `DesempenhoEntregaTestes` em três situações (telemetria desligada; ligada com o coletor; ligada com `OTEL_EXPORTER_OTLP_ENDPOINT=http://127.0.0.1:1`, destino inacessível) e registrar em Notes o p95 de cada, comparando com o REQ-15; a desligada e a com destino inacessível não podem piorar mais de 5%.
- [X] T035 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo nomes, atributos, métricas, intervalos e IDs de log no código e os links relativos:
  - `docs/fluxos/08-publicacao-e-consumo-de-eventos.md`: cabeçalho `traceparent`, coluna `ContextoRastreio` do outbox, span `outbox publicar`, atributos do `deliver`, métrica 4, configuração `Observabilidade`;
  - `docs/fluxos/02-criacao-de-pedido.md`: o trace começa na rota e o contexto é gravado no pedido e no evento; em "Limitações", um chamador com `traceparent` próprio decide a amostragem dos próprios pedidos (analyze A1);
  - `docs/fluxos/09-reconciliacao.md` e `10-estorno.md`: spans das rodadas, link ao pedido, métrica 5;
  - `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md`: reenvio no trace original, spans da limpeza, métricas 3, 5 e 6, leitura de 1 min com o 5011 a cada 5 min, atraso de ponta a ponta das métricas e agregação dos gauges por `max` entre instâncias (FR-016a, analyze C4); em "Limitações", "Não há métricas nem alertas além dos logs" vira "Não há alertas" com a issue #37 citada pela resolução das métricas;
  - `docs/fluxos/README.md`: linha sobre a telemetria (fluxo transversal) e o painel local na tabela de dependências externas.
- [X] T036 [P] Documentação do projeto: `README.md` (Estado atual com o REQ-16; Próximos passos com #18; seção de observabilidade com o painel, as variáveis `OTEL_*`, os destinos do contrato e a agregação dos gauges por `max` com várias instâncias do Worker (FR-016a); roteiros com o quickstart do REQ-16).
- [X] T037 Varredura de segredos, `dotnet format --verify-no-changes`, `dotnet list package --vulnerable --include-transitive` e a suíte local inteira (zero ignorados); registrar a contagem em Notes.
- [ ] T038 Executar o quickstart (§2 a §6) e registrar em Notes.
- [ ] T039 Fazer push e abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #37`, citando os quatro pacotes novos e a justificativa no plano, a migration, o painel no compose e os documentos de fluxo atualizados. Acompanhar o `build-testes` e o `contrato-compatibilidade` até ficarem verdes, inclusive depois de cada push posterior, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: T001 antes de tudo; T002 antes da fundação.
- **Foundational** (T003 a T009): bloqueia todas as histórias. T006 antes de T007; T007 antes de T008 e T009.
- **US1** (T010 a T016): depende da fundação. T012 antes de T013; T013 antes de T014.
- **US2** (T017 a T019): depende da US1 (migration e propagação pelo outbox).
- **US3** (T020 a T023): depende da US1 (migration e coluna em `Pedidos`).
- **US4** (T024, T025): depende só da fundação; a verificação completa usa o fluxo da US1.
- **US5** (T026 a T030): depende da fundação; T028 usa os pontos marcados na US1, US2 e US3 (`oms.resultado`), então roda depois delas.
- **US6** (T031 a T033): T031 e T032 dependem da US1; T033 é independente.
- **Polish**: depois de tudo. A T039 exige push.

### Parallel Opportunities

- T003, T004 e T005.
- T010 e T011.
- US2, US3 e US4 entre si, depois da US1.
- T026 e T027; T031 e T032.
- T034, T035 e T036.

### Exemplo: US1

```text
T010 TraceFluxoPedidoTestes.cs        ┐ em paralelo
T011 ContextoRastreioPersistenciaTestes.cs ┘
T012 interceptor → T013 migration → T014 despachante → T015 consumidor → T016 API
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: o pedido de ponta a ponta num único trace, que é o caso central da
issue. A US2 fecha o caso da DLQ citado na issue; a US3 liga as rotinas; a US4 comprova os
logs; a US5 traz as métricas; a US6 entrega o painel local.

### Incremental Delivery

1. Fundação → `feat(observabilidade): registra o OpenTelemetry com OTLP, amostragem e logs correlacionados [REQ-16]`.
2. US1 → `feat(observabilidade): propaga o trace do pedido pelo outbox e pelo RabbitMQ [REQ-16]`.
3. US2 → `feat(observabilidade): liga o reenvio da DLQ ao trace do pedido e ao pedido do Admin [REQ-16]`.
4. US3 → `feat(observabilidade): registra as rodadas das rotinas ligadas aos pedidos [REQ-16]`.
5. US4 → `test(observabilidade): comprova os logs correlacionados ao trace [REQ-16]`.
6. US5 → `feat(observabilidade): publica as metricas do outbox, da DLQ, do consumo e das rotinas [REQ-16]`.
7. US6 → `feat(observabilidade): adiciona o painel local de telemetria ao compose [REQ-16]`.
8. Polish → `docs(fluxos): ... [REQ-16]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): alertas, fornecedor pago, troca do compose pelo Aspire
  AppHost, latência por etapa, instrumentação de HTTP de saída (sem gateway real),
  `oms.estornos.pendentes` e `messaging.process.duration`.
- **Analyze (2026-10-26)**: aplicadas as correções I1 (FR-017 e SC-004 separam leitura e envio;
  envio das métricas a cada 30 s por padrão na T007; atraso total no contrato), U1 (coleção
  `ColecaoObservabilidade` sem paralelismo e `ActivitySource` próprio na T011), C1 (caso de
  amostragem zero na T010), C2 (destino inacessível na T034), I2 (assinatura de
  `ListarAsync` na T022 e no data-model), A1 (o `traceparent` de entrada é aceito e registrado
  como limitação no fluxo 02), C3 (API ligada e Worker desligado na T031), C4 (agregação por
  `max` na T035 e na T036) e D1 (8 serviços no plano).
- A T009 valida o maior risco do plano (SqlClient 1.19 com o Microsoft.Data.SqlClient 7.1.1);
  se falhar, registrar aqui e na spec antes de seguir.
- **T001 (linha de base, 2026-10-26)**: 1.033 testes, 636 unitários e 397 de integração, zero
  ignorados.
- **Fundação e US1 (2026-10-26)**, num só commit (`bf339be`): o mapeamento da coluna e a
  migration entraram cedo, porque o teste do SqlClient (T009) já exige o modelo sincronizado.
  - **Risco do plano resolvido (T009)**: a instrumentação do SqlClient 1.19 gera os spans com
    o Microsoft.Data.SqlClient 7.1.1, com o texto parametrizado e sem valores.
  - As opções `SetDbQueryParameters` e `EnableTraceContextPropagation` do SqlClient são internas
    na 1.19 (só por variáveis experimentais `OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_*`), e o padrão
    delas já é desligado: o registro usa `AddSqlClientInstrumentation()` sem opções.
  - O amostrador reconhece o SQL pelo atributo `db.system.name` que a instrumentação passa na
    criação do span (o `SamplingParameters` não traz o nome da fonte).
  - O interceptor ficou no `OnConfiguring` do `ContextoBancoDados`, e não no `AddDbContext`:
    vale também para os contextos criados fora da injeção (testes e ferramentas).
  - A rota do `POST` de pedidos é registrada como `/api/v1/pedidos/` (com a barra), e o
    `oms.pedido.id` da criação vem do `Location` da resposta, no middleware, sem mexer nos
    endpoints (o plano previa `EndpointsPedidos`).
  - Como as fontes valem para o processo inteiro, os provedores da API e do Worker dos testes
    recebem os mesmos spans: o coletor guarda cada span uma vez, pelo id.
  - A coleção `ColecaoObservabilidade` ficou no `CenarioObservabilidade.cs`, com a
    `MensageriaTeste` como fixture própria.
  - Com o broker pausado, a publicação começa logo e espera a confirmação: o intervalo aparece
    na duração do span, e não em `oms.outbox.espera_ms`. O teste mede o intervalo no trace.
- **US2 a US4 (2026-10-26)**:
  - **Achado (US3)**: o ASP.NET Core cria uma activity em toda requisição, mesmo sem
    OpenTelemetry; o interceptor passou a gravar o contexto só com a fonte `PedidoFulfillment`
    ouvida (SDK ligado), para cumprir o FR-020. O teste de persistência ouve também essa fonte.
  - A recuperação da etapa de estoque aplica a regra da etapa e grava `estoque.reservado`; o
    teste confere esse evento como filho do passo.
  - T025: nada precisou mudar no registro dos logs.
- **US5 (2026-10-26)**: os testes do `MonitorDlq` com a telemetria ligada ficaram em
  `Observabilidade/MetricasTestes.cs`, na coleção sem paralelismo, e não em
  `MonitorDlqTestes.cs` (regra U1); os contadores 4, 5 e 6 são conferidos nos testes de trace,
  que já montam os cenários. O estado dos gauges é estático, por processo (`EstadoMetricas`).
- **US6 (2026-10-26)**: no processo de teste, os ouvintes são globais, então o caso "ligada só
  na API" prova só que o fluxo termina; a ausência de `traceparent` com o Worker desligado só
  se observa em processos separados (quickstart §5).
- **T034 (desempenho, 2026-10-26)**: duas rodadas de 20 medições em cada situação. p95 do
  `POST` 9 a 11 ms e do pedido à entrega 382 a 386 ms, iguais com a telemetria desligada, ligada
  com o coletor e ligada com o destino inacessível (o REQ-13 mediu 381 ms). Sem piora.
- **T035**: fluxo novo `12-rastreabilidade-e-metricas.md` (transversal) e a linha no índice;
  fluxos 02, 08, 09, 10, 11 e o índice atualizados; a limitação de métricas saiu do fluxo 11.
- **T037 (2026-10-26)**: 1.104 testes verdes (683 unitários e 421 de integração), zero
  ignorados; `dotnet format --verify-no-changes` e `dotnet list package --vulnerable` limpos;
  nenhum segredo novo (só as credenciais de desenvolvimento já documentadas e o modo sem login
  do painel local, comentado no compose). Duas intermitências corrigidas antes:
  - o ASP.NET Core encerra o span da requisição depois de mandar a resposta, então o teste
    pode procurá-lo antes de ele existir: `CriacaoAsync` espera o span;
  - a fábrica da API (top-level `Program`) descarta o host numa thread própria e volta antes de
    o provedor de traces ser descartado: os testes que dependem da telemetria desligada esperam
    a fonte sem ouvinte (`EsperarSemOuvintesAsync`).
