# Feature Specification: Contratos da API v1 (OpenAPI e DTOs)

**Feature Branch**: `feature/req-01-contratos-api`

**Requisito**: REQ-01 (GitHub issue #2 — "[SDD] Especificação OpenAPI/Swagger e DTOs de Contrato")

**Created**: 2026-10-21

**Status**: Draft

**Input**: User description: "REQ-01 (GitHub issue #2): Especificação dos contratos da API do Order Management System (OpenAPI) e DTOs, antes de qualquer regra de negócio (SDD). Rotas base: POST /api/v1/auth/register; POST /api/v1/auth/login; POST /api/v1/pedidos (header obrigatório X-Idempotency-Key); GET /api/v1/pedidos/{id}; PATCH /api/v1/pedidos/{id}/status. DTOs/schemas: CriarPedidoRequestDTO, PedidoResponseDTO, ItemPedidoDTO, LoginResponseDTO (mais os DTOs de registro/login e de atualização de status necessários). Schemas de erro padrão RFC 7807 (ProblemDetails), com 400 para validação. Status do pedido: Pendente, Processando, ProntoParaEnvio, Despachado, Cancelado, PagamentoRejeitado. Roles: Cliente, Admin, OperadorLogistica. Segurança: todas as rotas exceto register/login exigem JWT Bearer e autorização por policies; contratos devem documentar 401/403. Contratos versionados (v1) e imutáveis conforme constituição. Nomes em português. Branch dedicada no padrão feature/req-01-contratos-api."

## Contexto

Seguindo o Princípio I da constituição (Specification-First), este requisito define o
**contrato público** da API v1 antes de qualquer implementação de regra de negócio. O
entregável é um documento de contrato publicado e navegável, que descreve operações,
formatos de entrada e saída, exigências de acesso e formato de erros. Os requisitos
seguintes (REQ-02 a REQ-06) implementam esse contrato e não podem divergir dele sem
aprovação (Princípio II).

## Clarifications

### Session 2026-10-21

- Q: Quem pode alterar o status do pedido? → A: Admin e OperadorLogistica fazem as
  transições logísticas e o cancelamento (a partir de Pendente, Processando ou
  ProntoParaEnvio); o Cliente pode cancelar o próprio pedido apenas enquanto Pendente;
  PagamentoRejeitado só é definido pelo fluxo de pagamento.
- Q: Toda resposta 400 deve trazer o campo `erros`, inclusive para JSON mal formado, `id`
  que não é UUID ou status desconhecido? → A: Sim, todo 400 traz `erros`. A chave é o
  campo ou parâmetro com problema (`id`, `status`, `X-Idempotency-Key`), ou `""` quando o
  corpo inteiro é ilegível.
- Q: O que a API deve responder a uma requisição sem token para um endereço que não
  existe (rota inexistente ou documentação em produção)? → A: 401 sem token; 404 com
  token válido. A exigência de autenticação vale por padrão para qualquer endereço.
- Q: Por quanto tempo o token de acesso emitido no login deve continuar válido? → A: 60
  minutos a partir da emissão.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Consultar o contrato da API v1 (Priority: P1)

Um desenvolvedor de um sistema consumidor (loja, app ou integração parceira) acessa o
documento de contrato da API v1 e encontra todas as operações disponíveis, com seus
dados de entrada, respostas possíveis, exigências de autenticação e exemplos, sem
precisar consultar o código nem a equipe.

**Why this priority**: É o entregável central do REQ-01. Sem o contrato publicado, os
demais requisitos não têm referência para implementar nem testar conformidade.

**Independent Test**: Abrir o documento de contrato publicado e verificar que as 5
operações estão descritas com entradas, respostas de sucesso, respostas de erro e
exemplos.

**Acceptance Scenarios**:

1. **Given** a API em execução no ambiente de desenvolvimento, **When** o desenvolvedor
   acessa o endereço do documento de contrato, **Then** ele vê as 5 operações da v1
   agrupadas por domínio (Autenticação e Pedidos).
2. **Given** o documento de contrato, **When** o desenvolvedor inspeciona qualquer
   operação, **Then** encontra os campos de entrada com tipo, obrigatoriedade e regras
   de validação, além de todas as respostas possíveis com seus formatos.
3. **Given** o documento de contrato, **When** o desenvolvedor inspeciona uma operação
   protegida, **Then** encontra a exigência de token de acesso, os perfis autorizados e
   as respostas 401 e 403 documentadas.

---

### User Story 2 - Contratos de autenticação (Priority: P1)

Um novo usuário se registra informando nome, e-mail e senha e recebe o perfil Cliente.
Um usuário registrado faz login com e-mail e senha e recebe um token de acesso com
prazo de validade, que deve usar nas demais operações.

**Why this priority**: Todas as operações de pedidos dependem do token emitido no
login; sem esse contrato nenhuma operação protegida pode ser exercitada.

**Independent Test**: Validar no contrato os formatos de registro e login, incluindo
respostas de sucesso, de dados inválidos, de e-mail já cadastrado e de credenciais
inválidas.

**Acceptance Scenarios**:

1. **Given** dados de registro válidos, **When** a operação de registro é chamada,
   **Then** o contrato prevê resposta 201 com identificador, nome, e-mail e perfis do
   usuário criado (perfil Cliente), sem expor a senha.
2. **Given** dados de registro inválidos (e-mail mal formado, senha fora da política,
   nome vazio), **When** a operação de registro é chamada, **Then** o contrato prevê
   resposta 400 no formato padrão de erro, com a lista de campos inválidos.
3. **Given** um e-mail já cadastrado, **When** a operação de registro é chamada,
   **Then** o contrato prevê resposta 409 no formato padrão de erro.
4. **Given** credenciais corretas, **When** a operação de login é chamada, **Then** o
   contrato prevê resposta 200 com token de acesso, tipo do token, data/hora de
   expiração e perfis do usuário (`LoginResponseDTO`).
5. **Given** credenciais incorretas, **When** a operação de login é chamada, **Then** o
   contrato prevê resposta 401 no formato padrão de erro, com mensagem genérica que não
   revela se o e-mail existe.

---

### User Story 3 - Contrato de criação de pedido com idempotência (Priority: P1)

Um Cliente autenticado cria um pedido informando os produtos e quantidades e uma chave
de idempotência. Se a mesma requisição for reenviada com a mesma chave (por exemplo,
após uma falha de rede), ele recebe o mesmo pedido, sem duplicação.

**Why this priority**: É a operação central de negócio do OMS e a que tem mais regras
de contrato (idempotência, validação, autorização).

**Independent Test**: Validar no contrato o formato de `CriarPedidoRequestDTO`, a
obrigatoriedade do cabeçalho `X-Idempotency-Key` e todas as respostas documentadas.

**Acceptance Scenarios**:

1. **Given** um Cliente autenticado e um pedido válido com chave de idempotência nova,
   **When** a operação de criação é chamada, **Then** o contrato prevê resposta 201 com
   o `PedidoResponseDTO` (status Pendente) e a localização do recurso criado.
2. **Given** uma chave de idempotência já usada pelo mesmo usuário com o mesmo conteúdo,
   **When** a operação é chamada novamente, **Then** o contrato prevê a mesma resposta
   do pedido originalmente criado, sem criar um novo pedido.
3. **Given** uma chave de idempotência já usada pelo mesmo usuário com conteúdo
   diferente, **When** a operação é chamada, **Then** o contrato prevê resposta 409 no
   formato padrão de erro.
4. **Given** a ausência do cabeçalho de idempotência ou um valor fora do formato,
   **When** a operação é chamada, **Then** o contrato prevê resposta 400.
5. **Given** um pedido sem itens, com quantidade menor ou igual a zero ou com produto
   inexistente, **When** a operação é chamada, **Then** o contrato prevê resposta 400
   (formato) ou 422 (produto inexistente ou indisponível), no formato padrão de erro.
6. **Given** uma requisição sem token ou com token inválido/expirado, **When** a
   operação é chamada, **Then** o contrato prevê resposta 401.
7. **Given** um usuário autenticado sem o perfil Cliente, **When** a operação é chamada,
   **Then** o contrato prevê resposta 403.

---

### User Story 4 - Contrato de consulta de pedido (Priority: P2)

Um Cliente consulta um pedido seu pelo identificador. Admin e OperadorLogistica podem
consultar qualquer pedido.

**Why this priority**: Necessário para acompanhar o pedido, mas não bloqueia a criação.

**Independent Test**: Validar no contrato as respostas 200, 401 e 404 da consulta e a
regra de visibilidade por perfil.

**Acceptance Scenarios**:

1. **Given** um Cliente dono do pedido, **When** consulta o pedido pelo identificador,
   **Then** o contrato prevê resposta 200 com o `PedidoResponseDTO`.
2. **Given** um Cliente que não é dono do pedido, **When** consulta o pedido, **Then** o
   contrato prevê resposta 404, sem revelar que o pedido existe.
3. **Given** um Admin ou OperadorLogistica, **When** consulta qualquer pedido existente,
   **Then** o contrato prevê resposta 200.
4. **Given** um identificador inexistente ou em formato inválido, **When** a consulta é
   feita, **Then** o contrato prevê resposta 404 ou 400, respectivamente.

---

### User Story 5 - Contrato de atualização de status do pedido (Priority: P2)

Admin e OperadorLogistica avançam o pedido no ciclo logístico ou o cancelam. O Cliente
pode cancelar o próprio pedido enquanto ele estiver Pendente. Toda alteração respeita as
transições permitidas do ciclo de vida (FR-020).

**Why this priority**: Suporta a operação logística, mas depende de o pedido existir.

**Independent Test**: Validar no contrato o formato de `AtualizarStatusPedidoRequestDTO`,
os perfis autorizados, a tabela de transições e as respostas 200, 400, 401, 403, 404 e
409.

**Acceptance Scenarios**:

1. **Given** um usuário autorizado e uma transição permitida, **When** a operação de
   atualização de status é chamada, **Then** o contrato prevê resposta 200 com o
   `PedidoResponseDTO` atualizado.
2. **Given** uma transição não permitida (por exemplo, de Despachado para Pendente),
   **When** a operação é chamada, **Then** o contrato prevê resposta 409 no formato
   padrão de erro, informando o status atual e o solicitado.
3. **Given** um status fora da lista de valores válidos, **When** a operação é chamada,
   **Then** o contrato prevê resposta 400.
4. **Given** um usuário sem perfil autorizado para a transição, **When** a operação é
   chamada, **Then** o contrato prevê resposta 403.
5. **Given** um Cliente dono de um pedido Pendente, **When** solicita a transição para
   Cancelado, **Then** o contrato prevê resposta 200 com o pedido Cancelado.
6. **Given** um Cliente dono de um pedido em Processando, **When** solicita Cancelado,
   **Then** o contrato prevê resposta 403.

---

### Edge Cases

- Cabeçalho `X-Idempotency-Key` enviado vazio, com espaços ou acima do tamanho máximo →
  400.
- Duas requisições simultâneas com a mesma chave de idempotência → apenas um pedido é
  criado; a outra recebe o mesmo pedido ou 409 se ainda estiver em processamento.
- Pedido com o mesmo produto repetido em dois itens → 400 (cada produto aparece uma vez).
- Quantidade acima do limite por item → 400.
- Corpo da requisição ausente, JSON mal formado ou com tipo errado em campo → 400 no
  formato padrão de erro, com `erros` (FR-010), nunca uma resposta de erro genérica ou
  com detalhes internos.
- Campos desconhecidos no corpo da requisição → ignorados, sem erro (permite evolução
  compatível dos consumidores).
- Erro inesperado no servidor → 500 no formato padrão de erro, sem rastreamento de pilha,
  nomes internos ou dados sensíveis, e com um identificador de rastreamento para suporte.
- Token com assinatura inválida, expirado ou emitido para outra audiência → 401.
- Endereço inexistente (incluindo a documentação em produção) → 401 sem token; 404 com
  token válido (FR-007a).
- Atualização de status para o mesmo status atual → 409 (transição inexistente).

## Requirements *(mandatory)*

### Functional Requirements

**Documento de contrato**

- **FR-001**: O sistema MUST publicar um documento de contrato legível por máquina e
  navegável por pessoas, descrevendo todas as operações da versão 1 da API.
- **FR-002**: Todas as operações MUST estar sob o prefixo de versão `/api/v1`, e o
  documento MUST identificar a versão do contrato.
- **FR-003**: O documento MUST conter exatamente estas operações na v1:
  - `POST /api/v1/auth/register` — registrar usuário (público)
  - `POST /api/v1/auth/login` — autenticar e obter token (público)
  - `POST /api/v1/pedidos` — criar pedido (perfil Cliente; cabeçalho `X-Idempotency-Key`
    obrigatório)
  - `GET /api/v1/pedidos/{id}` — consultar pedido (Cliente dono, Admin, OperadorLogistica)
  - `PATCH /api/v1/pedidos/{id}/status` — atualizar status (perfis conforme FR-020)
- **FR-004**: Cada operação MUST documentar todos os códigos de resposta possíveis, com o
  formato do corpo de cada um, e ao menos um exemplo de requisição e um de resposta de
  sucesso.
- **FR-005**: Nomes de operações, schemas, campos e mensagens de erro MUST estar em
  português, com campos em camelCase no JSON.

**Segurança no contrato**

- **FR-006**: Toda operação, exceto registro e login, MUST declarar a exigência de token
  de acesso do tipo Bearer e os perfis autorizados.
- **FR-007**: Toda operação protegida MUST documentar as respostas 401 (sem token, token
  inválido ou expirado) e 403 (autenticado sem permissão).
- **FR-007a**: A exigência de autenticação MUST valer por padrão para qualquer endereço,
  inclusive inexistente. Só register, login e a documentação fora de produção são
  públicos. Um endereço inexistente responde 401 sem token e 404 com token válido.
- **FR-008**: Nenhum schema de resposta MUST expor senha, hash de senha ou dados internos
  de segurança.

**Formato de erro**

- **FR-009**: Todas as respostas de erro (4xx e 5xx) MUST usar o formato padrão de
  detalhes de problema da RFC 7807, com os campos `type`, `title`, `status`, `detail`,
  `instance` e um identificador de rastreamento (`traceId`).
- **FR-010**: **Toda** resposta 400 MUST incluir o campo `erros`, que mapeia cada campo
  ou parâmetro inválido para a lista de mensagens em português. Isso vale para regras de
  validação e também para falhas de formato: JSON mal formado, tipo errado, enum
  desconhecido, campo obrigatório ausente, `id` de rota ou cabeçalho que não é UUID. A
  chave é o caminho do campo (`itens[0].quantidade`, `status`), o nome do parâmetro (`id`,
  `X-Idempotency-Key`) ou `""` quando o corpo inteiro é ausente ou ilegível.
- **FR-011**: Erros de regra de negócio (409 e 422) MUST incluir um código de erro
  estável (`codigo`, por exemplo `PEDIDO_TRANSICAO_INVALIDA`) que consumidores possam
  tratar sem depender do texto da mensagem.

**Autenticação**

- **FR-012**: O schema `RegistrarUsuarioRequestDTO` MUST conter `nome` (obrigatório, 2 a
  100 caracteres), `email` (obrigatório, formato válido, até 256 caracteres) e `senha`
  (obrigatória, de 8 a 128 caracteres, com ao menos uma letra maiúscula, uma minúscula
  e um dígito).
- **FR-013**: A resposta de registro (201) MUST usar `UsuarioResponseDTO` com `id`, `nome`,
  `email` e `perfis`; e-mail duplicado MUST resultar em 409.
- **FR-014**: O schema `LoginRequestDTO` MUST conter `email` e `senha`, ambos
  obrigatórios.
- **FR-015**: `LoginResponseDTO` MUST conter `tokenAcesso`, `tipoToken` (valor `Bearer`),
  `expiraEm` (data/hora UTC em ISO 8601) e `perfis`. O token MUST valer por 60 minutos a
  partir da emissão (`expiraEm` = emissão + 60 min); depois disso, toda operação protegida
  responde 401.

**Pedidos**

- **FR-016**: `CriarPedidoRequestDTO` MUST conter `itens`: lista de 1 a 50 `ItemPedidoDTO`.
  O cliente não informa preço, total, status nem identificador do usuário; esses dados
  são definidos pelo sistema.
- **FR-017**: `ItemPedidoDTO` de entrada MUST conter `produtoId` (identificador do
  produto) e `quantidade` (inteiro de 1 a 999). Cada produto MUST aparecer no máximo uma
  vez por pedido.
- **FR-018**: O cabeçalho `X-Idempotency-Key` MUST ser obrigatório na criação, aceitar um
  identificador único universal (UUID) e ter o comportamento descrito na User Story 3:
  mesma chave e mesmo conteúdo devolvem o pedido original; mesma chave e conteúdo
  diferente devolvem 409. A chave é considerada por usuário.
- **FR-019**: `PedidoResponseDTO` MUST conter `id`, `numero` (código legível do pedido),
  `clienteId`, `status`, `itens` (cada um com `produtoId`, `nomeProduto`, `quantidade`,
  `precoUnitario` e `subtotal`), `valorTotal`, `moeda` (`BRL`), `criadoEm` e
  `atualizadoEm` (UTC, ISO 8601).
- **FR-020**: `AtualizarStatusPedidoRequestDTO` MUST conter `status` (obrigatório) e
  `motivo` (opcional, até 500 caracteres; obrigatório quando o novo status é Cancelado).
  Perfis autorizados e transições permitidas:

  | De | Para | Perfis autorizados |
  |----|------|--------------------|
  | Pendente | Processando | Admin, OperadorLogistica |
  | Processando | ProntoParaEnvio | Admin, OperadorLogistica |
  | ProntoParaEnvio | Despachado | Admin, OperadorLogistica |
  | Pendente, Processando, ProntoParaEnvio | Cancelado | Admin, OperadorLogistica |
  | Pendente | Cancelado | Cliente (somente o dono do pedido) |

  Qualquer outra transição MUST resultar em 409. `PagamentoRejeitado` só é definido pelo
  fluxo de pagamento e nunca por esta operação; Despachado, Cancelado e
  PagamentoRejeitado são estados finais. Um Cliente que tenta alterar pedido de outro
  recebe 404 (mesma regra da consulta); um Cliente que tenta uma transição diferente de
  Pendente→Cancelado no próprio pedido recebe 403.
- **FR-021**: O status do pedido MUST ser representado no contrato como texto, com os
  valores exatos: `Pendente`, `Processando`, `ProntoParaEnvio`, `Despachado`, `Cancelado`,
  `PagamentoRejeitado`.
- **FR-022**: Valores monetários MUST ser números decimais com 2 casas decimais.

**Evolução do contrato**

- **FR-023**: Depois de aprovado, o contrato v1 MUST permanecer compatível: só são
  permitidas adições opcionais. Remoções, renomeações e mudanças de tipo ou
  obrigatoriedade exigem uma nova versão (`/api/v2`).
- **FR-024**: O contrato MUST ser validado por testes automatizados de conformidade que
  falhem se uma operação, schema ou código de resposta documentado divergir da
  implementação.

### Key Entities *(include if feature involves data)*

- **Usuário**: pessoa que acessa o sistema. Tem nome, e-mail único e um ou mais perfis
  (Cliente, Admin, OperadorLogistica). A senha nunca aparece no contrato de saída.
- **Token de acesso**: credencial temporária emitida no login, com data de expiração e
  os perfis do usuário.
- **Pedido**: solicitação de compra de um Cliente. Tem identificador, número legível,
  dono, status, itens, valor total, moeda e datas de criação e atualização.
- **Item do pedido**: produto e quantidade dentro de um pedido, com o preço unitário
  definido pelo sistema no momento da criação e o subtotal.
- **Status do pedido**: estado do ciclo de vida (Pendente, Processando, ProntoParaEnvio,
  Despachado, Cancelado, PagamentoRejeitado) com transições controladas.
- **Chave de idempotência**: identificador enviado pelo Cliente que associa uma
  tentativa de criação a um único pedido.
- **Detalhe de problema**: formato padrão de erro devolvido por todas as operações.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% das 5 operações da v1 estão documentadas com entradas, todas as
  respostas possíveis e exemplos.
- **SC-002**: 100% das operações protegidas documentam exigência de token, perfis
  autorizados e respostas 401 e 403.
- **SC-003**: 100% das respostas de erro documentadas usam o formato padrão de detalhes
  de problema.
- **SC-004**: Um desenvolvedor sem acesso ao código consegue montar requisições válidas
  para as 5 operações em menos de 30 minutos usando apenas o documento de contrato.
- **SC-005**: Os testes de conformidade comparam o contrato publicado com o
  comportamento da API e detectam 100% das divergências de operação, schema ou código de
  resposta.
- **SC-006**: Zero mudanças incompatíveis no contrato v1 após a aprovação, verificado em
  cada revisão.

## Assumptions

- O escopo do REQ-01 é o contrato: documento publicado, DTOs, schemas de erro e
  definição de segurança. As regras de negócio por trás de cada operação são
  implementadas nos REQ-03 (autenticação), REQ-05 (criação de pedido) e em requisitos
  futuros (consulta e atualização de status, que não têm issue própria ainda).
- Até a implementação, as operações podem existir apenas como contrato publicado,
  sem comportamento de negócio.
- O login usa e-mail e senha. Renovação de token (refresh token), logout, recuperação de
  senha e confirmação de e-mail ficam fora do escopo da v1.
- O registro público sempre atribui o perfil Cliente. Perfis Admin e OperadorLogistica
  são atribuídos por mecanismo administrativo fora deste contrato (por exemplo, carga
  inicial).
- O preço de cada item vem do cadastro de produtos no momento da criação; o cliente
  nunca envia preço. Cadastro e listagem de produtos ficam fora do escopo da v1.
- Listagem e paginação de pedidos ficam fora do escopo da v1.
- Para não revelar a existência de pedidos de terceiros, um Cliente que consulta pedido
  de outro recebe 404, e não 403.
- Moeda única: real brasileiro (BRL). Datas sempre em UTC.
- A chave de idempotência fica válida por no mínimo 24 horas; o mecanismo de
  armazenamento é decisão do plano (recomendação registrada: banco de dados).
- O documento de contrato fica disponível ao menos no ambiente de desenvolvimento; a
  exposição em produção é decisão do plano.
- Dependência: os perfis e a emissão de token são implementados no REQ-03; as
  entidades persistidas, no REQ-04.
