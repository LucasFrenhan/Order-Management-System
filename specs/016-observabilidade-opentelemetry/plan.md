# Implementation Plan: Rastreabilidade de ponta a ponta do pedido (traces, métricas e logs correlacionados)

**Branch**: `feature/req-16-observabilidade-opentelemetry` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/016-observabilidade-opentelemetry/spec.md`

**Requisito**: REQ-16 (GitHub issue #37)

## Summary

- **Base** (US4, US6): `AdicionarObservabilidade` na Infrastructure, chamada pela API e pelo
  Worker. Liga o SDK do OpenTelemetry só com destino configurado (`OTEL_EXPORTER_OTLP_ENDPOINT`)
  ou com `Observabilidade:Habilitada = true` (testes). Registra traces (ASP.NET Core, SqlClient,
  as fontes nativas do RabbitMQ.Client e a fonte `PedidoFulfillment`), métricas (meter
  `PedidoFulfillment`, mais os meters nativos do runtime e do ASP.NET Core) e logs, tudo
  exportado por OTLP. Um amostrador próprio descarta spans raiz do SQL e envolve o
  `ParentBased(TraceIdRatioBased)` (R-01, R-08, R-09, R-11).
- **Outbox** (US1): coluna de sombra `ContextoRastreio` em `EventosSaida`, preenchida por um
  interceptor de `SaveChanges`; o despachante retoma o contexto ao publicar, e o RabbitMQ.Client
  injeta o `traceparent` e continua o trace no consumidor (R-02, R-03).
- **Consumo** (US1): `ConsumidorEventos` marca o span de entrega com consumidor, tipo, pedido,
  tentativa e resultado, e soma a métrica de mensagens (R-06, R-07).
- **Reenvio da DLQ** (US2): span por pedido de reenvio, ligado à requisição do Admin; cada
  mensagem reenviada continua o trace de origem dela (R-04).
- **Rotinas** (US3): reconciliação, estornos e limpeza com span raiz por rodada efetiva e um
  filho por pedido, ligado ao trace de criação do pedido (coluna de sombra em `Pedidos`) (R-05).
- **Métricas** (US5): seis instrumentos; `AmostradorOutbox` a cada 30 s; `MonitorDlq` lê a cada
  1 min com a telemetria ligada e mantém o 5011 a cada 5 min (R-07).
- **Local** (US6): Aspire Dashboard 13.6.0 em contêiner, nos perfis `demo` e
  `observabilidade` (R-10).
- **Persistência**: migration `ContextoRastreio`, com três colunas `varchar(55)` nulas.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- novas (justificadas abaixo): `OpenTelemetry.Extensions.Hosting` 1.19.1,
  `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1, `OpenTelemetry.Instrumentation.AspNetCore`
  1.19.0, `OpenTelemetry.Instrumentation.SqlClient` 1.19.0;
- as que já existem: `RabbitMQ.Client` 7.2.2 (`ActivitySource` nativas e propagação W3C),
  EF Core SqlServer 10.0.13 (interceptor e propriedades de sombra), `System.Diagnostics`
  (`ActivitySource`, `Meter`, `ActivityContext.TryParse`).

**Storage**: SQL Server; coluna `ContextoRastreio varchar(55) NULL` em `EventosSaida`, `Pedidos`
e `PedidosReenvioDlq` (migration `ContextoRastreio`). Nenhum índice novo.

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker), Testcontainers
MsSql e RabbitMq; coletor de teste como `BaseProcessor<Activity>`/`BaseProcessor<LogRecord>` e
`MeterListener` (R-12)

**Target Platform**: API e Worker em .NET 10; SQL Server 2022, RabbitMQ 4.3.6 e Aspire Dashboard
13.6.0 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- telemetria desligada: nenhum custo além de checagens de ouvinte (SC-005);
- telemetria ligada: ~25 spans por pedido no caminho feliz, exportados em lote, sem esperar o
  destino; métricas de estado sem consultar banco ou broker na leitura (FR-017).

