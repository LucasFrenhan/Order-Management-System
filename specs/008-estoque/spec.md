# Feature Specification: Reserva de estoque assíncrona com concorrência otimista

**Feature Branch**: `feature/req-08-estoque`

**Requisito**: REQ-08 (GitHub issue #8 — "[Worker] Processamento Assíncrono de Estoque e Concorrência Otimista")

**Created**: 2026-10-22

**Status**: Draft (clarificada)

**Input**: User description: "REQ-08 (GitHub issue #8): consumidor no processo de workers que escuta o evento PedidoCriado, verifica a disponibilidade de cada item e reserva o estoque de forma transacional. Critérios da issue: consumidor de PedidoCriado; para cada item verifica quantidade suficiente; se todos têm estoque, abate usando concorrência otimista (tratando conflitos de atualização simultânea) e publica EstoqueReservado; se faltar estoque, muda o pedido para Cancelado e publica o evento de estoque insuficiente (contrato novo). Base existente: REQ-04 (produtos com estoque e concorrência otimista, catálogo com casos reservados para testes) e REQ-06 (base de consumo com novas tentativas, DLQ, descarte de duplicados e publicação por outbox). A issue cita MassTransit: ignorar, cliente nativo do broker (constituição)."

## Contexto

Hoje, quando um Cliente cria um pedido (REQ-05), o sistema confere se os produtos estão
ativos e têm estoque, mas não reserva nada: dois pedidos podem disputar a última unidade, e
os dois são aceitos. O REQ-06 passou a publicar o evento "pedido criado" e entregou a base
de consumo, mas ainda não há nenhum consumidor de negócio.

Este requisito entrega o primeiro deles, o de estoque:

- **Reserva**: ao receber "pedido criado", o estoque de todos os itens é abatido de uma vez,
  ou de nenhum.
- **Disputa**: atualizações simultâneas do mesmo produto não deixam o estoque negativo nem
  reservam a mesma unidade duas vezes (concorrência otimista, já mapeada no REQ-04).
- **Resultado publicado**: com a reserva feita, sai "estoque reservado", que é o gatilho do
  pagamento (issue #9). Sem estoque, o pedido é cancelado e sai "estoque insuficiente", um
  contrato novo.

A issue cita MassTransit, mas a constituição proíbe frameworks de abstração de mensageria.
O consumidor usa a base do REQ-06, sobre o cliente nativo do broker.

## Clarifications

### Session 2026-10-22

- Q: Quando um pedido que já teve o estoque reservado é cancelado (pelo Cliente ou pela
  operação, via PATCH), o estoque deve voltar neste REQ? → A: Sim, na API. O pedido passa a
  registrar que tem estoque reservado, e o cancelamento pela API devolve o estoque na mesma
  gravação. A concorrência otimista do pedido resolve a corrida entre o cancelamento e a
  reserva do worker. A devolução na recusa do pagamento continua na issue #9, reaproveitando
  esse registro.
- Q: Se a operação mover o pedido de `Pendente` para `Processando` ou `ProntoParaEnvio`
  (PATCH) antes de o worker reservar o estoque, o que o worker deve fazer? → A: Reservar
  mesmo assim. O worker reserva todo pedido ainda sem reserva em `Pendente`, `Processando`
  ou `ProntoParaEnvio` e, sem estoque, o cancela. Só os status finais (`Cancelado`,
  `Despachado` e `PagamentoRejeitado`) são ignorados.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o Cliente, que não pode ter um pedido aceito sem
mercadoria, quem opera o estoque e o worker de pagamento (issue #9), que só começa depois
da reserva.

### User Story 1 - Pedido com estoque tem a mercadoria reservada (Priority: P1)

Quando "pedido criado" chega e todos os itens têm estoque suficiente, o estoque de cada
produto é abatido da quantidade pedida, e o evento "estoque reservado" é publicado com os
itens reservados. O pedido continua `Pendente`, aguardando o pagamento.

**Why this priority**: É o caminho principal da issue e o gatilho do resto do fluxo
assíncrono (pagamento → entrega).

**Independent Test**: Criar um pedido com produtos em estoque e conferir que, em poucos
segundos, o estoque de cada produto caiu exatamente a quantidade pedida e que uma fila de
teste recebeu "estoque reservado" com os itens do pedido.

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente` cujos itens têm estoque suficiente, **When** "pedido
   criado" é consumido, **Then** o estoque de cada produto diminui exatamente a quantidade
   do item, e "estoque reservado" é publicado com o identificador do pedido e os itens
   (produto e quantidade).
2. **Given** a reserva feita, **When** o pedido é consultado, **Then** ele continua
   `Pendente`. A mudança para `Processando` é do pagamento (issue #9).
3. **Given** o mesmo "pedido criado" entregue de novo depois da reserva, **When** ele é
   consumido, **Then** o estoque não é abatido de novo, e nenhum evento novo é publicado.
4. **Given** uma falha no meio da reserva (por exemplo, o banco cai), **When** o
   processamento termina com erro, **Then** nenhum produto fica com o estoque abatido e
   nenhum evento sai. A mensagem é tentada de novo pela base de consumo.

---

### User Story 2 - Pedido sem estoque é cancelado e avisado (Priority: P1)

Quando algum item não tem estoque suficiente no momento do consumo, ou o produto foi
desativado depois da criação do pedido, nada é reservado: o pedido muda para `Cancelado`,
com o motivo no histórico, e o evento "estoque insuficiente" é publicado com os itens que
faltaram.

**Why this priority**: É a outra metade do critério da issue. Sem ela, um pedido sem
mercadoria ficaria parado em `Pendente` para sempre.

**Independent Test**: Criar um pedido e, antes do consumo, zerar ou reduzir o estoque de
um dos produtos (ou criar dois pedidos disputando o mesmo item). Conferir que o pedido
ficou `Cancelado`, que nenhum estoque foi abatido e que a fila de teste recebeu "estoque
insuficiente".

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente` em que pelo menos um item tem estoque menor que a
   quantidade pedida, **When** "pedido criado" é consumido, **Then** nenhum produto do
   pedido tem o estoque alterado, o pedido muda para `Cancelado` com o motivo
   `Estoque insuficiente.` no histórico, sem autor (mudança do sistema), e "estoque insuficiente" é
   publicado.
2. **Given** um item cujo produto foi desativado depois da criação do pedido, **When** o
   pedido é consumido, **Then** ele é tratado como sem estoque, como no cenário 1.
3. **Given** o evento "estoque insuficiente", **When** ele é lido, **Then** traz o
   identificador do pedido e, para cada item que faltou, o produto, a quantidade pedida, a
   quantidade disponível naquele momento e se o produto estava inativo.
4. **Given** o mesmo "pedido criado" entregue de novo depois do cancelamento, **When** ele
   é consumido, **Then** nada muda, e nenhum evento novo é publicado.

---

### User Story 3 - Disputa pelo mesmo produto não vende o que não existe (Priority: P1)

Vários pedidos consumidos ao mesmo tempo, disputando o mesmo produto, nunca levam o
estoque abaixo de zero nem reservam mais unidades do que existem. Quando duas reservas
tentam atualizar o mesmo produto ao mesmo tempo, a que perde reavalia com o estoque atual:
reserva, se ainda houver, ou cancela o pedido, se não houver mais.

**Why this priority**: É o "concorrência otimista" do título da issue e o risco de
negócio central: vender a mesma unidade duas vezes.

**Independent Test**: Usar o produto do catálogo com estoque 1 (`CAT-008`), criar dois
pedidos de 1 unidade e consumir os dois em paralelo. Conferir que exatamente um ficou
reservado e o outro cancelado, e que o estoque terminou em 0.

**Acceptance Scenarios**:

1. **Given** um produto com estoque 1 e dois pedidos de 1 unidade consumidos ao mesmo
   tempo, **When** os dois terminam, **Then** exatamente um pedido tem "estoque reservado",
   o outro é cancelado com "estoque insuficiente", e o estoque fica em 0.
2. **Given** um produto com estoque N e M pedidos simultâneos de 1 unidade (M > N),
   **When** todos terminam, **Then** exatamente N são reservados, M − N são cancelados, e
   o estoque fica em 0, nunca negativo.
3. **Given** um conflito de atualização simultânea, **When** a reserva que perdeu é
   reavaliada, **Then** ela usa o estoque atual do banco, não o que tinha lido antes.
4. **Given** conflitos que persistem (disputa muito intensa), **When** as reavaliações do
   próprio processamento se esgotam, **Then** a mensagem volta para a base de consumo e é
   tentada de novo (até 1 + 3 vezes), e só então vai para a DLQ. Nenhum estoque fica
   abatido pela metade.

---

### User Story 4 - Pedido que mudou de status antes da reserva (Priority: P2)

Entre a criação e o consumo, o pedido pode ter sido cancelado pelo Cliente ou movido pela
operação (PATCH do REQ-05). Se ele está num status final (`Cancelado`, `Despachado` ou
`PagamentoRejeitado`), o consumidor não reserva nada, não muda o status e não publica
evento: só registra em log o motivo. Se ele foi movido para `Processando` ou
`ProntoParaEnvio`, a reserva acontece normalmente, como em `Pendente` (clarify).

**Why this priority**: Evita reservar mercadoria para um pedido cancelado, o que deixaria
estoque preso, e evita que um pedido movido à mão siga sem mercadoria reservada.

**Independent Test**: Com o processo de workers parado, criar dois pedidos: cancelar um
pela API e mover o outro para `Processando`. Subir o processo e conferir que o cancelado
não alterou estoque nem gerou evento, e que o movido teve o estoque reservado e
"estoque reservado" publicado.

**Acceptance Scenarios**:

1. **Given** um pedido já `Cancelado` quando "pedido criado" é consumido, **When** o
   consumo termina, **Then** nenhum estoque muda, nenhum evento é publicado, e a mensagem é
   confirmada (não vai para a DLQ).
2. **Given** um pedido `Despachado` ou `PagamentoRejeitado`, **When** "pedido criado" é
   consumido, **Then** o comportamento é o mesmo do cenário 1, com log do status
   encontrado.
3. **Given** um pedido movido para `Processando` ou `ProntoParaEnvio` antes da reserva,
   **When** "pedido criado" é consumido, **Then** o estoque é reservado e "estoque
   reservado" é publicado, sem mudar o status. Sem estoque, o pedido é cancelado e
   "estoque insuficiente" é publicado, como na US2.
4. **Given** um "pedido criado" de um pedido que não existe no banco, **When** ele é
   consumido, **Then** a mensagem é tratada como falha e, esgotadas as tentativas, vai para
   a DLQ com o motivo.

---

### User Story 5 - Cancelar um pedido reservado devolve o estoque (Priority: P2)

Quando um pedido com estoque reservado é cancelado pela API (pelo Cliente dono, enquanto
`Pendente`, ou pela operação, em qualquer status que ainda permite cancelar), o estoque de
cada item volta ao produto na mesma gravação do cancelamento. Um pedido sem reserva é
cancelado como hoje, sem mexer em estoque.

**Why this priority**: Sem a devolução, cada cancelamento depois da reserva deixaria
mercadoria presa, que nenhum outro pedido poderia comprar (clarify).

**Independent Test**: Criar um pedido, esperar a reserva, cancelar pela API e conferir que
o estoque de cada produto voltou ao valor de antes da reserva. Repetir com um pedido
cancelado antes da reserva e conferir que o estoque não mudou.

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente` com estoque reservado, **When** o Cliente dono o cancela,
   **Then** a resposta é a mesma de hoje (contrato v1), o pedido fica `Cancelado`, e o
   estoque de cada produto aumenta exatamente a quantidade do item.
2. **Given** um pedido com estoque reservado em `Processando` ou `ProntoParaEnvio`, **When**
   a operação o cancela, **Then** o estoque é devolvido como no cenário 1.
3. **Given** um pedido sem estoque reservado (ainda não consumido, ou cancelado por falta
   de estoque), **When** ele é cancelado, **Then** nenhum estoque muda.
4. **Given** o cancelamento pela API e a reserva do worker acontecendo ao mesmo tempo para
   o mesmo pedido, **When** os dois terminam, **Then** o resultado é um destes dois, nunca
   estoque preso nem devolvido sem reserva:
   - o cancelamento vem antes: nada é reservado, e o worker ignora o pedido (US4);
   - a reserva vem antes: o cancelamento devolve o estoque reservado.
5. **Given** um produto desativado depois da reserva, **When** o pedido é cancelado, **Then**
   o estoque é devolvido do mesmo jeito; o produto continua inativo.

---

### Edge Cases

- Pedido com vários itens em que só um falta: nada é reservado (tudo ou nada), e "estoque
  insuficiente" lista só os itens que faltaram.
- Item com quantidade exatamente igual ao estoque: é reservado, e o estoque fica em 0.
- Produto desativado depois da reserva: a reserva já feita continua valendo.
- Estoque reposto depois do cancelamento por falta: o pedido continua `Cancelado`; não há
  nova tentativa automática.
- O pedido é cancelado pela API depois de o estoque ter sido reservado: o estoque volta na
  mesma gravação do cancelamento (US5, clarify). A devolução na recusa do pagamento é da
  issue #9.
- O cancelamento pela API e a reserva do worker disputam o mesmo pedido: a concorrência
  otimista do pedido faz um dos dois perder e ser reavaliado com o status atual (US5,
  cenário 4).
- O processo de workers cai depois de abater o estoque, mas antes de confirmar a mensagem:
  como a reserva, a mudança de status, o evento de saída e o registro de processado são
  gravados juntos, a reentrega é descartada como duplicada ou refaz tudo do zero, sem
  reserva dupla.
- Broker fora do ar quando a reserva termina: o evento fica pendente no outbox e é
  publicado quando o broker voltar (REQ-06). A reserva não é desfeita.
- "pedido criado" fora do contrato: vai direto para a DLQ, sem tentativa (base do REQ-06).

## Requirements *(mandatory)*

### Functional Requirements

**Consumo**

- **FR-001**: O processo de workers MUST ter um consumidor de estoque, com fila e DLQ
  próprias, que recebe o evento "pedido criado" (contrato v1 do REQ-06).
- **FR-002**: O consumidor MUST reservar o estoque de todo pedido ainda sem reserva em
  `Pendente`, `Processando` ou `ProntoParaEnvio` (clarify). Para pedido em status final
  (`Cancelado`, `Despachado` ou `PagamentoRejeitado`), MUST confirmar a mensagem sem
  alterar estoque nem status e sem publicar evento, registrando em log o status
  encontrado.
- **FR-003**: O consumidor MUST usar os itens e quantidades gravados no pedido. Um pedido
  que não existe MUST ser tratado como falha (novas tentativas e, depois, DLQ).

**Reserva**

- **FR-004**: Um item MUST ser considerado disponível quando o produto está ativo e o
  estoque atual é maior ou igual à quantidade do item.
- **FR-005**: Se todos os itens estão disponíveis, o consumidor MUST, numa única gravação:
  - abater do estoque de cada produto a quantidade do item;
  - registrar no pedido que o estoque está reservado;
  - registrar o evento "estoque reservado" com o identificador do pedido e os itens
    (produto e quantidade).

  O status do pedido MUST NOT mudar (em geral, continua `Pendente`).
- **FR-006**: Se algum item não está disponível, o consumidor MUST NOT alterar nenhum
  estoque. Na mesma gravação, MUST mudar o pedido do status atual para `Cancelado`
  (transição já permitida no contrato v1), com o motivo `Estoque insuficiente.` no
  histórico e sem autor, e registrar o evento "estoque insuficiente".
- **FR-007**: O estoque de um produto MUST NOT ficar negativo e MUST NOT ser abatido duas
  vezes para o mesmo pedido, mesmo com reentregas e processamento simultâneo.

**Concorrência**

- **FR-008**: Atualizações simultâneas do mesmo produto MUST ser detectadas pela
  concorrência otimista do REQ-04. A reserva que perder MUST ser reavaliada com o estoque
  atual, um número limitado de vezes dentro do mesmo processamento, e pode terminar em
  reserva ou em cancelamento, conforme o estoque atual.
- **FR-009**: Se os conflitos persistirem depois das reavaliações, o processamento MUST
  falhar sem gravar nada, para a base de consumo tentar de novo (1 + 3 tentativas) e, por
  fim, enviar à DLQ.

**Devolução no cancelamento**

- **FR-010**: Quando um pedido com estoque reservado é cancelado pela API (Cliente dono ou
  operação), o sistema MUST devolver ao estoque de cada produto a quantidade do item e
  registrar no pedido que a reserva foi desfeita, na mesma gravação da mudança de status.
  Pedido sem reserva MUST ser cancelado sem alterar estoque (clarify).
- **FR-011**: A reserva do worker e o cancelamento pela API MUST ser protegidos pela
  concorrência otimista do pedido: o lado que perder MUST reavaliar com o estado atual, sem
  reservar para pedido cancelado nem devolver estoque que não foi reservado. A devolução
  vale mesmo para produto inativo.

**Contratos de eventos**

- **FR-012**: O sistema MUST definir o contrato versionado (versão 1) "estoque
  insuficiente", com o envelope comum do REQ-06 e, no conteúdo:
  - identificador do pedido;
  - itens que faltaram, cada um com produto, quantidade pedida, quantidade disponível no
    momento e se o produto estava inativo.
- **FR-013**: O contrato "estoque insuficiente" MUST ser publicado como schema no
  repositório, nas mesmas convenções dos contratos do REQ-06, e MUST ter testes de
  serialização, ida e volta e recusa de mensagem fora do schema.
- **FR-014**: O contrato "estoque reservado" do REQ-06 MUST ser usado sem mudanças.

**Observabilidade**

- **FR-015**: Reserva, cancelamento por falta de estoque, pedido ignorado por status e
  conflito de concorrência MUST ser registrados em log com o identificador do evento e do
  pedido, o resultado e, no conflito, o número da reavaliação. Os logs MUST NOT conter
  dados pessoais além dos identificadores.

**Compatibilidade**

- **FR-016**: O contrato HTTP v1 MUST permanecer inalterado: a devolução no cancelamento
  não muda requisição nem resposta. A criação de pedidos continua conferindo a
  disponibilidade sem reservar (REQ-05).
- **FR-017**: A suíte de testes MUST continuar verde, com os testes de integração do
  consumidor usando banco e broker reais, e o ambiente de demonstração MUST continuar
  subindo com o processo de workers saudável.

### Key Entities *(include if feature involves data)*

- **Produto** (REQ-04): quantidade em estoque, situação (ativo ou inativo) e versão de
  concorrência. É o que a reserva abate.
- **Pedido** (REQ-04): status, itens, histórico e, agora, a indicação de que o estoque
  está reservado. A reserva lê os itens e marca a indicação; o cancelamento muda o status,
  acrescenta uma entrada no histórico e, se havia reserva, a desfaz.
- **Evento "estoque reservado"** (REQ-06): pedido e itens reservados.
- **Evento "estoque insuficiente"** (novo): pedido e itens que faltaram, com quantidade
  pedida, quantidade disponível e indicação de produto inativo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com o processo de workers e o broker no ar, 95% dos pedidos criados têm o
  resultado do estoque (reserva ou cancelamento) gravado e o evento correspondente
  publicado em até 5 segundos após a criação.
- **SC-002**: Em 100% das execuções do teste de disputa (produto com estoque N e M > N
  pedidos simultâneos), exatamente N pedidos são reservados, M − N são cancelados, e o
  estoque termina em 0, nunca negativo.
- **SC-003**: Um "pedido criado" entregue duas vezes produz uma única reserva ou um único
  cancelamento, e um único evento, em 100% dos casos.
- **SC-004**: Em 100% dos pedidos cancelados por falta de estoque, nenhum produto do
  pedido tem o estoque alterado.
- **SC-005**: O contrato "estoque insuficiente" tem testes de serialização, ida e volta e
  recusa de mensagem inválida, como os quatro contratos do REQ-06.
- **SC-006**: Em 100% dos cancelamentos pela API de pedidos com estoque reservado, o
  estoque de cada produto volta exatamente ao valor de antes da reserva (descontadas outras
  movimentações). Nenhum cancelamento de pedido sem reserva altera estoque.
- **SC-007**: A suíte continua 100% verde no CI, com os testes do consumidor executados
  contra banco e broker reais, e o contrato HTTP v1 não muda.

## Assumptions

- Depois da reserva, o pedido continua `Pendente`. A mudança para `Processando` vem com o
  pagamento aprovado (issue #9), como já está no README.
- A reserva é "tudo ou nada": não há reserva parcial nem pedido parcialmente atendido.
- O cancelamento por falta de estoque é uma mudança do sistema: entra no histórico sem
  autor, com o motivo `Estoque insuficiente.`. Ele não passa pelas regras de quem pode
  mudar o status pela API (REQ-05), mas respeita a transição `Pendente → Cancelado` já
  prevista no contrato v1.
- A devolução do estoque no cancelamento pela API entra neste requisito (clarify; US5). A
  devolução na recusa do pagamento é da issue #9, que reaproveita a indicação de reserva
  do pedido.
- Nenhum evento novo de "pedido cancelado" é criado: "estoque insuficiente" já informa o
  motivo, e o pedido cancelado pode ser consultado pela API.
- O número de reavaliações por conflito dentro do mesmo processamento é decidido no plano;
  depois delas valem as novas tentativas da base do REQ-06.
- Os casos de teste usam o catálogo inicial do REQ-04: `CAT-008` (estoque 1, disputa do
  último item), `CAT-009` (sem estoque) e `CAT-010` (inativo). Como a criação do pedido já
  recusa produto inativo ou sem estoque (422), os cenários de falta no consumo reduzem o
  estoque ou desativam o produto entre a criação e o consumo.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
