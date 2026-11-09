# Fluxo 15: catálogo de produtos

**Rotas**: `GET /api/v1/produtos` e `GET /api/v1/produtos/{id}` (Cliente, OperadorLogistica e
Admin); `POST /api/v1/produtos`, `PUT /api/v1/produtos/{id}`,
`POST /api/v1/produtos/{id}/ajustes-estoque`, `/desativacao` e `/ativacao` (só Admin).
**Origem**: REQ-22 ([spec](../../specs/022-catalogo-produtos/spec.md),
[research](../../specs/022-catalogo-produtos/research.md),
[contrato](../../specs/022-catalogo-produtos/contracts/api-produtos.md)), issue #53.

## Quem vê o quê

| Perfil | Produtos | Campos |
|--------|----------|--------|
| Cliente | Só os ativos; um inativo responde 404, igual a um inexistente | Id, SKU, nome, descrição, preço, moeda e `disponivel` (há ao menos uma unidade); **sem** a quantidade |
| OperadorLogistica | Todos, só leitura | Os do Cliente mais `quantidadeEstoque`, `ativo`, `criadoEm` e `atualizadoEm` |
| Admin | Todos; é o único que gerencia o catálogo | Como o OperadorLogistica |

Os campos da operação são **omitidos**, e não nulos, na resposta ao Cliente.

## Listagem e busca

```http
GET /api/v1/produtos?busca=camisetas%20algodao&tamanho=20
Authorization: Bearer <token do Cliente>
```

```http
HTTP/1.1 200 OK

{ "itens": [ { "id": "…", "sku": "CAT-001", "nome": "Camiseta básica", "descricao": "Algodão orgânico",
               "preco": 59.90, "moeda": "BRL", "disponivel": true } ],
  "tamanho": 20, "proximo": null, "anterior": null }
```

- **Ordem**: pelo nome e, no empate, pelo SKU, também com busca (a busca só filtra; não há
  ordem por relevância).
