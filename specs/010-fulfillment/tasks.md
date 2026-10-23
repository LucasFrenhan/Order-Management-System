---

description: "Lista de tarefas do REQ-10 — Ordem de entrega assíncrona para pedidos pagos"
---

# Tasks: Ordem de entrega assíncrona para pedidos pagos

**Input**: Design documents from `/specs/010-fulfillment/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/consumidor-entrega.md](./contracts/consumidor-entrega.md),
[contracts/eventos/entrega.criada.v1.schema.json](./contracts/eventos/entrega.criada.v1.schema.json),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio (criação
da ordem, decisão do consumidor) e testes de serialização e schema para o contrato novo. A
spec pede banco e broker reais para o consumidor (FR-013). Os testes de cada fase vêm antes
da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A fundação traz:
- a regra da ordem de entrega no `Pedido`;
- o tipo do contrato `entrega.criada` (record e catálogo) e a cópia do schema para os testes
  de integração;
- a leitura do pedido com a ordem;
- o esqueleto do consumidor e os logs. O registro do consumidor no Worker fica para a US1,
  como no REQ-08 e no REQ-09.

A US1 e a US3 completam o mesmo manipulador (`CriacaoOrdemEntrega.cs`), por isso correm em
sequência. A US2 (testes do contrato) só depende da fundação e pode correr em paralelo com a
US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Base de consumo do REQ-06, sobre RabbitMQ.Client nativo, sem MassTransit.
- Nomes em português; evento no passado (`entrega.criada`); consumidor e fila `entrega`
  (`oms.entrega`, R-01).
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo e nenhuma migration (R-10).
- Contrato HTTP v1 e contratos de eventos existentes intocados (FR-010, FR-012).
- Estoque e pagamento não são alterados pelo fulfillment (FR-006).
- Os testes com banco ou broker reais usam Testcontainers e respeitam
  `OMS_EXIGIR_DOCKER` (REQ-07); os de mensageria entram na `ColecaoMensageria`.
- Nenhuma espera fixa em teste: espere por condição, com prazo
  (`MensageriaTeste.EsperarAteAsync`).
- Nos testes de integração, o Worker completo roda com
  `GatewayPagamento:TaxaAprovacaoPercentual = 100` (padrão da `FabricaWorker`).
- Commits: `tipo(escopo): descrição [REQ-10]`.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-10-fulfillment` e registrar em Notes a contagem de testes como linha de base (596 no fim do REQ-09: 346 unitários e 250 de integração; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T002 [P] Testes de domínio em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoOrdemEntregaTestes.cs` (data-model, R-02), com o pedido reservado pelo `ReservarEstoque` do REQ-08 e pago pelo `AprovarPagamento` do REQ-09 (como em `PedidoPagamentoTestes`):
  - `AptoParaOrdemEntrega`: verdadeiro com reserva, pagamento `Aprovado`, sem ordem e em `Processando` ou `ProntoParaEnvio` (theory); falso em `Pendente`, `Cancelado`, `Despachado` e `PagamentoRejeitado` (theory), sem reserva, sem pagamento, com pagamento `Recusado` e com ordem já criada;
  - `CriarOrdemEntrega` em `Processando`: devolve a ordem e a guarda em `OrdemEntrega`, com `PedidoId = Id`, `CriadoEm = AtualizadoEm = instante`, `Transportadora`, `CodigoRastreio` e `DespachadoEm` nulos; status `ProntoParaEnvio`; histórico com `AutorId` nulo, de `Processando` para `ProntoParaEnvio`, com motivo `Ordem de entrega criada.`; `EstoqueReservado` continua verdadeiro e o pagamento não muda;
  - `CriarOrdemEntrega` em `ProntoParaEnvio`: ordem criada, status e histórico sem mudança, `AtualizadoEm = instante`;
  - pedido não apto (cada caso do falso acima): `InvalidOperationException` sem alterar nada (ordem nula, status, histórico e `AtualizadoEm` iguais);
  - chamar duas vezes: a segunda lança `InvalidOperationException` e a ordem continua a primeira.

### Implementação da fundação

- [X] T003 Em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`, ao lado do pagamento: `const string MotivoOrdemEntregaCriada = "Ordem de entrega criada."`, `bool AptoParaOrdemEntrega => EstoqueReservado && Pagamento?.Situacao == SituacaoPagamento.Aprovado && OrdemEntrega is null && Status is StatusPedido.Processando or StatusPedido.ProntoParaEnvio` e `OrdemEntrega CriarOrdemEntrega(DateTimeOffset instante)`: fora de apto, `InvalidOperationException` com o número, o status e as condições (como `NovoPagamento`); senão `OrdemEntrega = new OrdemEntrega(Id, instante)` e, em `Processando`, `MudarStatus(StatusPedido.ProntoParaEnvio, null, MotivoOrdemEntregaCriada, instante)`; em `ProntoParaEnvio`, `AtualizadoEm = instante` (a versão do pedido precisa mudar, R-04). XML doc citando R-02, R-03 e R-04. T002 deve passar.
- [X] T004 [P] Contrato (R-07):
  - em `src/PedidoFulfillment.Application/Contratos/Eventos/Eventos.cs`, `sealed record EntregaCriada` (`required Guid PedidoId`, `required string Numero`, `required Guid OrdemEntregaId`, `required IReadOnlyList<ItemEntregaCriada> Itens`) e `sealed record ItemEntregaCriada` (`required Guid ProdutoId`, `required int Quantidade`), com XML doc `<c>entrega.criada</c>: publicado pelo worker de entrega (issue #10)`;
  - em `src/PedidoFulfillment.Application/Contratos/Eventos/TiposEvento.cs`, `public const string EntregaCriada = "entrega.criada";` e a entrada em `TipoDados`; o XML doc da classe cita o REQ-10;
  - em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`, a linha `<None Include="..\..\specs\010-fulfillment\contracts\eventos\*.schema.json" LinkBase="Eventos" CopyToOutputDirectory="PreserveNewest" />`, ao lado das do 006 e do 008.
- [X] T005 [P] Leitura com a ordem (R-05): em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, `ObterPedidoAsync` passa a incluir `.Include(p => p.OrdemEntrega)`, e o XML doc de `IArmazenamentoPedidos.ObterPedidoAsync` (`src/PedidoFulfillment.Application/Pedidos/IArmazenamentoPedidos.cs`) cita a ordem de entrega (REQ-10). Rodar a suíte: tudo verde.
- [X] T006 Esqueleto do consumidor (R-01, R-08):
  - `src/PedidoFulfillment.Application/Entregas/EventosEntrega.cs`: `internal static partial class EventosEntrega` com `LoggerMessage` 8001 `OrdemEntregaCriada` (Information: pedido, ordem, status mudou, evento), 8002 `PedidoIgnoradoNaEntrega` (Information: pedido, status, estoque reservado, pagamento aprovado, já tem ordem, evento) e 8003 `ConflitoNaEntrega` (Warning: pedido, reavaliação, evento), só com identificadores e situação (FR-011);
  - `src/PedidoFulfillment.Application/Entregas/CriacaoOrdemEntrega.cs`: `public sealed class CriacaoOrdemEntrega(IArmazenamentoPedidos armazenamento, TimeProvider relogio, ILogger<CriacaoOrdemEntrega> logger) : IManipuladorEvento<PagamentoAprovado>`, com `Consumidor = "entrega"`, `Tipos = [TiposEvento.PagamentoAprovado]` e `ManipularAsync` lançando `NotImplementedException`; XML doc citando `specs/010-fulfillment/contracts/consumidor-entrega.md`.

  Ainda sem registro no Worker. Rodar a suíte: tudo verde.

**Checkpoint**: regra no domínio, tipo do contrato, leitura com a ordem e esqueleto do consumidor.

---

## Phase 3: User Story 1 - Pedido pago ganha a ordem de entrega e fica pronto para envio (Priority: P1) 🎯 MVP

**Goal**: "pagamento aprovado" para um pedido em `Processando` cria a ordem sem dados de despacho, muda para `ProntoParaEnvio` e publica `entrega.criada`, numa única gravação.

**Independent Test**: com a taxa em 100%, criar um pedido com estoque e conferir o pedido em `ProntoParaEnvio`, a ordem sem dados de despacho e a mensagem numa fila de teste.

### Testes da US1

- [X] T007 [P] [US1] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Entregas/CriacaoOrdemEntregaTestes.cs`, com o armazenamento falso de `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` (a fábrica registrada cria o pedido reservado e pago a cada leitura, como nos testes do REQ-09):
  - `Consumidor` é `entrega` e `Tipos` é `[pagamento.aprovado]`;
  - pedido `Processando` apto: ordem criada; status `ProntoParaEnvio`; nenhum produto lido; uma chamada a `SalvarAsync`; um `EventoSaida` `entrega.criada` com `idCorrelacao` igual ao pedido e `dados` com o id e o número do pedido, o id da ordem criada e os itens do pedido (produto e quantidade, na ordem dos itens); log 8001;
  - pedido inexistente: `InvalidOperationException` com o id, nada gravado.
- [X] T008 [P] [US1] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Entregas/OrdemEntregaCriadaTestes.cs` (`ColecaoMensageria`), com o helper novo `tests/PedidoFulfillment.Testes.Integracao/Entregas/CenarioEntrega.cs`:
  - helper: `FilaEntrega = "entrega"`; `WorkerProntoAsync` limpa as filas de `estoque`, `pagamento` e `entrega` (`LimparFilasConsumidorAsync`), sobe o Worker completo (taxa 100, logs opcionais) e espera os três consumidores ligados (`ConsumidorLigadoAsync`, 10 s), como `CenarioPagamento.WorkerProntoAsync`; consultas `OrdensAsync` (`COUNT(*)` em `OrdensEntrega` do pedido), `OrdemAsync` (id, transportadora, rastreio e despacho da ordem), contagem de `EventosSaida` `entrega.criada` do pedido; `PublicarPagamentoAprovadoAsync` (direto na troca, `idEvento` novo ou dado) e `RepublicarPagamentoAprovadoAsync` (o corpo gravado no outbox, mesmo `idEvento`), como os de `CenarioPagamento`;
  - cenários 1 a 3: o pedido criado pela API (`CenarioPedidos.Api`) termina em `ProntoParaEnvio` em até 10 s (SC-005), com uma linha em `OrdensEntrega` sem transportadora, rastreio e despacho, a entrada `Ordem de entrega criada.` sem autor no histórico (`CenarioPagamento.UltimoMotivoSemAutorAsync`) e `entrega.criada` na `FilaTesteAsync("entrega.criada")` válido pelo schema (`SchemaEventosTestes`), com o pedido, o número, o id da ordem gravada e os itens do pedido;
  - cenário 4: republicar o mesmo `pagamento.aprovado` (mesmo `idEvento`) direto na troca não cria ordem nem evento novo, e o status continua `ProntoParaEnvio`;
  - estoque e pagamento iguais aos de antes do fulfillment (FR-006).

### Implementação da US1

- [X] T009 [US1] Em `CriacaoOrdemEntrega.ManipularAsync`: ler o pedido com `paraAlterar: true` (inexistente → `InvalidOperationException` com o id, FR-003), `var ordem = pedido.CriarOrdemEntrega(agora)`, `armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new EntregaCriada { PedidoId, Numero, OrdemEntregaId = ordem.Id, Itens = pedido.Itens.Select(...) }, agora), agora))`, `SalvarAsync` e log 8001. No Worker (`src/PedidoFulfillment.Worker/ProgramaWorker.cs`), `builder.Services.AdicionarConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>();` depois do pagamento. T007 e T008 devem passar.
- [X] T010 [US1] Isolamento e regressão (R-09):
  - `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/CenarioPagamento.cs`: o Worker do pagamento remove o `IHostedService` `ConsumidorEventos<CriacaoOrdemEntrega, PagamentoAprovado>` pelo `Servicos` (combinando com o registro de logs), com comentário citando R-09 do REQ-10;
  - `tests/PedidoFulfillment.Testes.Integracao/Estoque/CenarioEstoque.cs`: o Worker do estoque também remove o consumidor de entrega;
  - revisar os testes do REQ-06 que sobem o Worker (`Mensageria/OutboxComBrokerTestes.cs`, `WorkerTestes.cs`, `ConsumoComNovaTentativaTestes.cs`) e restringir ao tipo tratado as asserções sobre `EventosSaida` ou filas que passariam a contar `entrega.criada`.

  Rodar a suíte inteira no checkpoint.

**Checkpoint**: MVP — um pedido pago ganha a ordem e é anunciado à logística.

---

## Phase 4: User Story 2 - Contrato "entrega criada" publicado e verificável (Priority: P1)

**Goal**: `entrega.criada` v1 com schema no repositório, serialização em camelCase, ida e volta e recusa de mensagem fora do contrato (FR-008, FR-009, SC-004).

**Independent Test**: serializar um evento de exemplo, validar contra o schema e conferir que exemplos inválidos são recusados.

- [X] T011 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SerializacaoEventosTestes.cs`, como para `estoque.insuficiente`: fábrica `internal static EnvelopeEvento<EntregaCriada> EntregaCriada()`; `Tipo` é `entrega.criada` e `TiposEvento.EntregaCriada` é `"entrega.criada"`; ida e volta igual (`DeveVoltarIgual`, comparando os itens); JSON em camelCase com `tipo`, `versao` 1 e `dados.pedidoId`, `dados.numero`, `dados.ordemEntregaId` e `dados.itens[0].produtoId`/`quantidade`.
- [X] T012 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`: `JsonEntregaCriada()` com número no padrão `PED-AAAAMMDD-NNNNNN` e dois itens, incluído no theory de eventos válidos; theory `Entrega_criada_fora_do_contrato_e_recusada` com as alterações: sem `ordemEntregaId`, `itens` vazio, `quantidade` 0, número fora do padrão, campo extra em `dados`, campo extra no item e `tipo` diferente (US2 cenário 2). O XML doc da classe cita `specs/010-fulfillment/contracts/eventos/`.

Sem implementação nova: o contrato é da T004 e a publicação, da T009 (US2 cenário 3 coberto na T008).

---

## Phase 5: User Story 3 - Pedido que mudou antes do fulfillment (Priority: P2)

**Goal**: só pedidos aptos ganham ordem; os demais são confirmados sem gravar, com log; conflitos são reavaliados com o estado atual.

**Independent Test**: cancelar um pedido pago pela operação e publicar o `pagamento.aprovado` dele; nenhuma ordem, status `Cancelado` e nenhum `entrega.criada`.

### Testes da US3

- [X] T013 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Entregas/CriacaoOrdemEntregaTestes.cs`:
  - `Pendente`, `Cancelado`, `Despachado` e `PagamentoRejeitado` (theory), pedido sem reserva, sem pagamento aprovado e já com ordem: nenhum `SalvarAsync`, nenhum evento, log 8002 com as condições;
  - pedido em `ProntoParaEnvio` apto: ordem criada, status sem mudança, `entrega.criada` publicado, log 8001 indicando que o status não mudou;
  - um conflito e depois sucesso: duas leituras, `DescartarAlteracoes` uma vez, um único evento, log 8003 com reavaliação 1;
  - conflito e, na releitura, o pedido foi cancelado (sem reserva): termina sem gravar, log 8002;
  - conflito e, na releitura, o pedido foi movido para `ProntoParaEnvio` pela operação: ordem criada sem mudança de status;
  - conflito nas 3 vezes: relança `ConflitoConcorrenciaException`, sem evento pendente no falso e log 8003 três vezes.
