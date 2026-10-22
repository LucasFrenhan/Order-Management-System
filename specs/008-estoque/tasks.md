---

description: "Lista de tarefas do REQ-08 — Reserva de estoque assíncrona com concorrência otimista"
---

# Tasks: Reserva de estoque assíncrona com concorrência otimista

**Input**: Design documents from `/specs/008-estoque/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/consumidor-estoque.md](./contracts/consumidor-estoque.md),
[contracts/eventos/](./contracts/eventos/), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige:
- testes unitários das regras de negócio (reserva, devolução, decisão do consumidor);
- testes de serialização e de conformidade de schema do contrato novo.

A spec pede banco e broker reais para o consumidor e para a disputa (SC-002, SC-007). Os
testes de cada fase vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A fundação traz:
- a regra de reserva e devolução no `Pedido`;
- a coluna `EstoqueReservado`, com a migration;
- as mudanças na porta `IArmazenamentoPedidos`;
- o contrato `estoque.insuficiente`;
- o esqueleto do consumidor e os logs. O registro no Worker fica para a US1 (achado M1).

As US1 a US4 completam o mesmo manipulador (`ReservaEstoque.cs`), por isso correm em
sequência. A US5 mexe no `ServicoPedidos` e pode correr em paralelo com as US2 a US4.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Base de consumo do REQ-06, sobre RabbitMQ.Client nativo, sem MassTransit.
- Nomes em português; tipos de evento no passado (`estoque.insuficiente`).
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo.
- Contrato HTTP v1 intocado.
- Os testes com banco ou broker reais usam Testcontainers e respeitam
  `OMS_EXIGIR_DOCKER` (REQ-07); os de mensageria entram na `ColecaoMensageria`.
- Nenhuma espera fixa em teste: espere por condição, com prazo
  (`MensageriaTeste.EsperarAteAsync`).
- Commits: `tipo(escopo): descrição [REQ-08]`.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-08-estoque` e registrar em Notes a contagem de testes como linha de base (485 no fim do REQ-06; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T002 [P] Testes de domínio em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoEstoqueTestes.cs` (data-model, R-02):
  - `ReservarEstoque` com todos os itens disponíveis: abate cada quantidade, marca `EstoqueReservado`, atualiza `AtualizadoEm` e devolve lista vazia;
  - quantidade igual ao estoque: reserva, e o estoque fica em 0;
  - um item com estoque menor e outro com produto inativo: devolve as duas `FaltaEstoque` (`ProdutoId`, `QuantidadeSolicitada`, `QuantidadeDisponivel`, `Inativo`) e não altera estoque, `EstoqueReservado` nem `AtualizadoEm` (tudo ou nada);
  - `ReservarEstoque` com `EstoqueReservado` já marcado, ou sem um dos produtos no dicionário: `InvalidOperationException`;
  - `DevolverEstoque` depois da reserva: soma cada quantidade, inclusive em produto desativado depois da reserva, e desmarca `EstoqueReservado`;
  - `DevolverEstoque` sem reserva: `InvalidOperationException`.
- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SerializacaoEventosTestes.cs`, cobrir `EstoqueInsuficiente` como os outros eventos: camelCase (`quantidadeSolicitada`, `quantidadeDisponivel`, `inativo`), `tipo` `estoque.insuficiente`, `versao` 1 e ida e volta igual por valor.
- [X] T004 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`, acrescentar o exemplo válido de `estoque.insuficiente` em `EventosValidos` e exemplos inválidos (sem `inativo`; `quantidadeDisponivel` negativa; `itens` vazio). O dicionário `Schemas` já percorre `TiposEvento`, então o tipo novo exige o schema copiado (T008).

### Implementação da fundação

- [X] T005 Criar `src/PedidoFulfillment.Domain/Pedidos/FaltaEstoque.cs` (`public sealed record FaltaEstoque(Guid ProdutoId, int QuantidadeSolicitada, int QuantidadeDisponivel, bool Inativo)`) e, em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`, a propriedade `EstoqueReservado` (`private set`) e os métodos `ReservarEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)` e `DevolverEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`, conforme o data-model. Disponível = produto ativo e `QuantidadeEstoque >= Quantidade`; a conferência de todos os itens vem antes de qualquer `AjustarEstoque`. T002 deve passar.
- [X] T006 Em `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoPedido.cs`, mapear `EstoqueReservado` (`bit NOT NULL`, padrão `false`) e gerar a migration `ReservaEstoque` com `dotnet tool run dotnet-ef migrations add ReservaEstoque` (projeto Infrastructure, inicialização na API, pasta `Persistencia/Migracoes`). Conferir que a migration só acrescenta a coluna com `defaultValue: false`. Atualizar a contagem e o nome da última migration em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/MigracaoBancoTestes.cs` e `ModeloSincronizadoTestes.cs`, se fixarem a lista.
- [X] T007 Mudanças na porta (R-07):
  - `src/PedidoFulfillment.Application/Pedidos/IArmazenamentoPedidos.cs`: `ObterProdutosAsync(IReadOnlyCollection<Guid> ids, bool paraAlterar, CancellationToken)` e `void Adicionar(EventoSaida evento)`, com XML doc;
  - `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`: leitura rastreada com `paraAlterar`, e `Adicionar` em `EventosSaida`;
  - `ServicoPedidos.CriarAsync` passa `paraAlterar: false`;
  - `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs`: o falso acompanha a porta e permite programar `ConflitoConcorrenciaException` nas próximas N chamadas de `SalvarAsync`. Os eventos ficam em duas listas públicas (achado M4):
    - `EventosAdicionados`: pendentes desde o último `SalvarAsync`; `DescartarAlteracoes` limpa esta lista;
    - `EventosGravados`: recebem os pendentes só quando `SalvarAsync` termina sem conflito.

    `DescartarAlteracoes` também descarta as alterações de pedidos e produtos feitas desde a última gravação: o falso devolve cópias na leitura com `paraAlterar: true` e só as aplica no `SalvarAsync` bem-sucedido.

  O tratamento de deadlock do `SalvarAsync` muda na US5 (T026). Rodar a suíte: tudo verde.
- [X] T008 Contrato `estoque.insuficiente` (R-06):
  - `src/PedidoFulfillment.Application/Contratos/Eventos/Eventos.cs`: `EstoqueInsuficiente` (`PedidoId`, `Itens`) e `ItemEstoqueInsuficiente` (`ProdutoId`, `QuantidadeSolicitada`, `QuantidadeDisponivel`, `Inativo`), `sealed record` com membros `required`;
  - `src/PedidoFulfillment.Application/Contratos/Eventos/TiposEvento.cs`: `EstoqueInsuficiente = "estoque.insuficiente"` no mapa;
  - `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`: segundo `None Include="..\..\specs\008-estoque\contracts\eventos\*.schema.json" LinkBase="Eventos"`.

  T003 e T004 devem passar.
- [X] T009 Esqueleto do consumidor (R-01, R-09):
  - `src/PedidoFulfillment.Application/Estoque/EventosEstoque.cs`: `LoggerMessage` 6001 `EstoqueReservado`, 6002 `PedidoCanceladoSemEstoque`, 6003 `PedidoIgnoradoNaReserva`, 6004 `ConflitoNaReserva` (Warning) e 6005 `EstoqueDevolvido`, só com identificadores e quantidades;
  - `src/PedidoFulfillment.Application/Estoque/ReservaEstoque.cs`: `public sealed class ReservaEstoque(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<ReservaEstoque> logger) : IManipuladorEvento<PedidoCriado>`, com `Consumidor = "estoque"`, `Tipos = [TiposEvento.PedidoCriado]` e `ManipularAsync` lançando `NotImplementedException`; XML doc citando `contracts/consumidor-estoque.md`.

  Ainda sem registro no Worker (achado M1): um consumidor que só lança mandaria todo `pedido.criado` para novas tentativas e DLQ, inclusive nos testes do REQ-06. Rodar a suíte: tudo verde.

**Checkpoint**: regra no domínio, coluna, porta, contrato e esqueleto do consumidor.

---

## Phase 3: User Story 1 - Pedido com estoque tem a mercadoria reservada (Priority: P1) 🎯 MVP

**Goal**: "pedido criado" com estoque abate tudo e publica `estoque.reservado`, sem mudar o status.

**Independent Test**: criar um pedido com o Worker no ar e conferir o estoque abatido e a mensagem numa fila de teste.

### Testes da US1

- [X] T010 [P] [US1] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`, com o falso:
  - `Consumidor` é `estoque` e `Tipos` é `[pedido.criado]`;
  - pedido `Pendente` com estoque: estoque abatido, `EstoqueReservado`, status `Pendente`, uma chamada a `SalvarAsync` e um `EventoSaida` do tipo `estoque.reservado`, com `idCorrelacao` igual ao pedido e itens ordenados por `produtoId`;
  - os itens vêm do pedido gravado, mesmo que o evento recebido traga outros (FR-003);
  - pedido inexistente: lança `InvalidOperationException` e não grava nada.
- [X] T011 [P] [US1] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Estoque/ReservaEstoqueTestes.cs` (`ColecaoMensageria`), com banco novo migrado, API (`CenarioPedidos.Api`) e Worker (`FabricaWorker`) no mesmo banco, e `FilaTesteAsync("estoque.*")`:
  - cenário 1 da US1: o pedido criado pela API gera `estoque.reservado` em até 5 s, o estoque cai a quantidade pedida, e a consulta mostra `Pendente`;
  - cenário 3: republicar o mesmo `pedido.criado` (mesmo `idEvento`) direto na troca não abate de novo nem gera evento novo.

### Implementação da US1

- [X] T012 [US1] Em `ReservaEstoque.ManipularAsync`: ler o pedido com `paraAlterar: true` (inexistente → `InvalidOperationException` com o id), ler os produtos dos itens com `paraAlterar: true`, chamar `pedido.ReservarEstoque` e, sem faltas, `armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new EstoqueReservado { ... }, agora), agora))`, `SalvarAsync` e log 6001. Registrar o consumidor em `src/PedidoFulfillment.Worker/ProgramaWorker.cs` com `builder.Services.AdicionarConsumidor<ReservaEstoque, PedidoCriado>();` (achado M1). T010 e T011 devem passar.
- [X] T013 [US1] Regressão do REQ-06 (achado M3): com o consumidor ativo, os testes que sobem o Worker passam a reservar estoque e a gravar `estoque.reservado` em `EventosSaida`. Revisar as asserções sobre `EventosSaida` em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/OutboxComBrokerTestes.cs` e `WorkerTestes.cs` (contagem de pendentes, "zerou", total de linhas) e restringir ao tipo `pedido.criado` onde o teste trata só desse evento. Rodar a suíte inteira no checkpoint.

