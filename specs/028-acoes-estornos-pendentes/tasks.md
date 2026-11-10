---

description: "Lista de tarefas do REQ-28 — Ações da operação sobre estornos pendentes e evento de estorno travado"
---

# Tasks: Ações da operação sobre estornos pendentes e evento de estorno travado

**Input**: Design documents from `/specs/028-acoes-estornos-pendentes/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/estornos.md](./contracts/estornos.md),
[contracts/eventos](./contracts/eventos/), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A spec pede testes de sucesso, 401, 403 e da concorrência com a rodada, e a constituição
exige 401/403 em todo endpoint protegido e testes de schema dos eventos.

**Organization**: a fundação traz o domínio, a migração, os contratos, as políticas e os logs, que todas as histórias
usam. Cada história entrega uma rota (ou o evento) com seus testes: US1 visibilidade, US2 baixa, US3 evento de atraso,
US4 tentativa forçada. **O MVP são a fundação e as duas histórias P1 (US1 e US2).**

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; endpoints só roteiam (regras no domínio e no `ServicoAcoesEstornos`/`ServicoEstornos`).
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo.** Uma migração só (`AcoesEstornos`). Contratos v1 só por acréscimo; `pagamento.estornado` e
  `pedido.estorno-confirmado` inalterados.
- **Nunca** em log ou evento: identificador da transação ou do estorno no gateway, mensagem de exceção do gateway,
  motivo da baixa ou dado pessoal. A referência externa vai só no `pagamento.estorno-baixado` (não em log).
- Logs novos só com os IDs 7021 a 7024 ([R-12](./research.md#r-12-logs-fr-022)).
- O `estornoId` é o `Pagamento.Id` ([R-01](./research.md#r-01-identificador-do-estorno-fr-001)); a `situacao` do
  contrato (`Pendente`, `Confirmado`, `Baixado`) é um enum próprio, e a `SituacaoPagamento` não aparece no contrato.
- Os testes existentes passam sem afrouxar asserções.
- Artefatos do Docker criados pelos testes não ficam na máquina: retrato antes (`docker ps -a`, `docker volume ls`,
  `docker network ls`) e conferência no fim (T050).

---

## Phase 1: Setup

- [X] T001 Tirar o retrato do Docker (`docker ps -a`, `docker volume ls`, `docker network ls`, `docker images`) e guardar
  a saída no scratchpad da sessão, para a conferência do T050.

---

## Phase 2: Foundational (domínio, migração, contratos, políticas e logs)

**Purpose**: tudo o que as quatro histórias usam. A suíte atual volta a ficar verde no T016.

- [X] T002 [P] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEstornoTestes.cs` para os
  métodos novos do `Pagamento` ([data-model](./data-model.md#métodos-do-domínio)): `PedirTentativaEstorno` grava a marca
  só se nula e devolve se mudou; `RegistrarFalhaEstorno(instante, erro)` soma a tentativa, grava `UltimoErroEstorno` e
  limpa a marca; `MarcarEstornoAtrasado` grava só uma vez e devolve se mudou; `ConfirmarEstorno` limpa a marca;
  `DarBaixaEstorno` leva a `EstornoBaixado` com autor, instante, motivo (sem os espaços das pontas) e referência
  (opcional) e limpa a marca; cada um lança `InvalidOperationException` fora de `EstornoPendente` (inclusive depois de
  baixado: nenhuma falha, confirmação ou marca nova, FR-008); erro acima de 200, motivo fora de 10 a 500 e referência
  acima de 100 caracteres lançam `ArgumentException`.
- [X] T003 Em `src/PedidoFulfillment.Domain/Pagamentos/SituacaoPagamento.cs`, acrescentar `EstornoBaixado` (final), com
  comentário citando o REQ-28 e a diferença para `Estornado` (devolução por fora, sem gateway).
- [X] T004 Em `src/PedidoFulfillment.Domain/Pagamentos/Pagamento.cs`, acrescentar as constantes
  `TamanhoMaximoUltimoErroEstorno = 200`, `TamanhoMinimoMotivoBaixa = 10`, `TamanhoMaximoMotivoBaixa = 500`,
  `TamanhoMaximoReferenciaExternaBaixa = 100`, as propriedades `UltimoErroEstorno`, `TentativaEstornoForcadaEm`,
  `EstornoAtrasadoEm`, `EstornoBaixadoEm`, `EstornoBaixadoPorId`, `MotivoBaixaEstorno` e
  `ReferenciaExternaBaixaEstorno` (todas anuláveis, `private set`) e os métodos do data-model, usando `Guardas` e
  `ExigirSituacao(EstornoPendente, ...)`. `RegistrarFalhaEstorno` passa a receber `string erro`. Até T002 passar.
- [X] T005 Em `src/PedidoFulfillment.Domain/Pedidos/Pedido.cs`, acrescentar os repasses `PedirTentativaEstorno`,
  `MarcarEstornoAtrasado` e `DarBaixaEstorno` pelo `PagamentoComEstornoPendente()`, e ajustar `RegistrarFalhaEstorno`
  para repassar o erro; nos chamadores, passar provisoriamente o mesmo texto que vai hoje para o log
  (`"{tipo}: {mensagem}"` ou `"Recusado: {motivo}"`, cortado em 200 caracteres), substituído pela
  `DescricaoFalhaEstorno` no T019 (nenhum teste lê a coluna antes do T017).
- [X] T006 Em `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoPagamento.cs`: `HasMaxLength`
  de 200 (`UltimoErroEstorno`), 500 (`MotivoBaixaEstorno`) e 100 (`ReferenciaExternaBaixaEstorno`);
  `CK_Pagamentos_BaixaCompleta` ("`Situacao = 'EstornoBaixado'` se e só se `EstornoBaixadoEm`, `EstornoBaixadoPorId` e
  `MotivoBaixaEstorno` estão preenchidos"); os índices filtrados `IX_Pagamentos_EstornoForcado`
  (`TentativaEstornoForcadaEm`, filtro `[Situacao] = N'EstornoPendente' AND [TentativaEstornoForcadaEm] IS NOT NULL`) e
  `IX_Pagamentos_EstornoPendenteListagem` (`EstornoSolicitadoEm`, `Id`, filtro `[Situacao] = N'EstornoPendente'`). A
  `CK_Pagamentos_SituacaoValida` já sai do enum (`RestricoesEnum`).
- [X] T007 Gerar a migração com `dotnet ef migrations add AcoesEstornos --project src/PedidoFulfillment.Infrastructure
  --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes` e conferir no arquivo gerado em
  `src/PedidoFulfillment.Infrastructure/Persistencia/Migracoes/` que só há colunas anuláveis novas, a
  `CK_Pagamentos_SituacaoValida` recriada com `EstornoBaixado`, a `CK_Pagamentos_BaixaCompleta` e os dois índices, sem
  nenhuma alteração de dados.
- [X] T008 [P] Criar `src/PedidoFulfillment.Application/Pagamentos/RegrasAlertaEstorno.cs` com
  `AlemDoLimite(DateTimeOffset solicitadoEm, int tentativas, DateTimeOffset agora, ParametrosAlertaEstorno parametros)`
  ("`agora − EstornoSolicitadoEm > LimiteAlerta` e `TentativasEstorno ≥ TentativasAlerta`", R-09), mover para lá o
  `ParametrosAlertaEstorno` e trocar a condição em linha do `ServicoEstornos.RegistrarFalhaAsync` por ela, sem mudar o
  comportamento do 7007/7008; testes em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/RegrasAlertaEstornoTestes.cs`
  (limite exato não conta; tentativas iguais contam).
- [X] T009 [P] Criar `src/PedidoFulfillment.Application/Pagamentos/DescricaoFalhaEstorno.cs` com as categorias do
  [R-05](./research.md#r-05-último-erro-sem-dado-sensível-fr-005): `Recusado(motivo)` → `Recusado: <motivo>` com o
  motivo cortado em 180 caracteres; `Falha(FalhaGatewayPagamentoException)` → `Falha passageira do gateway.` ou `Falha permanente do gateway.` (pelo `TipoFalhaGateway`);
  `Inesperada(Exception)` → `Erro inesperado: <nome do
  tipo>.`; nunca a mensagem da exceção. Testes em
  `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/DescricaoFalhaEstornoTestes.cs`, inclusive uma exceção cuja
  mensagem tem URL e identificador que não podem aparecer no texto.
- [X] T010 [P] Em `src/PedidoFulfillment.Application/Contratos/Eventos/Eventos.cs` e `TiposEvento.cs`, criar
  `PagamentoEstornoAtrasado` (`EstornoId`, `PedidoId`, `Numero`, `Valor`, `SolicitadoEm`, `Tentativas`, `AtrasadoEm`) e
  `PagamentoEstornoBaixado` (`EstornoId`, `PedidoId`, `Numero`, `Valor`, `AutorId`, `BaixadoEm`, `ReferenciaExterna`
  anulável e sempre serializado), os tipos `pagamento.estorno-atrasado` e `pagamento.estorno-baixado` e o registro no
  dicionário de tipos, conforme os [schemas](./contracts/eventos/).
- [X] T011 [P] Em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`, copiar
  `specs\028-acoes-estornos-pendentes\contracts\eventos\*.schema.json` para `Eventos` (como as linhas 41–45); em
  `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`, acrescentar os dois tipos à lista e ao
  comentário da classe, validando um exemplo serializado de cada (com `referenciaExterna` nula e preenchida).
- [X] T012 [P] Criar os DTOs em `src/PedidoFulfillment.Application/Contratos/Estornos/`: `SituacaoEstorno` (`Pendente`,
  `Confirmado`, `Baixado`), `EstornoResponseDTO`, `BaixaEstornoResponseDTO`, `PaginaEstornosResponseDTO`,
  `ListarEstornosRequestDTO` (`Tamanho`, `Cursor`) e `BaixaEstornoRequestDTO` (`Motivo` obrigatório, "10 a 500
  caracteres sem os espaços das pontas"; `ReferenciaExterna` opcional, "1 a 100 caracteres sem os espaços das pontas"),
  com os campos e a anulabilidade do [contrato](./contracts/estornos.md#schemas-resumo), no molde de
  `Contratos/Usuarios/`. Testes da validação do request em
  `tests/PedidoFulfillment.Testes.Unitarios/Contratos/BaixaEstornoRequestDTOTestes.cs` (molde de
  `RegistrarRastreioRequestDTOTestes.cs`).
- [X] T013 [P] Em `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs`, acrescentar
  `ESTORNO_NAO_PENDENTE` (409) e `ESTORNO_RODADA_EM_ANDAMENTO` (409) com os `title` e
  `detail` exatos do [contrato](./contracts/estornos.md#códigos-de-erro-novos), e as mesmas linhas no catálogo de
  `specs/001-contratos-api/data-model.md` (seção "Catálogo de códigos de erro").
- [X] T014 [P] Em `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs`, acrescentar `PoliticaEstornos` (Admin
  ou OperadorLogistica) e `PoliticaBaixaEstorno` (Admin com `RequisitoAdminAtivo`), com os comentários citando o REQ-28
  ([R-10](./research.md#r-10-autorização-fr-019-fr-020)).
- [X] T015 [P] Em `src/PedidoFulfillment.Application/Pagamentos/EventosPagamento.cs`, acrescentar os logs 7021
  `TentativaEstornoForcada` (Information: estorno, pedido, autor), 7022 `EstornoBaixado` (Warning: estorno, pedido,
  valor, autor), 7023 `BaixaEstornoRecusadaRodadaEmAndamento` (Warning: estorno, autor) e 7024
  `EstornoAtrasadoPublicado` (Warning: estorno, pedido, tentativas, horas pendente). Nenhum com motivo ou referência.
- [X] T016 Na API, registrar as opções de alerta: criar em
  `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs` a extensão `AdicionarAcoesEstornos` (chamada
  só pela API em `src/PedidoFulfillment.API/Program.cs`) e um método privado comum `AdicionarOpcoesEstornos` usado por
  ela e pelo `AdicionarProcessamentoEstornos`: `OpcoesEstornos` com `ValidateOnStart`, o validador registrado com
  `TryAddEnumerable` (nunca em dobro) e `ParametrosAlertaEstorno` como singleton (R-09). Os serviços da API das
  histórias (`IConsultaEstornos`, `IBloqueioRodadaEstornos`) entram nessa mesma extensão. Rodar `dotnet build` e `dotnet test`; acrescentar
  em `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs` um teste que confirma que
  alterar só o `Pagamento` de um pedido lido para alterar gera `ConflitoConcorrenciaException` numa gravação concorrente
  (conferência do research).

**Checkpoint**: domínio, migração, contratos, políticas e logs prontos; a suíte inteira verde.

---

## Phase 3: User Story 1 - Ver os estornos pendentes (Priority: P1) 🎯 MVP

**Goal**: a operação lista os pendentes e consulta um estorno, com tentativas, último erro e alerta.

**Independent Test**: estornos pendentes em situações diferentes (novo, com falhas, além do limite) e um confirmado;
a listagem traz só os pendentes, do mais antigo para o mais novo, com os dados certos.

- [X] T017 [P] [US1] Testes em `tests/PedidoFulfillment.Testes.Integracao/Estornos/ListagemEstornosTestes.cs`
  (Testcontainers, semeando pedidos pagos e cancelados pelo `CenarioEstorno` e ajustando campos do `Pagamento` direto
  pelo contexto): três pendentes e um confirmado → só os três, do `EstornoSolicitadoEm` mais antigo para o mais novo
  (US1 cenário 1); estorno com duas falhas e a recusa "Cobrança desconhecida." → `tentativas` 2, `ultimaFalhaEm` e
  `ultimoErro` "Recusado: Cobrança desconhecida.", e o JSON sem `identificadorTransacao`/`identificadorEstorno` (cenário
  2); pendente há mais de 24 h com 3 falhas → `alemDoLimiteAlerta: true`, e com 2 falhas → `false` (cenário 3);
  `tamanho=2` com 3 pendentes → `proximo` preenchido, a segunda página traz o terceiro e `anterior` volta à primeira
  (cenário 4); consulta por id de um pendente, de um `Estornado` (`situacao: Confirmado`, `confirmadoEm`) e de um
  `EstornoBaixado` semeado (`situacao: Baixado`, `baixa` preenchida) (cenário 5); lista vazia → 200 com `itens` vazio;
  id desconhecido → 404 "Estorno não encontrado." (sem `codigo`, como os demais 404); id inválido → 400 `erros.id`; `tamanho=0` e `cursor` adulterado → 400;
  sem token → 401 e Cliente → 403 nas duas rotas; OperadorLogistica → 200 (cenário 6, FR-019).
- [X] T018 [P] [US1] Testes em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ServicoEstornosTestes.cs`: a falha
  grava o texto da `DescricaoFalhaEstorno` para recusa, `FalhaGatewayPagamentoException`, exceção inesperada e conflito
  na confirmação; o log 7007/7008 continua como hoje.
- [X] T019 [US1] Em `src/PedidoFulfillment.Application/Pagamentos/ServicoEstornos.cs`, montar o erro com a
  `DescricaoFalhaEstorno` e passá-lo a `pedido.RegistrarFalhaEstorno(agora, erro)`, mantendo o texto atual no log (o log
  já não leva identificadores; só o texto gravado muda). Até T018 passar.
- [X] T020 [P] [US1] Criar `src/PedidoFulfillment.Application/Pagamentos/PosicaoCursorEstornos.cs` e
  `CodificadorCursorEstornos.cs`, no molde do `CodificadorCursorUsuarios`, com o rótulo `estornos`, a posição
  (`EstornoSolicitadoEm`, `Id`, sentido, inclusiva) e ligado a quem chama.
- [X] T021 [US1] Criar `src/PedidoFulfillment.Application/Pagamentos/IConsultaEstornos.cs` (listar página de pendentes
  por posição e tamanho; obter por id) e `src/PedidoFulfillment.Infrastructure/Pagamentos/ConsultaEstornos.cs`
  (`AsNoTracking`, junção com o pedido para o `Numero`, ordem `EstornoSolicitadoEm, Id` pelo índice
  `IX_Pagamentos_EstornoPendenteListagem`), com o mapeamento `Pendente`/`Confirmado`/`Baixado` (outras situações do
  pagamento → não encontrado) e `alemDoLimiteAlerta` pela `RegrasAlertaEstorno` (sempre `false` fora de `Pendente`);
  registrar no `AdicionarAcoesEstornos` (T016), que só a API chama.
- [X] T022 [US1] Criar `src/PedidoFulfillment.Application/Pagamentos/ServicoAcoesEstornos.cs` com `ListarAsync` e
  `ObterAsync`, devolvendo `Resultado<...>` com o 404 "Estorno não encontrado." e o cursor inválido como erro de validação
  `erros.cursor`, no molde do `ServicoGestaoUsuarios`.
- [X] T023 [US1] Criar `src/PedidoFulfillment.API/Endpoints/ParametrosListarEstornos.cs` (`tamanho` 1 a 100, padrão 20;
  `cursor`) e `src/PedidoFulfillment.API/Endpoints/EndpointsEstornos.cs` com o grupo `/api/v1/estornos`
  (`WithTags("Estornos")`) e as rotas `GET /pendentes` e `GET /{id}` (sem `{id:guid}`, 400 para id inválido) com
  `PoliticaEstornos`; registrar `ServicoAcoesEstornos`, o codificador e `MapearEndpointsEstornos` em
  `src/PedidoFulfillment.API/Program.cs`. Se o OpenAPI precisar dos parâmetros, criar
  `src/PedidoFulfillment.API/OpenApi/ParametrosListagemEstornosOpenApi.cs` no molde do de usuários.
- [X] T024 [US1] Em `specs/001-contratos-api/contracts/openapi-v1.yaml`, acrescentar as duas rotas e os schemas
  `EstornoResponseDTO`, `BaixaEstornoResponseDTO`, `PaginaEstornosResponseDTO` e `SituacaoEstorno`, com os erros de cada
  rota ([contrato](./contracts/estornos.md#rotas)); só acréscimos. Rodar os testes de `Contrato`. Até T017 passar.

**Checkpoint**: a operação enxerga os estornos pendentes sem ler logs (SC-001).

---

## Phase 4: User Story 2 - Dar baixa num estorno resolvido fora do sistema (Priority: P1) 🎯 MVP

**Goal**: o Admin ativo dá baixa com motivo e referência; o estorno sai da fila para sempre, com evento e aviso.

**Independent Test**: baixa de um pendente; a rodada seguinte não chama o gateway; o estorno aparece `Baixado`, com
autor, instante, motivo e referência; saem o `pagamento.estorno-baixado` e o aviso, sem `pagamento.estornado`.

- [X] T025 [P] [US2] Testes em `tests/PedidoFulfillment.Testes.Integracao/Estornos/BaixaEstornoTestes.cs`: baixa com
  motivo e referência → 200 `situacao: Baixado` com os quatro campos, e o estorno some da listagem (cenário 1); na mesma
  gravação, um `pagamento.estorno-baixado` no outbox (validado contra o schema, com `referenciaExterna`), um aviso
  `pedido.estorno-confirmado` com o id desse evento, e nenhum `pagamento.estornado` (cenário 5, FR-010, FR-010a); baixa
  sem referência → `referenciaExterna` nula no evento; sem motivo, motivo com 9 e com 501 caracteres (sem contar os
  espaços das pontas) e referência com 101 → 400 com o campo e o estorno ainda pendente (cenário 3); baixa de um
  `Estornado` e de um já baixado → 409 `ESTORNO_NAO_PENDENTE` sem mudança (cenário 4); OperadorLogistica e Cliente → 403,
  sem token → 401 (cenário 6); Admin desativado depois de emitido o token → 403 (cenário 7); duas baixas em paralelo do
  mesmo estorno → uma 200 e uma 409; log 7022 sem o motivo nem a referência (`CapturaLogs`).
- [X] T026 [P] [US2] Testes em `tests/PedidoFulfillment.Testes.Integracao/Estornos/BaixaContraRodadaTestes.cs`, com
  uma conexão de teste que segura `sp_getapplock 'oms-estornos'` (`LockOwner = 'Session'`) como a rodada: soltando o lock
  em 1 s, a baixa espera e responde 200 (cenário 8, SC-008); segurando por mais de 5 s, 409
  `ESTORNO_RODADA_EM_ANDAMENTO` com o `detail` exato, nada gravado e log 7023; com o `GatewayEstornoControlado` e o
  `WorkerEstornando`, a rodada confirma o estorno enquanto a baixa espera → a baixa relê e responde 409
  `ESTORNO_NAO_PENDENTE`; depois de uma baixa, três rodadas seguidas sem nenhuma chamada ao gateway para o pedido
  (`EstornosDo` = 0, SC-002, US2 cenário 2); e, com o `BloqueioRodadaEstornos` executando uma ação lenta (2 s), uma
  rodada chamada no meio pula (7011) sem chamar o gateway.
- [X] T027 [US2] Criar `src/PedidoFulfillment.Application/Pagamentos/IBloqueioRodadaEstornos.cs`
  (`Task<bool> ExecutarAsync(TimeSpan espera, Func<CancellationToken, Task> acao, CancellationToken)`: `false` se não
  conseguiu o lock) e `src/PedidoFulfillment.Infrastructure/Pagamentos/BloqueioRodadaEstornos.cs`: transação do
  `ContextoBancoDados` (`BeginTransactionAsync`), `sp_getapplock` com `@Resource = ProcessadorEstornos.RecursoLock`,
  `@LockMode = 'Exclusive'`, `@LockOwner = 'Transaction'` e `@LockTimeout` em milissegundos; com o lock, executa a ação e
  confirma; sem ele, desfaz ([R-03](./research.md#r-03-a-baixa-espera-a-rodada-fr-009)). Registrar no
  `AdicionarAcoesEstornos` (T016).
- [X] T028 [US2] Em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`: na guarda
  `ExigirEventosDeCancelamentoEEstorno`, exigir o `pagamento.estorno-baixado` na transição
  `EstornoPendente → EstornoBaixado`; no `AdicionarAvisosAsync`, tratar essa transição como a da confirmação, criando o
  aviso com `FabricaAvisos.EstornoConfirmado` e o id do `pagamento.estorno-baixado` da mesma gravação; atualizar os
  comentários ([R-07](./research.md#r-07-evento-da-baixa-e-aviso-ao-cliente-fr-010-fr-010a)).
- [X] T029 [US2] Em `src/PedidoFulfillment.Application/Pagamentos/ServicoAcoesEstornos.cs`, criar `DarBaixaAsync(autorId,
  estornoId, BaixaEstornoRequestDTO)`: pelo `IBloqueioRodadaEstornos` com espera de 5 s, relê o pedido do pagamento para
  alterar; não encontrado → 404; não pendente → 409 `ESTORNO_NAO_PENDENTE`; senão `DarBaixaEstorno`, acrescenta o
  `pagamento.estorno-baixado` e grava; sem o lock → 7023 e 409 `ESTORNO_RODADA_EM_ANDAMENTO`; conflito de concorrência →
  409 `ESTORNO_NAO_PENDENTE` depois de reler; sucesso → 7022 e o `EstornoResponseDTO` relido.
- [X] T030 [US2] Em `src/PedidoFulfillment.API/Endpoints/EndpointsEstornos.cs`, acrescentar `POST /{id}/baixa` com
  `PoliticaBaixaEstorno`, `FiltroValidacao<BaixaEstornoRequestDTO>`, 200 e os erros 400, 404 e 409.
- [X] T031 [US2] Em `specs/001-contratos-api/contracts/openapi-v1.yaml`, acrescentar a rota da baixa e o
  `BaixaEstornoRequestDTO`, com os dois 409. Até T025 e T026 passarem.

**Checkpoint**: MVP completo: visibilidade e baixa, sem risco de devolução dupla pela rodada (SC-002, SC-003).

---

## Phase 5: User Story 3 - Saber que um estorno travou (Priority: P2)

**Goal**: o `pagamento.estorno-atrasado` sai uma única vez por estorno, na falha que passa do limite.

**Independent Test**: um estorno falha até passar do limite; sai um evento; falhas seguintes, confirmação ou baixa não
geram outro.

- [ ] T032 [P] [US3] Testes em `tests/PedidoFulfillment.Testes.Unitarios/Pagamentos/ServicoEstornosTestes.cs`: a falha
  que passa do limite grava `EstornoAtrasadoEm` e acrescenta um `pagamento.estorno-atrasado` com os campos do schema, e
  emite o 7024; a falha seguinte só emite o 7008; a falha abaixo do limite não gera evento; o adiamento pelo circuito
  não grava nada nem gera evento (cenário 4); conflito na gravação → nada publicado.
- [ ] T033 [P] [US3] Testes em `tests/PedidoFulfillment.Testes.Integracao/Estornos/EstornoAtrasadoTestes.cs` (com
  `WorkerEstornando`, `LimiteAlertaSegundos` baixo e `GatewayEstornoControlado.FalharSemprePara`): 10 falhas seguidas
  com dois Workers → exatamente um `pagamento.estorno-atrasado` no outbox, válido contra o schema (SC-004); estorno
  semeado já além do limite com `EstornoAtrasadoEm` nulo → um evento na próxima falha (cenário 5); depois do evento, a
  confirmação gera só o `pagamento.estornado` e a baixa só o `pagamento.estorno-baixado` (cenário 3); nenhum aviso ao
  Cliente pelo evento de atraso (FR-018); mudar o limite depois do evento não gera outro.
- [ ] T034 [US3] Em `src/PedidoFulfillment.Application/Pagamentos/ServicoEstornos.cs`, no `RegistrarFalhaAsync`: depois
  de `RegistrarFalhaEstorno`, se `RegrasAlertaEstorno.AlemDoLimite` e `pedido.MarcarEstornoAtrasado(agora)` devolver
  `true`, acrescentar o `pagamento.estorno-atrasado` antes do `SalvarAsync`; depois de gravar, emitir o 7024 (e o 7008,
  como hoje); num conflito, não emitir o 7024 ([R-06](./research.md#r-06-evento-de-estorno-travado-uma-única-vez-fr-015-a-fr-018)).
- [ ] T035 [US3] Em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, exigir na guarda o
  `pagamento.estorno-atrasado` quando `EstornoAtrasadoEm` passa de nulo a preenchido; sem aviso ao Cliente. Até T032 e
  T033 passarem.

**Checkpoint**: os sistemas de fora sabem do estorno travado sem depender do log 7008.

---

## Phase 6: User Story 4 - Forçar uma nova tentativa (Priority: P3)

**Goal**: a operação pede a tentativa, e a rodada seguinte trata o estorno antes dos demais.

**Independent Test**: com mais pendentes que o lote, forçar o último da fila; ele é tentado na rodada seguinte.

- [ ] T036 [P] [US4] Testes em `tests/PedidoFulfillment.Testes.Integracao/Estornos/TentativaForcadaTestes.cs`: `POST
  /tentativas` de um pendente → 202, `Location: /api/v1/estornos/{id}` e `tentativaForcadaEm` preenchido (cenário 1);
  um segundo pedido → 202 com o mesmo instante (cenário 5); com `TamanhoLote` 2, 4 pendentes e o último da fila forçado →
  ele é tentado na primeira rodada (cenário 2, SC-005); a tentativa forçada que falha conta tentativa, grava o erro e
  limpa a marca (cenário 3); com o circuito aberto (`GatewayPagamentoIndisponivelException`), a marca continua e nada é
  contado, e o estorno é o primeiro quando o circuito fecha (cenário 4); confirmado ou baixado → 409
  `ESTORNO_NAO_PENDENTE` (cenário 6); id desconhecido → 404; sem token → 401, Cliente → 403, OperadorLogistica → 202
  (cenário 7); log 7021 com o autor.
- [ ] T037 [US4] Em `src/PedidoFulfillment.Application/Pagamentos/ServicoAcoesEstornos.cs`, criar
  `PedirTentativaAsync(autorId, estornoId)`: relê para alterar; 404 e 409 como na baixa; `PedirTentativaEstorno` e
  grava só se mudou; conflito de concorrência → relê e tenta de novo até 3 vezes (a rodada pode ter gravado uma falha);
  log 7021; devolve o `EstornoResponseDTO`.
- [ ] T038 [US4] Em `src/PedidoFulfillment.API/Endpoints/EndpointsEstornos.cs`, acrescentar `POST /{id}/tentativas` com
  `PoliticaEstornos`, sem corpo, `TypedResults.Accepted("/api/v1/estornos/{id}", ...)` e `.ComCabecalhoLocation()`.
- [ ] T039 [US4] Em `src/PedidoFulfillment.Infrastructure/Pagamentos/ConsultaEstornosPendentes.cs`, ler a fila em duas
  etapas ([R-04](./research.md#r-04-tentativa-forçada-fr-011-a-fr-014)): primeiro os pendentes com
  `TentativaEstornoForcadaEm` não nulo, por `TentativaEstornoForcadaEm`, até o tamanho do lote; depois completar com a
  ordem de hoje, sem repetir; atualizar o comentário da classe.
- [ ] T040 [US4] Em `specs/001-contratos-api/contracts/openapi-v1.yaml`, acrescentar a rota da tentativa forçada (202 com
  `Location`). Até T036 passar.

**Checkpoint**: as quatro histórias funcionam de forma independente.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T041 Medidor `oms.estornos.pendentes` ([R-11](./research.md#r-11-métrica-fr-022a)): em
  `src/PedidoFulfillment.Infrastructure/Observabilidade/TelemetriaOms.cs`, um `ObservableGauge<long>` com o atributo
  `oms.alerta` (`true`/`false`), no molde do `EventosPendentes`; em `ProcessadorEstornos.cs`, contar no começo da rodada
  que pega o lock os pendentes e os além do limite (`RegrasAlertaEstorno`, numa consulta só, em
  `IConsultaEstornosPendentes`) e guardar os dois números; testar com `MeterListener` em
  `tests/PedidoFulfillment.Testes.Integracao/Estornos/ProcessamentoEstornosTestes.cs` (SC-009).
- [ ] T042 [P] Em `docs/fluxos/10-estorno.md`: origem com o REQ-28 e a issue #57; situação `EstornoBaixado` no
  diagrama; as quatro rotas, com perfis, exemplos e erros (tabela de resultados); a baixa esperando a rodada (lock, 5 s,
  409) na seção de concorrência; a fila com os forçados primeiro; o último erro por categoria; o
  `pagamento.estorno-atrasado` e o `pagamento.estorno-baixado` com exemplos; logs 7021 a 7024; o medidor; a configuração
  de alerta igual na API e no Worker; nas Limitações, trocar as duas lacunas resolvidas pela #57 (REQ-28) e acrescentar
  a devolução dupla se o gateway confirmou e a gravação se perdeu antes da baixa (orientar consultar o gateway).
- [ ] T043 [P] Em `docs/fluxos/08-publicacao-e-consumo-de-eventos.md`, acrescentar os dois eventos à tabela (origem e
  consumidores: nenhum interno) e à seção de sistemas de fora.
- [ ] T044 [P] Em `docs/fluxos/12-rastreabilidade-e-metricas.md`, acrescentar o medidor `oms.estornos.pendentes` e o
  atributo `oms.alerta`.
- [ ] T045 [P] Em `docs/fluxos/13-notificacao-de-status.md`, registrar que o aviso `pedido.estorno-confirmado` também sai
  na baixa manual, com o id do `pagamento.estorno-baixado`.
- [ ] T046 [P] Em `docs/fluxos/README.md`, acrescentar o REQ-28 à linha do fluxo 10 (e às dos fluxos 8, 12 e 13) na
  coluna de REQs.
- [ ] T047 [P] Em `docs/proximos-passos.md`, tirar a #57 da fila (linha 38) e registrar o REQ-28 no resumo do que já foi
  entregue, como o REQ-27.
- [ ] T048 Conferir nos documentos alterados (T042 a T047) as rotas, os códigos, as mensagens, os IDs de log, o nome do
  medidor e os limites contra o código, e que os links relativos não estão quebrados; registrar no fluxo 10 que as
  rotas novas ficam fora do limite de requisições, que vale só para o login e o registro (FR-023). O README não muda (se algo
  precisar entrar nele, perguntar antes).
- [ ] T049 Atualizar o [quickstart](./quickstart.md) com o que mudou na implementação e seguir as seções 3.1 a 3.4.
- [ ] T050 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`; conferir que os contêineres,
  volumes e redes criados pelos testes não ficaram na máquina (comparar com o retrato do T001).

---

## Dependencies & Execution Order

- **Setup (T001)** e **Fundação (T002–T016)** antes de tudo; a suíte volta a ficar verde no T016.
- **US1 (fase 3)** depende da fundação. **US2 (fase 4)** usa a consulta e o `ServicoAcoesEstornos` da US1 (T021, T022)
  e a rota do grupo (T023): na prática, US1 antes da US2.
- **US3 (fase 5)** depende só da fundação e pode correr em paralelo com a US1/US2, mas divide o `ServicoEstornos.cs`
  com o T019 e o `ArmazenamentoPedidos.cs` com o T028: fazer depois deles.
- **US4 (fase 6)** depende da US1 (serviço e grupo de rotas).
- **Polish** por último; T041 depois do T039 (mesma consulta).

### Dentro de cada fase

- Testes primeiro e falhando; depois a implementação.
- T002 → T003 → T004 → T005 → T006 → T007; T016 por último na fundação.
- T018 → T019; T020 → T021 → T022 → T023 → T024.
- T027 → T028 → T029 → T030 → T031.
- T034 → T035; T037 → T038 → T039 → T040.

## Parallel Example

```text
Fundação: T008, T009, T010, T011, T012, T013, T014 e T015 em paralelo, depois de T002–T007.
US1: T017, T018 e T020 em paralelo.
US2: T025 e T026 em paralelo.
US3: T032 e T033 em paralelo.
Polish: T042 a T047 em paralelo.
```

## Implementation Strategy

1. **MVP**: fases 1 a 4 (fundação, visibilidade e baixa); um commit por fase.
2. **Incrementos**: fase 5 (evento de atraso) e fase 6 (tentativa forçada), um commit cada.
3. **Polish**: medidor e documentos de fluxo no mesmo PR, como exige o CLAUDE.md.
4. PR com `Closes #57`, citando em Validação os documentos de fluxo atualizados (08, 10, 12, 13 e o índice).
