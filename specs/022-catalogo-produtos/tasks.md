---

description: "Lista de tarefas do REQ-22 — Cadastro e gestão do catálogo de produtos"
---

# Tasks: Cadastro e gestão do catálogo de produtos

**Input**: Design documents from `/specs/022-catalogo-produtos/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/api-produtos.md](./contracts/api-produtos.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras, integração com
sucesso, 401 e 403 de toda rota protegida e conformidade do contrato; a issue #53 pede a prova de
que a reposição convive com a reserva sem perder unidades. Os testes da fundação vêm antes da
implementação; nas fases das histórias, descrevem o comportamento esperado e os que falharem
orientam o ajuste.

**Organization**: uma fundação com a imagem do SQL Server com Full-Text Search, a migration, os
tipos, o cursor, a busca e as rotas de leitura; depois uma fase por história. A US1 (o Cliente
encontra os produtos) é o MVP. A US2 (cadastro e alteração), a US3 (estoque) e a US4 (ativação)
dependem só da fundação.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; o endpoint só roteia; regras no `ServicoCatalogo`; consultas no
  `ArmazenamentoProdutos`. O domínio `Produto` não muda (data-model).
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo** (R-12). Uma única migration, `CatalogoProdutos`. Sem eventos novos.
- Contrato v1 só com acréscimos: cada rota nova entra no
  `specs/001-contratos-api/contracts/openapi-v1.yaml` e numa linha de `ConformidadeOpenApiTestes`
  na mesma tarefa que a cria.
- Mensagens e códigos exatamente como na [research R-10](./research.md#r-10--validação) e no
  [contrato](./contracts/api-produtos.md#mensagens).
- A condição do Full-Text Search só contém palavras de letras e dígitos e vai sempre como
  parâmetro (R-04); nunca concatenar o texto do usuário em SQL.
- Logs só na faixa 12001–12006 (R-11); IDs existentes não mudam.
- Os testes existentes do REQ-01 ao REQ-21 passam sem afrouxar asserções.
- Testes com banco usam a imagem própria (`ImagemSqlServerTeste`) e respeitam
  `OMS_EXIGIR_DOCKER`. Esperas da busca só por condição, com prazo de 10 s (FR-007a); nenhuma
  espera fixa.
- Commits: `tipo(escopo): descrição [REQ-22]`, um por fase.

---

## Phase 1: Setup

- [ ] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-22-catalogo-produtos` com Docker e registrar em Notes a contagem (unitários e integração) e zero falhas.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: imagem com FTS em todos os ambientes, migration, tipos, cursor, busca, armazenamento,
serviço de leitura e rotas de leitura. Bloqueia todas as histórias.

### Imagem do SQL Server com Full-Text Search (R-02)

- [ ] T002 Criar `docker/sqlserver/Dockerfile` a partir de `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`: como `root`, instalar `curl`, `gnupg` e `ca-certificates`, registrar a chave `https://packages.microsoft.com/keys/microsoft.asc` em `/usr/share/keyrings/microsoft-prod.gpg` e o repositório `https://packages.microsoft.com/ubuntu/22.04/mssql-server-2022 jammy main`, instalar `mssql-server=16.0.4265.3-1` e `mssql-server-fts=16.0.4265.3-1` (versão fixada: sem ela o motor muda, R-02), limpar `/var/lib/apt/lists` e voltar a `USER mssql`; comentário no topo com o motivo (REQ-22, FTS) e como atualizar a versão junto com a tag da base.
- [ ] T003 `docker-compose.yml`: o serviço `sqlserver` passa a `build: { context: docker/sqlserver }` com `image: oms-sqlserver:2022-CU26-fts`, mantendo variáveis, volume e healthcheck; conferir que `docker compose --profile demo up -d --wait --build` sobe tudo saudável e que `SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` dá 1.
- [ ] T004 Criar `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ImagemSqlServerTeste.cs`: monta uma vez por execução (`Lazy<Task>` estático) a imagem `oms-sqlserver-testes:2022-CU26-fts` com o `ImageFromDockerfileBuilder` do Testcontainers a partir de `docker/sqlserver` (caminho achado subindo até a raiz do repositório), sem apagar a imagem ao fim; trocar a imagem em `Infraestrutura/BancoDadosTeste.cs`, `Infraestrutura/MensageriaTeste.cs` e `Saude/SaudeComDependenciasTestes.cs` por `ImagemSqlServerTeste`, aguardando a montagem antes do `MsSqlBuilder`; sem Docker, o comportamento de hoje (ignorar ou falhar com `OMS_EXIGIR_DOCKER`) não muda.