**Checkpoint**: MVP — um pedido com estoque é reservado e anunciado.

---

## Phase 4: User Story 2 - Pedido sem estoque é cancelado e avisado (Priority: P1)

**Goal**: algum item indisponível cancela o pedido sem mexer no estoque e publica `estoque.insuficiente`.

**Independent Test**: reduzir o estoque ou desativar o produto entre a criação e o consumo e conferir pedido `Cancelado`, estoque intacto e a mensagem.

### Testes da US2

- [X] T014 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`:
  - item com estoque menor: nenhum estoque muda; status `Cancelado`, com entrada no histórico (`AutorId` nulo, motivo `Estoque insuficiente.`); um `EventoSaida` `estoque.insuficiente` só com os itens que faltaram;
  - produto inativo: `inativo: true` e `quantidadeDisponivel` com o estoque lido;
  - pedido `Processando` sem reserva e sem estoque: cancela também (clarify 2).
- [X] T015 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Estoque/ReservaEstoqueTestes.cs`, com o Worker parado na criação e iniciado depois:
  - cenário 1 da US2: zerar o estoque de um dos produtos pelo `ContextoBancoDados` antes de subir o Worker; o pedido fica `Cancelado`, nenhum estoque do pedido muda, e chega `estoque.insuficiente` válido;
  - cenário 2: desativar o produto (`Produto.Desativar`) antes de subir o Worker; mesmo resultado, com `inativo: true`;
  - cenário 4: republicar o mesmo `pedido.criado` não muda nada.

