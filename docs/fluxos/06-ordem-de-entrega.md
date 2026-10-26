# Fluxo 6: ordem de entrega

**Onde roda**: Worker, consumidor `entrega` (fila `oms.entrega`), ao receber `pagamento.aprovado`.
**Origem**: REQ-10 ([spec](../../specs/010-fulfillment/spec.md),
[contrato](../../specs/010-fulfillment/contracts/consumidor-entrega.md)), REQ-13
([spec](../../specs/013-cancelamento-com-estorno/spec.md), cancelamento da ordem) e REQ-15
([spec](../../specs/015-visibilidade-cancelamentos/spec.md), `pedido.cancelado`).
A base de consumo está no [fluxo 8](08-publicacao-e-consumo-de-eventos.md).

## Caminho feliz

1. Chega o `pagamento.aprovado`.
2. O pedido está **apto**: estoque reservado, pagamento `Aprovado`, sem ordem de entrega e em
   `Processando` ou `ProntoParaEnvio`.
3. Na mesma transação: a ordem de entrega é criada sem transportadora, código nem data de
   despacho; o pedido vai de `Processando` a `ProntoParaEnvio`, sem autor, com o motivo
   "Ordem de entrega criada."; e o `entrega.criada` é gravado no outbox.

```json
{ "tipo": "entrega.criada", "versao": 1,
  "dados": { "pedidoId": "…", "numero": "PED-20261024-000001", "ordemEntregaId": "…",
             "itens": [ { "produtoId": "…0001", "quantidade": 2 } ] } }
```

Aqui termina o fluxo automático. O despacho é feito pela operação (fluxo 7). O
`entrega.criada` não tem consumidor interno: ele existe para sistemas externos, como a
logística.

## Resultados possíveis

| Pedido lido | Ordem | Status | Evento | Por quê |
|-------------|-------|--------|--------|---------|
| `Processando`, apto | criada | `ProntoParaEnvio` | `entrega.criada` | — |
| `ProntoParaEnvio`, apto (a operação já o moveu) | criada | sem mudança | `entrega.criada` | A ordem faltava; o status já estava à frente |
| Não apto: `Pendente`, `Cancelado`, `Despachado` ou `PagamentoRejeitado`; sem pagamento aprovado; sem reserva; ou já com ordem | — | sem mudança | nenhum (log 8002, com o motivo) | No máximo uma ordem por pedido; pedido cancelado ou despachado não ganha ordem |
| Não existe | — | — | novas tentativas e DLQ | — |
| Conflito nas 3 reavaliações | — | sem mudança | novas tentativas e DLQ | — |

Estoque e pagamento nunca mudam neste fluxo.

## Casos de borda

| Caso | Comportamento |
|------|---------------|
| Pedido cancelado pela operação depois de `ProntoParaEnvio` | Permitido; na mesma gravação, o estoque volta, o estorno fica pendente e a ordem ganha `canceladaEm` (log 8004). Uma ordem cancelada não é despachada: o rastreio de pedido cancelado responde 409 (#28, REQ-13) |
| Pedido cancelado antes da ordem, com o `pagamento.aprovado` chegando depois | O consumidor o ignora (log 8002): pedido cancelado não ganha ordem |
| Ordem criada pelo consumidor ao mesmo tempo que o cancelamento | Um dos dois vence. Se a ordem grava antes, o cancelamento relê e cancela a ordem também; se o cancelamento grava antes, a ordem não é criada |
| Pedido movido pela operação direto para `Despachado` antes da ordem | Status final: o consumidor o ignora. O despacho com rastreio cria a ordem que falta (fluxo 7) |
| Ordem criada ao mesmo tempo pelo consumidor e pelo despacho | O índice único de `PedidoId` vira conflito de concorrência: um relê o pedido e segue sobre a ordem do outro |
| Pedido pago antes de a fila `oms.entrega` existir | Antes do REQ-12, ficava em `Processando` sem ordem. Agora a fila é declarada antes de publicar (fluxo 8), e a reconciliação recupera os antigos (fluxo 9) |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Worker cai depois de gravar e antes do ack | A reentrega é descartada como duplicada (transação única) |
| Banco fora do ar | Novas tentativas e DLQ; depois, a reconciliação |
| Broker fora do ar depois da ordem | O `entrega.criada` fica pendente no outbox; a ordem não é desfeita |
| Mensagem fora do contrato | Direto para a DLQ |

## Observabilidade

Logs 8001 (ordem criada), 8002 (ignorado), 8003 (conflito) e 8004 (ordem cancelada junto com
o pedido, escrito pela API).

## Pedido cuja ordem falha sempre

Desde o REQ-13 (#28), a reconciliação cancela o pedido pago cuja criação da ordem falha em 3
ciclos e que está parado há mais de 24 h, com o motivo "Não foi possível criar a ordem de
entrega.", o estoque devolvido e o estorno pendente (fluxo 9).

## Limitações

- Nenhuma. O cancelamento da ordem, que antes não chegava a outros sistemas, sai no `pedido.cancelado`, com
  `ordemEntregaCancelada: true`, para a logística externa que leu o `entrega.criada` (#29, REQ-15).
