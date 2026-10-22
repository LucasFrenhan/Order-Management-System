# Contrato: consumidor de estoque (REQ-08)

Complementa [topologia e consumo](../../006-mensageria/contracts/topologia-e-consumo.md) do
REQ-06. Tudo o que não está aqui (envelope, novas tentativas, DLQ, duplicados) segue aquele
documento.

## Topologia

| Item | Valor |
|------|-------|
| Consumidor | `estoque` |
| Fila | `oms.estoque`, ligada a `oms.eventos` com `pedido.criado` |
| Filas de espera | `oms.estoque.espera.{1000,2000,4000}` |
| DLQ | `oms.estoque.dlq` |
| Publica | `estoque.reservado` v1 ([schema](../../006-mensageria/contracts/eventos/estoque.reservado.v1.schema.json)) e `estoque.insuficiente` v1 ([schema](./eventos/estoque.insuficiente.v1.schema.json)) |

## Resultado por pedido

| Pedido lido | Estoque | Status | Publica | Mensagem |
|-------------|---------|--------|---------|----------|
| Não existe | — | — | — | nova tentativa; DLQ no fim |
| Status final ou já reservado | sem mudança | sem mudança | nada | ack |
| Não final, todos os itens disponíveis | abatido | sem mudança | `estoque.reservado` | ack |
| Não final, algum item indisponível | sem mudança | `Cancelado` | `estoque.insuficiente` | ack |
| Conflito de versão nas 3 reavaliações | sem mudança | sem mudança | nada | nova tentativa; DLQ no fim |

Disponível: produto ativo e `QuantidadeEstoque >= Quantidade` do item.

## Exemplos

`estoque.reservado`:

```json
{
  "idEvento": "0199f1a2-7b3c-7d4e-8f50-6a7b8c9d0e1f",
  "tipo": "estoque.reservado",
  "versao": 1,
  "ocorridoEm": "2026-10-22T21:15:03.512Z",
  "idCorrelacao": "0199f1a2-6a00-7000-8000-000000000001",
  "dados": {
    "pedidoId": "0199f1a2-6a00-7000-8000-000000000001",
    "itens": [{ "produtoId": "0199f0aa-0000-7000-8000-000000000008", "quantidade": 1 }]
  }
}
```

`estoque.insuficiente`:

```json
{
  "idEvento": "0199f1a2-7b3c-7d4e-8f50-6a7b8c9d0e20",
  "tipo": "estoque.insuficiente",
  "versao": 1,
  "ocorridoEm": "2026-10-22T21:15:03.601Z",
  "idCorrelacao": "0199f1a2-6a00-7000-8000-000000000002",
  "dados": {
    "pedidoId": "0199f1a2-6a00-7000-8000-000000000002",
    "itens": [
      { "produtoId": "0199f0aa-0000-7000-8000-000000000008", "quantidadeSolicitada": 1, "quantidadeDisponivel": 0, "inativo": false }
    ]
  }
}
```

## Devolução no cancelamento pela API

Não muda o contrato HTTP v1. `PATCH /api/v1/pedidos/{id}/status` com `Cancelado`, num pedido
com estoque reservado, devolve o estoque na mesma gravação. Se o pedido ou os produtos
mudarem durante a operação, ela é reavaliada até 3 vezes; esgotadas, responde o 409
`PEDIDO_TRANSICAO_INVALIDA` que já existe.

## Evolução

Mudanças em `estoque.insuficiente` seguem o Princípio II: só acréscimos compatíveis na
versão 1, ou versão nova.
