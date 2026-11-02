---

description: "Lista de tarefas do REQ-19 — Gateway de pagamento por HTTP com API simulada"
---

# Tasks: Gateway de pagamento por HTTP com API simulada

**Input**: Design documents from `/specs/019-gateway-pagamento-http/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/api-pagamentos-simulada.md](./contracts/api-pagamentos-simulada.md),
[contracts/cliente-gateway-pagamento.md](./contracts/cliente-gateway-pagamento.md), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras e testes de
integração com dependências reais; o FR-024 lista os cenários de integração (com a API
simulada real, banco e broker) e os unitários (classificação, política de tentativas e API
simulada). Os testes de cada fase vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário, depois de uma fundação que entrega o núcleo da API
simulada (autenticação, memória idempotente, decisor de comportamento e rota de controle), as
exceções e a porta no Application, e a migração do gateway em memória para os testes. A US1
(pagamento pela rede) é o MVP. A US2 (comportamento realista da API) depende só da fundação.
A US3 (classificação e novas tentativas) depende da US1 e da US2. A US4 (resultado incerto) e a
US5 (circuito e pausa) dependem da US3. A US6 (observabilidade) depende da US1 e fecha depois
da US5 (métrica do circuito).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US6)

## Convenções válidas para todas as tarefas

- Nomes em português; endpoints só roteiam; regras no Application; HTTP de saída, resiliência,
  base de consumo e supervisor no Infrastructure (plan, Structure Decision). O domínio não muda.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Um pacote novo**: `Microsoft.Extensions.Http.Resilience` (R-06, aprovado pelo usuário),
  numa versão 10.x com pelo menos duas semanas de publicada, fixada em
  `Directory.Packages.props`. Nenhum outro pacote. **Nenhuma migration.**
- Contratos v1 (HTTP do OMS e eventos) inalterados (FR-023): o `openapi-v1.yaml` e os schemas de
  evento não mudam; o identificador de transação continua texto opaco (`PAG-…`, `EST-…`).
- A API simulada (`src/PedidoFulfillment.PagamentosSimulados`) **não** referencia nenhum projeto
  do OMS e **não** exporta telemetria (só lê o `traceparent` para o escopo de log, FR-020).
- Decisões confirmadas pelo usuário: a chamada ao gateway **continua dentro da transação** do
  consumidor, limitada pelo tempo total (R-10); com o circuito aberto, o consumo de pagamento
  **pausa** (R-09, FR-016a).
- A `ChaveApi`, a `ChaveControle`, o identificador de transação e o corpo das chamadas nunca
  aparecem em log, trace, métrica ou mensagem de exceção (FR-002, FR-021). Erros de opção
  nomeiam a chave sem o valor (FR-018).
- Logs com `LoggerMessage`: OMS 7013–7020 e 5020–5021; API simulada 11001–11006 (R-12). IDs
  existentes não mudam.
- Os testes existentes do REQ-01 ao REQ-18 passam sem afrouxar asserções; os que dependiam do
  `GatewayPagamentoSimulado` passam a usar o `GatewayPagamentoMemoria` (R-11).
- Testes com banco e broker reais usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER` (REQ-07).
  A API simulada sobe nos testes em Kestrel, em modo determinístico, com tempos curtos (R-13:
  tentativa 300 ms, total 1500 ms, espera base 20 ms, circuito mínimo 4 em janela de 5 s com
  pausa de 1 s, `SemRespostaMs` 2000). Nenhuma espera fixa em teste: espere por condição, com
  prazo. Todo teste de integração do REQ-19 confere `efetivacoes <= 1` pela rota de controle
  (SC-002).
- Commits: `tipo(escopo): descrição [REQ-19]`, um por fase.

---

## Phase 1: Setup

**Purpose**: projeto novo, pacote e referências.

- [X] T001 Criar o projeto `src/PedidoFulfillment.PagamentosSimulados/PedidoFulfillment.PagamentosSimulados.csproj` (SDK Web, .NET 10, sem referência ao OMS), adicioná-lo à `PedidoFulfillment.sln`, referenciá-lo em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj` e `tests/PedidoFulfillment.Testes.Unitarios/PedidoFulfillment.Testes.Unitarios.csproj`, e adicionar `Microsoft.Extensions.Http.Resilience` (versão fixada, R-06) em `Directory.Packages.props` e em `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`; `dotnet build` verde.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: núcleo da API simulada (sem latência nem falhas ainda), porta e exceções no
Application, opções do cliente e gateway em memória só nos testes.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T002 [P] Testes unitários das opções da API simulada em `tests/PedidoFulfillment.Testes.Unitarios/PagamentosSimulados/OpcoesPagamentosSimuladosTestes.cs`: `Seguranca:ChaveApi` e `Seguranca:ChaveControle` obrigatórias, "≥ 16 caracteres, diferentes"; faixas da tabela de configuração do [contrato da API](./contracts/api-pagamentos-simulada.md#configuração) (latências "0–60000, mínimo ≤ máximo", percentuais "0–100", `RetryAfterSegundos` "1–60", `SemRespostaMs` "100–120000", "a soma das chances de falha não pode passar de 100"); a mensagem de erro nomeia a chave sem o valor.
- [X] T003 [P] Testes unitários do armazenamento em `tests/PedidoFulfillment.Testes.Unitarios/PagamentosSimulados/ArmazenamentoOperacoesTestes.cs`: `TryAdd` atômico por (`Tipo`, `Chave`); repetição com o mesmo valor devolve a operação guardada e soma `Chamadas`; valor diferente é `chave-reutilizada`; operação `EmAndamento` é `operacao-em-andamento`; falha que não efetivou remove a chave só se a operação foi criada naquela chamada e ainda está `EmAndamento`; falha sobre uma operação concluída não a remove nem a altera (só soma `Chamadas`); `Efetivacoes` "0 ou 1"; chamadas concorrentes com a mesma chave efetivam uma única vez.
- [X] T004 [P] Testes unitários do decisor em `tests/PedidoFulfillment.Testes.Unitarios/PagamentosSimulados/DecisorComportamentoTestes.cs`: ordem roteiro → modo forçado → sorteio (R-03); o roteiro consome um item por chamada; a decisão vem antes da consulta à memória (também na repetição de uma chave concluída, R-03); `restaurar` volta à configuração; no estorno, o sorteio nunca devolve `Recusar` (a recusa vem só das regras do estorno); com `Semente`, o sorteio é reproduzível.
- [X] T005 [P] Testes unitários da autenticação em `tests/PedidoFulfillment.Testes.Unitarios/PagamentosSimulados/AutenticacaoChavesTestes.cs`: chave ausente, errada e de outro grupo de rotas dão o mesmo `401` com o título `"Credencial inválida."`; `/health` sem chave; a comparação usa `CryptographicOperations.FixedTimeEquals`.
- [X] T006 [P] Testes unitários das opções do cliente em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesGatewayPagamentoTestes.cs` (reescrever): `UrlBase` obrigatória "URL absoluta `http`/`https`", `ChaveApi` "≥ 16 caracteres", `TempoTentativaMs` "100–30000", `Tentativas` "1–6", `EsperaBaseMs` "10–10000", `TempoTotalMs` "> `TempoTentativaMs`, ≤ 60000", `Circuito:ProporcaoFalhas` "0.1–1.0", `Circuito:MinimoChamadas` "2–100", `Circuito:JanelaSegundos` e `Circuito:PausaSegundos` "1–600"; a chave `TaxaAprovacaoPercentual` não existe mais; o erro nomeia a chave sem o valor.

