# Contrato: catálogo de produtos (REQ-22)

**Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

Acréscimos ao `specs/001-contratos-api/contracts/openapi-v1.yaml`: o caminho
`/api/v1/produtos` e seus subcaminhos, os schemas dos DTOs e os quatro códigos de erro novos.
Nada do que existe muda (Princípio II).

## Rotas

| Rota | Perfis | Respostas |
|------|--------|-----------|
| `GET /api/v1/produtos?busca=&situacao=&tamanho=&cursor=` | Cliente, OperadorLogistica, Admin | 200, 400, 401, 403 (sem perfil; Cliente com `situacao`) |
| `GET /api/v1/produtos/{id}` | Cliente, OperadorLogistica, Admin | 200, 400 (id não UUID), 401, 403, 404 (inexistente; inativo para o Cliente) |
| `POST /api/v1/produtos` | Admin | 201 + `Location`, 400, 401, 403, 409 `PRODUTO_SKU_DUPLICADO` |
| `PUT /api/v1/produtos/{id}` | Admin | 200, 400, 401, 403, 404, 409 `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO` |
| `POST /api/v1/produtos/{id}/ajustes-estoque` | Admin | 200, 400, 401, 403, 404, 409 `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO`, 422 `ESTOQUE_INSUFICIENTE` / `ESTOQUE_ACIMA_DO_LIMITE` |
| `POST /api/v1/produtos/{id}/desativacao` | Admin | 200 (idempotente), 400, 401, 403, 404, 409 |
| `POST /api/v1/produtos/{id}/ativacao` | Admin | 200 (idempotente), 400, 401, 403, 404, 409 |

## Exemplos

### Busca do Cliente

```http
GET /api/v1/produtos?busca=camisetas%20algodao&tamanho=2
Authorization: Bearer <token do Cliente>
```

```http
HTTP/1.1 200 OK

{ "itens": [
    { "id": "0192b6a0-0000-7000-8000-000000000001", "sku": "CAT-001", "nome": "Camiseta básica",
      "descricao": "Algodão orgânico", "preco": 59.90, "moeda": "BRL", "disponivel": true } ],
  "tamanho": 2, "proximo": null, "anterior": null }
```

A mesma consulta pelo Admin ou pelo OperadorLogistica traz também `quantidadeEstoque`, `ativo`,
`criadoEm` e `atualizadoEm`.

### Cadastro

```http
POST /api/v1/produtos
Authorization: Bearer <token do Admin>
Content-Type: application/json

{ "sku": "CAF-250", "nome": "Café torrado 250 g", "descricao": "Grãos especiais",
  "preco": 32.50, "estoqueInicial": 40 }
```

`201 Created`, `Location: /api/v1/produtos/{id}`, corpo `ProdutoResponseDTO` com a visão da operação.

### Ajuste de estoque

```http
POST /api/v1/produtos/{id}/ajustes-estoque
Authorization: Bearer <token do Admin>
Content-Type: application/json

{ "variacao": -3, "motivo": "Avaria no transporte" }
```

`200 OK` com o produto e a `quantidadeEstoque` nova; com estoque 2:
`422 { "title": "Estoque insuficiente.", "detail": "O ajuste deixaria o estoque do produto CAF-250 negativo (atual: 2, variação: -3).", "codigo": "ESTOQUE_INSUFICIENTE" }`.

## Schemas (resumo)

- `ProdutoResponseDTO`: obrigatórios `id`, `sku`, `nome`, `descricao` (`['null', string]`),
  `preco` (`number`, `decimal`), `moeda`, `disponivel`; opcionais `quantidadeEstoque`
  (`int32`), `ativo`, `criadoEm`, `atualizadoEm` (`date-time`).
- `PaginaProdutosResponseDTO`: `itens`, `tamanho`, `proximo`, `anterior`, como a página de pedidos.
- `CadastrarProdutoRequestDTO`, `AlterarProdutoRequestDTO`, `AjustarEstoqueRequestDTO`: campos e
  limites da [research R-10](../research.md#r-10--validação) (`maxLength`, `minimum`, `maximum`).
- `SituacaoProduto` (parâmetro): `Ativos`, `Inativos`, `Todos`.

## Mensagens

| Situação | Resposta |
|----------|----------|
| Cliente com `situacao` | 403 "O filtro por situação é exclusivo da operação." |
| Produto inexistente ou inativo para o Cliente | 404 "Produto não encontrado." |
| SKU repetido | 409 `PRODUTO_SKU_DUPLICADO` "Já existe um produto com o SKU {sku}." |
| Tentativas esgotadas | 409 `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO` "O produto foi alterado por outra operação; tente novamente." |
| Estoque negativo | 422 `ESTOQUE_INSUFICIENTE` |
| Acima do limite | 422 `ESTOQUE_ACIMA_DO_LIMITE` "O ajuste deixaria o estoque do produto {sku} acima de 1000000000." |
| Cursor inválido | 400 `erros.cursor`, a mesma mensagem da listagem de pedidos |