### Persistência

- [ ] T005 `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoProduto.cs`: propriedade de sombra `TextoBusca` (`nvarchar(1260)`, obrigatória) e os índices `IX_Produtos_Nome_Sku` (inclui `Preco, QuantidadeEstoque, Ativo`) e `IX_Produtos_Ativo_Nome_Sku` (inclui `Preco, QuantidadeEstoque`); em `ContextoBancoDados.cs`, antes de gravar, preencher `TextoBusca = Nome + " " + Sku + " " + (Descricao ?? "")` em todo `Produto` adicionado ou modificado (R-03).
- [ ] T006 Gerar a migration `CatalogoProdutos` (`dotnet ef migrations add CatalogoProdutos --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`) e completá-la à mão: preencher `TextoBusca` dos produtos existentes; com `suppressTransaction: true`, conferir `FULLTEXTSERVICEPROPERTY('IsFullTextInstalled') = 1` (senão `THROW 50001, 'O Full-Text Search do SQL Server é obrigatório (REQ-22); use a imagem docker/sqlserver.', 1`), criar `CatalogoProdutos` com `ACCENT_SENSITIVITY = OFF AS DEFAULT` e o índice full-text em `TextoBusca LANGUAGE 1046`, `KEY INDEX PK_Produtos`, `WITH CHANGE_TRACKING AUTO, STOPLIST = OFF`; `Down` desfaz na ordem inversa. Atualizar as listas de migrations em `ModeloSincronizadoTestes` e `MigracaoBancoTestes`.

### Tipos, cursor e busca (testes primeiro)

