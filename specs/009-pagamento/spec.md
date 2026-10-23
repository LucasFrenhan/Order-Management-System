# Feature Specification: Pagamento assíncrono simulado com compensação do estoque

**Feature Branch**: `feature/req-09-pagamento`

**Requisito**: REQ-09 (GitHub issue #9 — "[Worker] Integração com Serviço de Pagamento Assíncrono")

**Created**: 2026-10-23

**Status**: Draft (clarificada)

**Input**: User description: "REQ-09 (GitHub issue #9): consumidor no processo de workers que escuta o evento EstoqueReservado, simula o processamento do gateway de pagamento e atualiza o estado do pedido. Critérios da issue: consumidor de estoque reservado; simulação com sucesso em 90% e recusa em 10%; se aprovado, o pedido vai para Processando (a issue cita AguardandoEnvio, que não existe no contrato v1) e publica PagamentoAprovado (contrato v1 já existente); se recusado, o pedido vai para PagamentoRejeitado (status final) e publica PagamentoRecusado (contrato v1 já existente), com compensação que devolve os itens ao estoque reaproveitando a indicação de reserva do REQ-08. Base existente: REQ-04 (pagamento com situação Pendente/Aprovado/Recusado, no máximo um por pedido; transições e histórico de status), REQ-06 (base de consumo com novas tentativas, DLQ, descarte de duplicados e publicação por outbox) e REQ-08 (consumidor de estoque como modelo). A issue cita MassTransit: ignorar, cliente nativo do broker (constituição)."

## Contexto

Desde o REQ-08, todo pedido com mercadoria disponível tem o estoque reservado, e o evento
"estoque reservado" é publicado. Ninguém o consome ainda: o pedido fica em `Pendente` com
o estoque preso até alguém cancelá-lo.

Este requisito entrega o segundo consumidor de negócio, o de pagamento:

- **Cobrança simulada**: ao receber "estoque reservado", o sistema cobra o valor total do
  pedido num gateway simulado, que aprova cerca de 90% e recusa cerca de 10% das cobranças.
- **Aprovado**: o pagamento fica registrado como aprovado, o pedido passa a `Processando`
  e sai "pagamento aprovado", o gatilho do fulfillment (issue #10).
- **Recusado (compensação)**: o pagamento fica registrado como recusado, o pedido passa a
  `PagamentoRejeitado`, status final, o estoque reservado volta aos produtos e sai
  "pagamento recusado".

Os dois eventos de saída já existem como contratos v1 desde o REQ-06 e são usados sem
mudanças. A issue cita o status "AguardandoEnvio", que não existe no contrato v1: o
pagamento aprovado leva a `Processando`, como o README já registra. A issue também cita
MassTransit, mas a constituição proíbe frameworks de abstração de mensageria; o consumidor
usa a base do REQ-06, sobre o cliente nativo do broker.

## Clarifications

### Session 2026-10-23

- Q: Na recusa do pagamento, quem deve devolver o estoque reservado aos produtos? → A: O
  próprio consumidor de pagamento, na mesma gravação da recusa, reaproveitando a devolução
  do REQ-08. "pagamento recusado" só informa o resultado; não há consumidor de
  compensação separado.
- Q: Se a operação moveu o pedido para `Processando` ou `ProntoParaEnvio` antes do
  pagamento, o que o worker deve fazer? → A: Cobrar mesmo assim. Aprovado, o status não
  muda; recusado, o pedido vai para `PagamentoRejeitado` e o estoque é devolvido. Nenhum
  pedido segue sem pagamento.
- Q: Quando a operação cancela pela API um pedido com pagamento já aprovado, o que
  acontece com o pagamento? → A: Nada: o cancelamento segue como hoje e devolve o estoque
  (REQ-08), e o pagamento continua `Aprovado`. Estorno fica para um requisito futuro.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o Cliente, que espera ver o pedido andar depois de pago
ou ser avisado da recusa, quem opera o estoque, que não pode ter mercadoria presa por
pedido não pago, e o worker de fulfillment (issue #10), que só começa depois do pagamento
aprovado.

### User Story 1 - Pagamento aprovado leva o pedido a Processando (Priority: P1)

Quando "estoque reservado" chega e o gateway simulado aprova a cobrança, o pagamento do
pedido é registrado como aprovado, com o valor total do pedido e o identificador da
transação, o pedido muda de `Pendente` para `Processando`, e o evento "pagamento aprovado"
é publicado.

**Why this priority**: É o caminho principal da issue e o gatilho do resto do fluxo
(fulfillment e envio).

**Independent Test**: Com o gateway simulado configurado para sempre aprovar, criar um
pedido com produtos em estoque e conferir que, em poucos segundos, o pedido está em
`Processando`, há um pagamento aprovado com o valor total do pedido e uma fila de teste
recebeu "pagamento aprovado".

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente` com estoque reservado, **When** "estoque reservado" é
   consumido e o gateway aprova, **Then** o pedido tem um pagamento `Aprovado` com o valor
   total do pedido, um identificador de transação e o instante do processamento.
2. **Given** o mesmo cenário, **When** o pedido é consultado, **Then** ele está em
   `Processando`, e o histórico tem a entrada `Pendente → Processando` sem autor (mudança
   do sistema), com o motivo `Pagamento aprovado.`.
3. **Given** o mesmo cenário, **When** a fila de teste é lida, **Then** "pagamento
   aprovado" traz o identificador do pedido, o valor cobrado e o identificador da
   transação, iguais aos gravados no pagamento.
4. **Given** o mesmo "estoque reservado" entregue de novo depois da aprovação, **When** ele
   é consumido, **Then** não há nova cobrança, o status não muda e nenhum evento novo é
   publicado.
5. **Given** uma falha no meio do processamento (por exemplo, o banco cai), **When** o
   processamento termina com erro, **Then** nada fica gravado (nem pagamento, nem status,
   nem evento), e a mensagem é tentada de novo pela base de consumo.

---

### User Story 2 - Pagamento recusado rejeita o pedido e devolve o estoque (Priority: P1)

Quando o gateway simulado recusa a cobrança, o pagamento é registrado como recusado, o
pedido muda para `PagamentoRejeitado` (status final), o estoque reservado de cada item volta
ao produto, e o evento "pagamento recusado" é publicado com o motivo. Tudo acontece na
mesma gravação.

**Why this priority**: É a outra metade da issue e a compensação do fluxo: sem ela, a
mercadoria de um pedido não pago ficaria presa para sempre.

**Independent Test**: Com o gateway simulado configurado para sempre recusar, anotar o
estoque dos produtos, criar um pedido e conferir que ele terminou em `PagamentoRejeitado`,
que o estoque de cada produto voltou ao valor anterior ao pedido e que a fila de teste
recebeu "pagamento recusado".

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente` com estoque reservado, **When** o gateway recusa, **Then**
   o pedido tem um pagamento `Recusado`, com o valor total e o instante do processamento,
   e muda para `PagamentoRejeitado`, com o motivo da recusa no histórico e sem autor.
2. **Given** o mesmo cenário, **When** o processamento termina, **Then** o estoque de cada
   produto do pedido aumenta exatamente a quantidade do item, e o pedido deixa de ter
   estoque reservado.
3. **Given** o mesmo cenário, **When** a fila de teste é lida, **Then** "pagamento
   recusado" traz o identificador do pedido, o valor e o motivo da recusa.
4. **Given** o mesmo "estoque reservado" entregue de novo depois da recusa, **When** ele é
   consumido, **Then** nada muda: o estoque não é devolvido de novo, e nenhum evento novo é
   publicado.
5. **Given** um produto desativado depois da reserva, **When** o pagamento é recusado,
   **Then** o estoque é devolvido do mesmo jeito; o produto continua inativo.

---

### User Story 3 - Simulação do gateway com proporção e resultado controláveis (Priority: P1)

O gateway é simulado: em funcionamento normal, cerca de 90% das cobranças são aprovadas e
10% recusadas, de forma aleatória. A proporção é configurável, e os testes podem fixar o
resultado (sempre aprovar ou sempre recusar), sem depender de sorte.

**Why this priority**: A proporção de 90/10 é critério da issue, e sem resultado
controlável os testes dos dois caminhos seriam instáveis.

**Independent Test**: Executar a decisão do gateway simulado um número grande de vezes
(por exemplo, 10.000) com a configuração padrão e conferir que a taxa de aprovação fica
entre 88% e 92%. Configurar 100% e 0% e conferir que todas as cobranças são aprovadas ou
recusadas.

**Acceptance Scenarios**:

1. **Given** a configuração padrão, **When** 10.000 cobranças são decididas, **Then** entre
   88% e 92% são aprovadas.
2. **Given** a taxa de aprovação configurada em 100% (ou em 0%), **When** cobranças são
   decididas, **Then** todas são aprovadas (ou todas recusadas).
3. **Given** uma taxa de aprovação fora de 0% a 100%, **When** o processo de workers sobe,
   **Then** ele não sobe, e o erro aponta a configuração inválida sem mostrar valores
   sensíveis (como as validações do REQ-06).
4. **Given** uma cobrança aprovada, **When** o pagamento é registrado, **Then** o
   identificador da transação é único e tem no máximo 100 caracteres (limite do REQ-04).

---

### User Story 4 - Pedido que mudou antes do pagamento (Priority: P2)

Entre a reserva e o consumo de "estoque reservado", o pedido pode ter sido cancelado (pelo
Cliente ou pela operação, que já devolve o estoque, REQ-08) ou movido pela operação. O
consumidor só cobra pedidos que ainda estão com o estoque reservado, num status não final
e sem pagamento registrado. Nos demais casos, não cobra, não muda nada e não publica
evento: só registra em log o motivo.

**Why this priority**: Evita cobrar um pedido cancelado, devolver estoque que já voltou ou
cobrar duas vezes o mesmo pedido.

**Independent Test**: Com o processo de workers de pagamento parado, criar um pedido,
esperar a reserva e cancelá-lo pela API. Subir o consumo e conferir que nenhum pagamento
foi registrado, que o estoque continua como o cancelamento deixou e que nenhum evento de
pagamento saiu.

**Acceptance Scenarios**:

1. **Given** um pedido já `Cancelado` (ou `Despachado`, ou `PagamentoRejeitado`) quando
   "estoque reservado" é consumido, **When** o consumo termina, **Then** nenhum pagamento é
   registrado, nenhum estoque muda, nenhum evento é publicado, e a mensagem é confirmada
   (não vai para a DLQ), com log do status encontrado.
2. **Given** um pedido que já tem pagamento registrado, **When** "estoque reservado" é
   consumido, **Then** o comportamento é o mesmo do cenário 1.
3. **Given** um pedido movido pela operação para `Processando` ou `ProntoParaEnvio` antes
   do pagamento, com estoque reservado, **When** "estoque reservado" é consumido, **Then**
   a cobrança acontece (clarify). Aprovada, o status não muda (o pedido já passou de `Pendente`).
   Recusada, o pedido vai para `PagamentoRejeitado` e o estoque é devolvido, como na US2.
4. **Given** um "estoque reservado" de um pedido que não existe no banco, **When** ele é
   consumido, **Then** a mensagem é tratada como falha e, esgotadas as tentativas, vai
   para a DLQ com o motivo.
5. **Given** o cancelamento pela API e o pagamento do worker acontecendo ao mesmo tempo
   para o mesmo pedido, **When** os dois terminam, **Then** o resultado é um destes, nunca
   estoque devolvido duas vezes nem pagamento gravado para pedido cancelado antes da
   cobrança:
   - o cancelamento vem antes: o estoque volta pelo cancelamento, e o worker ignora o
     pedido (cenário 1);
   - o pagamento vem antes: o pedido fica `Processando` (aprovado) ou `PagamentoRejeitado`
     (recusado). No primeiro caso, o cancelamento posterior pela operação segue as regras
     do REQ-08 e devolve o estoque; no segundo, o cancelamento é recusado, porque o status
     é final.

---

### Edge Cases

- Cobrança de pedido de valor alto ou com muitos itens: o valor cobrado é sempre o valor
  total gravado no pedido, sem recálculo.
- O processo de workers cai depois de decidir a cobrança, mas antes de gravar: nada fica
  gravado, e a reentrega faz uma nova decisão. Como a simulação não tem efeito externo,
  isso não gera cobrança dupla; um gateway real exigiria chave de idempotência na cobrança
  (fora do escopo).
- O processo cai depois de gravar, mas antes de confirmar a mensagem: como o pagamento, a
  mudança de status, a devolução do estoque, o evento de saída e o registro de processado
  são gravados juntos, a reentrega é descartada como duplicada.
- Conflito de concorrência no pedido ou nos produtos (por exemplo, a API cancelando ou
  outra reserva mexendo no mesmo produto durante a devolução): o processamento é
  reavaliado com o estado atual, um número limitado de vezes, e depois segue as novas
  tentativas da base do REQ-06 e a DLQ, sem gravação pela metade.
- Broker fora do ar quando o pagamento termina: o evento fica pendente no outbox e é
  publicado quando o broker voltar (REQ-06). O resultado do pagamento não é desfeito.
- "estoque reservado" fora do contrato: vai direto para a DLQ, sem tentativa (base do
  REQ-06).
- Pedido cancelado pela operação depois do pagamento aprovado: o estoque volta (REQ-08), e
  o pagamento continua `Aprovado`, sem estorno; o estorno fica para um requisito futuro
  (clarify).

## Requirements *(mandatory)*

### Functional Requirements

**Consumo**

- **FR-001**: O processo de workers MUST ter um consumidor de pagamento, com fila e DLQ
  próprias, que recebe o evento "estoque reservado" (contrato v1 do REQ-06).
- **FR-002**: O consumidor MUST cobrar só pedidos que existem, estão com estoque reservado,
  em `Pendente`, `Processando` ou `ProntoParaEnvio`, e ainda não têm pagamento registrado.
  Nos demais casos (status final, sem reserva ou já pago), MUST confirmar a mensagem sem
  registrar pagamento, sem alterar status ou estoque e sem publicar evento, registrando em
  log o motivo.
- **FR-003**: Um pedido que não existe MUST ser tratado como falha (novas tentativas e,
  depois, DLQ).

**Gateway simulado**

- **FR-004**: A cobrança MUST ser decidida por um gateway simulado, sem chamada externa,
  que aprova cada cobrança com uma probabilidade configurável. O padrão MUST ser 90% de
  aprovação.
- **FR-005**: A taxa de aprovação MUST aceitar valores de 0% a 100%. Fora disso, o
  processo de workers MUST NOT subir, e o erro MUST apontar a configuração inválida.
- **FR-006**: O valor cobrado MUST ser o valor total gravado no pedido.
- **FR-007**: Uma cobrança aprovada MUST ter um identificador de transação único, com no
  máximo 100 caracteres. Uma cobrança recusada MUST ter um motivo legível, com no máximo
  500 caracteres; na simulação, o motivo padrão é `Pagamento recusado pelo emissor.`.

**Resultado aprovado**

- **FR-008**: Com a cobrança aprovada, o consumidor MUST, numa única gravação:
  - registrar o pagamento do pedido como `Aprovado`, com valor, identificador da transação
    e instante do processamento;
  - mudar o pedido de `Pendente` para `Processando`, com o motivo `Pagamento aprovado.` no
    histórico e sem autor. Se o pedido já estiver em `Processando` ou `ProntoParaEnvio`, o
    status MUST NOT mudar;
  - registrar o evento "pagamento aprovado" com o identificador do pedido, o valor e o
    identificador da transação.

**Resultado recusado (compensação)**

- **FR-009**: Com a cobrança recusada, o consumidor MUST, numa única gravação:
  - registrar o pagamento do pedido como `Recusado`, com valor e instante do
    processamento;
  - mudar o pedido do status atual para `PagamentoRejeitado`, com o motivo da recusa no
    histórico e sem autor;
  - devolver ao estoque de cada produto a quantidade do item e registrar que o pedido não
    tem mais estoque reservado, mesmo para produto inativo;
  - registrar o evento "pagamento recusado" com o identificador do pedido, o valor e o
    motivo.
- **FR-010**: `PagamentoRejeitado` MUST continuar sendo um status final: nenhuma transição
  pela API sai dele, inclusive o cancelamento.

**Unicidade e concorrência**

- **FR-011**: Um pedido MUST NOT ter mais de um pagamento, e o estoque MUST NOT ser
  devolvido duas vezes, mesmo com reentregas e processamento simultâneo.
- **FR-012**: O pagamento do worker e as mudanças pela API (cancelamento e PATCH do REQ-05)
  MUST ser protegidos pela concorrência otimista do pedido e dos produtos: o lado que
  perder MUST reavaliar com o estado atual, um número limitado de vezes dentro do mesmo
  processamento. Se os conflitos persistirem, o processamento MUST falhar sem gravar nada,
  para a base de consumo tentar de novo (1 + 3 tentativas) e, por fim, enviar à DLQ.

**Contratos de eventos**

- **FR-013**: Os contratos "estoque reservado", "pagamento aprovado" e "pagamento recusado"
  (v1, REQ-06) MUST ser usados sem mudanças.

**Observabilidade**

- **FR-014**: Aprovação, recusa, pedido ignorado (com o motivo) e conflito de concorrência
  MUST ser registrados em log com o identificador do evento e do pedido, o resultado e, no
  conflito, o número da reavaliação. Os logs MUST NOT conter dados pessoais além dos
  identificadores.

**Compatibilidade**

- **FR-015**: O contrato HTTP v1 MUST permanecer inalterado. A consulta de pedido continua
  mostrando o status e o histórico como hoje.
- **FR-016**: A suíte de testes MUST continuar verde, com os testes de integração do
  consumidor usando banco e broker reais e o resultado do gateway fixado, e o ambiente de
  demonstração MUST continuar subindo com o processo de workers saudável, usando a taxa
  padrão de aprovação.

### Key Entities *(include if feature involves data)*

- **Pagamento** (REQ-04): valor, situação (`Pendente`, `Aprovado` ou `Recusado`),
  identificador da transação, instante do processamento; no máximo um por pedido. É
  registrado já com o resultado.
- **Pedido** (REQ-04, REQ-08): status, itens, valor total, histórico e indicação de
  estoque reservado. O pagamento lê o valor e os itens, muda o status e, na recusa, desfaz
  a reserva.
- **Produto** (REQ-04): quantidade em estoque e versão de concorrência. Recebe o estoque
  devolvido na recusa.
- **Evento "estoque reservado"** (REQ-06): entrada do consumidor.
- **Eventos "pagamento aprovado" e "pagamento recusado"** (REQ-06): saída do consumidor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com o processo de workers e o broker no ar, 95% dos pedidos com estoque
  reservado têm o resultado do pagamento gravado e o evento correspondente publicado em até
  5 segundos após a reserva.
- **SC-002**: Com a taxa padrão, a proporção de aprovação em 10.000 decisões do gateway
  simulado fica entre 88% e 92%.
- **SC-003**: Um "estoque reservado" entregue duas vezes produz um único pagamento, uma
  única mudança de status, no máximo uma devolução de estoque e um único evento, em 100%
  dos casos.
- **SC-004**: Em 100% das recusas, o estoque de cada produto do pedido volta exatamente ao
  valor de antes da reserva (descontadas outras movimentações), e o pedido termina em
  `PagamentoRejeitado`.
- **SC-005**: Em 100% dos pedidos cancelados antes do consumo, nenhum pagamento é
  registrado e nenhum estoque é devolvido pelo consumidor.
- **SC-006**: A suíte continua 100% verde no CI, com os testes do consumidor executados
  contra banco e broker reais, e o contrato HTTP v1 e os contratos de eventos v1 não
  mudam.

## Assumptions

- O pagamento aprovado leva a `Processando`; a issue cita "AguardandoEnvio", que não existe
  no contrato v1 (README).
- A mudança de status pelo pagamento é do sistema: entra no histórico sem autor e não passa
  pelas regras de quem pode mudar o status pela API (REQ-05). As transições
  `Pendente → Processando` e `→ PagamentoRejeitado` feitas pelo sistema são próprias deste
  consumidor; a API continua sem permitir chegar a `PagamentoRejeitado`.
- A compensação (devolução do estoque) é feita pelo próprio consumidor de pagamento, na
  mesma gravação da recusa, reaproveitando a devolução do REQ-08 (clarify). O evento
  "pagamento recusado" informa o resultado; nenhum outro consumidor precisa agir para
  devolver o estoque.
- O pagamento é registrado só com o resultado final; a situação `Pendente` do REQ-04
  continua prevista, mas não é usada pela simulação, que decide na hora.
- A simulação não tem latência artificial nem falhas técnicas do gateway (indisponibilidade,
  tempo esgotado); só aprovação ou recusa. Erros técnicos de banco ou broker seguem as novas
  tentativas do REQ-06.
- Não há estorno nem nova tentativa de cobrança: um pedido recusado termina em
  `PagamentoRejeitado`, e o Cliente faz um novo pedido se quiser. O cancelamento de um
  pedido pago não altera o pagamento (clarify).
- O número de reavaliações por conflito e a forma de fixar o resultado do gateway nos
  testes são decididos no plano.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
