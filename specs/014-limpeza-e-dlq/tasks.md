---

description: "Lista de tarefas do REQ-14 — Limpeza de dados de mensageria e reprocessamento da DLQ"
---

# Tasks: Limpeza de dados de mensageria e reprocessamento da DLQ

**Input**: Design documents from `/specs/014-limpeza-e-dlq/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/limpeza-e-reenvio.md](./contracts/limpeza-e-reenvio.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras, testes de 401 e 403
nas rotas protegidas e o FR-023 pede testes de integração, com banco e broker reais, da
limpeza, do reenvio, do aviso e das permissões do usuário da API. Os testes de cada fase vêm
antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário, depois de uma fundação com a migration única, a
entidade do pedido de reenvio, a lista de consumidores e as opções. A US1 (limpeza) é o MVP.
A US2 (reenvio) e a US4 (usuário da API) são independentes; a US3 (aviso) usa só a
topologia existente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; regra no Application; Infrastructure com banco, broker, lock e host;
  endpoints só roteiam.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo. Uma migration só (`LimpezaEReenvioDlq`, T006).
- Contratos de eventos v1 inalterados (FR-022): `SchemaEventosTestes` verde sem edição.
  Contrato HTTP v1 só com acréscimos (T018).
- A API não registra nenhum serviço que fale com o broker além da prontidão (FR-019).
- Logs sem dados pessoais: o autor é o identificador do usuário.
- Os testes existentes do REQ-01 ao REQ-13 passam sem afrouxar asserções.
- Testcontainers e `OMS_EXIGIR_DOCKER` (REQ-07); nenhuma espera fixa em teste.
- Commits: `tipo(escopo): descrição [REQ-14]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-14-limpeza-e-dlq` e registrar em Notes a contagem de testes como linha de base (896 no fim do REQ-13: 551 unitários e 345 de integração).

---

## Phase 2: Foundational (Blocking Prerequisites)

### Testes da fundação

- [X] T002 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/PedidoReenvioDlqTestes.cs` (data-model): `Registrar` cria `Pendente` com id v7, autor, consumidor, filtro aparado e `RegistradoEm`; recusa autor vazio, consumidor fora de `ConsumidoresConhecidos.Nomes`, `FalhouAte < FalhouDe`, tipo com mais de 100 e trecho com mais de 200 caracteres (`ArgumentException`); `Iniciar` (`Pendente` ou `EmExecucao` → `EmExecucao`, `IniciadoEm`), `Concluir` (`EmExecucao` → `Concluido`, contagens, `ConcluidoEm`) e `Falhar` (`EmExecucao` → `Falhou`, contagens parciais, motivo aparado até 500); transições fora do ciclo lançam `InvalidOperationException` sem alterar nada; contagens negativas recusadas.
- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesLimpezaTestes.cs` e `OpcoesReenvioDlqTestes.cs` (R-10), como `OpcoesEstornosTestes`: padrões válidos; cada faixa nos limites e fora deles, com a mensagem citando a chave (por exemplo, "Limpeza:RetencaoChavesHoras deve estar entre 24 e 8760."); falhas acumuladas.
- [X] T004 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/ConsumidoresConhecidosTestes.cs` (novo): os nomes dos `IDescritorConsumidor` registrados no Worker são exatamente `ConsumidoresConhecidos.Nomes` (R-03).

### Implementação da fundação

- [X] T005 Application (R-03, data-model):
  - `src/PedidoFulfillment.Application/Mensageria/ConsumidoresConhecidos.cs`: `public static class ConsumidoresConhecidos { public static readonly IReadOnlyList<string> Nomes = ["estoque", "pagamento", "entrega"]; }`, com os consumidores (`ReservaEstoque`, `ProcessamentoPagamento`, `CriacaoOrdemEntrega`) passando a ler o nome dali;
  - `src/PedidoFulfillment.Application/Mensageria/PedidoReenvioDlq.cs`: entidade e `enum SituacaoReenvioDlq { Pendente = 1, EmExecucao, Concluido, Falhou }`, com os métodos do data-model ("`Consumidor` `nvarchar(100)`", "`TipoEvento` `nvarchar(100)?`", "`TrechoMotivo` `nvarchar(200)?`", "`MotivoFalha` `nvarchar(500)?`", contagens "`>= 0`");
  - `src/PedidoFulfillment.Application/Mensageria/FiltroReenvio.cs`: `sealed record FiltroReenvio(string? TipoEvento, DateTimeOffset? FalhouDe, DateTimeOffset? FalhouAte, string? TrechoMotivo)` (o `Atende` entra na US2, T016).

  T002 deve passar.
