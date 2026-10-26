---

description: "Lista de tarefas do REQ-15 — Visibilidade dos cancelamentos e dos estornos para outros sistemas"
---

# Tasks: Visibilidade dos cancelamentos e dos estornos para outros sistemas

**Input**: Design documents from `/specs/015-visibilidade-cancelamentos/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/eventos-cancelamento-e-estorno.md](./contracts/eventos-cancelamento-e-estorno.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio e testes
de serialização e conformidade de schema dos eventos, e o FR-013 pede testes de integração,
com banco e broker reais, de cada caminho de cancelamento, da confirmação do estorno, das
corridas e da falha na gravação. Os testes de cada fase vêm antes da implementação e devem
falhar primeiro.

**Organization**: por história de usuário, depois de uma fundação com o contrato dos dois
tipos novos. A US1 (`pedido.cancelado` em todo cancelamento) é o MVP. A US2
(`pagamento.estornado`) depende só da fundação. A US3 (confiança no contrato) depende da US1 e
da US2 para os testes de ponta a ponta. A guarda do R-03 vem por último, quando todos os
caminhos já gravam o evento; antes disso, ela quebraria os caminhos ainda não migrados.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Nomes em português; o evento é montado no Application; o domínio só devolve o que o
  cancelamento fez; o Infrastructure só ganha a guarda.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo e nenhuma migration (R-07).
- Contratos v1: os tipos existentes, os schemas existentes e o contrato HTTP não mudam
  (FR-009, FR-011). `ConformidadeOpenApiTestes` e o oasdiff continuam verdes sem edição.
- Eventos sem dados pessoais, sem `clienteId`, sem autor e sem o texto livre do motivo
  (FR-010). Os identificadores do gateway vão no `pagamento.estornado` e continuam fora dos
  logs (clarify Q2).
- Nenhum log novo (R-08); os IDs de log existentes não mudam.
- Os testes existentes do REQ-05 ao REQ-14 passam sem afrouxar asserções.
- Os testes com banco e broker reais usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER`
  (REQ-07). Nenhuma espera fixa em teste: espere por condição, com prazo.
