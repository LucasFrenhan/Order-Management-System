---

description: "Lista de tarefas do REQ-09 — Pagamento assíncrono simulado com compensação do estoque"
---

# Tasks: Pagamento assíncrono simulado com compensação do estoque

**Input**: Design documents from `/specs/009-pagamento/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/consumidor-pagamento.md](./contracts/consumidor-pagamento.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio
(aprovação, recusa com devolução, decisão do consumidor, gateway simulado). A spec pede
banco e broker reais para o consumidor, com o resultado do gateway fixado (FR-016). Os
testes de cada fase vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A fundação traz:
- a regra de pagamento no `Pedido`;
- a porta do gateway e a simulação, com a configuração validada;
- a leitura do pedido com o pagamento;
- o esqueleto do consumidor e os logs. O registro do consumidor no Worker fica para a US1,
  como no REQ-08 (achado M1 daquele REQ).

As US1, US2 e US4 completam o mesmo manipulador (`ProcessamentoPagamento.cs`), por isso
correm em sequência. A US3 (configuração do gateway no Worker) pode correr em paralelo com
a US2.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Base de consumo do REQ-06, sobre RabbitMQ.Client nativo, sem MassTransit.
- Nomes em português; eventos no passado (`pagamento.aprovado`, `pagamento.recusado`).
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo e nenhuma migration (R-11).
- Contrato HTTP v1 e contratos de eventos v1 intocados (FR-013, FR-015).
- Os testes com banco ou broker reais usam Testcontainers e respeitam
  `OMS_EXIGIR_DOCKER` (REQ-07); os de mensageria entram na `ColecaoMensageria`.
- Nenhuma espera fixa em teste: espere por condição, com prazo
  (`MensageriaTeste.EsperarAteAsync`).
- Nenhum teste depende de sorte: taxa 100 ou 0 na integração, gateway falso nos unitários
  e semente fixa no teste da proporção (R-02).
- Commits: `tipo(escopo): descrição [REQ-09]`.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-09-pagamento` e registrar em Notes a contagem de testes como linha de base (534 no fim do REQ-08: 292 unitários e 242 de integração; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T002 [P] Testes de domínio em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoPagamentoTestes.cs` (data-model, R-03), com pedido reservado pelo `ReservarEstoque` do REQ-08:
  - `AptoParaPagamento`: verdadeiro com reserva, sem pagamento e em `Pendente`, `Processando` ou `ProntoParaEnvio` (theory); falso sem reserva, com pagamento ou em `Cancelado`, `Despachado` e `PagamentoRejeitado` (theory);
  - `AprovarPagamento` em `Pendente`: `Pagamento` com `Valor = ValorTotal`, `Situacao = Aprovado`, o identificador e `ProcessadoEm`; status `Processando`; histórico com `AutorId` nulo e motivo `Pagamento aprovado.`; `EstoqueReservado` continua verdadeiro e nenhum estoque muda;
  - `AprovarPagamento` em `Processando` e `ProntoParaEnvio` (theory): pagamento aprovado, status e histórico sem mudança;
  - `RecusarPagamento`: `Pagamento` `Recusado` sem identificador; cada produto recebe a quantidade do item, inclusive produto desativado depois da reserva; `EstoqueReservado` falso; status `PagamentoRejeitado` com o motivo no histórico e `AutorId` nulo; vale a partir de `Pendente`, `Processando` e `ProntoParaEnvio` (theory);
  - os dois métodos em pedido não apto (sem reserva, já pago, status final): `InvalidOperationException` sem alterar nada;
  - motivo vazio ou com mais de 500 caracteres na recusa, e identificador vazio ou com mais de 100 na aprovação: exceção de argumento.
- [X] T003 [P] Testes do gateway em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/GatewayPagamentoSimuladoTestes.cs` (R-02), pelo construtor `internal` com `Random` de semente fixa:
  - taxa padrão (90): 10.000 cobranças com taxa de aprovação entre 88% e 92% (SC-002);
  - taxa 100 e taxa 0: todas aprovadas ou todas recusadas;
  - aprovada: identificador começa com `SIM-`, tem 36 caracteres e não se repete em 1.000 cobranças;
  - recusada: motivo `Pagamento recusado pelo emissor.` e sem identificador.
- [X] T004 [P] Testes do validador em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesGatewayPagamentoTestes.cs`, como `OpcoesMensageriaTestes`: padrão 90 válido; 0 e 100 válidos; -1, 100.5 e 150 inválidos, com a mensagem citando `GatewayPagamento:TaxaAprovacaoPercentual` sem o valor.

### Implementação da fundação

- [X] T005 Em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`: `const string MotivoPagamentoAprovado = "Pagamento aprovado."`, `bool AptoParaPagamento` e os métodos `AprovarPagamento(string identificadorTransacao, DateTimeOffset instante)` e `RecusarPagamento(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`, conforme o data-model. Criar o `Pagamento` com `new Pagamento(Id, ValorTotal, instante)` e `RegistrarResultado`; a recusa chama `DevolverEstoque` e `MudarStatus(PagamentoRejeitado, null, motivo, instante)`. Validar com `Guardas.Texto`, antes de qualquer alteração, o motivo da recusa (obrigatório, até 500) e o identificador da aprovação (obrigatório, até 100; `RegistrarResultado` o aceita opcional, achado M1). XML doc citando R-03 e R-04. T002 deve passar.
- [X] T006 [P] Porta em `src/PedidoFulfillment.Application/Pagamentos/IGatewayPagamento.cs`: `Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken)` e o `sealed record ResultadoCobranca(bool Aprovado, string? IdentificadorTransacao, string? Motivo)` com as fábricas `Aprovada(string identificadorTransacao)` e `Recusada(string motivo)`.
- [X] T007 Gateway simulado (R-02):
  - `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesGatewayPagamento.cs`: `OpcoesGatewayPagamento` (`Secao = "GatewayPagamento"`, `double TaxaAprovacaoPercentual = 90`) e `ValidadorOpcoesGatewayPagamento` (0 a 100; mensagem com a chave, sem o valor);
  - `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoSimulado.cs`: `internal sealed`, implementa `IGatewayPagamento`; construtor com `IOptions<OpcoesGatewayPagamento>` usando `Random.Shared` e construtor `internal` que recebe também o `Random`; aprova quando `NextDouble() * 100 < taxa`; identificador `SIM-` + `Guid.CreateVersion7().ToString("N")`; motivo constante `Pagamento recusado pelo emissor.`;
  - `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs`: `AdicionarGatewayPagamentoSimulado()` registra o validador, `AddOptions<OpcoesGatewayPagamento>().BindConfiguration(...).ValidateOnStart()` e o gateway como singleton.

  T003 e T004 devem passar.
- [X] T008 Leitura com o pagamento (R-06): em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, `ObterPedidoAsync` passa a incluir `Pagamento` (`.Include(p => p.Pagamento)`), e o XML doc de `IArmazenamentoPedidos.ObterPedidoAsync` cita o pagamento. O falso de `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` não copia pedidos: cada leitura chama a fábrica registrada e devolve uma instância nova (achado M2). O pedido "já pago" dos testes sai da própria fábrica (`AprovarPagamento` dentro dela), e o pagamento criado numa tentativa que perdeu some na releitura; a T019 confere isso. Rodar a suíte: tudo verde.
- [X] T009 Esqueleto do consumidor (R-01, R-08):
  - `src/PedidoFulfillment.Application/Pagamentos/EventosPagamento.cs`: `LoggerMessage` 7001 `PagamentoAprovado` (pedido, valor, transação, evento), 7002 `PagamentoRecusado` (pedido, valor, evento), 7003 `PedidoIgnoradoNoPagamento` (status, reservado, já pago, evento) e 7004 `ConflitoNoPagamento` (Warning, reavaliação), só com identificadores e valores;
  - `src/PedidoFulfillment.Application/Pagamentos/ProcessamentoPagamento.cs`: `public sealed class ProcessamentoPagamento(IArmazenamentoPedidos armazenamento, IGatewayPagamento gateway, TimeProvider relogio, ILogger<ProcessamentoPagamento> logger) : IManipuladorEvento<EstoqueReservado>`, com `Consumidor = "pagamento"`, `Tipos = [TiposEvento.EstoqueReservado]` e `ManipularAsync` lançando `NotImplementedException`; XML doc citando `contracts/consumidor-pagamento.md`.

  Ainda sem registro no Worker. Rodar a suíte: tudo verde.

**Checkpoint**: regra no domínio, gateway simulado com configuração validada, leitura com o pagamento e esqueleto do consumidor.

---

## Phase 3: User Story 1 - Pagamento aprovado leva o pedido a Processando (Priority: P1) 🎯 MVP

**Goal**: "estoque reservado" com cobrança aprovada grava o pagamento, muda para `Processando` e publica `pagamento.aprovado`.

**Independent Test**: com a taxa em 100%, criar um pedido com estoque e conferir o pedido em `Processando`, o pagamento aprovado e a mensagem numa fila de teste.

### Testes da US1

- [X] T010 [P] [US1] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ProcessamentoPagamentoTestes.cs`, com o armazenamento falso e um gateway falso que conta as chamadas e devolve o resultado programado:
  - `Consumidor` é `pagamento` e `Tipos` é `[estoque.reservado]`;
  - pedido `Pendente` reservado e cobrança aprovada: gateway chamado uma vez com o id e o `ValorTotal`; pagamento `Aprovado`; status `Processando`; nenhum produto lido para alteração; uma chamada a `SalvarAsync`; um `EventoSaida` `pagamento.aprovado` com `idCorrelacao` igual ao pedido, valor e identificador iguais aos do pagamento; log 7001;
  - pedido inexistente: `InvalidOperationException`, gateway não chamado, nada gravado.
- [X] T011 [P] [US1] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/PagamentoAprovadoTestes.cs` (`ColecaoMensageria`), com o helper novo `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/CenarioPagamento.cs` (Worker com `GatewayPagamento:TaxaAprovacaoPercentual` configurável; `LimparFilasConsumidorAsync` de `estoque` e de `pagamento` e espera dos dois consumidores ligados a `oms.estoque` e `oms.pagamento` antes de criar pedidos, como `CenarioEstoque.WorkerProntoAsync` (achado M3); publicação direta de `estoque.reservado` na troca; consultas de status, pagamento e estoque), API (`CenarioPedidos.Api`) e `FilaTesteAsync("pagamento.*")`:
  - cenários 1 a 3: o pedido criado pela API termina em `Processando` em até 5 s, com uma linha em `Pagamentos` (`Aprovado`, `Valor = ValorTotal`, `SIM-...`), a entrada `Pagamento aprovado.` no histórico e `pagamento.aprovado` válido pelo schema (`SchemaEventosTestes`) com os mesmos valores;
  - cenário 4: republicar o mesmo `estoque.reservado` (mesmo `idEvento`) direto na troca não cria pagamento nem evento novo.

### Implementação da US1

- [X] T012 [US1] Em `ProcessamentoPagamento.ManipularAsync`: ler o pedido com `paraAlterar: true` (inexistente → `InvalidOperationException` com o id), chamar `gateway.CobrarAsync(pedido.Id, pedido.ValorTotal, ...)` e, aprovada, `pedido.AprovarPagamento`, `armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoAprovado { ... }, agora), agora))`, `SalvarAsync` e log 7001. No Worker (`src/PedidoFulfillment.Worker/ProgramaWorker.cs`), `builder.Services.AdicionarGatewayPagamentoSimulado();` e `builder.Services.AdicionarConsumidor<ProcessamentoPagamento, EstoqueReservado>();`, e em `src/PedidoFulfillment.Worker/appsettings.json` a seção `GatewayPagamento` com `TaxaAprovacaoPercentual: 90` e comentário. T010 e T011 devem passar.
- [X] T013 [US1] Isolamento e regressão (R-09):
  - `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaWorker.cs`: `GatewayPagamento:TaxaAprovacaoPercentual = 100` por padrão (sobrescrito por `Configuracoes`);
  - `tests/PedidoFulfillment.Testes.Integracao/Estoque/CenarioEstoque.cs`: o Worker do estoque remove o `IHostedService` `ConsumidorEventos<ProcessamentoPagamento, EstoqueReservado>` pelo `Servicos` (combinando com o registro de logs que já existe), com comentário citando R-09;
  - revisar os testes do REQ-06 que sobem o Worker (`Mensageria/OutboxComBrokerTestes.cs`, `WorkerTestes.cs`, `ConsumoComNovaTentativaTestes.cs`) e restringir ao tipo tratado as asserções sobre `EventosSaida` que passariam a contar `pagamento.*`.

  Rodar a suíte inteira no checkpoint.

**Checkpoint**: MVP — um pedido reservado é pago e anunciado.

---

## Phase 4: User Story 2 - Pagamento recusado rejeita o pedido e devolve o estoque (Priority: P1)

**Goal**: cobrança recusada grava o pagamento, muda para `PagamentoRejeitado`, devolve o estoque e publica `pagamento.recusado`, tudo na mesma gravação.

**Independent Test**: com a taxa em 0%, criar um pedido e conferir `PagamentoRejeitado`, o estoque de volta e a mensagem.

### Testes da US2

- [X] T014 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ProcessamentoPagamentoTestes.cs`:
  - cobrança recusada em `Pendente`: produtos dos itens lidos com `paraAlterar: true`; estoque devolvido; `EstoqueReservado` falso; pagamento `Recusado`; status `PagamentoRejeitado` com o motivo do gateway; um `EventoSaida` `pagamento.recusado` com valor e motivo; uma chamada a `SalvarAsync`; log 7002;
  - recusa com produto inativo: estoque devolvido do mesmo jeito.
- [X] T015 [P] [US2] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/PagamentoRecusadoTestes.cs`, com o Worker em taxa 0:
  - cenários 1 a 3: anotar o estoque, criar o pedido e conferir `PagamentoRejeitado`, pagamento `Recusado`, `EstoqueReservado` falso, estoque igual ao anotado e `pagamento.recusado` válido pelo schema com o motivo `Pagamento recusado pelo emissor.`;
  - cenário 4: republicar o mesmo `estoque.reservado` não devolve o estoque de novo nem gera evento;
  - cenário 5 (achado H1): reservar com o Worker do estoque (`CenarioEstoque`, sem o consumidor de pagamento) e pará-lo; desativar o produto; subir o Worker completo (que limpa e declara as filas) e só então publicar direto na troca o `estoque.reservado` do pedido, com `idEvento` novo. Conferir a devolução com o produto ainda inativo. O `estoque.reservado` da primeira etapa não é usado: sem `oms.pagamento` ele não tem rota, e com a fila ele é limpo pelo cenário;
  - pela API, `PATCH` para `Cancelado` no pedido rejeitado responde a recusa de transição que já existe (status final, FR-010), sem mexer no estoque.

### Implementação da US2

- [X] T016 [US2] Em `ProcessamentoPagamento`, com a cobrança recusada: ler os produtos dos itens com `paraAlterar: true`, `pedido.RecusarPagamento(resultado.Motivo, produtos, agora)`, gravar `pagamento.recusado`, `SalvarAsync` e log 7002. T014 e T015 devem passar.

---

## Phase 5: User Story 3 - Simulação do gateway com proporção e resultado controláveis (Priority: P1)

**Goal**: a taxa padrão é 90%, configurável de 0 a 100; fora disso, o Worker não sobe.

**Independent Test**: subir o Worker com uma taxa inválida e conferir que a partida falha citando a chave, sem o valor.

A proporção (SC-002) e o validador já são cobertos na fundação (T003, T004).

- [X] T017 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/WorkerTestes.cs`, acrescentar ao theory `Configuracao_invalida_impede_o_Worker_de_subir_sem_mostrar_valores` o caso `GatewayPagamento:TaxaAprovacaoPercentual` = `150`, usando a cadeia de `FalhaPartida`, e conferir que a mensagem não contém `150`.
- [X] T018 [P] [US3] Conferir que a demonstração usa a taxa padrão: `docker-compose.yml` não sobrescreve `GatewayPagamento`, e o `appsettings.json` do Worker traz 90 (T012). Sem teste novo; registrar em Notes.

---

## Phase 6: User Story 4 - Pedido que mudou antes do pagamento (Priority: P2)

**Goal**: só pedidos aptos são cobrados; status final, sem reserva ou já pago é ignorado; conflitos são reavaliados com o estado atual, sem cobrar de novo.

**Independent Test**: com o Worker parado, cancelar um pedido reservado e publicar o `estoque.reservado`; ao subir, nada é cobrado nem devolvido.

### Testes da US4

- [X] T019 [P] [US4] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ProcessamentoPagamentoTestes.cs`:
  - `Cancelado`, `Despachado` e `PagamentoRejeitado` (theory), pedido sem reserva e pedido já pago: gateway não chamado, nenhum `SalvarAsync`, nenhum evento, log 7003 com o motivo;
  - `Processando` e `ProntoParaEnvio` reservados (theory): aprovado mantém o status e publica `pagamento.aprovado`; recusado vai para `PagamentoRejeitado` e devolve o estoque;
  - um conflito e depois sucesso: duas leituras, `DescartarAlteracoes` uma vez, gateway chamado uma vez só, um único evento;
  - conflito e, na releitura, o pedido foi cancelado (sem reserva): termina sem gravar, log 7003, gateway chamado uma vez;
  - conflito e, na releitura, o pedido foi movido para `Processando` pela operação (achado L2): aprovação sem mudança de status, gateway chamado uma vez;
  - conflito nas 3 vezes: relança `ConflitoConcorrenciaException`, sem evento pendente no falso, log 7004 três vezes e gateway chamado uma vez.
- [X] T020 [P] [US4] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/PedidoIgnoradoNoPagamentoTestes.cs`:
  - cenário 1 (achado H1): com o Worker do estoque (`CenarioEstoque`), reservar um pedido; parar; cancelar pela API (estoque devolvido pelo REQ-08); subir o Worker completo e só então publicar direto na troca o `estoque.reservado` dele, com `idEvento` novo: nenhum pagamento, estoque igual ao do cancelamento, nenhum `pagamento.*`, nada em `oms.pagamento.dlq` e log 7003;
  - cenário 4: `estoque.reservado` válido com `pedidoId` inexistente chega a `oms.pagamento.dlq` depois das 4 execuções (100, 200 e 400 ms);
  - cenário 5: com o produto de estoque 1, rodar 10 vezes, em paralelo, o cancelamento pela operação (`PATCH`) e o pagamento executado como a base (helper como `DisputaEstoqueTestes.ReservarComoABaseAsync`, com gateway fixo de recusa): em todas as rodadas, o estoque termina em 1 e o pedido em `Cancelado` sem pagamento ou em `PagamentoRejeitado` com pagamento `Recusado`; nunca estoque 2.

### Implementação da US4

- [X] T021 [US4] Em `ProcessamentoPagamento` (R-05):
  - depois da leitura, `!pedido.AptoParaPagamento` → log 7003 e retorno sem gravar;
  - envolver ler-decidir-gravar num laço de até 3 reavaliações (`Reavaliacoes = 3`, como `ReservaEstoque`): em `ConflitoConcorrenciaException`, `DescartarAlteracoes()`, log 7004 e nova volta; na última, relançar;
  - o resultado do gateway fica guardado fora do laço: a cobrança acontece só na primeira volta em que o pedido está apto.

  T019 e T020 devem passar.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T022 [P] Atualizar `README.md`:
  - **Estado atual**: linha do REQ-09;
  - **Próximos passos**: tirar a #9; na #10, citar que consome `pagamento.aprovado` com o pedido já em `Processando`;
  - **Mensageria**: a fila `oms.pagamento`, os eventos `pagamento.aprovado` e `pagamento.recusado` e a configuração `GatewayPagamento:TaxaAprovacaoPercentual`;
  - **Status do pedido**: manter a nota sobre "AguardandoEnvio";
  - **Roteiros de validação**: o quickstart do REQ-09.
- [ ] T023 [P] Em `specs/006-mensageria/contracts/topologia-e-consumo.md`, citar o consumidor de pagamento ao lado do de estoque, sem mudar o contrato. Em `specs/008-estoque/quickstart.md`, nota nos §3 e §5: com o pagamento ligado, o pedido reservado vai para `Processando` (ou `PagamentoRejeitado`); para repetir o roteiro do REQ-08, suba o Worker com a taxa em 100% e cancele como operação.
- [ ] T024 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados).
- [ ] T025 Executar o quickstart (§1 a §7) e registrar em Notes. Conferir nos logs do Worker os EventIds 7001, 7002 e 7003.
- [ ] T026 Fazer push e abrir o PR com `Closes #9`. Acompanhar o CI (`build-testes`, com os dois jobs) até ficar verde e registrar os tempos em Notes.

---

## Dependencies & Execution Order

- **Setup → Foundational**: a fundação precisa da linha de base.
- **US1**: depende da fundação inteira (T005 a T009).
- **US2 e US4**: completam o `ProcessamentoPagamento.cs` depois da US1, nessa ordem,
  porque editam o mesmo arquivo e os mesmos arquivos de teste.
- **US3**: depende da T012 (gateway registrado no Worker); pode correr em paralelo com a US2.
- **Polish**: depois de tudo. A T026 exige push.

### Parallel Opportunities

- T002, T003 e T004; T006 com T005; T010 e T011; T014 e T015; T017 e T018; T019 e T020;
  T022 e T023.
- A US3 (T017, T018) com a US2.

---

## Implementation Strategy

### MVP

Setup, Foundational e US1: o pedido reservado é pago, vai para `Processando`, e
`pagamento.aprovado` chega ao broker.

### Incremental Delivery

1. Setup + Foundational → commit `feat(pagamento): adiciona regra de pagamento, gateway simulado e consumidor [REQ-09]`.
2. US1 → commit `feat(pagamento): aprova pagamento do pedido reservado [REQ-09]`.
3. US2 → commit `feat(pagamento): recusa pagamento e devolve o estoque [REQ-09]`.
4. US3 → commit `test(pagamento): valida a taxa de aprovacao na partida do Worker [REQ-09]`.
5. US4 → commit `feat(pagamento): ignora pedido nao apto e reavalia em conflito [REQ-09]`.
6. Polish → `docs(pagamento): ... [REQ-09]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Estorno, nova tentativa de cobrança e falhas técnicas simuladas do gateway ficam fora do
  escopo (spec, Assumptions).
- **Analyze** (2026-10-23): aplicados H1 (cenários com dois Workers publicam
  `estoque.reservado` direto na troca depois de subir o Worker completo), M1 (identificador
  obrigatório na aprovação), M2 (o falso cria o pedido pela fábrica, sem cópias), M3
  (`CenarioPagamento` espera os dois consumidores), L1 (frase da US4, cenário 5) e L2
  (conflito com PATCH para `Processando`). L3 mantido: a demonstração do CI cobre a taxa
  padrão.
- **T001** (2026-10-23): linha de base com 534 testes verdes (292 unitários e 242 de
  integração), nenhum ignorado.
- **T013** (2026-10-23): as asserções sobre `EventosSaida` dos testes do REQ-06 já filtram
  pelo tipo `pedido.criado` desde o REQ-08; nada a mudar. `MensageriaTeste.ConsumidorLigadoAsync`
  passou a ser o helper comum de espera do consumidor (estoque e pagamento), e
  `CenarioEstoque.PublicarAsync` recebe o tipo. Suíte: 331 unitários e 244 de integração.
- **T018** (2026-10-23): o serviço `worker` do `docker-compose.yml` não define
  `GatewayPagamento`, então a demonstração usa os 90% do `appsettings.json` do Worker.
