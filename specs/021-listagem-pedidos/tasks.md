---

description: "Lista de tarefas do REQ-21 — Listagem e paginação de pedidos por perfil"
---

# Tasks: Listagem e paginação de pedidos por perfil

**Input**: Design documents from `/specs/021-listagem-pedidos/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/api-listagem-pedidos.md](./contracts/api-listagem-pedidos.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras, integração com
sucesso, 401 e 403 de toda rota protegida e conformidade do contrato; a issue #52 pede testes
de escopo do Cliente e de paginação. Os testes de cada fase vêm antes da implementação e devem
falhar primeiro.

**Organization**: uma fundação com os tipos, o cursor, a validação, a consulta e a rota sem
filtros; depois uma fase por história. A US1 (Cliente lista os próprios pedidos) é o MVP. A US2
(filtros da operação) e a US4 (escopo do Cliente) dependem só da fundação. A US3 (navegação por
cursor) depende da fundação e pode correr junto das outras.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; o endpoint só roteia; regras no `ServicoPedidos` (Application); consulta
  no `ArmazenamentoPedidos` (Infrastructure). O domínio não muda.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo** (plan, R-12). Uma única migration, `ListagemPedidos`, só com índices.
- Contrato v1 só com acréscimos: a rota nova e os schemas entram no
  `specs/001-contratos-api/contracts/openapi-v1.yaml` na mesma tarefa que cria a rota, para o
  `ConformidadeOpenApiTestes` continuar verde.
- Só leitura: a listagem não grava nada nem publica evento (FR-023).
- Mensagens de erro exatamente como em [research R-05](./research.md#r-05--parâmetros-de-consulta-e-validação-com-todos-os-erros)
  e [R-06](./research.md#r-06--escopo-por-perfil-e-o-403-do-filtro-por-cliente); o 400 do cursor
  nunca diz o motivo nem mostra o conteúdo dele.
- A chave do cursor chega só por `IChaveCursorPedidos` (R-04): hoje derivada da
  `Jwt:ChaveAssinatura`; nenhum outro código conhece a origem, para que a troca futura por uma
  chave própria mude só o registro na API.
- Log só o `4006 PedidosListados` (R-10), sem valores de filtro; IDs existentes não mudam.
- Os testes existentes do REQ-01 ao REQ-20 passam sem afrouxar asserções.
- Testes com banco usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER` (REQ-07). Nenhuma espera
  fixa em teste.
- Commits: `tipo(escopo): descrição [REQ-21]`, um por fase.

---

## Phase 1: Setup

**Purpose**: linha de base antes de mexer no código.

