# Implementation Plan: Mensageria, publicação de eventos e DLQ

**Branch**: `feature/req-06-mensageria` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/006-mensageria/spec.md`

**Requisito**: REQ-06 (GitHub issue #7)

## Summary

- **Contratos**: os quatro eventos da issue, versão 1, com envelope comum. Ficam no
  Application, com JSON Schema publicado em `contracts/eventos/` e testes de conformidade
  usando o `JsonSchema.Net`, só nos testes.
- **Outbox**: o `ServicoPedidos` grava `EventosSaida` na mesma operação do pedido. A API não
  fala com o broker.
- **Processo `PedidoFulfillment.Worker`** (novo, SDK Web, só health checks), que hospeda:
  - **Despachante**: publica os pendentes em ordem, com confirmação do broker, espera
    crescente até 10 s e `UPDLOCK/READPAST` para várias instâncias;
  - **Base de consumo**: `ConsumidorEventos<T>`, com:
    - leitura e validação estrutural, e DLQ direta para mensagem ilegível;
    - descarte de duplicados por consumidor, gravado na mesma transação do efeito;
    - 3 novas tentativas por filas de espera com TTL (1 s, 2 s e 4 s), depois DLQ.
- **Topologia**: troca `topic` `oms.eventos` e, por consumidor, `oms.C`,
  `oms.C.espera.{ms}` e `oms.C.dlq`, todas duráveis e declaradas pelo Worker na partida.
- **Entrega**: o Worker entra no perfil `demo` e na verificação do CI. O teste de
  arquitetura passa a ter cinco camadas.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- `RabbitMQ.Client` 7.2.2, que já existia, com publisher confirms e recuperação
  automática;
- EF Core SqlServer 10.0.13;
- novo, só nos testes: `JsonSchema.Net` 9.4.0.

**Storage**: SQL Server; migration `MensageriaOutbox` (`EventosSaida`, `EventosProcessados`)

**Testing**:
- xUnit v3, FluentAssertions e WebApplicationFactory, para a API e para o Worker;
- Testcontainers MsSql e RabbitMq (`PauseAsync` simula o broker fora).

**Target Platform**: API e Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner

**Project Type**: web-service, com um processo de background separado

**Performance Goals**:
- 95% dos eventos publicados em até 5 s (SC-002);
- recuperação em até 30 s depois que o broker volta (SC-003).

**Constraints**:
- RabbitMQ.Client nativo, sem MassTransit nem Wolverine (constituição);
- contrato HTTP v1 inalterado;
- a API não publica no broker (clarify);
- entrega pelo menos uma vez.

**Scale/Scope**:
- 1 projeto novo;
- 4 contratos e 4 schemas;
- 2 tabelas e 1 migration;
- 1 despachante e 1 base de consumo genérica;
- 1 serviço novo no Compose.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada após o specify (2 decisões do usuário) e o clarify (3 perguntas); commit `[REQ-06]` |
| II. Contratos versionados | ✅ | Eventos v1 com schema publicado e regra de evolução (`contracts/topologia-e-consumo.md`). OpenAPI v1 intocado |
| III. Segurança por padrão | ✅ | O Worker expõe só `/health` e `/health/pronto`, que já são públicos pelo REQ-02. Credenciais do broker só por configuração validada. Logs sem dados pessoais (FR-020). Nenhum segredo novo |
| IV. Desacoplamento assíncrono | ✅ | É o objetivo deste REQ: efeitos entre domínios por mensageria, consistentes com a transação (outbox) |
| V. Qualidade verificável | ✅ | Testes de serialização e de conformidade de schema dos 4 contratos (exigência expressa). Integração com broker real para outbox, novas tentativas, DLQ e duplicados |
| RabbitMQ.Client nativo | ✅ | Sem MassTransit, apesar da issue (decisão registrada) |
| Endpoint só roteia | ✅ | Nenhum endpoint de negócio novo. O `ServicoPedidos` só grava o evento |
| Dependências justificadas | ✅ | `JsonSchema.Net` 9.4.0 nos testes (R-12) |
| Nomes em português; eventos no passado | ✅ | `pedido.criado`, `EventosSaida`, `ConsumidorEventos`, `oms.eventos` |
| Branch dedicada | ✅ | `feature/req-06-mensageria`, a partir de `main` sincronizada (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

### Justificativa de dependências

| Pacote | Projeto | Justificativa |
|--------|---------|---------------|
| `JsonSchema.Net` 9.4.0 | Testes.Integracao | Conformidade de schema dos contratos de mensageria, exigida pela constituição (seção Qualidade). Biblioteca MIT, sem Newtonsoft. A 9.4.1 tem menos de duas semanas |

## Project Structure

### Documentation (this feature)

```text
specs/006-mensageria/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── topologia-e-consumo.md
│   └── eventos/{pedido.criado,estoque.reservado,pagamento.aprovado,pagamento.recusado}.v1.schema.json
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Application/
│   ├── Contratos/Eventos/            # NOVO: EnvelopeEvento<T>, 4 eventos, TiposEvento, SerializacaoEventos
│   ├── Mensageria/                   # NOVO: EventoSaida, IManipuladorEvento<T>
│   └── Pedidos/
│       ├── IArmazenamentoPedidos.cs  # Adicionar(pedido, registro, evento)
│       └── ServicoPedidos.cs         # monta PedidoCriado e grava no outbox
├── PedidoFulfillment.Infrastructure/
│   ├── Configuracao/OpcoesMensageria.cs                     # NOVO (+ validador)
│   ├── Mensageria/                                          # NOVO
│   │   ├── ConexaoRabbitMQ.cs, TopologiaMensageria.cs
│   │   ├── DespachanteEventosSaida.cs                       # BackgroundService (Worker)
│   │   ├── ConsumidorEventos.cs, LeitorMensagem.cs, PoliticaNovaTentativa.cs
│   │   ├── EventoProcessado.cs, ExtensoesMensageria.cs
│   ├── Persistencia/Configuracoes/{ConfiguracaoEventoSaida,ConfiguracaoEventoProcessado}.cs
│   ├── Persistencia/ArmazenamentoPedidos.cs                 # adiciona o evento
│   └── Persistencia/Migracoes/                              # + MensageriaOutbox
├── PedidoFulfillment.Worker/                                # NOVO
│   ├── PedidoFulfillment.Worker.csproj, Program.cs
│   ├── appsettings.json, appsettings.Development.json, Properties/launchSettings.json
│   └── Dockerfile
docker-compose.yml                                           # + worker (perfil demo)
.github/workflows/build-testes.yml                           # demonstração confere o Worker
PedidoFulfillment.sln                                        # + Worker

