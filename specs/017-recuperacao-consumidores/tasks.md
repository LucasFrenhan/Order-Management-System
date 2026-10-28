---

description: "Lista de tarefas do REQ-17 — Recuperação dos consumidores depois de o broker reiniciar"
---

# Tasks: Recuperação dos consumidores depois de o broker reiniciar

**Input**: Design documents from `/specs/017-recuperacao-consumidores/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/recuperacao-consumo.md](./contracts/recuperacao-consumo.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição (Princípio V), a issue #41 e a spec (FR-017, FR-018)
pedem testes de integração com broker real que reinicia o contêiner. Os testes de cada fase vêm
antes da implementação e devem falhar primeiro; o primeiro deles (T003) também confirma a causa
(FR-001).

**Organization**: por história de usuário, depois de uma fundação com o fixture de queda, a
confirmação da causa, o estado das inscrições e o supervisor. A US1 (reinício do broker) é o
MVP; a US2 (perda sem queda do broker) e a US3 (prontidão, logs e métricas) dependem da fundação
e da troca do consumidor feita na US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Nomes em português; tudo na Infrastructure (`Mensageria/`, `Saude/`, `Observabilidade/`); o
  Application, o Domain e a API não mudam.
- Logs, métricas, nomes da prontidão e mensagens exatamente como em
  `contracts/recuperacao-consumo.md`, com `[LoggerMessage]` e constantes em `TelemetriaOms`.
- IDs de log existentes inalterados; o 5008 continua só na partida.
- Envelope v1, schemas, trocas, filas e argumentos de fila inalterados: `SchemaEventosTestes`,
  `ConsumidoresConhecidosTestes` e os testes de contrato verdes sem edição.
- Nenhum pacote NuGet novo; nenhuma migration.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Testcontainers e `OMS_EXIGIR_DOCKER` (REQ-07); nenhuma espera fixa em teste (esperas por
  condição com `MensageriaTeste.EsperarAteAsync`). Os testes que derrubam o broker ficam numa
  coleção própria, `ColecaoQuedaBroker`, com uma instância própria do `MensageriaTeste` (analyze
  C1): rodam em série entre si e em paralelo com as outras coleções, sem reiniciar o broker
  delas.
- Nada sensível em logs e na prontidão: nunca a senha do broker nem a cadeia de conexão.
- Commits: `tipo(escopo): descrição [REQ-17]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Com o Docker no ar, rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-17-recuperacao-consumidores` e registrar em Notes a contagem de testes como linha de base.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: derrubar o broker nos testes sem trocar a porta, confirmar a causa e ter o estado
das inscrições e o supervisor prontos antes de mexer no consumidor.

### Fixture e confirmação da causa

- [X] T002 Fixture de queda (research R-09) em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MensageriaTeste.cs`:
  - na inicialização, escolher uma porta livre do host (`TcpListener` em `IPAddress.Loopback`, porta 0) e ligar o AMQP do contêiner a ela com `.WithPortBinding(porta, 5672)`; `ConfiguracoesBroker`, `ConectarAsync` e `ConectarComoApiAsync` passam a usar essa porta;
  - `ReiniciarBrokerAsync(TimeSpan? parado = null)`: `StopAsync` do contêiner, espera opcional com ele parado e `StartAsync`, depois espera o AMQP aceitar conexão (até 60 s);
  - `PararBrokerAsync()` e `IniciarBrokerAsync()` separados, para os testes que observam o meio da queda;
  - `RecriarBrokerAsync()`: `rabbitmqctl stop_app`, `reset`, `start_app`, espera o AMQP e recria o usuário da API (`CriarUsuarioApiAsync`); o usuário `oms` volta pelo usuário padrão do contêiner (se não voltar, criá-lo com `add_user` e `set_permissions ".*" ".*" ".*"`);
  - `FecharConexoesAsync()`: `rabbitmqctl close_all_connections "teste REQ-17"`;
  - `ConsumidoresNaFilaAsync(string fila)`: `ConsumerCount` da fila, ou nulo se ela não existe;
  - corrigir o comentário solto "Simula o broker fora do ar sem trocar a porta mapeada" sobre `ConfiguracoesBrokerApi` (ele é de `PausarBrokerAsync`).
  - `TrocarSenhaAsync(string senha)`: `rabbitmqctl change_password oms <senha>` (analyze G1);
  - `ColecaoQuedaBroker` (`[CollectionDefinition(nameof(ColecaoQuedaBroker))]`, `ICollectionFixture<MensageriaTeste>`) num arquivo novo `tests/PedidoFulfillment.Testes.Integracao/Mensageria/ColecaoQuedaBroker.cs`.
  Rodar os testes da `ColecaoMensageria` e da `ColecaoObservabilidade` para conferir que nada quebrou.
