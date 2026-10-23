# Feature Specification: Publicação garantida de eventos e reconciliação de pedidos parados

**Feature Branch**: `feature/req-12-publicacao-garantida`

**Requisito**: REQ-12 (GitHub issue #26 — "[Mensageria] Publicação garantida de eventos e reconciliação de pedidos parados")

**Created**: 2026-10-23

**Status**: Draft

**Input**: User description: "REQ-12 (GitHub issue #26): (1) publicação garantida: as filas de todos os consumidores conhecidos (estoque ← pedido.criado, pagamento ← estoque.reservado, entrega ← pagamento.aprovado) existem antes de o despachante do outbox publicar, mesmo num broker novo ou recriado; um evento com consumidor conhecido que o broker devolve sem rota continua pendente no outbox, com nova tentativa, em vez de ser marcado como enviado; eventos sem consumidor interno continuam publicados normalmente. (2) Reconciliação idempotente de pedidos parados por evento já perdido (sem reserva de estoque, sem cobrança ou sem ordem de entrega), com a mesma regra e o mesmo evento de saída do consumidor da etapa; recupera os pedidos pagos sem ordem do REQ-10; não republica eventos antigos do outbox. (3) Teste que reproduz a publicação num broker sem as filas dos consumidores. Origem: Pendências futuras da spec do REQ-06."

## Contexto

O fluxo automático do pedido anda por eventos: "pedido criado" leva à reserva de estoque
(REQ-08), "estoque reservado" leva à cobrança (REQ-09) e "pagamento aprovado" leva à ordem
de entrega (REQ-10). Cada evento é gravado no outbox junto com a mudança que o gerou e
publicado depois pelo despachante do processo de workers (REQ-06).

Hoje há um caminho de **perda silenciosa**. Se o despachante publica um evento quando a
fila do consumidor ainda não existe, o broker o descarta sem erro, e o outbox o marca como
enviado. O pedido fica parado na etapa para sempre, sem erro, sem DLQ e sem alerta. Isso
acontece:

- na primeira subida de um broker novo, ou recriado, com eventos pendentes: o despachante
  pode publicar antes de o consumidor declarar a fila (encontrado no REQ-08);
- quando um consumidor novo passa a ouvir um evento que já circulava sem fila. O caso
  concreto é o "pagamento aprovado" anterior ao REQ-10: os pedidos pagos até então estão em
  `Processando`, sem ordem de entrega (clarify do REQ-10).

Este requisito fecha esse caminho em duas frentes:

- **Evitar perdas futuras**: as filas de todos os consumidores conhecidos passam a existir
  antes da primeira publicação. Um evento que deveria chegar a um consumidor e não encontra
  fila deixa de ser dado como enviado: continua pendente e é tentado de novo.
- **Recuperar o que já se perdeu**: uma reconciliação periódica procura pedidos parados
  numa etapa do fluxo automático e aplica a mesma regra do consumidor daquela etapa. A
  recuperação parte do estado do pedido, não dos eventos antigos do outbox; republicá-los
  foi rejeitado no REQ-10.

## Clarifications

### Session 2026-10-23

- Q: A reconciliação roda sozinha, periodicamente, no processo de workers, ou só quando
  alguém dispara? → A: Automática, a cada ciclo no processo de workers, com o primeiro
  ciclo logo após a subida; sem disparo manual.
- Q: Se a regra da etapa falhar sempre para o mesmo pedido, a reconciliação tenta a cada
  ciclo sem limite ou desiste depois de algumas tentativas? → A: Tenta a cada ciclo, sem
  limite, com a falha registrada em log a cada vez; nada novo é gravado no pedido.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o Cliente, cujo pedido não pode ficar parado sem
explicação; a operação, que hoje precisa descobrir e mover à mão os pedidos parados; e quem
opera o ambiente, que sobe ou recria o broker.

### User Story 1 - Nenhum evento com consumidor se perde num broker sem filas (Priority: P1)

Com o processo de workers subindo contra um broker vazio (novo ou recriado) e eventos já
pendentes no outbox, todos os eventos chegam às filas dos consumidores, e os pedidos seguem
o fluxo automático normalmente.

**Why this priority**: É a causa raiz da perda silenciosa e o primeiro critério da issue.

**Independent Test**: Com um broker sem nenhuma fila, gravar um pedido com "pedido criado"
pendente no outbox e subir o processo de workers. Conferir que o pedido tem o estoque
reservado e segue o fluxo, sem nenhum evento marcado como enviado sem ter chegado a uma
fila.

**Acceptance Scenarios**:

1. **Given** um broker sem as filas dos consumidores e eventos "pedido criado" pendentes no
   outbox, **When** o processo de workers sobe, **Then** as filas de estoque, pagamento e
   entrega existem antes da primeira publicação, e cada evento pendente chega à fila do
   consumidor dele.
2. **Given** o broker recriado (filas apagadas) com o processo de workers no ar, **When** a
   conexão volta e o despachante publica de novo, **Then** as filas dos consumidores são
   recriadas antes da publicação, e nenhum evento com consumidor é perdido.
3. **Given** a fila de um consumidor removida enquanto o processo roda, **When** o
   despachante publica um evento que só aquele consumidor recebe e o broker o devolve sem
   rota, **Then** o evento continua pendente no outbox, com a falha registrada e a próxima
   tentativa marcada pela espera crescente do REQ-06, e os eventos registrados depois dele
   esperam atrás dele, como em qualquer falha de publicação.
4. **Given** o evento do cenário 3, **When** a fila volta a existir, **Then** o evento é
   publicado na tentativa seguinte e marcado como enviado.
5. **Given** um evento sem consumidor interno ("estoque insuficiente", "pagamento recusado"
   ou "entrega criada"), **When** ele é publicado sem nenhuma fila ligada, **Then** ele é
   marcado como enviado, como hoje.

---

### User Story 2 - Pedidos parados por evento perdido voltam ao fluxo (Priority: P1)

Uma reconciliação no processo de workers procura, periodicamente, pedidos parados há mais
tempo que o limite numa etapa do fluxo automático e aplica a regra do consumidor daquela
etapa, gravando o mesmo resultado e o mesmo evento de saída que o consumidor gravaria:

| Etapa | Pedido parado | Regra aplicada |
|---|---|---|
| Estoque | status não final, sem estoque reservado | reserva do REQ-08 ou cancelamento por falta de estoque |
| Pagamento | apto para cobrança: estoque reservado, sem pagamento, status não final | cobrança do REQ-09 (aprovação ou recusa) |
| Entrega | apto para ordem: pagamento aprovado, estoque reservado, sem ordem, em `Processando` ou `ProntoParaEnvio` | ordem de entrega do REQ-10 |

**Why this priority**: Recupera os pedidos que já se perderam, inclusive os pagos sem
ordem de entrega do REQ-10, que nenhuma correção na publicação alcança.

**Independent Test**: Gravar um pedido pago em `Processando`, sem ordem de entrega e sem
nenhum evento pendente, com a última atualização mais antiga que o limite. Subir o processo
de workers e conferir que, no ciclo seguinte, o pedido ganha a ordem, vai para
`ProntoParaEnvio` e "entrega criada" é publicado.

**Acceptance Scenarios**:

1. **Given** um pedido pago em `Processando`, sem ordem de entrega e parado há mais que o
   limite, **When** a reconciliação roda, **Then** o pedido ganha a ordem de entrega, vai
   para `ProntoParaEnvio` com o mesmo motivo do REQ-10 e sem autor, e "entrega criada" é
   registrado no outbox na mesma gravação.
2. **Given** um pedido `Pendente` sem estoque reservado e parado há mais que o limite,
   **When** a reconciliação roda, **Then** o estoque é reservado e "estoque reservado" é
   registrado; sem estoque, o pedido é cancelado com o motivo do REQ-08 e "estoque
   insuficiente" é registrado.
3. **Given** um pedido com estoque reservado, sem pagamento e parado há mais que o limite,
   **When** a reconciliação roda, **Then** o pedido é cobrado como no REQ-09, com o
   resultado e o evento de saída da aprovação ou da recusa.
4. **Given** um pedido que avançou há menos tempo que o limite, **When** a reconciliação
   roda, **Then** ele não é tocado: o fluxo normal ainda pode estar a caminho.
5. **Given** um pedido recuperado pela reconciliação, **When** o evento original chega
   depois ao consumidor (por exemplo, estava pendente com o broker fora), **Then** o
   consumidor o ignora pela regra de pedido não apto, sem gravar nada de novo.
6. **Given** a reconciliação rodando duas vezes, ou em duas instâncias do processo de
   workers ao mesmo tempo, sobre o mesmo pedido, **When** as duas terminam, **Then** o
   pedido avança uma única vez, com um único evento de saída.
7. **Given** pedidos em status final (`Cancelado`, `Despachado`, `PagamentoRejeitado`) ou
   já com o efeito da etapa, **When** a reconciliação roda, **Then** nada muda.

---

### User Story 3 - Reconciliação visível para a operação (Priority: P2)

Cada pedido recuperado e cada ciclo da reconciliação ficam registrados em log, para a
operação saber que houve perda e quanto foi recuperado, sem precisar consultar o banco.

**Why this priority**: Perda recuperada em silêncio esconde um problema de infraestrutura;
o registro permite investigar a causa.

**Independent Test**: Rodar a reconciliação com um pedido parado em cada etapa e conferir um
registro por pedido recuperado, com a etapa e o resultado, e um resumo do ciclo com as
contagens.

**Acceptance Scenarios**:

1. **Given** um pedido recuperado, **When** o log é lido, **Then** há um registro de aviso
   com o identificador do pedido, a etapa e o resultado, sem dados pessoais.
2. **Given** um ciclo que recuperou pedidos, **When** o log é lido, **Then** há um resumo
   com o total por etapa; um ciclo sem pedidos parados não gera aviso.
3. **Given** um evento devolvido sem rota pelo broker, **When** o log é lido, **Then** a
   falha de publicação traz o identificador e o tipo do evento e o motivo "sem rota".

---

### Edge Cases

- **Broker fora do ar na partida**: as filas são declaradas no primeiro canal que der
  certo, sempre antes de qualquer publicação nele; os eventos seguem pendentes até lá.
- **Broker fora do ar durante a reconciliação**: a reconciliação só grava no banco; os
  eventos de saída ficam pendentes no outbox e saem quando o broker voltar.
- **Banco fora do ar durante a reconciliação**: o ciclo termina com falha registrada em log
  e é tentado no próximo intervalo, sem derrubar o processo de workers.
- **Conflito de concorrência** (a operação cancela o pedido ou o consumidor da etapa age ao
  mesmo tempo): a reconciliação usa a mesma concorrência otimista e as mesmas reavaliações
  do consumidor. Se o pedido deixou de estar parado, termina sem gravar.
- **Falha da regra para um pedido** (por exemplo, o gateway de pagamento falha): o pedido é
  pulado no ciclo, com a falha em log, e volta a ser avaliado no ciclo seguinte, sem limite
  de ciclos e sem gravar nada no pedido. Os demais pedidos do ciclo continuam.
- **Pedido cuja mensagem foi para a DLQ** (por exemplo, falhas repetidas no consumidor): o
  pedido continua parado pelo estado e é reavaliado pela reconciliação, que aplica a mesma
  regra. O reprocessamento da mensagem na DLQ continua manual e fora do escopo.
- **Evento original ainda pendente no outbox** (broker fora por mais tempo que o limite): a
  reconciliação pode avançar o pedido antes; quando o evento original chegar, o consumidor
  o ignora (cenário 5 da US2), e o registro de duplicados do REQ-06 não é afetado.
- **Muitos pedidos parados de uma vez** (por exemplo, após a correção em produção): cada
  ciclo trata um lote limitado de pedidos, do mais antigo para o mais novo; o restante fica
  para os ciclos seguintes.
- **Pedido movido pela operação para `Processando` sem pagamento**: pela regra do REQ-09,
  ele é apto para cobrança e é cobrado pela reconciliação, como seria pelo consumidor.

## Requirements *(mandatory)*

### Functional Requirements

**Filas dos consumidores**

- **FR-001**: O processo de workers MUST declarar as filas (principal, espera e DLQ) e as
  ligações de todos os consumidores conhecidos (estoque, pagamento e entrega) antes de
  publicar qualquer evento, e de novo a cada canal de publicação aberto (por exemplo, após
  o broker voltar ou ser recriado).
- **FR-002**: A topologia declarada pelo despachante MUST ser idêntica à declarada por cada
  consumidor (mesmos nomes, ligações e argumentos), para que a declaração dos dois lados
  nunca entre em conflito.
- **FR-003**: Acrescentar um consumidor novo ao processo de workers MUST incluí-lo na
  topologia declarada antes da publicação, sem passo manual a mais.

**Publicação**

- **FR-004**: Um evento cujo tipo tem consumidor conhecido MUST ser publicado exigindo
  rota. Se o broker o devolver sem rota, a publicação MUST ser tratada como falha: o evento
  continua pendente, com a falha registrada, e segue a espera crescente e a ordem do outbox
  do REQ-06.
- **FR-005**: Um evento cujo tipo não tem consumidor conhecido MUST continuar publicado
  como hoje, sem exigir rota, e marcado como enviado após a confirmação do broker.
- **FR-006**: Um evento MUST NOT ser marcado como enviado sem a confirmação do broker e, no
  caso do FR-004, sem ter chegado a pelo menos uma fila.

**Reconciliação**

- **FR-007**: O processo de workers MUST rodar, sozinho e sem disparo manual, uma
  reconciliação periódica, com o primeiro ciclo logo após a subida, que procura
  pedidos parados nas etapas de estoque, pagamento e entrega (tabela da US2), com a última
  atualização mais antiga que um limite configurável.
- **FR-008**: Para cada pedido parado, a reconciliação MUST aplicar a mesma regra do
  consumidor da etapa, com o mesmo resultado, motivo de histórico (sem autor) e evento de
  saída, numa única gravação, com o evento saindo pelo outbox.
- **FR-009**: A reconciliação MUST ser idempotente: rodar de novo, em paralelo com outra
  instância ou com o consumidor da etapa, MUST NOT avançar o pedido mais de uma vez nem
  gerar evento de saída duplicado.
- **FR-010**: A reconciliação MUST NOT republicar eventos antigos do outbox nem depender
  deles.
- **FR-011**: Cada ciclo MUST tratar no máximo um lote configurável de pedidos, dos mais
  antigos para os mais novos. A falha de um pedido MUST NOT interromper os demais, e o
  pedido que falhou MUST ser reavaliado no ciclo seguinte, sem limite de ciclos e sem
  gravar nada no pedido. Pedidos que falham sempre MUST NOT impedir, ciclo após ciclo, que
  os demais pedidos parados sejam tratados, mesmo que ocupem o lote inteiro.
- **FR-012**: O intervalo dos ciclos, o limite de tempo parado e o tamanho do lote MUST ser
  configuráveis, com valores padrão (Assumptions) e validação na partida, como as demais
  opções de mensageria.

**Observabilidade**

- **FR-013**: Pedido recuperado (identificador do pedido, etapa e resultado), resumo do
  ciclo (total por etapa), falha de um pedido e falha do ciclo MUST ser registrados em log,
  sem dados pessoais além dos identificadores. A falha de publicação sem rota MUST trazer o
  identificador e o tipo do evento e o motivo.

**Compatibilidade**

- **FR-014**: Os contratos de eventos v1 e o contrato HTTP v1 MUST permanecer inalterados.
- **FR-015**: Os consumidores de estoque, pagamento e entrega MUST continuar com o
  comportamento atual; a reconciliação reaproveita as regras deles, sem duplicá-las.
- **FR-016**: A suíte MUST ter um teste de integração que reproduz a publicação num broker
  sem as filas dos consumidores e prova que nenhum evento com consumidor é perdido, além dos
  testes da reconciliação por etapa, todos com banco e broker reais, e MUST continuar verde
  no CI.

### Key Entities *(include if feature involves data)*

- **Evento de saída** (REQ-06): conteúdo, tipo, tentativas, último erro, próxima tentativa
  e instante de envio. Passa a ter "sem rota" como motivo de falha de publicação.
- **Consumidor conhecido**: nome, tipos de evento que recebe e filas. A lista de
  consumidores conhecidos define quais tipos de evento exigem rota.
- **Pedido** (REQ-04, REQ-08 a REQ-10): status, estoque reservado, pagamento, ordem de
  entrega e instante da última atualização, que a reconciliação usa para saber há quanto
  tempo o pedido está parado.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Num broker sem filas, 100% dos eventos com consumidor que estavam pendentes
  chegam à fila do consumidor; nenhum é marcado como enviado sem ter chegado a uma fila.
- **SC-002**: Um pedido parado por evento perdido volta ao fluxo em até o limite de tempo
  parado mais um intervalo de ciclo (com os padrões, em até 15 minutos).
- **SC-003**: Em 100% das execuções repetidas ou simultâneas da reconciliação sobre o mesmo
  pedido, ele avança uma única vez, com um único evento de saída.
- **SC-004**: Nenhum pedido que avançou há menos tempo que o limite é tocado pela
  reconciliação.
- **SC-005**: Os pedidos pagos sem ordem de entrega do REQ-10 existentes num ambiente são
  recuperados no primeiro ciclo depois da subida (até o tamanho do lote por ciclo).
- **SC-006**: O tempo do fluxo normal (do pedido criado ao "entrega criada") continua
  dentro da meta do REQ-10 (95% em até 10 segundos no ambiente de demonstração), e a suíte
  continua 100% verde no CI.

## Assumptions

- **Consumidores conhecidos** são os registrados no processo de workers: estoque ("pedido
  criado"), pagamento ("estoque reservado") e entrega ("pagamento aprovado"). Os demais
  eventos ("estoque insuficiente", "pagamento recusado" e "entrega criada") não têm
  consumidor interno e continuam publicados sem exigir rota; sistemas externos que os
  queiram declaram as próprias filas.
- **Valores padrão** da reconciliação, ajustáveis no plano: ciclo a cada 5 minutos, limite
  de 10 minutos parado e lote de 100 pedidos por ciclo. O limite fica bem acima do tempo do
  fluxo normal (segundos, REQ-10) e das novas tentativas do consumo (REQ-06), para não
  disputar com eles.
- O "tempo parado" é medido pela última atualização do pedido.
- A reconciliação roda no processo de workers, não na API, e o primeiro ciclo acontece logo
  após a subida.
- O gateway de pagamento é o simulado do REQ-09. Com um gateway real, a cobrança pela
  reconciliação exigiria a chave de idempotência na cobrança, que continua pendente.
- Pedidos parados por mensagem na DLQ também são reavaliados pelo estado; o
  reprocessamento das mensagens da DLQ continua manual e fora do escopo.
- Ficam fora do escopo:
  - a limpeza de eventos enviados, de eventos processados e de chaves de idempotência;
  - o reprocessamento da DLQ;
  - estorno, cancelamento da ordem de entrega e eventos de despacho e cancelamento;
  - alertas e métricas além dos logs.
  Esses itens continuam nas pendências futuras da spec do REQ-06 e do README.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
