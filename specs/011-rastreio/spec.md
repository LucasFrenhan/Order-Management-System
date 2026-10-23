# Feature Specification: Rastreio, despacho e histórico na consulta de pedidos

**Feature Branch**: `feature/req-11-rastreio`

**Requisito**: REQ-11 (GitHub issue #11 — "[API] Endpoints de Gestão de Logística, Rastreio e Consulta de Pedidos")

**Created**: 2026-10-23

**Status**: Draft

**Input**: User description: "REQ-11 (GitHub issue #11): (1) GET /api/v1/pedidos/{id}: o Cliente consulta só o próprio pedido (dono validado pelo token, já existe desde o REQ-05); Admin e OperadorLogistica consultam qualquer pedido; a resposta passa a trazer o histórico de status e os dados de entrega/rastreio (ordem de entrega criada pelo REQ-10: transportadora, código de rastreio, data de despacho). (2) PATCH /api/v1/pedidos/{id}/rastreio: só OperadorLogistica ou Admin; registra a transportadora e o código de rastreio na ordem de entrega e muda o pedido para Despachado. A rota de rastreio e o histórico na consulta não existem no contrato HTTP v1: pela constituição (Princípio II), exigem evolução compatível do contrato ou versão nova, a decidir na especificação."

## Contexto

Desde o REQ-10, todo pedido pago ganha uma ordem de entrega e fica em `ProntoParaEnvio`. A
ordem nasce sem transportadora, código de rastreio e data de despacho, e nada no sistema os
preenche: a operação só consegue marcar o pedido como `Despachado` pela mudança de status
do REQ-05, sem registrar como ele saiu. A consulta do pedido também não mostra o histórico
de status nem a entrega, então o Cliente não sabe como acompanhar a compra.

Este requisito entrega as duas pontas da issue:

- **Consulta completa**: a consulta do pedido passa a trazer o histórico de status e os
  dados da entrega (transportadora, código de rastreio e data de despacho), com as mesmas
  regras de acesso de hoje.
- **Despacho com rastreio**: a equipe de logística registra a transportadora e o código de
  rastreio de um pedido pronto para envio, e o pedido passa a `Despachado`, numa única
  operação.

**Contrato HTTP**: as duas mudanças entram como evolução compatível da v1, sem versão nova.
A consulta ganha campos novos, sem mudar nem remover os existentes, e o despacho é uma rota
nova. Clientes atuais da v1 continuam funcionando sem mudança, e a verificação automática
de compatibilidade do contrato (REQ-01) precisa continuar aprovando.

## User Scenarios & Testing *(mandatory)*

Os usuários são o **Cliente**, que acompanha o próprio pedido, e a equipe de logística
(**OperadorLogistica** e **Admin**), que despacha os pedidos prontos e consulta qualquer
pedido.

### User Story 1 - Cliente acompanha o pedido com histórico e rastreio (Priority: P1)

O Cliente consulta o próprio pedido e vê, além do que já via, o histórico de status (de
onde, para onde, quando e por quê) e os dados da entrega: transportadora, código de
rastreio e data de despacho, quando já existem.

**Why this priority**: É o critério principal da issue e o que dá valor ao rastreio para
quem comprou.

**Independent Test**: Criar e despachar um pedido; consultá-lo como o Cliente dono e
conferir o histórico completo e os dados de rastreio na resposta.

**Acceptance Scenarios**:

1. **Given** um pedido despachado do Cliente, **When** ele consulta o pedido, **Then** a
   resposta traz o histórico de status em ordem cronológica, cada entrada com o status
   anterior, o novo, o instante e o motivo, e a entrega com a transportadora, o código de
   rastreio e a data de despacho.
2. **Given** um pedido do Cliente ainda sem ordem de entrega (por exemplo, `Pendente`),
   **When** ele o consulta, **Then** a entrega vem vazia (ausente), e o histórico traz as
   entradas até agora.
3. **Given** um pedido `ProntoParaEnvio` do Cliente, com ordem de entrega e ainda sem
   despacho, **When** ele o consulta, **Then** a entrega aparece sem transportadora, código
   de rastreio e data de despacho.
4. **Given** um pedido de outro Cliente, **When** o Cliente tenta consultá-lo, **Then** a
   resposta é "não encontrado", como hoje, sem revelar que o pedido existe.
5. **Given** qualquer consulta, **When** o histórico é exibido, **Then** ele não identifica
   quem fez cada mudança (nem o identificador da operação nem do sistema).

---

### User Story 2 - Logística despacha o pedido com transportadora e rastreio (Priority: P1)

O OperadorLogistica ou o Admin informa a transportadora e o código de rastreio de um pedido
`ProntoParaEnvio`. O sistema registra os dois e a data de despacho na ordem de entrega e
muda o pedido para `Despachado`, com a entrada no histórico, tudo junto.

**Why this priority**: É o segundo critério da issue e completa o ciclo do pedido iniciado
no REQ-05.

**Independent Test**: Com um pedido `ProntoParaEnvio` e com ordem de entrega, despachá-lo
como OperadorLogistica e conferir na consulta o status `Despachado`, a entrada no histórico
e os dados de rastreio.

**Acceptance Scenarios**:

1. **Given** um pedido `ProntoParaEnvio` com ordem de entrega, **When** o OperadorLogistica
   informa transportadora e código de rastreio válidos, **Then** a resposta traz o pedido em
   `Despachado` com os dados de rastreio e a data de despacho, e o histórico ganha a entrada
   `ProntoParaEnvio → Despachado` com o autor da operação.
2. **Given** o mesmo cenário, **When** o Admin faz o despacho, **Then** o resultado é o
   mesmo.
3. **Given** um Cliente (mesmo o dono do pedido), **When** ele tenta despachar, **Then** a
   operação é recusada por falta de permissão, sem mudar nada.
4. **Given** um pedido em `Pendente`, `Processando`, `Cancelado` ou `PagamentoRejeitado`,
   **When** a logística tenta despachá-lo, **Then** a operação é recusada como transição
   inválida, sem mudar nada.
5. **Given** um pedido que não existe, **When** a logística tenta despachá-lo, **Then** a
   resposta é "não encontrado".
6. **Given** transportadora ou código de rastreio vazios ou acima do tamanho máximo,
   **When** a logística tenta despachar, **Then** a operação é recusada como dados
   inválidos, indicando o campo.
7. **Given** um código de rastreio já usado em outro pedido da mesma transportadora,
   **When** a logística tenta despachar, **Then** a operação é recusada como conflito, sem
   mudar nada.
8. **Given** um pedido `ProntoParaEnvio` **sem** ordem de entrega (por exemplo, movido pela
   operação sem pagamento aprovado, ou pago antes de a fila de entrega existir, REQ-10),
   **When** a logística o despacha, **Then** a ordem de entrega é criada na hora, já com a
   transportadora, o código de rastreio e a data de despacho, e o pedido vai para
   `Despachado`, como no cenário 1.

---

### User Story 3 - Correção do rastreio de um pedido despachado (Priority: P3)

Depois do despacho, a logística pode precisar corrigir a transportadora ou o código de
rastreio informados errados.

**Why this priority**: A issue fala em "atualizar o código de rastreio"; corrigir um erro
de digitação é comum, mas não bloqueia o fluxo principal.

**Independent Test**: Despachar um pedido, corrigir o código de rastreio e conferir na
consulta o novo código, com o status e a data de despacho preservados.

**Acceptance Scenarios**:

1. **Given** um pedido `Despachado`, **When** a logística informa uma nova transportadora ou
   um novo código de rastreio, **Then** a correção é aceita: a ordem passa a ter os dados
   novos, o status e a data de despacho original continuam, e o histórico não ganha
   entrada nova.
2. **Given** um pedido `Despachado`, **When** a logística envia de novo exatamente os mesmos
   dados, **Then** a operação é aceita sem mudar nada (repetição segura de uma requisição).
3. **Given** um pedido `Despachado`, **When** a correção usa um código de rastreio já usado
   em outro pedido da mesma transportadora, **Then** ela é recusada como conflito, sem mudar
   nada.
4. **Given** um pedido levado a `Despachado` pela mudança de status do REQ-05, sem rastreio,
   **When** a logística registra a transportadora e o código de rastreio, **Then** eles são
   gravados (criando a ordem, se não houver), a data de despacho passa a ser o instante do
   registro, e o status e o histórico não mudam.

---

### Edge Cases

- **Despacho pela mudança de status do REQ-05**: a operação continua podendo levar um pedido
  de `ProntoParaEnvio` a `Despachado` pela rota de status, sem rastreio (contrato v1
  intacto). A consulta desse pedido mostra a entrega sem dados de despacho (ou ausente, se
  não houver ordem), e o rastreio pode ser registrado depois (User Story 3, cenário 4).
- **Despacho e cancelamento ao mesmo tempo**: a operação cancela o pedido enquanto outra
  pessoa o despacha. O resultado é um dos dois, nunca os dois: ou o pedido fica
  `Cancelado` (estoque devolvido, REQ-08) e o despacho é recusado como transição inválida,
  ou fica `Despachado` e o cancelamento é recusado. Nenhum dado de rastreio fica gravado num
  pedido cancelado por essa corrida.
- **Dois despachos ao mesmo tempo** para o mesmo pedido, com dados diferentes: só um faz a
  transição; o outro é reavaliado com o estado atual e vira uma correção do pedido já
  `Despachado` (User Story 3). O histórico ganha uma única entrada de despacho.
- **Transportadora ou código de rastreio com espaços nas pontas**: os espaços são removidos
  antes de gravar e antes de conferir a duplicidade.
- **Pedido cancelado depois de `ProntoParaEnvio`** (REQ-10): a ordem continua registrada,
  sem despacho, e a consulta mostra a entrega sem dados de despacho e o status `Cancelado`.
- **Consulta de pedido antigo** (anterior ao REQ-10, sem ordem): a entrega vem ausente, e o
  histórico traz o que foi gravado.

## Requirements *(mandatory)*

### Functional Requirements

**Consulta**

- **FR-001**: A consulta de pedido MUST manter as regras de acesso atuais: o Cliente vê só
  os próprios pedidos (pedido de outro Cliente é "não encontrado"), e Admin e
  OperadorLogistica veem qualquer pedido.
- **FR-002**: A resposta da consulta MUST trazer o histórico de status do pedido, em ordem
  cronológica, cada entrada com o status anterior (ausente na criação), o novo status, o
  instante e o motivo (quando houver).
- **FR-003**: O histórico MUST NOT expor quem fez cada mudança, para nenhum perfil.
- **FR-004**: A resposta da consulta MUST trazer a entrega do pedido quando houver ordem de
  entrega: transportadora, código de rastreio, data de despacho (os três podem estar
  ausentes antes do despacho) e o instante de criação da ordem. Sem ordem, a entrega MUST
  vir ausente.
- **FR-005**: Os campos que a consulta já devolve MUST continuar iguais, em nome, tipo e
  significado.

**Despacho com rastreio**

- **FR-006**: O sistema MUST oferecer a operação de despacho com rastreio de um pedido,
  restrita a OperadorLogistica e Admin. Cliente recebe "sem permissão"; sem autenticação,
  "não autenticado".
- **FR-007**: O despacho MUST receber a transportadora (obrigatória, até 100 caracteres) e o
  código de rastreio (obrigatório, até 50 caracteres), ambos sem espaços nas pontas e não
  vazios.
- **FR-008**: Para um pedido `ProntoParaEnvio`, o despacho MUST, numa única gravação:
  criar a ordem de entrega, se o pedido não tiver; registrar na ordem a transportadora, o
  código de rastreio e a data de despacho; mudar o pedido para `Despachado`; e acrescentar
  ao histórico a entrada `ProntoParaEnvio → Despachado` com o autor da operação e o motivo
  `Pedido despachado.`.
- **FR-009**: Para um pedido já `Despachado`, a mesma operação MUST registrar ou corrigir a
  transportadora e o código de rastreio (criando a ordem, se não houver), sem mudar o
  status nem o histórico. A data de despacho já gravada MUST ser mantida; sem data, ela
  passa a ser o instante do registro. Dados iguais aos gravados MUST ser aceitos sem mudar
  nada.
- **FR-009a**: A operação de rastreio em pedido `Pendente`, `Processando`, `Cancelado` ou
  `PagamentoRejeitado` MUST ser recusada como transição inválida, sem mudar nada.
- **FR-009b**: A transição `ProntoParaEnvio → Despachado` pela mudança de status do REQ-05
  MUST continuar permitida, sem rastreio.
- **FR-010**: O mesmo código de rastreio MUST NOT ser usado em dois pedidos da mesma
  transportadora; a tentativa MUST ser recusada como conflito, sem mudar nada.
- **FR-011**: Despacho de pedido inexistente MUST responder "não encontrado".
- **FR-012**: A resposta de um despacho aceito MUST trazer o pedido no mesmo formato da
  consulta, já com o status, o histórico e a entrega atualizados.
- **FR-013**: O despacho e as demais mudanças do pedido (mudança de status, cancelamento e
  os workers) MUST ser protegidos pela concorrência otimista do pedido: o lado que perder
  reavalia com o estado atual ou é recusado, nunca grava pela metade.

**Contrato**

- **FR-014**: As mudanças MUST entrar como evolução compatível do contrato HTTP v1: campos
  novos na resposta da consulta (e das operações que devolvem o pedido) e uma rota nova
  para o despacho, sem alterar nem remover nada existente. O contrato publicado MUST ser
  atualizado, e a verificação de compatibilidade do contrato MUST continuar aprovando.
- **FR-015**: Erros MUST seguir o formato de erro padrão da API (RFC 7807, como no REQ-01),
  com código de negócio próprio para o código de rastreio duplicado.

**Observabilidade e segurança**

- **FR-016**: O despacho e a correção do rastreio MUST ser registrados em log com o
  identificador do pedido, o do usuário e o resultado (despachado, corrigido ou sem
  mudança); os logs MUST NOT conter dados pessoais além dos identificadores.

### Key Entities *(include if feature involves data)*

- **Pedido** (REQ-04, REQ-05): status, itens e histórico. Ganha a transição para
  `Despachado` pelo despacho com rastreio.
- **Histórico de status** (REQ-04): status anterior e novo, autor, motivo e instante. Passa
  a ser exibido na consulta, sem o autor.
- **Ordem de entrega** (REQ-04, REQ-10): transportadora, código de rastreio, data de
  despacho e instantes; uma por pedido; o par transportadora e código de rastreio é único
  quando informado. Recebe os dados do despacho.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 100% dos pedidos despachados, a consulta mostra a transportadora, o código
  de rastreio, a data de despacho e a entrada `ProntoParaEnvio → Despachado` no histórico.
- **SC-002**: Em 100% das tentativas de despacho recusadas (perfil sem permissão, status
  inválido, dados inválidos ou rastreio duplicado), o pedido, a ordem e o histórico ficam
  como estavam.
- **SC-003**: Em 100% dos casos, um Cliente não consegue ver nem despachar pedido de outro
  Cliente, nem despachar o próprio.
- **SC-004**: 95% das consultas e dos despachos respondem em menos de 500 ms no ambiente
  local, com um pedido de 10 itens e 10 entradas de histórico.
- **SC-005**: Clientes da v1 que só usam os campos atuais continuam funcionando sem mudança:
  a verificação de compatibilidade do contrato aprova, e os testes de contrato existentes
  continuam verdes.
- **SC-006**: Em corridas simultâneas de despacho e cancelamento do mesmo pedido, em 100%
  das rodadas o pedido termina num só estado final, sem rastreio gravado em pedido
  cancelado.

## Assumptions

- **Evolução compatível na v1**: os campos novos da consulta são acréscimos e a rota de
  despacho é nova; nenhum campo existente muda. Por isso não há versão nova da API.
- **Perfis**: OperadorLogistica e Admin, os mesmos da mudança de status do REQ-05.
- **Ordem criada no despacho**: um pedido pronto ou despachado sem ordem de entrega ganha a
  ordem no despacho, o que também cobre os pedidos pagos antes de a fila de entrega existir
  (REQ-10). Essa criação não publica `entrega.criada`: o pedido já sai despachado.
- **Correção sem histórico**: corrigir o rastreio não muda o status, então não entra no
  histórico; fica registrada no log e no instante de atualização da ordem.
- **Sem evento novo**: o despacho não publica evento de mensageria neste requisito; um
  "pedido despachado" para outros sistemas fica para um requisito futuro.
- **Data de despacho**: é o instante em que o despacho é registrado no sistema; a
  logística não informa uma data diferente.
- **Sem autor no histórico exibido**: nem o Cliente nem a operação veem quem fez cada
  mudança pela consulta; a informação continua gravada para auditoria.
- **Rastreio não é validado com a transportadora**: o sistema não consulta serviços
  externos; aceita o código informado, dentro dos tamanhos.
- **Tamanhos**: os de `OrdemEntrega` desde o REQ-04 (transportadora até 100 caracteres,
  código de rastreio até 50).
- O ambiente local do REQ-02 (banco no ar) é pré-requisito para os testes de integração.
  Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
