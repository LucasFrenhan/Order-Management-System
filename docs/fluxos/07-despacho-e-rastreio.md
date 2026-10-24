# Fluxo 7: despacho e rastreio

**Rota**: `PATCH /api/v1/pedidos/{id}/rastreio`, só para Admin e OperadorLogistica.
**Origem**: REQ-11 ([spec](../../specs/011-rastreio/spec.md),
[contrato](../../specs/011-rastreio/contracts/rastreio-http.md)).

## Caminho feliz

```http
PATCH /api/v1/pedidos/0199f1a2-…/rastreio
Authorization: Bearer <token do operador>
Content-Type: application/json

{ "transportadora": "Correios", "codigoRastreio": "BR123456789BR" }
```

```http
HTTP/1.1 200 OK

{ "status": "Despachado",
  "historico": [ …, { "statusAnterior": "ProntoParaEnvio", "statusNovo": "Despachado", "ocorridoEm": "…", "motivo": "Pedido despachado." } ],
  "entrega": { "transportadora": "Correios", "codigoRastreio": "BR123456789BR", "despachadoEm": "2026-10-24T16:10:00Z", "criadoEm": "…" } }
```

Com o pedido em `ProntoParaEnvio`, numa gravação só:
- a ordem de entrega é criada, se ainda não existir;
- a transportadora, o código e a data de despacho são gravados, sem os espaços das pontas;
- o pedido vai para `Despachado`, com o autor e o motivo "Pedido despachado." no histórico.

## Resultados por status

| Status atual | Resultado | Por quê |
|--------------|-----------|---------|
| `ProntoParaEnvio` | Despacha (200) | — |
| `Despachado`, com os mesmos dados | 200, **nada muda** (nem `atualizadoEm`) | Repetir a mesma chamada é seguro |
| `Despachado`, com dados diferentes | 200, transportadora e código corrigidos; status, data de despacho e histórico mantidos | Corrige um erro de digitação sem refazer o despacho |
| `Pendente`, `Processando`, `Cancelado`, `PagamentoRejeitado` | 409 `PEDIDO_TRANSICAO_INVALIDA`: "O pedido está em Processando e não pode ser despachado." | Só pedido pronto é despachado; pedido cancelado nunca recebe rastreio |

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Transportadora ou código vazio, ou só com espaços | 400, com o campo em `erros` | `"transportadora": "   "` | — |
| Transportadora com mais de 100 caracteres, ou código com mais de 50, contados depois de aparar | 400 | — | Limites do contrato |
| Mesmo código na mesma transportadora em outro pedido, **sem diferenciar maiúsculas** | 409 `PEDIDO_RASTREIO_DUPLICADO`: "Outro pedido já usa este código de rastreio nesta transportadora."; log 4005 | `correios`/`br123` com `Correios`/`BR123` em outro pedido | Um código de rastreio identifica um único volume |
| Mesmo código em transportadoras diferentes | Aceito | `BR123` na Jadlog e nos Correios | A unicidade vale por transportadora |
| Cliente | 403 | — | Despacho é da operação |
| Pedido inexistente | 404 | — | — |
| `{id}` inválido | 400 | — | — |
| Conflitos de concorrência persistentes (3 reavaliações) | 409 `PEDIDO_TRANSICAO_INVALIDA` "O pedido foi alterado por outra operação; tente novamente." | — | — |

## Concorrência

| Disputa | Resultado | Por quê |
|---------|-----------|---------|
| Despacho e cancelamento ao mesmo tempo | Um dos dois vence. Ou o pedido fica `Cancelado` e o despacho responde 409, ou fica `Despachado` e o cancelamento responde 409. Nunca há rastreio num pedido cancelado | Concorrência otimista com releitura |
| Dois despachos ao mesmo tempo com códigos diferentes | Os dois respondem 200: o segundo vira uma correção. O histórico tem uma única entrada de despacho | Na releitura, o segundo encontra o pedido já `Despachado` |
| Despacho e criação da ordem pelo Worker ao mesmo tempo | O índice único de `PedidoId` vira conflito; o despacho relê e grava sobre a ordem do Worker | Uma ordem por pedido |

## Despacho pela rota de status

`PATCH /status` com `ProntoParaEnvio → Despachado` continua aceito, sem rastreio (contrato
v1). A consulta mostra a entrega sem dados de despacho. O rastreio pode ser registrado depois
por esta rota: isso conta como correção, cria a ordem se faltar e grava a data de despacho
nesse momento.

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Banco fora do ar | 500 genérico; nada é gravado |
| Broker fora do ar | Não afeta: o despacho não publica evento |

## Observabilidade

Logs 4004 (rastreio registrado, com o resultado: `Despachado`, `Corrigido` ou `SemMudanca`)
e 4005 (duplicado). Nenhum log traz a transportadora nem o código.

## Limitações

- Não há evento `pedido.despachado` para outros sistemas (#29).
- O rastreio não é validado com a transportadora.