- [X] T006 Persistência (R-07): `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoPedidoReenvioDlq.cs` (tabela `PedidosReenvioDlq`, tamanhos, check da situação com `RestricoesEnum`, checks das contagens, índice `IX_PedidosReenvioDlq_Situacao_RegistradoEm`); `DbSet` no `ContextoBancoDados`; índices `IX_EventosSaida_Enviados` (filtrado `[EnviadoEm] IS NOT NULL`), `IX_EventosProcessados_ProcessadoEm` e `IX_RegistrosIdempotencia_CriadoEm` nas configurações existentes; migration `LimpezaEReenvioDlq` (`dotnet ef migrations add LimpezaEReenvioDlq --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`), conferindo que só cria a tabela e os índices; a migration nova nas listas de `ModeloSincronizadoTestes.cs` e `MigracaoBancoTestes.cs`; em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/PedidoReenvioDlqPersistenciaTestes.cs` (novo), gravar e reler um pedido em cada situação e conferir que uma situação inválida gravada por fora é recusada pela check constraint.
- [X] T007 Opções e logs (R-10, R-11): `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimpeza.cs` e `OpcoesReenvioDlq.cs` com validadores; `src/PedidoFulfillment.Infrastructure/Mensageria/EventosLimpezaReenvio.cs` com os logs 5009 a 5015; `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaWorker.cs` com `Limpeza:Habilitada=false`, `ReenvioDlq:Habilitado=false` e `ReenvioDlq:AvisoHabilitado=false` por padrão; seções `Limpeza` e `ReenvioDlq` em `src/PedidoFulfillment.Worker/appsettings.json`, com comentário como as demais. T003 e T004 devem passar.

**Checkpoint**: suíte inteira verde. Commit `feat(mensageria): modela o pedido de reenvio, as opções e os indices da limpeza [REQ-14]`.

---

## Phase 3: User Story 1 - Os dados de mensageria e de idempotência não crescem sem limite (Priority: P1) 🎯 MVP

**Goal**: a limpeza apaga, em lotes, o que passou da retenção nos quatro alvos, com uma instância por rodada (FR-001 a FR-007).

**Independent Test**: dados antigos e recentes nos quatro alvos e um evento pendente antigo; uma rodada apaga só os elegíveis e registra o 5013.

### Testes da US1

- [X] T008 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/LimpezaMensageriaTestes.cs`, chamando a rodada diretamente (`ExecutarRodadaAsync`, `internal`, como no `ReconciliadorPedidos`) com `FakeTimeProvider` ou datas gravadas por SQL:
  - eventos enviados com 8 dias e com 6 dias: só o de 8 some; evento pendente com 30 dias fica (US1 cenários 1 e 2);
  - processados com 8 e 6 dias: só o de 8 some (cenário 3);
  - chaves com 49 h e 23 h: só a de 49 h some, e o `POST /api/v1/pedidos` com a chave de 23 h devolve o mesmo pedido (cenário 4);
  - pedidos de reenvio: `Concluido` e `Falhou` com 91 dias somem; `Pendente` e `EmExecucao` com 91 dias ficam;
  - com `TamanhoLote=2` e `LotesPorRodada=1`, 5 elegíveis: a primeira rodada apaga 2, e as seguintes o resto (cenário 5);
  - duas instâncias ao mesmo tempo: uma pula (5015), e a contagem total apagada é a mesma; lock ocupado numa conexão sem pool faz pular, como no REQ-12 (cenário 6);
  - log 5013 com as contagens por alvo (Information); rodada sem nada a apagar em Debug (cenário 7);
  - falha da rodada (consulta que lança) gera 5014 e a rodada seguinte roda.

### Implementação da US1