- Commits: `tipo(escopo): descrição [REQ-15]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-15-visibilidade-cancelamentos` e registrar em Notes a contagem de testes como linha de base (unitários, integração e ignorados; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: o contrato dos dois tipos novos no catálogo, com os schemas copiados para os
testes, e o resultado do cancelamento em todos os métodos do domínio. Nenhum evento é gravado
ainda.

### Testes da fundação

- [X] T002 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SerializacaoEventosTestes.cs` (R-04, data-model):
  - ida e volta de `PedidoCancelado` e `PagamentoEstornado` com `SerializacaoEventos`, com os campos em camelCase;
  - `motivo` e `etapaEsgotada` serializados como texto (`"Operacao"`, `"Entrega"`); `etapaEsgotada` nulo aparece como `"etapaEsgotada": null` (o campo é sempre enviado);
  - os tipos do envelope são `pedido.cancelado` e `pagamento.estornado` (`TiposEvento.PedidoCancelado`, `TiposEvento.PagamentoEstornado`), e `TiposEvento.TipoDados` passa a ter 8 tipos (hoje o teste confere 6).
- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoPagamentoNaoProcessadoTestes.cs`: `RegistrarPagamentoNaoProcessado` devolve `ResultadoCancelamento(EstoqueDevolvido: true, EstornoSolicitado: false, OrdemCancelada: false)`; os casos existentes continuam iguais.

### Implementação da fundação

- [X] T004 Contrato (R-04, data-model, contrato) em `src/PedidoFulfillment.Application/Contratos/Eventos/Eventos.cs`:
  - `public enum MotivoCancelamento { Cliente = 1, Operacao, EstoqueInsuficiente, ReconciliacaoEsgotada }` e `public enum EtapaEsgotada { Estoque = 1, Pagamento, Entrega }`, com XML doc dizendo que acrescentar valor muda o contrato (Princípio II);
  - `sealed record PedidoCancelado` com `required` `PedidoId` (`Guid`), `Numero` (`string`), `Motivo` (`MotivoCancelamento`), `EtapaEsgotada` (`EtapaEsgotada?`, "texto quando o motivo é `ReconciliacaoEsgotada`; `null` nos demais"), `CanceladoEm` (`DateTimeOffset`), `EstoqueDevolvido`, `EstornoPendente` e `OrdemEntregaCancelada` (`bool`); sem valor, itens, cliente, autor ou texto do motivo;
  - `sealed record PagamentoEstornado` com `required` `PedidoId`, `Numero`, `Valor` (`decimal`), `IdentificadorTransacao` ("texto, até 100"), `IdentificadorEstorno` ("texto, até 100") e `EstornadoEm` (`DateTimeOffset`);
  - XML doc de cada record citando o REQ-15 e quando o evento sai.

  Em `TiposEvento.cs`: `PedidoCancelado = "pedido.cancelado"` e `PagamentoEstornado = "pagamento.estornado"` no catálogo e em `TipoDados`; o comentário da classe cita o REQ-15. Em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`, copiar `..\..\specs\015-visibilidade-cancelamentos\contracts\eventos\*.schema.json` com `LinkBase="Eventos"`, como as linhas existentes (o `SchemaEventosTestes` carrega um schema por tipo do catálogo). Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`, um exemplo válido de cada tipo novo em `EventosValidos` (um `pedido.cancelado` com `ReconciliacaoEsgotada`/`Entrega` e um com `Operacao`/`null`) e o resumo da classe citando `specs/015-visibilidade-cancelamentos/contracts/eventos/`. T002 deve passar.
- [X] T005 Em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`, `RegistrarPagamentoNaoProcessado` passa a devolver `ResultadoCancelamento` (data-model); os chamadores atuais ignoram o retorno por enquanto. Em `ResultadoCancelamento.cs`, o XML doc passa a dizer que o resultado alimenta os logs e o `pedido.cancelado` (REQ-15). T003 deve passar.

**Checkpoint**: suíte inteira verde, nenhum comportamento mudou. Commit `feat(eventos): contrato v1 de pedido.cancelado e pagamento.estornado [REQ-15]`.

---

## Phase 3: User Story 1 - Todo cancelamento sai como `pedido.cancelado` (Priority: P1) 🎯 MVP

**Goal**: toda chegada de um pedido a `Cancelado`, pelos cinco caminhos, grava um `pedido.cancelado` no outbox na mesma gravação, com o motivo em categoria, a etapa esgotada e o que foi desfeito junto (FR-001 a FR-005).

**Independent Test**: cancelar pedidos por cada caminho (Cliente, operação com e sem pagamento, falta de estoque e esgotamento das três etapas) e conferir no outbox, e numa fila de teste ligada a `oms.eventos`, um `pedido.cancelado` por pedido, com os campos da tabela do data-model.

### Testes da US1

- [X] T006 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/EventosPedidoTestes.cs` (novo; R-02), `EventosPedido.Cancelado(pedido, motivo, etapa, resultado, instante)`:
  - envelope v1 com `tipo` `pedido.cancelado`, `idCorrelacao` = `pedido.Id`, `ocorridoEm` = `instante`; `EventoSaida` com o mesmo `IdEvento` e `CriadoEm = instante`;
  - `pedidoId`, `numero` e `canceladoEm = instante`; as três flags copiadas do `ResultadoCancelamento` (`EstoqueDevolvido`, `EstornoSolicitado` → `estornoPendente`, `OrdemCancelada` → `ordemEntregaCancelada`);
  - `etapa` informada só com `ReconciliacaoEsgotada`: com outro motivo e etapa preenchida, ou com `ReconciliacaoEsgotada` sem etapa, `ArgumentException` (combinação do schema);
  - o JSON não tem `clienteId`, `valorTotal`, `itens`, `autor` nem o motivo do histórico.
- [X] T007 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoCancelamentoTestes.cs` (R-01): `Pedido.Cancelar(null, "Estoque insuficiente.", produtos vazios, instante)` num pedido `Pendente` sem reserva, sem pagamento e sem ordem leva a `Cancelado` com histórico sem autor e com o motivo, e devolve `ResultadoCancelamento` todo falso, exatamente como o `MudarStatus(Cancelado, null, …)` que ele substitui na falta de estoque e na reconciliação da etapa de estoque.
- [X] T008 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosStatusTestes.cs`, com os falsos de `Pedidos/Falsos.cs` (`EventosGravados`):
  - Cliente cancela o próprio pedido `Pendente`: um `pedido.cancelado` gravado com `motivo` `Cliente`, `etapaEsgotada` nulo, `estornoPendente` falso;
  - Admin e OperadorLogistica cancelam (sem pagamento, pago, pago com ordem): `motivo` `Operacao` e as flags do resultado (data-model);
  - um usuário com perfil de operação cancelando um pedido que é dele: `Operacao`;
  - conflito na primeira gravação e sucesso na segunda: um único `pedido.cancelado` gravado (o da segunda tentativa);
  - conflito nas 3 tentativas: nenhum `pedido.cancelado` gravado e a resposta de conflito de hoje (US1 cenário 9);
  - mudanças para outros status, 403, 404 e 409: nenhum `pedido.cancelado`.
- [X] T009 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`: com falta de estoque, a mesma gravação leva `estoque.insuficiente` (conteúdo de hoje, sem mudança) e `pedido.cancelado` com `motivo` `EstoqueInsuficiente`, `etapaEsgotada` nulo e as três flags falsas; o histórico continua sem autor e com `Estoque insuficiente.`; reserva bem-sucedida e pedido ignorado não geram `pedido.cancelado`.
- [X] T010 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Reconciliacao/ServicoReconciliacaoTestes.cs`: no esgotamento, um `pedido.cancelado` com `motivo` `ReconciliacaoEsgotada` e `etapaEsgotada` `Estoque` (flags falsas), `Pagamento` (`estoqueDevolvido` verdadeiro) e `Entrega` (`estoqueDevolvido` e `estornoPendente` verdadeiros, `ordemEntregaCancelada` falso); os motivos do histórico continuam os do REQ-12/13; falha sem esgotar, pedido que saiu da etapa e conflito seguido de releitura que encontra o pedido fora da etapa não geram evento; conflito seguido de esgotamento grava um só evento.
- [X] T011 [P] [US1] Integração, com banco real, conferindo as linhas de `EventosSaida` do tipo `pedido.cancelado` (conteúdo desserializado com `SerializacaoEventos`):
  - `tests/PedidoFulfillment.Testes.Integracao/Pedidos/StatusPedidoComBancoTestes.cs`: Cliente e operação (US1 cenários 1 e 2);
  - `tests/PedidoFulfillment.Testes.Integracao/Estornos/CancelamentoPedidoPagoTestes.cs`: pedido pago com ordem (cenário 3), e cancelamento recusado (409 do pedido já cancelado, 403 do Cliente em pedido pago) sem evento (cenário 8 e Edge Cases); com o armazenamento falhando na gravação (banco fora do ar), nenhuma linha no outbox (cenário 9);
  - `tests/PedidoFulfillment.Testes.Integracao/Estoque/ReservaEstoqueTestes.cs`: falta de estoque com os dois eventos (cenário 4);
  - `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/EsgotamentoTestes.cs`: as três etapas (cenários 5 a 7);
  - `tests/PedidoFulfillment.Testes.Integracao/Estornos/DisputaCancelamentoTestes.cs`: dois cancelamentos simultâneos e reconciliação × API geram um único `pedido.cancelado`, com o motivo de quem venceu (cenário 8 e Edge Cases);
  - `tests/PedidoFulfillment.Testes.Integracao/Pagamentos/PagamentoRecusadoTestes.cs`: pedido em `PagamentoRejeitado` tem `pagamento.recusado` e **nenhum** `pedido.cancelado` (FR-005).
- [X] T012 [P] [US1] Teste de ponta a ponta em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/EventosCancelamentoTestes.cs` (novo), com API e Worker (despachante ligado) e `MensageriaTeste.FilaTesteAsync("pedido.cancelado")`: um cancelamento pela API chega à fila de teste uma vez, válido pelo schema `pedido.cancelado.v1`.

### Implementação da US1

- [X] T013 [US1] `src/PedidoFulfillment.Application/Pedidos/EventosPedido.cs` (novo; R-02): `public static class EventosPedido` com `EventoSaida Cancelado(Pedido pedido, MotivoCancelamento motivo, EtapaEsgotada? etapa, ResultadoCancelamento resultado, DateTimeOffset instante)`, validando a combinação motivo/etapa (`ArgumentException`) e montando o envelope com `EnvelopeEvento.Criar` e `EventoSaida.De`. T006 deve passar.
- [X] T014 [US1] `src/PedidoFulfillment.Application/Pedidos/ServicoPedidos.cs`: no ramo de cancelamento de `AtualizarStatusAsync`, depois do `pedido.Cancelar`, `armazenamento.Adicionar(EventosPedido.Cancelado(pedido, operacao ? MotivoCancelamento.Operacao : MotivoCancelamento.Cliente, null, cancelamento, agora))`, a cada tentativa (o `DescartarAlteracoes` do conflito limpa o evento); o XML doc do método cita o REQ-15. T008 deve passar.
- [X] T015 [US1] `src/PedidoFulfillment.Application/Estoque/ReservaEstoque.cs` (R-01): `CancelarSemEstoqueAsync` troca `pedido.MudarStatus(StatusPedido.Cancelado, null, MotivoSemEstoque, agora)` por `pedido.Cancelar(null, MotivoSemEstoque, new Dictionary<Guid, Produto>(), agora)` e adiciona, depois do `estoque.insuficiente`, o `EventosPedido.Cancelado(..., MotivoCancelamento.EstoqueInsuficiente, null, resultado, agora)`; a checagem de transição existente pode sair, porque o `Cancelar` já a faz com a mesma exceção. T007 e T009 devem passar.
- [X] T016 [US1] `src/PedidoFulfillment.Application/Reconciliacao/ServicoReconciliacao.cs` (R-01, R-02): `CancelarAsync` devolve o motivo do histórico e o `ResultadoCancelamento`; a etapa de estoque usa `pedido.Cancelar(null, MotivoEstoqueNaoReservado, produtos vazios, agora)`; a de pagamento usa o retorno de `RegistrarPagamentoNaoProcessado`; antes do `SalvarAsync`, adiciona `EventosPedido.Cancelado(..., MotivoCancelamento.ReconciliacaoEsgotada, etapa convertida de EtapaFluxoPedido para EtapaEsgotada, resultado, agora)`. A conversão é um `switch` explícito (uma etapa interna nova não vira valor do contrato sem decisão). T010 deve passar.
- [X] T017 [US1] Rodar T011 e T012 e corrigir o que faltar.

**Checkpoint**: suíte inteira verde. Commit `feat(pedidos): publica pedido.cancelado em todo cancelamento [REQ-15]`.

---

## Phase 4: User Story 2 - O estorno concluído sai como `pagamento.estornado` (Priority: P1)

**Goal**: a confirmação do estorno grava um único `pagamento.estornado` na mesma gravação, com o valor e os identificadores do gateway (FR-006, FR-007, FR-014).

**Independent Test**: cancelar um pedido pago, deixar o processador de estornos confirmar e conferir um único `pagamento.estornado`, válido pelo schema, com o valor cobrado.

### Testes da US2

- [X] T018 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ServicoEstornosTestes.cs` (R-05):
  - confirmação: um `pagamento.estornado` gravado com `pedidoId`, `numero`, `valor` = valor cobrado, `identificadorTransacao` da cobrança, `identificadorEstorno` do gateway e `estornadoEm` = instante da confirmação;
  - recusa e exceção do gateway: nenhum evento;
  - conflito ao gravar a confirmação, releitura ainda pendente e nova gravação: um único evento gravado; releitura que encontra o estorno já confirmado por outra instância: nenhum evento (nada a fazer);
  - conflito persistente (reavaliações esgotadas): nenhum evento gravado;
  - o log 7006 continua sem os identificadores do gateway.
- [X] T019 [P] [US2] Integração com banco e broker reais:
  - `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs`: estorno confirmado gera um `pagamento.estornado` no outbox; depois de falhas, um só (US2 cenário 2); com a gravação da primeira confirmação falhando, a rodada seguinte grava um só (cenário 3); estorno pendente criado direto no banco antes da rodada (pedido "de antes do REQ-15") gera o evento (cenário 5);
  - `tests/PedidoFulfillment.Testes.Integracao/Estornos/DisputaCancelamentoTestes.cs`: cobrança aprovada para pedido já cancelado (REQ-13, R-07): `pedido.cancelado` com `estornoPendente` falso, nenhum `pagamento.aprovado` e, depois da rodada, um `pagamento.estornado` (cenário 4, FR-014);
  - em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/EventosCancelamentoTestes.cs`, com `FilaTesteAsync("pagamento.estornado")` e o processador de estornos ligado: o evento chega uma vez, válido pelo schema.

### Implementação da US2

- [X] T020 [US2] `src/PedidoFulfillment.Application/Pagamentos/ServicoEstornos.cs` (R-05): em `ConfirmarAsync`, depois de `pedido.ConfirmarEstorno`, `armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoEstornado { ... }, instante), instante))`, com o mesmo instante passado ao `ConfirmarEstorno`, a cada tentativa (inclusive depois da releitura). O resumo da classe cita o REQ-15. `RegistrarCobrancaAposCancelamento` no `ProcessamentoPagamento` não muda (FR-014). T018 e T019 devem passar.

**Checkpoint**: suíte inteira verde. Commit `feat(pagamentos): publica pagamento.estornado na confirmação do estorno [REQ-15]`.

---

## Phase 5: User Story 3 - Outros sistemas confiam no contrato dos eventos novos (Priority: P2)

**Goal**: schemas fechados e conferidos com casos inválidos, sem dados pessoais, e consumidores internos que não recebem os tipos novos (FR-008 a FR-010).

**Independent Test**: validar exemplos dos dois tipos contra os schemas, recusar os inválidos e conferir que as filas internas não recebem os tipos novos.

### Testes da US3

- [X] T021 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`, dois `[Theory]` no padrão de `Entrega_criada_fora_do_contrato_e_recusada`:
  - `pedido.cancelado`: sem `motivo`; `motivo` `PagamentoRecusado` (fora da lista, FR-005); `etapaEsgotada` ausente; `etapaEsgotada` `Entrega` com `motivo` `Operacao`; `etapaEsgotada` nulo com `ReconciliacaoEsgotada`; campo a mais (`clienteId`); `numero` fora do padrão `PED-AAAAMMDD-NNNNNN`;
  - `pagamento.estornado`: sem `identificadorEstorno`; `valor` 0; `valor` com 3 casas; `identificadorTransacao` com 101 caracteres; campo a mais.