- [X] T003 Confirmação da causa (FR-001, research R-01), **antes de qualquer correção**: em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/RecuperacaoConsumoTestes.cs` (nova, `[Collection(nameof(ColecaoQuedaBroker))]`), escrever `Os_tres_consumidores_voltam_depois_de_o_broker_reiniciar`: Worker (`FabricaWorker`) no ar, esperar os três consumidores (`ConsumidorLigadoAsync` para `estoque`, `pagamento` e `entrega`), `ReiniciarBrokerAsync(TimeSpan.FromSeconds(10))`, esperar até 60 s cada fila principal com `ConsumidoresNaFilaAsync == 1`. Rodar com o código da `main` e confirmar que **falha** (0 consumidores). Com logs temporários (não commitados) em `ConexaoRabbitMQ` para `ConnectionShutdownAsync`, `RecoverySucceededAsync`, `ConnectionRecoveryErrorAsync` e o `DisposeAsync` de `ObterAsync` (com quem chamou), registrar a sequência observada em `specs/017-recuperacao-consumidores/research.md`, R-01, numa subseção "Observado (2026-MM-DD)". **Se a sequência contrariar a análise, parar e revisar o plano antes de seguir.**

### Estado e supervisor

- [X] T004 [P] `src/PedidoFulfillment.Infrastructure/Mensageria/EstadoConsumidores.cs` (data-model, "Inscrição de consumidor"): singleton com uma entrada por consumidor de `IEnumerable<IDescritorConsumidor>`, começando com `Inscrito = false`; `MarcarInscrito(consumidor)` (zera `SemInscricaoDesde`), `MarcarSemInscricao(consumidor, instante)` (só grava o instante na primeira perda), `TodosInscritos`, `SemInscricaoDesde(consumidor)`; cada mudança espelhada em `EstadoMetricas.RegistrarInscricao(consumidor, inscrito)` (novo, em `src/PedidoFulfillment.Infrastructure/Observabilidade/EstadoMetricas.cs`, dicionário como o da DLQ). Registrar com `TryAddSingleton` em `AdicionarMensageria` (`src/PedidoFulfillment.Infrastructure/Mensageria/ExtensoesMensageria.cs`).
- [X] T005 [P] `src/PedidoFulfillment.Infrastructure/Mensageria/SessaoConsumo.cs` (data-model, "Sessão de consumo" e "Motivo da perda"): `SessaoConsumo` (`IAsyncDisposable`) com `CanalConsumo`, `CanalPublicacao` e `Task<MotivoPerda> Perdida`; `record MotivoPerda(string Origem, string Motivo)` com as origens `canal` e `cancelamento`; um `TaskCompletionSource<MotivoPerda>` (`RunContinuationsAsynchronously`, `TrySetResult`) completado por `ChannelShutdownAsync` de qualquer dos dois canais (motivo `"{ReplyCode} {ReplyText}"`) e por `UnregisteredAsync` do consumidor quando não foi a própria sessão que cancelou (motivo `cancelada pelo broker`); `DisposeAsync` fecha os dois canais com `CloseAsync(200, "sessao encerrada", abort: true)` e prazo de 2 s, sem relançar (como `DespachanteEventosSaida.DescartarCanalAsync`).
- [X] T006 [P] Testes unitários do supervisor (research R-10) em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SupervisorInscricaoTestes.cs`, com sessões falsas (uma `Task` de perda controlada pelo teste) e uma espera falsa que anota cada intervalo pedido e retorna na hora:
  - abre a sessão, marca inscrito e espera a perda;
  - perda por `canal` e por `cancelamento`: fecha a sessão, marca sem inscrição, abre outra na hora (intervalo 0) e conta uma reinscrição;
  - perda sinalizada duas vezes na mesma sessão: uma reinscrição só, nunca duas sessões abertas ao mesmo tempo;
  - abrir falha cinco vezes seguidas: intervalos 1, 2, 4, 8 e 10 s (e 10 s depois); dar certo zera a sequência;
  - na partida, a primeira tentativa sai na hora, falhas usam a mesma sequência e não contam reinscrição;
  - cancelar o token durante a espera ou durante uma sessão aberta: sai do laço sem abrir outra sessão e fecha a aberta.
  Devem falhar (o tipo ainda não existe).