**Constraints**:
- envelope v1, schemas e rotas da API inalterados (Princípio II);
- RabbitMQ.Client nativo, sem MassTransit; o cliente faz a injeção e a extração do contexto;
- IDs, níveis e mensagens de log inalterados (FR-015);
- nada sensível nos dados exportados: sem e-mail, token, corpo, cabeçalho de autorização nem
  valores de parâmetros de SQL (FR-023);
- a suíte atual (1.033 testes) passa sem mudança, com a telemetria desligada por padrão.

**Scale/Scope**:
- 1 extensão de registro, 1 amostrador de traces, 1 interceptor do EF, 1 classe de telemetria
  (fonte, meter, instrumentos e nomes), 1 serviço novo no Worker (`AmostradorOutbox`);
- ajustes em 8 serviços existentes (despachante, consumidor, reenvio, executor, monitor da
  DLQ, reconciliação, estornos e limpeza) e em 2 consultas (pedidos parados, estornos);
- 1 middleware na API (`user.id` e rota de pedido);
- 1 seção de opções (`Observabilidade`) e 1 opção nova em `ReenvioDlq`; 1 migration;
  compose, `.env.example` e `appsettings.Development.json`.

### Justificativa das dependências novas (constituição, Restrições Técnicas)

| Pacote | Projeto | Por que é necessário | Alternativa rejeitada |
|--------|---------|----------------------|------------------------|
| `OpenTelemetry.Extensions.Hosting` 1.19.1 | Infrastructure | SDK (provedores de traces, métricas e logs, amostradores, processadores em lote) e integração com o host genérico | Escrever um exportador próprio: reimplementaria o protocolo e o lote |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.19.1 | Infrastructure | Exportação OTLP, neutra em fornecedor (FR-019) | Exportadores de fornecedor: acoplam o código (fora do escopo) |
| `OpenTelemetry.Instrumentation.AspNetCore` 1.19.0 | Infrastructure | Spans das requisições com a convenção HTTP (`http.route`, `http.response.status_code`), filtro das rotas de saúde e mascaramento da query string | Só a `ActivitySource` nativa do ASP.NET Core: sem os atributos da convenção nem o filtro |
| `OpenTelemetry.Instrumentation.SqlClient` 1.19.0 | Infrastructure | Spans das consultas, com o texto parametrizado e sem valores (FR-012, clarify 4) | Instrumentação do EF Core: ainda beta (1.20.1-beta.1) |

Todos estáveis, na série 1.19 (a 1.20.x saiu em 2026-10-15/21, menos de duas semanas antes,
regra já registrada no `Directory.Packages.props`). O registro fica na Infrastructure, que a
API e o Worker já referenciam; o Application e o Domain não ganham pacote.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Estudo (`ddbac59`), spec e clarify com 4 perguntas (`f8d0cdb`), commits `[REQ-16]` |
| II. Contratos versionados | ✅ | Envelope v1, schemas e OpenAPI intocados; o `traceparent` é cabeçalho de transporte, como `x-tentativas`; o teste de contrato e o `contrato-compatibilidade` continuam valendo |
| III. Segurança por padrão | ✅ | `user.id` (nunca e-mail); sem corpo, tokens, cabeçalhos de autorização, query string nem valores de SQL; teste de varredura (SC-006); painel local sem autenticação só em desenvolvimento, documentado |
| IV. Desacoplamento assíncrono | ✅ | O trace atravessa o outbox e o broker sem acoplar os processos; a exportação é em segundo plano e nunca bloqueia (FR-021) |
| V. Qualidade verificável | ✅ | Unitários do amostrador, do interceptor e das opções; integração com banco e broker reais para o trace de ponta a ponta, as tentativas, o reenvio, as rotinas, as métricas, os logs e o destino fora do ar (R-12) |
| RabbitMQ.Client nativo | ✅ | `ActivitySource` nativas da 7.2.2; nenhum pacote de abstração; `RabbitMQ.Client.OpenTelemetry` (rc) rejeitado |
| Endpoint só roteia | ✅ | O middleware só marca atributos do span; nenhum endpoint muda de comportamento |
| Dependências justificadas | ✅ | Quatro pacotes, tabela acima |
| Nomes em português | ✅ | `AdicionarObservabilidade`, `TelemetriaOms`, `AmostradorSemSqlRaiz`, `InterceptorContextoRastreio`, `AmostradorOutbox`, `OpcoesObservabilidade`; atributos e métricas com prefixo `oms.` e termos em português |
| Branch dedicada | ✅ | `feature/req-16-observabilidade-opentelemetry`, a partir da `main` sincronizada depois do merge dos PRs #38 e #39 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/016-observabilidade-opentelemetry/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/telemetria.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
docker-compose.yml                                   # aspire-dashboard (demo, observabilidade); OTEL_* na api e no worker
.env.example                                         # OMS_DASHBOARD_PORTA, OMS_OTLP_PORTA
Directory.Packages.props                             # 4 pacotes, grupo "Observabilidade"

