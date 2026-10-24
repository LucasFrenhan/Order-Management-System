# Fluxo 3: consulta e mudança de status

**Rotas**: `GET /api/v1/pedidos/{id}` e `PATCH /api/v1/pedidos/{id}/status`, para todos os perfis.
**Origem**: REQ-01 ([ciclo de vida](../../specs/001-contratos-api/data-model.md#ciclo-de-vida-do-pedido-fr-020)),
REQ-05 ([spec](../../specs/005-criacao-pedidos/spec.md)), REQ-08 (devolução do estoque),
REQ-11 (histórico e entrega na resposta) e REQ-13 ([spec](../../specs/013-cancelamento-com-estorno/spec.md),
estorno e cancelamento da ordem).

## Consulta

```http
GET /api/v1/pedidos/0199f1a2-6a00-7000-8000-000000000001
Authorization: Bearer <token>
```

```http
HTTP/1.1 200 OK

{ "id": "…", "numero": "PED-20261024-000001", "status": "ProntoParaEnvio", "itens": [ … ], "valorTotal": 149.70,
  "historico": [
    { "statusAnterior": null, "statusNovo": "Pendente", "ocorridoEm": "…", "motivo": null },
    { "statusAnterior": "Pendente", "statusNovo": "Processando", "ocorridoEm": "…", "motivo": "Pagamento aprovado." },
    { "statusAnterior": "Processando", "statusNovo": "ProntoParaEnvio", "ocorridoEm": "…", "motivo": "Ordem de entrega criada." }
  ],
  "entrega": { "transportadora": null, "codigoRastreio": null, "despachadoEm": null, "criadoEm": "…", "canceladaEm": null } }
```

- O histórico vem em ordem cronológica, com o motivo de cada mudança e **sem o autor**, para
  todos os perfis.
- `entrega` é `null` enquanto não há ordem de entrega. `entrega.canceladaEm` é preenchido
  quando a ordem é cancelada junto com o pedido (REQ-13).
- O pagamento e o estorno não aparecem na consulta: as situações de estorno são internas
  (REQ-13, clarify).

| Situação | Resposta | Por quê |
|----------|----------|---------|
| Cliente dono, Admin ou OperadorLogistica | 200 | — |
| Cliente consultando pedido de outro | 404, o mesmo corpo de um pedido inexistente | Não revela que o pedido existe (FR-015) |
| Pedido inexistente | 404 "Pedido não encontrado." | — |
| `{id}` que não é UUID | 400 | — |
| Sem token | 401 | — |

## Mudança de status

```http
PATCH /api/v1/pedidos/0199f1a2-…/status
Authorization: Bearer <token>
Content-Type: application/json

{ "status": "Cancelado", "motivo": "Cliente desistiu da compra." }
```

Resposta 200 com o pedido atualizado e a nova entrada no histórico, com o autor gravado (mas
não exibido) e o motivo aparado.

### Transições permitidas

| De | Para | Quem |
|----|------|------|
| `Pendente` | `Processando` | Admin, OperadorLogistica |
| `Processando` | `ProntoParaEnvio` | Admin, OperadorLogistica |
| `ProntoParaEnvio` | `Despachado` (sem rastreio; ver fluxo 7) | Admin, OperadorLogistica |
| `Pendente`, `Processando`, `ProntoParaEnvio` | `Cancelado` | Admin, OperadorLogistica |
| `Pendente` | `Cancelado` | Cliente dono |

### O que o cancelamento leva junto

Tudo na mesma gravação do cancelamento, sem chamar o gateway de pagamento (#28, REQ-13, R-01):

| O pedido tem | O cancelamento faz | Log |
|--------------|--------------------|-----|
| Estoque reservado | O estoque volta aos produtos (REQ-08) | 6005 |
| Pagamento `Aprovado` | O pagamento passa a `EstornoPendente`; o Worker estorna depois (fluxo 10) | 7005, origem `Operacao` |
| Ordem de entrega sem despacho | A ordem ganha `canceladaEm` e não pode mais ser despachada | 8004 |

Exemplo: cancelar um pedido em `ProntoParaEnvio` responde 200 com
`"entrega": { …, "despachadoEm": null, "canceladaEm": "2026-10-24T15:20:00+00:00" }`; o estorno
é confirmado em até 1 minuto pelo Worker.

**Por que o estorno é assíncrono**: o cancelamento não pode depender do gateway. Com o gateway
fora do ar, o pedido é cancelado e o estorno espera (decisão do specify do REQ-13).

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Status ausente ou desconhecido | 400 "O status é obrigatório." | `"status": "Enviado"` | — |
| `PagamentoRejeitado` pedido pela API | 400 "O status PagamentoRejeitado não pode ser definido por esta operação." | — | Só o fluxo de pagamento define esse status |
| `Cancelado` sem motivo | 400 "O motivo é obrigatório quando o status é Cancelado." | `{ "status": "Cancelado" }` | O cancelamento precisa de justificativa no histórico |
| Motivo com mais de 500 caracteres | 400 "O motivo deve ter até 500 caracteres." | — | — |
| Transição fora da tabela, para a operação | 409 `PEDIDO_TRANSICAO_INVALIDA`: "O pedido está em Despachado e não pode passar para Cancelado." | Cancelar um pedido despachado; pular de `Pendente` para `Despachado`; repetir o status atual | Estados finais não voltam, e a ordem do fluxo é respeitada |
| Cliente tentando qualquer coisa além de `Pendente → Cancelado` no próprio pedido | 403 "O cliente só pode cancelar o próprio pedido enquanto ele está Pendente." | Cliente cancelando um pedido já `Processando` | Depois do pagamento, cancelar é com a operação, e o cancelamento gera o estorno (REQ-13, clarify) |
| Cliente alterando pedido de outro | 404 | — | Não revela que o pedido existe |
| Pedido alterado por outra operação durante a mudança | A mudança é reavaliada sobre o estado atual, até 3 vezes; esgotadas, 409 `PEDIDO_TRANSICAO_INVALIDA` "O pedido foi alterado por outra operação; tente novamente." | Operação cancelando enquanto o Worker reserva o estoque | Concorrência otimista: nada é gravado sobre um estado antigo |

## Concorrência com os Workers

| Disputa | Resultado |
|---------|-----------|
| Cancelamento pela API e reserva de estoque ao mesmo tempo | Um dos dois perde e é reavaliado. Se o cancelamento vence, a reserva encontra o pedido final e não reserva. Se a reserva vence, o cancelamento relê o pedido reservado e devolve o estoque |
| Cancelamento e pagamento ao mesmo tempo | O pagamento só cobra pedido apto. Se a cobrança grava antes, o cancelamento relê o pedido pago e deixa o estorno pendente. Se o cancelamento grava enquanto o gateway aprova, o consumidor registra o pagamento já com o estorno pendente (fluxo 5) |
| Cancelamento depois do pagamento aprovado | Permitido para a operação; o estoque volta e o estorno fica pendente para o Worker (fluxo 10) |
| Cancelamento depois de `ProntoParaEnvio` | Permitido; a ordem de entrega é cancelada junto (`canceladaEm`), e o estorno fica pendente |
| Cancelamento e despacho ao mesmo tempo | Um dos dois vence (fluxo 7). Nunca há rastreio num pedido cancelado nem ordem cancelada despachada |
| Dois cancelamentos ao mesmo tempo | Um responde 200 e o outro 409; há um único estorno pendente |
| Cancelamento e esgotamento da entrega na reconciliação | Um dos dois vence; o pedido é cancelado uma vez, com um único estorno pendente (fluxo 9) |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico; nenhuma mudança fica gravada pela metade |
| Broker fora do ar | Não afeta: a mudança de status pela API não publica evento |
| Gateway de pagamento ou Worker fora do ar | Não afeta o cancelamento: o estorno fica pendente até o Worker conseguir estornar (fluxo 10) |

## Observabilidade

Logs 4003 (status alterado, com os status antigo e novo e se foi a operação ou o Cliente),
6005 (estoque devolvido), 7005 (estorno solicitado, origem `Operacao`) e 8004 (ordem de
entrega cancelada).

## Limitações

- Não há evento de mudança de status, de cancelamento nem de estorno para outros sistemas (#29).
- Pedidos cancelados com pagamento aprovado **antes do REQ-13** não são estornados
  automaticamente; a consulta para encontrá-los está no
  [quickstart do REQ-13](../../specs/013-cancelamento-com-estorno/quickstart.md#5-pedidos-pagos-e-cancelados-antes-do-req-13).
