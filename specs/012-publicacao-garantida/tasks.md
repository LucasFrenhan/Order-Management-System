---

description: "Lista de tarefas do REQ-12 — Publicação garantida de eventos e reconciliação de pedidos parados"
---

# Tasks: Publicação garantida de eventos e reconciliação de pedidos parados

**Input**: Design documents from `/specs/012-publicacao-garantida/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/publicacao-e-reconciliacao.md](./contracts/publicacao-e-reconciliacao.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio, e o
FR-018 pede o teste de publicação num broker sem filas e os testes da reconciliação por etapa
e do esgotamento, com banco e broker reais. Os testes de cada fase vêm antes da
implementação e devem falhar primeiro.

**Organization**: por história de usuário. A US1 (publicação garantida) e a US2
(reconciliação) são independentes e podem ser feitas em qualquer ordem; as duas mexem em
`ExtensoesMensageria.AdicionarConsumidor`, por isso a T005 e a T014 correm em sequência. A
US3 confere os logs que a US1 e a US2 escrevem.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Nomes em português; regra no domínio e no Application; Infrastructure só com banco, broker
  e host.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo. Uma migration só (`PagamentoNaoProcessado`, T013).
- Contratos de eventos v1 e contrato HTTP v1 inalterados (FR-016): nenhum schema em
  `specs/*/contracts/` muda, e `SchemaEventosTestes` e `ConformidadeOpenApiTestes` continuam
  verdes sem edição.
- Comportamento dos três consumidores inalterado (FR-017): os testes existentes de
  `ReservaEstoqueTestes`, `ProcessamentoPagamentoTestes` e `CriacaoOrdemEntregaTestes`
  (unitários e de integração) passam sem afrouxar asserções.
- Os testes com banco e broker reais usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER`
  (REQ-07).
- Nenhuma espera fixa em teste: espere por condição, com prazo.
- Commits: `tipo(escopo): descrição [REQ-12]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-12-publicacao-garantida` e registrar em Notes a contagem de testes como linha de base (720 no fim do REQ-11: 430 unitários e 290 de integração; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

Nenhuma tarefa bloqueia as duas histórias ao mesmo tempo: a US1 só toca a mensageria e a US2
só a reconciliação e o domínio do pagamento. O ponto comum, `AdicionarConsumidor`, é editado
pela T005 (descritor) e depois pela T014 (`IEtapaFluxoPedido`).

---

## Phase 3: User Story 1 - Nenhum evento com consumidor se perde num broker sem filas (Priority: P1) 🎯 MVP

**Goal**: o despachante declara as filas de todos os consumidores conhecidos a cada canal novo e publica com `mandatory` os tipos que têm consumidor; uma devolução sem rota deixa o evento pendente com `SemRota` (FR-001 a FR-006, FR-018).

**Independent Test**: broker sem filas `oms.*`, um `pedido.criado` pendente no outbox; o Worker sobe e o pedido reserva o estoque, sem nenhum evento marcado como enviado sem chegar a uma fila.

### Testes da US1

- [X] T002 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/TopologiaConhecidaTestes.cs` (R-01, data-model), com descritores falsos:
  - com os três consumidores atuais (`estoque` ← `pedido.criado`, `pagamento` ← `estoque.reservado`, `entrega` ← `pagamento.aprovado`): `ExigeRota` verdadeiro para os três tipos e falso para `estoque.insuficiente`, `pagamento.recusado` e `entrega.criada` (theory);
  - sem descritores: nenhum tipo exige rota e `Consumidores` vazio;
  - um consumidor novo com um tipo novo passa a exigir rota só por estar na lista (FR-003);
  - `Consumidores` mantém nome e tipos de cada descritor.
- [X] T003 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/PublicacaoSemFilaTestes.cs` (FR-018, SC-001), com banco e broker reais e a `FabricaWorker`:
  - **broker sem filas**: apagar as filas `oms.*` (principal, espera e DLQ) dos três consumidores; gravar um pedido com `pedido.criado` pendente no outbox (como `CenarioEstoque`); remover os três consumidores com `RemoverConsumidor` para que só o despachante declare a topologia; subir o Worker; esperar o evento marcado como enviado e conferir que `oms.estoque`, `oms.pagamento` e `oms.entrega` existem com as ligações do contrato e que a mensagem está em `oms.estoque` (contagem 1);
  - **Worker completo num broker sem filas**: com os consumidores registrados, o pedido chega a `ProntoParaEnvio` (estoque, pagamento e entrega), sem `PRECONDITION_FAILED` em nenhum canal (FR-002);
  - **fila apagada com o Worker no ar** (cenários 3 e 4 da US1): com o Worker no ar e os consumidores removidos, apagar `oms.estoque` e gravar outro `pedido.criado`; esperar o evento com `Tentativas >= 1`, `UltimoErro` começando por `SemRota:` e `EnviadoEm` nulo; depois esperar o evento enviado e a fila `oms.estoque` recriada pelo despachante, com a mensagem nela;
  - **ordem do outbox**: um segundo evento gravado depois do evento sem rota só sai depois dele;
  - **tipo sem consumidor** (cenário 5): um `entrega.criada` pendente, sem nenhuma fila ligada a ele, é marcado como enviado sem erro.

  Usar os auxiliares de `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MensageriaTeste.cs` para apagar e contar filas, acrescentando o que faltar (por exemplo, `ApagarFilasConsumidorAsync(consumidor)` e `FilaExisteAsync`).

### Implementação da US1

- [X] T004 [P] [US1] Em `src/PedidoFulfillment.Infrastructure/Mensageria/DescritorConsumidor.cs` e `src/PedidoFulfillment.Infrastructure/Mensageria/TopologiaConhecida.cs` (R-01):
  - `interface IDescritorConsumidor { string Consumidor { get; } IReadOnlyCollection<string> Tipos { get; } }`;
  - `sealed class DescritorConsumidor<TManipulador, TDados> : IDescritorConsumidor`, que recebe o `IServiceScopeFactory`, abre um escopo uma vez (lazy), resolve o `TManipulador` e lê `Consumidor` e `Tipos`, como `ConsumidorEventos` faz na partida;
  - `sealed class TopologiaConhecida`, montada a partir de `IEnumerable<IDescritorConsumidor>`: `IReadOnlyList<(string Consumidor, IReadOnlyCollection<string> Tipos)> Consumidores` e `bool ExigeRota(string tipo)` (comparação ordinal).

  T002 deve passar.
- [X] T005 [US1] Em `src/PedidoFulfillment.Infrastructure/Mensageria/ExtensoesMensageria.cs`, `AdicionarConsumidor<TManipulador, TDados>` registra também `services.AddSingleton<IDescritorConsumidor, DescritorConsumidor<TManipulador, TDados>>()`; `TopologiaConhecida` registrada como singleton. `RemoverConsumidor` dos testes continua removendo só o hosted service (R-01, conferir em `CenarioEstoque` e na `FabricaWorker`).
- [X] T006 [US1] Em `src/PedidoFulfillment.Infrastructure/Mensageria/DespachanteEventosSaida.cs` (R-02, R-03):
  - recebe a `TopologiaConhecida`;
  - `ObterCanalAsync`, depois de `DeclararTrocaAsync`, chama `TopologiaMensageria.DeclararConsumidorAsync(canal, consumidor, tipos, opcoes.Intervalos, ct)` para cada consumidor conhecido, antes de devolver o canal;
  - `BasicPublishAsync` com `mandatory: topologia.ExigeRota(evento.Tipo)`;
  - `catch (PublishReturnException ex)` antes do `catch` geral: `RegistrarFalha` com `UltimoErro = $"SemRota: {ex.ReplyText}"` (truncado ao tamanho da coluna, como hoje), log 5002 com o motivo `SemRota` no lugar do nome da exceção, lote encerrado e canal descartado, como em qualquer falha;
  - XML doc citando o REQ-12.

  T003 deve passar. Rodar a suíte inteira no checkpoint: os testes do REQ-06 ao REQ-11 continuam verdes (filas de consumidores removidos passam a existir e acumular eventos; ajustar só a limpeza, se algum teste contar mensagens numa fila que antes não existia).

**Checkpoint**: MVP — nenhum evento com consumidor se perde por falta de fila. Commit `feat(mensageria): declara as filas dos consumidores e exige rota na publicacao [REQ-12]`.

---

## Phase 4: User Story 2 - Pedidos parados por evento perdido voltam ao fluxo (Priority: P1)

**Goal**: o Worker roda uma reconciliação periódica que aplica a regra do consumidor da etapa aos pedidos parados, com esgotamento das etapas de estoque e pagamento (FR-007 a FR-014, FR-017).

**Independent Test**: pedido pago em `Processando`, sem ordem e com `AtualizadoEm` atrasado; o Worker sobe com a reconciliação ligada, e o pedido vai para `ProntoParaEnvio` com `entrega.criada` no outbox.

### Testes da US2

- [ ] T007 [P] [US2] Testes de domínio em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoPagamentoNaoProcessadoTestes.cs` (data-model, R-12), com o pedido levado a `AptoParaPagamento` como em `PedidoPagamentoTestes`:
  - `RegistrarPagamentoNaoProcessado(motivo, produtos, instante)`: pagamento criado com `ValorTotal`, situação `NaoProcessado`, identificador de transação nulo e `ProcessadoEm = instante`; estoque de cada produto devolvido (quantidades conferidas); `EstoqueReservado = false`; status `Cancelado`; histórico com a entrada para `Cancelado`, sem autor, com o motivo aparado (até `TamanhoMaximoMotivo`); `AtualizadoEm = instante`; depois disso `AptoParaPagamento` e `AptoParaOrdemEntrega` falsos;
  - fora de `AptoParaPagamento` (sem reserva, com pagamento, status final) ou com produto ausente no dicionário: `InvalidOperationException` sem alterar nada, como `RecusarPagamento`;
  - em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEntregaTestes.cs`: `Pagamento.RegistrarResultado` aceita `NaoProcessado` (sem transação) e continua lançando `ArgumentOutOfRangeException` para `Pendente`.
- [ ] T008 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Reconciliacao/RegistroFalhasReconciliacaoTestes.cs` (R-06): `RegistrarFalha` devolve 1, 2, 3 para o mesmo pedido e conta pedidos separadamente; `Remover` zera; `Podar(antesDe)` remove só as entradas com `UltimaFalhaEm < antesDe`; `ComFalha` traz as chaves atuais; chamadas concorrentes (`Parallel.For`) não perdem contagem.
- [ ] T009 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesReconciliacaoTestes.cs` (R-10), como `OpcoesMensageriaTestes`: padrões (`Habilitada` true, 300, 600, 86 400, 3, 100) válidos; cada faixa nos limites (1 e 86 400; 1 e 604 800; até 2 592 000; 1 e 100; 1 e 1 000) e fora deles; `LimiteEsgotamentoSegundos` igual ou menor que `LimiteParadoSegundos` reprova; várias falhas acumuladas numa só validação.
- [ ] T010 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Reconciliacao/ServicoReconciliacaoTestes.cs` (data-model, R-06, R-07), com `IEtapaFluxoPedido` falsas por etapa e o armazenamento falso de `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` (acrescentar o que faltar para ler produtos e o pedido para alterar):
  - regra devolve `true`: `Recuperado`, entrada removida do registro, `AplicarAsync` chamado com `idOrigem = Guid.Empty`;
  - regra devolve `false`: `NaoMaisParado`, entrada removida;
  - regra lança: `Falhou`, contagem incrementada; o pedido não é gravado;
  - estoque com 3 falhas e parado há mais de 24 h: `Esgotado`, pedido `Cancelado`, sem autor, motivo `ServicoReconciliacao.MotivoEstoqueNaoReservado` (`"Não foi possível reservar o estoque."`), nenhum evento no outbox falso, entrada removida;
  - pagamento com 3 falhas e parado há mais de 24 h: `Esgotado`, `Pagamento.Situacao = NaoProcessado`, estoque devolvido, `Cancelado` com `MotivoPagamentoNaoProcessado` (`"Não foi possível processar o pagamento."`), nenhum evento;
  - 3 falhas mas parado há menos de 24 h; ou mais de 24 h e só 2 falhas: `Falhou`, nada cancelado (cenário 10 da US2);
  - entrega com 3 falhas e mais de 24 h: `Falhou`, nunca `Esgotado` (FR-013);
  - esgotamento e, na releitura, o pedido saiu da etapa (por exemplo, a operação o cancelou ou o estoque foi reservado): `NaoMaisParado`, nada gravado;
  - esgotamento com conflito de concorrência nas 3 tentativas: `Falhou`, entrada mantida;
  - transição para `Cancelado` não permitida por `TransicoesStatusPedido.PermitidaParaOperacao`: `NaoMaisParado`.
- [ ] T011 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/CenarioReconciliacao.cs` e `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/ReconciliacaoEtapasTestes.cs` (banco e broker reais; `FabricaWorker` com `Reconciliacao:Habilitada=true`, `IntervaloCicloSegundos=1` e `LimiteParadoSegundos=60`; pedido semeado como nos cenários de estoque, pagamento e entrega, sem evento pendente, e `AtualizadoEm` atrasado por SQL):
  - **entrega** (cenário 1, SC-005): pedido pago em `Processando` sem ordem → `ProntoParaEnvio`, ordem criada, histórico com o motivo do REQ-10 sem autor, `entrega.criada` no outbox válido pelo schema;
  - **estoque** (cenário 2): `Pendente` sem reserva → estoque reservado e `estoque.reservado`; sem estoque → `Cancelado` com `ReservaEstoque.MotivoSemEstoque` e `estoque.insuficiente`;
  - **pagamento** (cenário 3): estoque reservado sem pagamento → aprovado com `pagamento.aprovado` (taxa 100) e recusado com `pagamento.recusado` (taxa 0);
  - **recente intocado** (cenário 4, SC-004): o mesmo pedido de entrega com `AtualizadoEm` atual continua sem ordem depois de 2 ciclos (esperar o log 9004 duas vezes);
  - **status final e efeito já feito** (cenário 7): `Cancelado`, `Despachado`, `PagamentoRejeitado` e pedido já com ordem continuam iguais;
  - **evento original chega depois** (cenário 5): depois da recuperação, publicar o `pagamento.aprovado` original; o consumidor o ignora, sem segunda ordem;
  - **predicados = domínio** (R-05): para cada combinação de status (6), `EstoqueReservado` (2), pagamento (nenhum, `Aprovado`, `Recusado`) e ordem (com e sem), semeada por SQL com `AtualizadoEm` antigo, a etapa devolvida por `IConsultaPedidosParados.ListarAsync` é a esperada pelas propriedades do domínio do pedido carregado (`AptoParaPagamento`, `AptoParaOrdemEntrega`, status não final sem reserva) ou nenhuma; combinações impossíveis para o domínio ficam de fora;
  - **ordem do lote** (FR-011, SC-007): com `TamanhoLote=2` e dois pedidos em `comFalha`, um pedido parado novo vem primeiro em `ListarAsync`.
- [ ] T012 [P] [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/EsgotamentoTestes.cs` e `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/ReconciliacaoConcorrenteTestes.cs` (banco e broker reais):
  - **esgotamento do pagamento** (cenário 9): gateway que sempre lança (substituto em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ServicosFalsos.cs`), `CiclosFalhaEsgotamento=3`, `LimiteEsgotamentoSegundos=120`, pedido com `AtualizadoEm` de 1 dia atrás; depois de 3 ciclos: pagamento `NaoProcessado` no banco (a check constraint aceita), estoque devolvido, pedido `Cancelado` com o motivo e sem autor, nenhum evento novo no outbox para o pedido;
  - **esgotamento do estoque** (cenário 8): regra de estoque que sempre lança (substituto de `IEtapaFluxoPedido` de estoque) → `Cancelado` com `"Não foi possível reservar o estoque."`, sem evento;
  - **entrega não se esgota** (cenário 11): regra de entrega que sempre lança; depois de 4 ciclos o pedido continua `Processando`;
  - **duas instâncias** (cenário 6, SC-003): dois `FabricaWorker` no mesmo banco e broker, com a reconciliação ligada, e 5 pedidos de entrega parados: cada pedido termina com uma ordem e um único `entrega.criada` no outbox. Sem asserção sobre o log 9006, porque a sobreposição dos ciclos não é garantida (analyze U1);
  - **lock ocupado** (R-08, determinístico): o teste segura `sp_getapplock 'oms-reconciliacao'` (`Exclusive`, `Session`) numa conexão própria e sobe o Worker com um pedido de entrega parado; espera o log 9006 e confere o pedido sem ordem; solta o lock (fecha a conexão) e espera a recuperação;
  - **reconciliação repetida**: dois ciclos seguidos sobre o mesmo pedido produzem um único efeito;
  - **falha isolada** (FR-011, analyze C1): num mesmo ciclo, um pedido de estoque cuja regra sempre lança e um pedido de entrega recuperável; o de entrega é recuperado nesse ciclo (log 9004 com `Entrega=1` e `Falhas=1`);
  - **lote cheio de falhas** (cenário 12, SC-007): `TamanhoLote=2`, dois pedidos de estoque cuja regra sempre lança; depois do primeiro ciclo, um pedido de entrega parado novo é recuperado no ciclo seguinte, mesmo com os dois que falham ainda parados.

### Implementação da US2

- [ ] T013 [P] [US2] Domínio e persistência do pagamento (R-12, data-model):
  - `src/PedidoFulfillment.Domain/Pagamentos/SituacaoPagamento.cs`: valor `NaoProcessado` depois de `Recusado`, com XML doc (cobrança esgotada na reconciliação, REQ-12);
  - `src/PedidoFulfillment.Domain/Pagamentos/Pagamento.cs`: `RegistrarResultado` aceita `Aprovado`, `Recusado` e `NaoProcessado`; outro valor continua lançando `ArgumentOutOfRangeException`;
  - `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`: `void RegistrarPagamentoNaoProcessado(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`, no molde de `RecusarPagamento`: pré-condição `AptoParaPagamento` e todos os produtos presentes, senão `InvalidOperationException` sem alterar nada; cria o `Pagamento` com o `ValorTotal`, registra `NaoProcessado`, `DevolverEstoque`, `MudarStatus(Cancelado, null, motivo, instante)`;
  - migration: `dotnet ef migrations add PagamentoNaoProcessado --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`; conferir que ela só recria `CK_Pagamentos_SituacaoValida`; acrescentar a migration nova nas listas de `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ModeloSincronizadoTestes.cs` e `MigracaoBancoTestes.cs`.

  T007 deve passar.
- [ ] T014 [US2] Regras dos consumidores como etapas (R-04, FR-017):
  - `src/PedidoFulfillment.Application/Reconciliacao/EtapaFluxoPedido.cs`: `enum EtapaFluxoPedido { Estoque, Pagamento, Entrega }`;
  - `src/PedidoFulfillment.Application/Reconciliacao/IEtapaFluxoPedido.cs`: `EtapaFluxoPedido Etapa { get; }` e `Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken ct)`, com XML doc (`true` gravou, `false` não apto, exceção falha);
  - `src/PedidoFulfillment.Application/Estoque/ReservaEstoque.cs`, `src/PedidoFulfillment.Application/Pagamentos/ProcessamentoPagamento.cs` e `src/PedidoFulfillment.Application/Entregas/CriacaoOrdemEntrega.cs`: implementam a interface; o corpo de `ManipularAsync` vira `AplicarAsync` (o identificador do evento nos logs passa a ser `idOrigem`), e `ManipularAsync` só chama `AplicarAsync(evento.IdEvento, evento.Dados.PedidoId, ct)`;
  - `ExtensoesMensageria.AdicionarConsumidor`: se `TManipulador` implementa `IEtapaFluxoPedido`, registra `services.AddScoped<IEtapaFluxoPedido>(sp => (IEtapaFluxoPedido)sp.GetRequiredService<TManipulador>())`;
  - acrescentar nos unitários de cada consumidor (`tests/PedidoFulfillment.Testes.Unitarios/Estoque/ReservaEstoqueTestes.cs`, `Pagamentos/ProcessamentoPagamentoTestes.cs`, `Entregas/CriacaoOrdemEntregaTestes.cs`) um caso de `AplicarAsync`: `true` quando grava e `false` quando o pedido não está apto.

  Os testes existentes dos três consumidores passam sem mudança de asserção.
- [ ] T015 [P] [US2] Application da reconciliação (R-05, R-06, R-07, R-11):
  - `src/PedidoFulfillment.Application/Reconciliacao/PedidoParado.cs`: `sealed record PedidoParado(Guid PedidoId, EtapaFluxoPedido Etapa, DateTimeOffset AtualizadoEm)`;
  - `src/PedidoFulfillment.Application/Reconciliacao/IConsultaPedidosParados.cs`: `Task<IReadOnlyList<PedidoParado>> ListarAsync(DateTimeOffset paradoAntesDe, int quantidade, IReadOnlyCollection<Guid> comFalha, CancellationToken ct)`, com XML doc da ordem (fora de `comFalha` primeiro, cada grupo por `AtualizadoEm` crescente);
  - `src/PedidoFulfillment.Application/Reconciliacao/RegistroFalhasReconciliacao.cs`: singleton com `ConcurrentDictionary<Guid, (int Falhas, DateTimeOffset UltimaFalhaEm)>`; `int RegistrarFalha(Guid, DateTimeOffset)`, `void Remover(Guid)`, `void Podar(DateTimeOffset antesDe)`, `IReadOnlyCollection<Guid> ComFalha`;
  - `src/PedidoFulfillment.Application/Reconciliacao/EventosReconciliacao.cs`: `LoggerMessage` 9001 `PedidoRecuperado` (Warning: PedidoId, Etapa), 9002 `PedidoEsgotado` (Error: PedidoId, Etapa, Falhas, Motivo), 9003 `FalhaPedidoReconciliacao` (Warning: PedidoId, Etapa, Falhas, Erro), 9004 `ResumoReconciliacao` (dois métodos, Warning e Debug: Estoque, Pagamento, Entrega, Esgotados, Falhas), 9005 `FalhaCicloReconciliacao` (Warning: Erro, Mensagem), 9006 `CicloReconciliacaoIgnorado` (Debug), 9007 `PedidoNaoMaisParado` (Debug: PedidoId, Etapa); só identificadores e contagens (FR-015).

  T008 deve passar.
- [ ] T016 [US2] `src/PedidoFulfillment.Application/Reconciliacao/ServicoReconciliacao.cs` e `ResultadoReconciliacao.cs` (data-model, R-07):
  - `enum ResultadoReconciliacao { Recuperado, NaoMaisParado, Falhou, Esgotado }`;
  - constantes `MotivoEstoqueNaoReservado = "Não foi possível reservar o estoque."` e `MotivoPagamentoNaoProcessado = "Não foi possível processar o pagamento."`;
  - `ReconciliarAsync(PedidoParado pedido, DateTimeOffset agora, ParametrosEsgotamento parametros, CancellationToken ct)` (`ParametrosEsgotamento`: `LimiteEsgotamento` e `CiclosFalha`, para o Application não depender das opções do Infrastructure): escolhe a `IEtapaFluxoPedido` da etapa entre as injetadas; `AplicarAsync(Guid.Empty, ...)`; `true` → `Remover`, log 9001, `Recuperado`; `false` → `Remover`, log 9007, `NaoMaisParado`; exceção (exceto `OperationCanceledException` do `ct`) → `RegistrarFalha`, log 9003 e, se as três condições do R-07 valem, esgota;
  - o esgotamento usa o `IArmazenamentoPedidos` do próprio escopo, depois de `DescartarAlteracoes()` (sem `IServiceScopeFactory` no Application, analyze I1), e lê os produtos como o consumidor de pagamento: até 3 tentativas de ler para alterar, conferir a etapa (estoque: status não final e sem reserva, e `TransicoesStatusPedido.PermitidaParaOperacao(status, Cancelado)`; pagamento: `AptoParaPagamento`), aplicar (`MudarStatus` ou `RegistrarPagamentoNaoProcessado`) e salvar sem evento; `ConflitoConcorrenciaException` → descartar e reler; esgotado → `Remover`, log 9002, `Esgotado`; fora da etapa → `Remover`, log 9007, `NaoMaisParado`; 3 conflitos → `Falhou`.

  T010 deve passar.
- [ ] T017 [P] [US2] Opções (R-10): `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesReconciliacao.cs` com `const string Secao = "Reconciliacao"`, as seis chaves com os padrões e a faixa de cada uma, propriedades `TimeSpan` derivadas, e `ValidadorOpcoesReconciliacao : IValidateOptions<OpcoesReconciliacao>` que acumula as falhas, como `ValidadorOpcoesMensageria`, exigindo `LimiteEsgotamentoSegundos > LimiteParadoSegundos`; em `src/PedidoFulfillment.Worker/appsettings.json`, a seção `Reconciliacao` com os padrões. T009 deve passar.
- [ ] T018 [US2] Infrastructure da reconciliação (R-05, R-08, R-09). **Primeiro**, em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaWorker.cs`, `Reconciliacao:Habilitada=false` por padrão, com um jeito de ligar e ajustar a seção por teste (como a fábrica já faz para a mensageria) e o comentário citando o R-10, para a suíte nunca rodar com a reconciliação ligada sobre pedidos com datas fixas (analyze O1). Depois:
  - `src/PedidoFulfillment.Infrastructure/Reconciliacao/ConsultaPedidosParados.cs`: `IConsultaPedidosParados` com o `ContextoBancoDados`, `AsNoTracking`; duas consultas (fora e dentro de `comFalha`, a segunda só se sobrar espaço), cada uma ordenada por `AtualizadoEm` e limitada; a etapa por projeção condicional com os predicados do R-05 (estoque: `Status` em `Pendente`, `Processando`, `ProntoParaEnvio` e `!EstoqueReservado`; pagamento: `EstoqueReservado`, `Pagamento == null`, status não final; entrega: `EstoqueReservado`, `Pagamento.Situacao == Aprovado`, `OrdemEntrega == null`, `Processando` ou `ProntoParaEnvio`); conferir o SQL gerado no log de depuração do EF (sem avaliação no cliente);
  - `src/PedidoFulfillment.Infrastructure/Reconciliacao/ReconciliadorPedidos.cs`: `BackgroundService` com `IOptions<OpcoesReconciliacao>`, `TimeProvider`, `IServiceScopeFactory`, `RegistroFalhasReconciliacao` e o logger; se `!Habilitada`, termina sem fazer nada; primeiro ciclo na partida e depois `PeriodicTimer(IntervaloCiclo, timeProvider)`; cada ciclo: abre uma `SqlConnection` própria com a cadeia do banco e chama `sp_getapplock` (`'oms-reconciliacao'`, `Exclusive`, `Session`, timeout 0); retorno negativo → log 9006 e pula; poda o registro com `agora - LimiteEsgotamento`; lista o lote; para cada pedido, um escopo novo e `ServicoReconciliacao.ReconciliarAsync`; conta os resultados por etapa e escreve o 9004 (Warning se houve recuperado ou esgotado, Debug se não); exceção fora do laço → log 9005, sem derrubar o processo; a conexão é fechada no fim do ciclo (libera o lock);
  - `src/PedidoFulfillment.Infrastructure/Reconciliacao/ExtensoesReconciliacao.cs`: `AdicionarReconciliacao()` registra as opções com validação na partida (`ValidateOnStart`), o validador, `RegistroFalhasReconciliacao` (singleton), `IConsultaPedidosParados` e `ServicoReconciliacao` (escopo), `TimeProvider.System` se ainda não registrado e o hosted service;
  - `src/PedidoFulfillment.Worker/ProgramaWorker.cs`: chama `AdicionarReconciliacao()` depois dos consumidores.
- [ ] T019 [US2] Partida com opção inválida (FR-014, analyze C2): em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/ReconciliacaoEtapasTestes.cs`, o Worker com `Reconciliacao:TamanhoLote=0` não sobe (`FalhaPartida`), com a chave na mensagem. Rodar a suíte inteira: T011 e T012 devem passar, e os testes do REQ-06 ao REQ-11 continuam verdes.

**Checkpoint**: pedidos parados voltam ao fluxo, e os das etapas de estoque e pagamento se esgotam. Commit `feat(mensageria): reconcilia pedidos parados por evento perdido [REQ-12]`.

---

## Phase 5: User Story 3 - Reconciliação visível para a operação (Priority: P2)

**Goal**: os logs da reconciliação e da falha sem rota trazem identificadores, etapa, resultado e contagens, sem dados pessoais (FR-015).

**Independent Test**: um pedido parado em cada etapa; ler os logs capturados e conferir um 9001 por pedido e um 9004 com o total por etapa.

### Testes da US3

- [ ] T020 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/LogsReconciliacaoTestes.cs`, com `CapturaLogs`:
  - um pedido parado em cada etapa: três 9001 (Warning) com o `PedidoId` e a etapa, e um 9004 (Warning) com `Estoque=1`, `Pagamento=1`, `Entrega=1` (cenários 1 e 2 da US3);
  - ciclo sem pedidos parados: 9004 em Debug, nenhum Warning da faixa 9xxx;
  - esgotamento (reaproveitar o cenário da T012): 9002 (Error) com o `PedidoId`, a etapa e `Falhas=3`; cada falha anterior com 9003 e a contagem (cenário 4);
  - nenhuma mensagem da faixa 9xxx contém e-mail, nome do cliente, endereço ou valores do pedido semeado.
- [X] T021 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/PublicacaoSemFilaTestes.cs`, no cenário da fila apagada: o log 5002 traz o identificador e o tipo do evento e o motivo `SemRota` (cenário 3 da US3).

### Implementação da US3

- [ ] T022 [US3] Ajustar os campos e níveis dos logs de `EventosReconciliacao` e do 5002 em `DespachanteEventosSaida` ao que T020 e T021 pedirem, sem acrescentar dados pessoais. Rodar a suíte inteira no checkpoint.

**Checkpoint**: a operação vê o que foi recuperado e o que se esgotou. Commit `test(mensageria): confere os logs da reconciliacao e da publicacao sem rota [REQ-12]`.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T023 [P] Desempenho do fluxo normal (SC-008): rodar `tests/PedidoFulfillment.Testes.Integracao/Entregas/DesempenhoEntregaTestes.cs` e registrar em Notes o p95 do pedido criado ao `entrega.criada`, comparando com o REQ-10 (meta de 10 s).
- [ ] T024 [P] Documentação (R-14):
  - `specs/006-mensageria/spec.md` › Pendências futuras: o item "evento publicado antes de a fila existir" aponta para o REQ-12 (resolvido); limpeza e DLQ continuam pendentes;
  - `specs/010-fulfillment/spec.md` › Assumptions: nota de que o REQ-12 recupera os pedidos pagos sem ordem;
  - `specs/008-estoque/quickstart.md` e `specs/010-fulfillment/quickstart.md`: a nota "só crie pedidos depois de a fila existir" deixa de ser necessária (REQ-12);
  - `README.md`: linha do REQ-12 em **Estado atual**; a seção de configuração `Reconciliacao` com as chaves e os padrões; **Decisões**: filas declaradas pelo despachante, `mandatory` só para tipos com consumidor, reconciliação periódica com esgotamento; **Próximos passos**: o REQ-13 (cancelamento depois do pagamento, com estorno, que resolve a etapa de entrega que falha sempre), a visibilidade dos cancelamentos para outros sistemas, a limpeza e a DLQ (REQ-14) e a #18; **Roteiros de validação**: o quickstart do REQ-12.
- [ ] T025 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados); registrar a contagem em Notes.
- [ ] T026 Executar o quickstart (§1 a §4) com a API e o Worker locais e registrar em Notes: broker vazio com `pedido.criado` pendente, fila apagada com o Worker no ar (5002 `SemRota` e fila recriada) e pedido pago sem ordem recuperado (9001, 9004). O §5 é coberto pela T012.
- [ ] T027 Fazer push e abrir o PR com `Closes #26`. Acompanhar o workflow `build-testes` (e os demais do repositório) até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: a linha de base vem antes de tudo.
- **US1** (T002 a T006): independente da US2. T004 antes de T005 e T006.
- **US2** (T007 a T019): independente da US1, exceto pela T014, que edita `AdicionarConsumidor` depois da T005 se a US1 vier primeiro. T013 antes de T016 (o esgotamento usa `RegistrarPagamentoNaoProcessado`); T014 e T015 antes de T016; T016 e T017 antes de T018; a `FabricaWorker` com a reconciliação desligada entra no início da T018, antes do registro no `ProgramaWorker`; T018 antes de T019.
- **US3**: depende da US1 (log 5002) e da US2 (logs 9xxx).
- **Polish**: depois de tudo. A T027 exige push.

### Parallel Opportunities

- T002 e T003; T004 com os testes da US1.
- T007, T008, T009, T010, T011 e T012 (arquivos diferentes).
- T013, T015 e T017.
- T020 e T021; T023 e T024.

---

## Implementation Strategy

### MVP

Setup e US1: nenhum evento com consumidor se perde por falta de fila. Sozinha, a US1 já
evita perdas novas; a US2 recupera as antigas.

### Incremental Delivery

1. Setup + US1 → commit `feat(mensageria): declara as filas dos consumidores e exige rota na publicacao [REQ-12]`.
2. US2 → commit `feat(mensageria): reconcilia pedidos parados por evento perdido [REQ-12]`.
3. US3 → commit `test(mensageria): confere os logs da reconciliacao e da publicacao sem rota [REQ-12]`.
4. Polish → `docs(mensageria): ... [REQ-12]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): limpeza do outbox e dos eventos processados,
  reprocessamento da DLQ, estorno, cancelamento da ordem de entrega, evento de cancelamento,
  reinscrição automática do consumidor numa fila recriada, alertas e métricas.
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em outra rodada.
- **SC-002 e SC-006** são metas de tempo que decorrem das opções (limite parado mais um ciclo;
  limite de esgotamento mais 3 ciclos). Não têm teste próprio: os parâmetros são cobertos pela
  T009 e o comportamento pela T012 (analyze C3).
- **Analyze (2026-10-23)**: aplicadas as correções U1 (lock ocupado determinístico no lugar do
  9006 no teste de duas instâncias), I1 (esgotamento com `DescartarAlteracoes`, sem escopo no
  Application), C1 (falha isolada e lote cheio de falhas na T012), C2 (partida com opção
  inválida na T019), O1 (`FabricaWorker` no início da T018), T1, I2, I3, I4 e P1 (spec,
  research, data-model e plan).
- **T001 (linha de base, 2026-10-23)**: 720 testes, 430 unitários e 290 de integração, zero
  ignorados, com o Docker no ar.
- **T003 e T021**: o teste da fila apagada já confere o log 5002 com o tipo e o motivo `SemRota`
  (T021 feita junto). O `RabbitMQ.Client` 7.2.2 lançou `PublishReturnException` como previsto
  no R-03. Checkpoint da US1: 734 testes (440 unitários e 294 de integração), todos verdes, sem
  ajuste nos testes existentes.
