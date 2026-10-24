# Fluxo 3: consulta e mudança de status

**Rotas**: `GET /api/v1/pedidos/{id}` e `PATCH /api/v1/pedidos/{id}/status`, para todos os perfis.
**Origem**: REQ-01 ([ciclo de vida](../../specs/001-contratos-api/data-model.md#ciclo-de-vida-do-pedido-fr-020)),
REQ-05 ([spec](../../specs/005-criacao-pedidos/spec.md)), REQ-08 (devolução do estoque) e
REQ-11 (histórico e entrega na resposta).

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
  "entrega": { "transportadora": null, "codigoRastreio": null, "despachadoEm": null, "criadoEm": "…" } }
```

- O histórico vem em ordem cronológica, com o motivo de cada mudança e **sem o autor**, para
  todos os perfis.
- `entrega` é `null` enquanto não há ordem de entrega.

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

**Cancelamento com estoque reservado**: o estoque volta aos produtos na mesma gravação do
cancelamento (REQ-08), com o log 6005.

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Status ausente ou desconhecido | 400 "O status é obrigatório." | `"status": "Enviado"` | — |
| `PagamentoRejeitado` pedido pela API | 400 "O status PagamentoRejeitado não pode ser definido por esta operação." | — | Só o fluxo de pagamento define esse status |
| `Cancelado` sem motivo | 400 "O motivo é obrigatório quando o status é Cancelado." | `{ "status": "Cancelado" }` | O cancelamento precisa de justificativa no histórico |
| Motivo com mais de 500 caracteres | 400 "O motivo deve ter até 500 caracteres." | — | — |
| Transição fora da tabela, para a operação | 409 `PEDIDO_TRANSICAO_INVALIDA`: "O pedido está em Despachado e não pode passar para Cancelado." | Cancelar um pedido despachado; pular de `Pendente` para `Despachado`; repetir o status atual | Estados finais não voltam, e a ordem do fluxo é respeitada |
| Cliente tentando qualquer coisa além de `Pendente → Cancelado` no próprio pedido | 403 "O cliente só pode cancelar o próprio pedido enquanto ele está Pendente." | Cliente cancelando um pedido já `Processando` | Depois do pagamento, cancelar exige a operação, que responde pelo estorno (#28) |
| Cliente alterando pedido de outro | 404 | — | Não revela que o pedido existe |
| Pedido alterado por outra operação durante a mudança | A mudança é reavaliada sobre o estado atual, até 3 vezes; esgotadas, 409 `PEDIDO_TRANSICAO_INVALIDA` "O pedido foi alterado por outra operação; tente novamente." | Operação cancelando enquanto o Worker reserva o estoque | Concorrência otimista: nada é gravado sobre um estado antigo |

## Concorrência com os Workers

| Disputa | Resultado |
|---------|-----------|
| Cancelamento pela API e reserva de estoque ao mesmo tempo | Um dos dois perde e é reavaliado. Se o cancelamento vence, a reserva encontra o pedido final e não reserva. Se a reserva vence, o cancelamento relê o pedido reservado e devolve o estoque |
| Cancelamento e pagamento ao mesmo tempo | O mesmo: o pagamento só cobra pedido apto, e o cancelamento devolve o estoque que houver |
| Cancelamento depois do pagamento aprovado | Permitido para a operação; o estoque volta, mas o pagamento continua `Aprovado`, **sem estorno** (#28) |
| Cancelamento depois de `ProntoParaEnvio` | Permitido; a ordem de entrega continua registrada, sem despacho (#28) |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico; nenhuma mudança fica gravada pela metade |
| Broker fora do ar | Não afeta: a mudança de status pela API não publica evento |

## Observabilidade

Log 4003 (status alterado, com os status antigo e novo e se foi a operação ou o Cliente).

## Limitações

- Não há evento de mudança de status nem de cancelamento para outros sistemas (#29).
- O cancelamento depois do pagamento não estorna (#28).
