# Feature Specification: Gestão de usuários e perfis pelo Admin

**Feature Branch**: `feature/req-25-gestao-usuarios`

**Requisito**: REQ-25 (GitHub issue #54 — "[Usuários] Gestão de usuários e perfis pelo Admin")

**Created**: 2026-11-06

**Status**: Draft

**Input**: User description: "Permitir que um Admin gerencie os usuários e os perfis pela API: listagem e consulta, criação de usuário interno, atribuição e remoção de perfis sem deixar o sistema sem Admin, desativação e reativação sem apagar o histórico, efeito em tokens já emitidos e auditoria mínima (issue #54)"

## Contexto

O registro público sempre cria o perfil Cliente (REQ-01, REQ-03). Os perfis Admin e OperadorLogistica só existem pela carga inicial de usuários, configurada pela operação. Não há como criar um usuário interno com o sistema no ar, promover ou rebaixar alguém, nem impedir o acesso de uma pessoa que saiu da operação.

Este requisito acrescenta ao contrato v1 as rotas de gestão de usuários, só para o Admin, e faz o login e a sessão do REQ-24 respeitarem uma conta desativada.

## Clarifications

### Session 2026-11-06

Respostas dadas pelo usuário durante o specify:

- Q: Qual é o efeito de uma desativação ou de uma retirada de perfil no token de acesso já emitido? → A: O token continua valendo até expirar, em no máximo 15 minutos por padrão, sem consulta por requisição (REQ-24). A exceção são as rotas de gestão, que conferem no banco, a cada chamada, se quem chama ainda é um Admin ativo.
- Q: Um Admin pode desativar a própria conta ou retirar o próprio perfil Admin? → A: Não. Outro Admin precisa fazer isso, e a operação sobre a própria conta é recusada com 409.
- Q: Como a pessoa criada pelo Admin obtém a senha? → A: Por um convite enviado por e-mail, com um token de uso único válido por 72 horas, configurável. A conta nasce sem senha, o Admin nunca a conhece, e um convite vencido é resolvido pela recuperação de senha.

Respostas dadas pelo usuário no clarify:

- Q: Uma conta pode ter o perfil Cliente junto com um perfil interno (Admin ou OperadorLogistica)? → A: Não. Admin e OperadorLogistica podem se combinar entre si, mas não com Cliente. Uma conta interna só nasce pela criação feita pelo Admin, e atribuir um perfil do outro grupo recebe 409.
- Q: Enquanto uma conta de Cliente estiver desativada, os webhooks cadastrados por ela continuam recebendo os avisos de status dos pedidos? → A: Não. Os avisos gerados durante a desativação não são entregues nem reenviados depois. A reativação retoma os envios a partir dali, com os mesmos destinos e segredos.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consultar os usuários (Priority: P1)

O Admin lista os usuários, filtra por perfil, situação ou texto, e consulta um usuário específico. Nenhuma resposta traz senha, hash ou outro dado de credencial.

**Why this priority**: É a base das demais histórias: o Admin precisa encontrar a conta antes de alterá-la, e hoje só consegue isso olhando o banco.

**Independent Test**: Com contas de perfis diferentes, listar com e sem filtros, percorrer as páginas e consultar uma conta pelo id, conferindo que nenhum dado de credencial aparece.

**Acceptance Scenarios**:

1. **Given** um Admin autenticado, **When** ele lista os usuários, **Then** recebe uma página com id, nome, e-mail, perfis, situação, confirmação do e-mail e convite pendente de cada conta, e o indicador para a página seguinte.
2. **Given** a listagem, **When** o Admin filtra por perfil, por situação ou por um trecho do nome ou do e-mail, **Then** só as contas que atendem a todos os filtros aparecem.
3. **Given** o id de uma conta existente, **When** o Admin a consulta, **Then** recebe os mesmos dados da listagem.
4. **Given** um id sem conta, **When** o Admin o consulta, **Then** a resposta é 404. **Given** um id mal formado, **Then** a resposta é 400.
5. **Given** um OperadorLogistica ou um Cliente autenticado, **When** ele chama qualquer rota de gestão, **Then** a resposta é 403, e sem token ou com token inválido é 401.
6. **Given** um token de Admin ainda válido de quem perdeu o perfil Admin ou foi desativado, **When** ele chama qualquer rota de gestão, **Then** a resposta é 403, mesmo antes de o token expirar.

---

### User Story 2 - Criar um usuário interno (Priority: P1)

O Admin cria uma conta com o perfil Admin ou OperadorLogistica, sem depender de configuração nem de reinício do sistema. A pessoa recebe um convite por e-mail e define a própria senha.

**Why this priority**: É a lacuna que motivou a issue: em produção, a operação não tem outro meio de ganhar usuários internos.

**Independent Test**: No ambiente local, criar um OperadorLogistica, ler o e-mail capturado, definir a senha, fazer login e confirmar que ele usa as rotas de operação e recebe 403 nas de gestão.

**Acceptance Scenarios**:

1. **Given** um Admin autenticado e dados válidos, **When** ele cria um usuário com o perfil OperadorLogistica ou Admin, **Then** a resposta é 201, com a localização e os dados da conta, sem nenhum dado de credencial, e um e-mail de convite é pedido sem atrasar a resposta.
2. **Given** um e-mail já cadastrado, sem diferenciar maiúsculas e sem os espaços das pontas, **When** a criação é pedida, **Then** a resposta é o 409 de e-mail já cadastrado, já existente no contrato.
3. **Given** o perfil Cliente ou um perfil desconhecido, **When** a criação é pedida, **Then** a resposta é 400: conta de Cliente nasce no registro público.
4. **Given** nome ou e-mail fora das regras do contrato v1, **When** a criação é pedida, **Then** a resposta é 400 com o erro no campo, e nenhuma conta é criada.
5. **Given** duas criações simultâneas com o mesmo e-mail, **When** elas terminam, **Then** uma recebe 201 e a outra 409.
6. **Given** um token de convite válido e uma senha que atende às regras do contrato v1, **When** a pessoa define a senha, **Then** ela consegue fazer login, o e-mail fica confirmado e o token deixa de valer.
7. **Given** um token de convite vencido, já usado ou inexistente, **When** a definição é tentada, **Then** a resposta é a mesma, sem indicar o motivo, e nenhuma senha é definida.
8. **Given** uma conta que ainda não definiu a senha, **When** alguém tenta o login, **Then** a resposta é o mesmo 401 de credenciais inválidas.
9. **Given** um convite vencido, **When** a pessoa usa a recuperação de senha com o e-mail da conta, **Then** recebe um token, define a senha e passa a ter acesso, sem precisar de uma nova ação do Admin.

---

### User Story 3 - Atribuir e retirar perfis (Priority: P1)

O Admin promove ou rebaixa uma conta interna, entre Admin e OperadorLogistica. Uma conta de Cliente nunca vira interna, e o sistema nunca fica sem um Admin capaz de administrá-lo.

**Why this priority**: É a regra central da issue. Sem ela, um único engano tira de todos o acesso à gestão, e a recuperação exigiria mexer direto no banco.

**Independent Test**: Com dois Admins que também têm o perfil OperadorLogistica, retirar o perfil Admin de um, confirmar que o outro não pode retirar o próprio, e conferir os perfis novos depois de uma renovação de sessão.

**Acceptance Scenarios**:

1. **Given** uma conta interna sem o perfil interno pedido, **When** o Admin o atribui, **Then** a conta passa a tê-lo, e a resposta traz os perfis atuais.
2. **Given** uma conta com dois ou mais perfis, **When** o Admin retira um deles, **Then** a conta perde esse perfil e mantém os demais.
3. **Given** o único Admin ativo, **When** ele tenta retirar o próprio perfil Admin, **Then** a resposta é 409 com o código do último Admin, e nada muda.
4. **Given** uma conta com um único perfil, **When** ele é retirado, **Then** a resposta é 409 com um código próprio: toda conta mantém ao menos um perfil.
5. **Given** um perfil que a conta já tem, ou que já não tem, **When** a atribuição ou a remoção é repetida, **Then** a resposta é a mesma de sucesso, nada muda e nenhuma auditoria é registrada.
6. **Given** dois Admins ativos, ambos também com o perfil OperadorLogistica, **When** cada um tenta, ao mesmo tempo, retirar o perfil Admin do outro, **Then** só uma operação é aceita, a outra recebe 403 porque quem a pediu já não é Admin, e continua havendo um Admin ativo.
7. **Given** um perfil alterado, **When** a sessão da conta é renovada, **Then** o novo token de acesso traz os perfis atuais (REQ-24, FR-007).
8. **Given** dois ou mais Admins ativos, **When** um deles tenta retirar o próprio perfil Admin, **Then** a resposta é 409 com o código de operação sobre a própria conta, e nada muda.
9. **Given** uma conta de Cliente, **When** o Admin tenta atribuir a ela um perfil interno, ou **Given** uma conta interna, **When** ele tenta atribuir o perfil Cliente, **Then** a resposta é 409 com um código próprio, e nada muda.

---

### User Story 4 - Desativar e reativar uma conta (Priority: P2)

O Admin impede o acesso de uma pessoa sem apagar a conta nem o histórico dos pedidos, e pode devolver o acesso depois.

**Why this priority**: Fecha o ciclo de vida da conta e é um critério de aceite da issue. É o único meio de tirar o acesso de uma conta com um só perfil. Fica depois das demais porque não é preciso para criar usuários nem para ajustar os perfis, mas o PR só sai com ela.

**Independent Test**: Desativar uma conta com sessão aberta e pedidos, confirmar que login, renovação e recuperação de senha não devolvem o acesso e que os pedidos continuam consultáveis pelo Admin; reativar e fazer login com a mesma senha.

**Acceptance Scenarios**:

1. **Given** uma conta ativa, **When** o Admin a desativa, **Then** a resposta traz a situação nova, e todas as sessões da conta são encerradas.
2. **Given** uma conta desativada, **When** a pessoa tenta o login, mesmo com a senha certa, **Then** a resposta é o mesmo 401 de credenciais inválidas, em tempo equivalente, sem revelar a desativação.
3. **Given** uma sessão anterior à desativação, **When** a renovação é tentada, **Then** a resposta é o mesmo 401 de sessão inválida.
4. **Given** uma conta desativada, **When** a recuperação de senha ou o reenvio da confirmação são pedidos, **Then** a resposta é o mesmo 202 de sempre, e nenhum e-mail é enviado.
5. **Given** um token de recuperação, de confirmação ou de convite emitido antes da desativação, **When** ele é usado, **Then** a resposta é o mesmo 400 de token inválido: a senha não muda, e a conta continua desativada.
6. **Given** uma conta desativada com pedidos, **When** um Admin ou um OperadorLogistica consulta esses pedidos, **Then** eles aparecem como antes, e os pedidos em andamento seguem o fluxo normal.
7. **Given** o único Admin ativo, **When** ele tenta desativar a própria conta, **Then** a resposta é 409 com o código do último Admin, e nada muda. **Given** dois ou mais Admins ativos, **When** um deles tenta desativar a própria conta, **Then** a resposta é 409 com o código de operação sobre a própria conta.
8. **Given** uma conta desativada, **When** o Admin a reativa, **Then** o login volta a funcionar com a mesma senha e os mesmos perfis, e as sessões encerradas continuam encerradas.
9. **Given** uma conta já desativada, ou já ativa, **When** a mesma operação é repetida, **Then** a resposta é a mesma de sucesso, nada muda e nenhuma auditoria é registrada.
10. **Given** uma conta desativada com um token de acesso ainda válido, **When** ela usa uma rota fora da gestão, **Then** o token continua valendo até expirar, em no máximo 15 minutos por padrão, exceto na troca de senha, que é recusada como senha atual incorreta.

---

### Edge Cases

- **Bloqueio por erros de senha e desativação são coisas distintas**: a redefinição de senha zera o bloqueio, e essa regra do REQ-24 não pode reativar uma conta desativada. A reativação também não mexe no bloqueio.
- **Registro público com o e-mail de uma conta desativada**: continua respondendo o 409 de sempre. A conta existe, e nada indica que está desativada.
- **Carga inicial**: um usuário inicial que já existe nunca é alterado (REQ-03). Uma conta da carga desativada ou rebaixada continua assim depois de reiniciar o sistema.
- **Usuário inicial removido da configuração**: a conta continua existindo. A configuração só cria o que falta.
- **Sistema sem nenhum Admin ativo**: não é alcançável pelas rotas de gestão. Se ocorrer por uma carga vazia ou por dado anterior, a carga inicial continua sendo o caminho para criar o primeiro Admin.
- **Admin bloqueado por erros de senha**: continua contando como Admin ativo, porque o bloqueio é temporário.
- **Admin convidado que ainda não definiu a senha**: não conta como Admin ativo, porque ainda não consegue entrar. Passa a contar quando define a senha.
- **Reativação de uma conta que nunca definiu a senha**: o convite foi invalidado na desativação, e a pessoa usa a recuperação de senha.
- **Convite e recuperação ao mesmo tempo**: o primeiro a definir a senha invalida o outro token.
- **Serviço de e-mail fora do ar na criação**: a criação responde como de costume, e o convite é enviado depois (REQ-24, FR-021). A validade do token conta a partir do envio. Depois de 24 horas sem conseguir enviar, a solicitação é descartada, como as demais, e a conta fica com o convite pendente; a pessoa usa a recuperação de senha.
- **Rebaixar um Admin que só tem esse perfil**: retirar o perfil recebe `USUARIO_ULTIMO_PERFIL`. O caminho é atribuir OperadorLogistica e depois retirar Admin.
- **Rotas administrativas de outros REQs (reenvio da DLQ e catálogo)**: seguem só o perfil do token, então um Admin removido ainda as usa até o token expirar. É a limitação aceita na Q1, a registrar no fluxo 1.
- **Conta sem nenhum perfil, de dado inconsistente anterior**: aparece na listagem com a lista vazia, e o Admin pode atribuir um perfil de qualquer grupo.
- **Conta que já mistura Cliente e perfil interno, de dado anterior**: continua funcionando como está. O Admin pode retirar um dos perfis, mas não atribuir outro enquanto os grupos estiverem misturados.
- **Quem é cliente e também da operação**: usa duas contas, com e-mails diferentes.
- **Pedidos em andamento de uma conta desativada**: seguem até o fim, e o estorno devido também. A desativação tira o acesso, e não desfaz compromissos.
- **Destinos de notificação da conta desativada**: os destinos próprios dela não recebem os avisos gerados durante a desativação nem os que estavam pendentes ao desativar, e nada disso é reenviado depois. Os destinos e os segredos são mantidos, e a reativação retoma os envios a partir dali. Os destinos globais são da operação e não mudam, mesmo que a conta tenha cadastrado algum deles.
- **Eventos em tempo real**: a conexão de uma conta desativada termina, no máximo, quando o token de acesso expira, como já ocorre hoje. Depois de reativar, uma reconexão com `Last-Event-ID` pode trazer avisos do período desativado, dentro da retenção. É o esperado: a suspensão vale para os webhooks, e a conta já recuperou o acesso a esses pedidos.
- **E-mail pendente de envio quando a conta é desativada**: não é enviado.
- **Falha no meio de uma operação**: 500 e nada muda. A alteração, o encerramento das sessões e a invalidação dos tokens acontecem juntos.

## Requirements *(mandatory)*

### Functional Requirements

**Acesso**

- **FR-001**: Todas as rotas deste requisito MUST exigir token válido com o perfil Admin. Outro perfil recebe 403, e a falta de token ou um token inválido recebe 401.
- **FR-002**: Cada chamada às rotas de gestão MUST conferir no banco se quem chama ainda é um Admin ativo, e responder 403 se não for. Nas demais rotas, o token de acesso já emitido MUST continuar valendo até expirar depois de uma desativação ou de uma retirada de perfil, sem consulta por requisição (REQ-24, FR-009).
- **FR-003**: Um Admin MUST NOT desativar a própria conta nem retirar o próprio perfil Admin. A operação MUST ser recusada com 409 e um código próprio, exceto quando ele é o único Admin ativo, caso em que vale o código do último Admin (FR-013). Atribuir a si mesmo um perfil, ou retirar de si um perfil que não seja o Admin, é permitido.

**Consulta**

- **FR-004**: O Admin MUST poder listar os usuários com paginação por cursor, como nos pedidos e nos produtos, em ordem estável, com filtros combináveis por perfil, por situação e por trecho do nome ou do e-mail, sem diferenciar maiúsculas nem acentos.
- **FR-005**: O Admin MUST poder consultar um usuário pelo id.
- **FR-006**: As respostas MUST trazer id, nome, e-mail, perfis, situação, confirmação do e-mail e se o convite está pendente, isto é, se a conta ainda não definiu a senha, e MUST NOT trazer senha, hash, carimbo de segurança, token ou outro dado de credencial.

**Criação**

- **FR-007**: O Admin MUST poder criar uma conta com um perfil, Admin ou OperadorLogistica. Nome e e-mail seguem as regras do contrato v1.
- **FR-008**: A conta criada pelo Admin MUST nascer sem senha, e o Admin MUST NOT informar nem conhecer a senha dela. A criação MUST pedir um e-mail de convite, enviado de forma assíncrona como os demais (REQ-24, FR-021), com um token de uso único que siga a forma do token de recuperação e valha 72 horas por padrão, configuráveis. Definir a senha pelo convite MUST seguir as regras de senha do contrato v1 e marcar o e-mail como confirmado. Token inválido, vencido ou já usado MUST receber a mesma resposta, sem indicar o motivo. A conta sem senha MUST receber o mesmo 401 no login, e a recuperação de senha MUST funcionar para ela.
- **FR-009**: A criação MUST ser atômica: nunca fica uma conta sem perfil, e um e-mail repetido recebe o 409 existente, inclusive em criações simultâneas.

**Perfis**

- **FR-010**: O Admin MUST poder atribuir e retirar os perfis Admin e OperadorLogistica de uma conta interna, e eles podem se combinar. O perfil Cliente MUST NOT se combinar com um perfil interno: uma atribuição que misturaria os grupos MUST ser recusada com 409 e um código próprio. Uma conta interna só nasce pela criação feita pelo Admin (FR-007).
- **FR-011**: Toda conta MUST manter ao menos um perfil. A retirada do único perfil MUST ser recusada com 409 e um código próprio.
- **FR-012**: Repetir uma atribuição ou uma remoção já em vigor MUST responder com sucesso, sem alterar nada nem registrar auditoria.

**Último Admin**

- **FR-013**: Nenhuma operação MUST deixar o sistema sem ao menos um Admin ativo. A retirada do perfil e a desativação que levariam a isso MUST ser recusadas com 409 e um código próprio.
- **FR-014**: A regra do último Admin MUST valer sob concorrência, entre todas as instâncias: as operações que retiram um Admin ativo são avaliadas uma de cada vez, conferindo de novo quem chama (FR-002), e nunca deixam o sistema sem Admin ativo.

**Desativação e reativação**

- **FR-015**: O Admin MUST poder desativar e reativar uma conta, sem apagar a conta, os perfis, os pedidos ou o histórico. Repetir a operação MUST responder com sucesso, sem alterar nada nem registrar auditoria.
- **FR-016**: A desativação MUST, de forma atômica, encerrar todas as sessões da conta e invalidar os tokens de recuperação, de confirmação e de convite pendentes.
- **FR-017**: Uma conta desativada MUST NOT conseguir login nem renovação. As respostas MUST ser as mesmas 401 de hoje, em tempo equivalente ao de uma senha errada, sem revelar a desativação.
- **FR-018**: Uma conta desativada MUST NOT receber e-mails, e MUST NOT ter a senha alterada por redefinição ou troca. As respostas públicas MUST continuar iguais às de uma conta inexistente ou de um token inválido.
- **FR-019**: A desativação MUST ser independente do bloqueio por erros de senha: nenhuma das duas situações altera a outra, e a redefinição de senha não reativa a conta.
- **FR-020**: Os pedidos de uma conta desativada MUST continuar consultáveis por quem já tem acesso a eles e seguir o fluxo normal. Os destinos de notificação próprios da conta MUST NOT receber avisos durante a desativação, nem os pendentes ao desativar, e esses avisos MUST NOT ser reenviados depois. A reativação MUST retomar os envios a partir dali, com os mesmos destinos e segredos.

**Auditoria**

- **FR-021**: Toda alteração efetiva (criação, atribuição, remoção, desativação e reativação) e toda recusa pela regra do último Admin ou da própria conta MUST gerar um log com a ação, o id de quem fez, o id da conta alterada e, quando houver, o perfil. O log MUST NOT trazer nome, e-mail, endereço de origem ou credencial.

**Contrato e compatibilidade**

- **FR-022**: O contrato v1 MUST mudar só por acréscimo, com os erros em `ProblemDetails` e mensagens em português. As respostas das rotas existentes não mudam para as contas ativas.
- **FR-023**: As rotas de gestão MUST ficar fora do limite de requisições do REQ-23, como a troca de senha, por serem protegidas e restritas ao Admin. A definição da senha pelo convite é pública e MUST seguir o limite da redefinição de senha, de 10 por minuto por origem por padrão, com a mesma resposta 429.
- **FR-024**: Todas as rotas de gestão MUST ter testes de sucesso, de 401 e de 403, a definição da senha pelo convite MUST ter testes de sucesso e de falha, e as regras de negócio MUST ter testes unitários (constituição).
- **FR-025**: O documento do fluxo 1 (autenticação) e os demais fluxos afetados MUST ser atualizados no mesmo PR, e a limitação "Não há rota de gestão de usuários ou de perfis" MUST sair da lista, com a referência a esta issue.

### Key Entities

- **Usuário**: conta existente, com nome, e-mail, perfis e confirmação do e-mail. Ganha a **situação** (ativa ou desativada), independente do bloqueio por erros de senha.
- **Perfil**: Admin, OperadorLogistica ou Cliente. Uma conta tem um ou mais, de um único grupo: internos (Admin e OperadorLogistica) ou Cliente.
- **Admin ativo**: conta com o perfil Admin, a situação ativa e a senha já definida. É o que a regra do último Admin conta.
- **Token de convite**: token de uso único para definir a primeira senha: conta, forma não legível, validade e se foi usado.
- **Registro de auditoria**: entrada de log com a ação, o id de quem fez, o id da conta alterada e o perfil, quando houver. Não é uma entidade guardada no banco.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: No ambiente local, da criação pelo Admin ao primeiro login com a senha definida pelo convite, o usuário interno obtém acesso em menos de 2 minutos, sem reiniciar o sistema nem alterar configuração.
- **SC-002**: 100% das rotas de gestão respondem 401 sem token, 403 para OperadorLogistica e Cliente, e 403 para quem deixou de ser Admin ativo, mesmo com o token ainda válido.
- **SC-003**: Em 50 rodadas de operações simultâneas que tentam retirar o perfil ou desativar os Admins restantes, sobra sempre ao menos um Admin ativo.
- **SC-004**: 100% das tentativas de login e de renovação de uma conta desativada são recusadas, com status e corpo idênticos aos de credenciais inválidas e de sessão inválida, e a diferença entre as medianas de tempo do login fica abaixo de 50 ms em 100 tentativas de cada caso.
- **SC-005**: Depois de desativar e reativar uma conta, 100% dos pedidos dela continuam presentes e iguais.
- **SC-006**: Nenhuma resposta das rotas de gestão contém dado de credencial, nenhum log de auditoria contém nome, e-mail ou endereço de origem, e um despejo do banco não contém nenhum token de convite utilizável.
- **SC-007**: Cada alteração efetiva gera exatamente um log de auditoria, e uma repetição sem efeito não gera nenhum.
- **SC-008**: Todos os testes existentes continuam passando sem afrouxar asserções, e o comparador de contrato não aponta mudança incompatível.

## Assumptions

- O primeiro Admin de um ambiente continua vindo da carga inicial de usuários, que já pode ser configurada em qualquer ambiente. Este requisito não muda isso.
- O Admin não edita o nome nem o e-mail de outra conta nesta versão.
- Não há exclusão de conta: a desativação é o único meio de tirar o acesso, o que preserva o histórico dos pedidos.
- Desativar uma conta com o perfil Cliente é permitido, e os pedidos dela seguem as mesmas regras das demais contas.
- A desativação e a reativação não enviam e-mail à pessoa. Um aviso pode ser acrescentado depois.
- Não há rota para reenviar o convite: a recuperação de senha cobre o convite vencido.
- A auditoria fica só nos logs, como pede a issue; não há rota de consulta do histórico de alterações.
- Autogestão do perfil pelo Cliente, SSO e provedores externos ficam fora do escopo.
- O `README.md` não recebe acréscimos sem permissão.
