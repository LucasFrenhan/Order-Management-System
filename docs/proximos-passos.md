# Próximos passos

[← Voltar ao README](../README.md)

Com o REQ-11, as issues de negócio do roteiro original (#1 a #11) estão entregues: o pedido
vai da criação ao despacho com rastreio. O REQ-12 fechou a perda silenciosa de eventos, e o
REQ-13 garante que o Cliente nunca fica cobrado por um pedido cancelado. O REQ-14 limpou os
dados de mensageria que cresciam sem limite e trocou o painel do broker por um reenvio da DLQ
controlado, e o REQ-15 tornou os cancelamentos e os estornos visíveis para outros sistemas. O
REQ-16 trouxe a rastreabilidade de ponta a ponta do pedido, com métricas da mensageria, e o
REQ-17 fez os consumidores voltarem sozinhos depois de o broker reiniciar (issue #41). O REQ-18
avisa quem acompanha um pedido quando o status muda, por webhooks e por SSE (issue #43), e o
REQ-19 tirou o pagamento do processo de workers: a cobrança e o estorno vão pela rede a uma API
de pagamento simulada, com latência, falhas, novas tentativas seguras, circuito e pausa do
consumo (issue #46). O REQ-20 pôs a análise estática do SonarQube Cloud em todo PR e na
`main`, com o Quality Gate exigido para o merge e a `main` protegida por um ruleset (issue #18).
O REQ-21 trouxe a listagem de pedidos por perfil, com filtros e paginação por cursor (issue #52),
e o REQ-22, o catálogo de produtos pela API, com busca em português pelo Full-Text Search do
SQL Server (issue #53). O REQ-23 limitou as requisições do login e do registro, com uma contagem
única para todas as instâncias da API (issue #58), e o REQ-24 completou o ciclo da autenticação:
renovação com rotação, logout, troca e recuperação de senha e confirmação de e-mail, com o envio
de e-mail no Worker (issue #55).
Cada trabalho novo segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` →
`/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma
branch `feature/req-XX-...`, com a numeração a partir do REQ-25.

A fila abaixo saiu do levantamento das pendências de todas as specs (2026-11-02). A ordem é
uma sugestão; o número do REQ é dado quando o trabalho começa.

| Ordem | Requisito | Origem | Escopo |
|-------|-----------|--------|--------|
| 1 | a numerar | [#59](https://github.com/LucasFrenhan/Order-Management-System/issues/59) — Dependabot e CodeQL | Dependências vulneráveis e análise do CodeQL na cadeia de build |
| 2 | a numerar | [#54](https://github.com/LucasFrenhan/Order-Management-System/issues/54) — Gestão de usuários | Usuários internos e perfis pelo Admin |
| 3 | a numerar | [#60](https://github.com/LucasFrenhan/Order-Management-System/issues/60) — Rotação da chave JWT | Várias chaves com `kid` e rotação sem derrubar sessões |
| 4 | a numerar | [#57](https://github.com/LucasFrenhan/Order-Management-System/issues/57) — Estornos pela operação | Forçar tentativa, dar baixa manual e evento de estorno travado |
| 5 | a numerar | [#56](https://github.com/LucasFrenhan/Order-Management-System/issues/56) — Evolução do pagamento | Estudo: nova cobrança após recusa, estorno parcial, devolução, pagamento em análise e gateway real |
| 6 | a numerar | [#75](https://github.com/LucasFrenhan/Order-Management-System/issues/75) — Deadlock no limite de requisições | Investigar: teste intermitente na `main` e possível pausa sem o limite em um deadlock real (REQ-23) |
| contínua | sem REQ | [#61](https://github.com/LucasFrenhan/Order-Management-System/issues/61) — Dívida técnica de code smells | Sem prazo: todo PR que corrigir code smells referencia a #61 (`Refs #61`) e atualiza a tabela dela |

### Para análise futura (sem issue)

Itens do mesmo levantamento que ficaram anotados, sem issue, para decidir depois:

- **Negócio**
  - Rastreio não validado com a transportadora; a correção do rastreio não gera aviso nem
    evento (REQ-11, REQ-18).
  - Catálogo: tabela de movimentações de estoque (hoje o histórico dos ajustes fica no log),
    eventos `produto.*`, exclusão de produtos, categorias, imagens e importação em massa; busca
    de palavras sem acento e flexionadas ("cafes" não acha "Café") (REQ-22).
  - Notificações: SignalR, push, e-mail e SMS, reenvio manual de entrega esgotada, troca do
    segredo sem recadastro e avisos retroativos (REQ-18).
  - Pedidos pagos e cancelados antes do REQ-13 não foram estornados (passivo de dados; consulta
    no [quickstart do REQ-13](../specs/013-cancelamento-com-estorno/quickstart.md#5-pedidos-pagos-e-cancelados-antes-do-req-13)).
- **Segurança**
  - Autenticação: exigir o e-mail confirmado em ações específicas, lista de sessões e "encerrar todas" como rota, MFA e
    login social (REQ-24).
  - Limite de requisições: as contagens ficam no SQL Server, e um armazenamento próprio, como o Redis, isolaria o limite do
    banco dos pedidos em uma inundação. A troca é só outra implementação de `IArmazenamentoContagens` (REQ-23,
    [research R-01](../specs/023-rate-limiting/research.md#r-01-armazenamento-compartilhado-sql-server-ou-redis)).
  - A demonstração roda em HTTP, sem certificado (REQ-07; só local).
  - A chave que assina os cursores das listagens de pedidos e de produtos é derivada da chave de assinatura do JWT;
    pode passar a uma chave própria do cursor, trocando só o registro de `IChaveCursor`
    na API. Vale decidir junto com a rotação da chave do JWT (#60), que hoje invalidaria os
    cursores em uso (REQ-21, [research R-04](../specs/021-listagem-pedidos/research.md#r-04--chave-do-hmac-do-cursor)).
- **Operação e infraestrutura**
  - Provedor real de e-mail em produção: o envio é SMTP puro, e a escolha é uma decisão de implantação; os e-mails são
    só texto, sem HTML nem modelos configuráveis (REQ-24, [fluxo 16](fluxos/16-envio-de-email.md)).
  - Ambientes de homologação e produção, deploy e publicação de imagem (REQ-02, REQ-07).
  - Alertas sobre as métricas que já existem (REQ-14, REQ-16).
  - Purga da DLQ (apagar sem reenviar) pela API; hoje é pelo painel do RabbitMQ (REQ-14).
  - Métrica de latência por etapa do pedido (REQ-16, R-07).
  - Arquivamento dos dados apagados pela limpeza (REQ-14).
  - Evento sem consumidor interno, publicado antes de um sistema de fora ligar a fila, é
    descartado (REQ-15).
  - Garantia de ordem do reenvio da DLQ só para filas clássicas; filas quorum exigiriam rever
    (REQ-14).
  - Pagamento: consumidor sequencial numa rajada de pedidos, circuito por instância e chamada
    dentro da transação (REQ-19).
  - SSE: reenvio limitado a 500 avisos e 7 dias; limite de conexões por instância (REQ-18).
  - Chaves da API de pagamento simulada perdidas ao recriar o contêiner (REQ-19; aceito).
- **Registro e processo**
  - A data de ratificação da constituição está como `TODO(RATIFICATION_DATE)`.
  - A T038 do `tasks.md` do REQ-13 está desmarcada, mas o PR foi mergeado.
  - O fechamento de PRs de forks não foi testado de ponta a ponta (exige uma segunda conta), e o
    push direto na `main` não foi tentado (REQ-20).
  - Branches `feature/req-*` antigas acumuladas, locais e remotas.
  - Nos testes, `RemoverConsumidor` deixa o descritor do consumidor, e a prontidão fica 503.
  - O "código novo" do SonarQube usa "versão anterior", sem versão informada (REQ-20, mantido).


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
  condição `AptoParaPagamento`; a porta `IGatewayPagamento`, implementada pelo gateway por HTTP
  com resiliência (REQ-19, [fluxo 14](fluxos/14-gateway-de-pagamento.md))
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
- **Avisos de mudança de status** (REQ-18): o `ArmazenamentoPedidos.SalvarAsync` gera o
  `pedido.status-alterado` e o aviso de toda entrada nova do histórico; um caminho novo que muda
  status só precisa gravar por ele (a base de consumo recusa o que não passou por lá). A
  `LeituraAvisos` lê qualquer tabela de avisos por cursor de `rowversion` sem lacunas
  ([research R-13](../specs/018-notificacao-status/research.md#r-13-tabela-de-avisos-e-leitura-sem-lacunas-com-min_active_rowversion)).
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
  chamada repetida devolve o mesmo resultado. Desde o REQ-19, as chaves ficam na API de
  pagamento simulada (em memória, perdidas só se ela for recriada), e não no Worker.
- **Dependência fora do ar pausa o consumo** (REQ-19). Um manipulador que lança
  `DependenciaIndisponivelException` faz a base de consumo devolver a mensagem à fila, sem
  nova tentativa nem DLQ, e o supervisor pausar a inscrição até a dependência poder ser testada.
  Serve para qualquer consumidor futuro que chame um serviço externo.
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