### Implementação da US2

- [X] T016 [US2] Em `ReservaEstoque`, com faltas: conferir `TransicoesStatusPedido.PermitidaParaOperacao(status, Cancelado)`, `pedido.MudarStatus(Cancelado, null, "Estoque insuficiente.", agora)`, gravar `estoque.insuficiente` a partir das `FaltaEstoque` (ordenadas por `produtoId`), `SalvarAsync` e log 6002. T014 e T015 devem passar.

---

## Phase 5: User Story 3 - Disputa pelo mesmo produto não vende o que não existe (Priority: P1)

**Goal**: conflitos de versão são reavaliados com o estado atual até 3 vezes; esgotados, a base tenta de novo.

**Independent Test**: N unidades e M > N pedidos simultâneos terminam com N reservados, M − N cancelados e estoque 0.

### Testes da US3

- [X] T017 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`, com o falso programado:
  - um conflito e depois sucesso: duas leituras, `DescartarAlteracoes` uma vez, um único evento;
  - conflito, e na releitura o estoque já não basta: termina em cancelamento;
  - conflito nas 3 vezes: relança `ConflitoConcorrenciaException`, sem evento pendente no falso, e loga 6004 três vezes.
- [X] T018 [P] [US3] Teste de integração `tests/PedidoFulfillment.Testes.Integracao/Estoque/DisputaEstoqueTestes.cs` (R-10), com banco novo migrado e os pedidos criados pela API:
  - helper que executa `ReservaEstoque` como a base: escopo novo do `FabricaWorker.Services`, `BeginTransactionAsync`, `ManipularAsync`, `SaveChangesAsync` e commit; em exceção, rollback e nova execução até 4 vezes no total;
  - `CAT-008` (estoque 1) com 2 pedidos de 1 unidade em paralelo (`Task.WhenAll`): exatamente 1 com `EstoqueReservado`, 1 `Cancelado`, estoque 0;
  - produto criado no teste com estoque 5 e 8 pedidos de 1 unidade em paralelo: 5 reservados, 3 cancelados, estoque 0; repetir o cenário 5 vezes na mesma execução (SC-002);
  - conflito forçado: alterar o produto por outro contexto entre a leitura e a gravação (com um `IArmazenamentoPedidos` decorado só no teste) e conferir que a segunda tentativa grava uma única reserva dentro da mesma transação (savepoint, R-03).

### Implementação da US3

- [X] T019 [US3] Em `ReservaEstoque`, envolver ler-decidir-gravar num laço de até 3 reavaliações (constante `Reavaliacoes = 3`, como `TentativasStatus` do `ServicoPedidos`): em `ConflitoConcorrenciaException`, `DescartarAlteracoes()`, log 6004 com o número da reavaliação e nova volta; na última, relançar. T017 e T018 devem passar.

---

## Phase 6: User Story 4 - Pedido que mudou de status antes da reserva (Priority: P2)

**Goal**: status final ou pedido já reservado é ignorado; `Processando` e `ProntoParaEnvio` sem reserva são reservados.

**Independent Test**: com o Worker parado, cancelar um pedido e mover outro para `Processando`; ao subir o Worker, só o segundo é reservado.

### Testes da US4

- [ ] T020 [P] [US4] Em `tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`:
  - `Cancelado`, `Despachado` e `PagamentoRejeitado` (theory): nenhum estoque muda, nenhum evento, nenhum `SalvarAsync`, log 6003 com o status;
  - `EstoqueReservado` já marcado: mesmo resultado, log 6003 com "já reservado";
  - `Processando` e `ProntoParaEnvio` sem reserva (theory): reserva, status não muda.
- [ ] T021 [P] [US4] Em `tests/PedidoFulfillment.Testes.Integracao/Estoque/ReservaEstoqueTestes.cs`, com o Worker parado: criar dois pedidos, cancelar o primeiro como Cliente e mover o segundo para `Processando` como Admin; subir o Worker e conferir que só o segundo gera `estoque.reservado`, que o estoque do primeiro não muda, e que nenhuma mensagem dele vai para `oms.estoque.dlq`. Cenário 4 da US4 (achado M2): publicar direto na troca um `pedido.criado` válido com um `pedidoId` inexistente e conferir que, depois das 4 execuções (intervalos de 100, 200 e 400 ms do `FabricaWorker`), ele chega a `oms.estoque.dlq` com o motivo.

### Implementação da US4

- [ ] T022 [US4] Em `ReservaEstoque`, antes da leitura dos produtos: status final ou `EstoqueReservado` → log 6003 e retorno sem gravar. T020 e T021 devem passar.

---

## Phase 7: User Story 5 - Cancelar um pedido reservado devolve o estoque (Priority: P2)

**Goal**: o cancelamento pela API de um pedido com reserva devolve o estoque na mesma gravação.

**Independent Test**: reservar, cancelar pela API e conferir o estoque de volta; cancelar sem reserva e conferir o estoque intacto.

### Testes da US5

- [ ] T023 [P] [US5] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosStatusTestes.cs`:
  - Cliente cancela pedido `Pendente` reservado: estoque devolvido, `EstoqueReservado` falso, resposta igual à de hoje, log 6005;
  - operação cancela pedido `Processando` reservado: idem;
  - cancelamento sem reserva: nenhum produto lido para alteração, estoque intacto;
  - outra transição (por exemplo `Processando`) com reserva: estoque intacto;
  - conflito na primeira gravação: relê pedido e produtos e devolve uma vez só.
