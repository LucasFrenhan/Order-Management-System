# Fluxo 3: consulta, listagem e mudança de status

**Rotas**: `GET /api/v1/pedidos/{id}`, `GET /api/v1/pedidos` e `PATCH /api/v1/pedidos/{id}/status`, para todos os perfis.
**Origem**: REQ-01 ([ciclo de vida](../../specs/001-contratos-api/data-model.md#ciclo-de-vida-do-pedido-fr-020)),
REQ-05 ([spec](../../specs/005-criacao-pedidos/spec.md)), REQ-08 (devolução do estoque),
REQ-11 (histórico e entrega na resposta), REQ-13 ([spec](../../specs/013-cancelamento-com-estorno/spec.md),
estorno e cancelamento da ordem), REQ-15 ([spec](../../specs/015-visibilidade-cancelamentos/spec.md), evento `pedido.cancelado`)
REQ-18 ([spec](../../specs/018-notificacao-status/spec.md), `pedido.status-alterado`, avisos e `ETag`)
e REQ-21 ([spec](../../specs/021-listagem-pedidos/spec.md), listagem com cursor).

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
- O `ETag` muda a cada alteração que aparece na resposta (status, ordem de entrega, rastreio,
  inclusive a correção, e estorno); `If-None-Match` diferente ou malformado devolve 200
  (REQ-18, research R-17).

| Situação | Resposta | Por quê |
|----------|----------|---------|
| Cliente dono, Admin ou OperadorLogistica | 200, com `ETag` forte da versão do pedido | — |
| `If-None-Match` igual ao `ETag` atual | 304, sem corpo, com o mesmo `ETag` | Quem continua consultando (ou confirma um aviso) não baixa o pedido de novo (REQ-18, FR-025) |
| Cliente consultando pedido de outro | 404, o mesmo corpo de um pedido inexistente, com ou sem `If-None-Match` | Não revela que o pedido existe (FR-015); a autorização vem antes do `ETag` |
| Pedido inexistente | 404 "Pedido não encontrado." | — |
| `{id}` que não é UUID | 400 | — |
| Sem token | 401 | — |

## Listagem

Lista os pedidos do escopo do usuário, do criado mais recentemente para o mais antigo, com um
resumo de cada um (REQ-21). O detalhe (itens, histórico e entrega) continua na consulta por id.

```http
GET /api/v1/pedidos?status=ProntoParaEnvio&atualizadoAte=2026-11-02T23:59:59-03:00&tamanho=2
Authorization: Bearer <token da operação>
```

```http
HTTP/1.1 200 OK

{ "itens": [
    { "id": "…", "numero": "PED-20261027-000014", "clienteId": "…", "status": "ProntoParaEnvio",
      "valorTotal": 149.70, "moeda": "BRL", "quantidadeItens": 2,
      "criadoEm": "2026-10-27T13:10:00+00:00", "atualizadoEm": "2026-11-02T12:00:05+00:00" },
    { … } ],
  "tamanho": 2,
  "proximo": "eyJ2IjoxLCJzIjoicCIs….Qm9h…",
  "anterior": null }
```

- **Escopo**: o Cliente vê só os próprios pedidos, qualquer que seja a combinação de
  parâmetros; Admin e OperadorLogistica veem todos. Quem tem Cliente e um perfil da operação
  tem o escopo da operação, como na consulta por id.
- **Filtros**, combináveis: `status` (um ou mais, repetindo o parâmetro, sem diferenciar
  maiúsculas), `criadoDe` e `criadoAte`, `atualizadoDe` e `atualizadoAte` (inclusivos, com fuso)
  e `clienteId` (só a operação). O período de última alteração serve para achar pedidos parados
  num status, como no exemplo.
- **Resumo**: `quantidadeItens` conta as linhas de item (produtos distintos), não a soma das
  quantidades. Os demais campos têm os mesmos valores da consulta por id.
- **Navegação por cursor**: `tamanho` de 1 a 100 (padrão 20). A resposta traz `proximo` e
  `anterior`, ou `null` no fim e no começo da lista. Para navegar, repita a consulta com os
  mesmos filtros e `cursor` igual a um deles. Não há total nem número de página (decisão da
  clarify do REQ-21). A ordem desempata pelo número do pedido, e a navegação não repete nem pula
  pedidos quando entram pedidos novos no meio: o novo aparece ao recomeçar da primeira página.
- **O cursor** é opaco, assinado (HMAC) e vale só para o mesmo usuário e os mesmos filtros; o
  tamanho pode mudar no meio. Um pedido que sai do filtro durante a navegação não quebra o
  cursor. Se os pedidos à frente saírem todos do filtro, a página vem vazia, sem `proximo` e com
  um `anterior` que volta para a página de antes.
- Só leitura: a listagem não grava nada nem publica eventos.

| Situação | Resposta | Por quê |
|----------|----------|---------|
| Cliente, Admin ou OperadorLogistica | 200 com a página; lista vazia quando nada atende | — |
| Sem token ou token inválido | 401 | — |
| Autenticado sem nenhum dos três perfis | 403, antes de validar os parâmetros | Mesma política da consulta por id |
| Parâmetro inválido | 400 com todos os erros em `erros`: "O tamanho deve ser um número entre 1 e 100.", "O status 'Enviado' não existe.", "O parâmetro criadoDe deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00.", "O início do período de criação deve ser anterior ou igual ao fim." (e o equivalente da última alteração), "O parâmetro clienteId deve ser um UUID." | Datas sem fuso seriam ambíguas; o 400 diz o que corrigir |
| Cliente com `clienteId` bem formado, mesmo o próprio | 403 "O filtro por cliente é exclusivo da operação." | O uso indevido fica visível, em vez de ser ignorado (REQ-21, clarify) |
| Cursor malformado, adulterado, de outro usuário ou com outros filtros | 400 com `erros.cursor`: "O cursor é inválido para esta consulta; recomece da primeira página." | O cursor não revela nada, e o escopo do Cliente não depende dele |

A ordem das verificações é: token (401), perfil (403), parâmetros (400), filtro por Cliente
do Cliente (403) e cursor (400). Por isso um `clienteId` malformado dá 400 também para o
Cliente, e o cursor só aparece no 400 quando os demais parâmetros estão válidos.

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
| Qualquer situação | Grava no outbox um `pedido.cancelado`, com o motivo `Operacao` (Admin ou OperadorLogistica) ou `Cliente` e o que foi desfeito junto (REQ-15) | — |
| Qualquer situação | Grava também o `pedido.status-alterado` e o aviso de notificação, como toda mudança de status (REQ-18, fluxo 13) | — |

Exemplo: cancelar um pedido em `ProntoParaEnvio` responde 200 com
`"entrega": { …, "despachadoEm": null, "canceladaEm": "2026-10-24T15:20:00+00:00" }`; o estorno
é confirmado em até 1 minuto pelo Worker. O mesmo cancelamento grava:

```json
{ "tipo": "pedido.cancelado", "versao": 1, "idCorrelacao": "0199f1a2-…",
  "dados": { "pedidoId": "0199f1a2-…", "numero": "PED-20261024-000001", "motivo": "Operacao", "etapaEsgotada": null,
             "canceladoEm": "2026-10-24T15:20:00+00:00", "estoqueDevolvido": true, "estornoPendente": true,
             "ordemEntregaCancelada": true } }
```

O motivo digitado fica só no histórico; o evento leva a categoria, sem o texto, sem o autor e
sem dados pessoais. O contrato completo está no
[REQ-15](../../specs/015-visibilidade-cancelamentos/contracts/eventos-cancelamento-e-estorno.md),
e a publicação, no [fluxo 8](08-publicacao-e-consumo-de-eventos.md).

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
| Dois cancelamentos ao mesmo tempo | Um responde 200 e o outro 409; há um único estorno pendente e um único `pedido.cancelado` |
| Cancelamento e esgotamento da entrega na reconciliação | Um dos dois vence; o pedido é cancelado uma vez, com um único estorno pendente (fluxo 9) |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico; nenhuma mudança fica gravada pela metade, nem o `pedido.cancelado` |
| Broker fora do ar | Não afeta a resposta: o `pedido.cancelado` e o `pedido.status-alterado` ficam pendentes no outbox e saem quando o broker volta (fluxo 8). Os webhooks e o SSE não dependem do broker (fluxo 13) |
| Gateway de pagamento ou Worker fora do ar | Não afeta o cancelamento: o estorno fica pendente até o Worker conseguir estornar (fluxo 10) |
| Broker ou Worker fora do ar, na listagem | Não afeta: a listagem só lê o banco; com o banco fora do ar, 500 genérico |

## Observabilidade

Logs 4003 (status alterado, com os status antigo e novo e se foi a operação ou o Cliente),
6005 (estoque devolvido), 7005 (estorno solicitado, origem `Operacao`) e 8004 (ordem de
entrega cancelada). A listagem registra o 4006 (`PedidosListados`, nível Debug), com o escopo
(`Cliente` ou `Operacao`), os nomes dos filtros usados, sem os valores, o ponto de partida
(`Inicio`, `Proxima` ou `Anterior`) e a quantidade de pedidos devolvida.

## Limitações

- Toda mudança de status passou a publicar `pedido.status-alterado` e a avisar por webhook e
  SSE (#43, REQ-18, fluxo 13); o cancelamento e o estorno também publicam `pedido.cancelado` e
  `pagamento.estornado` (#29, REQ-15).
- Pedidos cancelados com pagamento aprovado **antes do REQ-13** não são estornados
  automaticamente; a consulta para encontrá-los está no
  [quickstart do REQ-13](../../specs/013-cancelamento-com-estorno/quickstart.md#5-pedidos-pagos-e-cancelados-antes-do-req-13).
- A listagem de pedidos, que o contrato v1 tinha deixado de fora, foi entregue pela #52
  (REQ-21).
- A listagem não tem total nem salto para uma página qualquer: a navegação é só pela próxima e
  pela anterior (decisão da clarify do REQ-21). Também não tem `ETag`, busca textual nem
  exportação.
- A chave que assina o cursor da listagem é derivada hoje da chave de assinatura do JWT
  (`Jwt:ChaveAssinatura`, por HKDF). Trocar essa chave, por exemplo na rotação da #60, invalida
  os cursores em uso: a resposta é 400 e o cliente recomeça da primeira página. A chave pode
  passar no futuro a uma chave própria do cursor, mudando só o registro de
  `IChaveCursorPedidos` na API ([research R-04](../../specs/021-listagem-pedidos/research.md#r-04--chave-do-hmac-do-cursor)).
