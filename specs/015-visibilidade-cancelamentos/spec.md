# Feature Specification: Visibilidade dos cancelamentos e dos estornos para outros sistemas

**Feature Branch**: `feature/req-15-visibilidade-cancelamentos`

**Requisito**: REQ-15 (GitHub issue #29 — "[Mensageria] Visibilidade dos cancelamentos de pedido para outros sistemas")

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "REQ-15 (issue #29) — Visibilidade dos cancelamentos e estornos para outros sistemas. Decisões já tomadas pelo usuário: (1) evento único `pedido.cancelado` com campo `motivo` (Cliente, Operacao, EstoqueInsuficiente, PagamentoRecusado, ReconciliacaoEsgotada), emitido a partir de `Pedido.Cancelar`, o único caminho de cancelamento; (2) o evento sai também nos casos que já têm evento próprio; `estoque.insuficiente` e `pagamento.recusado` continuam como hoje; (3) dados: id do pedido, número, motivo, data, se o estoque foi devolvido, se há estorno pendente, sem dados pessoais; o estorno concluído sai num evento separado `pagamento.estornado`, assíncrono, quando o Worker conclui o estorno; (4) mudanças de status em geral (ex.: `pedido.despachado`) ficam fora, para outra issue; (5) tipos novos são acréscimo na v1 do contrato de eventos, com schema versionado e teste de conformidade. Os eventos saem pelo outbox, na mesma gravação do cancelamento/estorno."

## Contexto

Um pedido chega a `Cancelado` por cinco caminhos hoje, e só um deles avisa outros sistemas:

| Caminho | Origem | Evento hoje |
|---------|--------|-------------|
| O Cliente cancela o próprio pedido em `Pendente` | API (REQ-05) | nenhum |
| A operação (Admin ou OperadorLogistica) cancela em `Pendente`, `Processando` ou `ProntoParaEnvio`, com estorno se pago | API (REQ-05, REQ-13) | nenhum |
| Falta estoque na reserva | consumidor de estoque (REQ-08) | `estoque.insuficiente` |
| A reconciliação esgota a etapa de estoque ou de pagamento | reconciliação (REQ-12) | nenhum |
| A reconciliação esgota a etapa de entrega, com estorno | reconciliação (REQ-13) | nenhum |

O pagamento recusado não cancela o pedido: ele termina em `PagamentoRejeitado`, outro status
final, e já sai `pagamento.recusado` (REQ-09).

O estorno do REQ-13 também é invisível de fora: o pagamento passa a "estornado" dentro do
sistema, e a consulta e os eventos não mostram isso (REQ-13, FR-018).

Este requisito faz todo cancelamento e todo estorno concluído saírem pela mensageria, para que
outros sistemas (financeiro, atendimento, logística externa) fiquem sabendo sem consultar a
API.

## Clarifications

### Session 2026-10-26 (specify)

- Q: Um evento único ou um por causa? → A: Um evento único, `pedido.cancelado`, com o motivo
  em categoria.
- Q: O evento sai também quando já existe um evento próprio? → A: Sim. `estoque.insuficiente`
  continua saindo como hoje, e `pedido.cancelado` sai junto.
- Q: Quais dados? → A: Pedido, número, motivo, instante, se o estoque foi devolvido e se há
  estorno pendente, sem dados pessoais. O estorno concluído sai depois, num evento próprio,
  `pagamento.estornado`.
- Q: Mudanças de status em geral (por exemplo, `pedido.despachado`) entram? → A: Não; ficam
  para outra issue.
- Q: Versão do contrato? → A: Os dois tipos são acréscimo na v1 (Princípio II), com schema e
  teste de conformidade.
- Q: O pagamento recusado (`PagamentoRejeitado`) gera `pedido.cancelado`? → A: Não.
  `pedido.cancelado` sai só quando o pedido vai a `Cancelado`; o pedido com pagamento recusado
  vai para `PagamentoRejeitado`, outro status final, e continua avisado só por
  `pagamento.recusado`. O motivo `PagamentoRecusado` da decisão inicial sai da lista.
- Q: `Pedido.Cancelar` é o único caminho de cancelamento? → A: Não (correção feita na leitura
  do código): a falta de estoque e o esgotamento das etapas de estoque e de pagamento chegam a
  `Cancelado` por outros caminhos. O evento vale para toda chegada a `Cancelado` (FR-001).

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são os sistemas de fora do OMS que precisam reagir ao fim de um
pedido: o financeiro, que concilia cobranças e estornos; o atendimento, que avisa o Cliente; e
a logística externa, que não pode separar um pedido cancelado. A operação também se beneficia,
porque passa a ter um registro de cada cancelamento na mensageria.

### User Story 1 - Todo cancelamento sai como `pedido.cancelado` (Priority: P1)

Qualquer que seja o caminho que leva o pedido a `Cancelado` (Cliente, operação, falta de
estoque ou reconciliação), sai um único `pedido.cancelado`, na mesma gravação do cancelamento,
com o motivo em categoria, se o estoque foi devolvido e se o estorno ficou pendente.

**Why this priority**: É o objetivo da issue: hoje, quatro dos cinco caminhos cancelam em
silêncio.

**Independent Test**: Cancelar pedidos por cada caminho (Cliente, operação com e sem
pagamento, falta de estoque e esgotamento de cada etapa da reconciliação) e conferir, numa fila
ligada ao exchange de eventos, um `pedido.cancelado` por pedido, com o motivo, o estoque e o
estorno certos, e válido pelo schema.

**Acceptance Scenarios**:

1. **Given** um pedido do Cliente em `Pendente`, **When** o Cliente o cancela pela API,
   **Then**, na mesma gravação do cancelamento, sai `pedido.cancelado` com o motivo `Cliente`,
   o estoque devolvido conforme havia reserva e sem estorno pendente.
2. **Given** um pedido em `Processando` com pagamento aprovado, **When** a operação o cancela,
   **Then** sai `pedido.cancelado` com o motivo `Operacao`, o estoque devolvido e o estorno
   pendente.
3. **Given** um pedido em `ProntoParaEnvio` com ordem de entrega, **When** a operação o
   cancela, **Then** sai `pedido.cancelado` com o motivo `Operacao`, o estoque devolvido, o
   estorno pendente e a indicação de que a ordem de entrega foi cancelada.
4. **Given** um pedido cujo estoque falta, **When** o consumidor de estoque o cancela, **Then**
   saem, na mesma gravação, `estoque.insuficiente` (como hoje) e `pedido.cancelado` com o motivo
   `EstoqueInsuficiente`, sem estoque devolvido e sem estorno pendente.
5. **Given** um pedido esgotado na etapa de estoque, **When** a reconciliação o cancela,
   **Then** sai `pedido.cancelado` com o motivo `ReconciliacaoEsgotada` e a etapa `Estoque`.
6. **Given** um pedido esgotado na etapa de pagamento, **When** a reconciliação o cancela,
   **Then** sai `pedido.cancelado` com o motivo `ReconciliacaoEsgotada`, a etapa `Pagamento`, o
   estoque devolvido e sem estorno pendente (não houve cobrança).
7. **Given** um pedido pago esgotado na etapa de entrega, **When** a reconciliação o cancela,
   **Then** sai `pedido.cancelado` com o motivo `ReconciliacaoEsgotada`, a etapa `Entrega`, o
   estoque devolvido e o estorno pendente.
8. **Given** dois cancelamentos simultâneos do mesmo pedido, **When** os dois terminam,
   **Then** sai um único `pedido.cancelado`; o perdedor responde como hoje (409) e não gera
   evento.
9. **Given** um cancelamento que falha ao gravar (banco fora do ar ou conflito esgotado),
   **When** a operação termina, **Then** nenhum `pedido.cancelado` é publicado.

---

### User Story 2 - O estorno concluído sai como `pagamento.estornado` (Priority: P1)

Quando o processo de workers confirma o estorno no gateway (REQ-13), sai `pagamento.estornado`
na mesma gravação da confirmação, com o valor estornado e as referências da cobrança e do
estorno, para o financeiro conciliar.

**Why this priority**: O cancelamento de um pedido pago só termina para o Cliente quando o
dinheiro volta; sem esse evento, ninguém de fora sabe quando isso aconteceu.

**Independent Test**: Cancelar um pedido pago, deixar o processo de workers confirmar o
estorno e conferir um único `pagamento.estornado`, válido pelo schema, com o valor cobrado.

**Acceptance Scenarios**:

1. **Given** um pedido cancelado com estorno pendente, **When** o processo de workers confirma
   o estorno, **Then**, na mesma gravação da confirmação, sai `pagamento.estornado` com o
   pedido, o número, o valor, a transação da cobrança, o identificador do estorno e o instante.
2. **Given** um estorno que falhou e foi tentado de novo, **When** ele finalmente é confirmado,
   **Then** sai um único `pagamento.estornado`; as falhas não geram evento.
3. **Given** um estorno confirmado no gateway cujo registro falha ao gravar, **When** a rodada
   seguinte recebe do gateway o mesmo estorno (REQ-13, idempotência), **Then** sai um único
   `pagamento.estornado`, na gravação que deu certo.
4. **Given** uma cobrança aprovada para um pedido já cancelado (REQ-13, FR-006), **When** o
   estorno é confirmado, **Then** sai `pagamento.estornado` normalmente, embora o
   `pedido.cancelado` desse pedido tenha saído sem estorno pendente.
5. **Given** um pedido com estorno pendente desde antes deste requisito, **When** o estorno é
   confirmado depois da implantação, **Then** sai `pagamento.estornado`.

---

### User Story 3 - Outros sistemas confiam no contrato dos eventos novos (Priority: P2)

Os dois tipos novos têm schema versionado na v1, publicado junto aos demais, sem dados
pessoais, e os consumidores existentes não percebem mudança.

**Why this priority**: Sem contrato publicado, cada sistema de fora interpretaria os eventos
por conta própria (Princípio II).

**Independent Test**: Validar exemplos dos dois tipos contra os schemas e conferir que os
schemas e o conteúdo dos tipos existentes não mudaram.

**Acceptance Scenarios**:

1. **Given** os schemas dos tipos novos, **When** um evento real de cada tipo é validado,
   **Then** ele passa, e um evento sem campo obrigatório ou com motivo fora da lista é
   recusado.
2. **Given** os consumidores internos de estoque, pagamento e entrega, **When** os eventos
   novos circulam, **Then** nenhum deles os recebe, e o comportamento deles não muda.
3. **Given** um `pedido.cancelado` ou `pagamento.estornado`, **When** ele é lido, **Then** não
   traz nome, e-mail, documento, endereço, o identificador do Cliente nem o texto livre do
   motivo digitado na API.

---

### Edge Cases

- **Cancelamento e cobrança ao mesmo tempo**: se o cancelamento vence, sai `pedido.cancelado`
  sem estorno pendente, e a cobrança aprovada depois é registrada com estorno (REQ-13, FR-006);
  o fim dessa história chega por `pagamento.estornado`. Se a cobrança vence, o cancelamento
  relê o pedido pago e sai `pedido.cancelado` com estorno pendente.
- **Reconciliação e API cancelando o mesmo pedido**: um dos dois vence pela concorrência
  otimista, e sai um único `pedido.cancelado`, com o motivo de quem venceu.
- **Reentrega da mensagem de estoque**: o pedido já está `Cancelado`; o consumidor o ignora,
  como hoje, e nenhum `pedido.cancelado` repetido sai.
- **Nenhum sistema de fora escutando**: os tipos novos não têm consumidor interno; como os
  demais tipos nessa situação (REQ-12), saem sem rota obrigatória e são marcados como enviados
  mesmo sem fila ligada. Quem quer recebê-los liga a própria fila ao exchange.
- **Broker fora do ar**: o cancelamento e a confirmação do estorno gravam normalmente; os
  eventos ficam pendentes no outbox e saem quando o broker volta, como os demais.
- **Pedido cancelado antes deste requisito**: não ganha `pedido.cancelado` retroativo.
- **Pedido em `PagamentoRejeitado`**: não é cancelamento; só `pagamento.recusado` sai, como
  hoje (FR-005). Quem quer saber de todo pedido que termina sem entrega escuta os dois tipos.
- **Admin que também é Cliente cancelando o próprio pedido**: o motivo segue o perfil usado na
  autorização; quem tem perfil de operação cancela como `Operacao`.

## Requirements *(mandatory)*

### Functional Requirements

**Cancelamento**

- **FR-001**: Toda mudança de um pedido para `Cancelado`, por qualquer caminho, MUST gerar
  exatamente um evento `pedido.cancelado`, gravado no outbox na mesma gravação do
  cancelamento. Um cancelamento que não grava MUST NOT gerar evento.
- **FR-002**: `pedido.cancelado` MUST trazer: o identificador e o número do pedido; o motivo em
  categoria (`Cliente`, `Operacao`, `EstoqueInsuficiente` ou `ReconciliacaoEsgotada`); a etapa
  esgotada (`Estoque`, `Pagamento` ou `Entrega`), só quando o motivo é
  `ReconciliacaoEsgotada`; o instante do cancelamento; se o estoque reservado foi devolvido;
  se o estorno ficou pendente; e se uma ordem de entrega foi cancelada.
- **FR-003**: O motivo MUST ser `Cliente` quando o cancelamento pela API é feito por um
  usuário sem perfil de operação, e `Operacao` quando é feito por Admin ou OperadorLogistica.
- **FR-004**: Os eventos que já existem (`estoque.insuficiente`, `pagamento.recusado`) MUST
  continuar saindo como hoje, sem mudança de conteúdo; `pedido.cancelado` sai junto quando o
  pedido vai a `Cancelado`.
- **FR-005**: O pedido que termina em `PagamentoRejeitado` MUST NOT gerar `pedido.cancelado`:
  ele continua avisado só por `pagamento.recusado`, como hoje.

**Estorno**

- **FR-006**: A confirmação de um estorno pelo processo de workers MUST gerar exatamente um
  evento `pagamento.estornado`, gravado no outbox na mesma gravação da confirmação. Falhas e
  novas tentativas de estorno MUST NOT gerar evento.
- **FR-007**: `pagamento.estornado` MUST trazer: o identificador e o número do pedido, o valor
  estornado, o identificador da transação da cobrança, o identificador do estorno no gateway e
  o instante do estorno.

**Contrato**

- **FR-008**: Os dois tipos MUST ser acréscimos à v1 do contrato de eventos, no mesmo envelope
  dos demais, com schema publicado junto aos existentes e testes de serialização e de
  conformidade de schema (Princípio II e V).
- **FR-009**: Os schemas e o conteúdo dos tipos existentes MUST permanecer inalterados, e os
  consumidores internos MUST NOT receber os tipos novos.
- **FR-010**: Os eventos novos MUST NOT trazer dados pessoais, o identificador do Cliente nem o
  texto livre do motivo digitado na API (que pode conter qualquer coisa).
- **FR-011**: O contrato HTTP v1 MUST NOT mudar.

**Observabilidade e testes**

- **FR-012**: Os logs de cancelamento e de estorno existentes MUST continuar; a publicação dos
  tipos novos segue os logs do despachante, sem log novo obrigatório.
- **FR-013**: A suíte MUST ter testes de integração, com banco e broker reais, de cada caminho
  de cancelamento (Cliente, operação com e sem pagamento, falta de estoque e esgotamento das
  três etapas), da confirmação do estorno, das disputas de concorrência das Edge Cases e da
  falha na gravação, além de testes unitários das regras novas, e MUST continuar verde no CI.

### Key Entities *(include if feature involves data)*

- **`pedido.cancelado`** (evento novo, v1): o fim de um pedido por cancelamento, com o motivo em
  categoria, a etapa esgotada quando é a reconciliação, e o que foi desfeito junto (estoque,
  estorno pendente, ordem de entrega).
- **`pagamento.estornado`** (evento novo, v1): a devolução do valor cobrado de um pedido
  cancelado, confirmada pelo gateway, com as referências da cobrança e do estorno.
- **Motivo do cancelamento**: categoria derivada do caminho do cancelamento; o texto livre do
  histórico de status continua só na API.
- **Outbox** (REQ-06): passa a receber os dois tipos novos, nas mesmas gravações do
  cancelamento e da confirmação do estorno.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos pedidos que chegam a `Cancelado` depois da implantação têm exatamente um
  `pedido.cancelado` no outbox, e nenhum pedido não cancelado tem.
- **SC-002**: 100% dos estornos confirmados depois da implantação têm exatamente um
  `pagamento.estornado`.
- **SC-003**: Com o broker e o despachante no ar, os eventos novos ficam disponíveis para
  outros sistemas no mesmo tempo dos demais eventos (em até alguns segundos no ambiente de
  demonstração).
- **SC-004**: 100% dos eventos novos publicados nos testes são válidos pelo schema.
- **SC-005**: Os consumidores existentes e os contratos HTTP e de eventos v1 continuam
  compatíveis (verificação de contrato sem quebra), e a suíte continua 100% verde no CI.
- **SC-006**: O tempo de resposta do cancelamento pela API e o tempo do fluxo normal do pedido
  continuam dentro das metas dos REQ-05 e REQ-10.

## Assumptions

- Os tipos novos não têm consumidor interno: são publicados para sistemas de fora, que ligam
  as próprias filas ao exchange de eventos. Sem fila ligada, a mensagem é descartada pelo
  broker e o outbox a marca como enviada, como os demais tipos sem consumidor (REQ-12).
- O motivo em categoria substitui, no evento, o texto livre do histórico; o texto continua na
  consulta do pedido pela API. A categoria nasce do caminho do cancelamento, não do texto.
- O "instante do cancelamento" é o mesmo instante gravado no histórico de status; o envelope
  já traz o instante em que o evento ocorreu.
- Indicar se a ordem de entrega foi cancelada é um acréscimo às decisões iniciais, para a
  logística externa não precisar inferir pela etapa; o custo é um campo booleano.
- Um `pedido.cancelado` sem estorno pendente pode ser seguido de `pagamento.estornado` (cobrança
  aprovada para pedido já cancelado, REQ-13, FR-006). Quem reconcilia dinheiro deve se guiar por
  `pagamento.estornado`.
- Não há envio retroativo: pedidos cancelados antes da implantação não geram evento; estornos
  pendentes de antes geram `pagamento.estornado` quando confirmados.
- Ficam fora do escopo: eventos para outras mudanças de status (`pedido.despachado` e
  similares, issue futura), eventos de falha de estorno, estorno parcial e consumidor interno
  dos eventos novos.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
