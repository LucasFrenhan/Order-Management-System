# Feature Specification: Domínio de pedidos e persistência

**Feature Branch**: `feature/req-04-dominio-persistencia`

**Requisito**: REQ-04 (GitHub issue #5 — "[Database] Mapeamento do Domínio e Migrations do Banco SQL")

**Created**: 2026-10-22

**Status**: Draft

**Input**: User description: "REQ-04 (GitHub issue #5 — \"[Database] Mapeamento do Domínio e Migrations do Banco SQL\"): mapear as entidades do domínio utilizando EF Core Fluent API, aplicando índices e controles de concorrência necessários para o negócio. Critérios de aceite: entidades mapeadas no DbContext: Usuario, Produto, Pedido, ItemPedido, Pagamento, OrdemEntrega; enum de status do pedido: Pendente, Processando, ProntoParaEnvio, Despachado, Cancelado, PagamentoRejeitado; controle de concorrência otimista na entidade Produto (RowVersion / ConcurrencyCheck); migration criada e executada via EF Core Migrations. Contexto: o REQ-03 já criou o ContextoBancoDados (IdentityDbContext<Usuario, Perfil, Guid>) e a primeira migration InicialIdentidade com Usuario; o REQ-04 acrescenta os DbSet de negócio e as migrations seguintes. StatusPedido e PerfilUsuario já existem no Domain (REQ-01). Branch dedicada no padrão feature/req-04-dominio-persistencia."

## Contexto

O REQ-01 publicou o contrato v1. Ele define o formato de um pedido: número, dono, status,
itens com nome e preço do produto no momento da compra, valor total e moeda. O REQ-03 criou
o banco da aplicação com usuários e perfis, e a API já aplica migrations ao iniciar em
Development. Ainda não existe onde guardar produtos, pedidos, pagamentos nem entregas.

Este requisito cria esse armazenamento e as regras de integridade que o protegem: o que é
obrigatório, o que é único, o que não pode ficar negativo e o que acontece quando duas
operações alteram o mesmo dado ao mesmo tempo. Ele não cria operações novas na API: quem usa
esse armazenamento são os requisitos seguintes:

- REQ-05: criação e consulta de pedidos, com idempotência;
- REQ-06: mensageria;
- requisitos de estoque, pagamento e entrega, executados por workers.

O contrato v1 não muda.

A constituição fixa a tecnologia: SQL Server por meio do Entity Framework Core.

## Clarifications

### Session 2026-10-22

- Q: Como os produtos passam a existir no banco, se o contrato v1 não tem operação de
  cadastro de produtos? → A: A API cria um catálogo inicial fixo (cerca de 10 produtos) ao
  iniciar, só em Development e no ambiente de testes, de forma idempotente, como os
  usuários iniciais do REQ-03. Nos demais ambientes, nenhum produto é criado.
- Q: O pedido também deve ter controle de concorrência otimista, além do produto? → A: Sim.
  O pedido ganha uma versão, como o produto, já nesta migration. Os itens, o pagamento e a
  ordem de entrega não ganham.
- Q: O histórico de mudanças de status do pedido deve ser guardado já neste requisito? →
  A: Sim. Uma nova entidade, Histórico de status do pedido, é criada nesta migration, sem
  operação na API. Os serviços do REQ-05 e dos workers gravam nela desde a primeira
  mudança.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são as equipes e os serviços que implementam os requisitos
seguintes. O valor entregue é um armazenamento em que eles confiam: os dados sobrevivem a
reinícios, e o próprio banco recusa dados inválidos ou conflitantes.

### User Story 1 - Guardar um pedido completo com seus itens (Priority: P1)

O serviço de pedidos (REQ-05) precisa guardar um pedido de um cliente com seus itens e
ler esse pedido depois, com exatamente as informações que o contrato v1 devolve. Cada item
guarda o nome e o preço do produto no momento da compra, para que mudanças futuras no
catálogo não alterem pedidos antigos.

**Why this priority**: O pedido é o centro do sistema. Sem ele, nenhum requisito seguinte
pode começar.

**Independent Test**: Gravar um pedido com dois itens para um cliente registrado,
reiniciar a aplicação, ler o pedido e conferir todos os campos do `PedidoResponseDTO`.
Alterar o preço de um produto e verificar que o item do pedido mantém o preço original.

**Acceptance Scenarios**:

1. **Given** um cliente registrado e dois produtos cadastrados, **When** um pedido com
   dois itens é gravado, **Then** ele pode ser lido depois com todos os campos do contrato
   v1:
   - identificador, número, dono e status;
   - em cada item: produto, nome do produto, quantidade, preço unitário e subtotal;
   - valor total, moeda, data de criação e data de atualização.
2. **Given** um pedido gravado, **When** o nome ou o preço do produto muda no catálogo,
   **Then** o item do pedido continua com o nome e o preço do momento da compra.
3. **Given** um pedido gravado, **When** a aplicação é reiniciada, **Then** o pedido e seus
   itens continuam existindo, iguais.
4. **Given** um pedido que referencia um usuário ou um produto inexistente, **When** ele é
   gravado, **Then** a gravação é recusada e nada é salvo.
5. **Given** um pedido já gravado com um número, **When** outro pedido é gravado com o
   mesmo número, **Then** a gravação é recusada.
6. **Given** um pedido com dois itens do mesmo produto, **When** ele é gravado, **Then** a
   gravação é recusada (o contrato v1 exige produtos únicos na lista).
7. **Given** os pedidos de vários clientes, **When** os pedidos de um cliente são
   consultados, **Then** só os dele aparecem, e a consulta não precisa percorrer os pedidos
   de todos os clientes.
8. **Given** um pedido gravado com a entrada de criação no histórico, **When** o status
   muda duas vezes (Pendente → Processando → Cancelado, com motivo), **Then** o histórico
   do pedido tem três entradas, na ordem, e a última guarda o motivo.
9. **Given** duas operações que leram o mesmo pedido, **When** as duas gravam uma
   mudança de status, **Then** a primeira é aceita e a segunda recebe conflito de
   concorrência. Nem o status nem a entrada de histórico da segunda são gravados.

---

### User Story 2 - Proteger o estoque contra alterações simultâneas (Priority: P1)

Dois pedidos podem reservar o último item de um produto ao mesmo tempo. O armazenamento
precisa detectar que o produto mudou desde que foi lido e recusar a segunda gravação, sem
perder nenhuma das alterações. Assim, o serviço de estoque pode ler de novo e decidir. O
estoque nunca fica negativo.

**Why this priority**: A issue #5 exige isso de forma explícita. O worker de estoque
depende disso para não vender o que não existe.

**Independent Test**: Ler o mesmo produto em duas operações independentes, alterar o
estoque nas duas e gravar as duas. A primeira grava, e a segunda recebe um conflito de
concorrência que identifica o produto. Tentar gravar estoque negativo e verificar a recusa.

**Acceptance Scenarios**:

1. **Given** um produto lido por duas operações, **When** a primeira grava uma alteração
   de estoque, **Then** a gravação é aceita e a versão do produto muda.
2. **Given** a mesma situação, **When** a segunda operação grava com base na leitura
   antiga, **Then** a gravação é recusada com um conflito de concorrência identificável, e
   o estoque guardado continua sendo o da primeira operação.
3. **Given** um conflito recebido, **When** a operação lê o produto de novo e grava,
   **Then** a gravação é aceita.
4. **Given** qualquer produto, **When** se tenta gravar estoque negativo ou preço
   negativo, **Then** a gravação é recusada.
5. **Given** um produto que aparece em algum pedido, **When** se tenta apagá-lo, **Then** a
   remoção é recusada; para tirar um produto de venda, ele é marcado como inativo.

---

### User Story 3 - Registrar pagamento e ordem de entrega de um pedido (Priority: P2)

O worker de pagamento registra o resultado do pagamento de um pedido. O worker de entrega
gera a ordem de entrega de um pedido pago, que depois recebe transportadora, código de
rastreio e data de despacho. Os dois registros pertencem ao pedido e não existem sem ele.

**Why this priority**: Esses registros só são usados nos requisitos de pagamento e de
entrega, que vêm depois do REQ-05. Mesmo assim, a issue #5 exige as duas entidades agora,
para que o esquema do banco nasça completo.

**Independent Test**: Para um pedido gravado, gravar um pagamento e uma ordem de entrega e
lê-los a partir do pedido. Tentar gravar uma segunda ordem de entrega para o mesmo pedido
e verificar a recusa.

**Acceptance Scenarios**:

1. **Given** um pedido gravado, **When** um pagamento com valor, situação e data é gravado
   para ele, **Then** o pagamento pode ser lido a partir do pedido.
2. **Given** um pedido que já tem pagamento, **When** se tenta gravar um segundo pagamento
   para ele, **Then** a gravação é recusada.
3. **Given** um pedido gravado, **When** uma ordem de entrega é gravada para ele, **Then**
   ela pode ser lida a partir do pedido, inicialmente sem transportadora, sem código de
   rastreio e sem data de despacho.
4. **Given** um pedido que já tem ordem de entrega, **When** se tenta gravar outra para
   ele, **Then** a gravação é recusada.
5. **Given** uma ordem de entrega existente, **When** a transportadora, o código de
   rastreio e a data de despacho são gravados, **Then** ficam guardados e podem ser lidos.
6. **Given** um pagamento ou uma ordem de entrega que referencia um pedido inexistente,
   **When** ele é gravado, **Then** a gravação é recusada.

---

### User Story 4 - Atualizar o banco existente sem perder dados e ter catálogo em Development (Priority: P1)

Quem já tem o banco do REQ-03, com usuários cadastrados, atualiza a aplicação e passa a
ter as novas tabelas, sem perder contas nem perfis. Um banco vazio recebe o esquema
inteiro de uma vez. O fluxo é o mesmo do REQ-03:

- Em Development e nos testes, a aplicação aplica as migrations sozinha ao iniciar.
- Nos demais ambientes, as migrations são aplicadas por um comando documentado.

**Why this priority**: A issue #5 exige que a migration seja criada e executada. Perder as
contas existentes na atualização seria uma regressão do REQ-03.

**Independent Test**: Criar um banco só com a migration do REQ-03 e registrar um usuário.
Iniciar a aplicação nova em Development e verificar que as novas tabelas existem e que o
usuário continua fazendo login. Repetir a partir de um banco vazio.

**Acceptance Scenarios**:

1. **Given** um banco com a migration do REQ-03 e usuários cadastrados, **When** a
   aplicação nova inicia em Development, **Then** as novas tabelas passam a existir, e os
   usuários e perfis continuam iguais e funcionando.
2. **Given** um banco vazio, **When** a aplicação inicia em Development, **Then** o esquema
   completo (identidade e negócio) é criado.
3. **Given** um ambiente que não é Development nem de testes, com a nova migration
   pendente, **When** a aplicação inicia, **Then** ela se recusa a iniciar, como já define o
   REQ-03 (FR-017), e o comando documentado aplica a migration.
4. **Given** o banco atualizado, **When** se compara o modelo da aplicação com as
   migrations, **Then** não existe diferença pendente: nenhuma alteração de modelo fica sem
   migration.
5. **Given** um banco sem produtos, **When** a aplicação inicia em Development, **Then** o
   catálogo inicial existe, com ao menos um produto inativo e um com estoque zero.
6. **Given** o catálogo inicial já criado e um produto dele com estoque alterado, **When**
   a aplicação é reiniciada, **Then** nenhum produto é duplicado e o estoque alterado é
   mantido.
7. **Given** um ambiente que não é Development nem de testes, **When** a aplicação inicia,
   **Then** nenhum produto é criado.

---

### Edge Cases

- Duas gravações simultâneas de pedidos com o mesmo número: uma é aceita e a outra é
  recusada. Nunca existem dois pedidos com o mesmo número.
- Duas mudanças de status simultâneas no mesmo pedido: a primeira é aceita e a segunda
  recebe conflito de concorrência (FR-021). A decisão de reler e tentar de novo é do
  serviço que gravou.
- Uma alteração de produto que não mexe no estoque (por exemplo, o nome), feita ao mesmo
  tempo que uma reserva de estoque: também gera conflito de concorrência. A versão
  protege o produto inteiro, não só o estoque.
- Gravar um pedido sem nenhum item: aceito pelo armazenamento. A regra "ao menos 1 item" é
  do contrato v1 e do serviço de pedidos (REQ-05), como as demais regras de negócio.
- Valores monetários com mais de 2 casas decimais: o serviço que calcula o valor
  arredonda antes de gravar. O armazenamento guarda exatamente 2 casas e não pode perder
  precisão em valores de até 999.999.999,99.
- Status desconhecido (fora dos 6 do contrato) ou ausente: a gravação é recusada.
- Apagar um usuário que tem pedidos: recusado. Pedidos são histórico e não podem ficar sem
  dono.
- Apagar um pedido: os itens, o pagamento e a ordem de entrega dele também são apagados.
  Nenhuma operação do contrato v1 apaga pedidos; isso só acontece em manutenção.
- Banco fora do ar durante uma gravação: a gravação falha por inteiro, e nenhum pedido
  fica gravado pela metade (sem itens, ou com parte dos itens).

## Requirements *(mandatory)*

### Functional Requirements

**Produto**

- **FR-001**: O armazenamento MUST guardar produtos com:
  - identificador;
  - código (SKU), único sem diferenciar maiúsculas e minúsculas;
  - nome;
  - descrição opcional;
  - preço unitário em reais, com 2 casas decimais;
  - quantidade em estoque;
  - indicação de ativo ou inativo;
  - datas de criação e de atualização.
- **FR-002**: Preço e quantidade em estoque MUST NOT ser negativos. O armazenamento MUST
  recusar a gravação que violar isso, mesmo que a regra também exista no serviço.
- **FR-003**: O produto MUST ter controle de concorrência otimista: toda alteração muda a
  versão do produto. Uma gravação baseada em uma leitura desatualizada MUST ser recusada
  com um erro de concorrência que permita identificar o produto. Nenhuma alteração pode
  ser sobrescrita em silêncio.
- **FR-004**: Um produto referenciado por algum item de pedido MUST NOT poder ser apagado.
- **FR-020**: Em Development e no ambiente de testes, a API MUST criar ao iniciar um
  catálogo inicial fixo de cerca de 10 produtos. A maioria é ativa e tem estoque
  positivo, mas o catálogo inclui ao menos um produto inativo e um com estoque zero, para
  os testes de `PRODUTO_INDISPONIVEL` do REQ-05. A criação MUST ser idempotente: produtos são
  identificados pelo código (SKU), e um produto já existente MUST NOT ser duplicado nem ter
  preço, estoque ou situação alterados. A criação acontece depois das migrations e faz
  parte da preparação da inicialização do REQ-03, então segue as mesmas regras de falha e
  de desligamento (FR-018 do REQ-03). Nos demais ambientes, nenhum produto é criado.

**Pedido e itens**

- **FR-005**: O armazenamento MUST guardar pedidos com todos os dados do
  `PedidoResponseDTO` do contrato v1:
  - identificador;
  - número no padrão `PED-AAAAMMDD-NNNNNN`, único;
  - dono (usuário);
  - status;
  - valor total com 2 casas decimais;
  - moeda (`BRL`);
  - datas de criação e de atualização, em UTC.
- **FR-006**: O status MUST aceitar exatamente os 6 valores de `StatusPedido` do contrato
  v1 (Pendente, Processando, ProntoParaEnvio, Despachado, Cancelado, PagamentoRejeitado) e
  MUST ser guardado como texto legível, para que consultas diretas ao banco e futuras
  inclusões de valores não dependam de números. O motivo de cancelamento do contrato v1 é
  guardado na entrada de histórico da mudança (FR-022), não no pedido.
- **FR-007**: O dono do pedido MUST ser um usuário existente. Um usuário com pedidos MUST
  NOT poder ser apagado.
- **FR-008**: O armazenamento MUST guardar os itens de cada pedido com:
  - produto;
  - nome do produto no momento da compra;
  - quantidade;
  - preço unitário no momento da compra;
  - subtotal.

  O mesmo produto MUST NOT aparecer duas vezes no mesmo pedido, e a quantidade MUST ser
  maior que zero.
- **FR-009**: Gravar um pedido com seus itens MUST ser uma operação única: ou tudo é
  gravado, ou nada.
- **FR-010**: Consultar os pedidos de um cliente, buscar um pedido pelo número e listar
  pedidos por status MUST ser possível sem percorrer a tabela inteira de pedidos.
- **FR-011**: Apagar um pedido MUST apagar também seus itens, seu histórico de status, seu
  pagamento e sua ordem de entrega.
- **FR-021**: O pedido MUST ter controle de concorrência otimista, como o produto (FR-003).
  Toda alteração no pedido muda a sua versão, e uma gravação baseada em uma leitura
  desatualizada MUST ser recusada com um erro de concorrência que permita identificar o
  pedido. Assim, duas mudanças de status simultâneas (por exemplo, o cancelamento de um
  operador e a aprovação de pagamento de um worker) nunca se sobrescrevem em silêncio.

**Histórico de status**

- **FR-022**: O armazenamento MUST guardar o histórico de status de cada pedido, uma
  entrada por mudança, com:
  - pedido;
  - status anterior, vazio na entrada de criação;
  - novo status;
  - data/hora em UTC;
  - usuário que fez a mudança, opcional, vazio quando é um worker;
  - motivo, opcional, com até 500 caracteres.

  As entradas MUST ser lidas na ordem em que aconteceram, a partir do pedido, sem percorrer
  o histórico de todos os pedidos. Os status MUST aceitar só os 6 valores de
  `StatusPedido` e MUST ser guardados como texto, como no FR-006. As entradas são só de
  inclusão: nenhuma operação deste sistema altera ou apaga uma entrada, exceto pela remoção
  do pedido (FR-011).
- **FR-023**: Gravar uma mudança de status e sua entrada no histórico MUST ser uma operação
  única, junto com o pedido. Gravar quando e quais entradas existem (inclusive a de
  criação) é regra dos serviços que mudam o status (REQ-05 e workers). Este requisito
  garante só a estrutura e a ordem.

**Pagamento**

- **FR-012**: O armazenamento MUST guardar no máximo um pagamento por pedido, com:
  - identificador;
  - pedido;
  - valor com 2 casas decimais, não negativo;
  - situação (Pendente, Aprovado ou Recusado);
  - identificador da transação externa, opcional;
  - data de processamento, opcional;
  - datas de criação e de atualização.

**Ordem de entrega**

- **FR-013**: O armazenamento MUST guardar no máximo uma ordem de entrega por pedido, com:
  - identificador;
  - pedido;
  - transportadora, opcional;
  - código de rastreio, opcional;
  - data de despacho, opcional;
  - datas de criação e de atualização.

  Um código de rastreio, quando informado, MUST ser único dentro da mesma transportadora.

**Migrations e compatibilidade**

- **FR-014**: As novas tabelas MUST ser criadas por uma migration nova, posterior à do
  REQ-03. Ela MUST aplicar-se a um banco que já tem a migration do REQ-03 sem perder nem
  alterar contas e perfis, e também a um banco vazio.
- **FR-015**: A aplicação das migrations MUST seguir as regras do REQ-03:
  - Em Development e nos testes, a aplicação aplica as pendentes ao iniciar.
  - Nos demais ambientes, a aplicação não altera o esquema e se recusa a iniciar enquanto
    houver migration pendente.
- **FR-016**: O modelo da aplicação e as migrations MUST estar sincronizados. Uma
  verificação automatizada MUST falhar se houver alteração de modelo sem migration.
- **FR-017**: As tabelas, colunas e restrições novas MUST ter nomes em português,
  coerentes com as tabelas do REQ-03 (por exemplo, `Usuarios` e `Perfis`).

**Contrato e transição**

- **FR-018**: O contrato v1 MUST permanecer inalterado, e o teste de conformidade do REQ-01
  MUST continuar passando. As operações de pedido continuam respondendo 501 até o REQ-05.
- **FR-019**: Registro, login, perfis e usuários iniciais do REQ-03 MUST continuar
  funcionando, e os testes do REQ-03 MUST continuar passando sem alteração de
  comportamento.

### Key Entities *(include if feature involves data)*

- **Usuário**: já existe (REQ-03). Passa a ser o dono dos pedidos; um usuário tem zero ou
  mais pedidos.
- **Produto**: item vendável do catálogo. Tem código único, nome, preço, estoque, situação
  ativo/inativo e uma versão que muda a cada alteração (concorrência otimista).
- **Pedido**: compra de um cliente. Tem número único legível, dono, status do ciclo de
  vida, valor total em BRL, datas e uma versão que muda a cada alteração (concorrência
  otimista). Contém um ou mais itens e um histórico de status, e pode ter um pagamento e
  uma ordem de entrega.
- **Histórico de status do pedido**: registro, só de inclusão, de cada mudança de status
  de um pedido. Guarda o status anterior e o novo, quando aconteceu, quem fez (se foi uma
  pessoa) e o motivo.
- **Item de pedido**: linha do pedido. Guarda o produto, a quantidade e uma cópia do nome e
  do preço do momento da compra, além do subtotal.
- **Pagamento**: resultado do pagamento de um pedido (Pendente, Aprovado ou Recusado), com
  valor e referência da transação externa. No máximo um por pedido.
- **Ordem de entrega**: registro de logística de um pedido pago, com transportadora,
  código de rastreio e data de despacho preenchidos ao longo do processo. No máximo uma por
  pedido.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos campos do `PedidoResponseDTO` do contrato v1 podem ser montados a
  partir do que está guardado, sem cálculo que dependa do catálogo atual.
- **SC-002**: Em um teste com duas atualizações simultâneas do mesmo produto, repetido 50
  vezes, 100% das execuções terminam com exatamente uma gravação aceita e uma recusada
  por conflito. Nenhuma atualização é perdida, e o estoque nunca fica negativo.
- **SC-003**: Um banco com a migration do REQ-03 e usuários cadastrados é atualizado sem
  perda de nenhuma conta, e 100% dos usuários continuam fazendo login.
- **SC-004**: 100% das restrições de integridade deste requisito têm teste automatizado
  contra um banco real que comprova a recusa da gravação inválida:
  - unicidades;
  - valores não negativos;
  - referências obrigatórias;
  - um pagamento e uma ordem de entrega por pedido;
  - item sem produto repetido;
  - conflito de concorrência no produto e no pedido.
- **SC-005**: A busca dos pedidos de um cliente e a busca de um pedido pelo número
  respondem em menos de 100 ms no ambiente local, com 10.000 pedidos no banco.
- **SC-006**: A suíte de testes continua 100% verde. O teste de conformidade do contrato v1
  passa sem alteração no contrato, e a verificação de modelo sem migration pendente passa.

## Assumptions

- Este requisito não cria operações na API. Cadastro e manutenção de produtos não estão no
  contrato v1 nem em nenhuma issue aberta. Até existirem, o único catálogo é o inicial de
  Development e testes (FR-020). Em produção, os produtos precisam ser inseridos por
  manutenção direta no banco.
- Ficam fora do escopo:
  - O armazenamento das chaves de idempotência: o contrato v1 o atribui ao REQ-05.
  - A tabela de saída de eventos (outbox, Princípio IV): é do REQ-06.
  - A exposição do histórico de status na API: é da issue de consulta e rastreio (gh
    #11). Aqui ele só é guardado (FR-022).
- O número do pedido é gerado pelo serviço de pedidos (REQ-05). Este requisito só garante
  que ele seja único.
- Regras de negócio ficam no serviço e no domínio, não no banco:
  - quais transições de status são válidas;
  - ao menos 1 e no máximo 50 itens;
  - o valor total é igual à soma dos subtotais.

  O armazenamento garante só integridade estrutural: obrigatoriedade, unicidade,
  referências e valores não negativos.
- O controle de concorrência otimista vale para o produto (pedido pela issue #5) e para o
  pedido (FR-021). Itens, pagamento e ordem de entrega não têm controle próprio. Se
  algum requisito seguinte precisar dele, ele entra lá, em migration própria.
- A moeda é sempre BRL (contrato v1); o campo existe para não depender de constante no
  código de leitura.
- O ambiente local do REQ-02 (banco de dados no ar) é pré-requisito. A suíte de testes
  continua rodando sem Docker; os testes que precisam de banco real são ignorados quando o
  Docker não está disponível, como nos REQ-02 e REQ-03.