- [ ] T007 [P] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/FiltroListagemProdutosTestes.cs`: palavras extraídas da busca (letras e dígitos, minúsculas, sem repetição, até 10); `"cafe torrado"` → `(FORMSOF(INFLECTIONAL, "cafe") OR "cafe*") AND (FORMSOF(INFLECTIONAL, "torrado") OR "torrado*")`; aspas, asteriscos, `AND`/`OR`/`NEAR`, parênteses e pontuação nunca chegam à condição como operadores; `"CAT-001"` vira as palavras `cat` e `001`; texto canônico igual para a mesma busca com outra caixa, ordem de espaços ou repetição.
- [ ] T008 [P] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/CodificadorCursorProdutosTestes.cs`, espelhando `CodificadorCursorPedidosTestes`: ida e volta (`Sentido`, `Nome` com acentos, `Sku`, `Inclusiva`); carga e `mac` alterados; malformados; outro usuário; outra busca ou situação; o mesmo cursor recusado pelo `CodificadorCursorPedidos` e vice-versa (rótulo `"produtos"`, R-05); o texto não contém o `usuarioId`.
- [ ] T009 [P] Testes unitários das validações em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/ValidacaoCatalogoTestes.cs`, um por regra da research R-10 com a mensagem exata, para `CadastrarProdutoRequestDTO`, `AlterarProdutoRequestDTO`, `AjustarEstoqueRequestDTO` e `ListarProdutosRequestDTO` (inclui preço 0, -1, 1000000.00, 10.123 e 0.01 aceito; variação 0, ±1000001; motivo em branco; busca de 1 e 101 caracteres e `"!!"`; situação desconhecida; vários erros juntos).
- [ ] T010 Renomear `IChaveCursorPedidos` para `IChaveCursor` (interface em `src/PedidoFulfillment.Application/Comum/IChaveCursor.cs`), atualizando `CodificadorCursorPedidos`, `ChaveCursorDerivadaDoJwt`, `Program.cs`, os testes e as menções em `docs/fluxos/03-consulta-e-mudanca-de-status.md`, `docs/proximos-passos.md` e `specs/021-listagem-pedidos/research.md` (com a nota "renomeada no REQ-22"); a suíte do REQ-21 continua verde.
- [ ] T011 [P] Criar os DTOs em `src/PedidoFulfillment.Application/Contratos/Produtos/`: `ProdutoResponseDTO` (`Id`, `Sku`, `Nome`, `Descricao`, `Preco`, `Moeda` = "BRL", `Disponivel`; opcionais `QuantidadeEstoque`, `Ativo`, `CriadoEm`, `AtualizadoEm` com `JsonIgnore(WhenWritingNull)`, "omitidos para o Cliente"), `PaginaProdutosResponseDTO`, `CadastrarProdutoRequestDTO`, `AlterarProdutoRequestDTO`, `AjustarEstoqueRequestDTO` e `ListarProdutosRequestDTO` (texto, como o de pedidos), com as regras e mensagens da research R-10 (`IValidatableObject`, valores aparados no `init`). Faz a T009 passar.
- [ ] T012 [P] Criar em `src/PedidoFulfillment.Application/Produtos/`: `SituacaoProduto { Ativos, Inativos, Todos }`, `FiltroListagemProdutos` (palavras, situação, `TextoCanonico()`, `CondicaoFullText()`, `NomesUsados()`), `PosicaoCursorProdutos` e `CodificadorCursorProdutos` (carga `{"v":1,"s","n","k","i"}`, `mac` com o rótulo `"produtos"`, R-05). Faz a T007 e a T008 passar.
- [ ] T013 Acrescentar a `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs` os códigos `PRODUTO_SKU_DUPLICADO`, `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO`, `ESTOQUE_INSUFICIENTE` e `ESTOQUE_ACIMA_DO_LIMITE`, e ao catálogo de códigos do `openapi-v1.yaml` (se houver enum de `codigo`, só acréscimos).
- [ ] T014 Criar `IArmazenamentoProdutos` em `src/PedidoFulfillment.Application/Produtos/` (`ObterAsync(id, paraAlterar)`, `SkuEmUsoAsync`, `Adicionar`, `ListarAsync(filtro, posicao, quantidade)`, `SalvarAsync`, `DescartarAlteracoes`) e `ArmazenamentoProdutos` em `src/PedidoFulfillment.Infrastructure/Persistencia/`: listagem por chave `Nome ASC, Sku ASC` nos dois sentidos (estrito ou inclusivo, `tamanho + 1`, a anterior invertida), situação, e a busca por `EF.Functions.Contains(EF.Property<string>(p, "TextoBusca"), condicao, 1046)` com a condição como parâmetro; violação do índice `IX_Produtos_Sku` vira `SkuDuplicadoException`. Registrar no `ExtensoesInfraestrutura`.
- [ ] T015 Criar `IServicoCatalogo` e `ServicoCatalogo` em `src/PedidoFulfillment.Application/Produtos/` com a leitura: `ListarAsync` (Cliente: situação `Ativos` fixa, `situacao` informado → 403 "O filtro por situação é exclusivo da operação."; operação: padrão `Todos`; cursor inválido → 400 `erros.cursor`; montagem dos cursores como no REQ-21) e `ObterAsync` (inexistente, ou inativo para o Cliente → 404 "Produto não encontrado."); visão por perfil (`EhOperacao`) mapeando para `ProdutoResponseDTO` com ou sem os campos da operação. Criar `EventosCatalogo.cs` com os logs 12001–12006 (R-11).
- [ ] T016 API: `PoliticaCatalogo` (só Admin) em `PoliticasAutorizacao.cs`; `ParametrosListarProdutos` (`[FromQuery]` com `busca`, `situacao`, `tamanho`, `cursor`) e `ParametrosListagemProdutosOpenApi`; `EndpointsProdutos.cs` com `GET /api/v1/produtos` e `GET /api/v1/produtos/{id}` (`PoliticaConsultaPedido`, sem `{id:guid}` para o 400); registros em `Program.cs`; acréscimos ao `openapi-v1.yaml` (as duas rotas, `ProdutoResponseDTO`, `PaginaProdutosResponseDTO`, `SituacaoProduto`) e as linhas em `ConformidadeOpenApiTestes`.
- [ ] T017 Testes de rota sem banco em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/ProdutosTestes.cs`, cobrindo desde já as 7 rotas do contrato à medida que existem: 401 sem token e com assinatura inválida; 403 sem perfil; 403 do Cliente e do OperadorLogistica nas rotas de gestão; 400 de id não UUID.

