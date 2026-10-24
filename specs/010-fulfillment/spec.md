# Feature Specification: Ordem de entrega assíncrona para pedidos pagos

**Feature Branch**: `feature/req-10-fulfillment`

**Requisito**: REQ-10 (GitHub issue #10 — "[Worker] Serviço de Fulfillment e Logística (Envio)")

**Created**: 2026-10-23

**Status**: Draft

**Input**: User description: "REQ-10 (GitHub issue #10): consumidor no processo de workers que escuta o pagamento aprovado e gera a ordem de entrega do pedido. Critérios da issue: consumidor de pagamento aprovado; criação do registro de ordem de entrega vinculado ao pedido (no máximo uma por pedido; transportadora, código de rastreio e data de despacho só chegam com o despacho, da issue #11); atualização do status para ProntoParaEnvio; publicação de um evento de pedido despachável (contrato novo, versionado, nome no passado). Base: REQ-06 (base de consumo com novas tentativas, DLQ, descarte de duplicados e publicação por outbox), REQ-08 e REQ-09 como modelo de consumidor. A issue cita MassTransit: ignorar, cliente nativo do broker (constituição)."

## Contexto

Desde o REQ-09, todo pedido com estoque reservado é cobrado. Com o pagamento aprovado, o
pedido fica em `Processando` e o evento "pagamento aprovado" é publicado, mas ninguém o
consome: não há registro de logística, e o pedido só avança se a operação o mover à mão.

Este requisito entrega o terceiro consumidor de negócio, o de fulfillment:

- **Ordem de entrega**: ao receber "pagamento aprovado", o sistema cria a ordem de entrega
  do pedido, ainda sem transportadora nem código de rastreio. Esses dados chegam com o
  despacho, que é da issue #11.
- **Pronto para envio**: o pedido passa de `Processando` para `ProntoParaEnvio`.
- **Aviso para a logística**: sai o evento "entrega criada", um contrato novo, que avisa que
  o pedido pode ser separado e despachado.

A issue cita "PedidoDespachávelEvent (ou equivalente)". A constituição pede eventos no
passado, como `pedido.criado`; por isso o evento se chama "entrega criada". A issue também
cita MassTransit, mas a constituição proíbe frameworks de abstração de mensageria; o
consumidor usa a base do REQ-06, sobre o cliente nativo do broker.

## Clarifications

### Session 2026-10-23

- Q: O que fazer com os pedidos já pagos antes de a fila de fulfillment existir, cujo
  "pagamento aprovado" não será reconsumido? → A: Fora do escopo; fica como premissa e vai
  para o REQ futuro de mensageria (evento publicado antes de a fila do consumidor existir).

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são a equipe de logística, que precisa saber quais pedidos
estão prontos para separar e despachar, o Cliente, que vê o pedido avançar depois de pago,
e o despacho com rastreio (issue #11), que parte da ordem de entrega criada aqui.

### User Story 1 - Pedido pago ganha a ordem de entrega e fica pronto para envio (Priority: P1)

Quando "pagamento aprovado" chega para um pedido em `Processando`, o sistema cria a ordem de
entrega do pedido, sem transportadora, código de rastreio nem data de despacho, muda o
pedido para `ProntoParaEnvio` e publica "entrega criada".

**Why this priority**: É o caminho principal da issue e o ponto de partida do despacho
(issue #11).

**Independent Test**: Com o pagamento fixado para aprovar, criar um pedido com produtos em
estoque e conferir que, em poucos segundos, o pedido está em `ProntoParaEnvio`, tem uma
ordem de entrega sem dados de despacho e uma fila de teste recebeu "entrega criada".

**Acceptance Scenarios**:

1. **Given** um pedido em `Processando` com pagamento aprovado e estoque reservado,
   **When** "pagamento aprovado" é consumido, **Then** o pedido tem uma ordem de entrega,
   sem transportadora, sem código de rastreio e sem data de despacho, com o instante da
   criação.
2. **Given** o mesmo cenário, **When** o pedido é consultado, **Then** ele está em
   `ProntoParaEnvio`, e o histórico tem a entrada `Processando → ProntoParaEnvio` sem autor
   (mudança do sistema), com o motivo `Ordem de entrega criada.`.
3. **Given** o mesmo cenário, **When** a fila de teste é lida, **Then** "entrega criada"
   traz o identificador do pedido, o número do pedido, o identificador da ordem de entrega e
   os itens (produto e quantidade).
4. **Given** o mesmo "pagamento aprovado" entregue de novo depois da criação, **When** ele é
   consumido, **Then** nenhuma ordem nova é criada, o status não muda e nenhum evento novo é
   publicado.
5. **Given** uma falha no meio do processamento (por exemplo, o banco cai), **When** o
   processamento termina com erro, **Then** nada fica gravado (nem ordem, nem status, nem
   evento), e a mensagem é tentada de novo pela base de consumo.

---

### User Story 2 - Contrato "entrega criada" publicado e verificável (Priority: P1)

O evento "entrega criada" é um contrato novo, versionado (versão 1), com o envelope comum
dos eventos do sistema e schema publicado no repositório, para que a logística e outros
consumidores dependam dele com segurança.

**Why this priority**: A publicação do evento é critério da issue, e a constituição exige
contrato versionado com schema e testes.

**Independent Test**: Serializar um evento "entrega criada" de exemplo, validar contra o
schema publicado e conferir que exemplos fora do contrato são recusados.

**Acceptance Scenarios**:

1. **Given** o contrato "entrega criada" v1, **When** um evento é serializado, **Then** ele
   passa na validação do schema e volta igual na desserialização.
2. **Given** um evento sem o identificador da ordem, com itens vazios ou com quantidade
   menor que 1, **When** ele é validado, **Then** o schema o recusa.
3. **Given** a mensagem publicada pelo consumidor, **When** ela é validada, **Then** passa no
   schema.

---

### User Story 3 - Pedido que mudou antes do fulfillment (Priority: P2)

Entre o pagamento e o consumo de "pagamento aprovado", a operação pode ter cancelado o
pedido (o estoque volta, REQ-08) ou movido o pedido pela API. O consumidor só cria a ordem
de pedidos aptos: pagamento aprovado, estoque reservado, sem ordem de entrega e em
`Processando` ou `ProntoParaEnvio`. Nos demais casos, não cria nada, não muda o status e não
publica evento: só registra em log o motivo.

**Why this priority**: Evita mandar para a logística um pedido cancelado ou já despachado, e
evita duas ordens para o mesmo pedido.

**Independent Test**: Com o consumidor de fulfillment parado, deixar um pedido ser pago e
cancelá-lo pela operação. Subir o consumo e conferir que nenhuma ordem foi criada, o status
continua `Cancelado` e nenhum "entrega criada" saiu.

**Acceptance Scenarios**:

1. **Given** um pedido `Cancelado`, `Despachado` ou `PagamentoRejeitado` quando "pagamento
   aprovado" é consumido, **When** o consumo termina, **Then** nenhuma ordem é criada, nada
   muda, nenhum evento é publicado, e a mensagem é confirmada (não vai para a DLQ), com log
   do motivo.
2. **Given** um pedido que já tem ordem de entrega, ou sem pagamento aprovado, ou sem
   estoque reservado, **When** "pagamento aprovado" é consumido, **Then** o comportamento é
   o mesmo do cenário 1.
3. **Given** um pedido movido pela operação para `ProntoParaEnvio` antes do fulfillment,
   com pagamento aprovado e estoque reservado, **When** "pagamento aprovado" é consumido,
   **Then** a ordem de entrega é criada e "entrega criada" é publicado, sem mudar o status.
4. **Given** um "pagamento aprovado" de um pedido que não existe no banco, **When** ele é
   consumido, **Then** a mensagem é tratada como falha e, esgotadas as tentativas, vai para
   a DLQ com o motivo.
5. **Given** o cancelamento pela operação e o fulfillment acontecendo ao mesmo tempo para o
   mesmo pedido, **When** os dois terminam, **Then** o resultado é um destes, nunca um
   pedido cancelado com "entrega criada" publicado:
   - o cancelamento vem antes: o estoque volta, e o consumidor ignora o pedido;
   - o fulfillment vem antes: o pedido fica `ProntoParaEnvio` com a ordem, e o
     cancelamento posterior pela operação segue as regras do REQ-08 (devolve o estoque).

---

### Edge Cases

- O processo cai depois de gravar, mas antes de confirmar a mensagem: como a ordem, a
  mudança de status, o evento de saída e o registro de processado são gravados juntos, a
  reentrega é descartada como duplicada.
- Conflito de concorrência no pedido (por exemplo, a operação cancelando ou movendo o pedido
  durante o fulfillment): o processamento é reavaliado com o estado atual, um número
  limitado de vezes, e depois segue as novas tentativas da base do REQ-06 e a DLQ, sem
  gravação pela metade.
- Pedido cancelado pela operação depois de `ProntoParaEnvio`: o cancelamento continua
  permitido (contrato v1) e devolve o estoque (REQ-08). A ordem de entrega continua
  registrada, sem dados de despacho; o despacho de pedido cancelado é recusado pelas regras
  de status, e o cancelamento da ordem não é tratado neste requisito.
- Pedido movido pela operação direto para `Despachado` antes do fulfillment: é status
  final, e o consumidor o ignora; a ordem para esse caso fica com a issue #11.
- Broker fora do ar quando o fulfillment termina: o evento fica pendente no outbox e é
  publicado quando o broker voltar (REQ-06). A ordem não é desfeita.
- "pagamento aprovado" fora do contrato: vai direto para a DLQ, sem tentativa (base do
  REQ-06).
- Pedido pago antes de a fila de fulfillment existir: o "pagamento aprovado" dele foi
  descartado pelo broker e não é reconsumido, então o pedido fica em `Processando` sem
  ordem de entrega. Este requisito não recupera esses pedidos (ver Assumptions).

## Requirements *(mandatory)*

### Functional Requirements

**Consumo**

- **FR-001**: O processo de workers MUST ter um consumidor de fulfillment, com fila e DLQ
  próprias, que recebe o evento "pagamento aprovado" (contrato v1 do REQ-06).
- **FR-002**: O consumidor MUST criar a ordem de entrega só para pedidos que existem, têm
  pagamento aprovado, estoque reservado, nenhuma ordem de entrega e estão em `Processando`
  ou `ProntoParaEnvio`. Nos demais casos, MUST confirmar a mensagem sem criar ordem, sem
  mudar status e sem publicar evento, registrando em log o motivo.
- **FR-003**: Um pedido que não existe MUST ser tratado como falha (novas tentativas e,
  depois, DLQ).

**Ordem de entrega**

- **FR-004**: Para um pedido apto, o consumidor MUST, numa única gravação:
  - criar a ordem de entrega do pedido, sem transportadora, código de rastreio e data de
    despacho, com o instante da criação;
  - mudar o pedido de `Processando` para `ProntoParaEnvio`, com o motivo
    `Ordem de entrega criada.` no histórico e sem autor. Se o pedido já estiver em
    `ProntoParaEnvio`, o status MUST NOT mudar;
  - registrar o evento "entrega criada".
- **FR-005**: Um pedido MUST NOT ter mais de uma ordem de entrega, mesmo com reentregas e
  processamento simultâneo.
- **FR-006**: O estoque e o pagamento MUST NOT ser alterados pelo fulfillment.

**Concorrência**

- **FR-007**: O fulfillment e as mudanças pela API (cancelamento e PATCH do REQ-05) MUST ser
  protegidos pela concorrência otimista do pedido: o lado que perder MUST reavaliar com o
  estado atual, um número limitado de vezes dentro do mesmo processamento. Se os conflitos
  persistirem, o processamento MUST falhar sem gravar nada, para a base de consumo tentar de
  novo (1 + 3 tentativas) e, por fim, enviar à DLQ.

**Contrato de evento**

- **FR-008**: O sistema MUST definir o contrato versionado (versão 1) "entrega criada", com
  o envelope comum do REQ-06 e, no conteúdo:
  - identificador do pedido;
  - número do pedido;
  - identificador da ordem de entrega;
  - itens do pedido, cada um com produto e quantidade.
- **FR-009**: O contrato "entrega criada" MUST ser publicado como schema no repositório, nas
  mesmas convenções dos contratos do REQ-06 e do REQ-08, e MUST ter testes de serialização,
  ida e volta e recusa de mensagem fora do schema.
- **FR-010**: O contrato "pagamento aprovado" do REQ-06 MUST ser usado sem mudanças.

**Observabilidade**

- **FR-011**: Criação da ordem, pedido ignorado (com o motivo) e conflito de concorrência
  MUST ser registrados em log com o identificador do evento e do pedido, o resultado e, no
  conflito, o número da reavaliação. Os logs MUST NOT conter dados pessoais além dos
  identificadores.

**Compatibilidade**

- **FR-012**: O contrato HTTP v1 MUST permanecer inalterado. A consulta de pedido continua
  mostrando o status como hoje; expor a ordem e o rastreio na consulta é da issue #11.
- **FR-013**: A suíte de testes MUST continuar verde, com os testes de integração do
  consumidor usando banco e broker reais, e o ambiente de demonstração MUST continuar
  subindo com o processo de workers saudável.

### Key Entities *(include if feature involves data)*

- **Ordem de entrega** (REQ-04): pedido, transportadora, código de rastreio, data de
  despacho e instantes de criação e atualização; no máximo uma por pedido. É criada aqui sem
  os dados de despacho.
- **Pedido** (REQ-04, REQ-08, REQ-09): status, itens, histórico, indicação de estoque
  reservado e pagamento. O fulfillment lê o pagamento, a reserva e os itens, cria a ordem e
  muda o status.
- **Evento "pagamento aprovado"** (REQ-06): entrada do consumidor.
- **Evento "entrega criada"** (novo): pedido, número do pedido, ordem de entrega e itens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com o processo de workers e o broker no ar, 95% dos pedidos com pagamento
  aprovado têm a ordem de entrega gravada, o status `ProntoParaEnvio` e "entrega criada"
  publicado em até 5 segundos após o pagamento.
- **SC-002**: Um "pagamento aprovado" entregue duas vezes produz uma única ordem de entrega,
  uma única mudança de status e um único evento, em 100% dos casos.
- **SC-003**: Em 100% dos pedidos cancelados antes do consumo, nenhuma ordem de entrega é
  criada e nenhum "entrega criada" é publicado.
- **SC-004**: O contrato "entrega criada" tem testes de serialização, ida e volta e recusa
  de mensagem inválida, como os contratos do REQ-06 e do REQ-08.
- **SC-005**: Do pedido criado ao "entrega criada", o fluxo completo (estoque, pagamento
  aprovado e fulfillment) termina em até 10 segundos em 95% dos pedidos aprovados no
  ambiente de demonstração.
- **SC-006**: A suíte continua 100% verde no CI, com os testes do consumidor executados
  contra banco e broker reais, e o contrato HTTP v1 não muda.

## Assumptions

- O nome do evento segue a regra da constituição (passado): "entrega criada"
  (`entrega.criada`), equivalente ao "PedidoDespachávelEvent" da issue.
- A mudança para `ProntoParaEnvio` é do sistema: entra no histórico sem autor e não passa
  pelas regras de quem pode mudar o status pela API (REQ-05), mas é uma transição já
  prevista no contrato v1 (`Processando → ProntoParaEnvio`).
- A ordem de entrega nasce sem transportadora nem código de rastreio. O despacho, com
  transportadora, rastreio e status `Despachado`, é da issue #11, que também decide como
  expor a ordem na consulta.
- O evento não traz dados pessoais nem endereço: o sistema não guarda endereço de entrega
  hoje. A logística consulta o pedido pelos identificadores.
- O cancelamento da ordem de entrega (por exemplo, quando o pedido pronto para envio é
  cancelado) fica fora do escopo; a ordem continua registrada sem despacho.
- O número de reavaliações por conflito é decidido no plano, como no REQ-08 e no REQ-09.
- Pedidos pagos antes de a fila de fulfillment existir não são recuperados por este
  requisito: não há varredura na partida nem reenvio do "pagamento aprovado". A perda de
  evento publicado antes de a fila do consumidor existir é tratada num REQ futuro de
  mensageria. Até lá, a operação pode mover esses pedidos pela API, mas eles continuam sem
  ordem de entrega. **Atualização**: o [REQ-12](../012-publicacao-garantida/spec.md)
  recupera esses pedidos pela reconciliação, que cria a ordem que falta.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
