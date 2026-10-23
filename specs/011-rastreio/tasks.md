---

description: "Lista de tarefas do REQ-11 — Rastreio, despacho e histórico na consulta de pedidos"
---

# Tasks: Rastreio, despacho e histórico na consulta de pedidos

**Input**: Design documents from `/specs/011-rastreio/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/rastreio-http.md](./contracts/rastreio-http.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio
(despacho, correção, consulta) e testes de conformidade do contrato HTTP. A spec pede banco
real nos testes de integração e mede a consulta e o rastreio (SC-004). Os testes de cada fase
vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A fundação traz os DTOs novos e a leitura do pedido
com o histórico. A US1 (consulta) entrega o mapeamento que a US2 e a US3 devolvem nas
respostas. A US2 (despacho) e a US3 (correção) completam a mesma regra de domínio e o mesmo
método do serviço, por isso correm em sequência.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Nomes em português; endpoint só roteia; regra no domínio e no `ServicoPedidos`.
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- Nenhum pacote novo. Uma migration só (`RastreioSemCaixa`, T013).
- Contrato HTTP v1 só com acréscimos; o `specs/001-contratos-api/contracts/openapi-v1.yaml` muda
  junto com o código, e `ConformidadeOpenApiTestes` confere os dois (R-01).
- Histórico na resposta **sem o autor**, para todos os perfis (FR-003); motivo visível a todos
  (clarify).
- Os testes com banco real usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER` (REQ-07).
- Nenhuma espera fixa em teste: espere por condição, com prazo.
- Commits: `tipo(escopo): descrição [REQ-11]`.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-11-rastreio` e registrar em Notes a contagem de testes como linha de base (650 no fim do REQ-10: 383 unitários e 267 de integração; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

- [X] T002 [P] DTOs e código de erro (data-model, R-02, R-03):
  - `src/PedidoFulfillment.Application/Contratos/Pedidos/HistoricoStatusPedidoResponseDTO.cs`: `sealed record` com `StatusPedido? StatusAnterior`, `required StatusPedido StatusNovo`, `required DateTimeOffset OcorridoEm`, `string? Motivo`;
  - `src/PedidoFulfillment.Application/Contratos/Pedidos/EntregaPedidoResponseDTO.cs`: `string? Transportadora`, `string? CodigoRastreio`, `DateTimeOffset? DespachadoEm`, `required DateTimeOffset CriadoEm`;
  - `src/PedidoFulfillment.Application/Contratos/Pedidos/RegistrarRastreioRequestDTO.cs`: `sealed record ... : IValidatableObject`, como `AtualizarStatusPedidoRequestDTO`, com `Transportadora` (`[Required]`, `[MaxLength(100)]`, para o schema) e `CodigoRastreio` (`[Required]`, `[MaxLength(50)]`); em `Validate`, vazio ou só espaços → "obrigatório" e tamanho depois de `Trim()` acima do limite → erro no campo (R-09, analyze U1);
  - `PedidoResponseDTO`: `[Required] public IReadOnlyList<HistoricoStatusPedidoResponseDTO> Historico { get; init; } = [];` (não `required` em C#, R-03) e `public EntregaPedidoResponseDTO? Entrega { get; init; }`, depois de `AtualizadoEm`;
  - `CodigosErro.PedidoRastreioDuplicado = "PEDIDO_RASTREIO_DUPLICADO"`.
- [X] T003 [P] Leitura com o histórico (R-04): em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, `ObterPedidoAsync` inclui `.Include(p => p.Historico)` e usa `.AsSplitQuery()`; o XML doc de `IArmazenamentoPedidos.ObterPedidoAsync` cita o histórico (REQ-11). Rodar a suíte: tudo verde (o contrato ainda não mudou, então os campos novos ficam fora do documento gerado até a T007; se a conformidade reprovar por causa da T002, a T007 corrige).

**Checkpoint**: tipos novos e leitura completa do pedido.

---

## Phase 3: User Story 1 - Cliente acompanha o pedido com histórico e rastreio (Priority: P1) 🎯 MVP

**Goal**: a consulta (e toda resposta de pedido) traz o histórico em ordem, sem autor, e a entrega quando há ordem (FR-001 a FR-005).

**Independent Test**: criar um pedido, levá-lo a `ProntoParaEnvio` e consultá-lo como dono e como operação; conferir histórico e entrega.

### Testes da US1

- [X] T004 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosConsultaTestes.cs`:
  - histórico em ordem de `OcorridoEm` e, no empate, na ordem de criação, com `StatusAnterior` nulo na primeira entrada, `StatusNovo`, `OcorridoEm` e `Motivo` iguais aos do domínio;
  - nenhuma propriedade de autor no JSON serializado da resposta (`JsonSerializer` com `JsonSerializerDefaults.Web`; nenhum nome contendo `autor`);
  - sem ordem: `Entrega` nula; ordem sem despacho: `Transportadora`, `CodigoRastreio` e `DespachadoEm` nulos e `CriadoEm` da ordem; ordem despachada (`RegistrarDespacho`): os três preenchidos;
  - Cliente dono e operação (Admin, OperadorLogistica) recebem a mesma resposta; Cliente de outro pedido continua com `NaoEncontrado`.