- [ ] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-21-listagem-pedidos` com Docker e registrar em Notes a contagem de testes (unitários e integração) e zero falhas.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: tipos, cursor, validação, consulta por chave, índices e a rota básica. Bloqueia
todas as histórias.

### Testes da fundação (escrever primeiro)

- [ ] T002 [P] Testes unitários do `CodificadorCursorPedidos` em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/CodificadorCursorPedidosTestes.cs`: ida e volta da posição (`Sentido`, `CriadoEm`, `Numero`); carga alterada num byte → falha; `mac` alterado → falha; texto sem ponto, base64url inválido, JSON inválido ou `"v"` diferente de 1 → falha; outro `usuarioId` → falha; filtros diferentes (um status a mais, outro período, outro `clienteId`) → falha; os mesmos status em outra ordem ou repetidos → aceito; o texto do cursor não contém o `usuarioId` nem o `clienteId`; chaves diferentes de `IChaveCursorPedidos` → falha.
- [ ] T003 [P] Testes unitários da validação em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ValidacaoListarPedidosTestes.cs`, um por regra da tabela da research R-05 com a mensagem exata: status desconhecido (`O status '{valor}' não existe.`), status sem diferenciar maiúsculas aceito, data sem fuso, data malformada, início depois do fim nos dois períodos, `clienteId` não UUID, `tamanho` `0`, `101` e `abc` (`O tamanho deve ser um número entre 1 e 100.`), padrão 20 sem `tamanho`; e vários erros juntos voltando todos (FR-022).

### Implementação da fundação

- [ ] T004 [P] Criar `ResumoPedidoResponseDTO` em `src/PedidoFulfillment.Application/Contratos/Pedidos/ResumoPedidoResponseDTO.cs` com `Id`, `Numero`, `ClienteId`, `Status`, `ValorTotal`, `Moeda`, `QuantidadeItens` ("Número de linhas de item", R-08), `CriadoEm`, `AtualizadoEm`, com os mesmos nomes JSON e formatos do `PedidoResponseDTO`; e `PaginaPedidosResponseDTO` em `src/PedidoFulfillment.Application/Contratos/Pedidos/PaginaPedidosResponseDTO.cs` com `Itens`, `Tamanho`, `Proximo` (`string?`, "null na última página") e `Anterior` (`string?`, "null na primeira página"), sempre serializados, inclusive quando `null` (data-model).
- [ ] T005 [P] Criar `FiltroListagemPedidos` em `src/PedidoFulfillment.Application/Pedidos/FiltroListagemPedidos.cs` (status como conjunto sem repetição; `CriadoDe`, `CriadoAte`, `AtualizadoDe`, `AtualizadoAte` em UTC, inclusivos; `ClienteId?`) com o método que gera o texto canônico da research R-03 (status ordenados, datas em UTC no formato ISO `O`, `clienteId` ou vazio); e `PosicaoCursor` com `SentidoCursor { Proxima, Anterior }` em `src/PedidoFulfillment.Application/Pedidos/PosicaoCursor.cs`.
- [ ] T006 Criar `IChaveCursorPedidos` (expõe os bytes da chave) e `CodificadorCursorPedidos` em `src/PedidoFulfillment.Application/Pedidos/CodificadorCursorPedidos.cs`, conforme a research R-03: `base64url(carga) + "." + base64url(mac)`, carga `{"v":1,"s":"p"|"a","c":"<CriadoEm>","n":"<Numero>"}`, `mac` = 16 primeiros bytes de `HMAC-SHA256(chave, carga ‖ usuarioId ‖ texto canônico do filtro)`, comparação com `CryptographicOperations.FixedTimeEquals`; `TentarDecodificar` nunca lança exceção para entrada ruim. Faz a T002 passar.
- [ ] T007 Criar `ListarPedidosRequestDTO` em `src/PedidoFulfillment.Application/Contratos/Pedidos/ListarPedidosRequestDTO.cs` com propriedades de texto ligadas aos parâmetros `status` (`string[]?`), `criadoDe`, `criadoAte`, `atualizadoDe`, `atualizadoAte`, `clienteId`, `tamanho` e `cursor` (`[FromQuery(Name = ...)]`, para uso com `[AsParameters]`), com `IValidatableObject` aplicando as regras e mensagens da research R-05 (o cursor não é validado aqui) e um método que converte para `FiltroListagemPedidos` e `int tamanho` depois de válido. Faz a T003 passar.
- [ ] T008 Acrescentar `ListarResumosAsync(FiltroListagemPedidos filtro, PosicaoCursor? posicao, int quantidade, CancellationToken)` a `src/PedidoFulfillment.Application/Pedidos/IArmazenamentoPedidos.cs` e implementar em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`: `AsNoTracking`, filtros aplicados, projeção direta para `ResumoPedidoResponseDTO` com `Itens.Count()` sem carregar itens, histórico, pagamento nem entrega (FR-019); sem posição ou com `Proxima`: ordem `CriadoEm DESC, Numero DESC` e só os estritamente depois da posição (`CriadoEm < c || (CriadoEm == c && string.Compare(Numero, n) < 0)`); com `Anterior`: ordem crescente e só os estritamente antes, devolvendo a lista já invertida para a ordem decrescente; `Take(quantidade)` (R-01, R-02, R-07). Atualizar o falso de `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` com a mesma semântica em memória.
- [ ] T009 Índices em `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoPedido.cs`: remover `IX_Pedidos_ClienteId_CriadoEm`; criar `IX_Pedidos_ClienteId_CriadoEm_Numero` (`ClienteId, CriadoEm DESC, Numero DESC`, incluindo `Status, AtualizadoEm, ValorTotal, Moeda`) e `IX_Pedidos_CriadoEm_Numero` (`CriadoEm DESC, Numero DESC`, incluindo `ClienteId, Status, AtualizadoEm, ValorTotal, Moeda`); gerar a migration `ListagemPedidos` em `src/PedidoFulfillment.Infrastructure/Persistencia/Migracoes/` e conferir que ela só mexe em índices (data-model).
- [ ] T010 Acrescentar `ListarAsync(ListarPedidosRequestDTO, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken)` a `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs` e implementar em `src/PedidoFulfillment.Application/Pedidos/ServicoPedidos.cs` (injetando `CodificadorCursorPedidos`): monta o filtro; se não for operação (`EhOperacao`), fixa `ClienteId = usuarioId`; decodifica o cursor (falha → `TipoFalha.Validacao` com `erros.cursor` = `O cursor é inválido para esta consulta; recomece da primeira página.`); busca `tamanho + 1`; monta `Proximo` e `Anterior` pelas regras da research R-02, inclusive a página vazia vinda de um cursor, que mantém o cursor do sentido oposto a partir da posição recebida. Nesta tarefa, sem o 403 do filtro por Cliente (US4).
- [ ] T011 Registrar `IChaveCursorPedidos` (implementação `ChaveCursorDerivadaDoJwt` em `src/PedidoFulfillment.API/Seguranca/ChaveCursorDerivadaDoJwt.cs`, com `HKDF.DeriveKey(SHA256, ChaveAssinatura, 32, info: "oms/listagem-pedidos/cursor/v1")` calculado uma vez, R-04) e `CodificadorCursorPedidos` como singletons no registro de serviços da API; comentário no registro dizendo que trocar a origem da chave por uma chave própria é mudar só esta linha.
- [ ] T012 Acrescentar o log `4006 PedidosListados` (`Debug`) em `src/PedidoFulfillment.Application/Pedidos/EventosPedidos.cs`, com perfil (`Cliente` ou `Operacao`), nomes dos filtros usados, sentido (`Inicio`, `Proxima`, `Anterior`) e quantidade de itens, sem valores (R-10), e chamá-lo no `ListarAsync`.
- [ ] T013 Rota `MapGet("")` `ListarPedidos` em `src/PedidoFulfillment.API/Endpoints/EndpointsPedidos.cs` com `[AsParameters] ListarPedidosRequestDTO`, `RequireAuthorization(PoliticasAutorizacao.PoliticaConsultaPedido)`, `AddEndpointFilter<FiltroValidacao<ListarPedidosRequestDTO>>()`, `Produces<PaginaPedidosResponseDTO>()` e `ProduzErros(400, 403)`; acrescentar ao `specs/001-contratos-api/contracts/openapi-v1.yaml` o `get` e os schemas de [contracts/api-listagem-pedidos.md](./contracts/api-listagem-pedidos.md) e, se o documento gerado mostrar os parâmetros como `string`, ajustar os formatos (`date-time`, `uuid`, `int32` com `default: 20`, array de `StatusPedido`) em `src/PedidoFulfillment.API/OpenApi/` até o `ConformidadeOpenApiTestes` passar.
- [ ] T014 Incluir `GET /api/v1/pedidos` na matriz de `tests/PedidoFulfillment.Testes.Integracao/Seguranca/AutorizacaoEndpointsTestes.cs`: 401 sem token e com token inválido; 403 para usuário autenticado sem nenhum dos três perfis (FR-002, FR-003).

