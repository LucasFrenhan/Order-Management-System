# Contrato: API de pagamento simulada

**Requisito**: REQ-19 | **Serviço**: `PedidoFulfillment.PagamentosSimulados` | **Decisões**: [research](../research.md) R-02 a R-05

Serviço separado do OMS, chamado pelo Worker. Tudo em JSON (`application/json`, nomes em
`camelCase`); erros em `ProblemDetails` (RFC 9457) com a extensão `codigo` quando houver. Não é
uma API do OMS: não está no `openapi-v1.yaml` e não usa o JWT do OMS.

## Autenticação

| Rotas | Cabeçalho | Configuração |
|-------|-----------|--------------|
| `/v1/*` | `X-Api-Key: <chave>` | `Seguranca:ChaveApi` |
| `/controle/*` | `X-Chave-Controle: <chave>` | `Seguranca:ChaveControle` |
| `/health` | nenhum | — |

- As duas chaves são obrigatórias, têm pelo menos 16 caracteres e são diferentes entre si; caso
  contrário, a API não sobe e o log nomeia a chave sem mostrar o valor.
- Chave ausente, errada ou de outro grupo de rotas: `401`, `ProblemDetails` com o título
  `"Credencial inválida."`, igual para todos os casos. A autenticação vem antes do comportamento
  simulado: uma chamada sem credencial nunca recebe `503` ou latência sorteada.
- Comparação em tempo constante; nenhuma chave aparece em log.

## Cobrança

### `POST /v1/cobrancas`

Cabeçalhos: `X-Api-Key`, `Idempotency-Key: <pedidoId>` (obrigatório, GUID).

```json
{ "valor": 149.70 }
```

| Situação | Status | Corpo |
|----------|--------|-------|
| Aprovada (primeira vez) | `201` | `{ "chave": "…", "identificador": "PAG-0199f6b1…", "situacao": "aprovada", "valor": 149.70, "processadaEm": "…" }` |
| Recusada (primeira vez) | `201` | `{ "chave": "…", "identificador": null, "situacao": "recusada", "motivo": "Pagamento recusado pelo emissor.", "valor": 149.70, "processadaEm": "…" }` |
| Repetição da mesma chave, mesmo valor | `200` + `Idempotent-Replayed: true` | o mesmo corpo da primeira resposta |
| Mesma chave, valor diferente | `409` | `codigo: "chave-reutilizada"` |
| Mesma chave ainda em processamento | `409` + `Retry-After: 1` | `codigo: "operacao-em-andamento"` |
| `Idempotency-Key` ausente ou não-GUID, valor ausente, ≤ 0 ou com mais de 2 casas | `400` | `ProblemDetails` com `errors` |
| Comportamento `indisponivel` | `503` | `codigo: "indisponivel"` |
| Comportamento `limite-taxa` | `429` + `Retry-After: <s>` | `codigo: "limite-taxa"` |
| Comportamento `erro-interno` | `500` | `codigo: "erro-interno"` |
| Comportamento `sem-resposta` | nenhuma resposta por `SemRespostaMs`; depois, `504` | nada é processado |
| Comportamento `processar-sem-responder` | a operação é efetivada e guardada; a resposta só sai depois de `SemRespostaMs` | o mesmo corpo de `201` |

O `identificador` tem o prefixo `PAG-` (cobrança) ou `EST-` (estorno), seguido de um GUID v7 sem
hífens. Só as linhas `aprovada`, `recusada` e `processar-sem-responder` efetivam a operação;
nas falhas `503`, `429`, `500` e `sem-resposta` a chave fica livre para a próxima tentativa.

O comportamento é decidido **antes** de consultar a memória, em toda chamada, inclusive na
repetição de uma chave já concluída (uma API real pode estar fora do ar para qualquer
chamada). Uma falha sobre uma chave concluída responde a falha e **não remove nem altera** a
operação guardada; só a operação criada na própria chamada, ainda em andamento, é removida.
`processar-sem-responder` sobre uma chave concluída devolve a resposta guardada depois de
`SemRespostaMs`, sem efetivar de novo.

### `GET /v1/cobrancas/{chave}`

`200` com o corpo da cobrança guardada; `404` com `codigo: "nao-encontrada"` se a chave não
existe (ou a API reiniciou); `409 operacao-em-andamento` se ainda em processamento. A consulta
**não** passa pelo comportamento simulado de falha, só pela latência (uma consulta também pode
ser lenta).

> Decisão: a consulta sofre a latência sorteada, mas não as falhas sorteadas, para o FR-015
> ser testável sem depender de sorte. O roteiro e o modo forçado (abaixo) valem para ela.