- [X] T005 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ConsultaPedidoComBancoTestes.cs`: pedido criado pela API e movido pela operação (`PATCH /status` para `Processando` com motivo e depois `ProntoParaEnvio`); ordem inserida por SQL (`OrdensEntrega`, sem dados de despacho); a consulta como dono traz `historico` com 3 entradas na ordem, `statusAnterior` nulo na primeira, o motivo da operação visível ao Cliente, nenhuma propriedade de autor, e `entrega` com `criadoEm` e os três campos nulos; um pedido sem ordem traz `entrega: null`; Cliente de outro pedido recebe 404.

### Implementação da US1

- [X] T006 [US1] Em `src/PedidoFulfillment.Application/Pedidos/MapeamentoPedido.cs`: `Historico` ordenado por `OcorridoEm` e `Id`, mapeado para `HistoricoStatusPedidoResponseDTO` sem o autor; `Entrega` a partir de `pedido.OrdemEntrega` (ou nula). T004 deve passar.
- [X] T007 [US1] Contrato (contracts/rastreio-http.md, R-01, R-02):
  - em `specs/001-contratos-api/contracts/openapi-v1.yaml`: schemas `HistoricoStatusPedidoResponseDTO` e `EntregaPedidoResponseDTO`; em `PedidoResponseDTO`, `historico` (no `required`) e `entrega` (anulável); os exemplos `PedidoPendente` e `PedidoCancelado` ganham `historico` e `entrega: null`; a descrição do documento cita o REQ-11;
  - a forma de `null` segue o que o gerador produz (comparar com o documento gerado em `/openapi/v1.json` e ajustar o YAML, não o gerador, salvo se um transformador existente já trata o caso);
  - em `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs`, os dois schemas novos na lista de schemas conferidos, se a lista cobrir os de pedido.

  T005 e a conformidade devem passar.
- [X] T008 [US1] Revisar os testes que comparam a resposta do pedido campo a campo (`CriacaoPedidoComBancoTestes.cs`, `StatusPedidoComBancoTestes.cs`, `IdempotenciaComBancoTestes.cs` e os unitários de `ServicoPedidos*`) e ajustar o que falhar por causa dos campos novos, sem afrouxar as asserções existentes. Acrescentar em `ServicoPedidosIdempotenciaTestes.cs` o caso da resposta gravada sem `historico` nem `entrega` (JSON anterior ao REQ-11): a repetição devolve `Historico` vazio e `Entrega` nula, sem exceção (R-03). Rodar a suíte inteira no checkpoint.

**Checkpoint**: MVP — a consulta mostra histórico e entrega.

---

## Phase 4: User Story 2 - Logística despacha o pedido com transportadora e rastreio (Priority: P1)

**Goal**: `PATCH /api/v1/pedidos/{id}/rastreio` despacha um pedido `ProntoParaEnvio`, criando a ordem se faltar, com recusas por perfil, status, dados e duplicidade (FR-006 a FR-016).

**Independent Test**: despachar um pedido pronto como OperadorLogistica e conferir `Despachado`, histórico e entrega na resposta e na consulta.

### Testes da US2

- [ ] T009 [P] [US2] Testes de domínio em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoRastreioTestes.cs` (data-model, R-05), com o pedido levado a `ProntoParaEnvio` por `MudarStatus` (sem ordem) e por `CriarOrdemEntrega` (com ordem, como em `PedidoOrdemEntregaTestes`):
  - `PodeRegistrarRastreio` verdadeiro só em `ProntoParaEnvio` e `Despachado` (theory com os 6 status);
  - `ProntoParaEnvio` com ordem: devolve `Despachado`; ordem com a transportadora, o código e `DespachadoEm = instante`; status `Despachado`; histórico com `ProntoParaEnvio → Despachado`, o autor e o motivo `Pedido despachado.` (`Pedido.MotivoPedidoDespachado`); `AtualizadoEm = instante`;
  - `ProntoParaEnvio` sem ordem: a ordem é criada com `PedidoId = Id`, `CriadoEm = instante` e os dados do despacho;
  - `Pendente`, `Processando`, `Cancelado` e `PagamentoRejeitado`: `InvalidOperationException` sem alterar nada;
  - transportadora acima de 100 ou código acima de 50 caracteres: exceção das guardas, sem alterar nada.