- [X] T009 [US1] `src/PedidoFulfillment.Infrastructure/Mensageria/LimpezaMensageria.cs` (R-01): `BackgroundService` com `PeriodicTimer`, primeira rodada na partida, `sp_getapplock 'oms-limpeza'` e `sp_releaseapplock`; para cada alvo, até `LotesPorRodada` comandos `ExecuteDeleteAsync` com `OrderBy` da data e `Take(TamanhoLote)`, cada um no seu escopo; condição de cada alvo como no R-01; resumo 5013, falha 5014, lock ocupado 5015. Registro em `AdicionarLimpezaEReenvio()` (`src/PedidoFulfillment.Infrastructure/Mensageria/ExtensoesMensageria.cs`) e chamada no `src/PedidoFulfillment.Worker/ProgramaWorker.cs`. T008 deve passar.

**Checkpoint**: MVP — as tabelas param de crescer. Commit `feat(mensageria): limpa eventos enviados, processados e chaves de idempotencia antigos [REQ-14]`.

---

## Phase 4: User Story 2 - Mensagens da DLQ podem ser reenviadas de forma controlada (Priority: P1)

**Goal**: a API registra o pedido de reenvio (Admin), e o Worker o executa sem perder nem embaralhar mensagens, com filtro e limite (FR-008 a FR-016).

**Independent Test**: mensagens de tipos e motivos diferentes na DLQ de `estoque`; o Admin pede o reenvio de um tipo; o Worker reenvia só esse tipo, com `x-reenvios`, e a consulta mostra `Concluido` com as contagens.

### Testes da US2

- [X] T010 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/FiltroReenvioTestes.cs` (R-05): cada campo sozinho e combinados; tipo por igualdade ordinal; período inclusivo nas pontas; motivo sem diferenciar maiúsculas; mensagem sem `x-falhou-em` não atende a filtro de período, e sem `x-motivo` não atende a filtro de motivo; filtro vazio atende a tudo.
- [X] T011 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/CabecalhosReenvioTestes.cs` (R-06): leitura de `x-reenvios` (ausente = 0; valor em `int`, `long` ou bytes de texto, como o RabbitMQ entrega), `x-motivo` e `x-falhou-em` (bytes UTF-8); montagem da cópia sem `x-tentativas` e `x-ultimo-erro`, com `x-reenvios` + 1, `x-reenviado-em` e `x-pedido-reenvio`, mantendo os demais.
- [X] T012 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/ServicoReenvioDlqTestes.cs`, com um armazenamento falso: registro válido grava `Pendente` com o autor e devolve o DTO; consumidor desconhecido, período invertido e campos longos devolvem `Falha` de validação com o campo, sem gravar; consulta inexistente devolve não encontrado; log 4006.
- [X] T013 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/RotasReenvioDlqTestes.cs`, com a `FabricaApi` e banco real:
  - `POST /api/v1/admin/dlq/estoque/reenvios` como Admin: 202, `Location`, corpo `Pendente` com `autorId` = `sub` do token; linha no banco (US2 cenário 1);
  - `GET` do `Location`: 200 com o mesmo pedido (cenário 3); id inexistente: 404 "Pedido de reenvio não encontrado." (cenário 12); id que não é UUID: 400;
  - sem token: 401; Cliente e OperadorLogistica: 403, nas duas rotas (cenário 9, clarify);
  - consumidor `desconhecido`: 400 com o campo `consumidor`; `falhouAte` antes de `falhouDe`: 400 com o campo `falhouAte` (cenário 10);
  - com a configuração do broker apontando para uma porta fechada, o `POST` continua 202, em menos de 1 s, sem esperar o tempo limite de conexão do broker (cenário 11, FR-008, SC-007).
