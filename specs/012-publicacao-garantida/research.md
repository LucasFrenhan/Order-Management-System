# Research: publicação garantida e reconciliação de pedidos parados (REQ-12)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

Não há `NEEDS CLARIFICATION` no contexto técnico. As decisões abaixo resolvem o "como" das
escolhas que a spec e o clarify deixaram para o plano.

## R-01 Catálogo dos consumidores conhecidos

**Decisão**: `AdicionarConsumidor<TManipulador, TDados>` passa a registrar também um
`IDescritorConsumidor` singleton (`DescritorConsumidor<TManipulador, TDados>`), que abre um
escopo, resolve o manipulador e lê `Consumidor` e `Tipos`, como `ConsumidorEventos` já faz
na partida. O despachante recebe `IEnumerable<IDescritorConsumidor>` e monta, uma vez, a
`TopologiaConhecida`: a lista de consumidores, com nome e tipos, e o conjunto dos tipos que
exigem rota.

**Por quê**: FR-003 (consumidor novo entra sem passo manual). O descritor nasce no mesmo
registro do consumidor, então não há lista paralela para esquecer.

**Nos testes**: `RemoverConsumidor` (REQ-08) tira só o `IHostedService`; o descritor fica.
O despachante continua declarando a fila do consumidor removido, como em produção, e os
eventos dele se acumulam nela. Os testes já limpam as filas que leem
(`LimparFilasConsumidorAsync`).

**Alternativas rejeitadas**:
- membros `static abstract` em `IManipuladorEvento<T>`: muda a interface de todos os
  consumidores só para ler dois valores;
- lista fixa no Infrastructure: viola o FR-003;
- ler a topologia do broker (API de gestão): depende do plugin de gestão e não resolve o
  broker vazio.

## R-02 Declaração da topologia antes de publicar

**Decisão**: `DespachanteEventosSaida.ObterCanalAsync`, depois de `DeclararTrocaAsync`, chama
`TopologiaMensageria.DeclararConsumidorAsync` para cada consumidor conhecido, com
`OpcoesMensageria.Intervalos`. Como isso acontece a cada canal novo, vale para a partida,
para a reconexão e para o canal recriado depois de uma falha (FR-001).

**Por quê**: a mesma função e as mesmas opções que o `ConsumidorEventos` usa garantem nomes,
ligações e argumentos idênticos (FR-002). Uma diferença de argumento derrubaria o canal com
`PRECONDITION_FAILED`.

**Alternativas rejeitadas**: o despachante esperar os consumidores do mesmo processo
declararem as filas. Não cobre um consumidor que roda em outra instância do Worker, nem um
consumidor que falhou ao subir.

## R-03 Publicação com rota obrigatória

**Decisão**: `mandatory: topologia.ExigeRota(evento.Tipo)`. Com confirmações rastreadas
(`publisherConfirmationTrackingEnabled: true`, já em uso), o RabbitMQ.Client 7.2.2 lança
`PublishReturnException` quando o broker devolve a mensagem (`basic.return`, `NO_ROUTE`).
O `catch` que já existe em `PublicarAsync` trata a exceção como falha:
- `RegistrarFalha` grava `SemRota: <ReplyText>` em `UltimoErro`, com a espera crescente do
  REQ-06 (2^tentativas s, com teto);
- o lote é encerrado, e os eventos seguintes esperam atrás deste (ordem do outbox);
- o canal é descartado, como em qualquer falha. O canal seguinte declara a topologia de
  novo, e isso **recria a fila apagada**: o evento sai na próxima tentativa e fica na fila
  até o consumidor voltar a ler (cenário 4 da US1).

O log `FalhaPublicacao` (5002) passa a mostrar o motivo `SemRota` no lugar do nome da
exceção, e o `UltimoErro` traz o texto do broker (FR-015).

**Alternativas rejeitadas**:
- `mandatory: true` para todos os tipos: `estoque.insuficiente`, `pagamento.recusado` e
  `entrega.criada` não têm fila interna e nunca sairiam (FR-005);
- declarar uma fila "coringa" que receba tudo: acumula mensagens sem leitor.

## R-04 Reaproveitamento das regras dos consumidores

**Decisão**: interface nova no Application, `IEtapaFluxoPedido`:

```text
EtapaFluxoPedido Etapa { get; }                       // Estoque, Pagamento, Entrega
Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken)
```