- [X] T022 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/EventosCancelamentoTestes.cs`: com as filas `oms.estoque`, `oms.pagamento` e `oms.entrega` declaradas pelo Worker, um `pedido.cancelado` e um `pagamento.estornado` publicados não chegam a elas (nem às DLQs), e o despachante os marca como enviados sem fila ligada (como em `PublicacaoSemFilaTestes`); o `dados` recebido tem exatamente as chaves do data-model (nenhum dado pessoal, FR-010).

### Implementação da US3

- [X] T023 [US3] Nenhuma mudança de código esperada (R-06). Se T021 ou T022 falharem, corrigir a fábrica (T013), o `ServicoEstornos` (T020) ou o schema em `specs/015-visibilidade-cancelamentos/contracts/eventos/`, mantendo o contrato do data-model, e registrar a correção em Notes.

**Checkpoint**: commit `test(eventos): conformidade e isolamento dos eventos de cancelamento e estorno [REQ-15]`.

---

## Phase 6: Guarda contra cancelamento sem evento (R-03)

**Purpose**: um caminho atual ou futuro que leve um pedido a `Cancelado`, ou um pagamento a `Estornado`, sem o evento falha na gravação, em vez de cancelar em silêncio (FR-001, FR-006). Depende da US1 e da US2: antes delas, a guarda quebraria os caminhos ainda não migrados.

- [X] T024 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/GuardaEventosCancelamentoTestes.cs` (novo), com o `IArmazenamentoPedidos` resolvido do contêiner de DI e banco real:
  - pedido lido para alteração, `MudarStatus(Cancelado, …)` sem evento e `SalvarAsync`: `InvalidOperationException`, e nada gravado (status, histórico e outbox relidos);
  - o mesmo com `EventosPedido.Cancelado(...)` adicionado: grava;
  - `pedido.cancelado` de **outro** pedido adicionado: continua recusando (a correlação é pelo `idCorrelacao`);
  - pagamento `EstornoPendente` → `ConfirmarEstorno` sem `pagamento.estornado`: `InvalidOperationException`; com o evento: grava;
  - pedido novo já `Cancelado` semeado direto pelo `ContextoBancoDados` (como fazem outros testes) continua sendo gravado: a guarda só olha o `ArmazenamentoPedidos` e só entidades `Modified`.