- [X] T014 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/ExecucaoReenvioDlqTestes.cs`, com o Worker (`ReenvioDlq:Habilitado=true`, intervalo de 1 s, sem os consumidores, salvo onde indicado) e mensagens postas direto em `oms.estoque.dlq` com os cabeçalhos do REQ-06:
  - filtro por tipo: só as do tipo saem para `oms.estoque`, com `x-reenvios = 1`, `x-pedido-reenvio` e sem `x-tentativas`; as demais continuam na DLQ **na mesma ordem** (conferida pelo `MessageId`); pedido `Concluido` com `Reenviadas`, `ForaDoFiltro` e `BloqueadasPeloLimite` (cenários 2, 4 e 6);
  - filtro por período e por trecho do motivo;
  - mensagem com `x-reenvios = 3`: fica na DLQ, contada como bloqueada (cenário 5, clarify Q1);
  - com o consumidor de estoque ligado: um `pedido.criado` reenviado é processado (o pedido reserva o estoque); um evento já processado é descartado como duplicado (5005) (cenário 7);
  - mensagem que falha de novo volta à DLQ com `x-reenvios = 1` (cenário 8);
  - dois pedidos para o mesmo consumidor são executados na ordem de registro; duas instâncias do Worker: um só executa por rodada (5015 no outro);
  - pedido `EmExecucao` gravado antes da subida (Worker que caiu) é retomado e concluído;
  - broker fora do ar antes da execução (porta fechada na configuração do Worker): o pedido continua `Pendente`, com o log 5010, e é concluído quando o broker volta (cenário 11);
  - broker derrubado no meio (canal fechado pelo teste): pedido `Falhou` com contagens parciais, e a soma das mensagens na DLQ e na fila principal é a mesma de antes (nenhuma perdida) (FR-014);
  - log 5009 com o autor e as contagens.
- [X] T015 [P] [US2] Contrato: as duas rotas, `RegistrarReenvioDlqRequestDTO` e `PedidoReenvioDlqResponseDTO` em `specs/001-contratos-api/contracts/openapi-v1.yaml`, com a tag "Administração", 202, 400, 401, 403 e 404; `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs` ganha as duas operações na lista `Operacoes`, e `tests/PedidoFulfillment.Testes.Integracao/Seguranca/AutorizacaoEndpointsTestes.cs` passa a cobri-las.

### Implementação da US2

- [X] T016 [US2] Application: `FiltroReenvio.Atende` (R-05); `src/PedidoFulfillment.Application/Contratos/Administracao/` com `RegistrarReenvioDlqRequestDTO` e `PedidoReenvioDlqResponseDTO` (e a validação de anotações, como os DTOs de pedido); `IArmazenamentoReenvios` (`Adicionar`, `ObterAsync`, `SalvarAsync`); `IServicoReenvioDlq` e `ServicoReenvioDlq` (registro e consulta, com o autor recebido do endpoint); `EventosReenvio` com o 4006. T010 e T012 devem passar.
- [X] T017 [US2] Infrastructure (R-04, R-06): `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoReenvios.cs` (registrado em `AdicionarInfraestrutura`, usado pela API e pelo Worker); `CabecalhosReenvio.cs`; `ReenvioDlq.cs` (execução de um pedido: `BasicGetAsync` sem ack até `MensagensPorPedido`, filtro, limite, publicação em `oms.C` com confirmação e `mandatory`, ack depois da confirmação, `BasicNackAsync(requeue: true)` das guardadas no fim, em ordem); `ExecutorReenviosDlq.cs` (`BackgroundService`, `sp_getapplock 'oms-reenvio-dlq'`, primeiro `EmExecucao`, depois `Pendente` por `RegistradoEm`, `Iniciar`/`Concluir`/`Falhar`, logs 5009, 5010 e 5015), registrado em `AdicionarLimpezaEReenvio()`. T011 e T014 devem passar.
- [X] T018 [US2] API: `PoliticaAdministracao` (só `Admin`) em `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs`; `src/PedidoFulfillment.API/Endpoints/EndpointsAdministracao.cs` com o grupo `/api/v1/admin/dlq`, as duas rotas, a tag e os metadados de OpenAPI como em `EndpointsPedidos`; registro do serviço e dos endpoints no `Program.cs`. O endpoint só lê o `sub` do token e chama o serviço. T013 e T015 devem passar.

**Checkpoint**: commit `feat(mensageria): reenvia a DLQ por pedido do Admin executado pelo Worker [REQ-14]`.

---

## Phase 5: User Story 3 - A operação fica sabendo quando a DLQ cresce (Priority: P2)

**Goal**: aviso em log quando a DLQ de um consumidor passa do limite (FR-020).

**Independent Test**: 11 mensagens em `oms.estoque.dlq` com o limite padrão geram o 5011; 10 não.

### Testes da US3

- [X] T019 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/MonitorDlqTestes.cs`, com o Worker e `ReenvioDlq:AvisoIntervaloSegundos=10` (o mínimo) ou chamando a verificação direto: acima do limite, 5011 com o consumidor, a quantidade e o limite; no limite ou abaixo, nenhum 5011; DLQ inexistente conta como zero; broker com a porta fechada gera 5012, sem derrubar o Worker (US3 cenários 1 a 3).