`ReservaEstoque`, `ProcessamentoPagamento` e `CriacaoOrdemEntrega` a implementam. O corpo
atual de `ManipularAsync` vira `AplicarAsync`, que devolve `true` quando gravou o efeito e
`false` quando o pedido não estava apto. `ManipularAsync` passa a só chamar
`AplicarAsync(evento.IdEvento, evento.Dados.PedidoId, ...)`. Exceção continua sendo falha.

A reconciliação chama `AplicarAsync` com `idOrigem = Guid.Empty`. Nos logs dos consumidores,
o identificador de evento vazio indica que a origem foi a reconciliação; o log da própria
reconciliação (R-11) liga o pedido à etapa.

**Por quê**: FR-008 e FR-017. As reavaliações por conflito, as chamadas ao gateway e os
eventos de saída ficam num lugar só; o comportamento do consumo não muda.

**Transação**: fora da base de consumo não há transação externa. Cada `SalvarAsync` é uma
transação do EF Core, e o efeito, o histórico e o evento de saída entram juntos, como hoje.
O registro em `EventosProcessados` não é gravado: a reconciliação não consome mensagem. Se o
evento original chegar depois, o consumidor o processa, encontra o pedido não apto e o
ignora (US2 cenário 5).

**Registro**: `AdicionarConsumidor` registra também
`IEtapaFluxoPedido → TManipulador` (escopo), quando o manipulador implementa a interface.

**Alternativa rejeitada**: montar um envelope sintético e chamar `ManipularAsync`. Exigiria
inventar o conteúdo de cada evento (por exemplo, os itens de `estoque.reservado`) só para
descartá-lo.

## R-05 Seleção dos pedidos parados

**Decisão**: porta `IConsultaPedidosParados` no Application, implementada no Infrastructure
com o `ContextoBancoDados`, sem rastreamento:

```text
Task<IReadOnlyList<PedidoParado>> ListarAsync(DateTimeOffset paradoAntesDe, int quantidade,
    IReadOnlyCollection<Guid> comFalha, CancellationToken)
PedidoParado(Guid PedidoId, EtapaFluxoPedido Etapa, DateTimeOffset AtualizadoEm)
```

São duas consultas, cada uma ordenada por `AtualizadoEm`:
1. pedidos parados fora de `comFalha`, até `quantidade`;
2. se sobrar espaço no lote, pedidos parados dentro de `comFalha`, até completar.

Assim os pedidos que falharam em ciclos anteriores ficam por último (FR-011, SC-007).

A etapa vem de um `CASE` com os mesmos predicados do domínio, todos com
`AtualizadoEm < paradoAntesDe`:

| Etapa | Predicado (espelha o domínio) |
|---|---|
| Estoque | `Status IN (Pendente, Processando, ProntoParaEnvio) AND EstoqueReservado = 0` |
| Pagamento | `AptoParaPagamento`: `EstoqueReservado = 1`, sem linha em `Pagamentos`, status não final |
| Entrega | `AptoParaOrdemEntrega`: `EstoqueReservado = 1`, pagamento `Aprovado`, sem linha em `OrdensEntrega`, `Processando` ou `ProntoParaEnvio` |

O predicado SQL só escolhe candidatos. A decisão final continua no domínio: a regra relê o
pedido e devolve `false` se ele não estiver mais apto. Um teste de integração confere que
cada predicado seleciona exatamente os pedidos que o domínio considera aptos, para que os
dois lados não se afastem.

**Índice**: nenhum novo. `IX_Pedidos_Status` já filtra os status finais, e o volume de
pedidos não finais é pequeno. Fica registrado como risco no plano.

## R-06 Contagem de falhas em memória

**Decisão**: `RegistroFalhasReconciliacao` (Application, singleton), com um dicionário
concorrente `PedidoId → (Falhas, UltimaFalhaEm)`:
- quando a regra falha num ciclo: `Falhas + 1` e `UltimaFalhaEm = agora`;
- quando a regra grava o efeito, quando o pedido não estava mais apto ou quando o pedido se
  esgota: a entrada é removida;
- no início de cada ciclo: são removidas as entradas com `UltimaFalhaEm` anterior a
  `agora - LimiteEsgotamento`. É o pedido que deixou de aparecer como candidato, por
  exemplo porque a operação o moveu.

O conjunto `comFalha` do R-05 é o conjunto das chaves.