**Checkpoint**: imagem com FTS em todos os ambientes, migration aplicada, leitura do catálogo
pelas duas rotas e contrato conforme.

---

## Phase 3: User Story 1 - O Cliente encontra os produtos que pode pedir (Priority: P1) 🎯 MVP

**Goal**: o Cliente lista, busca e consulta os produtos ativos e cria um pedido com eles.

**Independent Test**: catálogo com ativos e inativos; o Cliente vê só os ativos, sem quantidade;
a busca em português acha os casos da R-01; o identificador cria um pedido.

- [ ] T018 [P] [US1] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/ServicoCatalogoLeituraTestes.cs` com armazenamento falso: Cliente recebe só ativos e a situação fixa; Cliente com `situacao` → 403 sem consultar; inativo e inexistente com o mesmo 404 para o Cliente; operação vê inativos com `quantidadeEstoque`, `ativo` e datas; Cliente sem esses campos; `disponivel` = estoque > 0; cursor de outro usuário → 400.
- [ ] T019 [P] [US1] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Produtos/CatalogoClienteTestes.cs`: ordem por nome e SKU, só ativos para o Cliente e todos para o Operador e o Admin; o JSON do Cliente não tem `quantidadeEstoque`, `ativo`, `criadoEm` nem `atualizadoEm`; navegação de 45 produtos em páginas de 20 nos dois sentidos; 404 igual para inativo e inexistente; um produto da listagem cria um pedido com o preço exibido (SC-001).
- [ ] T020 [P] [US1] Testes de integração da busca em `tests/PedidoFulfillment.Testes.Integracao/Produtos/BuscaCatalogoTestes.cs`, com os produtos da R-01 e espera por condição (até 10 s) até o índice refletir: `cafe`, `camisetas`, `calcas`, `lencol`, `papel`, `pães`, `limões`, `cami`, `algodao` (descrição), `001` (SKU), `camiseta algodao` (todas as palavras) e `camiseta inox` (nenhum); busca combinada com o cursor; `"cafe" OR 1=1`, aspas e `*` tratados como texto; produto desativado some da busca do Cliente na hora (SC-006).
- [ ] T021 [US1] Ajustar o que os testes T018 a T020 apontarem.

**Checkpoint**: MVP — o Cliente monta um pedido sem conhecer identificadores.

---

## Phase 4: User Story 2 - O Admin cadastra e altera produtos (Priority: P1)

**Goal**: cadastro e alteração pelo Admin, com SKU único e preço novo só para pedidos novos.

**Independent Test**: cadastrar, alterar, criar pedidos antes e depois da mudança de preço e
conferir os preços; SKU repetido dá 409, também em disputa.

- [ ] T022 [P] [US2] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/ServicoCatalogoGestaoTestes.cs`: cadastro cria ativo, com o SKU aparado; SKU em uso → 409 `PRODUTO_SKU_DUPLICADO` "Já existe um produto com o SKU {sku}."; `SkuDuplicadoException` na gravação → o mesmo 409; alteração muda só nome, descrição e preço; alteração sem mudança → 200; conflito de concorrência reavaliado até 3 vezes e depois 409 `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO`; logs 12001 e 12002 (com preço anterior e novo).
- [ ] T023 [US2] Implementar no `ServicoCatalogo` o `CadastrarAsync` e o `AlterarAsync` (laço de 3 tentativas, R-07 e R-08) e as rotas `POST /api/v1/produtos` (201 + `Location`) e `PUT /api/v1/produtos/{id}` com `PoliticaCatalogo` e `FiltroValidacao`; acréscimos ao `openapi-v1.yaml` e às linhas de conformidade.
- [ ] T024 [P] [US2] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Produtos/CadastroProdutosTestes.cs`: 201 com `Location` e corpo da operação; 400 com todos os erros; SKU repetido com outra caixa → 409; 10 cadastros simultâneos do mesmo SKU → um 201 e nove 409, um produto só; preço alterado não muda pedidos existentes (itens, subtotal e total), e o pedido novo usa o preço novo (SC-003); o Admin vê na busca o produto renomeado em até 10 s.

