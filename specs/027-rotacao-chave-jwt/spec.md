# Feature Specification: Rotação da chave de assinatura do JWT

**Feature Branch**: `feature/req-27-rotacao-chave-jwt`

**Requisito**: REQ-27 (GitHub issue #60 — "[Segurança] Rotação da chave de assinatura do JWT")

**Created**: 2026-11-09

**Status**: Draft

**Input**: User description: "Permitir a rotação da chave de assinatura do JWT sem indisponibilidade: várias chaves aceitas na validação, cada uma com identificador (kid), uma chave ativa para emitir, procedimento de rotação documentado, validação na partida sem mostrar valores, e estudo da assinatura assimétrica e do cofre de segredos (issue #60)"

## Contexto

O token de acesso é assinado com uma chave simétrica única, `Jwt:ChaveAssinatura` (REQ-03). A mesma
chave emite e valida. Trocá-la invalida na hora todos os tokens de acesso emitidos, e uma chave vazada
não tem como ser substituída sem que todos os clientes recebam 401 ao mesmo tempo.

Duas coisas do sistema atual limitam o impacto e entram no desenho:

- A renovação da sessão (REQ-24) usa um token de renovação opaco, guardado no banco, que não depende
  da chave do JWT. Um cliente cujo token de acesso foi recusado consegue renová-lo sem digitar a senha.
- A chave que assina o cursor da listagem de pedidos (REQ-21) é derivada hoje da `Jwt:ChaveAssinatura`.
  A [documentação do fluxo 3](../../docs/fluxos/03-consulta-e-mudanca-de-status.md#limitações) já
  registra que a rotação da #60 invalidaria os cursores em uso.

Este requisito passa a aceitar várias chaves na validação, cada uma identificada no cabeçalho do token,
com uma só chave ativa para emitir, e documenta o procedimento de rotação. O estudo avalia a assinatura
assimétrica e a origem das chaves fora da configuração da aplicação.

## Clarifications

### Session 2026-11-09

Respostas dadas pelo usuário durante o specify:

- Q: Um token sem identificador de chave, emitido antes da implantação deste requisito, é aceito na
  transição? → A: Não. Todo token sem identificador é recusado com 401, a partir da implantação. Cada
  cliente recebe um 401 e renova a sessão uma vez, sem digitar a senha.
- Q: A configuração antiga (`Jwt:ChaveAssinatura` sozinha) continua aceita? → A: Não. A partida falha
  com ela e exige o formato novo (conjunto de chaves e chave ativa). Desenvolvimento, testes e a
  documentação migram no mesmo PR.
- Q: Como fica o cursor da listagem de pedidos, cuja chave deriva hoje da chave do JWT? → A: Ganha uma
  chave própria, configurável e separada do JWT, e a rotação do JWT não o afeta. Sem ela configurada, a
  chave do cursor continua derivada, agora da chave ativa do JWT.
- Q: O procedimento de rotação entra no README, como pede a issue? → A: Não. Vai para `docs/`, e o README
  só ganha o link na linha `docs/` da seção Estrutura.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rotacionar a chave sem derrubar as sessões (Priority: P1)

O responsável pela operação gera uma chave nova e a coloca em uso seguindo o procedimento documentado:
acrescenta a nova chave, passa a emitir com ela e, depois da validade máxima de um token de acesso,
retira a antiga. Durante toda a troca, quem já está conectado continua usando a API sem novo login e
sem nenhum 401.

**Why this priority**: É o objetivo da issue. Sem isso, a troca da chave é um evento de indisponibilidade
para todos os clientes, e por isso não é feita nem quando deveria.

**Independent Test**: Emitir um token com a chave A; configurar A e B, com B ativa; confirmar que o token
de A ainda é aceito e que os tokens novos saem com B; retirar A e confirmar que os tokens de B continuam
aceitos.

**Acceptance Scenarios**:

1. **Given** as chaves A (ativa) e B (aceita só na validação), **When** um token é emitido, **Then** ele é
   assinado com A e traz no cabeçalho o identificador de A.
2. **Given** um token emitido com A e a configuração já mudada para B ativa e A aceita, **When** o token é
   apresentado antes de expirar, **Then** a requisição é atendida normalmente.
3. **Given** a configuração com B ativa e A aceita, **When** um token é emitido, **Then** ele é assinado
   com B e traz o identificador de B.
4. **Given** a chave A retirada da configuração, **When** um token assinado com B é apresentado, **Then**
   a requisição é atendida normalmente.
5. **Given** uma instalação que segue as três etapas do procedimento, esperando a validade máxima do token
   antes de retirar A, **When** a troca termina, **Then** nenhuma requisição com token válido recebeu 401
   por causa da troca.

---

### User Story 2 - Recusar o token de uma chave retirada (Priority: P1)

Depois que uma chave é retirada, de forma planejada ou às pressas porque vazou, qualquer token assinado
com ela é recusado com 401, a mesma resposta de um token inválido. O cliente legítimo renova a sessão e
recebe um token assinado com a chave ativa.

**Why this priority**: É o que dá valor à rotação quando a chave vaza: quem tem a chave antiga deixa de
conseguir forjar tokens aceitos. É também um critério de aceite da issue.

**Independent Test**: Emitir um token com a chave A, retirar A da configuração e confirmar o 401; renovar
a sessão e confirmar que o novo token funciona.

**Acceptance Scenarios**:

1. **Given** a chave A retirada, **When** um token assinado com A e ainda dentro da validade é apresentado,
   **Then** a resposta é 401, com o mesmo corpo de um token inválido, sem indicar o motivo.
2. **Given** um token forjado com a chave A depois da retirada, **When** ele é apresentado, **Then** a
   resposta é 401.
3. **Given** um token com um identificador de chave desconhecido ou sem identificador, **When** ele é
   apresentado, **Then** a resposta é 401, inclusive para um token emitido antes da implantação deste
   requisito.
4. **Given** a retirada de emergência da chave A, **When** o cliente com token de A recebe 401 e renova a
   sessão com o token de renovação, **Then** a renovação funciona e o novo token de acesso é assinado com
   a chave ativa, sem nova senha.

---

### User Story 3 - Configuração errada barrada na partida (Priority: P2)

Uma configuração de chaves inválida impede a aplicação de subir, com uma mensagem que diz o que está
errado sem mostrar o valor de nenhuma chave.

**Why this priority**: A rotação mexe na configuração de segurança em produção. Um erro nela (chave curta,
identificador repetido, chave ativa ausente) precisa aparecer na partida, e não como 401 para todos os
clientes ou como token assinado com uma chave fraca.

**Independent Test**: Subir a aplicação com cada tipo de configuração inválida e confirmar que ela não
sobe e que a mensagem não contém o valor da chave.

**Acceptance Scenarios**:

1. **Given** nenhuma chave configurada, **When** a aplicação sobe, **Then** a partida falha com a mensagem
   de chave obrigatória.
2. **Given** uma chave com menos de 32 bytes, **When** a aplicação sobe, **Then** a partida falha citando o
   identificador da chave, sem o valor.
3. **Given** duas chaves com o mesmo identificador, **When** a aplicação sobe, **Then** a partida falha
   citando o identificador repetido.
4. **Given** uma chave ativa cujo identificador não está entre as chaves configuradas, ou nenhuma chave
   ativa, **When** a aplicação sobe, **Then** a partida falha.
5. **Given** uma chave com identificador vazio ou fora do formato permitido, **When** a aplicação sobe,
   **Then** a partida falha.
6. **Given** só a configuração antiga (`Jwt:ChaveAssinatura`), **When** a aplicação sobe, **Then** a
   partida falha com uma mensagem que indica o formato novo.
7. **Given** uma chave própria do cursor com menos de 32 bytes, **When** a aplicação sobe, **Then** a
   partida falha, sem mostrar o valor.

---

### User Story 4 - Procedimento e estudo documentados (Priority: P2)

Quem opera o sistema encontra o procedimento de rotação passo a passo, inclusive o de emergência, e o
resultado do estudo sobre assinatura assimétrica e cofre de segredos, com a recomendação para um
requisito futuro.

**Why this priority**: A rotação só funciona se for feita na ordem certa e esperando o tempo certo. O
estudo é pedido pela issue, mas não muda o comportamento entregue.

**Independent Test**: Seguir o procedimento documentado em uma instalação local e chegar ao resultado da
User Story 1.

**Acceptance Scenarios**:

1. **Given** o documento do procedimento, **When** ele é lido, **Then** ele traz as três etapas, o tempo
   mínimo de espera antes da retirada (validade máxima do token mais a tolerância de relógio), o
   procedimento de emergência, a migração da configuração antiga e o efeito em cada etapa.
2. **Given** o estudo, **When** ele é lido, **Then** ele compara a assinatura simétrica atual com RS256 e
   ES256 (publicação da chave pública, tamanho do token, custo, impacto nos clientes) e as origens de
   chave fora da configuração, com uma recomendação.
3. **Given** o documento do fluxo 1 (autenticação), **When** ele é lido, **Then** ele descreve as chaves,
   a recusa por chave retirada e a validação na partida.

### Edge Cases

- **Uma só chave configurada**: é a situação normal fora da rotação; ela é a ativa.
- **Chave aceita que nunca foi ativa**: tokens assinados com ela são aceitos (é a etapa 1 do procedimento).
- **Instâncias com configurações diferentes durante a troca**: se uma instância já emite com B e outra
  ainda não conhece B, os tokens de B são recusados nesta. O procedimento exige que a etapa 1 (acrescentar
  B como aceita) chegue a todas as instâncias antes da etapa 2.
- **Retirada antes do prazo**: os tokens da chave retirada passam a receber 401, e os clientes renovam a
  sessão. É o comportamento da retirada de emergência.
- **Cursor da listagem de pedidos com chave própria**: a rotação do JWT não muda a chave do cursor, e os
  cursores em uso continuam válidos.
- **Cursor sem chave própria**: a chave do cursor é derivada da chave ativa do JWT. Na troca da chave ativa
  os cursores em uso recebem 400 e o cliente recomeça da primeira página, como hoje. Migrar o mesmo valor
  da chave atual para o formato novo mantém os cursores válidos na implantação.
- **Implantação deste requisito**: os tokens em uso não têm identificador e passam a receber 401; cada
  cliente renova a sessão uma vez. É uma interrupção curta e esperada, registrada nas limitações.
- **Configuração antiga e nova juntas**: a presença de `Jwt:ChaveAssinatura` falha a partida mesmo com o
  formato novo também preenchido, para não deixar dúvida sobre qual chave vale.
- **Token com algoritmo diferente do esperado**: continua recusado, como hoje.
- **Mesmo material de chave com dois identificadores**: não é barrado pela validação, por não ser
  detectável sem comparar valores; o procedimento recomenda gerar uma chave nova a cada rotação.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: O sistema MUST aceitar na validação do token de acesso um conjunto de uma ou mais chaves de
  assinatura configuradas, cada uma com um identificador único.
- **FR-002**: O sistema MUST emitir os tokens de acesso com uma só chave, a chave ativa, indicada na
  configuração, e MUST pôr o identificador dela no cabeçalho do token.
- **FR-003**: O sistema MUST aceitar um token assinado com qualquer chave configurada, ativa ou não, desde
  que ele passe nas demais validações existentes (emissor, audiência, validade, algoritmo).
- **FR-004**: O sistema MUST recusar com 401, com o mesmo corpo de hoje para token inválido, um token
  assinado com uma chave que não está na configuração, com identificador desconhecido ou com assinatura
  que não confere com a chave do identificador.
- **FR-005**: O sistema MUST validar a configuração das chaves na partida e falhar a partida quando:
  não houver chave; alguma chave tiver menos de 32 bytes; um identificador estiver vazio, fora do formato
  permitido ou repetido; a chave ativa não estiver indicada ou não existir entre as chaves.
- **FR-006**: As mensagens de falha da partida e os logs MUST citar no máximo o identificador da chave,
  nunca o valor dela.
- **FR-007**: A configuração antiga, `Jwt:ChaveAssinatura`, MUST fazer a partida falhar, com uma mensagem
  que indique o formato novo. A configuração de desenvolvimento, a dos testes e a documentação MUST migrar
  para o formato novo no mesmo PR.
- **FR-008**: O cursor da listagem de pedidos MUST poder ter uma chave própria, configurada separadamente
  do JWT, com no mínimo 32 bytes, validada na partida sem mostrar o valor. Sem ela, a chave do cursor MUST
  ser derivada da chave ativa do JWT. O comportamento MUST ficar registrado no documento do fluxo 3.
- **FR-009**: A renovação de sessão (REQ-24) MUST continuar funcionando durante e depois de qualquer
  etapa da rotação, inclusive na retirada de emergência, e o token novo MUST sair com a chave ativa.
- **FR-010**: O procedimento de rotação MUST estar documentado em `docs/`, com as três etapas (acrescentar,
  ativar, retirar), o tempo mínimo de espera antes da retirada, a ordem entre instâncias, o procedimento de
  emergência, a migração da configuração antiga e como gerar uma chave nova.
- **FR-011**: O estudo MUST comparar a assinatura simétrica com RS256 e ES256 e avaliar a origem das chaves
  fora da configuração da aplicação (cofre de segredos), terminando com uma recomendação; a troca de
  algoritmo e a integração com um cofre não fazem parte deste requisito.
- **FR-012**: O documento do fluxo 1 (autenticação) MUST ser atualizado com as chaves, a recusa por chave
  retirada, a validação na partida e as limitações novas.
- **FR-013**: Os testes automatizados MUST cobrir: token da chave antiga aceito no período de transição;
  token de chave retirada recusado com 401; token sem identificador ou com identificador desconhecido
  recusado; emissão com a chave ativa e o identificador dela; cada falha de validação da partida;
  mensagens de falha sem o valor da chave; e o cursor da listagem válido depois da troca da chave ativa
  quando há chave própria do cursor.
- **FR-014**: Um token sem identificador de chave no cabeçalho MUST ser recusado com 401, sem período de
  transição.

### Key Entities

- **Chave de assinatura**: um segredo usado para assinar e validar tokens de acesso. Atributos:
  identificador (público, vai no cabeçalho do token), material secreto (no mínimo 32 bytes, nunca exibido)
  e se é a ativa. Uma chave fora da configuração é uma chave retirada.
- **Conjunto de chaves**: as chaves configuradas em uma instância. Exatamente uma é a ativa; as demais só
  validam.
- **Chave do cursor**: segredo próprio que assina o cursor da listagem de pedidos; opcional, com no mínimo
  32 bytes. Na falta dele, a chave do cursor é derivada da chave ativa do JWT.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uma rotação planejada feita conforme o procedimento termina com 0 respostas 401 para tokens
  que estavam válidos antes da troca.
- **SC-002**: 100% dos tokens assinados com uma chave retirada são recusados com 401, a partir do momento
  em que a instância sobe sem a chave.
- **SC-003**: Na retirada de emergência, um cliente com sessão ativa volta a usar a API com uma única
  renovação, sem digitar a senha.
- **SC-004**: 100% das configurações inválidas listadas no FR-005, FR-007 e FR-008 impedem a partida, e
  nenhuma mensagem de falha contém o valor de uma chave.
- **SC-005**: Uma pessoa que nunca rotacionou a chave consegue fazê-lo em uma instalação local seguindo
  só o documento do procedimento.
- **SC-006**: A rotação da chave do JWT com a chave do cursor configurada não causa nenhum 400 de cursor
  inválido na listagem de pedidos.

## Assumptions

- A configuração de chaves é lida na partida; a troca de configuração entre as etapas do procedimento
  é feita reiniciando ou reimplantando as instâncias, uma de cada vez. Recarregar a configuração sem
  reiniciar fica fora do escopo.
- O algoritmo continua o HMAC-SHA256 simétrico de hoje. A assinatura assimétrica é só avaliada no estudo
  (a issue pede "avaliar"); se recomendada, vira um requisito futuro.
- As chaves continuam vindo da configuração da aplicação (variáveis de ambiente ou segredos locais em
  desenvolvimento); a integração com um cofre de segredos é só avaliada no estudo.
- O tempo mínimo antes da retirada é a validade máxima do token de acesso (`Jwt:DuracaoTokenMinutos`,
  15 minutos por padrão) mais a tolerância de relógio da validação (30 segundos).
- O identificador da chave não é segredo: vai no cabeçalho de todo token e pode aparecer nos logs.
- O procedimento de rotação fica em `docs/`, e o README só ganha o link na linha `docs/` da seção
  Estrutura (decisão do usuário). O critério de aceite da issue que fala em "README" é atendido assim.
- O Worker não emite nem valida JWT, então não é afetado.
- Fora do escopo: provedor de identidade externo (IdP), publicação de JWKS e troca automática da chave
  por agendamento.
