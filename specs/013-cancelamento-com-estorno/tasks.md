---

description: "Lista de tarefas do REQ-13 — Cancelamento depois do pagamento, com estorno e cancelamento da ordem de entrega"
---

# Tasks: Cancelamento depois do pagamento, com estorno e cancelamento da ordem de entrega

**Input**: Design documents from `/specs/013-cancelamento-com-estorno/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/estorno-e-cancelamento.md](./contracts/estorno-e-cancelamento.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio, e o
FR-019 pede testes de integração, com banco e broker reais, do cancelamento de pedido pago,
do estorno pendente, das corridas, da idempotência e do esgotamento da entrega. Os testes de
cada fase vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário, depois de uma fase de fundação com o modelo do
estorno, a migration única e a porta do gateway. A US1 (estorno no cancelamento) é o MVP. A
US2 (ordem de entrega) e a US4 (idempotência) dependem só da fundação. A US3 (esgotamento da
entrega) usa o `Pedido.Cancelar` da US1.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; regra no domínio e no Application; Infrastructure só com banco, host e
  o gateway simulado.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo. Uma migration só (`EstornoPagamento`, T006).
- Contratos de eventos v1 inalterados (FR-017): `SchemaEventosTestes` continua verde sem
  edição, e nenhum evento novo é gravado no outbox pelo cancelamento ou pelo estorno.
- Contrato HTTP v1: a única mudança é `EntregaPedidoResponseDTO.canceladaEm`, opcional e
  anulável (T024). As respostas do `PATCH /status` não mudam (clarify Q3).
- As situações `EstornoPendente` e `Estornado` não aparecem na API nem nos eventos (FR-018).
- Logs sem identificador de transação, de estorno ou dado pessoal (FR-016, R-11).
- Os testes existentes do REQ-05 ao REQ-12 passam sem afrouxar asserções.
- Os testes com banco e broker reais usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER`
  (REQ-07). Nenhuma espera fixa em teste: espere por condição, com prazo.