## Estorno

### `POST /v1/estornos`

Cabeçalhos: `X-Api-Key`, `Idempotency-Key: <pedidoId>`.

```json
{ "identificadorCobranca": "PAG-0199f6b1…", "valor": 149.70 }
```

| Situação | Status | Corpo |
|----------|--------|-------|
| Confirmado | `201` | `{ "chave": "…", "identificador": "EST-…", "situacao": "confirmado", "valor": 149.70, "processadoEm": "…" }` |
| Cobrança desconhecida (identificador que a API não tem, por exemplo depois de reiniciar) | `201` | `situacao: "recusado"`, `motivo: "Cobrança desconhecida."` |
| Valor diferente do cobrado | `201` | `situacao: "recusado"`, `motivo: "Valor diferente do cobrado."` |
| Repetição, mesma chave e mesmo corpo | `200` + `Idempotent-Replayed: true` | o mesmo corpo |
| Demais linhas | como na cobrança | — |

O estorno sorteado só tem falhas técnicas: a recusa vem só das duas regras acima. A chave do
estorno é o pedido, num espaço separado do da cobrança.

### `GET /v1/estornos/{chave}`

Como a consulta da cobrança.

## Controle (FR-007a)

Todas exigem `X-Chave-Controle`.

| Rota | Efeito |
|------|--------|
| `GET /controle/comportamento` | Devolve as chances e a latência em vigor, o modo forçado e o tamanho do roteiro |
| `PUT /controle/comportamento` | Troca as chances e a latência (corpo com os mesmos campos da configuração, validados como na partida; `400` se inválidos) |
| `PUT /controle/modo` | Força um modo para todas as chamadas: `{ "modo": "indisponivel" }` (um dos comportamentos) |
| `DELETE /controle/modo` | Desfaz o modo forçado |
| `POST /controle/roteiro` | Acrescenta comportamentos à fila, um por chamada: `{ "comportamentos": ["indisponivel", "aprovar"] }` |
| `DELETE /controle/roteiro` | Esvazia o roteiro |
| `POST /controle/restaurar` | Volta tudo à configuração (chances, sem modo, sem roteiro) e apaga as operações guardadas |
| `GET /controle/operacoes/{tipo}/{chave}` | `{ "situacao": "…", "efetivacoes": 1, "chamadas": 3 }` ou `404`; `tipo` = `cobranca` ou `estorno` |

O estado do controle fica em memória e volta à configuração quando a API reinicia.

## Configuração

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Seguranca:ChaveApi`, `Seguranca:ChaveControle` | — (obrigatórias) | ≥ 16 caracteres, diferentes |
| `Simulacao:Deterministico` | `false` | — |
| `Simulacao:Semente` | sem semente | inteiro |
| `Simulacao:LatenciaMinimaMs` / `LatenciaMaximaMs` | 40 / 250 | 0–60000, mínimo ≤ máximo |
| `Simulacao:CaudaPercentual` | 5 | 0–100 |
| `Simulacao:CaudaMinimoMs` / `CaudaMaximoMs` | 1000 / 5000 | 0–60000, mínimo ≤ máximo |
| `Simulacao:TaxaAprovacaoPercentual` | 90 | 0–100 |
| `Simulacao:IndisponivelPercentual` | 2 | 0–100 |
| `Simulacao:LimiteTaxaPercentual` | 1 | 0–100 |
| `Simulacao:RetryAfterSegundos` | 1 | 1–60 |
| `Simulacao:ErroInternoPercentual` | 1 | 0–100 |
| `Simulacao:SemRespostaPercentual` | 1 | 0–100 |
| `Simulacao:ProcessarSemResponderPercentual` | 1 | 0–100 |
| `Simulacao:SemRespostaMs` | 15000 | 100–120000 |

Com `Simulacao:Deterministico` = `true`, a partida zera as chances de falha e a latência (só
aprova), mas `PUT /controle/comportamento` continua podendo definir uma latência, que vale até
`POST /controle/restaurar` (usado nos testes de latência alta, FR-024).

A soma das chances de falha não pode passar de 100. A taxa de aprovação vale sobre as chamadas
que efetivam (a recusa é o complemento).

## Logs da API simulada

Console, com o `traceparent` recebido no escopo: 11001 `OperacaoProcessada` (tipo, chave,
situação, comportamento, latência), 11002 `FalhaSimulada` (tipo, chave, comportamento), 11003
`RepeticaoIdempotente`, 11004 `ChaveReutilizada`, 11005 `ComportamentoAlterado` (o que mudou,
sem chaves), 11006 `CredencialRecusada` (rota, sem o valor). Não exporta telemetria (clarify).