**Por quê**: o clarify decidiu não gravar nada no pedido. Reiniciar o Worker zera a contagem
e só adia o esgotamento (spec, Edge Cases).

## R-07 Esgotamento

**Decisão**: quando a regra falha no ciclo atual, a contagem é incrementada. O esgotamento
acontece no mesmo ciclo se valerem as três condições:
- `Falhas >= CiclosFalhaEsgotamento` (3);
- `AtualizadoEm <= agora - LimiteEsgotamento` (24 h);
- etapa `Estoque` ou `Pagamento`.

A falha que completa a contagem é a da tentativa atual. A regra é sempre tentada antes do
esgotamento: um pedido que voltaria a funcionar nunca é cancelado sem uma última tentativa.

O esgotamento roda no mesmo escopo do pedido, depois de `DescartarAlteracoes()` no
armazenamento (o padrão dos consumidores), para limpar o que a regra deixou rastreado. Segue
o padrão "ler, decidir, gravar", com até 3 reavaliações em conflito. Assim o Application não
precisa criar escopo nem depender do host (analyze I1):
- relê o pedido para alterar e confere se continua parado na mesma etapa (estoque:
  status não final e sem reserva; pagamento: `AptoParaPagamento`). Se não continua, só
  remove a entrada e registra em log;
- **estoque**: confere `TransicoesStatusPedido.PermitidaParaOperacao(status, Cancelado)` e
  chama `MudarStatus(Cancelado, null, "Não foi possível reservar o estoque.", agora)`;
- **pagamento**: lê os produtos para alterar e chama o novo
  `Pedido.RegistrarPagamentoNaoProcessado("Não foi possível processar o pagamento.", produtos, agora)`
  (data-model);
- grava sem evento de saída e registra o log de erro (R-11).

**Entrega**: nunca se esgota (FR-013). A contagem continua crescendo, e o pedido continua
no fim da fila do lote.

## R-08 Uma instância por ciclo

**Decisão**: no início de cada ciclo, o reconciliador abre uma conexão própria e chama
`sp_getapplock @Resource = 'oms-reconciliacao', @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0`.
A conexão fica aberta até o fim do ciclo e é fechada depois, o que libera o lock. A
instância que não consegue o lock pula o ciclo, com log de depuração.

**Por quê**: evita que duas instâncias do Worker cobrem o mesmo pedido ao mesmo tempo. Com
o gateway simulado, a concorrência otimista já impediria a gravação dupla, mas o gateway
seria chamado duas vezes. A disputa com os consumidores continua protegida pela
concorrência otimista (FR-009).

**Alternativas rejeitadas**:
- sem lock: duas chamadas ao gateway por pedido;
- tabela de arrendamento: exige migration e limpeza, para o mesmo efeito.

## R-09 Onde e quando roda

**Decisão**: `ReconciliadorPedidos : BackgroundService` no Infrastructure, registrado por
`AdicionarReconciliacao()` no `ProgramaWorker`. Ele faz o seguinte:
- o primeiro ciclo roda na partida, depois um `PeriodicTimer` com `IntervaloCiclo`;
- o relógio é o `TimeProvider` (testável);
- em cada ciclo, adquire o lock (R-08), poda o registro (R-06), lista os candidatos (R-05)
  e trata cada pedido num escopo novo, chamando o `ServicoReconciliacao` do Application;
- uma exceção num pedido é falha daquele pedido (R-06); uma exceção fora do laço, como banco
  fora do ar, termina o ciclo com o log `FalhaCiclo`, sem derrubar o processo.

O `ServicoReconciliacao` (Application) decide por pedido: escolhe a regra, conta a falha e
esgota. A parte que depende do banco e do host fica no Infrastructure.

## R-10 Opções

**Decisão**: seção nova `Reconciliacao` (`OpcoesReconciliacao` e validador na partida,
como `OpcoesMensageria`):

| Chave | Padrão | Faixa |
|---|---|---|
| `Habilitada` | `true` | — |
| `IntervaloCicloSegundos` | 300 | 1 a 86 400 |
| `LimiteParadoSegundos` | 600 | 1 a 604 800 |
| `LimiteEsgotamentoSegundos` | 86 400 | maior que `LimiteParadoSegundos`, até 2 592 000 |
| `CiclosFalhaEsgotamento` | 3 | 1 a 100 |
| `TamanhoLote` | 100 | 1 a 1 000 |

