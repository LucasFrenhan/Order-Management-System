# Contrato: idempotência da criação de pedido

**Requisitos**: FR-007 a FR-013 | **Contrato HTTP**: [openapi-v1.yaml](../../001-contratos-api/contracts/openapi-v1.yaml) (`POST /api/v1/pedidos`, inalterado)

Este documento detalha o comportamento que o contrato v1 descreve de forma resumida. Ele
não acrescenta rotas, status nem códigos de erro.

## Chave

- **Cabeçalho**: `X-Idempotency-Key`, obrigatório, UUID. Ausente ou inválido recebe 400 e
  não consome a chave.
- **Escopo**: o par (usuário do token, chave). Clientes diferentes podem usar a mesma
  chave.
- **Validade**: no mínimo 24 horas a partir da criação. Depois disso, a chave pode
  continuar reconhecida ou ser tratada como nova.

## Impressão do conteúdo

- **Texto canônico**: os itens são ordenados por `produtoId` (Guid em minúsculas, formato
  `D`). Cada item vira `produtoId:quantidade`, e os itens são unidos por `;`.
- **Impressão**: SHA-256 do texto canônico, em UTF-8.
- **Consequência**: os mesmos pares (produto, quantidade) em qualquer ordem formam o mesmo
  conteúdo. Mudar um produto ou uma quantidade muda o conteúdo.

## Comportamento

| Situação | Resposta |
|----------|----------|
| Chave nova, criação bem-sucedida | 201 com `PedidoResponseDTO` e `Location`; a chave passa a valer |
| Chave já registrada, mesmo conteúdo | 201 com **o mesmo corpo e o mesmo `Location` da primeira resposta** (status `Pendente`, mesmo que o pedido tenha mudado depois); nada é criado |
| Chave já registrada, conteúdo diferente | 409 `IDEMPOTENCIA_CONTEUDO_DIVERGENTE` |
| Duas requisições com a mesma chave ao mesmo tempo | exatamente um pedido; a outra recebe a resposta dele (mesmo conteúdo), 409 divergente (outro conteúdo) ou, raramente, 409 `IDEMPOTENCIA_EM_PROCESSAMENTO` |
| Tentativa que terminou em 400, 422 ou 500 | a chave não é consumida; reenviar a mesma chave é uma tentativa nova |

## Para o estado atual

A repetição idempotente sempre devolve a resposta original. Para o status atual, o
cliente usa `GET /api/v1/pedidos/{id}` (o `Location` da resposta).