- [X] T025 `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs` (R-03, data-model "Transições observadas pela guarda"): antes do `SaveChangesAsync` em `SalvarAsync`, percorrer `contexto.ChangeTracker`:
  - `Pedido` em `Modified` com `Property(p => p.Status).OriginalValue != Cancelado` e `CurrentValue == Cancelado`;
  - `Pagamento` em `Modified` com `Situacao` original `EstornoPendente` e atual `Estornado`;
  - para cada um, exigir um `EventoSaida` em `Added` do tipo `pedido.cancelado` ou `pagamento.estornado` cujo `idCorrelacao` (lido do `Conteudo` com `JsonDocument`) é o `PedidoId`; faltando, `InvalidOperationException` com o número ou o identificador do pedido e o tipo esperado, sem gravar.

  XML doc citando o REQ-15, por que a guarda não fica no `ContextoBancoDados` e que a exceção segue o caminho de erro de hoje (Worker: novas tentativas e DLQ; API: 500). T024 deve passar, e a suíte inteira continua verde (todos os caminhos já gravam o evento).

**Checkpoint**: commit `feat(persistencia): recusa cancelamento e estorno sem evento no outbox [REQ-15]`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T026 [P] Documentos de fluxo (regra do `CLAUDE.md`, R-10), conferindo mensagens, motivos, IDs de log e links relativos no código:
  - `docs/fluxos/03-consulta-e-mudanca-de-status.md`: o cancelamento pela API grava `pedido.cancelado` (`Cliente` ou `Operacao`), com exemplo do evento; Limitações sem o item de cancelamento invisível (#29 no lugar);
  - `docs/fluxos/04-reserva-de-estoque.md`: a falta de estoque grava `estoque.insuficiente` e `pedido.cancelado` na mesma gravação, sem ordem garantida entre eles;
  - `docs/fluxos/06-ordem-de-entrega.md`: a ordem cancelada junto com o pedido aparece no `pedido.cancelado` (`ordemEntregaCancelada`);
  - `docs/fluxos/08-publicacao-e-consumo-de-eventos.md`: catálogo com os dois tipos novos, sem consumidor interno (saem sem rota obrigatória), e como um sistema de fora os recebe;
  - `docs/fluxos/09-reconciliacao.md`: o esgotamento grava `pedido.cancelado` com a etapa; Limitações sem o "cancela sem evento";
  - `docs/fluxos/10-estorno.md`: a confirmação grava `pagamento.estornado`; a cobrança aprovada para pedido cancelado só aparece nele; Limitações sem a invisibilidade do estorno;
  - `docs/fluxos/README.md`: tabela de eventos (se houver) com os tipos novos; o diagrama de estados não muda.
- [X] T027 [P] Documentação do projeto:
  - `README.md`: linha do REQ-15 em **Estado atual**; **Próximos passos** sem a #29 (resolvida), com a #18 e a #37; **Decisões**: evento único de cancelamento com motivo em categoria e guarda na gravação; **Roteiros de validação**: o quickstart do REQ-15;
  - `specs/012-publicacao-garantida/spec.md` e `specs/013-cancelamento-com-estorno/spec.md`: as pendências que apontam para a #29 passam a citar o REQ-15.
- [X] T028 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados); registrar a contagem em Notes.
- [ ] T029 Executar o [quickstart](./quickstart.md) (§2, §3, §4, §6 e §7) com a API e o Worker locais e registrar em Notes o que chegou à fila `sistema-externo.teste`.
- [ ] T030 Fazer push e abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #29`, citando em Validação os documentos de fluxo atualizados. Acompanhar os workflows `build-testes` e `contrato-compatibilidade` até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: a linha de base vem antes de tudo.
- **Foundational** (T002 a T005): bloqueia todas as histórias. T004 junta o catálogo, o `.csproj` e os exemplos válidos, porque o `SchemaEventosTestes` carrega um schema por tipo do catálogo.
- **US1** (T006 a T017): depende da fundação. T013 antes de T014 a T016; T017 por último.
- **US2** (T018 a T020): depende só da fundação; pode correr junto da US1. A parte da T019 com o `pedido.cancelado` usa a T014.
- **US3** (T021 a T023): os schemas dependem da fundação; a T022 publica os eventos reais e depende da US1 e da US2.
- **Guarda** (T024 e T025): depois da US1 e da US2.
- **Polish**: depois de tudo. A T030 exige push.