### Implementação da US3

- [X] T020 [US3] `src/PedidoFulfillment.Infrastructure/Mensageria/MonitorDlq.cs` (R-08): `BackgroundService` que, a cada `AvisoIntervaloSegundos`, faz `QueueDeclarePassiveAsync` de `oms.C.dlq` para cada `ConsumidoresConhecidos.Nomes`, com o 404 do broker tratado como zero; logs 5011 e 5012; registrado em `AdicionarLimpezaEReenvio()` e ligado por `ReenvioDlq:AvisoHabilitado` (chave própria, padrão `true`, desligada na `FabricaWorker` como as demais). T019 deve passar.

**Checkpoint**: commit `feat(mensageria): avisa quando a DLQ passa do limite [REQ-14]`.

---

## Phase 6: User Story 4 - A API tem acesso mínimo ao broker (Priority: P2)

**Goal**: a API usa o usuário `oms-api`, sem permissões; o broker o cria em todos os ambientes (FR-017 a FR-019).

**Independent Test**: com as credenciais da API, a prontidão é saudável e publicar, ler e declarar são recusados.

### Testes da US4

- [X] T021 [P] [US4] Em `tests/PedidoFulfillment.Testes.Integracao/Administracao/PermissoesBrokerApiTestes.cs`, com o contêiner do `MensageriaTeste`: com as credenciais da API, a conexão abre; `ExchangeDeclareAsync`, `QueueDeclareAsync`, `BasicPublishAsync` em `oms.eventos` e `BasicGetAsync` em `oms.estoque` lançam `OperationInterruptedException` com `ACCESS_REFUSED` (403) (US4 cenário 2, SC-008); a prontidão da `FabricaApi` com essas credenciais é `Healthy` (cenário 3); com senha errada, a prontidão mostra o broker `Unhealthy`, sem a senha no corpo, e `POST /api/v1/pedidos` continua 201 (cenário 4). Em `tests/PedidoFulfillment.Testes.Integracao/Arquitetura/DependenciasCamadasTestes.cs`, a API não registra `ConexaoRabbitMQ`, `DespachanteEventosSaida`, consumidores nem os serviços do REQ-14 (FR-019).

### Implementação da US4

- [X] T022 [US4] Broker (R-09):
  - `docker/rabbitmq/iniciar.sh` (LF; `set -euo pipefail`): sobe `rabbitmq-server` em segundo plano, espera `rabbitmq-diagnostics -q ping`, cria `OMS_RABBITMQ_API_USUARIO` se não existir (ou troca a senha se existir), aplica `set_permissions -p / <usuário> "^$" "^$" "^$"` e espera o servidor (`wait`), repassando o sinal de parada;
  - `docker-compose.yml`: o serviço `rabbitmq` monta o script (somente leitura) e o usa como `command`, com as variáveis da API; o serviço `api` (perfil `demo`) passa a usar `RabbitMQ__Usuario: ${OMS_RABBITMQ_API_USUARIO:-oms-api}` e `RabbitMQ__Senha: ${OMS_RABBITMQ_API_SENHA:-oms-api-dev-local}`; o `worker` continua com `OMS_RABBITMQ_USUARIO`;
  - `.env.example`: `OMS_RABBITMQ_API_USUARIO` e `OMS_RABBITMQ_API_SENHA`, com o comentário de que são só de desenvolvimento;
  - `src/PedidoFulfillment.API/appsettings.Development.json`: `RabbitMQ` com `oms-api` / `oms-api-dev-local`;
  - `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MensageriaTeste.cs`: cria `oms-api` no contêiner na inicialização (`ExecAsync` com `rabbitmqctl add_user` e `set_permissions`) e expõe `ConfiguracoesBrokerApi`; a `FabricaApi` (e os testes de saúde da API com broker) passam a usar essas credenciais.

  T021 deve passar; `docker compose up -d --wait` e `docker compose --profile demo up -d --wait --build` sobem com os dois usuários (conferido em Notes), e o job "Demonstração" do CI continua verde.