### Implementação da fundação

- [X] T007 [P] Opções da API simulada em `src/PedidoFulfillment.PagamentosSimulados/Simulacao/OpcoesSimulacao.cs` e `src/PedidoFulfillment.PagamentosSimulados/Simulacao/OpcoesSeguranca.cs`, com validadores e `ValidateOnStart`, conforme T002.
- [X] T008 [P] Memória idempotente em `src/PedidoFulfillment.PagamentosSimulados/Operacoes/OperacaoPagamento.cs` e `src/PedidoFulfillment.PagamentosSimulados/Operacoes/ArmazenamentoOperacoes.cs` (`ConcurrentDictionary`, campos e transições do [data-model](./data-model.md#operacaopagamento): `Tipo` `Cobranca`|`Estorno`, `Chave` "única por `Tipo`", `Valor` "> 0, até 2 casas", `Situacao` `EmAndamento`|`Aprovada`|`Recusada`|`Confirmado`|`Recusado`, `Identificador` "`PAG-<guid v7>` ou `EST-<guid v7>`; nulo na recusa", `Efetivacoes` "0 ou 1", `Chamadas`), conforme T003.
- [X] T009 [P] Comportamento e decisor em `src/PedidoFulfillment.PagamentosSimulados/Simulacao/Comportamento.cs` (`Aprovar`, `Recusar`, `Indisponivel`, `LimiteTaxa`, `ErroInterno`, `SemResposta`, `ProcessarSemResponder`, com os nomes em kebab-case do contrato), `src/PedidoFulfillment.PagamentosSimulados/Simulacao/DecisorComportamento.cs` e `src/PedidoFulfillment.PagamentosSimulados/Simulacao/ControleSimulacao.cs` (chances em vigor, `ModoForcado?`, roteiro em `ConcurrentQueue<Comportamento>`, `Restaurar`), conforme T004. Nesta fase, os comportamentos de falha só existem no decisor; a execução deles vem na US2.
- [X] T010 [P] Autenticação em `src/PedidoFulfillment.PagamentosSimulados/Seguranca/AutenticacaoChaves.cs` (`X-Api-Key` em `/v1/*`, `X-Chave-Controle` em `/controle/*`, antes de qualquer comportamento simulado), conforme T005.
- [X] T011 Serviço e rotas em `src/PedidoFulfillment.PagamentosSimulados/Operacoes/ServicoPagamentosSimulados.cs` e `src/PedidoFulfillment.PagamentosSimulados/ProgramaPagamentos.cs` (`Main` explícito, `ProblemDetails` com `codigo`, JSON camelCase): `POST /v1/cobrancas` e `POST /v1/estornos` com `Idempotency-Key` GUID (`400` com `errors` para chave, valor ausente, ≤ 0 ou com mais de 2 casas), `201` na primeira vez, `200` + `Idempotent-Replayed: true` na repetição, `409` `chave-reutilizada` e `operacao-em-andamento` (+ `Retry-After: 1`); estorno de cobrança desconhecida ou de valor diferente é `201` `recusado` com os motivos `"Cobrança desconhecida."` e `"Valor diferente do cobrado."`; `GET /v1/cobrancas/{chave}` e `GET /v1/estornos/{chave}` (`200`, `404` `nao-encontrada`, `409`); rotas `/controle/*` da tabela do contrato (`GET/PUT /controle/comportamento`, `PUT/DELETE /controle/modo`, `POST/DELETE /controle/roteiro`, `POST /controle/restaurar`, `GET /controle/operacoes/{tipo}/{chave}`); `GET /health`. Depende de T007 a T010.
- [X] T012 [P] Configuração da API simulada em `src/PedidoFulfillment.PagamentosSimulados/appsettings.json` e `appsettings.Development.json` (padrões do contrato, chaves de desenvolvimento só no `Development`) e `src/PedidoFulfillment.PagamentosSimulados/Dockerfile` no padrão dos Dockerfiles da API e do Worker.
- [X] T013 [P] Exceções e porta no Application: `src/PedidoFulfillment.Application/Mensageria/DependenciaIndisponivelException.cs` (`DisponivelEm`), `src/PedidoFulfillment.Application/Pagamentos/GatewayPagamentoIndisponivelException.cs` (herda dela), `src/PedidoFulfillment.Application/Pagamentos/FalhaGatewayPagamentoException.cs` (`Tipo`: `TipoFalhaGateway.Passageira`|`Permanente`, `Operacao`; mensagem sem chave, identificador ou corpo), e `ConsultaCobranca` (`NaoEncontrada`, `Aprovada(IdentificadorTransacao)`, `Recusada(Motivo)`) mais `ConsultarCobrancaAsync(Guid pedidoId, CancellationToken)` em `src/PedidoFulfillment.Application/Pagamentos/IGatewayPagamento.cs`.
- [X] T014 Opções do cliente em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesGatewayPagamento.cs` (seção `GatewayPagamento` do [contrato do cliente](./contracts/cliente-gateway-pagamento.md#configuração-gatewaypagamento), validador e `ValidateOnStart`), conforme T006; tirar `TaxaAprovacaoPercentual` de `src/PedidoFulfillment.Worker/appsettings.json`.
- [X] T015 Mover o gateway em memória para os testes (R-11, FR-009a): `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoSimulado.cs` vira `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/GatewayPagamentoMemoria.cs` (chave `GatewayPagamentoMemoria:TaxaAprovacaoPercentual`, implementando também `ConsultarCobrancaAsync`); `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/GatewayPagamentoSimuladoTestes.cs` é movido para `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/GatewayPagamentoMemoriaTestes.cs` (o projeto unitário não referencia o de integração); `FabricaWorker.cs` o registra por padrão no lugar do `IGatewayPagamento` e ganha a opção `ApiPagamentos` para os testes HTTP; ajustar `CenarioEntrega.cs`, `CenarioPagamento.cs`, `ReconciliacaoEtapasTestes.cs`, `WorkerTestes.cs` e `ProcessamentoEstornosTestes.cs` para a chave nova. A suíte existente continua verde.
- [X] T016 Infraestrutura de teste em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaPagamentos.cs`: `WebApplicationFactory<ProgramaPagamentos>` com `UseKestrel` em porta local livre, `Simulacao:Deterministico` = `true`, chaves de teste, e um cliente de controle (`Roteiro(...)`, `Modo(...)`, `Restaurar()`, `Operacao(tipo, chave)`); testes de integração diretos da API em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/ApiPagamentosSimuladaTestes.cs` (`401` uniforme, `/health`, aprovação, recusa, repetição `200` + `Idempotent-Replayed`, `chave-reutilizada`, consulta `200`/`404`, estorno confirmado e recusado pelas duas regras, rota de controle sem a chave de controle recusada e sem efeito).

**Checkpoint**: build verde; a API simulada aprova, recusa, guarda e devolve pela chave; a suíte
existente passa com o `GatewayPagamentoMemoria`. Commit
`feat(pagamentos): api simulada idempotente e gateway em memoria nos testes [REQ-19]`.

---

## Phase 3: User Story 1 - O pagamento passa por um serviço externo, pela rede (Priority: P1) 🎯 MVP

**Goal**: o Worker cobra, estorna e cobra pela reconciliação chamando a API simulada por HTTP,
com o comportamento de negócio de hoje.

**Independent Test**: com o compose no ar, criar um pedido e ver `Processando` com `PAG-…`;
forçar `recusar` e ver `PagamentoRejeitado` com o estoque devolvido; cancelar um pedido pago e
ver o pagamento estornado com `EST-…` (quickstart §1–§3).

### Testes da US1

- [X] T017 [P] [US1] Testes unitários da leitura das respostas em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ClassificacaoRespostaGatewayTestes.cs` (primeira parte): `201`/`200` `aprovada` → `ResultadoCobranca.Aprovada(identificador)`; `recusada` → `Recusada(motivo)`; estorno `confirmado`/`recusado` → `ResultadoEstorno`; consulta `200`/`404` → `ConsultaCobranca`; `401`, `403`, `400` e corpo fora do contrato → `FalhaGatewayPagamentoException(Permanente)`.
- [X] T018 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/PagamentoHttpTestes.cs`, com Worker + `FabricaPagamentos`: aprovação (pedido `Processando`, `pagamento.aprovado` com `PAG-…`, a API recebeu o valor do pedido e a chave do pedido); recusa por roteiro (`PagamentoRejeitado`, estoque devolvido); reconciliação de pedido parado na etapa de pagamento cobrando pela API com a mesma chave; credencial errada no Worker (nada gravado, log 7015, caminho de falha da base). `efetivacoes <= 1` em todos.
- [X] T019 [P] [US1] Teste de integração do estorno pela API em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/EstornoHttpTestes.cs`: pedido pago e cancelado → `pagamento.estornado` com `EST-…`, `efetivacoes` do estorno = 1.

### Implementação da US1

- [X] T020 [US1] `src/PedidoFulfillment.Infrastructure/Pagamentos/ClassificacaoRespostaGateway.cs` (leitura dos corpos e classificação de resultado e permanente, conforme T017) e `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoHttp.cs` (`CobrarAsync`, `EstornarAsync`, `ConsultarCobrancaAsync` sobre `HttpClient` tipado, `X-Api-Key`, `Idempotency-Key: <pedidoId>`, corpos do [contrato do cliente](./contracts/cliente-gateway-pagamento.md#requisição)).
- [X] T021 [US1] `AdicionarGatewayPagamento` em `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs`: `AddHttpClient<IGatewayPagamento, GatewayPagamentoHttp>` com `BaseAddress` = `UrlBase` (sem pipeline de resiliência ainda) e registro em `src/PedidoFulfillment.Worker/ProgramaWorker.cs`; `GatewayPagamento:UrlBase` e `ChaveApi` em `src/PedidoFulfillment.Worker/appsettings.Development.json` (chave de desenvolvimento) e ausentes do `appsettings.json` (o Worker não sobe sem elas, FR-018).
- [X] T022 [US1] Ambiente: serviço `pagamentos` em `docker-compose.yml` (fora de perfil, porta 5232, healthcheck em `/health`, `Seguranca__ChaveApi` e `Seguranca__ChaveControle` de `OMS_PAGAMENTOS_CHAVE_API` e `OMS_PAGAMENTOS_CHAVE_CONTROLE` com padrões de desenvolvimento); Worker com `GatewayPagamento__UrlBase=http://pagamentos:8080`, `GatewayPagamento__ChaveApi` e `depends_on` saudável; variáveis em `.env.example`; job de demo em `.github/workflows/build-testes.yml` esperando a API simulada saudável.

**Checkpoint**: MVP. O pagamento passa pela rede com o comportamento de hoje. Commit
`feat(pagamentos): cobranca, estorno e reconciliacao pela api simulada [REQ-19]`.

---

## Phase 4: User Story 2 - A API simulada se comporta como um serviço real (Priority: P1)

**Goal**: latência sorteada com cauda lenta, falhas sorteadas ou forçadas e mudança do
comportamento com a API no ar.

**Independent Test**: chamar a API diretamente várias vezes e ver tempos variados e cada tipo
de resposta; forçar cada comportamento pela rota de controle (quickstart §2, §4–§6 do lado da
API).

### Testes da US2

- [X] T023 [P] [US2] Testes unitários do sorteio em `tests/PedidoFulfillment.Testes.Unitarios/PagamentosSimulados/SorteioComportamentoTestes.cs`: com `Semente` e 10 000 sorteios, as proporções ficam perto das chances (aprovação 90% sobre as que efetivam, 2/1/1/1/1% de falhas); latência dentro de 40–250 ms fora da cauda e 1000–5000 ms na cauda de 5%; `Deterministico` = sem latência artificial na partida, mas a latência definida por `PUT /controle/comportamento` vale até o `restaurar` (R-03).
- [X] T024 [P] [US2] Testes de integração da API em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/ComportamentosApiTestes.cs`: `indisponivel` → `503` `codigo: "indisponivel"`, chave livre; `limite-taxa` → `429` + `Retry-After`; `erro-interno` → `500`; `sem-resposta` → nada antes de `SemRespostaMs`, depois `504`, chave livre; `processar-sem-responder` → efetiva, responde o `201` só depois de `SemRespostaMs`, e a repetição devolve `200` + `Idempotent-Replayed`; a consulta sofre só a latência (sem falha sorteada), definida por `PUT /controle/comportamento` no modo determinístico; falha forçada sobre uma chave concluída responde a falha e a operação continua igual em `GET /controle/operacoes` (só `chamadas` cresce); `PUT /controle/comportamento` inválido → `400`; `PUT/DELETE /controle/modo` e `POST /controle/restaurar` mudam e desfazem sem reiniciar.

### Implementação da US2

- [X] T025 [US2] Execução dos comportamentos em `src/PedidoFulfillment.PagamentosSimulados/Operacoes/ServicoPagamentosSimulados.cs`, com a decisão antes da consulta à memória e sem remover operação concluída (contrato da API, R-03): latência sorteada (também no modo determinístico quando definida pela rota de controle) (`Task.Delay` com o `RequestAborted`), `503`/`429` + `Retry-After: <RetryAfterSegundos>`/`500` liberando a chave, `sem-resposta` (espera `SemRespostaMs`, cancelada quando o cliente desconecta, depois `504`, sem processar), `processar-sem-responder` (efetiva e guarda antes da espera); sorteio em `DecisorComportamento.cs` com `Random` semeado quando há `Semente`.
- [X] T026 [US2] `PUT /controle/comportamento` validando como na partida, e logs em `src/PedidoFulfillment.PagamentosSimulados/Logs.cs`: 11001 `OperacaoProcessada`, 11002 `FalhaSimulada`, 11003 `RepeticaoIdempotente`, 11004 `ChaveReutilizada`, 11005 `ComportamentoAlterado` (sem chaves), 11006 `CredencialRecusada` (rota, sem o valor), com o `traceparent` recebido no escopo; nenhum exportador de telemetria no projeto.

**Checkpoint**: a API simulada reproduz as falhas de uma integração real, sorteadas ou sob
demanda. Commit `feat(pagamentos-simulados): latencia, falhas e controle da api simulada [REQ-19]`.

---

## Phase 5: User Story 3 - Classificação dos erros e novas tentativas seguras (Priority: P1)

**Goal**: o cliente classifica toda falha e repete as passageiras com a mesma chave, espera
exponencial com jitter, `Retry-After` e limites por tentativa e total, sem nunca cobrar duas
vezes.

**Independent Test**: com roteiros de falha, conferir o desfecho do pedido, o número de
chamadas recebidas pela API e `efetivacoes <= 1` (quickstart §4).

### Testes da US3

- [X] T027 [P] [US3] Completar `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ClassificacaoRespostaGatewayTestes.cs`: `503`, `500`, `502`, `504`, `429` e `409 operacao-em-andamento` → passageira; `409 chave-reutilizada` → permanente; falha de conexão → passageira; tempo da tentativa esgotado → incerto; `429` e `409` informam o `Retry-After`; `409 operacao-em-andamento` e os permanentes não contam para o circuito (tabela do contrato do cliente).
- [X] T028 [P] [US3] Testes unitários da política em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/PipelineGatewayPagamentoTestes.cs` (com `HttpMessageHandler` falso e `FakeTimeProvider` se disponível, ou tempos curtos): até `Tentativas` com a mesma `Idempotency-Key`; espera ≥ `Retry-After`; permanente não repete; o tempo total interrompe as tentativas; o tempo da tentativa abandona a espera.
- [X] T029 [P] [US3] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/NovasTentativasHttpTestes.cs`: roteiro `indisponivel, aprovar` (aprovado, `chamadas: 2`, `efetivacoes: 1`, log 7013, e a mensagem sem `x-tentativas` nem reentrega pela base, SC-003); `limite-taxa, aprovar` (espera ≥ `Retry-After`); `erro-interno, aprovar`; latência alta dentro do limite, definida por `PUT /controle/comportamento` (sem repetição); `sem-resposta` em todas as tentativas, conferindo que a mensagem é liberada em até `TempoTotalMs` mais uma margem e que, nesse intervalo, outra conexão lê e atualiza o pedido sem ficar bloqueada (FR-019, R-10); falhas em todas as tentativas (nada gravado, nova tentativa da base de consumo e, esgotada, DLQ); `chave-reutilizada` forçada (log 7015, sem repetição); estorno com `indisponivel, aprovar` confirmado uma vez, e com falhas esgotadas continuando pendente com tentativa contada (REQ-13).

### Implementação da US3

- [X] T030 [US3] Pipeline de resiliência em `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs` com `AddResilienceHandler` (R-06): tempo total `TempoTotalMs` → retry (`Tentativas`, exponencial com jitter a partir de `EsperaBaseMs`, `DelayGenerator` respeitando `Retry-After`, `ShouldHandle` pela classificação) → (circuito reservado para a US5) → tempo por tentativa `TempoTentativaMs`; a mesma requisição com a mesma `Idempotency-Key` em toda tentativa.
- [X] T031 [US3] Tradução das falhas em `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoHttp.cs`: tentativas ou tempo total esgotados → `FalhaGatewayPagamentoException(Passageira)`; permanente → `FalhaGatewayPagamentoException(Permanente)`; cancelamento do processo propaga `OperationCanceledException` sem log de falha; nada é gravado pelos chamadores (os consumidores e o `ServicoEstornos` já tratam exceção como falha técnica; conferir em `src/PedidoFulfillment.Application/Pagamentos/ProcessamentoPagamento.cs` e `ServicoEstornos.cs`).
- [X] T032 [US3] Logs em `src/PedidoFulfillment.Infrastructure/Pagamentos/EventosGatewayPagamento.cs`: 7013 `FalhaPassageiraGateway` (pedido, operação, tipo `indisponivel`|`limite-taxa`|`erro-interno`|`em-andamento`|`conexao`, tentativa) emitido no `OnRetry`, e 7015 `ErroPermanenteGateway` (Error; pedido, operação, status HTTP, código).

**Checkpoint**: uma falha passageira isolada não chega à base de consumo (SC-003) e nenhuma
combinação cobra duas vezes. Commit
`feat(pagamentos): classificacao das falhas e novas tentativas do gateway [REQ-19]`.

---

## Phase 6: User Story 4 - Resultado incerto (Priority: P1)

**Goal**: a cobrança ou o estorno efetivado sem resposta é resolvido pela mesma chave, mesmo
depois de reiniciar o Worker; o pedido cancelado sem pagamento consulta a cobrança.

**Independent Test**: roteiro `processar-sem-responder`, pedido aprovado com o mesmo `PAG-…` e
`efetivacoes: 1`; repetir reiniciando o Worker (quickstart §5, §7).

### Testes da US4

- [X] T033 [P] [US4] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ProcessamentoPagamentoTestes.cs`: pedido `Cancelado` sem pagamento e com o estoque devolvido → `ConsultarCobrancaAsync`; `Aprovada` → `RegistrarCobrancaAposCancelamento` (estorno pendente, REQ-13 R-07); `NaoEncontrada` e `Recusada` → nada; exceção na consulta → propaga como falha (nada gravado).
- [X] T034 [P] [US4] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/ResultadoIncertoHttpTestes.cs`: `processar-sem-responder` na cobrança (log 7014, segunda tentativa recebe `200` + `Idempotent-Replayed`, mesmo `PAG-…`, `efetivacoes: 1`); o mesmo com o Worker parado e recriado (nova `FabricaWorker`, mesma API) antes da reentrega; `processar-sem-responder` no estorno (estornado uma vez); duas entregas simultâneas do mesmo pedido (uma `operacao-em-andamento`, `efetivacoes: 1`); duas instâncias do Worker (duas `FabricaWorker`) apontando para a mesma API e tratando o mesmo pedido (reentrega e reconciliação), com `efetivacoes: 1` (SC-002).
- [X] T035 [P] [US4] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/CobrancaAposCancelamentoTestes.cs` (FR-015): roteiro `processar-sem-responder` seguido de `sem-resposta` para as demais tentativas (a falha sobre a chave concluída não apaga a cobrança, R-03), tentativas esgotadas, cancelamento pela operação, reentrega → log 7019 com `aprovada`, pagamento com estorno pendente e, na rodada seguinte, `pagamento.estornado`; sem cobrança na API → nada gravado.

### Implementação da US4

- [X] T036 [US4] Resultado incerto em `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoHttp.cs` e `EventosGatewayPagamento.cs`: tempo da tentativa esgotado (ou conexão perdida depois do envio) → log 7014 `ResultadoIncertoGateway` (pedido, operação, tentativa) e repetição com a mesma chave; nada gravado sem resultado (FR-013).
- [X] T037 [US4] FR-015 em `src/PedidoFulfillment.Application/Pagamentos/ProcessamentoPagamento.cs` (R-08): pedido cancelado sem pagamento e com o estoque devolvido consulta a cobrança antes de ignorar; log 7019 `ConsultaCobranca` (pedido, resultado) em `src/PedidoFulfillment.Application/Pagamentos/EventosPagamento.cs`, onde a consulta é decidida; vale também para a reconciliação, que passa pelo mesmo processamento.

**Checkpoint**: nenhum resultado incerto vira cobrança dupla ou cobrança sem registro. Commit
`feat(pagamentos): resultado incerto e consulta da cobranca pela chave [REQ-19]`.

---

## Phase 7: User Story 5 - Circuito aberto e pausa do consumo (Priority: P2)

**Goal**: falhas em sequência abrem o circuito; o consumo de pagamento pausa sem DLQ nem
tentativa contada e retoma sozinho; estornos ficam adiados; a reconciliação conta falha.

**Independent Test**: modo `indisponivel`, 10 pedidos, mensagens esperando em `oms.pagamento`
com a DLQ vazia e a prontidão `200`; desfeito o modo, retomada e pedidos cobrados
(quickstart §6, §8).

### Testes da US5

- [X] T038 [P] [US5] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SupervisorInscricaoTestes.cs` e `VerificacaoConsumidoresTestes.cs`: sessão encerrada com motivo `pausa` marca o consumidor como pausado com `PausadoAte`, espera até `DisponivelEm` e reabre; a reabertura depois da pausa não conta em `oms.mensageria.consumidor.reinscricoes`; consumidor pausado é saudável na prontidão.
- [X] T039 [P] [US5] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ServicoEstornosTestes.cs`: `GatewayPagamentoIndisponivelException` → `ResultadoRodadaEstorno.Adiado`, nada gravado, tentativa não contada.
- [X] T040 [P] [US5] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/PagamentosHttp/CircuitoGatewayTestes.cs`: modo `indisponivel` até abrir (log 7016, 5020); com pouco movimento, um único pedido com a API em `indisponivel` também abre o circuito e pausa, sem chegar à DLQ (SC-004); com o circuito aberto, nenhuma chamada nova chega à API (contagem pela rota de controle); mensagens em `oms.pagamento`, `oms.pagamento.dlq` vazia, sem `x-tentativas` novo; prontidão `200`; consumidor `estoque` continua consumindo; desfeito o modo, 7017/7018 e 5021 e os pedidos cobrados com `efetivacoes <= 1`; estorno com o circuito aberto (7020, continua `EstornoPendente` sem tentativa contada, confirmado depois); reconciliação com o circuito aberto conta falha do ciclo.

### Implementação da US5

- [X] T041 [US5] Circuito no pipeline de `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs` (`Circuito:ProporcaoFalhas`, `MinimoChamadas`, `JanelaSegundos`, `PausaSegundos`; recusa de negócio, permanentes e `operacao-em-andamento` não contam) e `src/PedidoFulfillment.Infrastructure/Pagamentos/EstadoGatewayPagamento.cs` (singleton atualizado por `OnOpened`/`OnHalfOpened`/`OnClosed`, com `DisponivelEm`); logs 7016 `CircuitoAberto` (pausa em s, disponível em), 7017 `CircuitoMeioAberto`, 7018 `CircuitoFechado`; `BrokenCircuitException` vira `GatewayPagamentoIndisponivelException(DisponivelEm)` em `GatewayPagamentoHttp.cs`.
- [X] T042 [US5] Pausa na base de consumo em `src/PedidoFulfillment.Infrastructure/Mensageria/ConsumidorEventos.cs` e `SessaoConsumo.cs`: `DependenciaIndisponivelException` → rollback, `BasicNack` com `requeue` (sem `x-tentativas` novo, sem republicar, sem DLQ) e fim da sessão com o motivo `pausa` e o `DisponivelEm`; log 5020 `ConsumoPausado` (consumidor, até quando).
- [X] T043 [US5] Supervisor e estado em `src/PedidoFulfillment.Infrastructure/Mensageria/SupervisorInscricao.cs`, `EstadoConsumidores.cs` e `src/PedidoFulfillment.Infrastructure/Saude/VerificacaoConsumidores.cs`: situação pausado (`PausadoAte`), saudável na prontidão; espera até `DisponivelEm` (ou o encerramento do processo) e reabre a sessão; log 5021 `ConsumoRetomado` (consumidor, duração da pausa em ms); a reabertura não conta como reinscrição.
- [X] T044 [US5] Estornos adiados: `ResultadoRodadaEstorno.Adiado` em `src/PedidoFulfillment.Application/Pagamentos/ServicoEstornos.cs` (captura `GatewayPagamentoIndisponivelException`, nada gravado, tentativa não contada) e `src/PedidoFulfillment.Infrastructure/Pagamentos/ProcessadorEstornos.cs` encerrando a rodada no primeiro `Adiado`, com log 7020 `EstornosAdiados` (quantidade, disponível em). A reconciliação não muda (a exceção já conta como falha do ciclo; conferir com o teste da T040).

**Checkpoint**: numa queda da API, zero chamadas até o fim da pausa (SC-004), nada na DLQ e
retomada sozinha. Commit `feat(pagamentos): circuito do gateway e pausa do consumo de pagamento [REQ-19]`.

---

## Phase 8: User Story 6 - A operação enxerga a chamada ao pagamento (Priority: P2)

**Goal**: span interno por operação com um span de cliente HTTP por tentativa, `traceparent`
propagado, métricas `oms.pagamentos.*` e medidor nativo do HTTP.

**Independent Test**: roteiro `indisponivel, aprovar` com a telemetria ligada; no trace do
pedido, `cobrar pagamento` com dois spans HTTP (`503`, `201`) e `oms.pagamento.tentativa` 1 e 2
(quickstart §3–§4).

### Testes da US6

- [X] T045 [P] [US6] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/TelemetriaGatewayPagamentoTestes.cs` (com `ColetorTelemetria`): span `cobrar pagamento` filho do span de consumo, com `oms.pedido.id`, `oms.pagamento.operacao`, `oms.resultado`, `oms.pagamento.tentativas`; dois spans de cliente HTTP filhos dele, com `http.response.status_code` e `oms.pagamento.tentativa`; a API simulada recebeu o `traceparent` do span de tentativa; métricas `oms.pagamentos.chamadas` (operação, resultado), `oms.pagamentos.duracao` e `oms.pagamentos.circuito`; nenhuma tag, atributo ou log com a `ChaveApi`, o `PAG-…` ou o corpo; span `estornar pagamento` e `consultar cobranca` nos cenários correspondentes.

### Implementação da US6

- [X] T046 [P] [US6] `src/PedidoFulfillment.Infrastructure/Pagamentos/MarcadorTentativaHandler.cs` (`DelegatingHandler` dentro do retry, põe `oms.pagamento.tentativa` no `Activity.Current` do HTTP) e spans internos `cobrar pagamento`, `estornar pagamento` e `consultar cobranca` por `TelemetriaOms` em `GatewayPagamentoHttp.cs`.
- [X] T047 [US6] `src/PedidoFulfillment.Infrastructure/Observabilidade/ExtensoesObservabilidade.cs` e `TelemetriaOms.cs`: `AddSource("System.Net.Http")` (ou instrumentação nativa equivalente) e medidor `System.Net.Http`; métricas `oms.pagamentos.chamadas`, `oms.pagamentos.duracao` e `oms.pagamentos.circuito` (gauge a partir do `EstadoGatewayPagamento`); `url.full` com a query oculta (padrão do .NET), sem cabeçalhos nem corpo. Conferir que os testes de telemetria do REQ-18 (webhooks) seguem verdes com o span de saída novo.

**Checkpoint**: uma lentidão ou queda da API aparece no trace, nos logs e nas métricas.
Commit `feat(observabilidade): spans e metricas das chamadas ao gateway de pagamento [REQ-19]`.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T048 [P] Documentos de fluxo (regra do `CLAUDE.md`, research R-14, FR-025), conferindo mensagens, códigos, limites, IDs de log e links relativos no código:
  - `docs/fluxos/14-gateway-de-pagamento.md` novo, no formato dos demais: caminho feliz, rotas e respostas da API simulada, classificação e reação (com exemplos), novas tentativas, resultado incerto e consulta pela chave, circuito e pausa do consumo, quando a mensagem ainda vai para a DLQ (erro permanente, inclusive credencial errada, falha passageira que não abre o circuito e falhas fora do gateway, conforme o [contrato do cliente](./contracts/cliente-gateway-pagamento.md#quando-a-mensagem-de-pagamento-vai-para-a-dlq)), concorrência (mesma chave, duas instâncias com circuito por instância), falhas externas, pior caso por mensagem (≈ 4 × 10 s + 7 s antes da DLQ), logs 7013–7020, 5020–5021 e 11001–11006, métricas e limitações (chaves da API simulada em memória, perdidas ao recriá-la);
  - fluxo 05 (pagamento: chamada pela rede, FR-015; a limitação "cobrança não consultável pela chave" sai e a #46 entra no lugar), 08 (publicação e consumo: pausa com `nack` + `requeue`, motivo `pausa`, logs 5020–5021, prontidão), 09 (reconciliação: falha do ciclo com o circuito aberto), 10 (estorno: `Adiado`, `EST-…`; a limitação "chaves perdidas no reinício do Worker" sai e a #46 entra no lugar), 12 (spans de saída inclusive dos webhooks, métricas `oms.pagamentos.*`, medidor `System.Net.Http`);
  - linha do fluxo 14 em `docs/fluxos/README.md`; o diagrama de estados não muda.
- [X] T049 [P] Documentação do projeto: `README.md` (serviço `pagamentos` na porta 5232, variáveis `OMS_PAGAMENTOS_*`, `GatewayPagamento:*`) e `docs/proximos-passos.md` (REQ-19 entregue; a #18 continua a numerar).
- [X] T050 Varredura de segredos (nenhuma chave real no diff, só as de desenvolvimento), `dotnet build`, `dotnet format --verify-no-changes` e a suíte local inteira com Docker (zero ignorados); registrar a contagem em Notes.
- [X] T051 Executar o [quickstart](./quickstart.md) (§1 a §8) com o compose e registrar em Notes o que foi visto nos logs, no painel do RabbitMQ e no Aspire, corrigindo o quickstart onde divergir. Com as chances padrão, registrar também quantos pedidos de uma carga de demonstração tiveram desfecho de pagamento (SC-001, esperado 100%) e o p95 de "criado → entrega criada" (SC-006, até 10 s), pela métrica ou pelo job de demonstração do REQ-10.
- [X] T052 Push e PR em português (Resumo, Pontos de atenção, Validação) com `Closes #46`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, o pacote novo, a chamada dentro da transação, a pausa do consumo, o circuito por instância e o span de saída dos webhooks. Acompanhar `build-testes` e `contrato-compatibilidade` até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup** (T001): antes de tudo.
- **Foundational** (T002 a T016): bloqueia todas as histórias. T007 a T010 antes de T011; T011 e T012 antes de T016; T013 antes de T014 e T015.
- **US1** (T017 a T022): depende da fundação. T020 antes de T021; T022 por último.
- **US2** (T023 a T026): depende só da fundação; pode correr junto da US1. T025 antes de T026.
- **US3** (T027 a T032): depende da US1 (cliente HTTP) e da US2 (falhas executadas). T030 antes de T031 e T032.
- **US4** (T033 a T037): depende da US3 (tempo por tentativa e repetição). T036 e T037 independentes entre si.
- **US5** (T038 a T044): depende da US3 (pipeline). T041 antes de T042; T042 antes de T043; T044 depois de T041.
- **US6** (T045 a T047): depende da US1; a métrica do circuito depende da T041. Pode correr junto da US4.
- **Polish** (T048 a T052): depois de tudo. A T052 exige push.

### Parallel Opportunities

- T002 a T006; T007 a T010, T012 e T013.
- T017 a T019; US1 e US2 em paralelo depois da fundação.
- T027 a T029; T033 a T035; T038 a T040.
- US4, US5 e US6 em paralelo depois da US3 (arquivos distintos, exceto `GatewayPagamentoHttp.cs` e `ExtensoesPagamento.cs`, que pedem ordem: T036 → T041 → T046).
- T048 e T049.

### Exemplo: fundação

```text
Juntos: T002 (opções API), T003 (armazenamento), T004 (decisor), T005 (autenticação), T006 (opções cliente)
Depois: T007, T008, T009, T010, T012, T013 → T011 → T016; T013 → T014 → T015
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: o pagamento passa a ser feito por uma API separada, pela rede, com o
comportamento de negócio de hoje e as chaves guardadas fora do Worker.

### Incremental Delivery

1. Setup + fundação → `feat(pagamentos): api simulada idempotente e gateway em memoria nos testes [REQ-19]`.
2. US1 → `feat(pagamentos): cobranca, estorno e reconciliacao pela api simulada [REQ-19]`.
3. US2 → `feat(pagamentos-simulados): latencia, falhas e controle da api simulada [REQ-19]`.
4. US3 → `feat(pagamentos): classificacao das falhas e novas tentativas do gateway [REQ-19]`.
5. US4 → `feat(pagamentos): resultado incerto e consulta da cobranca pela chave [REQ-19]`.
6. US5 → `feat(pagamentos): circuito do gateway e pausa do consumo de pagamento [REQ-19]`.
7. US6 → `feat(observabilidade): spans e metricas das chamadas ao gateway de pagamento [REQ-19]`.
8. Polish → `docs(fluxos): gateway de pagamento por http [REQ-19]`, push e PR.

---

## Notes

- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Decisões confirmadas pelo usuário depois do plan: pacote `Microsoft.Extensions.Http.Resilience`
  (R-06), chamada dentro da transação (R-10) e pausa do consumo com o circuito aberto (R-09).
- Fora do escopo: banco na API simulada (chaves em memória, clarify), telemetria exportada pela
  API simulada, circuito compartilhado entre instâncias do Worker, gateway real.
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em algumas execuções.
- **T050 (2026-11-02)**: varredura de segredos sem chave real no diff (só as de desenvolvimento
  `oms-pagamentos-dev-local-2026` e `controle-dev-local-2026` e as de teste); `dotnet build` sem
  avisos; `dotnet format --verify-no-changes` limpo; suíte local com Docker: **895 unitários e
  578 de integração, todos aprovados, 0 ignorados**.
- **T051 (2026-11-02)**, quickstart com `docker compose --profile demo`:
  - §1–§2: os seis serviços saudáveis; `401` sem a chave do OMS e com a chave do OMS na rota de
    controle.
  - §3: 30 pedidos, um a cada 0,5 s, com as chances padrão: 30/30 com desfecho (28
    `ProntoParaEnvio`, 2 `PagamentoRejeitado`; SC-001). Pelos instantes gravados, criado →
    `ProntoParaEnvio` entre 2,1 e 6,5 s, p95 ≈ 6,0 s (SC-006, meta de 10 s). Numa rajada de 40
    pedidos de uma vez, mediana ≈ 10,8 s e máximo 28,7 s: fila no consumidor de pagamento, que
    trata uma mensagem por vez (ponto de atenção do PR). Nenhuma operação com `efetivacoes > 1`.
  - §4: `indisponivel, aprovar` → 7013 na tentativa 1 e o pedido pago; a operação mostra
    `chamadas: 1` (a chamada que falhou não cria operação; quickstart corrigido).
  - §5: `processar-sem-responder` → 7014, mesmo `PAG-…` da API, `efetivacoes: 1`, `chamadas: 2`.
  - §6: modo `indisponivel` com 10 pedidos → 7016, prontidão do Worker `200`, **0 chamadas à
    API em 10 s** com o circuito aberto, `oms.pagamento.dlq` vazia, os 10 em `Pendente`;
    desfeito o modo, 5021 e 7018, e os 10 em `ProntoParaEnvio` em 7 s.
  - §8: estorno com o circuito aberto → três 7020 e nenhum 7007 (tentativa não contada);
    desfeito o modo, estorno confirmado uma vez. O quickstart passou a explicar que as cobranças
    abrem o circuito (as rodadas de estorno sozinhas não atingem o mínimo de chamadas).
- **T052 (2026-11-02)**: PR #47 (`Closes #46`). CI do primeiro push verde: `Build e testes`
  (8 min 14 s) e `Demonstração` (1 min 36 s, com o `/health` da API simulada). O
  `contrato-compatibilidade` não rodou: o contrato v1 não mudou.