- [X] T007 `src/PedidoFulfillment.Infrastructure/Mensageria/SupervisorInscricao.cs` (research R-02 a R-04): `internal sealed class SupervisorInscricao(string consumidor, Func<CancellationToken, Task<SessaoConsumo>> abrir, Func<TimeSpan, CancellationToken, Task> esperar, EstadoConsumidores estado, TimeProvider relogio, ILogger logger)` com `ExecutarAsync(CancellationToken)`: laço abrir → marcar inscrito → `await Perdida` (ou cancelamento) → marcar sem inscrição → `DisposeAsync` da sessão → próxima tentativa; intervalos `[0, 1, 2, 4, 8, 10]` s, ficando em 10 s, zerados depois de uma abertura bem-sucedida; para a abstração de teste, `SessaoConsumo` pode ser trocada por uma interface `ISessaoConsumo` (`Task<MotivoPerda> Perdida`, `IAsyncDisposable`). O supervisor recebe o logger e é quem registra as falhas ao abrir: o 5008 (`FalhaInicioConsumidor`, movido de `ConsumidorEventos` com o mesmo ID, nome, nível e mensagem) antes da primeira inscrição, e o 5017 depois de uma perda (analyze I1). Deixa os pontos dos outros logs e das métricas marcados com `// US3` (entram na T023 e na T024). T006 passa.

**Checkpoint**: fundação pronta; o teste da T003 continua falhando até a US1.

---

## Phase 3: User Story 1 - O consumo volta sozinho depois de o broker reiniciar (Priority: P1) 🎯 MVP

**Goal**: depois de o broker parar e voltar, os três consumidores voltam em até 1 min, sem
reiniciar o Worker, sem mensagem perdida nem efeito repetido.

**Independent Test**: o teste da T003 e os da T008 a T010 passam; o quickstart §2 termina sem
`docker compose restart worker`.

### Testes da US1

