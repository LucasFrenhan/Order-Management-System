# Feature Specification: Ações da operação sobre estornos pendentes e evento de estorno travado

**Feature Branch**: `feature/req-28-acoes-estornos-pendentes`

**Requisito**: REQ-28 (GitHub issue #57 — "[Estorno] Ações da operação sobre estornos pendentes e evento de estorno travado")

**Created**: 2026-11-10

**Status**: Draft

**Input**: User description: "Dar à operação visibilidade e ação sobre estornos pendentes: consulta dos pendentes, forçar uma tentativa respeitando o lock da rodada e o circuito do gateway, dar baixa manual com motivo e autor sem chamar o gateway, e um evento versionado de estorno travado, emitido uma vez por estorno ao passar do limite de alerta (issue #57)"

## Contexto

Cancelar um pedido pago deixa o estorno **pendente**, e o Worker o pede ao gateway a cada rodada, sem
nunca desistir (REQ-13, [fluxo 10](../../docs/fluxos/10-estorno.md)). Depois de 24 h e 3 tentativas
com falha, cada nova falha vira um erro no log (7008), que hoje é o único sinal para a operação agir, e
fora do sistema.

Duas lacunas ficaram registradas nas limitações do fluxo 10:

- **Sem ação da operação.** Não há como ver os estornos pendentes, forçar uma nova tentativa agora, nem
  dar baixa num estorno que o financeiro resolveu por fora (por exemplo, uma devolução manual). Um
  estorno resolvido por fora continua sendo tentado para sempre.
- **Sem evento de falha.** Só a confirmação gera o `pagamento.estornado` (REQ-15). Os sistemas de fora
  não sabem que um estorno está travado.

Três fatos do sistema atual entram no desenho:

- O estorno é pedido e tentado só pelo Worker; a API não conversa com o gateway de pagamento. As ações
  que dependem do gateway seguem o padrão do reenvio da DLQ (REQ-14): a API registra o pedido, e o
  Worker executa.
- A rodada de estornos roda sob um lock exclusivo entre instâncias, e o circuito do gateway (REQ-19)
  adia a rodada inteira, sem contar tentativa, enquanto o gateway está fora do ar.
- Hoje cada pagamento tem no máximo um estorno, sempre do valor total. O estorno parcial (#56) pode
  levar a vários estornos por pagamento; por isso, as ações deste requisito identificam o **estorno**
  por um identificador próprio, e não pelo pedido.

## Clarifications

### Session 2026-11-10

Respostas dadas pelo usuário durante o specify:

- Q: Que evento sai na baixa manual? → A: Um evento próprio, `pagamento.estorno-baixado`, versão 1,
  com o estorno, o pedido, o valor, o autor e o instante, sem o motivo. O `pagamento.estornado`
  continua significando só a confirmação pelo gateway, sem mudança.
- Q: Na baixa manual, o Cliente recebe o aviso `pedido.estorno-confirmado` nos webhooks e no SSE? →
  A: Sim, o mesmo aviso da confirmação pelo gateway. Ele não traz valores nem identificadores e não
  revela que a devolução foi manual.
- Q: Quem pode dar baixa manual? → A: Só o Admin. O OperadorLogistica lista, consulta e força
  tentativas, mas recebe 403 na baixa.

Respostas dadas pelo usuário no clarify:

- Q: Se a baixa chega com a rodada de estornos em andamento, o que a API faz? → A: Espera até 5 s a
  rodada terminar. Se ela termina, a baixa segue (relendo o estorno); se não, 409 com "Rodada de
  estornos em andamento; tente de novo em instantes.", sem gravar nada.
- Q: A baixa tem um campo para a referência externa da devolução (comprovante, transferência)? → A:
  Sim, opcional, de até 100 caracteres, guardado, exibido na consulta e enviado no
  `pagamento.estorno-baixado`.
- Q: O sistema expõe uma métrica dos estornos pendentes? → A: Sim, um medidor
  `oms.estornos.pendentes`, com o atributo `oms.alerta` (além do limite ou não), atualizado a cada
  rodada.
- Q: Na baixa, o sistema confere no banco que o Admin ainda está ativo e ainda é Admin? → A: Sim, como
  na gestão de usuários (REQ-25); um Admin desativado ou rebaixado recebe 403 mesmo com o token válido.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Ver os estornos pendentes (Priority: P1)

A operação abre a lista de estornos pendentes e vê, para cada um, o pedido, o valor, há quanto tempo
está pendente, quantas tentativas falharam, quando foi a última e qual foi o último erro, e se já
passou do limite de alerta. Com isso, decide em quais agir sem precisar procurar nos logs.

**Why this priority**: Sem visibilidade, nenhuma das outras ações tem como ser usada com critério. É
também o primeiro critério de aceite da issue.

**Independent Test**: Criar estornos pendentes em situações diferentes (novo, com falhas, além do
limite) e conferir que a lista traz todos, com os dados certos, e nenhum estorno já confirmado.

**Acceptance Scenarios**:

1. **Given** três estornos pendentes e um já confirmado, **When** a operação lista os pendentes,
   **Then** a resposta traz os três, sem o confirmado, do pedido de estorno mais antigo para o mais
   novo.
2. **Given** um estorno com duas falhas, a última com a recusa "Cobrança desconhecida.", **When** a
   operação o consulta, **Then** a resposta mostra 2 tentativas, o instante da última e o erro
   "Cobrança desconhecida.", sem o identificador da transação nem dado pessoal.
3. **Given** um estorno pendente há mais de 24 h e com 3 falhas, **When** a operação lista os
   pendentes, **Then** ele aparece marcado como além do limite de alerta.
4. **Given** mais estornos pendentes do que cabem numa página, **When** a operação lista, **Then** a
   resposta traz uma página e um cursor para a seguinte, como nas outras listagens.
5. **Given** o identificador de um estorno, pendente ou não, **When** a operação o consulta, **Then** a
   resposta mostra a situação atual dele (pendente, confirmado pelo gateway ou baixado manualmente).
6. **Given** um usuário sem token, **When** ele lista ou consulta, **Then** a resposta é 401; **Given**
   um Cliente, **Then** a resposta é 403.

---

### User Story 2 - Dar baixa num estorno resolvido fora do sistema (Priority: P1)

O financeiro devolveu o dinheiro ao Cliente por fora (transferência, crédito manual). A operação dá
baixa no estorno, informando o motivo. O estorno sai da fila, o Worker nunca mais o tenta, e fica
registrado quem deu a baixa, quando e por quê.

**Why this priority**: Sem a baixa, um estorno resolvido por fora é tentado para sempre e gera um erro
no log a cada rodada, e há o risco de o gateway acabar devolvendo o dinheiro uma segunda vez. É o
critério de aceite mais sensível da issue.

**Independent Test**: Dar baixa num estorno pendente com motivo, rodar a rotina de estornos e conferir
que o gateway não foi chamado e que o estorno aparece como baixado, com autor, instante e motivo.

**Acceptance Scenarios**:

1. **Given** um estorno pendente, **When** a operação autorizada dá baixa com um motivo válido e,
   opcionalmente, a referência externa da devolução, **Then** o estorno passa a baixado manualmente,
   com o autor, o instante, o motivo e a referência registrados, e sai da lista de pendentes.
2. **Given** um estorno baixado, **When** a rotina de estornos roda, **Then** o gateway não é chamado
   para ele, e nenhum estorno é pedido de novo.
3. **Given** um pedido de baixa sem motivo, com motivo fora de 10 a 500 caracteres, ou com referência
   externa acima de 100 caracteres, **When** ele é enviado, **Then** a resposta é 400 com o campo
   inválido, e o estorno continua pendente.
4. **Given** um estorno já confirmado pelo gateway ou já baixado, **When** a operação tenta dar baixa,
   **Then** a resposta é 409, e nada muda.
5. **Given** a baixa feita, **When** ela é gravada, **Then** saem, na mesma gravação, o evento
   `pagamento.estorno-baixado` para os sistemas de fora e o aviso `pedido.estorno-confirmado` para os
   webhooks e o SSE do Cliente; o `pagamento.estornado` não sai.
6. **Given** um OperadorLogistica ou um Cliente, **When** ele tenta dar baixa, **Then** a resposta é
   403; **Given** um usuário sem token, **Then** 401.
7. **Given** um Admin desativado ou rebaixado depois de receber o token, **When** ele tenta dar baixa
   com o token ainda válido, **Then** a resposta é 403.
8. **Given** a rodada de estornos em andamento, **When** o Admin dá baixa e a rodada termina em até
   5 s, **Then** a baixa é gravada sobre o estado relido; **When** a rodada não termina em 5 s,
   **Then** a resposta é 409 com "Rodada de estornos em andamento; tente de novo em instantes.", e
   nada é gravado.

---

### User Story 3 - Saber que um estorno travou (Priority: P2)

Quando um estorno passa do limite de alerta (hoje, 24 h pendente e 3 tentativas com falha), sai um
evento versionado, uma única vez por estorno, para que os sistemas de fora (financeiro, atendimento,
monitoração) saibam que precisam agir.

**Why this priority**: Fecha a segunda lacuna da issue e tira a dependência exclusiva do log 7008. Vem
depois da visibilidade e da baixa porque não muda o estado do estorno.

**Independent Test**: Fazer um estorno falhar até passar do limite e conferir que sai um evento, e que
as falhas seguintes, a confirmação ou a baixa não geram outro evento de atraso.

**Acceptance Scenarios**:

1. **Given** um estorno pendente há mais de 24 h com 2 falhas, **When** a terceira falha é gravada,
   **Then** sai, na mesma gravação, o evento de estorno travado, com o estorno, o pedido, o valor,
   desde quando está pendente e as tentativas.
2. **Given** um estorno que já gerou o evento de estorno travado, **When** ele falha de novo, **Then**
   nenhum outro evento de atraso sai (o log 7008 continua a cada falha).
3. **Given** um estorno travado que depois é confirmado pelo gateway ou baixado, **When** isso
   acontece, **Then** sai só o evento da confirmação ou da baixa, sem novo evento de atraso.
4. **Given** o circuito do gateway aberto, **When** a rodada é adiada, **Then** nenhuma tentativa é
   contada e nenhum evento de atraso sai por causa do adiamento.
5. **Given** um estorno que já estava além do limite antes da implantação, **When** ele falha de novo
   depois dela, **Then** o evento de estorno travado sai uma vez.

---

### User Story 4 - Forçar uma nova tentativa (Priority: P3)

O gateway voltou, ou o financeiro corrigiu o problema que causava a recusa. A operação pede uma nova
tentativa de um estorno, sem esperar a fila. O pedido é aceito na hora, e a tentativa é feita pela
rotina de estornos, na frente dos demais, respeitando o lock da rodada e o circuito do gateway.

**Why this priority**: Útil, mas a rotina já tenta todos os estornos a cada minuto; o ganho é furar a
fila quando há muitos pendentes. Por isso fica depois da visibilidade, da baixa e do evento.

**Independent Test**: Com vários estornos pendentes e um lote menor que a fila, forçar a tentativa do
último da fila e conferir que ele é tentado na rodada seguinte, antes dos outros.

**Acceptance Scenarios**:

1. **Given** um estorno pendente, **When** a operação força uma tentativa, **Then** a resposta é 202, e
   o estorno passa a ter uma tentativa forçada pendente, visível na consulta.
2. **Given** um estorno com tentativa forçada pendente, **When** a próxima rodada começa, **Then** ele é
   tentado antes dos que não foram forçados, mesmo que o lote não alcançasse a posição dele na fila.
3. **Given** a tentativa forçada, **When** ela falha, **Then** conta como uma tentativa comum (log 7007,
   e 7008 e evento de atraso se passar do limite), e a marca de tentativa forçada é retirada.
4. **Given** a tentativa forçada e o circuito do gateway aberto, **When** a rodada é adiada, **Then** a
   marca continua, nenhuma tentativa é contada, e o estorno é tentado primeiro quando o circuito fechar.
5. **Given** um estorno que já tem tentativa forçada pendente, **When** a operação força de novo,
   **Then** a resposta é 202, sem duplicar a tentativa.
6. **Given** um estorno confirmado ou baixado, **When** a operação força uma tentativa, **Then** a
   resposta é 409.
7. **Given** um usuário sem token, **When** ele força, **Then** 401; **Given** um Cliente, **Then** 403.

---

### Edge Cases

- **Baixa durante a rodada**: a rodada pode já ter pedido o estorno ao gateway sem ter gravado o
  resultado. A baixa não pode ser gravada nesse intervalo, senão o dinheiro volta duas vezes (pelo
  gateway e por fora): ela espera até 5 s a rodada terminar e relê o estorno (que pode ter sido
  confirmado nesse meio-tempo, e aí a resposta é 409); se a rodada não termina, 409 com "Rodada de
  estornos em andamento; tente de novo em instantes.".
- **Rodada que começa durante a baixa**: espera a baixa terminar ou pula a rodada (como já faz com
  outra instância), e relê o estorno antes de chamar o gateway.
- **Rodada depois da baixa**: a rodada relê o estorno antes de chamar o gateway (como já faz) e não o
  tenta se ele foi baixado.
- **Duas baixas ao mesmo tempo**: uma é gravada; a outra recebe 409.
- **Baixa e confirmação do gateway ao mesmo tempo**: só uma vale; a outra recebe conflito (a API
  responde 409; o Worker relê e não grava sobre o estado novo).
- **Gateway confirmou, mas a gravação se perdeu, e depois veio a baixa**: o dinheiro já tinha voltado
  pelo gateway e volta de novo por fora. O sistema não tem como saber; a operação deve consultar o
  gateway antes de dar baixa. Fica nas limitações do fluxo 10.
- **Estorno inexistente** (identificador desconhecido) na consulta, na baixa ou ao forçar: 404.
- **Lista vazia**: 200 com a lista vazia.
- **Worker fora do ar**: a lista, a consulta e a baixa funcionam (só dependem da API e do banco); a
  tentativa forçada fica registrada e é feita quando o Worker voltar.
- **Configuração do limite de alerta mudada** depois que um estorno gerou o evento de atraso: o evento
  não sai de novo para ele.
- **Último erro**: guarda só o texto da recusa do gateway ou uma descrição genérica da falha (por
  exemplo, "Gateway sem resposta."), nunca o identificador da transação, do estorno ou dado pessoal.

## Requirements *(mandatory)*

### Functional Requirements

**Identificação e visibilidade**

- **FR-001**: Cada estorno MUST ter um identificador próprio, distinto do pedido, usado na consulta e
  nas ações deste requisito.
- **FR-002**: A operação MUST poder listar os estornos pendentes, com o identificador do estorno, o
  pedido (id e número), o valor, o instante em que o estorno foi pedido, as tentativas com falha, o
  instante da última falha, o último erro, se está além do limite de alerta e se há tentativa forçada
  pendente.
- **FR-003**: A listagem MUST ir do estorno pedido há mais tempo para o mais novo e ser paginada por
  cursor, como as outras listagens do sistema. (Ajuste do plan, research R-08: a ordem da fila da
  rodada muda a cada falha, e um cursor sobre ela pularia ou repetiria estornos entre as páginas.)
- **FR-004**: A operação MUST poder consultar um estorno pelo identificador, em qualquer situação
  (pendente, confirmado pelo gateway ou baixado manualmente), vendo também, quando houver, o instante
  da confirmação ou os dados da baixa (autor, instante, motivo e referência externa).
- **FR-005**: O sistema MUST guardar o último erro de cada tentativa com falha, limitado ao texto da
  recusa do gateway ou a uma descrição genérica da falha, sem identificadores do gateway nem dados
  pessoais.

**Baixa manual**

- **FR-006**: A operação autorizada MUST poder dar baixa num estorno pendente, informando um motivo
  obrigatório de 10 a 500 caracteres e, opcionalmente, a referência externa da devolução (até 100
  caracteres), sem que o gateway seja chamado.
- **FR-007**: A baixa MUST registrar o autor (o usuário autenticado), o instante, o motivo e a
  referência externa, e levar o estorno a uma situação final própria, distinta da confirmação pelo
  gateway.
- **FR-008**: Um estorno baixado MUST NOT ser tentado de novo pela rotina nem por uma tentativa forçada,
  e MUST NOT gerar um novo pedido de estorno ao gateway.
- **FR-009**: A baixa MUST NOT ser gravada enquanto a rodada de estornos está em andamento: ela espera
  até 5 s a rodada terminar e relê o estorno; se a rodada não termina, responde 409 com "Rodada de
  estornos em andamento; tente de novo em instantes.". A baixa de um estorno que não está pendente
  (inclusive confirmado durante a espera) MUST responder 409. Em nenhum desses casos algo é gravado.
- **FR-010**: A baixa MUST publicar, na mesma gravação e pelo outbox, o evento
  `pagamento.estorno-baixado`, versão 1, com o identificador do estorno, o pedido (id e número), o
  valor, o autor, o instante da baixa e a referência externa (quando informada), sem o motivo,
  identificadores do gateway ou dados pessoais. O
  `pagamento.estornado` MUST NOT sair na baixa.
- **FR-010a**: A baixa MUST gerar, na mesma gravação, o aviso `pedido.estorno-confirmado` para os
  webhooks e o SSE do Cliente, igual ao da confirmação pelo gateway.

**Tentativa forçada**

- **FR-011**: A operação MUST poder pedir uma tentativa forçada de um estorno pendente; a API registra
  o pedido e responde 202, e a tentativa é feita pela rotina de estornos.
- **FR-012**: Na rodada seguinte, os estornos com tentativa forçada MUST ser tentados antes dos demais,
  dentro do mesmo lock e do mesmo tratamento do circuito do gateway.
- **FR-013**: A tentativa forçada MUST contar como uma tentativa comum (falha, log 7007, alerta 7008 e
  evento de atraso), e a marca MUST ser retirada quando a tentativa é feita, com sucesso ou falha. Com
  o circuito aberto, a marca continua e nada é contado.
- **FR-014**: Pedir de novo a tentativa forçada de um estorno que já a tem MUST responder 202 sem
  duplicá-la; pedi-la para um estorno que não está pendente MUST responder 409.

**Evento de estorno travado**

- **FR-015**: Quando a falha de uma tentativa leva o estorno além do limite de alerta (os mesmos
  `LimiteAlertaSegundos` e `TentativasAlerta` do log 7008), o sistema MUST publicar o evento
  `pagamento.estorno-atrasado`, versão 1, na mesma gravação da falha, pelo outbox.
- **FR-016**: O evento de estorno travado MUST sair no máximo uma vez por estorno, mesmo com novas
  falhas, reinícios, várias instâncias ou mudança da configuração do limite.
- **FR-017**: O evento MUST trazer o identificador do estorno, o pedido (id e número), o valor, o
  instante do pedido de estorno, as tentativas com falha e o instante em que passou do limite, sem
  identificadores do gateway, o último erro ou dados pessoais.
- **FR-018**: O evento de estorno travado MUST NOT gerar aviso ao Cliente (webhooks e SSE).

**Acesso, contratos e registros**

- **FR-019**: A listagem, a consulta e a tentativa forçada MUST ser permitidas ao Admin e ao
  OperadorLogistica, e recusadas com 401 sem token e 403 para o Cliente.
- **FR-020**: A baixa MUST ser permitida só ao Admin ainda ativo no banco no momento da requisição
  (como na gestão de usuários, REQ-25), e recusada com 403 para o OperadorLogistica e o Cliente.
- **FR-021**: Os contratos v1, HTTP e de eventos, MUST mudar só por acréscimo: rotas, campos e eventos
  novos, sem alterar nem remover o que existe. O `pagamento.estornado` de uma confirmação pelo gateway
  continua igual.
- **FR-022**: A baixa e o pedido de tentativa forçada MUST gerar log com ID de evento próprio, com o
  estorno, o pedido e o autor, sem o texto do motivo nem a referência externa. A publicação do evento
  de atraso MUST gerar log próprio. A baixa recusada por rodada em andamento MUST gerar log de aviso.
- **FR-022a**: Com a telemetria ligada (REQ-16), o sistema MUST expor o medidor
  `oms.estornos.pendentes`, com o atributo `oms.alerta` (além do limite de alerta ou não), atualizado
  a cada rodada de estornos.
- **FR-023**: As rotas novas MUST seguir o limite de requisições, o formato de erro (ProblemDetails) e
  a documentação OpenAPI já em uso.

**Documentação**

- **FR-024**: Os documentos de fluxo 10 (estorno) e 8 (eventos) MUST ser atualizados, retirando das
  limitações do fluxo 10 as duas lacunas resolvidas, o fluxo 13 (notificação), com o aviso da
  baixa, e o fluxo 12 (rastreabilidade e métricas), com o medidor novo.

### Key Entities

- **Estorno**: devolução do valor pago de um pedido. Tem identificador próprio, pedido, valor, instante
  em que foi pedido, situação (pendente, confirmado pelo gateway, baixado manualmente), tentativas com
  falha, instante e texto do último erro, instante em que passou do limite de alerta (quando passou),
  marca de tentativa forçada pendente e, conforme a situação, a confirmação do gateway (identificador e
  instante) ou a baixa (autor, instante, motivo e referência externa opcional). Hoje, no máximo um por
  pagamento.
- **Evento de estorno travado** (`pagamento.estorno-atrasado`, v1): aviso, para sistemas de fora, de que
  um estorno passou do limite de alerta.
- **Evento da baixa manual** (`pagamento.estorno-baixado`, v1): aviso, para sistemas de fora, de que
  o estorno foi resolvido fora do sistema, por quem e quando.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A operação identifica todos os estornos travados e o motivo da última falha em uma única
  consulta, sem procurar nos logs.
- **SC-002**: Depois de uma baixa, o estorno recebe zero tentativas novas no gateway, verificado em pelo
  menos 3 rodadas seguidas.
- **SC-003**: Em testes com a baixa e a rodada concorrendo pelo mesmo estorno, nenhuma execução termina
  com o estorno confirmado pelo gateway e baixado, nem com duas devoluções registradas.
- **SC-004**: Cada estorno que passa do limite de alerta gera exatamente um evento de atraso, mesmo com
  10 falhas seguidas e duas instâncias do Worker.
- **SC-005**: Um estorno com tentativa forçada é tentado na primeira rodada seguinte ao pedido, mesmo
  quando há mais pendentes do que o tamanho do lote.
- **SC-006**: Todas as rotas novas recusam 100% das requisições sem token (401) e de perfis sem
  permissão (403).
- **SC-007**: A verificação de compatibilidade dos contratos v1 não acusa nenhuma mudança incompatível.
- **SC-008**: Uma baixa pedida durante uma rodada comum (que termina em menos de 5 s) é gravada sem
  que o Admin precise repetir o pedido.
- **SC-009**: O painel de telemetria mostra quantos estornos estão pendentes e quantos estão além do
  limite, com no máximo uma rodada de atraso.

## Assumptions

- "Operação" são os perfis Admin e OperadorLogistica, os mesmos que já cancelam pedidos e pedem o
  estorno. O Cliente não vê nem age sobre estornos por estas rotas.
- O identificador do estorno pode, enquanto houver um estorno por pagamento, ser o mesmo do pagamento;
  a decisão fica no plano, desde que as rotas e os eventos o tratem como identificador do estorno.
- A tentativa forçada é assíncrona, como o reenvio da DLQ (REQ-14): a API não chama o gateway. A rodada
  continua no intervalo configurado (60 s por padrão); o ganho da tentativa forçada é a prioridade na
  fila, não um intervalo menor.
- O limite de alerta do evento de atraso é o mesmo do log 7008, sem configuração nova.
- O último erro passa a ser guardado a partir da implantação; estornos que falharam antes mostram o
  último erro vazio até a próxima falha.
- O motivo da baixa é texto livre da operação, guardado no banco e exibido na consulta; não vai para os
  logs nem para os eventos. A referência externa vai para o evento da baixa, para a conciliação, mas
  não para os logs.
- Fora do escopo: estorno parcial e devolução de pedido despachado (#56), reverter uma baixa, e
  qualquer ação do Cliente sobre o estorno.
