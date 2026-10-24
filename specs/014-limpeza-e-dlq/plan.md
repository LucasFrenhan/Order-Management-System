# Implementation Plan: Limpeza de dados de mensageria e reprocessamento da DLQ

**Branch**: `feature/req-14-limpeza-e-dlq` | **Date**: 2026-10-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/014-limpeza-e-dlq/spec.md`

**Requisito**: REQ-14 (GitHub issue #30)

## Summary

- **Limpeza** (US1): `LimpezaMensageria`, serviço novo no Worker, a cada 1 h, com uma
  instância por rodada (`sp_getapplock 'oms-limpeza'`). Apaga em lotes curtos (`DELETE TOP`)
  os eventos enviados com mais de 7 dias, os processados com mais de 7 dias, as chaves de
  idempotência com mais de 48 h e os pedidos de reenvio finais com mais de 90 dias, com índices
  novos para cada condição. Nunca toca em pendentes (R-01, R-02).
- **Reenvio da DLQ** (US2): a API só registra o pedido. `POST /api/v1/admin/dlq/{consumidor}/reenvios`
  responde 202, e `GET /api/v1/admin/dlq/reenvios/{id}` consulta o resultado, as duas só para
  Admin. O `ExecutorReenviosDlq` do Worker executa um pedido de cada vez: lê a cabeça da DLQ
  sem ack, republica as selecionadas com confirmação e só então dá ack, e devolve as outras à
  posição original (R-03 a R-06).
- **Aviso da DLQ** (US3): `MonitorDlq` confere o tamanho de cada DLQ a cada 5 min (R-08).
- **Usuário próprio da API no broker** (US4): o RabbitMQ sobe por um script que cria `oms-api`
  com permissões `^$`; a API passa a usá-lo, e um teste prova o `ACCESS_REFUSED` (R-09).
- **Persistência**: migration `LimpezaEReenvioDlq`, com a tabela `PedidosReenvioDlq` e três
  índices (R-07).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: EF Core SqlServer 10.0.13 (`ExecuteDeleteAsync`), `Microsoft.Data.SqlClient`
  (lock de aplicação), `RabbitMQ.Client` 7.2.2 (`BasicGetAsync`, `BasicNackAsync`,
  `QueueDeclarePassiveAsync`, confirmações);
- nos testes: `Testcontainers.RabbitMq` (já em uso; `ExecAsync` para criar o usuário da API);
- nenhum pacote novo.

**Storage**: SQL Server; tabela nova `PedidosReenvioDlq` e índices em `EventosSaida`,
`EventosProcessados` e `RegistrosIdempotencia` (migration `LimpezaEReenvioDlq`).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker), Testcontainers
MsSql e RabbitMq

**Target Platform**: API e Worker em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- fluxo normal dentro da meta do REQ-10 (SC-003): lotes curtos, índices na data;
- pedido de reenvio executado em até 1 min (SC-007); a rota de registro só grava no banco.

**Constraints**:
- a API não fala com o broker além da prontidão, e com um usuário sem permissões;
- nenhuma mensagem perdida pelo reenvio; ordem das não selecionadas preservada;
- nunca apagar pendente, chave com menos de 24 h ou pedido de reenvio em aberto;
- contratos de eventos v1 inalterados; HTTP v1 só com acréscimos.

**Scale/Scope**:
- 3 serviços em segundo plano no Worker (limpeza, executor de reenvios, monitor da DLQ);
- 1 entidade, 1 serviço e 1 porta no Application; 2 rotas e 1 política na API;
- 2 seções de opções; 1 migration; 1 script do broker e mudanças no compose, no `.env.example`
  e nas configurações da API.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify (reenvio por rota Admin, revisto para execução no Worker e usuário próprio da API) e o clarify (3 perguntas), commits `d540d00`, `2a6b294` e `dca6360` `[REQ-14]` |
| II. Contratos versionados | ✅ | Eventos v1 intocados; os cabeçalhos novos são de transporte. HTTP v1 só ganha as duas rotas administrativas e os schemas delas, compatíveis para o oasdiff |
| III. Segurança por padrão | ✅ | Rotas com política só de Admin e testes de 401 e 403; a API passa a ter o mínimo de permissão no broker (`^$`), com teste de `ACCESS_REFUSED`; autor pelo `sub`, logs sem dados pessoais; credenciais só de desenvolvimento, configuráveis |
| IV. Desacoplamento assíncrono | ✅ | O reenvio é um pedido gravado pela API e executado pelo Worker; a API continua sem publicar nem consumir |
| V. Qualidade verificável | ✅ | Unitários do filtro, da entidade, do serviço e das opções; integração com banco e broker reais para limpeza, rotas, execução, aviso e permissões (R-12) |
| RabbitMQ.Client nativo | ✅ | `BasicGet`, `BasicNack`, confirmações e `QueueDeclarePassive` do cliente nativo; nenhum Shovel nem MassTransit |
| Endpoint só roteia | ✅ | Os endpoints chamam o `ServicoReenvioDlq`; a gravação fica na porta do Infrastructure |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `LimpezaMensageria`, `ExecutorReenviosDlq`, `MonitorDlq`, `PedidoReenvioDlq`, `FiltroReenvio` |
| Branch dedicada | ✅ | `feature/req-14-limpeza-e-dlq`, a partir da `main` sincronizada depois do merge dos PRs #34 e #35 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/014-limpeza-e-dlq/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/limpeza-e-reenvio.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
docker/rabbitmq/iniciar.sh                           # NOVO: sobe o broker e cria oms-api (R-09)
docker-compose.yml                                   # rabbitmq pelo script; api com OMS_RABBITMQ_API_*
.env.example                                         # OMS_RABBITMQ_API_USUARIO e _SENHA

src/
├── PedidoFulfillment.Application/
│   ├── Mensageria/
│   │   ├── ConsumidoresConhecidos.cs                # NOVO: estoque, pagamento, entrega
│   │   ├── PedidoReenvioDlq.cs                      # NOVO: entidade e situação
│   │   ├── FiltroReenvio.cs                         # NOVO
│   │   ├── IArmazenamentoReenvios.cs                # NOVO
│   │   ├── IServicoReenvioDlq.cs, ServicoReenvioDlq.cs  # NOVO: registro e consulta
│   │   └── EventosReenvio.cs                        # NOVO: log 4006
│   └── Contratos/Administracao/                     # NOVO: DTOs de requisição e resposta
├── PedidoFulfillment.Infrastructure/
│   ├── Configuracao/OpcoesLimpeza.cs, OpcoesReenvioDlq.cs   # NOVO, com validadores
│   ├── Mensageria/
│   │   ├── LimpezaMensageria.cs                     # NOVO: BackgroundService, sp_getapplock
│   │   ├── ExecutorReenviosDlq.cs                   # NOVO: BackgroundService, sp_getapplock
│   │   ├── ReenvioDlq.cs                            # NOVO: execução de um pedido no broker (R-04)
│   │   ├── CabecalhosReenvio.cs                     # NOVO: leitura e montagem (R-06)
│   │   ├── MonitorDlq.cs                            # NOVO
│   │   ├── EventosLimpezaReenvio.cs                 # NOVO: logs 5009-5015
│   │   └── ExtensoesMensageria.cs                   # AdicionarLimpezaEReenvio
│   └── Persistencia/
│       ├── ArmazenamentoReenvios.cs                 # NOVO
│       ├── Configuracoes/ConfiguracaoPedidoReenvioDlq.cs     # NOVO
│       ├── Configuracoes/ConfiguracaoEventoSaida.cs, ConfiguracaoEventoProcessado.cs,
│       │   ConfiguracaoRegistroIdempotencia.cs      # índices da limpeza
│       └── Migracoes/*_LimpezaEReenvioDlq.cs
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsAdministracao.cs          # NOVO: as duas rotas
│   ├── Seguranca/PoliticasAutorizacao.cs            # PoliticaAdministracao
│   ├── Program.cs                                   # registra o serviço e os endpoints
│   └── appsettings.Development.json                 # RabbitMQ com oms-api
└── PedidoFulfillment.Worker/
    ├── ProgramaWorker.cs                            # AdicionarLimpezaEReenvio
    └── appsettings.json                             # seções Limpeza e ReenvioDlq

specs/001-contratos-api/contracts/openapi-v1.yaml    # as duas rotas e os schemas

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Mensageria/FiltroReenvioTestes.cs, PedidoReenvioDlqTestes.cs, CabecalhosReenvioTestes.cs
│   ├── Mensageria/ServicoReenvioDlqTestes.cs
│   └── Configuracao/OpcoesLimpezaTestes.cs, OpcoesReenvioDlqTestes.cs
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/MensageriaTeste.cs            # cria oms-api no contêiner
    ├── Infraestrutura/FabricaWorker.cs              # Limpeza e ReenvioDlq desligados por padrão
    ├── Infraestrutura/FabricaApi.cs                 # credenciais da API no broker
    └── Administracao/                               # NOVO
        ├── LimpezaMensageriaTestes.cs
        ├── RotasReenvioDlqTestes.cs
        ├── ExecucaoReenvioDlqTestes.cs
        ├── MonitorDlqTestes.cs
        └── PermissoesBrokerApiTestes.cs
```

