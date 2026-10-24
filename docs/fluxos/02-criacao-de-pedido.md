# Fluxo 2: criação de pedido

**Rota**: `POST /api/v1/pedidos`, só para o perfil `Cliente`.
**Origem**: REQ-01 (contrato), REQ-04 ([domínio](../../specs/004-dominio-persistencia/spec.md))
e REQ-05 ([spec](../../specs/005-criacao-pedidos/spec.md),
[idempotência](../../specs/005-criacao-pedidos/contracts/idempotencia.md)).

## Caminho feliz

```http
POST /api/v1/pedidos
Authorization: Bearer <token do Cliente>
X-Idempotency-Key: 6f1c2a9e-3b7d-4c55-9a11-2f0e8d7c6b5a
Content-Type: application/json

{ "itens": [
    { "produtoId": "0192b6a0-0000-7000-8000-000000000001", "quantidade": 2 },
    { "produtoId": "0192b6a0-0000-7000-8000-000000000002", "quantidade": 1 }
] }
```

```http
HTTP/1.1 201 Created
Location: /api/v1/pedidos/0199f1a2-6a00-7000-8000-000000000001

{ "id": "0199f1a2-…", "numero": "PED-20261024-000001", "clienteId": "…", "status": "Pendente",
  "itens": [ { "produtoId": "…0001", "nomeProduto": "…", "quantidade": 2, "precoUnitario": 49.90, "subtotal": 99.80 }, … ],
  "valorTotal": 149.70, "moeda": "BRL", "criadoEm": "…", "atualizadoEm": "…",
  "historico": [ { "statusAnterior": null, "statusNovo": "Pendente", "ocorridoEm": "…", "motivo": null } ],
  "entrega": null }
```

Numa única gravação, o sistema:
1. confere que cada produto existe, está ativo e tem estoque suficiente (sem reservar);
2. cria o pedido `Pendente`, com o número `PED-AAAAMMDD-NNNNNN` (data em UTC) e os preços
   do cadastro naquele momento;
3. registra a chave de idempotência com a impressão do conteúdo e a resposta;
4. grava o evento `pedido.criado` no outbox.

O Worker publica o evento depois (fluxo 8), e a reserva acontece no fluxo 4.

**Por quê**: o cliente não envia preço, total, status nem dono, que vêm do cadastro e do token
(FR-016 do REQ-01). O evento sai pelo outbox para que a criação não dependa do broker (REQ-06).

## Idempotência

A chave vale por par (Cliente, chave) por pelo menos 24 h. O conteúdo é comparado por uma
impressão dos itens ordenados por produto: os mesmos pares (produto, quantidade) em outra
ordem são o mesmo conteúdo.

| Situação | Resposta | Por quê |
|----------|----------|---------|
| Chave nova | 201 e o pedido criado | — |
| Mesma chave, mesmo conteúdo | 201 com **o mesmo corpo e o mesmo `Location` da primeira resposta**, mesmo que o pedido já tenha mudado de status; nada é criado | Reenvio seguro depois de timeout ou queda de rede |
| Mesma chave, conteúdo diferente | 409 `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`: "A chave já foi usada com outro conteúdo." | Uma chave identifica uma intenção; reaproveitá-la para outra é erro do cliente |
| Duas requisições simultâneas com a mesma chave | Um pedido só. A outra recebe a resposta dele, o 409 divergente ou, raramente, 409 `IDEMPOTENCIA_EM_PROCESSAMENTO` | O índice único da chave decide a corrida |
| Tentativa anterior com 400, 422 ou 500 | A chave não foi consumida; reenviar é uma tentativa nova | Só uma criação bem-sucedida consome a chave |

Para ver o estado atual, o Cliente consulta o `Location` (fluxo 3).

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Sem `X-Idempotency-Key`, ou valor que não é UUID | 400, `erros["X-Idempotency-Key"]`: "O cabeçalho X-Idempotency-Key é obrigatório e deve ser um UUID." | `X-Idempotency-Key: abc` | Sem chave não há como garantir a idempotência |
| Lista vazia ou com mais de 50 itens | 400: "O pedido deve ter entre 1 e 50 itens." | `"itens": []` | Contrato v1 |
| Produto repetido na lista | 400: "Cada produto pode aparecer no máximo uma vez." | Dois itens com o mesmo `produtoId` | Quantidades se somam num item só |
| Quantidade fora de 1 a 999 | 400, `erros["itens[0].quantidade"]`: "A quantidade deve estar entre 1 e 999." | `"quantidade": 0` | Contrato v1 |
| `produtoId` vazio | 400: "O produto é obrigatório." | `"00000000-0000-0000-0000-000000000000"` | — |
| Produto que não existe | 422 `PRODUTO_INEXISTENTE`, com todos os produtos com problema no `detail` | Id de outro ambiente | Um único 422 lista tudo; "inexistente" prevalece sobre "indisponível" |
| Produto inativo, ou com estoque menor que a quantidade | 422 `PRODUTO_INDISPONIVEL`: "Produtos inativos ou sem estoque suficiente: …" | `CAT-009` (sem estoque) ou `CAT-010` (inativo) no catálogo de desenvolvimento | Evita aceitar um pedido que certamente falharia |
| Perfil diferente de `Cliente` | 403 | Admin tentando criar pedido | Só o Cliente compra para si |
| Sem token | 401 | — | — |
| Token de um usuário que não existe mais | Erro genérico, sem pedido criado | Conta removida do banco | A chave estrangeira do dono impede o pedido órfão |

## Concorrência e casos de borda

- **Dois pedidos com chaves diferentes ao mesmo tempo**: dois pedidos, com números diferentes.
- **Estoque suficiente na criação e esgotado logo depois**: a criação não reserva. A falta
  é tratada pelo consumidor de estoque, que cancela o pedido com "Estoque insuficiente."
  (fluxo 4).
- **Preço alterado depois da criação**: o pedido mantém o preço do momento da criação.

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico. Nada fica gravado e a chave não é consumida, porque o pedido, a chave e o evento são uma transação só |
| Broker fora do ar | Não afeta a criação. O `pedido.criado` fica pendente no outbox e sai quando o broker voltar (fluxo 8) |

## Observabilidade

Logs 4001 (pedido criado, com id, número, itens e total) e 4002 (repetição idempotente).

## Limitações

- A chave de idempotência não é apagada depois de 24 h; a limpeza está na issue #30.