- Commits: `tipo(escopo): descrição [REQ-13]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-13-cancelamento-com-estorno` e registrar em Notes a contagem de testes como linha de base (unitários, integração e ignorados; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: o modelo do estorno, a coluna da ordem cancelada e a porta do gateway, usados por
todas as histórias. Nenhuma regra de cancelamento ainda.

### Testes da fundação

- [X] T002 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEstornoTestes.cs` (R-02, data-model), com um `Pagamento` aprovado (`RegistrarResultado(Aprovado, "SIM-1", t0)`):
  - `SolicitarEstorno(t1)`: situação `EstornoPendente`, `EstornoSolicitadoEm = t1`, `TentativasEstorno = 0`, `UltimaFalhaEstornoEm` nulo, `AtualizadoEm = t1`; o `IdentificadorTransacao` da cobrança continua;
  - `SolicitarEstorno` fora de `Aprovado` (`Pendente`, `Recusado`, `NaoProcessado`, `EstornoPendente`, `Estornado`): `InvalidOperationException` sem alterar nada;
  - `RegistrarFalhaEstorno(t2)` duas vezes: `TentativasEstorno = 2`, `UltimaFalhaEstornoEm` = a última, situação continua `EstornoPendente`; fora de `EstornoPendente`: `InvalidOperationException`;
  - `ConfirmarEstorno("SIM-EST-1", t3)`: situação `Estornado`, `IdentificadorEstorno` aparado, `EstornadoEm = t3`; identificador vazio ou com mais de 100 caracteres: `ArgumentException`; fora de `EstornoPendente`: `InvalidOperationException`;
  - `RegistrarResultado` continua recusando `EstornoPendente` e `Estornado` (`ArgumentOutOfRangeException`): só as transições acima levam a eles;
  - instantes fora de UTC são recusados como nos demais métodos (`Guardas.Utc`).
- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/GatewayPagamentoSimuladoTestes.cs` (R-03): `EstornarAsync` confirma com identificador começando por `SIM-EST-` e com até 100 caracteres. A idempotência fica para a US4 (T030).

### Implementação da fundação

- [X] T004 Em `src/PedidoFulfillment.Domain/Pagamentos/SituacaoPagamento.cs`, acrescentar `EstornoPendente` e `Estornado` depois de `NaoProcessado` (sem renumerar os existentes), com XML doc citando o REQ-13 e que são situações internas. Em `src/PedidoFulfillment.Domain/Pagamentos/Pagamento.cs` (data-model):
  - campos `EstornoSolicitadoEm` (`DateTimeOffset?`), `IdentificadorEstorno` (`string?`, "`nvarchar(100)?` — aparado, até 100", constante `TamanhoMaximoIdentificadorEstorno = 100`), `EstornadoEm` (`DateTimeOffset?`), `TentativasEstorno` (`int`, "padrão 0, `>= 0`") e `UltimaFalhaEstornoEm` (`DateTimeOffset?`), todos com `private set`;
  - `SolicitarEstorno(DateTimeOffset)`, `RegistrarFalhaEstorno(DateTimeOffset)` e `ConfirmarEstorno(string, DateTimeOffset)`, com as validações antes de qualquer mudança e o diagrama do data-model;
  - `RegistrarResultado` mantém a lista de situações aceitas (`Aprovado`, `Recusado`, `NaoProcessado`).

  T002 deve passar.
- [X] T005 Em `src/PedidoFulfillment.Domain/Entregas/OrdemEntrega.cs`, campo `CanceladaEm` (`DateTimeOffset?`, "Preenchido no cancelamento do pedido; só se `DespachadoEm` é nulo"), sem método ainda (o `Cancelar` entra na US2, T022).
- [X] T006 Persistência (R-08):
  - `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoPagamento.cs`: `IdentificadorEstorno` com `HasMaxLength(Pagamento.TamanhoMaximoIdentificadorEstorno)`; `TentativasEstorno` com padrão 0 e check `CK_Pagamentos_TentativasEstornoNaoNegativas` (`[TentativasEstorno] >= 0`); `CK_Pagamentos_SituacaoValida` passa a incluir as situações novas (vem de `RestricoesEnum.Em<SituacaoPagamento>`); índice `IX_Pagamentos_EstornoPendente` em (`UltimaFalhaEstornoEm`, `EstornoSolicitadoEm`) com `HasFilter("[Situacao] = 'EstornoPendente'")`;
  - `ConfiguracaoOrdemEntrega.cs`: nada além do mapeamento padrão de `CanceladaEm`;
  - migration `EstornoPagamento` em `src/PedidoFulfillment.Infrastructure/Persistencia/Migracoes/` (`dotnet ef migrations add EstornoPagamento`), conferindo que só acrescenta as colunas, recria a check constraint e cria o índice; snapshot atualizado;
  - `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ModeloSincronizadoTestes.cs` e `MigracaoBancoTestes.cs` continuam verdes; em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/PagamentoEntregaTestes.cs`, um caso que grava e relê um pagamento `EstornoPendente` com todos os campos novos e uma ordem com `CanceladaEm`, e outro em que `TentativasEstorno = -1` é recusado pelo banco.
- [X] T007 Porta do gateway (R-03, contrato) em `src/PedidoFulfillment.Application/Pagamentos/IGatewayPagamento.cs`:
  - `Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken)`;
  - `sealed record ResultadoEstorno(bool Confirmado, string? IdentificadorEstorno, string? Motivo)`, com `Confirmado(string)` e `Recusado(string)`, como `ResultadoCobranca`;
  - XML doc: `pedidoId` é a chave de idempotência da cobrança e do estorno; exceção é falha técnica.

  Em `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoSimulado.cs`, `EstornarAsync` sempre confirma com `SIM-EST-{Guid.CreateVersion7():N}` (sem memória ainda: US4). Atualizar os gateways falsos dos testes que implementam a interface (`tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/EsgotamentoTestes.cs`, `Pagamentos/PedidoIgnoradoNoPagamentoTestes.cs`, `Infraestrutura/ServicosFalsos.cs` e os falsos dos unitários). T003 deve passar.

**Checkpoint**: suíte inteira verde, nenhum comportamento mudou. Commit `feat(pagamentos): modela o estorno do pagamento e a porta do gateway [REQ-13]`.

---

## Phase 3: User Story 1 - Cancelar um pedido pago estorna o valor (Priority: P1) 🎯 MVP

**Goal**: o cancelamento de pedido pago grava o estorno pendente com o cancelamento e a devolução do estoque, sem chamar o gateway; o Worker estorna, tenta de novo sem limite e alerta depois de 24 h e 3 tentativas (FR-001 a FR-007).

**Independent Test**: pedido pago em `Processando`; cancelar pela API como operação dá 200, `Cancelado`, estoque devolvido e pagamento `EstornoPendente`; com o processador de estornos ligado, o pagamento fica `Estornado`, com um único estorno no gateway.

### Testes da US1

- [X] T008 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoCancelamentoTestes.cs` (R-01, data-model), `Pedido.Cancelar(autorId, motivo, produtos, instante)`:
  - `Pendente` sem reserva nem pagamento: só o status muda, histórico com autor e motivo aparado; `ResultadoCancelamento` sem estoque devolvido, sem estorno, sem ordem cancelada;
  - com estoque reservado e sem pagamento (`Pendente` ou `Processando` movido pela operação): estoque devolvido (quantidades conferidas) e `EstoqueReservado = false`;
  - pago (`Processando`, pagamento `Aprovado`): estoque devolvido, pagamento `EstornoPendente` com `EstornoSolicitadoEm = instante`, status `Cancelado`; `ResultadoCancelamento.EstornoSolicitado` verdadeiro;
  - sem autor (`null`): histórico sem autor (uso da reconciliação);
  - `Despachado`, `Cancelado` e `PagamentoRejeitado`: `InvalidOperationException` sem alterar nada (estoque, pagamento e histórico conferidos);
  - pagamento `Recusado` ou `NaoProcessado` num pedido não final não acontece pelo fluxo, mas, se acontecer, `Cancelar` não pede estorno (FR-004);
  - produto ausente no dicionário com estoque reservado: `InvalidOperationException` sem alterar nada;
  - `AtualizadoEm = instante` em todos os casos que gravam.
