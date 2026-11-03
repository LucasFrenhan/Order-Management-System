# Research: Cadastro e gestão do catálogo de produtos (REQ-22)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data**: 2026-11-03

## R-01 — Busca textual: Full-Text Search do SQL Server

**Decision**: usar o Full-Text Search (FTS) do SQL Server, com o separador de palavras do
português do Brasil (LCID 1046), catálogo sem acentos e sem lista de palavras irrelevantes
(stoplist), por decisão do usuário no plan, depois de um teste prático (2026-11-03).

**Teste prático** (imagem `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` + pacote
`mssql-server-fts`, 8 produtos):

| Busca | Achou |
|-------|-------|
| `cafe` | "Café torrado" (sem acento) |
| `FORMSOF(INFLECTIONAL, camisetas)` | "Camiseta básica" |
| `calca`, `calcas`, `lencol`, `papel`, `pães`, `limão`, `limões`, `limoes` | "Calças jeans", "Lençóis de casal", "Papéis coloridos", "Pão de forma", "Limões sicilianos" |
| `limao` (sem acento e no singular de um plural irregular) | nada — único caso que falhou |
| `"cami*"` | "Camiseta básica" (prefixo) |
| `"001"`, `"CAT-001"` | "Camiseta básica" (o separador divide o SKU) |
| Produto novo / renomeado | encontrado em 2,6 s / 4,5 s (`CHANGE_TRACKING AUTO`) |

**Rationale**: atende o FR-007 (palavras, sem acento, plural e singular, prefixo, SKU) com o
dicionário do próprio SQL Server, inclusive plurais irregulares, e o atraso medido fica dentro
dos 10 s do FR-007a. Não precisa de pacote NuGet: o EF Core já traduz `EF.Functions.Contains`.

**Alternatives considered**:
- Busca feita pela aplicação (coluna normalizada sem acentos, plural reduzido por regras): roda
  na imagem oficial e é imediata, mas cobre pior os plurais irregulares; descartada pelo usuário.
- `LIKE '%termo%'`: sem plural nem acentos (a collation `CI_AS` diferencia acentos) e sem índice.
- Motor de busca externo (Elasticsearch, Azure AI Search): infraestrutura nova, fora da escala.

**Limitação registrada**: `limao` (sem acento, singular) não acha "Limões"; com acento ou no
plural acha. Vai para as Limitações do fluxo do catálogo.

## R-02 — Imagem própria do SQL Server com FTS

**Decision**: `docker/sqlserver/Dockerfile`, a partir da mesma imagem de hoje
(`2022-CU26-ubuntu-22.04`, motor 16.0.4265.3), instalando `mssql-server` e `mssql-server-fts`
**na mesma versão** (`16.0.4265.3-1`) pelo repositório da Microsoft para Ubuntu 22.04, e
voltando ao usuário `mssql`. Sem a versão fixada, o `apt` traz o CU mais novo e troca o motor
(no teste, foi para 16.0.4305.5).

Onde a imagem passa a ser usada:

| Lugar | Hoje | Depois |
|-------|------|--------|
| `docker-compose.yml` (`sqlserver`, também na demonstração) | `image: mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` | `build: docker/sqlserver` com `image: oms-sqlserver:2022-CU26-fts` |
| `BancoDadosTeste`, `MensageriaTeste`, `SaudeComDependenciasTestes` | `new MsSqlBuilder("mcr…")` | `new MsSqlBuilder(ImagemSqlServerTeste.Nome)`, com a imagem montada uma vez por execução pelo `ImageFromDockerfileBuilder` do Testcontainers (já referenciado) |

**Rationale**: um único Dockerfile para todos os ambientes; a imagem dos testes é a mesma do
compose. O custo medido é cerca de 2 minutos para montar a imagem (download do pacote), uma vez
por execução da suíte e no build da demonstração.

**Alternatives considered**: publicar a imagem num registro — mais rápido no CI, mas exige um
registro e um fluxo de publicação que o projeto ainda não tem (ambientes e deploy estão em
"Para análise futura").

## R-03 — Coluna de busca e índice full-text

**Decision**:
- Nova coluna `Produtos.TextoBusca` `nvarchar(1260)` não nula, com `Nome + ' ' + Sku + ' ' +
  ISNULL(Descricao, '')`, mantida pelo `ContextoBancoDados` ao gravar produtos adicionados ou
  modificados (propriedade de sombra; o domínio não muda).