- **Paginação por cursor**, como a listagem de pedidos ([fluxo 3](03-consulta-e-mudanca-de-status.md#listagem)):
  `tamanho` de 1 a 100 (padrão 20), `proximo` e `anterior`, sem total. O cursor é assinado, vale
  só para o mesmo usuário, a mesma busca e a mesma situação, e nunca vale na listagem de pedidos
  (nem o contrário). A chave da assinatura é a mesma dos pedidos: `Listagens:ChaveCursor`, ou,
  sem ela, derivada da chave ativa do JWT, cuja troca invalida os cursores em uso (400; REQ-27,
  [rotação da chave](../rotacao-chave-jwt.md)).
- **Situação** (`Ativos`, `Inativos` ou `Todos`, padrão `Todos`): só a operação usa.
- **Busca** (`busca`, de 2 a 100 caracteres e até 10 palavras), pelo Full-Text Search do SQL
  Server em português do Brasil, sobre o nome, o SKU e a descrição:

| Busca | Acha | Por quê |
|-------|------|---------|
| `cafe` | "Café torrado" | Sem diferenciar acentos nem maiúsculas |
| `camisetas`, `calcas`, `lencol`, `pães`, `limões` | "Camiseta", "Calças", "Lençóis", "Pão", "Limões" | Plural e singular, inclusive irregulares |
| `cami` | "Camiseta" | Começo da palavra |
| `algodao` | "Camiseta básica" (descrição "Algodão orgânico") | A descrição entra na busca |
| `001`, `cat 001` | "CAT-001" | O SKU é dividido no hífen; números só por igualdade ("001" não acha "CAT-010") |
| `camiseta inox` | nada | Com várias palavras, só os produtos que têm todas |
| `"cafe" OR 1=1`, `cafe*` | tratados como as palavras `cafe`, `or`, `1` | Só letras e dígitos entram na condição, sempre como parâmetro; não há sintaxe do Full-Text Search vinda do usuário |

Um produto cadastrado ou alterado pode levar **até 10 segundos** para entrar na busca (o índice é
atualizado em segundo plano; medido: de 2,6 a 4,5 s). A listagem sem busca, a consulta por id e a
desativação valem na hora: um produto desativado some da busca do Cliente imediatamente.

## Cadastro e alteração (Admin)

```http
POST /api/v1/produtos
Content-Type: application/json

{ "sku": "CAF-250", "nome": "Café torrado 250 g", "descricao": "Grãos especiais", "preco": 32.50, "estoqueInicial": 40 }
```

`201 Created` com `Location: /api/v1/produtos/{id}` e o produto, ativo. O SKU é aparado e único
sem diferenciar maiúsculas; não muda depois do cadastro. `PUT /api/v1/produtos/{id}` troca nome,
descrição e preço: **o preço novo vale só para os pedidos criados depois**; os existentes mantêm
itens, subtotais e total. Uma alteração que não muda nada responde 200, sem gravar.

## Estoque (Admin)

```http
POST /api/v1/produtos/{id}/ajustes-estoque
Content-Type: application/json

{ "variacao": -3, "motivo": "Avaria no transporte" }
```

A variação (positiva repõe, negativa dá baixa) é **somada no banco ao estoque do momento da
gravação, numa instrução só**, com os limites conferidos na mesma instrução. A reserva do Worker
(fluxo 4), que lê, abate e grava com concorrência otimista, vê a versão do produto mudar e
reavalia com o estoque já ajustado. Por isso nada se perde: 100 em estoque, 50 repostos e 30
reservados ao mesmo tempo terminam sempre em 120 (teste de integração, 5 rodadas), e o ajuste
nunca precisa de nova tentativa. O motivo vai para o log 12003, que é o histórico dos ajustes.

## Desativação e reativação (Admin)

`POST …/desativacao` e `POST …/ativacao` respondem 200 com o produto; repetir não grava nem gera
log. O produto **não é apagado**:

| Depois de desativar | Comportamento |
|---------------------|---------------|
| Cliente lista, busca ou consulta | Não aparece (404 na consulta) |
| Operação lista ou consulta | Aparece com `ativo: false` |
| Pedido novo com ele | 422 `PRODUTO_INDISPONIVEL` ([fluxo 2](02-criacao-de-pedido.md)) |
| Pedido já criado ou reservado | Segue o fluxo normal; consulta com itens e preços originais |
| Cancelamento de um pedido reservado | Devolve o estoque ao produto inativo |

## Erros conhecidos e tratados

| Situação | Resposta | Por quê |
|----------|----------|---------|
| Sem token ou token inválido | 401 | — |
| Sem nenhum perfil; Cliente ou OperadorLogistica nas rotas de gestão | 403 | Só o Admin gerencia (clarify) |
| Cliente com `situacao` | 403 "O filtro por situação é exclusivo da operação." | Como o filtro por Cliente da listagem de pedidos |
| Produto inexistente, ou inativo para o Cliente | 404 "Produto não encontrado." | Não revela produtos inativos ao Cliente |
| `{id}` que não é UUID | 400 | — |
| Dados inválidos | 400 com todos os erros: "O SKU é obrigatório e deve ter até 50 caracteres.", "O nome é obrigatório e deve ter até 200 caracteres.", "A descrição deve ter até 1000 caracteres.", "O preço deve ser maior que zero, até 999999.99, com no máximo duas casas decimais.", "O estoque inicial deve estar entre 0 e 1000000000.", "A variação deve ser diferente de zero e estar entre -1000000 e 1000000.", "O motivo é obrigatório e deve ter até 200 caracteres.", "A busca deve ter entre 2 e 100 caracteres e de 1 a 10 palavras.", "A situação deve ser Ativos, Inativos ou Todos.", "O tamanho deve ser um número entre 1 e 100." | — |
| Cursor malformado, adulterado, de outro usuário ou de outra busca | 400 `erros.cursor` "O cursor é inválido para esta consulta; recomece da primeira página." | O cursor não revela nada |
| SKU repetido, inclusive com outra caixa ou em cadastros simultâneos | 409 `PRODUTO_SKU_DUPLICADO` "Já existe um produto com o SKU {sku}." | O índice único decide a disputa: 10 cadastros simultâneos criam um produto só |
| Alteração, desativação ou reativação com o produto mudando por outra operação | Reavaliada até 3 vezes; esgotadas, 409 `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO` "O produto foi alterado por outra operação; tente novamente." | Concorrência otimista |
| Ajuste que deixaria o estoque negativo | 422 `ESTOQUE_INSUFICIENTE` "O ajuste deixaria o estoque do produto {sku} negativo (atual: {n}, variação: {v})." | Nada é gravado |
| Ajuste acima de 1000000000 | 422 `ESTOQUE_ACIMA_DO_LIMITE` "O ajuste deixaria o estoque do produto {sku} acima de 1000000000." | Evita estouro |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico; nada fica gravado pela metade |
| SQL Server sem Full-Text Search | A migration `CatalogoProdutos` para com "O Full-Text Search do SQL Server é obrigatório (REQ-22); use a imagem docker/sqlserver." (a imagem do compose e dos testes já tem o FTS) |
| Broker ou Worker fora do ar | Não afeta o catálogo; nenhum evento é publicado |

## Observabilidade

Logs 12001 (produto cadastrado), 12002 (alterado, com o preço anterior e o novo), 12003 (estoque
ajustado, com a variação, o motivo, a quantidade nova e o usuário), 12004 (desativado), 12005
(reativado) e 12006 (alteração reavaliada por conflito, Debug).

## Limitações

- Uma palavra **sem acento e flexionada** pode não achar a forma acentuada: "cafes" não acha
  "Café", e "limao" não acha "Limões"; "cafés", "café", "limão" e "limoes" acham. O dicionário do
  português reconhece as flexões a partir da forma acentuada (research R-01).
- Um trecho no meio de um SKU sem separador não é achado: "250" não acha "CAF250".
- O histórico de ajustes de estoque fica só no log; não há tabela de movimentações.
- Não há eventos de catálogo (`produto.*`), nem exclusão de produtos, categorias, imagens,
  variações ou importação em massa.
- A imagem do SQL Server com Full-Text Search é própria (`docker/sqlserver/Dockerfile`, com a
  versão do motor fixada) e é montada no compose e nos testes, o que leva cerca de 2 minutos a
  mais na primeira vez e no CI.
