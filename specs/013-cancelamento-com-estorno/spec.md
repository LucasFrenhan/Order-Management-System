# Feature Specification: Cancelamento depois do pagamento, com estorno e cancelamento da ordem de entrega

**Feature Branch**: `feature/req-13-cancelamento-com-estorno`

**Requisito**: REQ-13 (GitHub issue #28 — "[Pedidos] Cancelamento depois do pagamento: estorno e cancelamento da ordem de entrega")

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "REQ-13 (issue #28): tratar o cancelamento de um pedido depois que o pagamento foi aprovado, para o Cliente nunca ficar cobrado por um pedido cancelado. (1) Estorno no cancelamento: cancelar um pedido com pagamento aprovado registra o estorno do valor no gateway (simulado, como no REQ-09) e uma situação nova no pagamento, na mesma gravação que o cancelamento e a devolução do estoque; uma falha do gateway no estorno não deixa o pedido cancelado com o pagamento ainda aprovado e sem registro (decidir no clarify: recusar o cancelamento, ou cancelar e tentar o estorno de novo). (2) Cancelar um pedido com ordem de entrega sem despacho cancela também a ordem; pedido despachado continua não podendo ser cancelado. (3) Na reconciliação, um pedido pago cuja criação da ordem falha em 3 ciclos e está parado há mais de 24 h é cancelado com estorno, sem autor e com motivo próprio. (4) A chave de idempotência da cobrança no gateway, pendente desde o REQ-09, é avaliada no clarify. Origem: clarify do REQ-09, REQ-10 e clarify do REQ-12."

## Contexto

O fluxo automático do pedido cobra o Cliente na etapa de pagamento (REQ-09) e, depois, cria a
ordem de entrega (REQ-10). A operação (Admin e OperadorLogistica) pode cancelar o pedido pela
mudança de status enquanto ele está em `Pendente`, `Processando` ou `ProntoParaEnvio`; o
Cliente só cancela o próprio pedido em `Pendente` (REQ-05).

Hoje, cancelar depois do pagamento deixa três pontas soltas:

- o estoque volta aos produtos (REQ-08), mas o pagamento continua `Aprovado`, **sem estorno**:
  o Cliente fica cobrado por um pedido cancelado;
- a ordem de entrega de um pedido cancelado em `ProntoParaEnvio` continua registrada, sem
  despacho, como se ainda fosse sair;
- a reconciliação do REQ-12 nunca cancela um pedido pago cuja ordem de entrega falha sempre,
  justamente porque cancelar sem estorno deixaria o Cliente cobrado. Ele segue sendo tentado
  a cada ciclo, no fim da fila.

Este requisito fecha as três pontas: todo cancelamento de pedido pago estorna o valor, todo
cancelamento de pedido com ordem de entrega sem despacho cancela a ordem, e a etapa de entrega
passa a se esgotar como as etapas de estoque e de pagamento.

## Clarifications

### Session 2026-10-24 (specify)

- Q: Na falha do gateway no estorno, o cancelamento é recusado ou o pedido é cancelado e o
  estorno tentado de novo? → A: O pedido é cancelado na hora, com o estorno registrado como
  pendente na mesma gravação; o processo de workers pede o estorno ao gateway e tenta de novo
  sozinho até a confirmação. O cancelamento não depende do gateway.
- Q: A chave de idempotência da cobrança entra agora ou continua pendente? → A: Entra agora.
  A chave é o pedido: a mesma cobrança repetida devolve o mesmo resultado e a mesma transação,
  sem nova cobrança. O estorno usa a mesma ideia.

### Session 2026-10-24 (clarify)

- Q: Quando o gateway aprova uma cobrança, mas o pedido foi cancelado antes de o pagamento ser
  gravado, o que acontece? → A: O pagamento é registrado no pedido cancelado, com a transação
  da cobrança e o estorno pendente; o processo de workers estorna como nos demais casos
  (FR-006). Uma recusa nesse caso continua descartada.
- Q: A consulta do pedido mostra a situação do estorno? → A: Não. As situações "estorno
  pendente" e "estornado" ficam internas, como `NaoProcessado`; a operação acompanha pelos
  logs (FR-016), e a visibilidade para outros sistemas segue na issue #29 (FR-018).
- Q: Quem pode cancelar um pedido já pago, agora que o cancelamento gera estorno? → A: Não
  muda: Admin e OperadorLogistica cancelam em `Processando` e `ProntoParaEnvio`; o Cliente só
  em `Pendente` (403 depois, como hoje). O autor fica no histórico.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o Cliente, que não pode ficar cobrado por um pedido que não
vai receber; a operação, que cancela pedidos pagos e precisa confiar que o estorno foi feito; e
a logística, que não pode despachar um pedido cancelado.

### User Story 1 - Cancelar um pedido pago estorna o valor (Priority: P1)

A operação cancela um pedido com pagamento aprovado, em `Processando` ou `ProntoParaEnvio`. O
pedido é cancelado na hora, com o estoque devolvido e o estorno registrado como pendente; logo
depois, o processo de workers pede o estorno ao gateway e, confirmado, o pagamento passa para
"estornado". Se o gateway falhar, o estorno é tentado de novo sozinho até dar certo.

**Why this priority**: É o objetivo da issue: o Cliente nunca fica cobrado por um pedido
cancelado.

**Independent Test**: Com um pedido pago em `Processando`, cancelar pela API como operação e
conferir que o pedido está `Cancelado`, o estoque voltou e o estorno está pendente; com o
processo de workers no ar, conferir que o gateway recebeu um único estorno do valor cobrado e
o pagamento está "estornado".

**Acceptance Scenarios**:

1. **Given** um pedido em `Processando` com pagamento aprovado, **When** a operação o cancela
   com um motivo, **Then**, numa única gravação e sem chamar o gateway, o pagamento passa para
   "estorno pendente", o estoque volta aos produtos, e o pedido vai para `Cancelado` com o
   autor e o motivo no histórico; a resposta é a de hoje (200 com o pedido cancelado).
2. **Given** o pedido do cenário 1, **When** o processo de workers trata o estorno pendente,
   **Then** o gateway estorna o valor cobrado, referente à transação da cobrança, e o pagamento
   passa para "estornado", com o identificador do estorno e o instante.
3. **Given** um pedido em `ProntoParaEnvio` com pagamento aprovado e ordem de entrega sem
   despacho, **When** a operação o cancela, **Then** acontece o mesmo dos cenários 1 e 2, e a
   ordem de entrega também é cancelada (US2), na mesma gravação do cancelamento.
4. **Given** um pedido sem pagamento (`Pendente`, ou `Processando` movido pela operação sem
   cobrança), **When** ele é cancelado, **Then** nenhum estorno é registrado e o cancelamento
   segue como hoje.
5. **Given** um pedido com o pagamento recusado (`PagamentoRejeitado`), **When** alguém tenta
   cancelá-lo, **Then** a resposta é a de hoje (409, estado final), e nada é estornado.
6. **Given** um estorno pendente e o gateway falhando ou sem responder, **When** o processo de
   workers tenta o estorno, **Then** o pagamento continua "estorno pendente", a falha vai para
   o log, e o estorno é tentado de novo na rodada seguinte, sem limite, até a confirmação; o
   pedido continua `Cancelado`.
7. **Given** um estorno que continua falhando há mais de 24 h e em pelo menos 3 tentativas,
   **When** o processo de workers tenta de novo e falha, **Then** a falha vai para o log como
   erro, para a operação agir, e o estorno continua sendo tentado.
8. **Given** dois cancelamentos do mesmo pedido pago ao mesmo tempo, **When** os dois
   terminam, **Then** o pedido é cancelado uma vez, com um único estorno pendente e um único
   estorno no gateway; o outro recebe a resposta de hoje para um pedido já cancelado (409).
7. **Given** o Cliente dono de um pedido pago, **When** ele tenta cancelá-lo, **Then** a
   resposta continua 403, como hoje: depois do pagamento, cancelar é com a operação.

---

### User Story 2 - Cancelar um pedido com ordem de entrega cancela a ordem (Priority: P1)

Cancelar um pedido que tem ordem de entrega ainda sem despacho cancela também a ordem, para
que a logística não despache um pedido cancelado. Um pedido despachado continua não podendo
ser cancelado.

**Why this priority**: Sem isso, a consulta e a logística veem uma ordem de entrega ativa num
pedido cancelado.

**Independent Test**: Com um pedido em `ProntoParaEnvio` com ordem de entrega, cancelar pela
API e conferir que a ordem está cancelada, com o instante, e que a consulta do pedido a mostra
assim.

**Acceptance Scenarios**:

1. **Given** um pedido em `ProntoParaEnvio` com ordem de entrega sem despacho, **When** a
   operação o cancela, **Then** a ordem é marcada como cancelada, com o instante do
   cancelamento, na mesma gravação do cancelamento do pedido.
2. **Given** o pedido do cenário 1 cancelado, **When** alguém consulta o pedido, **Then** a
   entrega aparece com o instante do cancelamento da ordem, e sem transportadora, código ou
   data de despacho.
3. **Given** um pedido cancelado com ordem cancelada, **When** a operação tenta registrar o
   rastreio, **Then** a resposta é a de hoje para pedido cancelado (409), e a ordem não muda.
4. **Given** um pedido `Despachado`, **When** alguém tenta cancelá-lo, **Then** a resposta
   continua 409 "O pedido está em Despachado e não pode passar para Cancelado.", e nada é
   estornado.
5. **Given** um pedido cancelado sem ordem de entrega, **When** o "pagamento aprovado" dele
   chega depois ao consumidor de entrega, **Then** nenhuma ordem é criada, como hoje.

---

### User Story 3 - A etapa de entrega se esgota na reconciliação, com estorno (Priority: P2)

Um pedido pago cuja criação da ordem de entrega falha em 3 ciclos da reconciliação e que está
parado há mais de 24 h é cancelado pela reconciliação, sem autor, com o motivo
`Não foi possível criar a ordem de entrega.`, com o estorno pendente e a devolução do estoque,
como as etapas de estoque e de pagamento já se esgotam no REQ-12. O estorno segue o mesmo
caminho da US1.

**Why this priority**: Fecha a última etapa que a reconciliação não consegue encerrar; até
aqui, esses pedidos ficavam tentados para sempre.

**Independent Test**: Com um pedido pago sem ordem, parado há mais de 24 h, e a criação da
ordem forçada a falhar, rodar 3 ciclos da reconciliação e conferir que, no terceiro, o pedido
está `Cancelado` sem autor e com o motivo, o estoque voltou e o estorno está pendente; em
seguida, que o pagamento fica "estornado".

**Acceptance Scenarios**:

1. **Given** um pedido pago sem ordem, parado há mais de 24 h, cuja criação da ordem já falhou
   em 3 ciclos, **When** a reconciliação roda, **Then**, numa única gravação, o pagamento passa
   para "estorno pendente", o estoque volta, e o pedido vai para `Cancelado`, sem autor, com o
   motivo `Não foi possível criar a ordem de entrega.`, sem evento de saída; o estorno segue
   como nos cenários 2, 6 e 7 da US1.
2. **Given** um pedido da etapa de entrega parado há mais de 24 h que falhou só 1 ou 2 vezes,
   **When** a reconciliação roda, **Then** ele não é cancelado: é tentado de novo.
3. **Given** um pedido da etapa de entrega esgotado, **When** o gateway está fora do ar,
   **Then** o cancelamento acontece mesmo assim, porque não depende do gateway, e só o estorno
   espera.
4. **Given** um pedido esgotado e cancelado, **When** o log é lido, **Then** há o mesmo
   registro de erro de pedido esgotado do REQ-12, com a etapa de entrega.
5. **Given** um pedido da etapa de entrega que saiu da etapa entre a falha e o esgotamento
   (por exemplo, a operação o despachou), **When** a reconciliação roda, **Then** ele não é
   cancelado nem estornado.

---

### User Story 4 - O gateway não cobra nem estorna duas vezes a mesma coisa (Priority: P2)

A cobrança e o estorno no gateway podem ser repetidos (reentrega da mensagem, nova tentativa,
reavaliação por conflito, reconciliação) sem cobrar ou estornar o Cliente duas vezes, para que
o fluxo continue seguro quando o gateway simulado for trocado por um real.

**Why this priority**: O estorno e a cobrança pela reconciliação repetem chamadas ao gateway;
com um gateway real, cada repetição sem proteção seria dinheiro a mais ou a menos.

**Independent Test**: Pedir ao gateway simulado a mesma cobrança, e depois o mesmo estorno,
duas vezes para o mesmo pedido, e conferir que a segunda resposta repete a primeira, sem nova
transação.

**Acceptance Scenarios**:

1. **Given** uma cobrança já feita para um pedido, **When** ela é repetida com a mesma chave
   (o pedido), **Then** o gateway devolve o mesmo resultado (aprovação ou recusa) e a mesma
   transação, sem cobrar de novo.
2. **Given** um estorno já feito para um pedido, **When** ele é pedido de novo para o mesmo
   pedido, **Then** o gateway devolve o mesmo estorno, sem estornar duas vezes.
3. **Given** o processo de workers cai depois de o gateway confirmar um estorno e antes de
   gravar, **When** o estorno pendente é tratado de novo, **Then** o gateway devolve o mesmo
   estorno, e o pagamento passa para "estornado" uma única vez.
4. **Given** a reconciliação cobra um pedido cuja cobrança pelo consumidor já tinha sido
   aprovada no gateway sem chegar a ser gravada, **When** o gateway recebe a mesma chave,
   **Then** devolve a aprovação original, sem segunda cobrança.

---

### Edge Cases

- **Cancelamento ao mesmo tempo que a cobrança**: se o cancelamento grava primeiro, o
  consumidor de pagamento encontra o pedido final e não cobra, como hoje. Se a cobrança grava
  primeiro, o cancelamento relê o pedido pago e registra o estorno pendente.
- **Cobrança aprovada para um pedido cancelado no meio do caminho**: o consumidor de pagamento
  chamou o gateway, que aprovou, mas, ao gravar, encontra o pedido já `Cancelado`. Hoje o
  resultado é descartado, o que, com um gateway real, deixaria o Cliente cobrado sem registro.
  Nesse caso, o pagamento MUST ser registrado no pedido cancelado com a transação da cobrança e
  o estorno pendente, e o estorno segue como na US1. Uma recusa nesse caso continua descartada.
- **Conflito de concorrência no cancelamento**: o cancelamento não chama o gateway, então
  reavaliar não tem efeito externo; ele segue as 3 reavaliações de hoje.
- **Cancelamento ao mesmo tempo que o despacho**: um dos dois vence, como hoje (REQ-11). Se o
  despacho vence, o cancelamento responde 409 e nada é estornado; se o cancelamento vence, o
  despacho responde 409 e a ordem fica cancelada.
- **Cancelamento ao mesmo tempo que a criação da ordem pelo consumidor de entrega**: se o
  cancelamento vence, o consumidor encontra o pedido final e não cria a ordem; se a ordem é
  criada primeiro, o cancelamento relê o pedido e cancela a ordem também.
- **Pedido com pagamento `NaoProcessado`** (esgotado na reconciliação do REQ-12): o pedido já
  está `Cancelado`; não há o que estornar.
- **Pedido despachado pela rota de status sem rastreio**: está `Despachado` e não pode ser
  cancelado, como hoje.
- **Gateway fora do ar por muito tempo**: os cancelamentos continuam acontecendo; os estornos
  ficam pendentes e são tentados a cada rodada, com erro no log depois de 24 h e 3 tentativas
  (US1, cenário 7), até o gateway voltar.
- **Reconciliação e cancelamento pela API ao mesmo tempo sobre um pedido esgotado**: um dos
  dois vence pela concorrência otimista; há um único estorno pendente e um único estorno no
  gateway.
- **Banco fora do ar no cancelamento**: nada é gravado, como hoje; o gateway não foi chamado.
- **Banco fora do ar ao gravar o estorno confirmado**: o pagamento continua "estorno
  pendente"; na rodada seguinte, o gateway devolve o mesmo estorno (US4), sem estornar duas
  vezes.
- **Dois processos de workers tratando o mesmo estorno pendente**: o gateway devolve o mesmo
  estorno para os dois, e o pagamento passa para "estornado" uma única vez.
- **Processo de workers fora do ar**: o cancelamento pela API continua funcionando; os
  estornos ficam pendentes até ele voltar.

## Requirements *(mandatory)*

### Functional Requirements

**Estorno**

- **FR-001**: Cancelar um pedido com pagamento `Aprovado` MUST, numa única gravação e sem
  chamar o gateway, passar o pagamento para a situação "estorno pendente", devolver o estoque
  reservado aos produtos e levar o pedido a `Cancelado`, com o autor e o motivo informados (ou
  sem autor e com o motivo da reconciliação, no FR-011). O pedido MUST NOT ficar `Cancelado`
  com o pagamento `Aprovado`.
- **FR-002**: O processo de workers MUST tratar os estornos pendentes sozinho, sem disparo
  manual: pedir ao gateway o estorno do valor cobrado, referente à transação da cobrança, e,
  confirmado, passar o pagamento para "estornado", com o identificador do estorno e o
  instante.
- **FR-003**: Na falha, recusa ou falta de resposta do gateway, o pagamento MUST continuar
  "estorno pendente", e o estorno MUST ser tentado de novo nas rodadas seguintes, sem limite
  de tentativas. Um estorno pendente há mais que o limite de esgotamento do REQ-12 (padrão
  24 h) e com pelo menos 3 tentativas falhas MUST gerar um registro de erro a cada nova falha,
  para a operação agir, sem deixar de ser tentado.
- **FR-004**: Cancelar um pedido sem pagamento aprovado (sem pagamento, `Recusado` ou
  `NaoProcessado`) MUST NOT registrar estorno.
- **FR-005**: O estorno MUST ser feito uma única vez por pedido, mesmo com cancelamentos
  simultâneos, rodadas repetidas, dois processos de workers ou a reconciliação ao mesmo tempo
  que a API.
- **FR-006**: Uma cobrança aprovada pelo gateway cujo pedido foi cancelado antes da gravação
  MUST ser registrada no pedido cancelado, com a transação da cobrança e o estorno pendente.
- **FR-007**: O primeiro pedido de estorno de um cancelamento MUST acontecer em até um
  intervalo de rodada configurável depois do cancelamento, com valor padrão (Assumptions) e
  validação na partida, como as demais opções do processo de workers.

**Ordem de entrega**

- **FR-008**: Cancelar um pedido com ordem de entrega sem despacho MUST cancelar a ordem, com o
  instante, na mesma gravação do cancelamento do pedido.
- **FR-009**: A consulta do pedido MUST mostrar, na entrega, o instante do cancelamento da
  ordem, como campo novo e opcional, sem mudar os campos que já existem.
- **FR-010**: As regras de hoje MUST continuar valendo: pedido `Despachado` não pode ser
  cancelado (409), pedido cancelado não recebe rastreio (409) e o consumidor de entrega não
  cria ordem para pedido cancelado.

**Reconciliação**

- **FR-011**: Um pedido da etapa de entrega MUST se esgotar com os mesmos limites do REQ-12
  (parado há mais que o limite de esgotamento, padrão 24 h, **e** pelo menos 3 ciclos com
  falha, contados em memória). Esgotado, ele MUST ser cancelado sem autor, com o motivo
  `Não foi possível criar a ordem de entrega.`, com o estorno pendente (FR-001 a FR-003) e a
  devolução do estoque, e sem evento de saída.
- **FR-012**: O esgotamento da etapa de entrega MUST reler o pedido e conferir que ele ainda
  está na etapa antes de cancelar, como as demais etapas no REQ-12.

**Gateway**

- **FR-013**: O estorno no gateway MUST ser idempotente por pedido: pedir de novo o estorno de
  um pedido já estornado MUST devolver o mesmo estorno, sem estornar outra vez.
- **FR-014**: A cobrança no gateway MUST usar uma chave de idempotência por pedido: repetir a
  cobrança de um pedido (reentrega, nova tentativa ou reconciliação) MUST devolver o mesmo
  resultado e a mesma transação, sem cobrar de novo. O consumidor de pagamento e a
  reconciliação MUST continuar com o comportamento atual.
- **FR-015**: O gateway simulado MUST honrar as duas chaves e permitir, nos testes, forçar a
  aprovação, a recusa e a falha do estorno, como já permite na cobrança, sem depender de
  sorte.

**Observabilidade**

- **FR-016**: Estorno pendente registrado (identificador do pedido e origem: cancelamento pela
  operação, reconciliação ou cobrança aprovada para pedido cancelado), estorno confirmado
  (identificador do pedido e valor), falha do estorno (com o número de tentativas), estorno
  pendente além do limite (erro) e ordem de entrega cancelada MUST ser registrados em log, sem
  dados pessoais nem identificadores de transação.

**Compatibilidade**

- **FR-017**: Os contratos de eventos v1 MUST permanecer inalterados, e nenhum evento novo é
  publicado (a visibilidade dos cancelamentos fica na issue #29). O contrato HTTP v1 MUST
  mudar só de forma compatível: o campo opcional do FR-009; as respostas do cancelamento não
  mudam.
- **FR-018**: As situações "estorno pendente" e "estornado" do pagamento MUST ser internas,
  como `NaoProcessado`: não aparecem na API nem nos eventos.
- **FR-019**: A suíte MUST ter testes de integração, com banco e broker reais, do
  cancelamento de pedido pago (com e sem ordem de entrega), do estorno pendente tratado pelo
  processo de workers, da falha e da nova tentativa do estorno, das disputas de concorrência
  das Edge Cases, da idempotência da cobrança e do estorno e do esgotamento da etapa de
  entrega, além de testes unitários das regras novas do domínio, e MUST continuar verde no CI.

### Key Entities *(include if feature involves data)*

- **Pagamento** (REQ-04, REQ-09, REQ-12): ganha as situações "estorno pendente" e
  "estornado", ao lado de pendente, aprovado, recusado e "não foi possível processar", com o
  identificador do estorno no gateway, o instante do estorno e as tentativas de estorno
  (quantas falharam e quando foi a última).
- **Estorno**: a devolução do valor cobrado de um pedido, feita no gateway pelo processo de
  workers, no máximo uma por pedido, referente à transação da cobrança, com o pedido como chave
  de idempotência.
- **Chave de idempotência da cobrança**: o pedido; a mesma chave devolve sempre o mesmo
  resultado da cobrança.
- **Ordem de entrega** (REQ-10, REQ-11): ganha o instante do cancelamento. Uma ordem cancelada
  não pode ser despachada.
- **Pedido** (REQ-04 em diante): o cancelamento passa a levar junto o estorno pendente e o
  cancelamento da ordem; os status e as transições não mudam.
- **Contagem de falhas da reconciliação** (REQ-12): passa a valer também para o esgotamento da
  etapa de entrega.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 100% dos cancelamentos de pedidos pagos, o pedido termina `Cancelado` com o
  estorno pendente ou feito; nenhum pedido fica `Cancelado` com pagamento `Aprovado`.
- **SC-002**: Com o gateway e o processo de workers no ar, o estorno é confirmado em até um
  intervalo de rodada depois do cancelamento (com o padrão, em até 1 minuto).
- **SC-003**: Em 100% das execuções repetidas ou simultâneas do cancelamento, da cobrança ou do
  estorno de um mesmo pedido, o gateway cobra e estorna o valor no máximo uma vez.
- **SC-004**: Nenhum pedido cancelado mantém uma ordem de entrega ativa, e nenhum pedido
  cancelado é despachado.
- **SC-005**: Nenhum pedido da etapa de entrega fica na reconciliação por mais que o limite de
  esgotamento mais 3 ciclos com o processo de workers no ar (com os padrões, cerca de 24 h e
  15 min).
- **SC-006**: O cancelamento de um pedido pago pela operação responde no mesmo tempo de hoje e
  continua funcionando com o gateway ou o processo de workers fora do ar.
- **SC-007**: O tempo do fluxo normal (do pedido criado ao "entrega criada") continua dentro da
  meta do REQ-10 (95% em até 10 segundos no ambiente de demonstração), e a suíte continua 100%
  verde no CI.

## Assumptions

- O gateway é o simulado do REQ-09. O estorno simulado aprova sempre, a menos que os testes
  forcem recusa ou falha; não tem efeito externo. A memória das chaves de idempotência no
  simulado pode se perder quando o processo reinicia, o que é aceitável só porque a simulação
  não tem efeito externo; um gateway real guarda as chaves do lado dele.
- **Valores padrão**, ajustáveis no plano: estornos pendentes tratados a cada 1 minuto, num
  lote limitado por rodada, do mais antigo para o mais novo; alerta de erro com 24 h e 3
  tentativas falhas, os mesmos números do esgotamento do REQ-12.
- O estorno é tratado pelo processo de workers, não pela API, para o cancelamento não depender
  do gateway (decisão do specify, princípio IV da constituição).
- Quem pode cancelar não muda: a operação cancela em `Pendente`, `Processando` e
  `ProntoParaEnvio`; o Cliente só em `Pendente`, quando ainda não há cobrança.
- O estorno é sempre do valor total cobrado; estorno parcial e cancelamento de parte dos itens
  ficam fora do escopo.
- Uma recusa do estorno pelo gateway (resposta válida, mas negativa) é tratada como a falha do
  FR-003; o motivo vai para o log.
- O motivo do esgotamento da etapa de entrega segue o padrão do REQ-12:
  `Não foi possível criar a ordem de entrega.`.
- O estorno pendente que não se confirma nunca é abandonado: o dinheiro do Cliente não pode
  ficar retido por desistência do sistema. Não há rota para a operação forçar ou dar baixa
  num estorno; o registro de erro (FR-003) é o sinal para agir fora do sistema.
- As situações de estorno e o cancelamento da ordem não geram evento; outros sistemas ficarem
  sabendo dos cancelamentos é a issue #29.
- Ficam fora do escopo: estorno parcial, cancelamento de pedido despachado (devolução),
  eventos de cancelamento e de estorno (#29), limpeza e reprocessamento da DLQ (#30) e um
  gateway real.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