- [X] T014 [P] [US3] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Entregas/PedidoIgnoradoNaEntregaTestes.cs`, com o Worker completo pronto (`CenarioEntrega.WorkerProntoAsync`):
  - cenário 1: com o Worker do pagamento (`CenarioPagamento`, sem o consumidor de entrega), deixar o pedido chegar a `Processando` e parar; cancelar pela API como operação (estoque devolvido, REQ-08); subir o Worker completo e só então publicar direto na troca o `pagamento.aprovado` dele, com `idEvento` novo: nenhuma ordem, status `Cancelado`, nenhum `entrega.criada`, nada em `oms.entrega.dlq` e log 8002;
  - cenário 1, `Despachado`: mesmo roteiro, movendo o pedido pela API (`PATCH` como operação) para `ProntoParaEnvio` e depois `Despachado` (transições permitidas pelo REQ-05); nenhuma ordem e status `Despachado`;
  - cenário 2: pedido que já tem ordem (criada pelo fluxo normal) recebe outro `pagamento.aprovado` com `idEvento` novo: nenhuma ordem nova, nenhum evento, log 8002;
  - cenário 3: pedido movido pela operação para `ProntoParaEnvio` com o Worker sem o consumidor de entrega; ao publicar o `pagamento.aprovado`, a ordem é criada, `entrega.criada` sai, e o histórico não ganha entrada nova;
  - cenário 4: `pagamento.aprovado` válido com `pedidoId` inexistente chega a `oms.entrega.dlq` depois das 4 execuções (100, 200 e 400 ms);
  - cenário 5: rodar 10 vezes, em paralelo, o cancelamento pela operação (`PATCH`) e o fulfillment executado como a base (helper como `DisputaEstoqueTestes.ReservarComoABaseAsync`), com pedidos pagos pelo Worker sem o consumidor de entrega. Em todas as rodadas, o pedido termina `Cancelado` com o estoque devolvido uma vez só, e vale um de dois resultados:
    - o cancelamento veio antes: nenhuma ordem e nenhum `entrega.criada` no outbox;
    - o fulfillment veio antes: uma ordem, um `entrega.criada`, e o histórico tem `ProntoParaEnvio` antes de `Cancelado`.

    Nunca `entrega.criada` sem a ordem, nem duas ordens.

### Implementação da US3

- [X] T015 [US3] Em `CriacaoOrdemEntrega` (R-04):
  - depois da leitura, `!pedido.AptoParaOrdemEntrega` → log 8002 e retorno sem gravar (FR-002);
  - envolver ler-decidir-gravar num laço de até 3 reavaliações (`Reavaliacoes = 3`, como `ProcessamentoPagamento`): em `ConflitoConcorrenciaException`, `DescartarAlteracoes()`, log 8003 e nova volta; na última, relançar (FR-007).

  T013 e T014 devem passar.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T016 [P] Atualizar `README.md` (inclui, em Mensageria, que pedidos pagos antes de a fila `oms.entrega` existir ficam sem ordem, com link para a pendência em `specs/006-mensageria/spec.md`):
  - **Estado atual**: linha do REQ-10 (consumidor `entrega`, ordem sem dados de despacho, `ProntoParaEnvio` e `entrega.criada`);
  - **Próximos passos**: tirar a #10; na #11, citar que o despacho parte da ordem criada aqui;
  - **Mensageria**: a fila `oms.entrega` e o evento `entrega.criada`;
  - **Status do pedido**: o fluxo automático termina em `ProntoParaEnvio`;
  - **Roteiros de validação**: o quickstart do REQ-10.
- [X] T017 [P] Em `specs/006-mensageria/contracts/topologia-e-consumo.md`, `entrega.criada` no catálogo de eventos (publicado pelo worker de entrega, #10, com link para o schema do 010) e o consumidor de entrega ao lado dos de estoque e pagamento. Em `specs/009-pagamento/quickstart.md`, nota no §3: com o Worker completo, o pedido aprovado segue para `ProntoParaEnvio` (REQ-10).
- [X] T021 [P] Desempenho em `tests/PedidoFulfillment.Testes.Integracao/Entregas/DesempenhoEntregaTestes.cs` (`ColecaoMensageria`, Worker completo com `CenarioEntrega.WorkerProntoAsync`; SC-001 e SC-005):
  - 3 pedidos de aquecimento;
  - 20 pedidos criados pela API, em sequência, com produtos em estoque; para cada um, esperar `ProntoParaEnvio` com a ordem (prazo de 15 s);
  - pelos instantes gravados no banco: do pagamento aprovado à criação da ordem (SC-001) e da criação do pedido à criação da ordem (SC-005);
  - percentil 95 abaixo de 5 s e de 10 s, respectivamente; os valores vão para a saída do teste e para Notes.
- [X] T018 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados).
- [X] T019 Executar o quickstart (§1 a §6) e registrar em Notes, com os percentis da T021. Conferir nos logs do Worker os EventIds 8001 e 8002.
- [ ] T020 Fazer push e abrir o PR com `Closes #10`. Acompanhar o CI (`build-testes`, com os dois jobs) até ficar verde e registrar os tempos em Notes.