- Catálogo `CatalogoProdutos` com `ACCENT_SENSITIVITY = OFF`; índice full-text em `TextoBusca`
  com `LANGUAGE 1046`, `KEY INDEX PK_Produtos`, `CHANGE_TRACKING AUTO` e `STOPLIST = OFF`.
- A migration `CatalogoProdutos` cria a coluna, preenche os produtos existentes e cria o
  catálogo e o índice com `suppressTransaction: true` (o SQL Server não cria índice full-text
  dentro de transação). Sem o FTS instalado, ela falha com uma mensagem clara
  (`THROW 50001, 'O Full-Text Search do SQL Server é obrigatório (REQ-22)…'`).

**Rationale**: uma única coluna faz o "todas as palavras" valer entre nome, SKU e descrição
(com várias colunas, o `CONTAINS` exige todas as palavras na mesma coluna). O FTS não indexa
colunas calculadas não determinísticas de forma confiável, por isso a coluna é gravada. Sem
stoplist, palavras como "de" não anulam um `AND`.

## R-04 — Montagem da condição de busca

**Decision**: o texto é quebrado em palavras (sequências de letras e dígitos; mais de 10 dá 400);
cada palavra vira `(FORMSOF(INFLECTIONAL, "p") OR "p*")`, e as palavras se juntam com `AND`. A
condição vai como **parâmetro** de `EF.Functions.Contains(p.TextoBusca, condicao, 1046)`; o
usuário nunca escreve a sintaxe do FTS. Sem nenhuma palavra (só pontuação) → 400.

**Rationale**: só letras e dígitos entram na condição, então não há como injetar operadores do
FTS; o prefixo cobre "cami" e o `FORMSOF` cobre plural e singular.

## R-05 — Ordem, cursor e reaproveitamento do REQ-21

**Decision**:
- Ordem `Nome ASC, Sku ASC`, com e sem busca (clarify); paginação por chave nos dois sentidos,
  `tamanho + 1`, sem total, exatamente como a listagem de pedidos.
- A interface da chave passa a se chamar `IChaveCursor` (era `IChaveCursorPedidos`), porque
  agora assina os dois cursores; a implementação `ChaveCursorDerivadaDoJwt` não muda. As
  referências nos documentos (fluxo 3, próximos passos, research do REQ-21) são atualizadas.
- Novo `CodificadorCursorProdutos`, com a mesma estrutura do de pedidos: carga
  `{"v":1,"s":"p"|"a","n":"<Nome>","k":"<Sku>","i":true?}` e `mac` sobre
  `"produtos" ‖ carga ‖ usuarioId ‖ filtros canônicos (busca normalizada, situação)`. O rótulo
  `"produtos"` impede usar um cursor de produtos na listagem de pedidos e vice-versa.
- Índices: `IX_Produtos_Nome_Sku` (inclui `Preco`, `QuantidadeEstoque`, `Ativo`) e
  `IX_Produtos_Ativo_Nome_Sku` (para o Cliente).

**Alternatives considered**: um codificador genérico parametrizado — a carga dos dois difere
(data versus texto); duplicar ~80 linhas com testes próprios é mais simples que generalizar
agora.

## R-06 — Perfis, visões e políticas

**Decision**:
- Consulta e listagem: `PoliticaConsultaPedido` já cobre os três perfis; uma política nova
  `PoliticaCatalogo` (= `PoliticaAdministracao`, só Admin) para cadastro, alteração, ajuste,
  desativação e reativação (FR-008).
- Visão: `EhOperacao` (Admin ou OperadorLogistica) decide. Cliente: só ativos; o filtro
  `situacao` dá 403 "O filtro por situação é exclusivo da operação." (FR-004); produto inativo
  dá o mesmo 404 de um inexistente.
- `ProdutoResponseDTO` único: `quantidadeEstoque`, `ativo`, `criadoEm` e `atualizadoEm` são
  opcionais no schema e **omitidos** para o Cliente (não vêm como `null`).

## R-07 — Ajuste de estoque concorrente

**Decision**: `ServicoCatalogo.AjustarEstoqueAsync` lê o produto para alterar, chama
`Produto.AjustarEstoque(variacao)` e grava; num `ConflitoConcorrenciaException` (o `rowversion`
do produto mudou por uma reserva, devolução ou outro ajuste), descarta e repete sobre o estado
atual, até 3 vezes, como a reserva (REQ-08, R-03); esgotadas, 409
`PRODUTO_ALTERADO_POR_OUTRA_OPERACAO`. Estoque final negativo → 422 `ESTOQUE_INSUFICIENTE`;
acima de 1.000.000.000 → 422 `ESTOQUE_ACIMA_DO_LIMITE`. A alteração de dados e a ativação usam
o mesmo laço.