- [X] T009 [P] [US1] No mesmo arquivo, os métodos de estorno do agregado (data-model): `ConfirmarEstorno(id, instante)` e `RegistrarFalhaEstorno(instante)` delegam ao pagamento e atualizam `AtualizadoEm`; sem pagamento ou fora de `EstornoPendente`: `InvalidOperationException`. `RegistrarCobrancaAposCancelamento(transacao, instante)` (R-07): pedido `Cancelado` sem pagamento ganha pagamento com `ValorTotal`, transação aparada, `Aprovado` e logo `EstornoPendente`; status e histórico não mudam; pedido não cancelado, ou já com pagamento: `InvalidOperationException`.
- [X] T010 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosStatusTestes.cs`, com os falsos de `Pedidos/Falsos.cs`: cancelar pedido pago como operação devolve 200, o pedido fica `Cancelado` com o pagamento `EstornoPendente`, o gateway nunca é chamado (o serviço da API nem o recebe) e o log 7005 sai com a origem `Operacao`; Cliente cancelando pedido pago continua 403; conflito seguido de releitura que encontra o pedido pago (a cobrança venceu) pede o estorno na segunda tentativa. Em `tests/PedidoFulfillment.Testes.Integracao/Arquitetura/DependenciasCamadasTestes.cs`, um caso que garante que nenhum construtor do `ServicoPedidos` recebe `IGatewayPagamento` e que a API não registra o gateway (SC-006: o cancelamento não depende do gateway).
- [X] T011 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ServicoEstornosTestes.cs` (R-04), com armazenamento e gateway falsos e `FakeTimeProvider`:
  - pedido com `EstornoPendente`: chama `EstornarAsync` uma vez, com o `pedidoId`, a transação e o valor da cobrança; confirmado, o pagamento fica `Estornado` e o log 7006 sai;
  - pagamento que já não está `EstornoPendente` (ou pedido inexistente): não chama o gateway e devolve "nada a fazer";
  - recusa e exceção do gateway: descarta o rastreado, relê, `TentativasEstorno + 1` e grava; log 7007 (Warning) com as tentativas;
  - com `agora - EstornoSolicitadoEm` acima de `LimiteAlerta` **e** `TentativasEstorno` (depois da falha) `>= TentativasAlerta`: log 7008 (Error) em vez do 7007; só um dos dois limites cumprido: 7007;
  - conflito ao gravar a confirmação: relê e grava de novo sem chamar o gateway, até 3 vezes; esgotadas, a exceção sobe (a rodada conta como falha do pedido);
  - cancelamento do `CancellationToken` não é contado como falha.
- [X] T012 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ProcessamentoPagamentoTestes.cs` (R-07): gateway aprova, a gravação dá conflito e a releitura encontra o pedido `Cancelado` sem pagamento → `RegistrarCobrancaAposCancelamento`, gravado sem `pagamento.aprovado` no outbox e com o log 7009; o gateway é chamado uma vez só. Com recusa no mesmo cenário: nada gravado, como hoje. Os demais casos de "não apto" seguem como hoje.
- [X] T013 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesEstornosTestes.cs` (R-09), como `OpcoesReconciliacaoTestes`: padrões (`Habilitado` true, 60, 100, 86 400, 3) válidos; cada faixa nos limites ("1 a 3 600", "1 a 1 000", "1 a 2 592 000", "1 a 100") e fora deles, com a mensagem citando a chave (por exemplo, "Estornos:TamanhoLote deve estar entre 1 e 1000."); várias falhas acumuladas numa só validação.
- [X] T014 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Estornos/CancelamentoPedidoPagoTestes.cs`, com o cenário novo `tests/PedidoFulfillment.Testes.Integracao/Estornos/CenarioEstorno.cs` (semeia o pedido pago pelo fluxo real: Worker de estoque e pagamento com `TaxaAprovacaoPercentual` 100), banco e broker reais:
  - operação cancela pedido `Processando` pago: 200 com o mesmo corpo de hoje; no banco, `Cancelado`, estoque dos produtos de volta, pagamento `EstornoPendente`, nenhum evento novo no outbox (US1 cenário 1);
  - pedido `Pendente` sem pagamento: nada de estorno (cenário 4);
  - `PagamentoRejeitado`: 409, nada muda (cenário 5);
  - Cliente dono cancelando pedido pago: 403 (cenário 9, clarify Q3);
  - a consulta do pedido cancelado não mostra pagamento nem estorno (clarify Q2).
- [X] T015 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs`, com a `FabricaWorker` e `Estornos:Habilitado=true` só aqui:
  - pedido cancelado pago: em até um intervalo curto (`Estornos:IntervaloSegundos=1`), pagamento `Estornado`, com `IdentificadorEstorno` e `EstornadoEm`; logs 7006 e 7012 (cenário 2);
  - gateway falso que falha: pagamento continua `EstornoPendente`, `TentativasEstorno` cresce a cada rodada, log 7007; com `LimiteAlertaSegundos` baixo e `TentativasAlerta=2`, log 7008 a partir da segunda falha; quando o falso volta a confirmar, `Estornado` (cenários 6 e 7);
  - Worker fora do ar: o cancelamento pela API funciona e o estorno fica pendente; o Worker sobe e confirma na primeira rodada (Edge Cases);
  - dois Workers ao mesmo tempo: um único `EstornarAsync` para o pedido no gateway falso que conta chamadas, e um único 7006 (FR-005); lock ocupado numa conexão sem pool: o Worker pula a rodada (log 7011), como no REQ-12;
  - os que já falharam vão para o fim do lote: com `TamanhoLote=1`, um pedido que falha sempre e um pendente novo, o novo é confirmado na rodada seguinte;
  - falha da rodada inteira: com uma `IConsultaEstornosPendentes` falsa que lança exceção, registrada na `FabricaWorker`, o log 7010 sai e a rodada seguinte roda normalmente, sem derrubar o Worker.