### Parallel Opportunities

- T002 e T003.
- T006 a T012 (arquivos diferentes).
- T018 e T019; T021 e T022.
- T026 e T027.

### Exemplo: US1

```text
Juntos: T006 (EventosPedidoTestes), T008 (ServicoPedidosStatusTestes), T009 (ReservaEstoqueTestes), T010 (ServicoReconciliacaoTestes)
Depois: T013 → T014, T015 e T016 → T017
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: todo cancelamento passa a ser visível para outros sistemas, que é o
objetivo da issue. A US2 fecha o ciclo do dinheiro, a US3 prova o contrato, e a guarda impede
que um caminho futuro volte a cancelar em silêncio.

### Incremental Delivery

1. Setup + fundação → `feat(eventos): contrato v1 de pedido.cancelado e pagamento.estornado [REQ-15]`.
2. US1 → `feat(pedidos): publica pedido.cancelado em todo cancelamento [REQ-15]`.
3. US2 → `feat(pagamentos): publica pagamento.estornado na confirmação do estorno [REQ-15]`.
4. US3 → `test(eventos): conformidade e isolamento dos eventos de cancelamento e estorno [REQ-15]`.
5. Guarda → `feat(persistencia): recusa cancelamento e estorno sem evento no outbox [REQ-15]`.
6. Polish → `docs(fluxos): ... [REQ-15]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): eventos para outras mudanças de status
  (`pedido.despachado`), evento de falha de estorno, envio retroativo de cancelamentos antigos,
  estorno parcial e consumidor interno dos tipos novos.
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em outra rodada.
- **Analyze (2026-10-26)**: aplicadas as correções C1 (falha na gravação sem evento na T008 e
  na T011), I1 (cenários da T011 renumerados), A1 (FR-002: etapa sempre enviada, nula fora da
  reconciliação), I2 (FR-014 marcado como do clarify), U1 (a exceção da guarda segue o caminho
  de erro de hoje, no R-03 e na T025), A2 (SC-003 com a meta de 10 s do REQ-10) e G1 (fluxo 6
  na T026).
