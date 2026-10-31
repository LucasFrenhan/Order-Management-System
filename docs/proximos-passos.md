# Próximos passos

[← Voltar ao README](../README.md)

Com o REQ-11, as issues de negócio do roteiro original (#1 a #11) estão entregues: o pedido
vai da criação ao despacho com rastreio. O REQ-12 fechou a perda silenciosa de eventos, e o
REQ-13 garante que o Cliente nunca fica cobrado por um pedido cancelado. O REQ-14 limpou os
dados de mensageria que cresciam sem limite e trocou o painel do broker por um reenvio da DLQ
controlado, e o REQ-15 tornou os cancelamentos e os estornos visíveis para outros sistemas. O
REQ-16 trouxe a rastreabilidade de ponta a ponta do pedido, com métricas da mensageria, e o
REQ-17 fez os consumidores voltarem sozinhos depois de o broker reiniciar (issue #41).
Cada trabalho novo segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` →
`/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma
branch `feature/req-XX-...`, com a numeração a partir do REQ-18.

| Ordem | Requisito | Origem | Escopo |
|-------|-----------|--------|--------|
| 1 | a numerar | [#18](https://github.com/LucasFrenhan/Order-Management-System/issues/18) — SonarQube Cloud | Análise estática de código no CI |
| 2 | a numerar | [#43](https://github.com/LucasFrenhan/Order-Management-System/issues/43) — notificação de mudança de status | Estudo de webhooks ou push (custo de cada abordagem) e, depois, a implementação |


## O que já está pronto para os próximos REQs

- **Armazenamento**: produtos, pedidos com itens e histórico de status, pagamentos e ordens
  de entrega, com as restrições no banco.
- **Concorrência otimista** em produto e pedido, com `ConflitoConcorrenciaException` para
  os serviços e workers tratarem sem depender do EF Core
  ([contrato de persistência](../specs/004-dominio-persistencia/contracts/persistencia.md)).
- **Catálogo inicial** com casos reservados para testes: `CAT-008` (estoque 1, disputa do
  último item), `CAT-009` (sem estoque) e `CAT-010` (inativo).
- **Serviço de pedidos** (REQ-05): criação idempotente, consulta e a tabela de transições
  de status no domínio (`TransicoesStatusPedido`), que os workers podem reaproveitar.
- **Mensageria** (REQ-06): cada worker implementa `IManipuladorEvento<T>` no Application e
  se registra no Worker com `AdicionarConsumidor`; fila, novas tentativas, DLQ e
  duplicados ficam com a base. Para publicar, grava um `EventoSaida` (outbox). Guia em
  [topologia e consumo](../specs/006-mensageria/contracts/topologia-e-consumo.md).
- **Estoque** (REQ-08): `Pedido.ReservarEstoque` e `Pedido.DevolverEstoque`, com a marca
  `EstoqueReservado`; o consumidor `ReservaEstoque` é o exemplo de worker de negócio
  ([consumidor de estoque](../specs/008-estoque/contracts/consumidor-estoque.md)).
- **Pagamento** (REQ-09): `Pedido.AprovarPagamento` e `Pedido.RecusarPagamento`, com a
  condição `AptoParaPagamento`; a porta `IGatewayPagamento` e o gateway simulado
  ([consumidor de pagamento](../specs/009-pagamento/contracts/consumidor-pagamento.md)).
- **Entrega** (REQ-10): `Pedido.CriarOrdemEntrega`, com a condição `AptoParaOrdemEntrega`;
  a `OrdemEntrega` passa a ser lida com o pedido
  ([consumidor de entrega](../specs/010-fulfillment/contracts/consumidor-entrega.md)).
- **Rastreio** (REQ-11): `Pedido.RegistrarRastreio` (despacho, correção ou sem mudança) e
  `OrdemEntrega.CorrigirRastreio`; a política `PoliticaRastreioPedido`; a resposta do pedido
  com `historico` e `entrega` ([contrato do rastreio](../specs/011-rastreio/contracts/rastreio-http.md)).
- **Reconciliação** (REQ-12): os consumidores do fluxo implementam `IEtapaFluxoPedido`, a
  regra aplicável sem a mensagem; um consumidor novo entra sozinho na topologia declarada
  pelo despachante ([publicação e reconciliação](../specs/012-publicacao-garantida/contracts/publicacao-e-reconciliacao.md)).
- **Cancelamento e estorno** (REQ-13): `Pedido.Cancelar` é o único caminho de cancelamento
  (estoque, estorno pendente e ordem juntos); `IGatewayPagamento.EstornarAsync` e o
  `ServicoEstornos` ([estorno e cancelamento](../specs/013-cancelamento-com-estorno/contracts/estorno-e-cancelamento.md)).
- **Administração da mensageria** (REQ-14): a política `PoliticaAdministracao` (só Admin), o
  `PedidoReenvioDlq` e a lista `ConsumidoresConhecidos`, que um consumidor novo precisa ganhar
  ([limpeza e reenvio](../specs/014-limpeza-e-dlq/contracts/limpeza-e-reenvio.md)).
- **Eventos de cancelamento e estorno** (REQ-15): `EventosPedido.Cancelado` monta o
  `pedido.cancelado` de qualquer caminho, a partir do `ResultadoCancelamento`; um caminho novo
  de cancelamento precisa adicioná-lo, ou a guarda do `ArmazenamentoPedidos` recusa a gravação
  ([contrato](../specs/015-visibilidade-cancelamentos/contracts/eventos-cancelamento-e-estorno.md)).
- **Recuperação do consumo** (REQ-17): cada consumidor registrado com `AdicionarConsumidor`
  ganha um `SupervisorInscricao`, que refaz a inscrição depois de qualquer perda, e entra na
  prontidão `consumidores` do Worker; a conexão não usa a recuperação automática do cliente
  ([contrato](../specs/017-recuperacao-consumidores/contracts/recuperacao-consumo.md)).

## Decisões e pontos já conhecidos

- **Mensageria com RabbitMQ.Client nativo.** As issues #7 a #10 citam MassTransit, mas a
  constituição proíbe frameworks de abstração de mensageria. Retry, DLQ e consumidores são
  implementados sobre o cliente nativo.
- **Publicação garantida** (REQ-12). A cada canal novo, o despachante declara as filas de
  todos os consumidores conhecidos, com a mesma função que eles usam. Os tipos com
  consumidor (`pedido.criado`, `estoque.reservado`, `pagamento.aprovado`) saem com
  `mandatory`: devolvidos sem fila, ficam pendentes com `UltimoErro = "SemRota: ..."` e a
  espera crescente, e o canal seguinte recria a fila. Os demais tipos saem como antes.
- **Reconciliação** (REQ-12). Uma vez por ciclo, só uma instância do Worker (lock de
  aplicação no SQL Server) procura pedidos parados há mais de 10 min nas etapas de estoque,
  pagamento e entrega e aplica a regra do consumidor da etapa, com o mesmo evento de saída.
  Uma falha conta um ciclo, em memória; com 3 ciclos e mais de 24 h parado, o pedido de
  estoque ou de pagamento é cancelado (o pagamento fica `NaoProcessado`, situação interna),
  com o `pedido.cancelado` do REQ-15. Desde o REQ-13, o pedido pago cuja ordem falha sempre também se esgota: é
  cancelado com o estorno pendente.
- **Estorno assíncrono** (REQ-13). Cancelar um pedido pago não chama o gateway: o pagamento
  passa a `EstornoPendente` na mesma gravação, e o `ProcessadorEstornos` do Worker estorna a
  cada 1 min (uma instância por rodada, pelo lock de aplicação), tentando de novo sem
  desistir; depois de 24 h e 3 tentativas, cada falha vira um erro no log (7008). As
  situações de estorno são internas. Pedidos cancelados com pagamento aprovado antes do
  REQ-13 não são estornados sozinhos ([quickstart](../specs/013-cancelamento-com-estorno/quickstart.md#5-pedidos-pagos-e-cancelados-antes-do-req-13)).
- **Idempotência no gateway** (REQ-13). O pedido é a chave da cobrança e do estorno: a mesma
  chamada repetida devolve o mesmo resultado. O simulado guarda as chaves em memória; um
  gateway real as guarda do lado dele.
- **Limpeza e reenvio da DLQ** (REQ-14). O Worker apaga, em lotes e com uma instância por
  rodada, os eventos enviados e os processados com mais de 7 dias, as chaves de idempotência
  com mais de 48 h (nunca antes de 24 h) e os pedidos de reenvio finais com mais de 90 dias.
  O reenvio da DLQ é pedido pela API, só pelo Admin, e executado pelo Worker: as mensagens
  selecionadas voltam à fila original com as tentativas zeradas, as outras ficam na posição
  original, e cada mensagem é reenviada no máximo 3 vezes. A API não fala com o broker além da
  prontidão, e com o usuário próprio `oms-api`, sem permissões
  ([fluxo 11](fluxos/11-limpeza-e-reenvio-da-dlq.md)).
- **Mudanças de contrato (Princípio II).** O REQ-11 evoluiu a v1 só com acréscimos: os
  campos `historico` e `entrega` na resposta do pedido e a rota `PATCH .../rastreio`. O
  workflow "Compatibilidade do contrato" (oasdiff) barra mudanças incompatíveis no PR.
- **Status do pedido.** A issue #9 cita "AguardandoEnvio", que não existe no contrato v1;
  o pagamento aprovado leva a `Processando` (REQ-09), e a ordem de entrega leva a
  `ProntoParaEnvio` (REQ-10), onde termina o fluxo automático. O despacho com rastreio leva a
  `Despachado` (REQ-11).
- **Despacho e rastreio** (REQ-11). O despacho cria a ordem de entrega se ela faltar (o que
  também atende aos pedidos pagos sem ordem). Num pedido despachado, a mesma rota corrige
  a transportadora ou o código, sem mudar o status nem a data de despacho. O código é único
  por transportadora, sem diferenciar maiúsculas. A mudança de status do REQ-05 continua
  aceitando `ProntoParaEnvio → Despachado` sem rastreio, que pode ser registrado depois. O
  histórico na consulta mostra o motivo de cada mudança a todos os perfis, inclusive o
  Cliente, e nunca o autor.
- **Ordem de entrega de pedido cancelado** (REQ-13). Cancelar um pedido `ProntoParaEnvio`
  cancela também a ordem (`entrega.canceladaEm`), que não pode mais ser despachada.
- **Um evento só de cancelamento** (REQ-15). `pedido.cancelado` sai em todo cancelamento, com o
  motivo em categoria (sem o texto digitado, que pode ter dados pessoais), inclusive junto do
  `estoque.insuficiente`. O pedido em `PagamentoRejeitado` não é cancelamento e continua
  avisado só pelo `pagamento.recusado`. O estorno confirmado sai em `pagamento.estornado`, com
  os identificadores do gateway. Uma guarda na gravação do pedido recusa, com erro, um
  cancelamento ou um estorno sem o evento, para nenhum caminho novo cancelar em silêncio.