**Checkpoint**: a rota responde para os três perfis, com validação e cursor; o contrato está
conforme.

---

## Phase 3: User Story 1 - O Cliente lista os próprios pedidos (Priority: P1) 🎯 MVP

**Goal**: o Cliente vê só os pedidos dele, do mais recente ao mais antigo, com o resumo.

**Independent Test**: pedidos de dois Clientes; cada um lista e recebe só os seus, na ordem
certa, com os campos do resumo iguais aos da consulta por id.

- [ ] T015 [P] [US1] Testes unitários do escopo em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosListagemTestes.cs`: Cliente recebe o filtro com o próprio `ClienteId`; usuário com Cliente e um perfil da operação tem escopo da operação (FR-007); sem cursor, `Anterior` é `null`.
- [ ] T016 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosTestes.cs` (criar o arquivo e o cenário com Testcontainers, reaproveitando `CenarioPedidos`): Cliente com 3 pedidos e outro com 2 → 200 com os 3, ordem decrescente, sem cursores; Cliente sem pedidos → lista vazia sem cursores; o resumo tem exatamente os campos do `ResumoPedidoResponseDTO`, sem `itens`, `historico` nem `entrega`, e `numero`, `status`, `valorTotal`, `moeda`, `criadoEm` e `atualizadoEm` iguais aos do `GET /api/v1/pedidos/{id}` (FR-018 a FR-020); `quantidadeItens` = número de linhas de item; a listagem não grava nada no outbox nem nos avisos (FR-023).
- [ ] T017 [US1] Ajustar o que os testes T015 e T016 apontarem no `ServicoPedidos.ListarAsync` e no `ArmazenamentoPedidos.ListarResumosAsync` até ficarem verdes.

**Checkpoint**: MVP entregue; o Cliente acha os próprios pedidos sem guardar ids.

---

## Phase 4: User Story 2 - A operação lista e filtra todos os pedidos (Priority: P1)