`Habilitada` existe para os testes: cada teste de integração usa um banco novo, mas muitos
semeiam pedidos com datas fixas, que pareceriam parados e seriam alterados por um Worker em
teste. A `FabricaWorker` desliga a reconciliação por padrão, e os testes da reconciliação a
ligam. Em produção e no ambiente de demonstração, ela fica ligada (FR-007).

## R-11 Logs

| Id | Nome | Nível | Campos |
|---|---|---|---|
| 9001 | `PedidoRecuperado` | Warning | PedidoId, Etapa |
| 9002 | `PedidoEsgotado` | Error | PedidoId, Etapa, Falhas, Motivo |
| 9003 | `FalhaPedidoReconciliacao` | Warning | PedidoId, Etapa, Falhas, Erro (tipo e mensagem da exceção) |
| 9004 | `ResumoReconciliacao` | Warning se houve recuperado ou esgotado; Debug se não | Estoque, Pagamento, Entrega, Esgotados, Falhas |
| 9005 | `FalhaCicloReconciliacao` | Warning | Erro, Mensagem |
| 9006 | `CicloReconciliacaoIgnorado` | Debug | — (outra instância tem o lock) |
| 9007 | `PedidoNaoMaisParado` | Debug | PedidoId, Etapa |

Os campos são só identificadores e contagens (FR-015). A faixa 9xxx é livre: as faixas em uso
vão de 3001 a 8003.

## R-12 Situação de pagamento nova

**Decisão**: `SituacaoPagamento.NaoProcessado`, gravada como texto, como as demais. A check
constraint `CK_Pagamentos_SituacaoValida` é gerada a partir do enum, então a migration
`PagamentoNaoProcessado` a recria. `Pagamento.RegistrarResultado` passa a aceitar
`NaoProcessado`, sem identificador de transação.

A situação não aparece no contrato HTTP v1 nem nos eventos, então os contratos não mudam
(FR-016). Isso foi conferido: `SituacaoPagamento` só é usada no Domain, no consumidor de
entrega e na configuração do EF.

## R-13 Testes

- **Unitários**:
  - domínio: `RegistrarPagamentoNaoProcessado`, devolução do estoque e `RegistrarResultado`
    com `NaoProcessado`;
  - `ServicoReconciliacao` com a regra falsa: recuperado, não mais apto, falha, contagem,
    esgotamento por etapa, entrega sem esgotamento, esgotamento perdendo para um conflito;
  - `RegistroFalhasReconciliacao`: contagem e poda;
  - validador de `OpcoesReconciliacao`;
  - `TopologiaConhecida`: quais tipos exigem rota.
- **Integração (banco e broker reais)**:
  - `PublicacaoSemFilaTestes` (FR-018):
    - broker sem as filas `oms.*` e um `pedido.criado` pendente: o Worker sobe, as filas
      existem antes da publicação, e a mensagem está em `oms.estoque`;
    - fila apagada com o Worker no ar: o evento fica pendente com `SemRota`, a fila é
      recriada e o evento sai;
    - um tipo sem consumidor é marcado como enviado sem fila;
  - `ReconciliacaoTestes`, com o pedido semeado e a data atrasada por SQL:
    - recuperação nas três etapas, com o evento de saída válido pelo schema;
    - pedido recente intocado;
    - dois Workers ao mesmo tempo produzem um efeito só;
    - esgotamento do pagamento com um gateway que sempre falha (estoque devolvido,
      `NaoProcessado`, `Cancelado`);
    - predicados SQL iguais aos do domínio.

## R-14 Documentação

- `specs/006-mensageria/spec.md` › Pendências futuras: o item "evento publicado antes de a
  fila existir" passa a apontar para o REQ-12. Limpeza e DLQ continuam pendentes.
- `specs/010-fulfillment/spec.md` › Assumptions: nota de que o REQ-12 recupera os pedidos
  pagos sem ordem.
- `README.md`: linha do REQ-12, seção de configuração `Reconciliacao` e próximos passos com
  as duas pendências do clarify:
  - a etapa de entrega, que exige estorno;
  - a visibilidade dos cancelamentos para outros sistemas.
- `specs/008-estoque` e `specs/010-fulfillment` quickstart: a nota "só crie pedidos depois
  de a fila existir" deixa de ser necessária.