---

## Dependencies & Execution Order

- **Setup → Foundational**: a fundação precisa da linha de base.
- **US1**: depende da fundação inteira (T003 a T006).
- **US2**: depende da T004; pode correr em paralelo com a US1.
- **US3**: completa o `CriacaoOrdemEntrega.cs` depois da US1, porque edita o mesmo arquivo e
  os mesmos arquivos de teste; o teste de integração usa o isolamento da T010.
- **Polish**: depois de tudo. A T020 exige push.

### Parallel Opportunities

- T002 com T004 e T005; T007 e T008; T011 e T012 (e a US2 inteira com a US1); T013 e T014;
  T016, T017 e T021 (a T021 roda antes da T018, que a inclui na suíte).

---

## Implementation Strategy

### MVP

Setup, Foundational e US1: o pedido pago ganha a ordem, vai para `ProntoParaEnvio`, e
`entrega.criada` chega ao broker.

### Incremental Delivery

1. Setup + Foundational → commit `feat(fulfillment): adiciona regra da ordem de entrega, contrato entrega.criada e consumidor [REQ-10]`.
2. US1 → commit `feat(fulfillment): cria a ordem de entrega do pedido pago [REQ-10]`.
3. US2 → commit `test(fulfillment): valida o contrato entrega.criada [REQ-10]`.
4. US3 → commit `feat(fulfillment): ignora pedido nao apto e reavalia em conflito [REQ-10]`.
5. Polish → `docs(fulfillment): ... [REQ-10]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Despacho, transportadora, rastreio, exposição da ordem na consulta e cancelamento da ordem
  ficam fora do escopo (issue #11; spec, Assumptions).
- A branch carrega o commit `9a6e17a` (CI verde do PR #23, T026 do REQ-09), que não chegou à
  `main`; ele vai junto no PR deste REQ.
- **T001 (linha de base, 2026-10-23)**: 596 testes, 346 unitários e 250 de integração, zero
  ignorados, com o Docker no ar.
- **T006, desvio**: o esqueleto com `NotImplementedException` não compila com
  `TreatWarningsAsErrors` (CS9113: parâmetros do construtor primário não lidos). O caminho
  feliz entrou direto na T009, depois dos testes da T007.
- **US2 junto com a fundação**: o teste `Cada_contrato_tem_o_seu_tipo` conta o catálogo e
  reprova assim que `entrega.criada` entra em `TiposEvento` (T004). Os testes da US2 (T011 e
  T012) foram no mesmo commit da fundação, para nenhum commit ficar vermelho.
- **T021 (2026-10-23, local)**: 20 pedidos medidos, depois de 3 de aquecimento. O p95 do
  pagamento aprovado ao envio de `entrega.criada` foi de 200 ms (limite de 5 s, SC-001), e o
  da criação do pedido ao envio, de 383 ms (limite de 10 s, SC-005). Os máximos foram 200 ms
  e 445 ms.
- **T018 (2026-10-23)**: varredura de segredos sem achados (só as credenciais de
  desenvolvimento já documentadas); `dotnet format --verify-no-changes` limpo; suíte local com
  650 testes (383 unitários e 267 de integração), zero ignorados com o Docker no ar. São 54 a
  mais que a linha de base.
- **T019 (2026-10-23, quickstart)**:
  - §1: suíte verde, como na T018;
  - §2: API e Worker locais, com a taxa em 100%; `oms.entrega`, `oms.entrega.dlq` e as três
    filas de espera criadas na partida;
  - §3: o pedido `PED-20261023-000151` chegou a `ProntoParaEnvio` em cerca de 2,8 s (medidos
    pelo `GET`, com consulta a cada 300 ms). Ele tem uma ordem sem transportadora, rastreio e
    data de despacho, e o histórico `Pendente → Processando → ProntoParaEnvio` sem autor, com
    `Ordem de entrega criada.`. `teste.entrega` recebeu `entrega.criada` com o pedido, o
    número, a ordem e os itens; log 8001;
  - §4: o mesmo `pagamento.aprovado` republicado pelo painel foi descartado como duplicado
    (log 5005); continuam uma ordem, um evento e `ProntoParaEnvio`;
  - §5: coberto pelos testes de `PedidoIgnoradoNaEntregaTestes`, que conferem o log 8002;
  - §6: `ConformidadeOpenApi` verde (13 testes).

  Uma primeira republicação saiu sem corpo, por erro no script do roteiro. Ela foi para
  `oms.entrega.dlq` sem novas tentativas (log 5004, "corpo vazio"), como a base prevê para
  mensagem fora do contrato. Essa mensagem ficou na DLQ do broker local.