- [ ] T024 [P] [US5] Teste de integração `tests/PedidoFulfillment.Testes.Integracao/Estoque/DevolucaoEstoqueTestes.cs`, com API e Worker:
  - cenário 1: reservar, cancelar como Cliente (`CenarioPedidos.MudarStatusAsync`), 200, e estoque igual ao de antes da reserva;
  - cenário 2: reservar, mover para `Processando` e cancelar como operação; estoque devolvido;
  - cenário 3: cancelar com o Worker parado e subir depois; estoque intacto e nenhum evento de estoque;
  - cenário 4: com o produto de estoque 1, rodar em paralelo o cancelamento pela API e a reserva (helper da T018), 10 vezes; em todas, ou o pedido não foi reservado e o estoque continua 1, ou foi reservado e devolvido, e o estoque volta a 1;
  - cenário 5: reservar, desativar o produto e cancelar; estoque devolvido e produto continua inativo.

### Implementação da US5

- [ ] T025 [US5] Em `src/PedidoFulfillment.Application/Pedidos/ServicoPedidos.cs`, dentro do laço de `AtualizarStatusAsync`: se o novo status é `Cancelado` e `pedido.EstoqueReservado`, ler os produtos dos itens com `paraAlterar: true` e chamar `pedido.DevolverEstoque` antes do `SalvarAsync`; depois do sucesso, log 6005 (`EventosEstoque`). Atualizar o XML doc do método (R-05). T023 deve passar.
- [ ] T026 [US5] Em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, `SalvarAsync` (R-07): deadlock com `RegistroIdempotencia` em estado `Added` → `ChaveIdempotenciaRegistradaException` (como hoje); deadlock sem transação aberta → `ConflitoConcorrenciaException`; com transação aberta → relança sem conversão. Comentário citando R-07. T024 deve passar.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T027 [P] Atualizar `README.md`:
  - **Estado atual**: linha do REQ-08;
  - **Próximos passos**: tirar a #8; na #9, citar que a compensação reaproveita `Pedido.DevolverEstoque` e a marca `EstoqueReservado`;
  - **Mensageria**: a fila `oms.estoque` e os eventos `estoque.reservado` e `estoque.insuficiente`;
  - **Roteiros de validação**: o quickstart do REQ-08.
