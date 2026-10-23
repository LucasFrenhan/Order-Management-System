# Contrato: publicação garantida e reconciliação (REQ-12)

Complementa [topologia e consumo](../../006-mensageria/contracts/topologia-e-consumo.md) do
REQ-06 e os contratos dos consumidores de
[estoque](../../008-estoque/contracts/consumidor-estoque.md),
[pagamento](../../009-pagamento/contracts/consumidor-pagamento.md) e
[entrega](../../010-fulfillment/contracts/consumidor-entrega.md). Nenhum contrato de evento
nem o contrato HTTP v1 mudam.

## Topologia declarada pelo despachante

A cada canal de publicação aberto, antes da primeira publicação nele:

| Consumidor | Fila | Ligação em `oms.eventos` | Espera | DLQ |
|---|---|---|---|---|
| `estoque` | `oms.estoque` | `pedido.criado` | `oms.estoque.espera.{intervalos}` | `oms.estoque.dlq` |
| `pagamento` | `oms.pagamento` | `estoque.reservado` | `oms.pagamento.espera.{intervalos}` | `oms.pagamento.dlq` |
| `entrega` | `oms.entrega` | `pagamento.aprovado` | `oms.entrega.espera.{intervalos}` | `oms.entrega.dlq` |

Os nomes, as ligações e os argumentos são os mesmos que cada consumidor declara: mesma
função e mesmos `Mensageria:IntervalosNovaTentativaMs`. Um consumidor novo entra nesta
tabela só por ser registrado no Worker.

## Publicação por tipo

| Tipo | Exige rota | Sem fila ligada |
|---|---|---|
| `pedido.criado`, `estoque.reservado`, `pagamento.aprovado` | sim (`mandatory`) | falha de publicação: `UltimoErro = "SemRota: ..."`, próxima tentativa com espera crescente, lote encerrado, canal recriado (e topologia declarada de novo) |
| `estoque.insuficiente`, `pagamento.recusado`, `entrega.criada` | não | marcado como enviado após a confirmação do broker, como hoje |

## Reconciliação

| Item | Valor padrão (configurável em `Reconciliacao`) |
|---|---|
| Primeiro ciclo | na partida do Worker |
| Intervalo entre ciclos | 300 s |
| Parado | `AtualizadoEm` mais antigo que 600 s |
| Lote | 100 pedidos; primeiro os sem falha anterior, depois os com falha, cada grupo do mais antigo para o mais novo |
| Esgotamento | `AtualizadoEm` mais antigo que 86 400 s **e** pelo menos 3 ciclos com falha (em memória) |
| Instâncias | uma por ciclo (`sp_getapplock 'oms-reconciliacao'`); as demais pulam o ciclo |

### Resultado por pedido

| Etapa | Regra ok | Pedido não mais apto | Falha | Esgotado |
|---|---|---|---|---|
| Estoque | o mesmo do consumidor de estoque: reserva e `estoque.reservado`, ou `Cancelado` ("Estoque insuficiente.") e `estoque.insuficiente` | nada muda | log 9003; tenta no próximo ciclo | `Cancelado`, sem autor, "Não foi possível reservar o estoque.", sem evento; log 9002 |
| Pagamento | o mesmo do consumidor de pagamento: aprovado e `pagamento.aprovado`, ou recusado, estoque devolvido, `PagamentoRejeitado` e `pagamento.recusado` | nada muda | log 9003; tenta no próximo ciclo | pagamento `NaoProcessado`, estoque devolvido, `Cancelado`, sem autor, "Não foi possível processar o pagamento.", sem evento; log 9002 |
| Entrega | o mesmo do consumidor de entrega: ordem, `ProntoParaEnvio` e `entrega.criada` | nada muda | log 9003; tenta no próximo ciclo | **não se esgota** |

Tudo o que muda num pedido é gravado numa única transação, com o evento de saída no
outbox. A reconciliação não grava em `EventosProcessados` e não republica eventos antigos.

### Logs

Ver o research R-11 (ids 9001 a 9007). Os campos são só identificadores, etapas e contagens.