- [X] T016 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Estornos/DisputaCancelamentoTestes.cs` (Edge Cases), com sincronização determinística (gateway falso que espera um sinal, como nos testes de disputa do REQ-08):
  - cancelamento × cobrança: cancelamento grava antes → o consumidor não cobra; cobrança grava antes → o cancelamento relê e pede o estorno;
  - cobrança aprovada para pedido cancelado no meio do caminho (R-07): pagamento registrado `EstornoPendente` no pedido `Cancelado`, sem `pagamento.aprovado` no outbox; log 7009;
  - dois cancelamentos simultâneos do mesmo pedido pago: um 200 e um 409, um único pagamento `EstornoPendente` (US1 cenário 8);
  - esgotamento da entrega pela reconciliação e cancelamento pela API ao mesmo tempo sobre o mesmo pedido (Edge Cases): um dos dois vence, o pedido fica `Cancelado` uma vez, com um único pagamento `EstornoPendente`. Este caso depende da T029 (US3) e entra no checkpoint da US3.

### Implementação da US1

- [X] T017 [US1] Domínio (R-01, data-model):
  - `src/PedidoFulfillment.Domain/Pedidos/ResultadoCancelamento.cs`: `readonly record struct ResultadoCancelamento(bool EstoqueDevolvido, bool EstornoSolicitado, bool OrdemCancelada)`;
  - `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`: `Cancelar(Guid? autorId, string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)` na ordem do R-01 (passo 4, a ordem, entra na T022), com todas as validações antes de qualquer mudança; `RegistrarCobrancaAposCancelamento`, `ConfirmarEstorno` e `RegistrarFalhaEstorno`, todos atualizando `AtualizadoEm`.

  T008 e T009 devem passar.
- [X] T018 [US1] Logs (R-11) em `src/PedidoFulfillment.Application/Pagamentos/EventosPagamento.cs`: 7005 `EstornoSolicitado` (Information; pedido, valor, origem), 7006 `EstornoConfirmado` (Information; pedido, valor, tentativas anteriores), 7007 `FalhaEstorno` (Warning; pedido, tentativas, erro), 7008 `EstornoPendenteAlemDoLimite` (Error; pedido, tentativas, horas pendente), 7009 `CobrancaAprovadaParaPedidoCancelado` (Warning; pedido, valor), 7010 `FalhaRodadaEstornos` (Warning), 7011 `RodadaEstornosIgnorada` (Debug), 7012 `ResumoEstornos` (nível como parâmetro, como o 9004; confirmados e falhas). `src/PedidoFulfillment.Application/Pagamentos/OrigemEstorno.cs`: `enum OrigemEstorno { Operacao = 1, Reconciliacao, CobrancaAposCancelamento }`. Nenhum log com transação ou identificador de estorno.
- [X] T019 [US1] `src/PedidoFulfillment.Application/Pedidos/ServicoPedidos.cs`: em `AtualizarStatusAsync`, quando o status pedido é `Cancelado`, ler os produtos para alteração se houver estoque reservado e chamar `pedido.Cancelar(usuarioId, motivo, produtos, agora)` no lugar de `DevolverEstoque` + `MudarStatus`; depois de gravar, logar 6005 se o estoque voltou e 7005 (origem `Operacao`) se o estorno foi pedido. As demais transições seguem com `MudarStatus`. XML doc citando o REQ-13. T010 e T014 devem passar, junto com `StatusPedidoComBancoTestes`, `DevolucaoEstoqueTestes` e `AtualizarStatusPedidoTestes` sem edição.
- [X] T020 [US1] `src/PedidoFulfillment.Application/Pagamentos/ProcessamentoPagamento.cs` (R-07): na releitura depois de um conflito, se `resultado` é aprovado e o pedido está `Cancelado` sem pagamento, chamar `pedido.RegistrarCobrancaAposCancelamento(transacao, agora)`, gravar sem evento de saída, logar 7009 e 7005 (origem `CobrancaAposCancelamento`) e devolver `true`. T012 deve passar, e os testes existentes do consumidor continuam verdes.
- [X] T021 [US1] Processamento dos estornos (R-04, R-09, contrato):
  - `src/PedidoFulfillment.Application/Pagamentos/IConsultaEstornosPendentes.cs`: `Task<IReadOnlyList<Guid>> ListarAsync(int quantidade, CancellationToken)`;
  - `src/PedidoFulfillment.Application/Pagamentos/ServicoEstornos.cs`: decide um pedido (passos 1 a 5 do R-04), com `ParametrosAlertaEstorno(TimeSpan LimiteAlerta, int TentativasAlerta)`; devolve `ResultadoRodadaEstorno` (`Confirmado`, `NadaAFazer`, `Falhou`), para não confundir com o `ResultadoEstorno` do gateway;
  - `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesEstornos.cs` com `ValidadorOpcoesEstornos` (faixas do R-09, acumulando falhas);
  - `src/PedidoFulfillment.Infrastructure/Pagamentos/ConsultaEstornosPendentes.cs`: pagamentos `EstornoPendente`, até a quantidade, primeiro os que nunca falharam (`UltimaFalhaEstornoEm` nulo) por `EstornoSolicitadoEm`, depois os que já falharam por `UltimaFalhaEstornoEm` (o tentado há mais tempo primeiro), usando o índice filtrado (R-04);
  - `src/PedidoFulfillment.Infrastructure/Pagamentos/ProcessadorEstornos.cs`: `BackgroundService` com `PeriodicTimer`, primeira rodada na partida, `sp_getapplock 'oms-estornos'` numa conexão própria e `sp_releaseapplock` no fim (como `ReconciliadorPedidos`), um escopo por pedido, resumo 7012 e 7010 na falha da rodada;
  - `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs`: `AdicionarProcessamentoEstornos()` registra opções validadas na partida, consulta, serviço e hosted service;
  - `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaWorker.cs`: `Estornos:Habilitado=false` por padrão, antes do registro no Worker;
  - `src/PedidoFulfillment.Worker/ProgramaWorker.cs`: `AdicionarProcessamentoEstornos()` depois do gateway; `src/PedidoFulfillment.Worker/appsettings.json`: seção `Estornos` com os padrões.

  T011, T013, T015 e T016 devem passar. Rodar a suíte inteira no checkpoint.

**Checkpoint**: MVP — nenhum pedido pago fica cancelado sem estorno. Commit `feat(pagamentos): estorna o pagamento no cancelamento do pedido [REQ-13]`.

---

## Phase 4: User Story 2 - Cancelar um pedido com ordem de entrega cancela a ordem (Priority: P1)

**Goal**: o cancelamento de pedido com ordem sem despacho cancela a ordem na mesma gravação, e a consulta mostra `entrega.canceladaEm` (FR-008 a FR-010).

**Independent Test**: pedido em `ProntoParaEnvio` com ordem; cancelar pela API e conferir a ordem com `CanceladaEm` e a consulta com `entrega.canceladaEm`.

### Testes da US2

- [X] T022 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoOrdemEntregaTestes.cs` e `PedidoCancelamentoTestes.cs`: `OrdemEntrega.Cancelar(instante)` preenche `CanceladaEm` e `AtualizadoEm`; com `DespachadoEm` preenchido ou já cancelada: `InvalidOperationException`; `Pedido.Cancelar` em `ProntoParaEnvio` com ordem cancela a ordem (`ResultadoCancelamento.OrdemCancelada`), junto com o estorno e o estoque; sem ordem, `OrdemCancelada` falso.
- [X] T023 [P] [US2] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Estornos/CancelamentoPedidoPagoTestes.cs` (US2), com o pedido levado a `ProntoParaEnvio` pelo fluxo real (Worker completo):
  - cancelar: 200 com `entrega.canceladaEm` preenchido e transportadora, código e despacho nulos; no banco, a ordem com `CanceladaEm` e o pagamento `EstornoPendente`; log 8004 (cenários 1 e 2);
  - rastreio depois do cancelamento: 409 `PEDIDO_TRANSICAO_INVALIDA`, ordem sem mudança (cenário 3);
  - pedido `Despachado`: 409 "O pedido está em Despachado e não pode passar para Cancelado.", nada estornado (cenário 4);
  - em `DisputaCancelamentoTestes.cs`: cancelamento × despacho (um vence; nunca rastreio em pedido cancelado nem ordem cancelada despachada) e cancelamento × criação da ordem pelo consumidor (se a ordem é criada antes, o cancelamento a cancela; se o cancelamento vence, nenhuma ordem é criada).
- [X] T024 [P] [US2] Contrato: em `specs/001-contratos-api/contracts/openapi-v1.yaml`, `EntregaPedidoResponseDTO.canceladaEm` com `type: ['null', string]`, `format: date-time` e a descrição do contrato do REQ-13, fora de `required`. `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs` passa a cobrir um pedido cancelado com ordem (resposta real com `canceladaEm` conferida contra o schema).

### Implementação da US2

- [X] T025 [US2] `src/PedidoFulfillment.Domain/Entregas/OrdemEntrega.cs`: `Cancelar(DateTimeOffset)`. `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`: passo 4 do R-01 em `Cancelar` (validação de ordem despachada antes de qualquer mudança). T022 deve passar.
- [X] T026 [US2] `src/PedidoFulfillment.Application/Contratos/Pedidos/EntregaPedidoResponseDTO.cs`: `public DateTimeOffset? CanceladaEm { get; init; }`, com XML doc; `src/PedidoFulfillment.Application/Pedidos/MapeamentoPedido.cs`: mapear `CanceladaEm`; `src/PedidoFulfillment.Application/Entregas/EventosEntrega.cs`: log 8004 `OrdemEntregaCancelada` (Information; pedido), chamado pelo `ServicoPedidos` (e pela reconciliação na US3) quando `ResultadoCancelamento.OrdemCancelada`. T023 e T024 devem passar; `SerializacaoPedidoTestes` e `ObterPedidoTestes` continuam verdes.

**Checkpoint**: commit `feat(entregas): cancela a ordem de entrega junto com o pedido [REQ-13]`.

---

## Phase 5: User Story 3 - A etapa de entrega se esgota na reconciliação, com estorno (Priority: P2)

**Goal**: pedido pago sem ordem, com 3 ciclos com falha e mais de 24 h parado, é cancelado sem autor, com `Não foi possível criar a ordem de entrega.`, estoque devolvido e estorno pendente (FR-011, FR-012).

**Independent Test**: pedido pago sem ordem, com `AtualizadoEm` de mais de 24 h e a regra de entrega forçada a falhar; três ciclos da reconciliação e o pedido fica `Cancelado`, com o pagamento `EstornoPendente`.

### Testes da US3

- [X] T027 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Reconciliacao/ServicoReconciliacaoTestes.cs`: a etapa de entrega passa a se esgotar com os mesmos limites (3 falhas e mais de 24 h), chamando `Pedido.Cancelar` sem autor com `MotivoOrdemEntregaNaoCriada`; com 1 ou 2 falhas, não; pedido que saiu da etapa (`AptoParaOrdemEntrega` falso, por exemplo despachado) não é cancelado; os casos de estoque e pagamento continuam como estão. Ajustar o teste do REQ-12 que garantia "entrega nunca se esgota" para o comportamento novo, citando o FR-011 do REQ-13.
- [X] T028 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/EsgotamentoTestes.cs`, com a regra de entrega substituída por uma que sempre falha (último `IEtapaFluxoPedido` registrado, como no REQ-12): pedido pago sem ordem, com `AtualizadoEm` de 25 h, depois de 3 ciclos fica `Cancelado`, sem autor, com o motivo `Não foi possível criar a ordem de entrega.`, estoque devolvido, pagamento `EstornoPendente`, nenhum evento no outbox; logs 9002 (etapa `Entrega`) e 7005 (origem `Reconciliacao`); com o processador de estornos ligado, o pagamento chega a `Estornado` (US3 cenários 1 e 4). Gateway fora do ar (gateway falso que falha) não impede o cancelamento (cenário 3).

### Implementação da US3

- [X] T029 [US3] `src/PedidoFulfillment.Application/Reconciliacao/ServicoReconciliacao.cs` (R-06): constante `MotivoOrdemEntregaNaoCriada = "Não foi possível criar a ordem de entrega."`; `esgotavel` inclui `Entrega`; `ContinuaNaEtapa(Entrega) => pedido.AptoParaOrdemEntrega`; `CancelarAsync(Entrega)` lê os produtos para alteração e chama `pedido.Cancelar(null, MotivoOrdemEntregaNaoCriada, produtos, agora)`; depois de gravar, log 7005 (origem `Reconciliacao`). Atualizar o XML doc que dizia que a entrega nunca se esgota. T027 e T028 devem passar.

**Checkpoint**: commit `feat(reconciliacao): esgota a etapa de entrega com estorno [REQ-13]`.

---

## Phase 6: User Story 4 - O gateway não cobra nem estorna duas vezes a mesma coisa (Priority: P2)

**Goal**: o pedido é a chave de idempotência da cobrança e do estorno no gateway simulado (FR-013 a FR-015).

**Independent Test**: a mesma cobrança e o mesmo estorno pedidos duas vezes para o mesmo pedido devolvem o mesmo resultado, sem nova transação.

### Testes da US4

- [ ] T030 [P] [US4] Em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/GatewayPagamentoSimuladoTestes.cs`: com `Random` de semente e taxa 50, a segunda `CobrarAsync` do mesmo pedido devolve o mesmo resultado (aprovação com a mesma transação, ou a mesma recusa), e pedidos diferentes sorteiam separadamente; `EstornarAsync` repetido devolve o mesmo identificador de estorno; chamadas concorrentes para o mesmo pedido (`Task.WhenAll`) devolvem um único resultado (US4 cenários 1 e 2).
- [ ] T031 [P] [US4] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs` (US4 cenário 3), com um armazenamento que falha na gravação da primeira confirmação: o gateway simulado confirma, a gravação falha, a rodada seguinte recebe o mesmo `IdentificadorEstorno` e o pagamento fica `Estornado` uma vez. Em `tests/PedidoFulfillment.Testes.Integracao/Reconciliacao/ReconciliacaoEtapasTestes.cs` (cenário 4): o consumidor cobra e "perde" a gravação; a reconciliação cobra o mesmo pedido e recebe a mesma transação.

### Implementação da US4

- [ ] T032 [US4] `src/PedidoFulfillment.Infrastructure/Pagamentos/GatewayPagamentoSimulado.cs` (R-03): dois `ConcurrentDictionary<Guid, ...>` (`GetOrAdd` com `Lazy` para concorrência), um para a cobrança e um para o estorno, por `pedidoId`; XML doc com a limitação da memória perdida no reinício (spec, Assumptions). T030 e T031 devem passar.

**Checkpoint**: commit `feat(pagamentos): torna a cobranca e o estorno idempotentes por pedido no gateway [REQ-13]`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T033 [P] Desempenho (SC-007): rodar `tests/PedidoFulfillment.Testes.Integracao/Entregas/DesempenhoEntregaTestes.cs` e registrar em Notes o p95 do pedido criado ao `entrega.criada`, comparando com o REQ-12 (meta de 10 s).
- [ ] T034 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo mensagens, códigos, limites e IDs de log no código e os links relativos:
  - `docs/fluxos/03-consulta-e-mudanca-de-status.md`: cancelamento com estorno pendente e ordem cancelada; `entrega.canceladaEm` no exemplo da consulta; linhas de concorrência "Cancelamento depois do pagamento" e "depois de `ProntoParaEnvio`" com o comportamento novo; logs 7005 e 8004; Limitações sem o item do estorno (#28 no lugar) e com os pedidos antigos sem estorno automático (R-10);
  - `docs/fluxos/05-pagamento.md`: estorno (situações, rodada, nova tentativa, alerta), chave de idempotência por pedido (a pendência da linha "Worker cai depois da decisão" sai, com o #28 no lugar), cobrança aprovada para pedido cancelado (R-07) e a limitação aceita dela; configuração `Estornos`; logs 7005 a 7012; Limitações atualizadas;
  - `docs/fluxos/06-ordem-de-entrega.md`: ordem cancelada; Limitações sem os dois itens do #28;
  - `docs/fluxos/09-reconciliacao.md`: entrega esgotável no diagrama e nas tabelas; Limitações sem o item da entrega;
  - fluxo novo `docs/fluxos/10-estorno.md`, no formato dos demais (caminho feliz, resultados, idempotência e concorrência, falhas externas, configuração, observabilidade, limitações), e uma linha no índice `docs/fluxos/README.md`; o diagrama de estados do pedido no índice não muda (os status são os mesmos), mas a nota de `Cancelado` cita o estorno.
- [ ] T035 [P] Documentação do projeto:
  - `README.md`: linha do REQ-13 em **Estado atual**; seção de configuração `Estornos`; **Decisões**: estorno assíncrono pelo Worker e chave de idempotência por pedido; **Próximos passos**: #29 (visibilidade dos cancelamentos, agora também dos estornos), #30 (REQ-14) e #18; **Roteiros de validação**: o quickstart do REQ-13;
  - `specs/009-pagamento/spec.md` e `research.md` (R-05): a chave de idempotência pendente aponta para o REQ-13;
  - `specs/012-publicacao-garantida/spec.md` › Assumptions: a pendência da etapa de entrega aponta para o REQ-13.
- [ ] T036 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados); registrar a contagem em Notes.
- [ ] T037 Executar o quickstart (§2 e §3) com a API e o Worker locais e registrar em Notes: cancelamento de pedido pago com ordem (200, `canceladaEm`, 7005, 8004 e, em até 1 min, 7006) e estorno com o Worker parado. O §4 é coberto pela T028.
- [ ] T038 Fazer push e abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #28`, citando em Validação os documentos de fluxo atualizados. Acompanhar o workflow `build-testes` e o `contrato-compatibilidade` até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup**: a linha de base vem antes de tudo.
- **Foundational** (T002 a T007): bloqueia todas as histórias. T004 antes de T006 (as colunas vêm do domínio); T005 antes de T006 (migration única).
- **US1** (T008 a T021): depende da fundação. T017 antes de T019 e T020; T018 antes de T019 a T021; T021 por último.
- **US2** (T022 a T026): depende da fundação e do `Pedido.Cancelar` da T017. Pode correr junto da T018 a T021.
- **US3** (T027 a T029): depende da T017 (e da T021 para o caso com o processador ligado na T028).
- **US4** (T030 a T032): depende só da fundação; a parte de integração da T031 usa o processador da T021.
- **Polish**: depois de tudo. A T038 exige push.

