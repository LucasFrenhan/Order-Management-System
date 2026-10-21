# Feature Specification: Autenticação e autorização de usuários

**Feature Branch**: `feature/req-03-autenticacao-identity`

**Requisito**: REQ-03 (GitHub issue #4 — "[Auth] Autenticação e Autorização com ASP.NET Core Identity + JWT")

**Created**: 2026-10-21

**Status**: Draft

**Input**: User description: "REQ-03 (GitHub issue #4): implementar a infraestrutura de segurança com ASP.NET Core Identity para emissão e validação de tokens JWT e configurar as roles de acesso. Critérios de aceite: Identity integrado ao Entity Framework Core; roles Cliente, Admin e OperadorLogistica criadas na inicialização; POST /api/v1/auth/register (registra usuário com role padrão Cliente); POST /api/v1/auth/login (autentica e gera token JWT assinado); filtros de autorização ativos nos endpoints."

## Contexto

O REQ-01 publicou o contrato v1 das operações de registro e login, com os formatos de
entrada e saída, as respostas de erro e as regras de acesso por perfil. Hoje as duas
operações respondem "não implementada" (501). As rotas protegidas já validam o token e o
perfil, mas não há quem emita tokens: os testes usam tokens gerados à mão.

Este requisito faz o registro e o login funcionarem de verdade: guarda os usuários e seus
perfis, emite o token de acesso e garante que um token emitido pelo login seja aceito,
ou recusado, pelas rotas protegidas conforme o perfil do usuário. O contrato v1 não muda.

A constituição fixa a tecnologia: ASP.NET Core Identity com JWT Bearer, e persistência em
SQL Server por meio do Entity Framework Core.

## Clarifications

### Session 2026-10-21

- Q: Deve haver bloqueio da conta após tentativas de login erradas? → A: Sim. 5 erros
  seguidos bloqueiam a conta por 15 minutos; durante o bloqueio o login responde o mesmo
  401 genérico, sem mudar o contrato v1.
- Q: Como passam a existir usuários Admin e OperadorLogistica? → A: A API cria usuários
  iniciais na inicialização a partir de configuração (e-mail e senha). Sem configuração,
  nenhum é criado; em Development os valores ficam no arquivo de desenvolvimento.
- Q: Quem cria o armazenamento de usuários e perfis, REQ-03 ou REQ-04? → A: O REQ-03 cria
  as tabelas de usuários e perfis e a primeira migration do banco. O REQ-04 acrescenta as
  entidades de negócio em migrations seguintes e relaciona `Usuario` ao que já existe.
- Q: Como o banco é criado e atualizado com as migrations? → A: A API aplica as migrations
  pendentes ao iniciar só em Development e no ambiente de testes. Nos demais ambientes elas
  são aplicadas por comando separado, e a API não inicia com o banco desatualizado.
- Q: O que a API faz se o banco estiver fora do ar ao iniciar? → A: Não inicia; falha logo
  com mensagem que nomeia o banco sem expor dados de conexão. Uma configuração desliga essa
  preparação, e os testes que não usam banco a desligam.
- Q: A API registra eventos de segurança em log, e como identifica a pessoa? → A: Sim:
  registro, login bem-sucedido, login recusado e bloqueio de conta, identificando a pessoa
  pelo identificador do usuário quando a conta existe e pelo e-mail mascarado; nunca senha,
  hash nem token.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Registrar-se como cliente (Priority: P1)

Uma pessoa informa nome, e-mail e senha e passa a ter uma conta com o perfil Cliente. Se o
e-mail já tiver conta, ela é avisada de forma clara, sem que outra conta seja criada.

**Why this priority**: Sem conta não há login, e sem login nenhuma operação de pedido pode
ser usada.

**Independent Test**: Chamar o registro com dados válidos e verificar a resposta 201 com o
perfil Cliente. Repetir com o mesmo e-mail e verificar o 409.

**Acceptance Scenarios**:

1. **Given** um e-mail sem conta, **When** a pessoa se registra com nome, e-mail e senha
   válidos, **Then** recebe 201 com identificador, nome, e-mail e o perfil Cliente, e a
   resposta não contém a senha.
2. **Given** um nome com espaços nas pontas, **When** a pessoa se registra, **Then** o nome
   é guardado e devolvido sem esses espaços.
3. **Given** um e-mail que já tem conta, inclusive escrito com outra combinação de
   maiúsculas e minúsculas, **When** alguém tenta se registrar com ele, **Then** recebe 409
   com o código `USUARIO_EMAIL_JA_CADASTRADO` e nenhuma conta nova é criada.
4. **Given** dados fora das regras do contrato (e-mail mal formado, senha fraca, nome
   vazio), **When** a pessoa se registra, **Then** recebe 400 e nenhuma conta é criada.
5. **Given** um registro concluído, **When** a API é reiniciada, **Then** a conta continua
   existindo e o login funciona.

---

### User Story 2 - Entrar e receber um token de acesso (Priority: P1)

Uma pessoa registrada informa e-mail e senha e recebe um token de acesso válido por 60
minutos, que identifica quem ela é e quais perfis tem. Com credenciais erradas, ela
recebe uma recusa que não revela se o e-mail tem conta.

**Why this priority**: É o token que libera todas as operações protegidas.

**Independent Test**: Registrar uma conta, fazer login e usar o token recebido em uma rota
protegida. Fazer login com senha errada e com e-mail inexistente e comparar as respostas.

**Acceptance Scenarios**:

1. **Given** uma conta existente, **When** a pessoa faz login com e-mail e senha corretos,
   **Then** recebe 200 com o token, o tipo `Bearer`, a data/hora de expiração (emissão + 60
   minutos, em UTC) e seus perfis.
2. **Given** o e-mail da conta escrito com outra combinação de maiúsculas e minúsculas,
   **When** a pessoa faz login com a senha correta, **Then** o login funciona.
3. **Given** uma senha errada, ou um e-mail sem conta, **When** a pessoa faz login,
   **Then** recebe 401 com a mesma mensagem genérica nos dois casos.
4. **Given** um token emitido pelo login, **When** ele é usado em uma rota protegida que o
   perfil da pessoa pode acessar, **Then** a rota aceita o token.
5. **Given** um token emitido há mais de 60 minutos, **When** ele é usado em uma rota
   protegida, **Then** a resposta é 401.
6. **Given** várias tentativas seguidas de login com senha errada para a mesma conta,
   **When** a quinta tentativa errada seguida acontece, **Then** a conta fica bloqueada por
   15 minutos e, nesse período, até o login com a senha correta recebe o mesmo 401
   genérico.
7. **Given** uma conta bloqueada, **When** os 15 minutos passam, **Then** o login com a
   senha correta volta a funcionar, e um login bem-sucedido zera a contagem de erros.

---

### User Story 3 - Perfis de acesso aplicados de ponta a ponta (Priority: P2)

Os três perfis (Cliente, Admin e OperadorLogistica) existem desde a primeira
inicialização. Um token emitido pelo login dá acesso exatamente às operações que o perfil
do usuário permite, segundo as regras do contrato v1.

**Why this priority**: As regras de acesso já existem desde o REQ-01; aqui elas passam a
valer com tokens reais. As operações de pedido só ganham comportamento no REQ-05, então o
efeito completo aparece depois.

**Independent Test**: Com a API recém-iniciada em um banco vazio, verificar que os três
perfis existem. Com tokens emitidos pelo login para cada perfil, verificar que cada rota
protegida responde 403 para os perfis não autorizados e deixa passar os autorizados.

**Acceptance Scenarios**:

1. **Given** um banco sem nenhum perfil, **When** a API inicia, **Then** os perfis Cliente,
   Admin e OperadorLogistica passam a existir.
2. **Given** os perfis já existentes, **When** a API é reiniciada, **Then** nenhum perfil é
   duplicado nem alterado.
3. **Given** um token de Cliente emitido pelo login, **When** ele é usado na atualização de
   status de pedido, **Then** a resposta é 403.
4. **Given** um token de Admin ou de OperadorLogistica emitido pelo login, **When** ele é
   usado na criação de pedido, **Then** a resposta é 403.
5. **Given** e-mail e senha de usuário inicial Admin (e, opcionalmente, OperadorLogistica)
   na configuração, **When** a API inicia, **Then** o usuário existe com aquele perfil e
   consegue fazer login.
6. **Given** um usuário inicial já criado, **When** a API é reiniciada, **Then** ele não é
   duplicado, e sua senha e seus perfis não são alterados.
7. **Given** nenhuma configuração de usuário inicial, **When** a API inicia, **Then** nenhum
   usuário Admin ou OperadorLogistica é criado, e a API inicia normalmente.

---

### Edge Cases

- Duas tentativas de registro simultâneas com o mesmo e-mail → uma recebe 201 e a outra
  409; nunca existem duas contas com o mesmo e-mail.
- E-mail com espaços nas pontas → o contrato já recusa com 400 (formato inválido).
- Senha com espaços → aceita como foi digitada; o login exige a senha idêntica.
- Banco de dados fora do ar quando a API inicia → a API não inicia (FR-018). Isso substitui,
  para a API com a preparação ligada, o comportamento do REQ-02 de iniciar sem as
  dependências; `/health` continua sem consultar o banco depois que a API está no ar.
- Banco de dados fora do ar durante registro ou login → resposta de erro genérica (500 em
  ProblemDetails), sem detalhes internos; a API não inicia um registro pela metade.
- Perfil do usuário alterado depois do login → o token já emitido mantém os perfis antigos
  até expirar; o novo perfil vale a partir do próximo login.
- Token assinado com outra chave, emitido por outro emissor ou para outra audiência → 401
  (comportamento do REQ-01, mantido).
- Login de conta que existe mas sem nenhum perfil (dado inconsistente) → o token não dá
  acesso a nenhuma rota protegida (403 em todas).

## Requirements *(mandatory)*

### Functional Requirements

**Registro**

- **FR-001**: O registro MUST criar a conta com o perfil Cliente e responder 201 com
  `UsuarioResponseDTO` (`id`, `nome`, `email`, `perfis`), conforme o contrato v1.
- **FR-002**: A senha MUST ser guardada apenas na forma de hash irreversível, com sal, e
  MUST nunca aparecer em respostas, logs ou mensagens de erro.
- **FR-003**: O e-mail MUST ser único sem diferenciar maiúsculas de minúsculas. Um e-mail
  já cadastrado MUST resultar em 409 com o código `USUARIO_EMAIL_JA_CADASTRADO`, inclusive
  quando dois registros com o mesmo e-mail chegam ao mesmo tempo.
- **FR-004**: O nome MUST ser guardado sem os espaços das pontas. O e-mail MUST ser
  devolvido como foi informado no registro.
- **FR-005**: As regras de senha aplicadas no registro MUST ser exatamente as do contrato
  v1 (8 a 128 caracteres, ao menos uma letra maiúscula, uma minúscula e um dígito). Nenhuma
  regra adicional, como exigir símbolo, pode recusar uma senha que o contrato aceita.

**Login e token**

- **FR-006**: O login com e-mail e senha corretos MUST responder 200 com
  `LoginResponseDTO`: `tokenAcesso`, `tipoToken` igual a `Bearer`, `expiraEm` igual à
  emissão + 60 minutos (UTC, ISO 8601) e `perfis`.
- **FR-007**: O token MUST ser assinado e MUST identificar o usuário pelo seu
  identificador e trazer todos os seus perfis, além do emissor e da audiência configurados.
  As rotas protegidas MUST aceitar esse token sem nenhuma configuração adicional.
- **FR-008**: E-mail inexistente e senha errada MUST resultar na mesma resposta 401, com o
  mesmo título e a mesma mensagem, para que não seja possível descobrir quais e-mails têm
  conta.
- **FR-009**: 5 tentativas de login erradas seguidas MUST bloquear a conta por 15
  minutos. Durante o bloqueio, o login MUST responder o mesmo 401 genérico do FR-008, até
  com a senha correta. Um login bem-sucedido MUST zerar a contagem de erros.
- **FR-010**: A duração do token MUST vir da configuração já existente (60 minutos por
  padrão), e a chave de assinatura MUST vir de configuração. Nenhum arquivo versionado
  MUST conter chave de assinatura que não seja de desenvolvimento.

**Perfis e autorização**

- **FR-011**: Os perfis Cliente, Admin e OperadorLogistica MUST existir após a
  inicialização da API. A criação MUST ser idempotente: reinícios ou várias instâncias
  iniciando juntas não duplicam nem alteram perfis.
- **FR-012**: A API MUST criar na inicialização os usuários iniciais definidos em
  configuração (e-mail, senha e perfil Admin ou OperadorLogistica). A criação MUST ser
  idempotente: usuário já existente não é duplicado nem tem senha ou perfis alterados. Sem
  configuração, nenhum usuário inicial é criado. Credenciais de usuário inicial só podem
  estar versionadas no arquivo de desenvolvimento, marcadas como tal; senha que não atende
  ao FR-005 MUST impedir a API de iniciar, com mensagem que não expõe o valor.
- **FR-013**: As regras de acesso por perfil do contrato v1 MUST continuar valendo e MUST
  ser verificadas com tokens emitidos pelo próprio login, cobrindo sucesso, 401 e 403 em
  cada rota protegida.

**Armazenamento**

- **FR-014**: Contas e perfis MUST ser guardados no banco de dados da aplicação e
  sobreviver a reinícios da API. Este requisito MUST criar esse armazenamento, com a
  primeira migration do banco da aplicação; o REQ-04 acrescenta as entidades de negócio em
  migrations seguintes.
- **FR-017**: Em Development e no ambiente de testes, a API MUST criar o banco, se não
  existir, e aplicar as migrations pendentes ao iniciar, antes de criar perfis e usuários
  iniciais. Nos demais ambientes, a API MUST NOT alterar o esquema: as migrations são
  aplicadas por um comando documentado, e a API MUST recusar-se a iniciar quando houver
  migration pendente, com mensagem que diz isso sem expor dados de conexão.
- **FR-018**: Se o banco estiver inacessível durante a preparação da inicialização
  (migrations, perfis e usuários iniciais), a API MUST falhar ao iniciar, com mensagem que
  nomeia o banco e não expõe dados de conexão. Uma configuração MUST permitir desligar essa
  preparação; desligada, a API inicia sem consultar o banco, como no REQ-02.

**Eventos de segurança**

- **FR-019**: A API MUST registrar em log os eventos de registro, login bem-sucedido,
  login recusado e bloqueio de conta. Cada evento MUST identificar a pessoa pelo
  identificador do usuário, quando a conta existe, e pelo e-mail mascarado (primeira letra
  do nome local seguida de `***` e o domínio, por exemplo `m***@exemplo.com.br`). Os logs
  MUST NOT conter senha, hash de senha, token nem o e-mail completo.

**Contrato e transição**

- **FR-015**: O contrato v1 MUST permanecer inalterado: mesmas rotas, formatos e respostas
  documentadas. O teste de conformidade do REQ-01 MUST continuar passando.
- **FR-016**: Registro e login MUST deixar de responder 501. As operações de pedido
  continuam respondendo 501 até o REQ-05.

### Key Entities *(include if feature involves data)*

- **Usuário**: pessoa com conta no sistema. Tem identificador, nome, e-mail único (sem
  diferenciar maiúsculas de minúsculas), senha guardada só como hash e um ou mais perfis.
- **Perfil**: papel de acesso (Cliente, Admin ou OperadorLogistica) que define quais
  operações o usuário pode executar.
- **Token de acesso**: credencial temporária emitida no login, assinada, válida por 60
  minutos, que carrega o identificador do usuário e seus perfis. Não é guardado no
  servidor.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Uma pessoa sem conta consegue se registrar e obter um token que funciona em
  uma rota protegida com apenas duas chamadas (registro e login).
- **SC-002**: 100% das rotas protegidas são verificadas com tokens emitidos pelo login para
  cada perfil, e respondem conforme a tabela de acesso do contrato v1 (sucesso, 401 ou 403).
- **SC-003**: Zero ocorrências de senha, hash de senha ou token em respostas de erro e em
  logs, e zero e-mails completos em logs.
- **SC-004**: As respostas de login para e-mail inexistente e para senha errada são
  idênticas em status, título e mensagem.
- **SC-005**: 95% dos logins com credenciais corretas são concluídos em menos de 1 segundo
  no ambiente local.
- **SC-006**: A suíte de testes continua 100% verde, e o teste de conformidade do contrato
  v1 passa sem alterações no contrato.

## Assumptions

- Renovação de token, logout, recuperação de senha e confirmação de e-mail ficam fora do
  escopo, como já definido no REQ-01. O login não exige e-mail confirmado.
- O registro público sempre atribui o perfil Cliente (REQ-01). Este requisito não cria
  operação de gestão de usuários nem de perfis.
- Limitação de taxa de requisições (rate limiting) nas rotas públicas fica fora do escopo.
- A chave de assinatura é simétrica e única; rotação de chave fica fora do escopo.
- O ambiente local do REQ-02 (banco de dados no ar) é pré-requisito para registro e login.
  A suíte de testes continua rodando sem Docker; os testes que precisam de banco real são
  ignorados quando o Docker não está disponível, como no REQ-02.
