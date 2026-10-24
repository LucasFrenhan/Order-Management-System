# Contrato: limpeza de mensageria e reenvio da DLQ (REQ-14)

**Spec**: [spec.md](../spec.md) | **Research**: [research.md](../research.md)

## HTTP: rotas administrativas (acréscimo ao contrato v1)

Política `PoliticaAdministracao`: token válido e perfil `Admin`. Sem token, 401; Cliente ou
OperadorLogistica, 403. Nenhuma das duas fala com o broker.

### `POST /api/v1/admin/dlq/{consumidor}/reenvios`

`{consumidor}`: `estoque`, `pagamento` ou `entrega`.

```json
{ "tipoEvento": "pedido.criado", "falhouDe": "2026-10-24T00:00:00Z",
  "falhouAte": "2026-10-24T23:59:59Z", "trechoMotivo": "Timeout" }
```

Todos os campos são opcionais; corpo vazio (`{}`) reenvia tudo que o limite permitir.

| Situação | Resposta |
|----------|----------|
| Registrado | **202** com `Location: /api/v1/admin/dlq/reenvios/{id}` e o corpo `PedidoReenvioDlqResponseDTO` em `Pendente` |
| Consumidor desconhecido | 400 `ValidacaoProblemDetails`, campo `consumidor`: "O consumidor deve ser estoque, pagamento ou entrega." |
| `falhouAte` antes de `falhouDe` | 400, campo `falhouAte`: "O fim do período deve ser igual ou posterior ao início." |
| `tipoEvento` acima de 100 ou `trechoMotivo` acima de 200 caracteres | 400, com o campo |
| Sem token / outro perfil | 401 / 403 |

### `GET /api/v1/admin/dlq/reenvios/{id}`

| Situação | Resposta |
|----------|----------|
| Existe | 200 com `PedidoReenvioDlqResponseDTO` |
| Não existe | 404 "Pedido de reenvio não encontrado." |
| `{id}` que não é UUID | 400 |
| Sem token / outro perfil | 401 / 403 |

### `PedidoReenvioDlqResponseDTO`

```yaml
PedidoReenvioDlqResponseDTO:
  type: object
  required: [id, consumidor, situacao, registradoEm, autorId, reenviadas, bloqueadasPeloLimite, foraDoFiltro]
  properties:
    id: { type: string, format: uuid }
    consumidor: { type: string }
    situacao: { type: string, enum: [Pendente, EmExecucao, Concluido, Falhou] }
    autorId: { type: string, format: uuid }
    tipoEvento: { type: ['null', string] }
    falhouDe: { type: ['null', string], format: date-time }
    falhouAte: { type: ['null', string], format: date-time }
    trechoMotivo: { type: ['null', string] }
    registradoEm: { type: string, format: date-time }
    iniciadoEm: { type: ['null', string], format: date-time }
    concluidoEm: { type: ['null', string], format: date-time }
    reenviadas: { type: integer, minimum: 0 }
    bloqueadasPeloLimite: { type: integer, minimum: 0 }
    foraDoFiltro: { type: integer, minimum: 0 }
    motivoFalha: { type: ['null', string] }
```

O `RegistrarReenvioDlqRequestDTO` tem os quatro campos do filtro, todos opcionais e anuláveis.

## Execução do reenvio (Worker)

| Item | Valor |
|------|-------|
| Serviço | `ExecutorReenviosDlq`, a cada `ReenvioDlq:IntervaloSegundos` |
| Exclusão entre instâncias | `sp_getapplock 'oms-reenvio-dlq'` |
| Ordem | primeiro `EmExecucao` (retomada), depois `Pendente` por `RegistradoEm`; um de cada vez |
| Leitura | até `MensagensPorPedido` da cabeça de `oms.C.dlq`, `BasicGet` sem ack |
| Reenvio | publica em `oms.C` com confirmação e `mandatory`; só então ack na DLQ |
| Não selecionadas | `BasicNack(requeue: true)` no fim: voltam à posição original |
| Cabeçalhos | ver research R-06 (`x-reenvios`, `x-reenviado-em`, `x-pedido-reenvio`; sem `x-tentativas`) |
| Limite | `x-reenvios >= MaximoReenvios`: não reenvia, conta como bloqueada |

## Limpeza (Worker)

`LimpezaMensageria`, a cada `Limpeza:IntervaloSegundos`, com `sp_getapplock 'oms-limpeza'`;
alvos, condições e retenções no [research R-01](../research.md#r-01-limpeza-serviço-periódico-com-lock-e-lotes).
Nunca apaga evento pendente, chave com menos de 24 h nem pedido de reenvio em aberto.

## Aviso da DLQ (Worker)

`MonitorDlq`, a cada `ReenvioDlq:AvisoIntervaloSegundos`: log 5011 quando
`oms.C.dlq` passa de `ReenvioDlq:AvisoLimite` mensagens.

## Configuração

Seções `Limpeza` e `ReenvioDlq`, com as chaves, os padrões e as faixas do
[research R-10](../research.md#r-10-configuração). Fora das faixas, o Worker não sobe, e a
mensagem cita a chave (por exemplo, "Limpeza:RetencaoChavesHoras deve estar entre 24 e 8760.").

## Broker

| Variável | Padrão (só desenvolvimento) | Quem usa |
|----------|-----------------------------|----------|
| `OMS_RABBITMQ_USUARIO` / `OMS_RABBITMQ_SENHA` | `oms` / `oms-dev-local` | Worker |
| `OMS_RABBITMQ_API_USUARIO` / `OMS_RABBITMQ_API_SENHA` | `oms-api` / `oms-api-dev-local` | API |

## Logs

4006, 5009 a 5015, com os campos do [research R-11](../research.md#r-11-logs).

## Eventos

Nenhum evento novo; os contratos v1 não mudam.
