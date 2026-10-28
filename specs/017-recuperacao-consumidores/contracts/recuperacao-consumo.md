# Contrato: recuperação do consumo, prontidão, logs e métricas

**Requisito**: REQ-17 | **Spec**: [spec.md](../spec.md) | **Research**: [research.md](../research.md)

Este requisito não muda os contratos de evento (envelope v1, schemas, cabeçalhos, trocas e
filas) nem as rotas da API. Muda o comportamento do processo de workers diante de uma perda de
inscrição e acrescenta uma entrada de prontidão, quatro logs e duas métricas.

## Comportamento

| Situação | Comportamento |
|----------|---------------|
| Broker para e volta, com o Worker no ar | Cada consumidor perde a inscrição (5016), tenta voltar com intervalos de 0, 1, 2, 4, 8 e 10 s (5017 a cada falha) e volta em até 1 min depois de o broker aceitar conexões (5018) |
| Conexão fechada à força pelo broker | Como acima; a conexão registra 5019 |
| Fila principal apagada com o Worker no ar | O broker cancela a inscrição; o consumidor redeclara a topologia dele e se inscreve de novo. Os outros não são afetados |
| Canal de consumo ou de publicação do consumidor fechado por erro | O consumidor fecha a sessão e abre outra |
| Broker volta sem dados (filas apagadas) | Cada consumidor redeclara fila principal, filas de espera e DLQ antes de se inscrever |
| Broker volta e recusa as credenciais | As tentativas continuam no intervalo de 10 s, com 5017; a prontidão continua não pronta |
| Worker encerrando | Nenhuma reinscrição; o laço sai na hora, mesmo no meio de uma espera |
| Mensagem em processamento quando o canal cai | O ack falha sem relançar; o broker devolve a mensagem; a reentrega é descartada como duplicada (5005) se o efeito já foi gravado |

Garantias: no máximo um consumidor por fila principal em cada processo de workers; entrega
"pelo menos uma vez", como no REQ-06; contagem de tentativas preservada.

## Prontidão do Worker (`/health/pronto`)

Uma entrada nova, `consumidores`, só no Worker. As entradas `sqlserver` e `rabbitmq` continuam.

| Estado | `consumidores` | Status HTTP |
|--------|----------------|-------------|
| Os três consumidores inscritos (e as outras entradas saudáveis) | `Healthy` | 200 |
| Algum consumidor sem inscrição, inclusive antes da primeira inscrição | `Unhealthy` | 503 |

```json
{ "status": "Unhealthy", "dependencias": { "sqlserver": "Healthy", "rabbitmq": "Healthy", "consumidores": "Unhealthy" } }
```

A resposta continua sem descrições, nomes de fila, exceções nem dados de conexão (REQ-02). A
prontidão da API não muda.

## Logs

IDs novos, na faixa da mensageria. Os IDs existentes não mudam; o 5008 continua só para a
partida.

| ID | Nome | Nível | Mensagem |
|----|------|-------|----------|
| 5016 | `InscricaoPerdida` | Warning | `Consumidor {Consumidor} perdeu a inscrição ({Origem}): {Motivo}` |
| 5017 | `FalhaReinscricao` | Warning | `Consumidor {Consumidor} não voltou a se inscrever ({Erro}): {Mensagem}. Nova tentativa em {EsperaMs} ms.` |
| 5018 | `ConsumidorReinscrito` | Information | `Consumidor {Consumidor} voltou a consumir depois de {DuracaoMs} ms sem inscrição.` |
| 5019 | `ConexaoBrokerFechada` | Warning | `Conexão com o broker fechada ({Iniciador} {Codigo}): {Motivo}` |

- `{Origem}`: `canal` (canal de consumo ou de publicação fechado) ou `cancelamento` (inscrição
  cancelada pelo broker).
- `{Motivo}`: o texto de fechamento do broker ou do cliente (por exemplo,
  `CONNECTION_FORCED - broker forced connection closure with reason 'shutdown'`), ou
  `cancelada pelo broker`. Nunca credenciais nem cadeia de conexão.
- 5019 não sai quando o próprio processo fecha a conexão (descarte ou encerramento).

## Métricas (meter `PedidoFulfillment`, acréscimo ao contrato do REQ-16)

| # | Nome | Tipo | Unidade | Atributos | Processo |
|---|------|------|---------|-----------|----------|
| 7 | `oms.mensageria.consumidor.inscrito` | ObservableGauge (long) | `{inscricao}` | `oms.consumidor` | Worker |
| 8 | `oms.mensageria.consumidor.reinscricoes` | Counter (long) | `{reinscricao}` | `oms.consumidor` | Worker |

- 7: `1` com o consumidor inscrito, `0` sem inscrição.
- 8: soma 1 a cada inscrição que dá certo depois de uma perda; a da partida não conta.
- Só com a telemetria ligada (REQ-16); as métricas 1 a 6 não mudam.