- [ ] T010 [P] [US2] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosRastreioTestes.cs`, com o armazenamento falso de `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` (acrescentar `RastreiosEmUso` consultado por `RastreioEmUsoAsync` e `DuplicadoNaProximaGravacao`, que faz `SalvarAsync` lançar `RastreioDuplicadoException`):
  - pedido `ProntoParaEnvio`: sucesso com o pedido `Despachado`, uma gravação e log 4004 com resultado `Despachado`; transportadora e código gravados sem os espaços nas pontas;
  - pedido inexistente: `NaoEncontrado`;
  - `Pendente`, `Processando`, `Cancelado`, `PagamentoRejeitado`: `Conflito` com `PEDIDO_TRANSICAO_INVALIDA`, nenhuma gravação;
  - rastreio em uso por outro pedido: `Conflito` com `PEDIDO_RASTREIO_DUPLICADO`, nenhuma gravação, log 4005;
  - duplicado só na gravação (corrida): `Conflito` com `PEDIDO_RASTREIO_DUPLICADO`, alterações descartadas;
  - conflito de concorrência e, na releitura, o pedido foi cancelado: `Conflito` com `PEDIDO_TRANSICAO_INVALIDA`, nada gravado;
  - conflito de concorrência porque o consumidor de entrega criou a ordem ao mesmo tempo (analyze C1): na releitura, o pedido tem ordem, e o despacho grava sobre ela (uma ordem só);
  - conflito nas 3 tentativas: `Conflito` "alterado por outra operação".
- [ ] T011 [P] [US2] Teste de integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/RastreioPedidoComBancoTestes.cs` (`ColecaoBancoDados`, API com banco real; pedido levado a `ProntoParaEnvio` pela operação via `PATCH /status`, com ordem inserida por SQL quando o cenário pede):
  - OperadorLogistica e Admin (theory): 200, `status: Despachado`, histórico terminando em `ProntoParaEnvio → Despachado` com `Pedido despachado.`, entrega com os dados e `despachadoEm`; a consulta seguinte mostra o mesmo; uma linha em `OrdensEntrega`;
  - pedido sem ordem: 200, uma ordem criada com os dados e nenhum `entrega.criada` em `EventosSaida` para o pedido (spec, Assumptions; analyze G1);
  - Cliente dono: 403; sem token: 401; pedido inexistente: 404; identificador inválido: 400;
  - transportadora vazia, só espaços, com 101 caracteres; código com 51 caracteres: 400 com o campo em `erros`;
  - `Pendente` e `Cancelado`: 409 `PEDIDO_TRANSICAO_INVALIDA`, nada muda;
  - outro pedido já despachado com `Correios`/`BR123`: despachar com `correios`/`br123` dá 409 `PEDIDO_RASTREIO_DUPLICADO`, e o pedido continua `ProntoParaEnvio` sem rastreio;
  - `PATCH /status` `ProntoParaEnvio → Despachado` continua aceito (FR-009b);
  - corrida: 10 rodadas de despacho e cancelamento simultâneos (`Task.WhenAll`) do mesmo pedido `ProntoParaEnvio`; em todas, o pedido termina `Despachado` (cancelamento 409) ou `Cancelado` (despacho 409, sem transportadora nem código gravados na ordem) (SC-006).

### Implementação da US2