### Parallel Opportunities

- T002 e T003.
- T008 a T016 (arquivos diferentes; T008 e T009 no mesmo arquivo, escritos juntos).
- T022, T023 e T024; T027 e T028; T030 e T031.
- T033, T034 e T035.

### Exemplo: US1

```text
Juntos: T011 (ServicoEstornosTestes), T013 (OpcoesEstornosTestes), T014 (CancelamentoPedidoPagoTestes)
Depois: T017 → T018 → T019 e T020 → T021
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: nenhum pedido pago fica cancelado sem estorno, e o cancelamento não
depende do gateway. A US2 limpa a ordem de entrega, a US3 fecha a etapa de entrega na
reconciliação e a US4 protege contra cobrança e estorno repetidos num gateway real.

### Incremental Delivery

1. Setup + fundação → `feat(pagamentos): modela o estorno do pagamento e a porta do gateway [REQ-13]`.
2. US1 → `feat(pagamentos): estorna o pagamento no cancelamento do pedido [REQ-13]`.
3. US2 → `feat(entregas): cancela a ordem de entrega junto com o pedido [REQ-13]`.
4. US3 → `feat(reconciliacao): esgota a etapa de entrega com estorno [REQ-13]`.
5. US4 → `feat(pagamentos): torna a cobranca e o estorno idempotentes por pedido no gateway [REQ-13]`.
6. Polish → `docs(fluxos): ... [REQ-13]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): estorno parcial, devolução de pedido despachado,
  eventos de cancelamento e de estorno (#29), limpeza e DLQ (#30), gateway real e o estorno
  automático de pedidos cancelados antes do REQ-13 (R-10).
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em outra rodada.
- **SC-002 e SC-005** decorrem das opções (intervalo da rodada; limite de esgotamento mais 3
  ciclos). Os parâmetros são cobertos pela T013 e o comportamento pela T015 e pela T028.
- **Analyze (2026-10-24)**: aplicadas as correções I1 (cenário do Cliente renumerado para 9),
  I2 (FR-003 com limite de alerta próprio), I3 (FR-015: falhas forçadas por gateway falso),
  C1 (disputa entre reconciliação e API na T016), U1 (ordem da fila de estornos no R-04 e na
  T021), U2 (conflito ao gravar a falha no R-04), U3 (falha da rodada por consulta falsa na
  T015) e C2 (dependência da API sem gateway na T010).
- **T001 (linha de base, 2026-10-24)**: 800 testes, 482 unitários e 318 de integração, zero
  ignorados, com o Docker no ar.
- **Fundação (T002 a T007)**: o teste de persistência do REQ-04 usava `Estornado` como
  situação inválida; passou a usar `Cancelado`, que continua inválida. Os falsos do gateway
  nos testes ganharam `EstornarAsync`. Checkpoint: 514 unitários (com os testes de domínio da
  US1 já escritos) e 321 de integração, todos verdes.
- **US1 (2026-10-24)**:
  - `EventosPagamento` passou a ser `public`, como `EventosReconciliacao`: o `ProcessadorEstornos`
    fica no Infrastructure e escreve os logs 7010 a 7012;
  - o `ServicoEstornos` trata sozinho o conflito persistente ao gravar a confirmação: devolve
    `Falhou` com o log 7007, sem contar tentativa (o gateway confirmou), e a próxima rodada
    recebe o mesmo estorno. Na T011 isso substitui "a exceção sobe";
  - um erro inesperado num pedido (por exemplo, banco fora no meio) vai para o log 7010 com o
    identificador do pedido, e os demais pedidos da rodada continuam;
  - o consumidor de pagamento passou a ler o pedido uma vez por tentativa e decidir sobre ele:
    cancelado sem pagamento com cobrança aprovada em mãos registra o estorno pendente (R-07);
  - o caso "esgotamento × API" da T016 depende da US3 e entra no checkpoint dela (T028);
  - checkpoint: 540 unitários e 336 de integração, todos verdes; `dotnet format` limpo.
- **US2 (2026-10-24)**: a conformidade do OpenAPI compara o documento gerado com o contrato;
  com `canceladaEm` no DTO e no `openapi-v1.yaml`, `ConformidadeOpenApiTestes` continua verde
  sem edição, e a resposta real é conferida em `CancelamentoPedidoPagoTestes` (T024).
  `SerializacaoPedidoTestes` e `ObterPedidoTestes` continuam verdes sem edição.
- **US3 (2026-10-24)**: os testes do REQ-12 que garantiam "a entrega nunca se esgota"
  (`ServicoReconciliacaoTestes.Entrega_nunca_se_esgota` e
  `EsgotamentoTestes.Pedido_pago_cuja_ordem_falha_sempre_nunca_e_cancelado`) foram substituídos
  pelo comportamento novo (FR-011). O caso "esgotamento × API" da T016 ficou em
  `EsgotamentoTestes`, que tem os atalhos da reconciliação. Integração da reconciliação: 26 testes
  verdes.