**Checkpoint**: o catálogo é mantido pela API.

---

## Phase 5: User Story 3 - O estoque é reposto e ajustado sem perder unidades (Priority: P1)

**Goal**: ajuste por variação, concorrente com a reserva do Worker, sem perder nem criar unidades.

**Independent Test**: repor 50 enquanto 30 pedidos de 1 unidade são reservados ao mesmo tempo e
conferir o estoque final exato.

- [ ] T025 [P] [US3] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/ServicoCatalogoEstoqueTestes.cs`: reposição e baixa; estoque final negativo → 422 `ESTOQUE_INSUFICIENTE` com a mensagem do contrato, sem gravar; acima de 1.000.000.000 → 422 `ESTOQUE_ACIMA_DO_LIMITE`; conflito reavaliado sobre o estoque atual (a variação aplicada sobre a releitura); esgotado → 409; log 12003 com variação, motivo, quantidade nova e usuário, e 12006 a cada conflito.
- [ ] T026 [US3] Implementar `AjustarEstoqueAsync` no `ServicoCatalogo` e a rota `POST /api/v1/produtos/{id}/ajustes-estoque` (Admin), com acréscimos ao contrato e à conformidade.
- [ ] T027 [P] [US3] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Produtos/EstoqueConcorrenteTestes.cs`, com o Worker de reserva do REQ-08 (o mesmo arranjo de `Estoque/DisputaEstoqueTestes`): produto com 100, reposição de 50 junto de 30 pedidos de 1 unidade sendo reservados → estoque final 120 em todas as execuções de um laço de 5 rodadas (SC-002); baixa que deixaria negativo → 422 e nada muda; devolução por cancelamento junto de um ajuste soma as duas.

**Checkpoint**: a reposição convive com a reserva.

---

## Phase 6: User Story 4 - O Admin desativa e reativa produtos (Priority: P2)

**Goal**: desativar sem apagar e reativar, de forma idempotente.

**Independent Test**: desativar um produto com pedidos; os pedidos continuam consultáveis, o
Cliente não o vê e não consegue pedi-lo; reativar e conferir que volta.

- [ ] T028 [P] [US4] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Produtos/ServicoCatalogoAtivacaoTestes.cs`: desativar e reativar mudam a situação e geram 12004 e 12005; repetir não muda nada, responde 200 e não grava nem gera log.
- [ ] T029 [US4] Implementar `DesativarAsync` e `AtivarAsync` e as rotas `POST /api/v1/produtos/{id}/desativacao` e `/ativacao` (Admin), com acréscimos ao contrato e à conformidade.
- [ ] T030 [P] [US4] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Produtos/AtivacaoProdutosTestes.cs`: desativado some do Cliente (listagem, busca e consulta) e continua visível para a operação; criar pedido com ele → 422 `PRODUTO_INDISPONIVEL`; pedidos antigos continuam consultáveis com itens e preços; cancelar um pedido com estoque reservado de um produto inativo devolve o estoque; reativado volta a aparecer e a poder ser pedido.

**Checkpoint**: o ciclo de vida do produto está completo.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T031 [P] Teste de desempenho em `tests/PedidoFulfillment.Testes.Integracao/Produtos/CatalogoDesempenhoTestes.cs`: 10.000 produtos em lote (com `TextoBusca`), espera o índice full-text terminar de popular; primeira página, página a 50 passos e página com busca, cada uma em até 1 s (SC-005).
- [ ] T032 [P] Documentos de fluxo (regra do `CLAUDE.md`), conferindo mensagens, códigos, limites, IDs de log e links no código:
  - novo `docs/fluxos/15-catalogo-de-produtos.md`, no formato dos demais: rotas, visões por perfil, busca em português (com exemplos e o atraso de até 10 s), cadastro, alteração, estoque e concorrência, ativação, erros com resposta e motivo, falhas externas, logs 12001–12006 e limitações (o caso `limao`, histórico de ajustes só no log, sem eventos, sem apagar produto);
  - linha do fluxo 15 no índice `docs/fluxos/README.md`;
  - `docs/fluxos/02-criacao-de-pedido.md`: o Cliente acha os produtos pela listagem; preço lido na criação, sem efeito de mudanças posteriores; produto inativo;
  - `docs/fluxos/04-reserva-de-estoque.md`: o ajuste do Admin disputa com a reserva e é reavaliado, sem perder unidades.