- [ ] T012 [US2] Domínio (R-05):
  - `src/PedidoFulfillment.Domain/Pedidos/ResultadoRastreio.cs`: `enum ResultadoRastreio { Despachado, Corrigido, SemMudanca }`;
  - em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`: `const string MotivoPedidoDespachado = "Pedido despachado."`; `bool PodeRegistrarRastreio => Status is StatusPedido.ProntoParaEnvio or StatusPedido.Despachado`; `ResultadoRastreio RegistrarRastreio(string transportadora, string codigoRastreio, Guid autorId, DateTimeOffset instante)`: fora de `PodeRegistrarRastreio`, `InvalidOperationException` com o número e o status; em `ProntoParaEnvio`, `OrdemEntrega ??= new OrdemEntrega(Id, instante)`, `OrdemEntrega.RegistrarDespacho(...)` e `MudarStatus(StatusPedido.Despachado, autorId, MotivoPedidoDespachado, instante)`; o caso `Despachado` lança `NotImplementedException` até a T021, sem teste ainda. As guardas da `OrdemEntrega` validam antes de qualquer mudança no pedido.

  T009 deve passar.
- [ ] T013 [P] [US2] Collation (R-06): em `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoOrdemEntrega.cs`, `Transportadora` e `CodigoRastreio` com `.UseCollation("SQL_Latin1_General_CP1_CI_AS")` e comentário citando o REQ-11 e o SKU (REQ-04, R-06); gerar a migration com `dotnet ef migrations add RastreioSemCaixa --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes` (analyze U2) e conferir que ela recria `IX_OrdensEntrega_Transportadora_CodigoRastreio` com o filtro. `ModeloSincronizadoTestes` deve passar.
- [ ] T014 [US2] Armazenamento (R-06):
  - `src/PedidoFulfillment.Application/Pedidos/RastreioDuplicadoException.cs` (como `ChaveIdempotenciaRegistradaException`);
  - `IArmazenamentoPedidos.RastreioEmUsoAsync(string transportadora, string codigoRastreio, Guid pedidoId, CancellationToken)`: existe ordem de **outro** pedido com o mesmo par (a collation da coluna cuida da caixa);
  - em `ArmazenamentoPedidos.SalvarAsync`, violação de unicidade de `IX_OrdensEntrega_Transportadora_CodigoRastreio` (`ErrosBancoDados.EhViolacaoUnicidade`) → `RastreioDuplicadoException`;
  - violação do índice único de `OrdensEntrega.PedidoId` (ordem criada ao mesmo tempo por outra operação, como o consumidor de entrega) → `ConflitoConcorrenciaException` (research R-07, analyze C1); conferir o nome do índice na migration `EntidadesNegocio`.
- [ ] T015 [US2] Serviço (R-07, R-10):
  - `IServicoPedidos.RegistrarRastreioAsync(Guid pedidoId, Guid usuarioId, RegistrarRastreioRequestDTO rastreio, CancellationToken)`, com XML doc da tabela de contracts/rastreio-http.md;
  - em `ServicoPedidos`: aparar os dois textos; laço de até `TentativasStatus`: ler para alterar (inexistente → `PedidoNaoEncontrado`); `!PodeRegistrarRastreio` → `Conflito` `PEDIDO_TRANSICAO_INVALIDA` com o status atual; `RastreioEmUsoAsync` → `Conflito` `PEDIDO_RASTREIO_DUPLICADO` e log 4005; `RegistrarRastreio`; `SalvarAsync`; em `ConflitoConcorrenciaException`, descartar e reler; em `RastreioDuplicadoException`, descartar e responder o duplicado; sucesso → log 4004 e `ParaResposta()`; esgotado → `AlteradoPorOutraOperacao`;
  - em `src/PedidoFulfillment.Application/Pedidos/EventosPedidos.cs`, `LoggerMessage` 4004 `RastreioRegistrado` (Information: pedido, usuário, resultado) e 4005 `RastreioDuplicado` (Information: pedido, usuário), sem transportadora nem código (FR-016).

  T010 deve passar.
- [ ] T016 [US2] API (R-08): em `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs`, `PoliticaRastreioPedido` (`RequireRole` de `Admin` e `OperadorLogistica`); em `src/PedidoFulfillment.API/Endpoints/EndpointsPedidos.cs`, `MapPatch("/{id}/rastreio", ...)` como o de status: `WithName("RegistrarRastreioPedido")`, `WithSummary(...)`, a política nova, `FiltroValidacao<RegistrarRastreioRequestDTO>`, `Produces<PedidoResponseDTO>()` e `ProduzErros(400, 404, 409)`.
- [ ] T017 [US2] Contrato da operação: em `specs/001-contratos-api/contracts/openapi-v1.yaml`, o caminho `/api/v1/pedidos/{id}/rastreio` com o `patch` de contracts/rastreio-http.md (descrição com a tabela de resultados, perfis, exemplo de corpo, respostas 200/400/401/403/404/409 e exemplo de 409 com `PEDIDO_RASTREIO_DUPLICADO`), o schema `RegistrarRastreioRequestDTO` e o código novo na lista de códigos, se houver; em `ConformidadeOpenApiTestes.Operacoes`, a linha `{ "PATCH", "/api/v1/pedidos/{id}/rastreio" }`. T011 e a conformidade devem passar; rodar a suíte inteira no checkpoint.

**Checkpoint**: a logística despacha com rastreio.

---

## Phase 5: User Story 3 - Correção do rastreio de um pedido despachado (Priority: P3)

**Goal**: em pedido `Despachado`, a mesma rota corrige o rastreio sem mudar status, data de despacho nem histórico; dados iguais não mudam nada (FR-009).

**Independent Test**: despachar, corrigir o código e conferir o código novo com a data de despacho e o histórico preservados.

### Testes da US3

- [ ] T018 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoRastreioTestes.cs`:
  - `Despachado` com exatamente os mesmos dados: `SemMudanca`, nada muda (`AtualizadoEm` da ordem e do pedido iguais);
  - `Despachado` com outro código, outra transportadora ou só outra caixa (`br123` → `BR123`): `Corrigido` (analyze I1); ordem com os dados novos e `AtualizadoEm = instante`; `DespachadoEm` original; status e histórico iguais; `Pedido.AtualizadoEm = instante` (R-07);
  - `Despachado` pela rota de status, sem ordem: `Corrigido`, ordem criada com `DespachadoEm = instante`;
  - `Despachado` com ordem sem `DespachadoEm`: `Corrigido`, `DespachadoEm = instante`;
  - em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEntregaTestes.cs` (onde a `OrdemEntrega` é testada), `CorrigirRastreio` mantém `DespachadoEm` e valida os tamanhos.
- [ ] T019 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosRastreioTestes.cs`:
  - correção: sucesso, uma gravação, log 4004 `Corrigido`;
  - mesmos dados: sucesso **sem** gravação, log 4004 `SemMudanca`;
  - correção para um rastreio em uso por outro pedido: `PEDIDO_RASTREIO_DUPLICADO`;
  - conflito e, na releitura, o pedido foi despachado por outra operação com outros dados: a segunda chamada vira correção (`Corrigido`), com uma única entrada de despacho no histórico.
