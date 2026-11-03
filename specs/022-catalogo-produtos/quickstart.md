# Quickstart: validar o catálogo de produtos (REQ-22)

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/api-produtos.md](./contracts/api-produtos.md)

## Pré-requisitos

- .NET 10 SDK e Docker. A primeira execução monta a imagem do SQL Server com Full-Text Search
  (`docker/sqlserver/Dockerfile`, cerca de 2 minutos), tanto no compose quanto nos testes.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Produto|FullyQualifiedName~Catalogo"
dotnet test
dotnet format --verify-no-changes
```

## 2. Ambiente local

```bash
docker compose up -d --wait --build     # monta a imagem do SQL Server com FTS
```

Conferir o FTS: `docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost
-U sa -P "$MSSQL_SA_PASSWORD" -Q "SELECT FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')"` → `1`.

Depois, a API pelo SDK em Development, como no README.

## 3. Roteiro manual

Tokens: `$TOKEN_CLIENTE` (Cliente novo), `$TOKEN_ADMIN` (`admin@oms.local`) e `$TOKEN_OPERADOR`.

| # | Requisição | Esperado |
|---|------------|----------|
| 1 | `GET /api/v1/produtos` sem token | 401 |
| 2 | `GET /api/v1/produtos?tamanho=3` com `$TOKEN_CLIENTE` | 200; produtos ativos pelo nome, sem `quantidadeEstoque` |
| 3 | `POST /api/v1/produtos` com `$TOKEN_CLIENTE` | 403 |
| 4 | `POST /api/v1/produtos` com `$TOKEN_ADMIN` (`CAF-250`, "Café torrado 250 g", 32.50, 40) | 201 + `Location` |
| 5 | Repetir o passo 4 com `caf-250` | 409 `PRODUTO_SKU_DUPLICADO` |
| 6 | Depois de até 10 s, `GET /api/v1/produtos?busca=cafés` com `$TOKEN_CLIENTE` | O produto do passo 4 aparece ("cafes", sem acento, não acha: limitação do fluxo 15) |
| 7 | `POST …/{id}/ajustes-estoque` `{ "variacao": -50, "motivo": "Teste" }` | 422 `ESTOQUE_INSUFICIENTE` |
| 8 | `POST …/{id}/ajustes-estoque` `{ "variacao": 10, "motivo": "Reposição" }` | 200, `quantidadeEstoque` 50 |
| 9 | Criar um pedido com o produto; `PUT …/{id}` com preço 35.00; consultar o pedido | O pedido mantém 32.50 |
| 10 | `POST …/{id}/desativacao`; Cliente consulta o produto e tenta criar pedido | 404 e 422 `PRODUTO_INDISPONIVEL`; o Operador ainda vê o produto, inativo |
| 11 | `GET /api/v1/produtos?situacao=Inativos` com `$TOKEN_CLIENTE` | 403 |

## 4. Contrato e documentação

- `ConformidadeOpenApiTestes` e o workflow **Compatibilidade do contrato** verdes.
- Fluxos 2 (criação), 4 (reserva) e o novo fluxo do catálogo atualizados, com a linha no índice.