- [ ] T033 [P] Documentação do projeto: `docs/estado-atual.md` (linha do REQ-22), `docs/proximos-passos.md` (tira a #53 da fila; anota a tabela de movimentações de estoque e eventos de catálogo como análise futura), e onde a imagem do SQL Server é citada (`docs/integracao-continua.md` e a seção Ambiente local do README, só corrigindo o texto que ficar errado, sem crescer). Qualquer acréscimo ao README só com permissão (regra do `CLAUDE.md`).
- [ ] T034 Varredura de segredos, `dotnet build`, `dotnet format --verify-no-changes` e a suíte inteira com Docker (zero ignorados); registrar a contagem em Notes, comparando com a T001, e o tempo a mais da montagem da imagem.
- [ ] T035 Executar o [quickstart](./quickstart.md) (§2 e §3) com o compose montando a imagem e a API local; registrar em Notes e corrigir o quickstart onde divergir.
- [ ] T036 Push e PR em português (Resumo, Pontos de atenção, Validação) com `Closes #53`, citando em Pontos de atenção a imagem própria do SQL Server (versão fixada, ~2 min no CI, migration exige FTS) e a troca de `IChaveCursorPedidos` por `IChaveCursor`, e em Validação os documentos de fluxo. Acompanhar `Build e testes`, `Demonstração`, SonarCloud e a compatibilidade do contrato até ficarem verdes, inclusive depois de cada push, armar o auto-merge por rebase e marcar esta tarefa antes do merge.

---

## Dependencies & Execution Order

- **Setup** (T001): antes de tudo.
- **Foundational** (T002 a T017): T002 antes de T003 e T004; T004 antes de qualquer teste de integração; T005 antes de T006; T007 a T009 antes de T011 e T012; T010 antes de T012; T011 a T014 antes de T015; T015 antes de T016; T016 antes de T017.
- **US1** (T018 a T021), **US2** (T022 a T024), **US3** (T025 a T027) e **US4** (T028 a T030): dependem da fundação; as rotas de gestão (T023, T026, T029) mexem no mesmo `EndpointsProdutos.cs` e no mesmo `ServicoCatalogo.cs`, então rodam em sequência entre si.
- **Polish** (T031 a T036): depois de tudo. A T036 exige push.

### Parallel Opportunities

- T007, T008 e T009; T011 e T012.
- T018, T019 e T020; T022 e T024 (depois da T023 para os testes de integração); T025 e T027; T028 e T030.
- T031, T032 e T033.

## Parallel Example: Foundational

```text
Juntos: T007 (busca), T008 (cursor), T009 (validação)
Depois: T011 e T012 → T013 → T014 → T015 → T016 → T017
```

## Implementation Strategy

1. Setup e fundação → `build(sqlserver): imagem com full-text search nos ambientes e nos testes [REQ-22]` e `feat(produtos): leitura do catalogo com busca em portugues [REQ-22]`.
2. US1 (MVP) → `test(produtos): catalogo e busca para o cliente [REQ-22]`.
3. US2 → `feat(produtos): cadastro e alteracao pelo admin [REQ-22]`.
4. US3 → `feat(produtos): ajuste de estoque concorrente [REQ-22]`.
5. US4 → `feat(produtos): desativacao e reativacao [REQ-22]`.
6. Polish → `docs(fluxos): catalogo de produtos [REQ-22]`, push e PR.

## Notes

- Decisões do clarify e do plan: só o Admin gerencia; o OperadorLogistica vê como o Admin, só
  leitura; o Cliente vê só ativos, sem quantidade; listagem por cursor ordenada pelo nome; busca
  pelo Full-Text Search do SQL Server em pt-BR (teste prático na research R-01); histórico de
  ajustes só no log; sem eventos novos; catálogo inicial só em Development e testes.
