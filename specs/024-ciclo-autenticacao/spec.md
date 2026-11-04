# Feature Specification: Ciclo de vida da autenticação

**Feature Branch**: `feature/req-24-ciclo-autenticacao`

**Requisito**: REQ-24 (GitHub issue #55 — "[Autenticação] Renovação de token, logout, troca e recuperação de senha e confirmação de e-mail")

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Completar o ciclo de vida da autenticação: renovação com rotação, logout, troca e recuperação de senha, confirmação de e-mail e mensagens que não revelam se um e-mail está cadastrado (issue #55)"

## Contexto

O login devolve só um token de acesso, válido por 60 minutos (REQ-03). Quando ele expira, a pessoa precisa fazer login de novo; um token vazado vale até expirar; não há como encerrar uma sessão; e quem esquece a senha não tem como recuperar a conta. O e-mail do registro nunca é verificado.

Este requisito acrescenta ao contrato v1 a renovação da sessão, o logout, a troca e a recuperação de senha e a confirmação de e-mail, e introduz o envio de e-mail no sistema, com um canal de captura local em Development.

## Clarifications

### Session 2026-11-04

Respostas dadas pelo usuário durante o specify:

- Q: Um token de renovação já usado, ao ser apresentado de novo, encerra a sessão inteira ou só é recusado? → A: Recusa e encerra a sessão inteira, sem tolerância para repetição. Se o token foi roubado, o ladrão perde o acesso; em uma falha de rede, a pessoa faz login de novo.
- Q: O token de acesso ainda válido continua funcionando depois do logout? → A: Sim, até expirar, sem consulta por requisição. Em troca, a validade padrão cai de 60 para 15 minutos, o que limita o risco.
- Q: O login exige o e-mail confirmado? → A: Não. O login é aceito e a confirmação só fica registrada, para nada mudar para os clientes atuais nem para as contas existentes. Uma regra futura pode exigir a confirmação em ações específicas.

Respostas dadas pelo usuário no clarify:

- Q: Quais são os limites padrão das rotas públicas novas? → A: Recuperação e reenvio: 3 por hora por origem mais e-mail e 20 por hora por origem. Redefinição e confirmação: 10 por minuto por origem. Renovação e logout: 120 por minuto por origem.
- Q: A redefinição de senha e a confirmação de e-mail recebem só o token, ou o token com o e-mail da conta? → A: Só o token, aleatório e longo, que identifica a conta sozinho. O link não traz dados pessoais.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Continuar conectado sem digitar a senha de novo (Priority: P1)

Depois do login, o cliente da API recebe também um token de renovação. Quando o token de acesso expira, ele troca o token de renovação por um novo par, sem pedir a senha. Cada token de renovação serve uma só vez.

**Why this priority**: É a base das demais histórias: sem sessão renovável, não há o que encerrar no logout nem na troca de senha. Permite também um token de acesso de vida curta.

**Independent Test**: Fazer login, renovar, confirmar que o token de acesso novo funciona e que o token de renovação antigo é recusado.

**Acceptance Scenarios**:

1. **Given** um login bem-sucedido, **When** a resposta é devolvida, **Then** ela traz, além do que já trazia, o token de renovação e a data em que ele expira.
2. **Given** um token de renovação válido, **When** ele é apresentado, **Then** a resposta traz um novo token de acesso e um novo token de renovação, e o apresentado deixa de valer.
3. **Given** um token de renovação já usado, **When** ele é apresentado de novo, **Then** a resposta é 401, e toda a sessão é encerrada: o token de renovação mais recente dela também deixa de valer.
4. **Given** um token de renovação expirado, revogado ou inexistente, **When** ele é apresentado, **Then** a resposta é o mesmo 401, sem indicar o motivo.
5. **Given** um perfil alterado depois do login, **When** a sessão é renovada, **Then** o novo token de acesso traz os perfis atuais.
6. **Given** uma conta bloqueada por erros de senha, **When** a pessoa renova uma sessão existente, **Then** a renovação funciona: o bloqueio impede a adivinhação de senha, e um atacante não derruba quem já está conectado.

---

### User Story 2 - Encerrar a sessão (Priority: P1)

A pessoa encerra a sessão, e o token de renovação daquele dispositivo deixa de funcionar.

**Why this priority**: Sem logout, uma sessão em um dispositivo perdido ou compartilhado continua renovável até expirar.

**Independent Test**: Fazer login, fazer logout e confirmar que a renovação é recusada.

**Acceptance Scenarios**:

1. **Given** uma sessão ativa, **When** o logout é feito com o token de renovação, **Then** a resposta é 204, e esse token não renova mais.
2. **Given** um token de renovação já revogado, expirado ou inexistente, **When** o logout é feito, **Then** a resposta é o mesmo 204: repetir o logout não é erro e não revela nada.
3. **Given** duas sessões da mesma pessoa, **When** uma é encerrada, **Then** a outra continua funcionando.
4. **Given** um token de acesso ainda válido depois do logout, **When** ele é usado, **Then** continua valendo até expirar, em no máximo 15 minutos por padrão.

---

### User Story 3 - Recuperar a conta ao esquecer a senha (Priority: P1)

Quem esqueceu a senha informa o e-mail, recebe um token de uso único e define uma senha nova. A resposta nunca revela se o e-mail tem conta.

**Why this priority**: Hoje, quem esquece a senha perde a conta, e não há canal de suporte para isso. É um critério de aceite da issue.

**Independent Test**: No ambiente local, pedir a recuperação, ler o e-mail capturado, definir a senha nova e fazer login com ela.

**Acceptance Scenarios**:

1. **Given** um e-mail com conta, **When** a recuperação é pedida, **Then** a resposta é 202, e um e-mail com o token é enviado.
2. **Given** um e-mail sem conta, **When** a recuperação é pedida, **Then** a resposta é o mesmo 202, com o mesmo corpo, em tempo equivalente, e nenhum e-mail é enviado.
3. **Given** um token válido e uma senha que atende às regras, **When** a redefinição é feita, **Then** a senha muda, o token deixa de valer e todas as sessões da conta são encerradas.
4. **Given** um token expirado, já usado ou inexistente, **When** a redefinição é tentada, **Then** a resposta é a mesma, sem indicar o motivo, e a senha não muda.
5. **Given** dois pedidos de recuperação seguidos, **When** o segundo é atendido, **Then** só o token mais recente vale.
6. **Given** uma conta bloqueada por erros de senha, **When** a redefinição é concluída, **Then** o bloqueio e a contagem de erros são zerados.
7. **Given** uma redefinição concluída, **When** ela termina, **Then** a conta recebe um e-mail avisando da troca, para que a pessoa perceba um uso indevido.

---

### User Story 4 - Trocar a senha estando conectado (Priority: P2)

A pessoa conectada informa a senha atual e a senha nova.

**Why this priority**: É uma ação de higiene frequente e simples, depois das histórias que resolvem a perda de acesso.

**Independent Test**: Trocar a senha, fazer login com a nova e confirmar que a antiga e a sessão anterior deixaram de valer.

**Acceptance Scenarios**:

1. **Given** um token de acesso válido e a senha atual certa, **When** a troca é pedida com uma senha nova válida, **Then** a resposta é 204, a senha muda, e todas as sessões da conta são encerradas.
2. **Given** a senha atual errada, **When** a troca é pedida, **Then** a resposta é 400 com erro no campo da senha atual, e o erro conta para o bloqueio de conta.
3. **Given** uma senha nova fora das regras, ou igual à atual, **When** a troca é pedida, **Then** a resposta é 400, sem mudar nada.
4. **Given** nenhum token, ou um token inválido, **When** a troca é pedida, **Then** a resposta é 401.

---

### User Story 5 - Confirmar o e-mail (Priority: P3)

Depois do registro, a pessoa recebe um e-mail com um token e confirma que o endereço é dela. Pode pedir um reenvio.

**Why this priority**: Dá confiança ao endereço usado na recuperação, mas nenhuma outra história depende dela.

**Independent Test**: Registrar uma conta, ler o e-mail capturado, confirmar e ver a conta marcada como confirmada.

**Acceptance Scenarios**:

1. **Given** um registro bem-sucedido, **When** ele termina, **Then** um e-mail de confirmação é enviado, sem atrasar nem mudar a resposta do registro.
2. **Given** um token válido, **When** a confirmação é feita, **Then** a conta passa a ter o e-mail confirmado.
3. **Given** um token inválido, expirado ou já usado, **When** a confirmação é feita, **Then** a resposta é a mesma, sem indicar o motivo.
4. **Given** qualquer e-mail, com conta ou não, confirmada ou não, **When** o reenvio é pedido, **Then** a resposta é o mesmo 202, e só uma conta não confirmada recebe o e-mail.
5. **Given** uma conta não confirmada, **When** a recuperação de senha é concluída, **Then** o e-mail passa a confirmado, porque o token chegou àquele endereço.
6. **Given** uma conta sem o e-mail confirmado, **When** ela faz login, **Then** o login é aceito normalmente, e a resposta é a mesma de uma conta confirmada.

---

### Edge Cases

- **Duas renovações simultâneas com o mesmo token**: só uma é aceita, e a outra é tratada como reutilização, o que encerra a sessão. O par recém-emitido também deixa de renovar, e o cliente faz login de novo. Um cliente deve renovar uma vez de cada vez.
- **A resposta da renovação se perde na rede**: o cliente fica com um token já usado e, ao repetir, é tratado como reutilização. Precisa fazer login de novo (decisão do usuário: sem tolerância).
- **Clientes que não usam a renovação**: fazem login com mais frequência, porque o token de acesso passa a valer 15 minutos por padrão. O contrato não muda, pois a resposta já informa `expiraEm`, e a validade é configurável.
- **E-mail fora do ar**: as respostas não mudam, e o envio é tentado de novo depois. A falha fica em log e métrica, sem chegar ao cliente.
- **Token em logs**: nenhum token, senha ou endereço completo aparece em logs, métricas ou spans.
- **E-mail escrito com outra caixa ou espaços**: é o mesmo e-mail, como no login.
- **Conta criada antes deste requisito**: não tem e-mail confirmado nem sessão, e continua entrando normalmente pelo login. Quem tem um token de acesso antigo continua usando até expirar.
- **Usuários iniciais**: entram como confirmados, pois são configurados pela operação.
- **E-mail já cadastrado no registro**: continua respondendo 409, como no contrato v1. Mudar isso seria incompatível, então fica como limitação conhecida: a rota de registro revela e-mails cadastrados, e o limite do REQ-23 reduz o abuso.
- **Rotas públicas novas sob rajada**: seguem o limite de requisições do REQ-23, com estado único entre as instâncias.
- **Troca de senha repetida com a senha atual errada**: chega ao bloqueio de conta, e a resposta continua sendo o 400 da senha atual, sem revelar o bloqueio.

## Requirements *(mandatory)*

### Functional Requirements

**Renovação**

- **FR-001**: O login MUST devolver, por acréscimo ao contrato, um token de renovação e a data em que ele expira.
- **FR-002**: Apresentar um token de renovação válido MUST devolver um novo token de acesso e um novo token de renovação, e invalidar o apresentado (rotação), de forma atômica: duas apresentações simultâneas nunca renovam as duas.
- **FR-003**: O sistema MUST NOT guardar um token de renovação, de recuperação ou de confirmação de forma que um vazamento do banco o exponha.
- **FR-004**: Um token de renovação reutilizado MUST ser recusado com 401 e encerrar toda a sessão a que pertence, sem tolerância para repetição.
- **FR-005**: O token de renovação MUST valer 14 dias, renovados a cada rotação, e a sessão MUST durar no máximo 30 dias desde o login, por padrão e configuráveis. Ao fim dos 30 dias, é preciso fazer login de novo.
- **FR-006**: Token expirado, revogado, reutilizado ou inexistente MUST receber a mesma resposta 401.
- **FR-007**: A renovação MUST ler os perfis atuais da conta e MUST continuar funcionando com a conta bloqueada por erros de senha.

**Logout**

- **FR-008**: O logout MUST revogar a sessão do token de renovação informado, responder 204 em qualquer caso e não exigir token de acesso, para funcionar também com ele expirado.
- **FR-009**: O token de acesso ainda válido MUST continuar funcionando até expirar depois do logout, da troca e da redefinição de senha, sem consulta por requisição. A validade padrão do token de acesso MUST passar de 60 para 15 minutos, configurável.

**Senha**

- **FR-010**: A troca de senha MUST exigir autenticação e a senha atual, e a senha nova MUST seguir as regras do contrato v1 e ser diferente da atual.
- **FR-011**: A troca e a redefinição de senha MUST encerrar todas as sessões da conta.
- **FR-012**: Uma senha atual errada MUST contar para o bloqueio de conta do REQ-03, cujas regras não mudam.
- **FR-013**: O pedido de recuperação MUST responder 202 com o mesmo corpo e em tempo equivalente, com ou sem conta, e o envio do e-mail não pode ocorrer durante a requisição.
- **FR-014**: O token de recuperação MUST ser aleatório, longo o bastante para não ser adivinhado e suficiente para identificar a conta, sem que a chamada informe o e-mail. MUST ser de uso único e valer 30 minutos por padrão, configuráveis, e um pedido novo MUST invalidar os anteriores da mesma conta.
- **FR-015**: A redefinição concluída MUST zerar o bloqueio e a contagem de erros da conta, marcar o e-mail como confirmado e enviar um e-mail de aviso.
- **FR-016**: Token de recuperação inválido, expirado ou já usado MUST receber a mesma resposta, sem indicar o motivo.

**Confirmação de e-mail**

- **FR-017**: O registro MUST gerar um e-mail de confirmação, sem mudar a resposta nem o tempo da rota de registro.
- **FR-018**: O token de confirmação MUST seguir a mesma forma do token de recuperação, sem que a chamada informe o e-mail, ser de uso único e valer 24 horas por padrão, configuráveis.
- **FR-019**: O reenvio MUST responder 202 com o mesmo corpo em qualquer caso, e enviar só para conta existente e não confirmada.
- **FR-020**: O login MUST NOT exigir o e-mail confirmado. Nenhuma resposta existente muda por causa da confirmação.

**Envio de e-mail**

- **FR-021**: O envio MUST ser assíncrono e sobreviver a uma queda do serviço de e-mail ou do processo, com novas tentativas e sem duplicar o efeito do token.
- **FR-022**: Em Development e na demonstração, os e-mails MUST ir para um canal local de captura, que permita lê-los, sem sair para a internet.
- **FR-023**: A configuração do envio MUST ser validada na partida, como as demais opções, e a mensagem de erro MUST citar a chave, sem o valor.

**Proteção e observabilidade**

- **FR-024**: As rotas públicas novas MUST seguir o limite de requisições do REQ-23, com a mesma resposta 429 e o estado único. Os padrões, configuráveis, são: pedido de recuperação e reenvio, 3 por hora por origem mais e-mail e 20 por hora por origem; redefinição e confirmação, 10 por minuto por origem; renovação e logout, 120 por minuto por origem.
- **FR-025**: Respostas de rotas públicas MUST NOT revelar se um e-mail tem conta, exceto o 409 já existente no registro.
- **FR-026**: O sistema MUST registrar em log, com o e-mail mascarado, renovação recusada por reutilização, logout, troca, pedido e conclusão de recuperação e confirmação, e expor métricas de renovações por resultado e de e-mails por resultado, sem token, senha, e-mail completo ou endereço de origem.
- **FR-027**: Tokens vencidos ou revogados MUST ser removidos periodicamente, sem intervenção da operação.

**Contrato e compatibilidade**

- **FR-028**: O contrato v1 MUST mudar só por acréscimo. Campos novos na resposta do login e rotas novas não alteram o que já existia, e quem ignora o token de renovação continua funcionando.
- **FR-029**: Todas as rotas novas MUST ter testes de sucesso e de falha, com 401 e 403 nas protegidas.

### Key Entities

- **Sessão de renovação**: agrupa os tokens de renovação gerados a partir de um login: conta, início, limite de 30 dias e se foi encerrada.
- **Token de renovação**: pertence a uma sessão, em forma não legível, com validade e o momento em que foi usado.
- **Token de uso único**: de recuperação de senha ou de confirmação de e-mail: conta, finalidade, forma não legível, validade e se foi usado.
- **Solicitação de e-mail**: pedido de envio pendente, com destino, tipo e tentativas, sem o token nem o corpo da mensagem. O token é gerado só no momento de enviar.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% das renovações válidas devolvem um par novo, e 100% das reapresentações de um token usado são recusadas.
- **SC-002**: Em 50 renovações simultâneas com o mesmo token, exatamente uma é aceita.
- **SC-003**: Depois do logout, 100% das tentativas de renovar com aquele token são recusadas.
- **SC-004**: No ambiente local, a recuperação de senha funciona de ponta a ponta em menos de 2 minutos, do pedido ao login com a senha nova.
- **SC-005**: Com e sem conta, o corpo e o status do pedido de recuperação e do reenvio são idênticos, e a diferença entre as medianas de tempo fica abaixo de 50 ms em 100 pedidos de cada caso.
- **SC-006**: Um despejo das tabelas de tokens não contém nenhum token utilizável.
- **SC-007**: Com o serviço de e-mail fora do ar, 100% das rotas continuam respondendo como de costume, e o e-mail pendente é entregue em até 5 minutos depois da volta.
- **SC-008**: Nenhum log, métrica ou span contém token, senha, e-mail completo ou endereço de origem.
- **SC-009**: Todos os testes existentes continuam passando sem afrouxar asserções, exceto os que conferem a validade de 60 minutos, atualizados para os 15 minutos desta decisão, e o comparador de contrato não aponta mudança incompatível.

## Assumptions

- A API não tem interface própria. O e-mail traz o token e, se houver um endereço de aplicação configurado, um link para ela. Telas de redefinição e de confirmação ficam fora do escopo.
- O token de renovação é trafegado no corpo das respostas, como o token de acesso, porque os clientes da API não dependem de navegador.
- Encerrar todas as sessões na troca de senha inclui a do dispositivo que fez a troca. É mais simples e não deixa dúvida; a pessoa faz login de novo.
- Não há lista de sessões ativas nem "encerrar todas" como rota nesta versão.
- O provedor real de e-mail em produção é uma decisão de implantação, e a escolha do mecanismo de envio fica para o plano. Qualquer dependência nova exige justificativa escrita (constituição).
- Até 15 minutos depois do logout ou da troca de senha, um token de acesso vazado continua valendo. É uma limitação aceita, a registrar no fluxo 1.
- A rotação da chave do JWT (#60), MFA e login social ficam fora do escopo.
- O documento do fluxo 1 é atualizado no mesmo PR, e o `README.md` não recebe acréscimos sem permissão.