**Structure Decision**: nenhum projeto novo. O pedido de reenvio, o filtro e o serviço da
API ficam no Application; tudo que fala com o banco em lote, com o lock ou com o broker fica
no Infrastructure, e só o Worker registra esses serviços.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| A limpeza bloquear a publicação ou o consumo | Lotes de 1 000 em transações curtas, índices na data, nunca nas linhas que eles usam (R-01) |
| Reenvio perder mensagens | Ack na DLQ só depois da confirmação da publicação; canal caído devolve as sem ack (R-04) |
| Reenvio duplicar mensagens | Só na queda exata entre a confirmação e o ack; o consumidor descarta pelo registro de processado (R-04) |
| Ordem da DLQ mudar | `BasicNack(requeue)` recoloca na posição original (R-04); teste da ordem |
| Muitas mensagens sem ack num canal | Limite de 1 000 por pedido; o canal é só do executor |
| Ambiente local antigo sem `oms-api` | O script cria o usuário a cada subida, de forma idempotente (R-09) |
| Testes da API quebrarem com o usuário novo | O `MensageriaTeste` cria `oms-api` no contêiner; a `FabricaApi` passa a usá-lo; testes de saúde existentes conferidos |
| Consumidor novo fora da lista aceita pela API | Teste que compara `ConsumidoresConhecidos.Nomes` com os descritores do Worker (R-03) |
| Testes existentes com dados antigos apagados pela limpeza | `FabricaWorker` com `Limpeza:Habilitada=false` e `ReenvioDlq:Habilitado=false` por padrão (R-10) |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxo 8 (limitações resolvidas), fluxo 1 ou 3 (rotas de Admin), fluxo novo de limpeza e reenvio e o índice, pela regra do `CLAUDE.md` |