**Rationale**: a variação é aplicada sobre a leitura de cada tentativa, e o `rowversion` impede
gravar sobre uma leitura antiga: nenhuma unidade se perde (SC-002). O domínio já recusa estoque
negativo.

## R-08 — SKU único e cadastro concorrente

**Decision**: o SKU é aparado; a unicidade já é garantida pelo índice único `IX_Produtos_Sku`
com collation sem diferenciar maiúsculas. O serviço confere antes (resposta rápida) e traduz a
violação do índice na gravação (disputa entre dois cadastros) para 409
`PRODUTO_SKU_DUPLICADO`, como o rastreio duplicado (REQ-11).

## R-09 — Rotas

| Método e rota | Perfis | Sucesso |
|---------------|--------|---------|
| `GET /api/v1/produtos` | todos | 200 `PaginaProdutosResponseDTO` |
| `GET /api/v1/produtos/{id}` | todos | 200 `ProdutoResponseDTO` |
| `POST /api/v1/produtos` | Admin | 201 + `Location` |
| `PUT /api/v1/produtos/{id}` | Admin | 200 (nome, descrição, preço) |
| `POST /api/v1/produtos/{id}/ajustes-estoque` | Admin | 200 |
| `POST /api/v1/produtos/{id}/desativacao` | Admin | 200 (idempotente) |
| `POST /api/v1/produtos/{id}/ativacao` | Admin | 200 (idempotente) |

`PUT` porque a alteração troca os três campos editáveis de uma vez; as ações de ativação seguem o
padrão `POST /{id}/reativacao` dos destinos de notificação (REQ-18).

## R-10 — Validação

Como no REQ-21: parâmetros e corpos validados por `FiltroValidacao` com todas as mensagens.

| Campo | Regra | Mensagem |
|-------|-------|----------|
| `sku` | obrigatório, até 50 após aparar | `O SKU é obrigatório e deve ter até 50 caracteres.` |
| `nome` | obrigatório, até 200 após aparar | `O nome é obrigatório e deve ter até 200 caracteres.` |
| `descricao` | opcional, até 1.000 | `A descrição deve ter até 1000 caracteres.` |
| `preco` | > 0, ≤ 999.999,99, até 2 casas | `O preço deve ser maior que zero, até 999999.99, com no máximo duas casas decimais.` |
| `estoqueInicial` | 0 a 1.000.000.000 | `O estoque inicial deve estar entre 0 e 1000000000.` |
| `variacao` | ≠ 0, de -1.000.000 a 1.000.000 | `A variação deve ser diferente de zero e estar entre -1000000 e 1000000.` |
| `motivo` | obrigatório, até 200 após aparar | `O motivo é obrigatório e deve ter até 200 caracteres.` |
| `busca` | 2 a 100 caracteres, de 1 a 10 palavras | `A busca deve ter entre 2 e 100 caracteres e de 1 a 10 palavras.` |
| `situacao` | `Ativos`, `Inativos` ou `Todos` | `A situação deve ser Ativos, Inativos ou Todos.` |
| `tamanho`, `cursor` | como no REQ-21 | as mesmas mensagens |

## R-11 — Logs

Faixa 12001–12006 (livre), `Information`, sem dado pessoal além do id do usuário:
`12001 ProdutoCadastrado`, `12002 ProdutoAlterado` (preço anterior e novo), `12003
EstoqueAjustado` (variação, motivo, quantidade nova, usuário), `12004 ProdutoDesativado`,
`12005 ProdutoReativado`, `12006 ConflitoNoAjusteEstoque` (tentativa, `Debug`).

## R-12 — Testes e dependências

- **Unitários**: validações, condição de busca (palavras, limite, caracteres especiais),
  codificador de cursor de produtos, regras do serviço com armazenamento falso (visões, 403,
  404 de inativo, ajuste com conflito e esgotamento, idempotência da ativação).
- **Integração** (imagem com FTS): rotas com 200/201, 401, 403 por perfil; busca em português
  (os casos da R-01, esperando o índice por condição, com prazo de 10 s); concorrência de 50
  unidades repostas junto de 30 reservas (SC-002); preço antigo preservado nos pedidos (SC-003);
  desempenho com 10.000 produtos (SC-005); SKU duplicado concorrente.
- **Nenhum pacote novo**: `ImageFromDockerfileBuilder` é do Testcontainers já referenciado;
  `EF.Functions.Contains` é do EF Core SqlServer.