**Goal**: OperadorLogistica e Admin listam todos os pedidos e filtram por status, pelos dois
períodos e por Cliente.

**Independent Test**: pedidos de vários Clientes em status e datas diferentes; cada filtro e a
combinação dão exatamente os pedidos esperados.

- [ ] T018 [P] [US2] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosFiltrosTestes.cs`, com `CriadoEm` e `AtualizadoEm` controlados pelo relógio de teste: operação sem filtros vê todos os Clientes (Admin e OperadorLogistica); `status=ProntoParaEnvio`; `status=Pendente&status=Processando`; `PagamentoRejeitado` e `Cancelado` filtráveis; status repetido = uma vez; período de criação com limites inclusivos, só início, só fim e início igual ao fim; período de última alteração (pedido criado há uma semana que mudou ontem versus um que mudou hoje, acceptance 6 da US2); `clienteId` de um Cliente e de um usuário inexistente (lista vazia, não 404); todos os filtros juntos.
- [ ] T019 [US2] Ajustar os filtros no `ArmazenamentoPedidos.ListarResumosAsync` (`Status IN`, `CriadoEm` e `AtualizadoEm` entre os limites inclusivos, `ClienteId`) até a T018 passar.

**Checkpoint**: a operação acha pedidos parados ou de um Cliente sem saber ids.

---

## Phase 5: User Story 3 - Navegação por cursor e erros claros (Priority: P2)

**Goal**: próxima e anterior sem repetir nem pular pedidos, com tamanho limitado e 400 claros.

**Independent Test**: 45 pedidos com tamanho 20, ida até o fim e volta; pedido novo no meio;
empate de `CriadoEm`; parâmetros e cursores inválidos.

- [ ] T020 [P] [US3] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosNavegacaoTestes.cs`: 45 pedidos, tamanho 20 → 20, 20 e 5, sem repetição, última sem `proximo`, primeira sem `anterior`; da terceira, `anterior` devolve a segunda igual (mesmos pedidos e ordem, com os dois cursores); pedido criado entre a página 1 e a 2 não desloca nem repete nada e aparece ao recomeçar (SC-002); dois pedidos com o mesmo `CriadoEm` na divisa entre páginas, nenhum perdido nem repetido; troca de `tamanho` no meio aceita; pedido que sai do filtro de status durante a navegação não quebra o cursor; sem parâmetros → 20 itens.
- [ ] T021 [P] [US3] Testes de integração dos erros em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosErrosTestes.cs`: 400 em `ValidacaoProblemDetails` com a mensagem exata da research R-05 para `tamanho` `0`, `101` e `abc`, status desconhecido, data sem fuso e período invertido; três erros juntos na mesma resposta; cursor malformado, com um caractere trocado, com filtros diferentes e de outro usuário → 400 `erros.cursor` `O cursor é inválido para esta consulta; recomece da primeira página.`; nenhum caso dá 500 (SC-006).
- [ ] T022 [P] [US3] Teste de desempenho em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosDesempenhoTestes.cs`, no padrão de `DesempenhoCriacaoTestes`: 10.000 pedidos inseridos em lote; primeira página, uma página a 50 passos de profundidade pelo cursor e uma com `status` e período, cada uma em até 1 s (SC-004).
- [ ] T023 [US3] Ajustar a montagem dos cursores no `ServicoPedidos.ListarAsync` e o keyset no `ArmazenamentoPedidos` até T020 a T022 passarem.

**Checkpoint**: navegação estável nos dois sentidos e erros claros.

---

## Phase 6: User Story 4 - O Cliente não alcança pedidos de outros (Priority: P1)

**Goal**: o filtro por Cliente dá 403 para o Cliente, e nenhum parâmetro vaza pedido de outro.

**Independent Test**: Cliente com `clienteId` (de outro e o próprio) → 403; cursor da operação
usado pelo Cliente → 400; filtros só sobre os próprios pedidos.

- [ ] T024 [P] [US4] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosListagemTestes.cs`: Cliente com `clienteId` (outro e o próprio) → `TipoFalha.SemPermissao` com `"Operação não permitida."` / `"O filtro por cliente é exclusivo da operação."`, sem chamar o armazenamento; o 403 vem antes da validação do cursor; operação com `clienteId` aceita.
- [ ] T025 [P] [US4] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ListagemPedidosEscopoTestes.cs`: Cliente com `clienteId` de outro e com o próprio → 403 com a mensagem; Cliente com `clienteId` malformado → 400; Cliente com filtros de status e de período só vê os próprios; cursor devolvido à operação e a outro Cliente, usado pelo Cliente → 400 e nada listado; varredura combinando status, períodos, tamanhos e cursores sem nenhum pedido de outro Cliente na resposta (SC-001).
- [ ] T026 [US4] Implementar o 403 do filtro por Cliente no `ServicoPedidos.ListarAsync` (research R-06), antes de decodificar o cursor e de consultar o banco, até T024 e T025 passarem.