- [X] T008 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/RecuperacaoConsumoTestes.cs`, acrescentar (US1, cenários 1, 2, 4, 5 e 6):
  - `Pedido_criado_com_o_broker_fora_segue_quando_ele_volta`: `PararBrokerAsync`, gravar pedido e `pedido.criado` no outbox (como os testes de `Estoque/`), `IniciarBrokerAsync`, esperar até 90 s o pedido em `ProntoParaEnvio` com a ordem de entrega criada;
  - `Consumo_volta_depois_de_queda_longa`: broker parado 30 s, mais que o necessário para a espera chegar ao teto de 10 s (analyze C1; os "vários minutos" do cenário 4 ficam no quickstart §2); os três consumidores voltam em até 60 s contados de `IniciarBrokerAsync`;
  - `Quedas_seguidas_deixam_um_unico_consumidor_por_fila`: três `ReiniciarBrokerAsync` com 2 s entre eles; ao fim, `ConsumidoresNaFilaAsync == 1` nas três filas por 5 s seguidos;
  - `Broker_sem_dados_tem_as_filas_recriadas_antes_da_inscricao`: `RecriarBrokerAsync`; em até 60 s, as filas principal, de espera (100, 200 e 400 ms nos testes) e DLQ dos três consumidores existem, e cada principal tem 1 consumidor.
- [X] T009 [P] [US1] Efeito único (FR-008, FR-009, SC-003) em `RecuperacaoConsumoTestes.cs`, `Mensagem_em_processamento_na_queda_nao_repete_o_efeito`: um decorator da `ReservaEstoque` real registrado por `FabricaWorker.Servicos` (padrão de `ServicosFalsos.cs`) que, na primeira chamada, executa a reserva real e segura **antes de retornar** num `TaskCompletionSource` até o teste liberar; o efeito só é confirmado na transação do `ConsumidorEventos` depois do retorno (analyze U1). O teste para o broker com a chamada presa, libera (o commit acontece com o canal já fechado, e o ack falha), inicia o broker e confere: a reentrega é descartada como duplicada (log 5005), há exatamente uma reserva para o pedido e um único `estoque.reservado` no outbox.
- [X] T010 [P] [US1] Outros componentes (FR-011) em `RecuperacaoConsumoTestes.cs`, `Despachante_monitor_e_reenvio_funcionam_depois_da_queda`: com `ReenvioDlq:Habilitado=true` e o aviso ligado (`Configuracoes`), reiniciar o broker; depois, um evento novo do outbox é publicado e marcado como enviado, o `MonitorDlq` volta a ler: com `ReenvioDlq:AvisoLimite=1`, `ReenvioDlq:AvisoIntervaloSegundos=10` (os mínimos das opções) e duas mensagens na DLQ de estoque (`CenarioReenvio.NaDlqAsync`), um 5011 novo aparece depois da volta (analyze A2) e um pedido de reenvio (padrão de `Administracao/CenarioReenvio.cs`) termina com sucesso, com a mensagem de volta na fila principal.

### Implementação da US1

- [X] T011 [US1] `src/PedidoFulfillment.Infrastructure/Mensageria/ConexaoRabbitMQ.cs` (research R-02): `AutomaticRecoveryEnabled = false` e `TopologyRecoveryEnabled = false`, com comentário citando o REQ-17 e o motivo (o processo é dono da recuperação; descartar a conexão fechada passa a ser correto); atualizar o `<summary>` (REQ-06 R-09 substituído em parte por REQ-17 R-02).
- [X] T012 [US1] `src/PedidoFulfillment.Infrastructure/Mensageria/ConsumidorEventos.cs` (research R-03, R-05):
  - `ExecuteAsync` lê consumidor e tipos como hoje e roda `SupervisorInscricao.ExecutarAsync(stoppingToken)` com `abrir` = o atual `IniciarAsync`, que agora devolve uma `SessaoConsumo` (canais, `DeclararConsumidorAsync`, `BasicQosAsync`, `BasicConsumeAsync`) em vez de gravar campos; `esperar` = `Task.Delay`;
  - remover os campos `canalConsumo` e `canalPublicacao`; `ProcessarAsync` e `PublicarAsync` recebem a sessão que entregou; o ack usa `sessao.CanalConsumo`;
  - ack ou republicação em canal fechado (`AlreadyClosedException`, `OperationInterruptedException`): sai do callback sem relançar e sem ack (o broker devolve a mensagem);
  - `StopAsync` deixa o fechamento para o supervisor (o cancelamento do `stoppingToken` fecha a sessão aberta);
  - na partida, a falha ao abrir continua registrando o 5008.
  O teste da T003 e os da T008 a T010 passam.

**Checkpoint**: o MVP resolve a issue #41.

---

## Phase 4: User Story 2 - Um consumidor que perde a inscrição por outro motivo também volta (Priority: P2)

**Goal**: fila apagada, inscrição cancelada pelo broker, canal fechado ou conexão fechada à
força: o consumidor volta sozinho em até 1 min, sem afetar os outros.

**Independent Test**: os testes da T013 a T015 passam; quickstart §3 e §4.

### Testes da US2

- [X] T013 [P] [US2] `Fila_apagada_e_recriada_e_o_consumidor_volta` em `RecuperacaoConsumoTestes.cs` (US2, cenário 1): com os três inscritos, `ApagarFilasConsumidorAsync("estoque", soPrincipal: true)`; em até 60 s, `oms.estoque` existe com 1 consumidor e um `pedido.criado` publicado nela é processado; `oms.pagamento` e `oms.entrega` mantêm 1 consumidor o tempo todo (amostrar durante a espera). Com `CapturaLogs`: um 5016 com origem `cancelamento` só para `estoque`, e um 5018 para ele.
- [X] T014 [P] [US2] `Conexao_fechada_a_forca_tem_os_tres_consumidores_de_volta` em `RecuperacaoConsumoTestes.cs` (US2, cenário 2): `FecharConexoesAsync`; em até 60 s, as três filas com 1 consumidor.
- [X] T015 [P] [US2] `Encerrar_o_worker_durante_a_recuperacao_nao_trava` em `RecuperacaoConsumoTestes.cs` (US2, cenário 3; edge case de encerramento): `PararBrokerAsync`, esperar o log 5016 dos três, descartar a `FabricaWorker` e conferir que termina em até 10 s, sem nenhum 5018 depois do pedido de encerramento; `IniciarBrokerAsync` no `finally`.

### Implementação da US2

- [X] T016 [US2] Conferir e completar em `SessaoConsumo.cs` e `SupervisorInscricao.cs` (analyze U2):
  - o `UnregisteredAsync` vindo do broker (fila apagada) completa a perda com origem `cancelamento` mesmo com o canal aberto, e o `DisposeAsync` da sessão fecha esse canal;
  - o `UnregisteredAsync` causado pela própria sessão (cancelamento no `DisposeAsync`) é ignorado;
  - o `ChannelShutdownAsync` do canal de publicação também completa a perda;
  - com o token cancelado, nenhuma sessão nova é aberta, e a espera em curso termina na hora;
  - o canal fechado sozinho (sem comando no broker, research R-09) fica coberto pelos testes da T006.
  T013 a T015 passam.

**Checkpoint**: a limitação "fila apagada com o Worker no ar" do fluxo 08 está resolvida.

---

## Phase 5: User Story 3 - A prontidão e os logs mostram o consumo parado e a volta (Priority: P2)

**Goal**: prontidão do Worker não pronta na hora em que algum consumidor perde a inscrição;
logs 5016 a 5019; métricas 7 e 8 com a telemetria ligada.

**Independent Test**: os testes da T017 a T020 passam; quickstart §2 (passos 3, 5, 8 e 10) e §5.

### Testes da US3

- [X] T017 [P] [US3] Testes unitários de `VerificacaoConsumidores` em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/VerificacaoConsumidoresTestes.cs`: `Healthy` com todos inscritos; `Unhealthy` com um sem inscrição e antes da primeira inscrição; nenhum resultado traz `Description`, `Exception` nem `Data`.
- [X] T018 [P] [US3] Prontidão e logs numa queda só (US3, cenários 1 a 4; SC-004; FR-015, FR-016; analyze C1) em `RecuperacaoConsumoTestes.cs`, `Queda_e_volta_aparecem_na_prontidao_e_nos_logs_sem_credenciais`, com `CapturaLogs`: pronta (200) com `"consumidores": "Healthy"`; `PararBrokerAsync` → em até 15 s, 503 com `"consumidores": "Unhealthy"`, e toda leitura seguinte até a volta é 503; `IniciarBrokerAsync` → 200 em até 60 s. Nos logs: 5019 (`ConexaoBrokerFechada`), um 5016 por consumidor com origem `canal`, pelo menos um 5017 e um 5018 por consumidor com `DuracaoMs > 0`, níveis como no contrato. Nem a resposta da prontidão nem os logs contêm a senha do broker; a resposta também não tem o host nem nomes de fila. Em `tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs`, acrescentar que a prontidão da API não tem a entrada `consumidores` (FR-014).
- [X] T019 [P] [US3] Credenciais recusadas na volta (spec, Edge Cases; analyze G1) em `RecuperacaoConsumoTestes.cs`, `Credenciais_recusadas_mantem_o_worker_nao_pronto_ate_voltarem`, com `CapturaLogs`: com os três inscritos, `TrocarSenhaAsync("outra-senha-req17")` e `FecharConexoesAsync`; por 15 s a prontidão responde 503 com `"consumidores": "Unhealthy"` e há 5017; nenhum log contém nenhuma das duas senhas; `TrocarSenhaAsync` de volta para a original (também no `finally`) → os três voltam em até 60 s.
- [X] T020 [P] [US3] Métricas (FR-016a) em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/MetricasTestes.cs`: com a telemetria ligada, `oms.mensageria.consumidor.inscrito` = 1 para os três; depois de apagar `oms.estoque` e o consumidor voltar, o gauge de `estoque` passou por 0 e voltou a 1, e `oms.mensageria.consumidor.reinscricoes{oms.consumidor=estoque}` = 1 (a inscrição da partida não conta); nomes, unidades (`{inscricao}`, `{reinscricao}`) e atributo `oms.consumidor` como no contrato. Em `TelemetriaDesligadaTestes.cs`, conferir que as duas não são publicadas com a telemetria desligada.

### Implementação da US3

- [X] T021 [US3] `src/PedidoFulfillment.Infrastructure/Saude/VerificacaoConsumidores.cs` (research R-06): `IHealthCheck` que lê `EstadoConsumidores.TodosInscritos` e devolve `Healthy()` ou `Unhealthy()` sem descrição; registrar em `AdicionarMensageria` (`ExtensoesMensageria.cs`) com `services.AddHealthChecks().AddCheck<VerificacaoConsumidores>("consumidores", tags: [ExtensoesInfraestrutura.TagPronto])`, só no Worker.
- [X] T022 [US3] Ajustar `tests/PedidoFulfillment.Testes.Integracao/Mensageria/WorkerTestes.cs`: `Worker_fica_pronto_e_declara_a_troca_de_eventos` espera até 30 s a prontidão responder 200 (research R-06), em vez de ler uma vez. Procurar com `grep -rn "health/pronto" tests` outros testes do Worker que leiam a prontidão uma vez só e ajustá-los do mesmo jeito.
- [X] T023 [US3] Logs em `SupervisorInscricao.cs` (5016 `InscricaoPerdida`, 5017 `FalhaReinscricao`, 5018 `ConsumidorReinscrito` com `DuracaoMs` de `SemInscricaoDesde`; o 5008 já está lá desde a T007) e em `ConexaoRabbitMQ.cs` (5019 `ConexaoBrokerFechada`, assinando `ConnectionShutdownAsync` na criação; não registra quando `Initiator == ShutdownInitiator.Application`), com nomes, níveis e mensagens exatamente como em `contracts/recuperacao-consumo.md`, seção Logs; `ConexaoRabbitMQ` passa a receber `ILogger<ConexaoRabbitMQ>`.
- [X] T024 [US3] Métricas 7 e 8 em `src/PedidoFulfillment.Infrastructure/Observabilidade/TelemetriaOms.cs` (contrato, seção Métricas): `ConsumidorInscrito` (`ObservableGauge<long>`, `oms.mensageria.consumidor.inscrito`, `{inscricao}`, lido de `EstadoMetricas`, com `oms.consumidor`) e `Reinscricoes` (`Counter<long>`, `oms.mensageria.consumidor.reinscricoes`, `{reinscricao}`); o supervisor soma 1 em `Reinscricoes` a cada abertura bem-sucedida depois de uma perda.

**Checkpoint**: todas as histórias funcionando.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T025 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo IDs de log, nomes, intervalos e links relativos no código:
  - `docs/fluxos/08-publicacao-e-consumo-de-eventos.md`: em "Falhas externas", linhas para o broker que cai e volta com o Worker no ar (perda, intervalos 0, 1, 2, 4, 8 e 10 s, volta em até 1 min), a conexão fechada à força, o broker sem dados e as credenciais recusadas; a linha "Broker fora do ar na partida" mantém o 5008; a linha "Fila de um consumidor apagada com o Worker no ar" passa a dizer que o consumidor se inscreve de novo, citando a issue #41; seção de consumo com o supervisor e o ack pela sessão que entregou; prontidão com a entrada `consumidores`; logs 5016 a 5019; a recuperação automática desligada;
  - `docs/fluxos/12-rastreabilidade-e-metricas.md`: métricas 7 e 8;
  - `docs/fluxos/README.md`: a linha "RabbitMQ (Worker)" da tabela de dependências externas passa a dizer que os consumidores voltam sozinhos também com o Worker no ar e que a prontidão do Worker mostra os consumidores parados (analyze G2); o diagrama de estados do pedido não muda.
- [X] T026 [P] `README.md`: o trecho de `/health/pronto` (hoje "verifica banco e broker… com o estado de `sqlserver` e `rabbitmq`") ganha a entrada `consumidores`, só no Worker (analyze G2); linha do REQ-17 na tabela de requisitos (com o link da spec), "Estado atual" com o REQ-17, numeração do próximo REQ a partir do REQ-18 e a tabela de próximos passos só com a #18; roteiros com o quickstart do REQ-17.
- [X] T027 [P] Desempenho (SC-006): rodar `tests/PedidoFulfillment.Testes.Integracao/Entregas/DesempenhoEntregaTestes.cs` e registrar em Notes o p95, comparando com o da `main` na T001; não pode piorar mais de 5%.
- [X] T028 Varredura de segredos nos arquivos alterados, `dotnet format --verify-no-changes` e a suíte local inteira com `OMS_EXIGIR_DOCKER=1` (zero ignorados); registrar a contagem em Notes.
- [X] T029 Executar o quickstart (§2 a §5) com `docker compose --profile demo` e registrar em Notes (consumidores por fila antes, durante e depois; prontidão; logs; métricas no painel).
- [ ] T030 Fazer push e abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #41`, citando a causa confirmada, a recuperação automática do cliente desligada, a entrada nova na prontidão (e o efeito no healthcheck do compose), os logs e as métricas novos, e os documentos de fluxo atualizados. Acompanhar o `build-testes` e o `contrato-compatibilidade` até ficarem verdes, inclusive depois de cada push posterior, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: T001 antes de tudo.
- **Foundational** (T002 a T007): bloqueia todas as histórias. T002 antes de T003; T003 antes de qualquer mudança em `src/` (FR-001); T004 e T005 antes de T007; T006 antes de T007.
- **US1** (T008 a T012): depende da fundação. T011 e T012 depois dos testes T008 a T010.
- **US2** (T013 a T016): depende da US1 (o consumidor já usa o supervisor).
- **US3** (T017 a T024): depende da US1; T021 antes de T022; T023 e T024 depois da T007 e da T012.
- **Polish**: depois de tudo. A T030 exige push.