- [ ] T020 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/RastreioPedidoComBancoTestes.cs`:
  - despachar, repetir o mesmo corpo (200, `atualizadoEm` igual) e corrigir o código (200, código novo, `despachadoEm` e histórico iguais);
  - correção para um rastreio já usado em outro pedido (com outra caixa): 409 `PEDIDO_RASTREIO_DUPLICADO`;
  - pedido despachado pela rota de status, sem ordem: o rastreio cria a ordem com `despachadoEm`, status e histórico iguais;
  - dois despachos simultâneos do mesmo pedido com códigos diferentes (10 rodadas): os dois 200, uma única entrada `ProntoParaEnvio → Despachado` no histórico e o código gravado é um dos dois.

### Implementação da US3

- [ ] T021 [US3] Em `src/PedidoFulfillment.Domain/Entregas/OrdemEntrega.cs`, `CorrigirRastreio(string transportadora, string codigoRastreio, DateTimeOffset instante)` (guardas como `RegistrarDespacho`, `DespachadoEm ??= instante`, `AtualizadoEm = instante`); em `Pedido.RegistrarRastreio`, o caso `Despachado` de data-model.md (comparação ordinal, `string.Equals(..., StringComparison.Ordinal)`, depois de aparar, analyze I1; `SemMudanca` sem tocar em nada; `Corrigido` com `OrdemEntrega ??= new OrdemEntrega(Id, instante)`, `CorrigirRastreio` e `AtualizadoEm = instante`); no serviço, `SemMudanca` não chama `SalvarAsync`. T018 a T020 devem passar.

**Checkpoint**: despacho e correção completos.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [ ] T022 [P] Desempenho em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/DesempenhoRastreioTestes.cs` (`ColecaoBancoDados`; SC-004): pedido de 10 itens (como `DesempenhoCriacaoTestes`) levado a `ProntoParaEnvio` e com o histórico completado por SQL até 10 entradas; 5 consultas de aquecimento; 40 consultas e 40 rastreios (o primeiro despacha, os demais alternam dois códigos), cada um medido com `Stopwatch`; percentil 95 abaixo de 500 ms para cada operação, com os valores na saída do teste.
- [ ] T023 [P] Atualizar `README.md`:
  - **Estado atual**: linha do REQ-11; tirar "Issue #11 pendente";
  - **Próximos passos**: sem issues de negócio pendentes; citar a #18 (SonarQube) e as pendências futuras de mensageria;
  - **Decisões**: o fluxo do pedido vai até `Despachado` com rastreio; despacho pela rota de status continua sem rastreio; correção do rastreio; ordem criada no despacho;
  - **Roteiros de validação**: o quickstart do REQ-11.