**Checkpoint**: escopo do Cliente fechado para qualquer parâmetro.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T027 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo mensagens, limites, IDs de log e links relativos no código:
  - `docs/fluxos/03-consulta-e-mudanca-de-status.md`: rota nova no cabeçalho e REQ-21 na origem; seção **Listagem** com exemplo de requisição e resposta, os filtros, a navegação por cursor e a tabela de respostas (200, 400 com as mensagens, 401, 403 sem perfil e 403 do filtro por Cliente) com o motivo de cada uma; falhas externas (banco fora do ar → 500); log 4006;
  - em **Limitações**: a chave do cursor hoje é derivada da chave de assinatura do JWT; trocá-la (rotação da #60) invalida os cursores em uso (400, recomeçar da primeira página), e a chave pode passar no futuro a uma chave própria do cursor, mudando só o registro de `IChaveCursorPedidos` (R-04); sem total nem salto de página, por decisão da clarify;
  - `docs/fluxos/README.md`: a rota nova na linha do fluxo 3; o diagrama de estados não muda.
- [ ] T028 [P] Documentação do projeto: `docs/estado-atual.md` e `docs/proximos-passos.md` com a listagem entregue e a pendência da chave própria do cursor. O `README.md` **não** cresce sem permissão (regra do `CLAUDE.md`): se houver algo a acrescentar lá, perguntar antes.
- [ ] T029 Varredura de segredos no diff, `dotnet build`, `dotnet format --verify-no-changes` e a suíte inteira com Docker (zero ignorados); registrar a contagem em Notes, comparando com a T001.
- [ ] T030 Executar o [quickstart](./quickstart.md) (§2) com a API local e registrar o resultado em Notes, corrigindo o quickstart onde divergir.
- [ ] T031 Push e PR em português (Resumo, Pontos de atenção, Validação) com `Closes #52`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, a paginação por cursor sem total e a chave do cursor derivada do JWT, com a possível troca futura por uma chave própria. Acompanhar `build-testes` e `Compatibilidade do contrato` até ficarem verdes, inclusive depois de cada push posterior, e entrar por auto-merge com rebase.

---

## Dependencies & Execution Order

- **Setup** (T001): antes de tudo.
- **Foundational** (T002 a T014): bloqueia todas as histórias. T002 antes de T006; T003 antes de T007; T004 e T005 antes de T006 a T008; T006 a T008 antes de T010; T010 e T011 antes de T013; T013 antes de T014.
- **US1** (T015 a T017): depende da fundação.
- **US2** (T018 e T019): depende da fundação; pode correr junto da US1.
- **US3** (T020 a T023): depende da fundação; pode correr junto da US1 e da US2.
- **US4** (T024 a T026): depende da fundação; T024 usa o mesmo arquivo da T015, então vem depois dela.
- **Polish** (T027 a T031): depois de tudo. A T031 exige push.

### Parallel Opportunities

- T002 e T003; T004 e T005.
- T015 e T016; T018; T020, T021 e T022; T025.
- T027 e T028.

## Parallel Example: User Story 3

```text
Juntos: T020 (navegação), T021 (erros), T022 (desempenho)
Depois: T023
```

## Implementation Strategy

1. Setup e fundação → `feat(pedidos): rota de listagem com cursor e validacao [REQ-21]`.
2. US1 (MVP) → `test(pedidos): listagem do cliente [REQ-21]`; parar e validar.
3. US2 → `feat(pedidos): filtros da operacao na listagem [REQ-21]`.
4. US3 → `test(pedidos): navegacao por cursor, erros e desempenho da listagem [REQ-21]`.
5. US4 → `feat(pedidos): 403 do filtro por cliente na listagem [REQ-21]`.
6. Polish → `docs(fluxos): listagem de pedidos no fluxo 3 [REQ-21]`, push e PR.

## Notes

- A chave do cursor vem hoje da `Jwt:ChaveAssinatura` (HKDF), por `IChaveCursorPedidos`; a troca
  futura por uma chave própria é mudar só o registro na API (pedido do usuário no tasks,
  2026-11-03; research R-04).