### Parallel Opportunities

- T004, T005 e T006.
- T008, T009 e T010 (mesmo arquivo de testes: escrever em sequência, mas são independentes entre si).
- US2 e US3 entre si, depois da US1.
- T017 a T020.
- T025, T026 e T027.

### Exemplo: fundação

```text
T002 fixture → T003 causa confirmada (teste falhando)
T004 EstadoConsumidores ┐
T005 SessaoConsumo      ├ em paralelo
T006 testes do supervisor┘
T007 SupervisorInscricao
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: o consumo volta sozinho depois de o broker reiniciar, que é a issue #41.
A US2 cobre as outras formas de perder a inscrição e resolve a limitação do fluxo 08; a US3 dá
visibilidade (prontidão, logs e métricas).

### Incremental Delivery

1. Fundação → `test(mensageria): reproduz a perda dos consumidores no reinicio do broker e cria o supervisor de inscricao [REQ-17]`.
2. US1 → `fix(mensageria): consumidores voltam a se inscrever depois de o broker reiniciar [REQ-17]`.
3. US2 → `fix(mensageria): consumidor volta depois de fila apagada ou conexao fechada a forca [REQ-17]`.
4. US3 → `feat(mensageria): mostra os consumidores parados na prontidao, nos logs e nas metricas [REQ-17]`.
5. Polish → `docs(fluxos): ... [REQ-17]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- A T003 precisa do Docker no ar; ela é o portão da correção (FR-001).
- **Analyze (2026-10-28)**: aplicadas as correções C1 (coleção `ColecaoQuedaBroker` própria,
  queda longa de 30 s, prontidão e logs numa queda só na T018), I1 (o supervisor registra o 5008
  na partida e o 5017 depois de uma perda), G1 (credenciais recusadas na T019), G2 (seção de
  saúde do README e linha do índice dos fluxos), U1 (decorator da `ReservaEstoque` na T009), U2
  (itens da T016), A1 (FR-016 reescrito na spec), A2 (5011 com o limite mínimo na T010) e T1
  (FR-013 com "todos os consumidores registrados").
  - G3: a contagem de tentativas preservada quando a republicação falha fica coberta pelo
    desenho (a original não é confirmada e volta com os cabeçalhos de antes) e pela revisão da
    T012; um teste de integração exigiria derrubar o canal entre o processamento e a
    republicação, sem gancho no broker.
  - G4: o pedido de reenvio em execução na queda segue o tratamento de erro do broker do REQ-14
    (`ExecucaoReenvioDlqTestes`), sem teste novo.