- [ ] T024 [P] Em `specs/005-criacao-pedidos/quickstart.md`, nota de que a resposta do pedido passou a trazer `historico` e `entrega` (REQ-11).
- [ ] T025 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados).
- [ ] T026 Executar o quickstart (§1 a §5) com a API e o Worker locais e registrar em Notes, com os percentis da T022. Conferir nos logs da API os EventIds 4004 e 4005.
- [ ] T027 Fazer push e abrir o PR com `Closes #11`. Acompanhar os workflows `build-testes` e "Compatibilidade do contrato" até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup → Foundational**: a fundação precisa da linha de base.
- **US1**: depende da fundação (T002 e T003).
- **US2**: depende da fundação e do mapeamento da US1 (T006), que as respostas do rastreio usam; os testes da US2 podem ser escritos em paralelo com a US1.
- **US3**: completa `Pedido.RegistrarRastreio`, o serviço e os mesmos arquivos de teste da US2, por isso vem depois dela.
- **Polish**: depois de tudo. A T027 exige push.

### Parallel Opportunities

- T002 e T003; T004 e T005; T009, T010 e T011; T012 e T013 (arquivos diferentes); T018, T019 e T020; T022, T023 e T024.

---

## Implementation Strategy

### MVP

Setup, Foundational e US1: a consulta mostra o histórico e a entrega.

### Incremental Delivery

1. Setup + Foundational + US1 → commit `feat(rastreio): mostra historico e entrega na consulta do pedido [REQ-11]`.
2. US2 → commit `feat(rastreio): despacha o pedido com transportadora e rastreio [REQ-11]`.
3. US3 → commit `feat(rastreio): corrige o rastreio de pedido despachado [REQ-11]`.
4. Polish → `docs(rastreio): ... [REQ-11]` e `test(rastreio): ... [REQ-11]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Evento "pedido despachado", validação do rastreio com a transportadora e exposição do autor
  no histórico ficam fora do escopo (spec, Assumptions).
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em outra rodada.
- **T001 (linha de base, 2026-10-23)**: 650 testes, 383 unitários e 267 de integração, zero
  ignorados, com o Docker no ar.
- **T002 e T007, desvio**: `historico` ficou **opcional** no schema (sem `[Required]`), e não
  no `required` do `PedidoResponseDTO`. O próprio contrato diz que a v1 só aceita adições
  opcionais. A API sempre devolve a lista (vazia, no mínimo), e isso também cobre a resposta
  idempotente gravada antes do REQ-11 (R-03). Research, data-model e contrato foram ajustados.
- **T007**: o gerador de OpenAPI escreve objeto anulável como `oneOf: [{type: 'null'}, {$ref}]`
  e texto anulável como `type: ['null', string]`; o YAML segue essa forma.
- **T008**: só `SerializacaoPedidoTestes` fixava a lista exata de campos do pedido; ela ganhou
  `historico` e `entrega`, com o tipo de cada um.