tests/
├── PedidoFulfillment.Testes.Unitarios/Mensageria/           # serialização, leitor, política, evento no ServicoPedidos
└── PedidoFulfillment.Testes.Integracao/
    ├── Mensageria/                                          # schemas, outbox com broker, consumo e DLQ, Worker
    ├── Infraestrutura/FabricaWorker.cs                      # NOVO
    ├── Arquitetura/DependenciasCamadasTestes.cs             # 5 camadas
    └── Persistencia/{ModeloSincronizado,MigracaoBanco}Testes.cs  # 4ª migration
```

**Structure Decision**: o Worker é a quinta camada, de hospedagem, no mesmo nível da API.
Os contratos e a interface de consumo ficam no Application, porque os workers de negócio
escrevem contra eles. A implementação com RabbitMQ fica na Infrastructure, onde o cliente
já é referenciado desde o REQ-02.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Mudança da regra de 4 camadas | O teste de arquitetura é atualizado com a direção `Worker → Application/Infrastructure/Domain` e a proibição de API ↔ Worker |
| Testes temporais (5 s, 30 s, intervalos de nova tentativa) instáveis | Intervalos configuráveis (100, 200 e 400 ms nos testes) e esperas por condição com prazo, não `sleep` fixo |
| Publicação duplicada (queda entre a confirmação e o commit) | Aceita pelo contrato ("pelo menos uma vez") e coberta pelo descarte de duplicados |
| `UPDLOCK/READPAST` em SQL bruto | Teste de integração com dois despachantes simultâneos: nenhum evento perdido |
| Worker subindo antes de a API criar as tabelas | Na demonstração, `depends_on: api: service_healthy`. Localmente, o quickstart manda subir a API antes |
| `multipleOf: 0.01` e precisão de ponto flutuante na validação | O `JsonSchema.Net` usa `decimal`. Um teste com 829,70 cobre o caso |
| Crescimento de `EventosSaida` e `EventosProcessados` | Fora do escopo; registrado em "Pendências futuras" na spec |
