# Data Model: Cadastro e gestão do catálogo de produtos (REQ-22)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

## Persistência

### Tabela `Produtos` (existente)

| Mudança | Detalhe |
|---------|---------|
| Coluna nova `TextoBusca` | `nvarchar(1260) NOT NULL`: `Nome + ' ' + Sku + ' ' + ISNULL(Descricao, '')`; gravada pelo `ContextoBancoDados` em todo produto adicionado ou modificado (propriedade de sombra; R-03). A migration preenche os existentes |
| Índice `IX_Produtos_Nome_Sku` (novo) | `Nome, Sku`, incluindo `Preco, QuantidadeEstoque, Ativo` — listagem da operação |
| Índice `IX_Produtos_Ativo_Nome_Sku` (novo) | `Ativo, Nome, Sku`, incluindo `Preco, QuantidadeEstoque` — listagem do Cliente |
| Catálogo full-text `CatalogoProdutos` (novo) | `ACCENT_SENSITIVITY = OFF`, padrão |
| Índice full-text (novo) | `TextoBusca LANGUAGE 1046`, `KEY INDEX PK_Produtos`, `CHANGE_TRACKING AUTO`, `STOPLIST = OFF` |
| Existentes, sem mudança | `IX_Produtos_Sku` (único, `SQL_Latin1_General_CP1_CI_AS`), `CK_Produtos_PrecoNaoNegativo`, `CK_Produtos_EstoqueNaoNegativo`, `rowversion` `Versao` |

Migration: `CatalogoProdutos` (coluna, preenchimento, índices; o catálogo e o índice full-text
fora de transação; exige o FTS instalado, R-03).

### Produto (domínio, sem mudança)

`Sku`, `Nome`, `Descricao`, `Preco`, `QuantidadeEstoque`, `Ativo`, `CriadoEm`, `AtualizadoEm` e
os métodos `AlterarDados`, `AjustarEstoque` (recusa estoque negativo), `Ativar` e `Desativar`.
As regras novas da API (preço > 0 e ≤ 999.999,99, limites do estoque e da variação) ficam na
validação dos DTOs e no serviço, para não revalidar produtos existentes (spec, Assumptions).

## Tipos de aplicação

### `FiltroListagemProdutos` (novo, valor)

| Campo | Tipo | Regra |
|-------|------|-------|
| `Palavras` | lista de `string` | Da busca, em minúsculas, até 10; vazia = sem busca (R-04) |
| `Situacao` | `Ativos` \| `Inativos` \| `Todos` | Para o Cliente, sempre `Ativos` (FR-003); para a operação, o parâmetro, padrão `Todos` |

`TextoCanonico()` entra no `mac` do cursor; `CondicaoFullText()` monta
`(FORMSOF(INFLECTIONAL, "p") OR "p*") AND …`.

### `PosicaoCursorProdutos` (novo, valor)

`Sentido` (`Proxima` \| `Anterior`, o enum do REQ-21), `Nome`, `Sku`, `Inclusiva`.

### `IChaveCursor` (renomeada de `IChaveCursorPedidos`) e `CodificadorCursorProdutos` (novo)

Mesma chave dos cursores de pedidos; carga e `mac` na research R-05.

## Contratos da API (DTOs)

### `ProdutoResponseDTO` (novo)

| Campo | Tipo | Quem vê |
|-------|------|---------|
| `id` | `uuid` | todos |
| `sku` | `string` | todos |
| `nome` | `string` | todos |
| `descricao` | `string?` | todos |
| `preco` | `decimal` (2 casas) | todos |
| `moeda` | `string` (`BRL`) | todos |
| `disponivel` | `bool` (`quantidadeEstoque > 0`) | todos |
| `quantidadeEstoque` | `int32` | só a operação (omitido para o Cliente) |
| `ativo` | `bool` | só a operação |
| `criadoEm`, `atualizadoEm` | `date-time` | só a operação |

### `PaginaProdutosResponseDTO` (novo)

`itens` (`ProdutoResponseDTO`), `tamanho`, `proximo`, `anterior` — como a página de pedidos.

### Requisições (novas)

| DTO | Campos |
|-----|--------|
| `CadastrarProdutoRequestDTO` | `sku`, `nome`, `descricao?`, `preco`, `estoqueInicial` |
| `AlterarProdutoRequestDTO` | `nome`, `descricao?`, `preco` |
| `AjustarEstoqueRequestDTO` | `variacao`, `motivo` |
| `ListarProdutosRequestDTO` (parâmetros) | `busca?`, `situacao?`, `tamanho?`, `cursor?` |

Regras e mensagens na [research R-10](./research.md#r-10--validação).

### Códigos de erro novos

| Código | Status | Quando |
|--------|--------|--------|
| `PRODUTO_SKU_DUPLICADO` | 409 | SKU já cadastrado, também na disputa |
| `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO` | 409 | Tentativas esgotadas no ajuste, na alteração ou na ativação |
| `ESTOQUE_INSUFICIENTE` | 422 | O ajuste deixaria o estoque negativo |
| `ESTOQUE_ACIMA_DO_LIMITE` | 422 | O ajuste passaria de 1.000.000.000 |

`PRODUTO_INDISPONIVEL` (criação de pedido com produto inativo) não muda.