- Fora do escopo (spec, Assumptions): conexões separadas para publicar e consumir, reinício
  automático do Worker por prontidão, alertas.
- **T001 (linha de base, 2026-10-28)**: na `main`, numa worktree limpa, 1.104 testes verdes (683
  unitários e 421 de integração), zero ignorados. Uma primeira rodada teve 2 falhas
  intermitentes em `TraceRotinasTestes` (REQ-16), que não se repetiram.
- **T003 (causa, 2026-10-28)**: confirmada; a sequência está em `research.md`, R-01,
  "Observado". Detalhe: o defeito precisa de alguém pedindo a conexão durante a queda; sem
  pedido nem monitor da DLQ, a recuperação do cliente bastava. Por isso o teste de reprodução
  cria um pedido com o broker fora. Ele falhou na `main` (0 consumidores em 60 s) e passa com a
  correção. Os logs temporários de diagnóstico não foram commitados.
- **T006**: a espera falsa, que não cede, prendia o contexto do xUnit em laços de falhas
  seguidas; o supervisor roda nos testes com `Task.Run`, como um `BackgroundService`. Em
  produção não há laço apertado: depois de uma falha, a espera mínima é de 1 s.
- **T010**: as opções do aviso têm mínimos (`AvisoLimite` ≥ 1, `AvisoIntervaloSegundos` ≥ 10);
  o teste usa duas mensagens na DLQ e o intervalo de 10 s. O despachante depois da queda é
  coberto pelo teste de reprodução (o pedido chega a `ProntoParaEnvio`).
