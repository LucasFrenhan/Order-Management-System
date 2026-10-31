# Mensageria

[← Voltar ao README](../README.md)

Os eventos de domínio trafegam pelo RabbitMQ ([spec](../specs/006-mensageria/spec.md),
[topologia e consumo](../specs/006-mensageria/contracts/topologia-e-consumo.md),
[schemas](../specs/006-mensageria/contracts/eventos/)):

- **Publicação por outbox**: a API grava o evento em `EventosSaida` na mesma operação do
  pedido e não fala com o broker. O processo `PedidoFulfillment.Worker` publica os pendentes
  em ordem, com confirmação do broker, na troca `oms.eventos` (`topic`), com o tipo como
  chave (`pedido.criado`). Com o broker fora, os pedidos continuam sendo criados, e os
  eventos saem quando ele volta. A entrega é **pelo menos uma vez**.
- **Consumo**: por consumidor `C`, a fila `oms.C`, as filas de espera
  `oms.C.espera.{1000,2000,4000}` e a DLQ `oms.C.dlq`. Uma falha é tentada de novo até 3
  vezes, com espera crescente e sem bloquear a fila, e depois vai para a DLQ com o motivo
  nos cabeçalhos. Mensagem fora do contrato vai direto para a DLQ. Duplicados são
  descartados por consumidor.
- **Estoque** (REQ-08, [consumidor de estoque](../specs/008-estoque/contracts/consumidor-estoque.md)):
  o consumidor `estoque` (fila `oms.estoque`) recebe `pedido.criado` e reserva o estoque
  de todos os itens de uma vez, publicando `estoque.reservado`; se algum item falta, nada é
  abatido, o pedido é cancelado e sai `estoque.insuficiente`
  ([schema](../specs/008-estoque/contracts/eventos/)). Conflitos de concorrência no mesmo
  produto são reavaliados com o estoque atual. Cancelar pela API um pedido com estoque
  reservado devolve o estoque na mesma gravação.
- **Pagamento** (REQ-09, [consumidor de pagamento](../specs/009-pagamento/contracts/consumidor-pagamento.md)):
  o consumidor `pagamento` (fila `oms.pagamento`) recebe `estoque.reservado` e cobra o valor
  total num gateway simulado, que aprova com a chance de
  `GatewayPagamento:TaxaAprovacaoPercentual` (padrão 90, de 0 a 100; fora disso o Worker não
  sobe). Aprovado, o pedido vai de `Pendente` para `Processando` e sai `pagamento.aprovado`.
  Recusado, o pedido vai para `PagamentoRejeitado` (final), o estoque volta na mesma
  gravação e sai `pagamento.recusado`. Pedido cancelado, em status final ou já pago é
  ignorado.
- **Entrega** (REQ-10, [consumidor de entrega](../specs/010-fulfillment/contracts/consumidor-entrega.md)):
  o consumidor `entrega` (fila `oms.entrega`) recebe `pagamento.aprovado`, cria a ordem de
  entrega do pedido (sem transportadora, rastreio nem data de despacho), muda o pedido de
  `Processando` para `ProntoParaEnvio` e publica `entrega.criada`
  ([schema](../specs/010-fulfillment/contracts/eventos/)), tudo na mesma gravação. Pedido
  cancelado, despachado, sem pagamento aprovado ou já com ordem é ignorado. Pedidos pagos
  antes de `oms.entrega` existir ganham a ordem pela reconciliação do REQ-12.
- **Publicação garantida e reconciliação** (REQ-12, [contrato](../specs/012-publicacao-garantida/contracts/publicacao-e-reconciliacao.md)):
  as filas dos consumidores existem antes da primeira publicação, mesmo num broker novo, e
  um evento com consumidor nunca é dado como enviado sem chegar a uma fila. Os pedidos
  parados por evento perdido voltam ao fluxo pela reconciliação, configurada na seção
  `Reconciliacao` do Worker:

  | Chave | Padrão | Faixa |
  |---|---|---|
  | `Habilitada` | `true` | — |
  | `IntervaloCicloSegundos` | 300 | 1 a 86 400 |
  | `LimiteParadoSegundos` | 600 | 1 a 604 800 |
  | `LimiteEsgotamentoSegundos` | 86 400 | maior que `LimiteParadoSegundos`, até 2 592 000 |
  | `CiclosFalhaEsgotamento` | 3 | 1 a 100 |
  | `TamanhoLote` | 100 | 1 a 1 000 |

  Fora das faixas, o Worker não sobe. Os logs 9001 a 9007 mostram cada pedido recuperado
  ou esgotado e o resumo de cada ciclo.
- **Estorno** (REQ-13, [contrato](../specs/013-cancelamento-com-estorno/contracts/estorno-e-cancelamento.md),
  [fluxo](fluxos/10-estorno.md)): o `ProcessadorEstornos` do Worker estorna no gateway os
  pedidos pagos que foram cancelados, configurado na seção `Estornos`:

  | Chave | Padrão | Faixa |
  |---|---|---|
  | `Habilitado` | `true` | — |
  | `IntervaloSegundos` | 60 | 1 a 3 600 |
  | `TamanhoLote` | 100 | 1 a 1 000 |
  | `LimiteAlertaSegundos` | 86 400 | 1 a 2 592 000 |
  | `TentativasAlerta` | 3 | 1 a 100 |

  Fora das faixas, o Worker não sobe. Os logs 7005 a 7012 mostram cada estorno pedido,
  confirmado ou com falha e o resumo de cada rodada.
- **Limpeza e reenvio da DLQ** (REQ-14, [contrato](../specs/014-limpeza-e-dlq/contracts/limpeza-e-reenvio.md),
  [fluxo](fluxos/11-limpeza-e-reenvio-da-dlq.md)): seções `Limpeza` (a cada 1 h, em lotes de
  1 000; enviados e processados por 7 dias, chaves por 48 h, pedidos de reenvio por 90 dias)
  e `ReenvioDlq` (pedidos executados a cada 1 min, até 3 reenvios e 1 000 mensagens por
  pedido; aviso acima de 10 mensagens na DLQ, a cada 5 min). As faixas estão no fluxo 11; fora
  delas, o Worker não sobe. Os logs 5009 a 5015 mostram cada limpeza, reenvio e aviso.
- **Cancelamentos e estornos para outros sistemas** (REQ-15, [contrato e schemas](../specs/015-visibilidade-cancelamentos/contracts/eventos-cancelamento-e-estorno.md),
  [fluxo](fluxos/08-publicacao-e-consumo-de-eventos.md#sistemas-de-fora-cancelamentos-e-estornos-req-15)):
  todo cancelamento grava `pedido.cancelado`, com o motivo (`Cliente`, `Operacao`,
  `EstoqueInsuficiente` ou `ReconciliacaoEsgotada`, com a etapa) e o que foi desfeito junto, e
  toda confirmação de estorno grava `pagamento.estornado`. Nenhum consumidor interno os recebe:
  um sistema de fora liga a própria fila a `oms.eventos` com essas chaves.

Para rodar localmente, com o [ambiente local](../README.md#ambiente-local) no ar e a API já iniciada
(é ela que aplica as migrations):

```powershell
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # http://localhost:5231/health/pronto
```

No [painel do RabbitMQ](http://localhost:15672), a troca `oms.eventos` e as filas aparecem
assim que o Worker sobe. A DLQ é reenviada pelo pedido de um Admin
(`POST /api/v1/admin/dlq/{consumidor}/reenvios`, executado pelo Worker) e só a purga continua
pelo painel ([fluxo 11](fluxos/11-limpeza-e-reenvio-da-dlq.md)).