- [ ] T028 [P] Em `specs/006-mensageria/contracts/topologia-e-consumo.md`, na seção "Para escrever um worker", citar o consumidor de estoque como exemplo (`specs/008-estoque/contracts/consumidor-estoque.md`), sem mudar o contrato.
- [ ] T029 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados).
- [ ] T030 Executar o quickstart (§1 a §7) e registrar em Notes. Conferir nos logs da API e do Worker os EventIds 6001 (reserva), 6002 (cancelamento sem estoque) e 6005 (devolução), que não têm asserção nos testes (achado L3).
- [ ] T031 Fazer push e abrir o PR com `Closes #8`. Acompanhar o CI (`build-testes`, com os dois jobs) até ficar verde e registrar os tempos em Notes.

---

## Dependencies & Execution Order

- **Setup → Foundational**: a fundação precisa da linha de base.
- **US1**: depende da fundação inteira (T005 a T009).
- **US2, US3 e US4**: completam o `ReservaEstoque.cs` depois da US1, nessa ordem, porque
  editam o mesmo arquivo e os mesmos arquivos de teste.
- **US5**: depende da fundação (T005, T007) e, para o teste de integração, da US1 (reserva
  pelo Worker) e do helper da T018. A T025 pode correr em paralelo com as US2 a US4.