src/
├── PedidoFulfillment.Application/
│   ├── Reconciliacao/PedidoParado.cs                # + ContextoRastreio (string?)
│   └── Pagamentos/IConsultaEstornosPendentes.cs     # devolve o pedido com o contexto
├── PedidoFulfillment.Infrastructure/
│   ├── Observabilidade/                             # NOVO
│   │   ├── ExtensoesObservabilidade.cs              # AdicionarObservabilidade(builder, nomeServico)
│   │   ├── TelemetriaOms.cs                         # ActivitySource, Meter, instrumentos, nomes de atributos
│   │   ├── AmostradorSemSqlRaiz.cs                  # descarta raiz do SqlClient (R-09)
│   │   ├── InterceptorContextoRastreio.cs           # SaveChanges: preenche ContextoRastreio (R-03)
│   │   ├── ContextoRastreio.cs                      # leitura/validação do texto W3C, cabeçalho traceparent
│   │   └── AmostradorOutbox.cs                      # BackgroundService, gauges 1 e 2 (R-07)
│   ├── Configuracao/OpcoesObservabilidade.cs        # NOVO, com validador (taxa 0..1, intervalo)
│   ├── Configuracao/OpcoesReenvioDlq.cs             # + MetricaIntervaloSegundos
│   ├── Mensageria/
│   │   ├── DespachanteEventosSaida.cs               # activity "outbox publicar {tipo}"
│   │   ├── ConsumidorEventos.cs                     # atributos do deliver, erro, métrica 4
│   │   ├── ReenvioDlq.cs, ExecutorReenviosDlq.cs    # spans e links (R-04), métrica 5
│   │   ├── MonitorDlq.cs                            # leitura de 1 min, 5011 a cada 5 min, gauge 3
│   │   └── LimpezaMensageria.cs                     # span por rodada e por alvo, métrica 6
│   ├── Reconciliacao/ReconciliadorPedidos.cs, ConsultaPedidosParados.cs   # spans, links, métrica 5
│   ├── Pagamentos/ProcessadorEstornos.cs, ConsultaEstornosPendentes.cs    # spans, links, métrica 5
│   └── Persistencia/
│       ├── ContextoBancoDados.cs                    # registra o interceptor
│       ├── Configuracoes/ConfiguracaoEventoSaida.cs, ConfiguracaoPedido.cs,
│       │   ConfiguracaoPedidoReenvioDlq.cs          # propriedade de sombra ContextoRastreio
│       └── Migracoes/*_ContextoRastreio.cs
├── PedidoFulfillment.API/
│   ├── Program.cs                                   # AdicionarObservabilidade("oms-api"); middleware
│   ├── Observabilidade/AtributosRequisicao.cs       # NOVO: user.id e oms.pedido.id no span
│   └── appsettings.Development.json                 # OTEL_EXPORTER_OTLP_ENDPOINT comentado (painel local)
└── PedidoFulfillment.Worker/
    ├── ProgramaWorker.cs                            # AdicionarObservabilidade("oms-worker")
    └── appsettings.json                             # seção Observabilidade; ReenvioDlq:MetricaIntervaloSegundos

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   └── Observabilidade/                             # NOVO
│       ├── AmostradorSemSqlRaizTestes.cs, ContextoRastreioTestes.cs
│       └── OpcoesObservabilidadeTestes.cs
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/ColetorTelemetria.cs          # NOVO: processadores de teste e MeterListener
    ├── Infraestrutura/FabricaApi.cs, FabricaWorker.cs   # opção de ligar a telemetria com o coletor
    └── Observabilidade/                             # NOVO
        ├── TraceFluxoPedidoTestes.cs                # US1: ponta a ponta, broker fora, tentativas, DLQ
        ├── TraceReenvioDlqTestes.cs                 # US2
        ├── TraceRotinasTestes.cs                    # US3
        ├── LogsCorrelacionadosTestes.cs             # US4
        ├── MetricasTestes.cs                        # US5
        ├── TelemetriaDesligadaTestes.cs             # US6: desligada, destino fora do ar
        └── DadosSensiveisTestes.cs                  # SC-006

docs/fluxos/                                         # 08, 09, 10, 11 e README (Polish)
```

**Structure Decision**: nenhum projeto novo. Tudo que conhece o SDK fica em
`Infrastructure/Observabilidade`; os serviços existentes só usam `TelemetriaOms` (BCL:
`ActivitySource` e `Meter`). O Application ganha apenas o contexto como texto nos dois
registros de leitura das rotinas, e o Domain não muda.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| A instrumentação do SqlClient 1.19 não gerar spans com o Microsoft.Data.SqlClient 7.1.1 (R-01) | Primeira tarefa da fase de base: teste de integração que exige um span de SQL dentro da requisição. Se falhar, o pacote sai, a spec registra a limitação e o resto segue |
| Ruído de traces do polling (despachante a cada 1 s) | `AmostradorSemSqlRaiz` e nenhum span por ciclo; teste de que um ciclo vazio não gera trace (R-09) |
| `UsePublisherAsParent` mudar numa versão futura do cliente | Teste de ponta a ponta (aceite 1) falha na atualização |
| Testes existentes mudarem de comportamento | Telemetria desligada por padrão nas fábricas; só os testes de `Observabilidade/` a ligam |
| Dois provedores no mesmo processo de teste (API e Worker) | Cada fábrica tem o próprio coletor; asserções filtram pelo trace id do pedido |
| Ouvintes de `ActivitySource` valem para o processo inteiro: um teste com a telemetria ligada em paralelo faria um teste de "desligada" ver contexto e `traceparent` (analyze U1) | Testes de `Observabilidade/` numa coleção `ColecaoObservabilidade` com `DisableParallelization = true`; testes de persistência do contexto usam um `ActivitySource` próprio |
| Atraso das métricas maior que o prometido (analyze I1) | Envio das métricas a cada 30 s por padrão no código; o FR-017 separa leitura e envio, e o contrato documenta o total |
| Destino fora do ar travar a aplicação | Exportação em lote com fila limitada e prazo; teste com endpoint inválido (FR-021) |
| Contexto gravado inválido ou adulterado no cabeçalho | `ActivityContext.TryParse`; inválido é ignorado (FR-013), teste unitário |
| Vazar dado sensível | Atributos definidos em `TelemetriaOms` e `contracts/telemetria.md`; teste de varredura do exportado (SC-006) |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxos 08, 09, 10, 11 e o índice, pela regra do `CLAUDE.md`; a limitação de métricas sai do 11 |