- **T020**: o 0 do gauge `oms.mensageria.consumidor.inscrito` dura milissegundos quando a fila
  é apagada (a volta é imediata) e ficou com o teste unitário
  `Estado_espelha_a_inscricao_para_a_metrica_7`; o teste de integração confere o 1 antes e
  depois, as unidades e o contador. A conferência em `TelemetriaDesligadaTestes` não foi
  acrescentada: as duas métricas seguem as outras seis (BCL, sem ouvinte não publicam nada), e
  esse teste não confere métricas.
- **T028 (suíte, 2026-10-28)**: `dotnet format --verify-no-changes` limpo; 696 unitários
  verdes. Integração: 432 de 433 verdes (6 min 22 s); a falha foi
  `TraceRotinasTestes.Reconciliacao_liga_o_passo...` (REQ-16), a mesma da primeira linha de base
  da `main`, e se repetiu em 2 de 3 rodadas isoladas. Causa: o coletor só guarda o span quando
  ele termina, e o teste lia o span do ciclo (`coletor.Span`) logo depois do passo, antes de o
  ciclo terminar. Corrigido só no teste, esperando o ciclo com `EsperarSpanAsync` (e o mesmo na
  rodada de estornos); depois, 8 rodadas isoladas da classe, todas verdes. Nenhum segredo novo
  nos arquivos alterados (só as senhas de teste do `MensageriaTeste` e a senha falsa
  `outra-senha-req17`).
