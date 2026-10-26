# Research: Visibilidade dos cancelamentos e dos estornos para outros sistemas

**Requisito**: REQ-15 (issue #29) | **Spec**: [spec.md](./spec.md) | **Data**: 2026-10-26

Cada decisão segue o formato Decisão / Motivo / Alternativas. Não havia `NEEDS CLARIFICATION`
no contexto técnico; as decisões abaixo resolvem como cumprir a spec no código atual.

## R-01 — Os caminhos de cancelamento no código

**Levantamento** (código em 2026-10-26, depois do merge do PR #36):

| Caminho | Onde | Como cancela hoje | Evento hoje |
|---------|------|-------------------|-------------|
| Cliente ou operação pela API | `ServicoPedidos.AtualizarStatusAsync` | `Pedido.Cancelar(autor, motivo, …)` | nenhum |
| Falta de estoque | `ReservaEstoque.CancelarSemEstoqueAsync` | `Pedido.MudarStatus(Cancelado, null, "Estoque insuficiente.")` | `estoque.insuficiente` |
| Reconciliação, etapa de estoque | `ServicoReconciliacao.CancelarAsync` | `Pedido.MudarStatus(Cancelado, null, MotivoEstoqueNaoReservado)` | nenhum |
| Reconciliação, etapa de pagamento | `ServicoReconciliacao.CancelarAsync` | `Pedido.RegistrarPagamentoNaoProcessado` | nenhum |
| Reconciliação, etapa de entrega | `ServicoReconciliacao.CancelarAsync` | `Pedido.Cancelar(null, MotivoOrdemEntregaNaoCriada, …)` | nenhum |

E a confirmação do estorno: `ServicoEstornos.ConfirmarAsync`, com `Pedido.ConfirmarEstorno`.

**Decisão**: os dois caminhos que usam `MudarStatus(Cancelado)` passam a usar
`Pedido.Cancelar(null, motivo, produtos vazios, agora)`. Com o estoque não reservado, sem
pagamento e sem ordem, `Cancelar` faz exatamente o que o `MudarStatus` fazia (valida a
transição e grava o histórico sem autor), e devolve o `ResultadoCancelamento`.
`RegistrarPagamentoNaoProcessado` passa a devolver `ResultadoCancelamento` também (estoque
devolvido, sem estorno, sem ordem). Assim, todo cancelamento no domínio devolve o que fez, e o
evento é montado a partir desse resultado.

**Motivo**: o evento precisa saber se o estoque foi devolvido, se o estorno ficou pendente e se
a ordem foi cancelada (FR-002). Tirar essa informação de um único tipo evita que cada caminho
a deduza de um jeito.

**Alternativas**:
- Proibir `MudarStatus(Cancelado)` no domínio: rejeitada por ora; testes de concorrência e de
  domínio usam `MudarStatus(Cancelado)` para preparar cenários, e a guarda do R-03 já pega
  qualquer cancelamento sem evento no caminho real de gravação.
- Eventos de domínio no agregado `Pedido`, convertidos em outbox por um interceptador do EF:
  rejeitada; o projeto monta os eventos no Application em todos os casos (REQ-06 em diante), e a
  categoria do motivo (`Cliente`/`Operacao`) depende do perfil, que o domínio não conhece.

## R-02 — Montagem do `pedido.cancelado`

**Decisão**: uma fábrica no Application, `EventosPedido.Cancelado(pedido, motivo, etapa,
resultado, instante)` em `Application/Pedidos/EventosPedido.cs`, devolve o `EventoSaida` com
o envelope v1. Os quatro serviços chamam `armazenamento.Adicionar(...)` com ele antes do
`SalvarAsync`, como já fazem com os demais eventos. Nos laços com reavaliação por conflito, o
evento é adicionado de novo a cada tentativa, porque `DescartarAlteracoes` limpa o
`ChangeTracker`, inclusive o evento pendente.

- `ServicoPedidos`: motivo `Operacao` se o usuário tem Admin ou OperadorLogistica
  (`EhOperacao`), senão `Cliente`.
- `ReservaEstoque`: motivo `EstoqueInsuficiente`; `estoque.insuficiente` continua sendo
  adicionado como hoje, na mesma gravação.
- `ServicoReconciliacao`: motivo `ReconciliacaoEsgotada`, com a etapa (`Estoque`, `Pagamento`
  ou `Entrega`), convertida de `EtapaFluxoPedido` para o enum do contrato.
- O instante do evento (`canceladoEm` e `ocorridoEm`) é o mesmo `agora` gravado no histórico.

**Motivo**: um ponto só define o conteúdo; os serviços continuam responsáveis por adicionar o
evento à gravação, como em todo o projeto.

**Alternativas**: montar o evento em cada serviço: rejeitada, quatro cópias do mesmo
mapeamento.

## R-03 — Garantia de que nenhum caminho esquece o evento

**Decisão**: `ArmazenamentoPedidos.SalvarAsync` confere, antes do `SaveChangesAsync`, o
`ChangeTracker`:

- cada `Pedido` modificado cujo `Status` original não era `Cancelado` e o atual é `Cancelado`
  precisa de um `EventoSaida` adicionado do tipo `pedido.cancelado` com o mesmo
  `idCorrelacao`;
- cada `Pagamento` modificado cuja `Situacao` passou para `Estornado` precisa de um
  `pagamento.estornado` do mesmo pedido.

Faltando algum, lança `InvalidOperationException` (erro de programação, não de negócio), sem
gravar nada. O `idCorrelacao` é lido do `Conteudo` com `JsonDocument`, sem coluna nova. A
exceção segue o caminho de erro que já existe em cada processo: no Worker, a mensagem vai para
as novas tentativas e, depois, para a DLQ; na API, vira 500. Nenhum comportamento novo.

**Motivo**: FR-001 e FR-006 pedem "por qualquer caminho". Um caminho novo que cancele sem
evento falha em qualquer teste de integração que passe por ele, em vez de cancelar em silêncio
como hoje.

**Alternativas**:
- Só testes por caminho: necessários (FR-013), mas não pegam um caminho futuro.
- Guarda no `ContextoBancoDados.SaveChanges`: rejeitada; os testes semeiam pedidos cancelados
  direto pelo contexto, e a regra é do fluxo da aplicação, não do banco.
- Gerar o evento automaticamente na guarda: rejeitada; ela não sabe o motivo em categoria.

## R-04 — Conteúdo e tipos do contrato

**Decisão**: dois records novos em `Contratos/Eventos/Eventos.cs` e duas entradas em
`TiposEvento`:

- `pedido.cancelado` → `PedidoCancelado { PedidoId, Numero, Motivo, EtapaEsgotada?,
  CanceladoEm, EstoqueDevolvido, EstornoPendente, OrdemEntregaCancelada }`;
- `pagamento.estornado` → `PagamentoEstornado { PedidoId, Numero, Valor,
  IdentificadorTransacao, IdentificadorEstorno, EstornadoEm }`.

`MotivoCancelamento` (`Cliente`, `Operacao`, `EstoqueInsuficiente`, `ReconciliacaoEsgotada`) e
`EtapaEsgotada` (`Estoque`, `Pagamento`, `Entrega`) são enums do contrato, serializados como
texto (`JsonStringEnumConverter`, já configurado). `etapaEsgotada` é sempre enviado: texto
quando o motivo é `ReconciliacaoEsgotada`, `null` nos demais (o serializador escreve nulos, e o
schema exige a combinação com `if/then/else`).

Sem valor nem itens no `pedido.cancelado` (clarify Q1); com os identificadores do gateway no
`pagamento.estornado` (clarify Q2). A `Moeda` não entra no `pagamento.estornado`: `pagamento.
aprovado` e `pagamento.recusado` também não a trazem, e a moeda do pedido está no
`pedido.criado`.

**Motivo**: segue o padrão dos eventos existentes (camelCase, enums como texto, objetos
fechados com `additionalProperties: false`).

**Alternativas**: motivo como texto livre do histórico: rejeitado (FR-010, pode conter dados
pessoais).

## R-05 — `pagamento.estornado` no `ServicoEstornos`

**Decisão**: em `ConfirmarAsync`, depois de `pedido.ConfirmarEstorno`, o serviço adiciona o
`pagamento.estornado` a cada tentativa de gravação (inclusive depois de reler por conflito). Se
outra instância já confirmou (`LerPendenteAsync` devolve nulo), nada é adicionado. A repetição
do mesmo estorno pelo gateway (idempotência do REQ-13) só grava uma vez, porque o pagamento
deixa de estar pendente; então sai um único evento por pedido.

A cobrança aprovada para pedido já cancelado (`RegistrarCobrancaAposCancelamento`) não gera
evento (clarify Q3, FR-014): não há transição para `Cancelado` nem para `Estornado` ali.

**Motivo**: o evento nasce na mesma gravação da confirmação (FR-006), e o lock por rodada e a
concorrência otimista do REQ-13 já garantem uma confirmação por pedido.

## R-06 — Publicação e topologia

**Decisão**: nenhuma mudança na mensageria. A troca `oms.eventos` é `topic` e as filas internas
se ligam só aos tipos que consomem (`TopologiaMensageria`), então os consumidores de estoque,
pagamento e entrega não recebem os tipos novos (FR-009). Sem consumidor interno, os tipos novos
saem sem `mandatory` (`TopologiaConhecida.ExigeRota` devolve falso) e são marcados como
enviados mesmo sem fila ligada, como descrito no fluxo 8. Sistemas de fora ligam as próprias
filas a `oms.eventos` com as chaves `pedido.cancelado` e `pagamento.estornado`.

A ordem entre `estoque.insuficiente` e `pedido.cancelado`, gravados na mesma transação, não faz
parte do contrato: o outbox publica pela ordem do `Id`, mas quem consome não deve depender da
ordem entre tipos diferentes (entrega pelo menos uma vez, sem ordem garantida entre filas).

**Motivo**: o despachante, a limpeza do REQ-14 e o reenvio da DLQ já tratam qualquer tipo do
catálogo.

## R-07 — Persistência

**Decisão**: nenhuma migration. Os eventos vão para a tabela `EventosSaida` existente, e nenhum
campo de domínio muda.

## R-08 — Logs

**Decisão**: nenhum log novo. Os logs de cancelamento (estoque devolvido, ordem cancelada,
estorno solicitado, pedido esgotado, pedido cancelado sem estoque), o de estorno confirmado
(7006) e os do despachante continuam. A guarda do R-03 lança exceção, que aparece nos logs de
erro já existentes de cada chamador.

## R-09 — Testes

- **Unitários**: `EventosPedido.Cancelado` (cada motivo, etapa nula fora da reconciliação,
  flags a partir do resultado); `Pedido.Cancelar` sem reserva (o caminho do estoque);
  `RegistrarPagamentoNaoProcessado` devolvendo o resultado; `ServicoPedidos`
  (`Cliente`/`Operacao`), `ReservaEstoque`, `ServicoReconciliacao` e `ServicoEstornos`
  adicionando o evento certo, inclusive nas reavaliações, com o armazenamento falso.
- **Schema**: `SchemaEventosTestes` cobre os dois tipos novos (válidos, sem campo obrigatório,
  motivo fora da lista, `etapaEsgotada` incoerente com o motivo); o `.csproj` copia os schemas de
  `specs/015-visibilidade-cancelamentos/contracts/eventos/`.
- **Integração** (banco e broker reais): cada caminho de cancelamento gera um
  `pedido.cancelado` no outbox e na fila de teste ligada a `oms.eventos`; confirmação do estorno
  gera um `pagamento.estornado`; cancelamentos simultâneos geram um só evento; falha na gravação
  não gera evento; a guarda do R-03 recusa um cancelamento sem evento; os consumidores internos
  não recebem os tipos novos.

## R-10 — Documentos de fluxo

**Decisão**: na fase de Polish, atualizar os fluxos 3 (mudança de status), 4 (reserva de
estoque), 8 (publicação: catálogo de tipos e tipos sem consumidor), 9 (reconciliação) e 10
(estorno), e o índice `docs/fluxos/README.md` (tabela de eventos). O diagrama de estados não
muda. A limitação "não existe evento de cancelamento" sai dos documentos onde aparece, com a
issue #29 no lugar.