**Checkpoint**: commit `feat(seguranca): da a API um usuario proprio sem permissoes no broker [REQ-14]`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T023 [P] Desempenho (SC-003): rodar `DesempenhoEntregaTestes` com a limpeza ligada (intervalo curto e dados elegíveis semeados) e registrar em Notes o p95, comparando com o REQ-13 (381 ms do pedido à entrega).
- [X] T024 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo mensagens, códigos, limites e IDs de log no código e os links relativos:
  - `docs/fluxos/08-publicacao-e-consumo-de-eventos.md`: as duas limitações do #30 saem (com o #30 no lugar); cabeçalhos de reenvio; o reenvio volta como entrega nova;
  - fluxo novo `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md` (caminho feliz, rotas de Admin com erros tratados, execução no Worker, ordem e ausência de perda, limite, aviso, configuração, logs, limitações) e a linha no índice `docs/fluxos/README.md`;
  - `docs/fluxos/README.md`: a tabela de falhas externas com a API usando o usuário `oms-api` e a linha "RabbitMQ (API)" atualizada; a prontidão continua mostrando o broker;
  - `docs/fluxos/01-autenticacao.md`: o perfil Admin com as rotas administrativas, se a tabela de perfis citar as rotas.
- [X] T025 [P] Documentação do projeto: `README.md` (Estado atual com o REQ-14; Próximos passos com #29 e #18; configuração `Limpeza` e `ReenvioDlq`; a seção Mensageria sem "a DLQ é reenviada à mão"; ambiente local com os dois usuários do broker e como conferir; roteiros com o quickstart do REQ-14); `specs/006-mensageria/spec.md` › Pendências futuras: limpeza e DLQ apontam para o REQ-14 (resolvido).
- [X] T026 Varredura de segredos (só credenciais de desenvolvimento novas, já documentadas), `dotnet format --verify-no-changes` e a suíte local inteira (zero ignorados); registrar a contagem em Notes.
- [X] T027 Executar o quickstart (§2 a §4) com a API e o Worker locais e registrar em Notes.
- [ ] T028 Fazer push e abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #30`, citando os documentos de fluxo atualizados e a mudança de credenciais do broker. Acompanhar o `build-testes` e o `contrato-compatibilidade` até ficarem verdes, inclusive depois de cada push posterior, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: linha de base antes de tudo.
- **Foundational** (T002 a T007): bloqueia todas as histórias. T005 antes de T006 (a entidade vem antes do mapeamento).
- **US1** (T008, T009): depende da fundação.
- **US2** (T010 a T018): depende da fundação. T016 antes de T017 e T018.
- **US3** (T019, T020): depende só da fundação (opções e logs).
- **US4** (T021, T022): independente das demais; mexe no `MensageriaTeste` e na `FabricaApi`, usados por todos os testes, então roda a suíte inteira no checkpoint.
- **Polish**: depois de tudo. A T028 exige push.

### Parallel Opportunities

- T002, T003 e T004.
- T010 a T015 (arquivos diferentes).
- US1, US3 e US4 entre si, depois da fundação.
- T023, T024 e T025.

---

## Implementation Strategy

### MVP

Setup, fundação e US1: as tabelas de mensageria e de idempotência param de crescer. A US2
troca o painel do broker por um reenvio controlado; a US3 avisa; a US4 reduz o acesso da API.

### Incremental Delivery

1. Fundação → `feat(mensageria): modela o pedido de reenvio, as opções e os indices da limpeza [REQ-14]`.
2. US1 → `feat(mensageria): limpa eventos enviados, processados e chaves de idempotencia antigos [REQ-14]`.
3. US2 → `feat(mensageria): reenvia a DLQ por pedido do Admin executado pelo Worker [REQ-14]`.
4. US3 → `feat(mensageria): avisa quando a DLQ passa do limite [REQ-14]`.
5. US4 → `feat(seguranca): da a API um usuario proprio sem permissoes no broker [REQ-14]`.
6. Polish → `docs(fluxos): ... [REQ-14]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): purga da DLQ, métricas e alertas além dos logs,
  arquivamento dos dados apagados, visibilidade dos cancelamentos (#29).
- O R-04 depende de o RabbitMQ recolocar na posição original as mensagens devolvidas com
  `BasicNack(requeue: true)`; o teste da ordem na T014 prova isso. Se não se confirmar,
  rever a estratégia antes de seguir (registrar em Notes).
- **Analyze (2026-10-24)**: aplicadas as correções I1 (FR-014 e SC-004: nenhuma perda e nenhum
  efeito repetido, com a cópia de uma queda exata descartada como duplicada), I2 (canal aberto
  antes de marcar o pedido; broker fora no início deixa o pedido `Pendente`; caso novo na
  T014), U1 (R-04 vale para filas clássicas), C1 (limpeza dos pedidos de reenvio no FR-002),
  A1 (arquivo da T004), D1 (chave própria `ReenvioDlq:AvisoHabilitado`) e C2 (a T013 mede que
  o `POST` não espera o broker).
- **T001 (linha de base, 2026-10-24)**: 896 testes, 551 unitários e 345 de integração, zero
  ignorados.
- **Fundação e US1 (2026-10-24)**:
  - a tabela de chaves do REQ-05 se chama `ChavesIdempotencia` (não `RegistrosIdempotencia`)
    e já tinha o índice em `CriadoEm`, preparado no REQ-05 para esta limpeza; a migration
    criou só a tabela nova e os outros dois índices;
  - o lock de aplicação dos serviços novos ficou num utilitário, `LockAplicacao`; os serviços
    do REQ-12 e do REQ-13 continuam com as cópias deles;
  - a limpeza para de pedir lotes de um alvo quando um lote vem incompleto;
  - os testes da limpeza chamam a rodada direto, com os dados datados em relação ao instante
    atual; a falha da rodada é provocada renomeando a tabela de processados;
  - fundação e US1 entraram num só commit, porque o registro das opções e o da limpeza ficam
    na mesma extensão (`AdicionarLimpezaEReenvio`);
  - checkpoint: 579 unitários e 354 de integração, todos verdes; `dotnet format` limpo.
- **US2 (2026-10-24)**:
  - **T014, ordem da DLQ (R-04)**: confirmada. Com 6 mensagens e um pedido que examina só as
    3 primeiras, a devolvida voltou antes das não lidas (`m2, m4, m5, m6`), em 4 execuções
    seguidas. A estratégia do `BasicNack(requeue)` vale, para filas clássicas;
  - o despachante do REQ-12 declara a topologia dos consumidores na partida; o teste da falha
    no meio apaga a fila de destino depois de o despachante recriá-la;
  - a `FabricaApi` já aponta o broker para uma porta fechada por padrão, então todos os testes
    das rotas provam que elas não dependem do broker; o `POST` responde em menos de 1 s;
  - a varredura de `AutorizacaoEndpointsTestes` cobre as rotas novas sem edição; a tag do
    OpenAPI ficou `Administracao`, sem acento, como `Autenticacao`;
  - consumidor desconhecido responde 400 com `detail` (validado no serviço); os campos do
    filtro, 400 com `erros` (validados no DTO);
  - checkpoint: 607 unitários e 369 de integração, todos verdes.
- **US4 (2026-10-24)**:
  - o `ping` do RabbitMQ responde antes de a aplicação subir; o script espera com
    `rabbitmqctl await_startup` antes de criar o usuário;
  - a prontidão do broker no compose só fica saudável depois de o usuário da API ter acesso ao
    vhost (`list_user_permissions`), para a API não conectar antes dele existir;
  - conferido localmente: `docker compose up -d --wait` sobre o volume antigo cria `oms-api`
    com `^$ ^$ ^$`; reiniciar o broker é idempotente; `docker compose --profile demo up -d
    --wait --build` sobe a API e o Worker saudáveis, com a API usando `oms-api`;
  - a publicação recusada só aparece na chamada com confirmações: o teste usa um canal com
    confirmações rastreadas;
  - `ConfiguracaoAusenteTestes` conferia o usuário `oms` no `appsettings.Development.json` da
    API; passou a conferir `oms-api`, porque a troca é o objetivo da US4;
  - o `SaudeComDependenciasTestes` tem um contêiner próprio e continua com o usuário de
    administrador; a prontidão com o usuário da API é coberta pelo `PermissoesBrokerApiTestes`;
  - checkpoint: 607 unitários e 375 de integração, todos verdes.
- **T023 (2026-10-25, local)**: p95 do pagamento à entrega de 199 ms e do pedido à entrega de
  382 ms (máximos de 200 ms e 383 ms); no REQ-13 eram 198 ms e 381 ms (SC-003). O teste roda
  com a limpeza desligada, como toda a `FabricaWorker`; a limpeza trabalha em lotes curtos de
  hora em hora, fora do caminho do fluxo, e não foi medida junto.
- **T024**: fluxos 1, 2 (a limitação das chaves saiu, com o #30 no lugar), 8 (as duas
  limitações do #30 saíram) e o índice atualizados; fluxo novo
  `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md` com a linha no índice. Links conferidos.
- **T025**: `README.md` (Estado atual, Próximos passos a partir do REQ-15, Decisões,
  configuração, Mensageria, Ambiente local com os dois usuários e roteiros) e
  `specs/006-mensageria/spec.md` (pendências de limpeza e DLQ resolvidas no REQ-14).
- **T026**: nenhuma credencial nova além das de desenvolvimento documentadas
  (`oms-api-dev-local`, `oms-api-teste-mensageria`); `dotnet format` limpo; suíte inteira com
  607 unitários e 375 de integração, zero ignorados (982 no total; 896 na linha de base). Numa
  rodada, `ConsumoComNovaTentativaTestes.Mensagem_fora_do_contrato_vai_direto_para_a_DLQ`
  (REQ-06, consumidor `verificacao`, que o REQ-14 não toca) falhou uma vez; passou sozinho, em
  duas rodadas da mensageria e em duas suítes inteiras seguintes. Fica anotado como possível
  instabilidade sob carga.
- **T027 (2026-10-25, local)**:
  - §2: o broker do volume antigo ganhou `oms-api` (`^$ ^$ ^$`); a prontidão da API, com ele,
    saudável;
  - §3: OperadorLogistica 403; Admin 202 com `Location`, log 4006; o Worker concluiu em até
    1 min (5009). **Achado**: uma mensagem com o corpo fora do contrato, reenviada, voltava à
    DLQ durante a própria execução e o mesmo pedido a reenviava até o limite (3 reenviadas
    num pedido). Corrigido em `944b60a`: o pedido lê só as mensagens que já estavam na DLQ e
    devolve as que trazem o identificador dele; repetido, o pedido contou 1 reenviada e 1
    bloqueada, e a mensagem voltou com `x-reenvios = 1`. O teste da T014 passou a conferir a
    contagem. O quickstart também citava o motivo errado ("contrato"); o motivo gravado é
    "Envelope sem tipo.";
  - §4: a primeira rodada da limpeza apagou 73 eventos enviados antigos do banco local
    (retenção de 1 h); com 12 mensagens na DLQ, o 5011 apareceu em 10 s;
  - as mensagens de teste foram retiradas da DLQ local, e os contêineres, parados.
- **T028, CI (2026-10-25)**: o primeiro push deixou o job "Demonstração" vermelho: num volume
  novo, o `rabbitmq-diagnostics` do script, como root, criava o cookie do Erlang antes do
  servidor, que então não conseguia lê-lo (`eacces`) e parava. Localmente não aparecia porque o
  volume já tinha o cookie. Corrigido: o script e o healthcheck só chamam as ferramentas depois
  de o servidor criar o cookie. Conferido com `docker compose -p omsteste --profile demo up -d
  --wait --build` em volumes novos (os quatro serviços saudáveis). "Build e testes" e
  "breaking-changes" já estavam verdes.
- **T028, CI (2026-10-25, segundo push)**: "Demonstração" ficou verde, mas "Build e testes"
  falhou em `Com_o_lock_ocupado_a_rodada_e_pulada_e_sem_nada_a_apagar_o_resumo_e_debug` (1 de
  375): o teste soltava o lock fechando a conexão sem pool, e o SQL Server encerra a sessão de
  forma assíncrona; a rodada seguinte ainda pegava o lock ocupado e era pulada. A rotina não
  muda (ela solta com `sp_releaseapplock`, síncrono); o teste agora solta da mesma forma e
  confere que só uma rodada foi pulada. 5 de 5 execuções locais verdes.