- **T029 (quickstart, 2026-10-28)**, com `docker compose --profile demo up -d --wait --build`:
  - §2: antes, 1 consumidor em cada fila principal e a prontidão do Worker `Healthy` com
    `consumidores`; com o broker parado, 503 com `consumidores: Unhealthy`; ~10 s depois de
    `docker compose start rabbitmq`, os três de volta e a prontidão `Healthy`, sem reiniciar o
    Worker (`RestartCount=0`). Um pedido criado depois chegou a `ProntoParaEnvio` em 3 s. Nos
    logs: 5019, um 5016 (`canal`) e 5017 por consumidor, e 5018 com ~33 s sem inscrição;
    nenhuma ocorrência da senha.
  - §3: `delete_queue oms.estoque`: em 3 s, a fila existia de novo com 1 consumidor, e o 5016
    saiu com `(cancelamento)` só para o estoque.
  - §4: `close_all_connections`: em 5 s, os três de volta (7 logs 5018 no total: 3 + 1 + 3).
  - §5 (métricas no painel do Aspire): não conferido visualmente; as métricas 7 e 8 têm teste
    de integração (`MetricasTestes`).
  A demo foi derrubada com `docker compose --profile demo down`, sem apagar os volumes.
- **T027 (desempenho)**: duas rodadas de `DesempenhoEntregaTestes` na branch: p95 do pagamento à
  entrega 198 a 200 ms e do pedido à entrega 382 ms nas duas, igual ao REQ-16 (382 a 386 ms).
  Sem piora.