- **SC-003 e SC-006** decorrem de não mudar o despachante nem o caminho do pedido: os eventos
  novos são uma linha a mais no outbox, na mesma transação. Não há tarefa de desempenho
  própria; a suíte de desempenho existente continua rodando no CI.
- **T001 (linha de base, 2026-10-26)**: 982 testes, 607 unitários e 375 de integração, zero
  ignorados, com o Docker no ar.
- **Fundação (T002 a T005)**: `LeitorMensagemTestes` usava `pedido.cancelado` como exemplo de
  tipo desconhecido; passou a usar `pedido.despachado`, que continua fora do catálogo. O
  `EventoGravado` do `ReservaEstoqueTestes` (unitário) passou a escolher o evento pelo tipo,
  porque a falta de estoque agora grava dois eventos.
- **US1 (T006 a T017)**:
  - os testes do REQ-12 e do REQ-13 que exigiam "nenhum evento de cancelamento"
    (`CancelamentoPedidoPagoTestes` e três casos de `EsgotamentoTestes`, mais os unitários da
    reconciliação e do status) passaram a exigir o `pedido.cancelado` certo (FR-001);
  - helper novo `Infraestrutura/EventosOutbox.cs` para ler o outbox de um pedido nos testes;
  - **cenário 9** (falha na gravação sem evento): coberto pelos unitários de conflito
    persistente (`ServicoPedidosStatusTestes` e `ServicoReconciliacaoTestes`) e pela T024, em que
    a gravação recusada não deixa linha no outbox; não há teste de integração que derrube o
    banco no meio do cancelamento, porque a transação única já garante o resultado.