- **Polish**: depois de tudo. A T031 exige push.

### Parallel Opportunities

- T002, T003 e T004; T010 e T011; T014 e T015; T017 e T018; T020 e T021; T023 e T024;
  T027 e T028.
- T025 com as fases 4 a 6 (arquivos diferentes).

---

## Implementation Strategy

### MVP

Setup, Foundational e US1: o pedido com estoque é reservado e `estoque.reservado` chega ao
broker.

### Incremental Delivery

1. Setup + Foundational → commit `feat(estoque): adiciona regra de reserva, contrato e consumidor de estoque [REQ-08]`.
2. US1 → commit `feat(estoque): reserva estoque do pedido criado [REQ-08]`.
3. US2 → commit `feat(estoque): cancela pedido sem estoque [REQ-08]`.
4. US3 → commit `feat(estoque): reavalia reserva em conflito de concorrencia [REQ-08]`.
5. US4 → commit `feat(estoque): ignora pedido em status final na reserva [REQ-08]`.
6. US5 → commit `feat(pedidos): devolve estoque no cancelamento [REQ-08]`.
7. Polish → `docs(estoque): ... [REQ-08]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- A devolução na recusa do pagamento é da issue #9, que reaproveita `DevolverEstoque`.
- **T001** (2026-10-22): linha de base com 485 testes verdes (262 unitários e 223 de
  integração), nenhum ignorado.
- Desvios registrados na implementação:
  - **T006**: a configuração não usa `HasDefaultValue(false)`. A migration gera
    `defaultValue: false` para a coluna nova de qualquer jeito, e sem a configuração o EF
    não trata o campo como gerado pelo banco. `ModeloSincronizadoTestes` também fixava a
    lista de migrations e foi atualizado.
  - **T015**: num broker novo, o despachante pode publicar `pedido.criado` antes de o
    consumidor declarar `oms.estoque`, e a mensagem sem rota é descartada (comportamento do
    REQ-06, `mandatory: false`). Os testes que sobem o Worker depois da criação declaram a
    fila antes (`CenarioEstoque.DeclararFilaEstoqueAsync`). Em produção, a fila durável já
    existe depois da primeira partida; o risco fica só na primeira subida de um broker vazio
    com eventos pendentes. Registrado para o PR (Pontos de atenção).
  - **T009**: o esqueleto referencia as dependências do construtor só para compilar, porque
    `TreatWarningsAsErrors` recusa parâmetro não lido (CS9113); a T012 remove isso.
- **SC-001** (95% em 5 s) é verificado por amostra: um pedido no cenário 1 da T011, como
  no REQ-06 (achado L2).
- **US1, cenário 4** (falha no meio da reserva): sem teste com o banco caindo. É coberto
  pela atomicidade da transação da base (efeito, evento e processado no mesmo commit) e
  pelos testes de conflito esgotado (T017, T018), que conferem que nada fica gravado
  (achado L4).
- `/speckit-analyze` (2026-10-22), achados aplicados:
  - **M1**: o registro do consumidor no Worker saiu da T009 e foi para a T012;
  - **M2**: pedido inexistente chega à DLQ, na T021;
  - **M3**: T013 nova, para a regressão dos testes do REQ-06 com o Worker;
  - **M4**: o falso separa eventos pendentes de gravados e descarta alterações (T007);
  - **L1**: SC-007 no cabeçalho;
  - **L2** e **L4**: registrados acima;
  - **L3**: EventIds 6001, 6002 e 6005 conferidos na T030;
  - **L5**: motivo `Estoque insuficiente.` fixado na spec (FR-006).
- Fazer commit ao fim de cada fase.