- **US2 (T018 a T020)**: o cenário 5 (estorno pendente de antes do REQ-15) não tem teste próprio:
  o `pagamento.estornado` depende só da confirmação, e o primeiro caso de
  `ProcessamentoEstornosTestes` cobre o mesmo caminho. O teste da cobrança aprovada para pedido
  cancelado (`DisputaCancelamentoTestes`) passou a ligar o processador de estornos.
- **US3 (T021 a T023)**: nenhuma correção foi necessária na fábrica, no serviço ou nos schemas.
- **Guarda (T024 e T025)**: a suíte inteira continuou verde com a guarda ligada, o que confirma
  que todos os caminhos de cancelamento e de estorno gravam o evento.
- **T026 e T027**: fluxos 3, 4, 5, 6, 7, 8, 9 e 10 e o índice; o diagrama de estados não muda.
  O fluxo 5 entrou além da lista da T026 (a cobrança aprovada para pedido cancelado não gera
  evento), e o fluxo 7 só ajustou a pendência do `pedido.despachado`, que ficou fora do #29.
  `README.md` (Estado atual, Próximos passos com #18 e #37, Mensageria, Decisões, pronto e
  roteiros) e `specs/012` e `specs/013` apontando a #29 para o REQ-15. Links relativos
  conferidos por script.
- **T028 (2026-10-26)**: nenhum segredo no diff; `dotnet format --verify-no-changes` limpo;
  suíte local inteira com Docker: 636 unitários e 397 de integração, zero ignorados (1.033 no
  total; 982 na linha de base).
