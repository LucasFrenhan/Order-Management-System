# Feature Specification: Listagem e paginação de pedidos por perfil

**Feature Branch**: `feature/req-21-listagem-pedidos`

**Requisito**: REQ-21 (GitHub issue #52 — "[Pedidos] Listagem e paginação de pedidos por perfil")

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Listar pedidos com paginação e filtros, respeitando o escopo de cada perfil (issue #52)"

## Contexto

O contrato v1 deixou a listagem de pedidos de fora (REQ-01, Assumptions; REQ-05). Hoje um
pedido só pode ser consultado pelo identificador. O Cliente não consegue ver os próprios
pedidos sem ter guardado os identificadores, e a operação (OperadorLogistica e Admin) não tem
como procurar pedidos por status ou período, por exemplo para achar o que está parado em
`ProntoParaEnvio` desde ontem.

Este requisito acrescenta ao contrato v1 a listagem paginada de pedidos, com filtros e um
resumo de cada pedido. A consulta por identificador continua sendo o lugar do detalhe
(itens, histórico e entrega).

## User Scenarios & Testing *(mandatory)*

Os usuários deste requisito são o **Cliente**, que quer ver os próprios pedidos, e a
**operação** (OperadorLogistica e Admin), que quer encontrar pedidos de qualquer Cliente.

### User Story 1 - O Cliente lista os próprios pedidos (Priority: P1)

O Cliente autenticado pede a lista de pedidos e recebe só os dele, dos mais recentes para os
mais antigos, em páginas. Cada item traz um resumo (número, status, valor total, quantidade
de itens e datas) e o identificador, que leva à consulta completa.

**Why this priority**: É a lacuna mais visível hoje: sem a lista, o Cliente depende de ter
guardado o identificador de cada pedido. Sozinha, esta história já entrega valor.

**Independent Test**: Criar pedidos para dois Clientes, listar com o token de cada um e
conferir que cada lista traz só os pedidos do dono, na ordem certa, com o total certo.

**Acceptance Scenarios**:

1. **Given** um Cliente com 3 pedidos e outro Cliente com 2, **When** o primeiro lista os
   pedidos sem filtros, **Then** recebe 200 com os 3 pedidos dele, do mais recente para o mais
   antigo, e o total de 3.
2. **Given** um Cliente sem pedidos, **When** ele lista os pedidos, **Then** recebe 200 com a
   lista vazia e o total 0.
3. **Given** um pedido do Cliente, **When** ele aparece na lista, **Then** o resumo traz o
   identificador, o número, o status, o valor total, a quantidade de itens, a data de criação
   e a data da última alteração, e não traz os itens, o histórico nem a entrega.
4. **Given** uma requisição sem token, **When** ela chega, **Then** a resposta é 401.

---

### User Story 2 - A operação lista e filtra todos os pedidos (Priority: P1)

O OperadorLogistica ou o Admin lista os pedidos de todos os Clientes e filtra por status (um
ou mais), por período de criação e por Cliente. Os filtros se combinam.

**Why this priority**: É o outro critério de aceite central da issue: a operação precisa
achar pedidos parados ou de um Cliente específico sem saber os identificadores.

**Independent Test**: Criar pedidos de vários Clientes em status e datas diferentes, listar
com o token da operação usando cada filtro e combinações, e conferir os resultados e o total.

**Acceptance Scenarios**:

1. **Given** pedidos de vários Clientes, **When** a operação lista sem filtros, **Then**
   recebe os pedidos de todos os Clientes, do mais recente para o mais antigo.
2. **Given** pedidos em vários status, **When** a operação filtra por `ProntoParaEnvio`,
   **Then** recebe só os pedidos nesse status.
3. **Given** pedidos em vários status, **When** a operação filtra por `Pendente` e
   `Processando` ao mesmo tempo, **Then** recebe os pedidos de qualquer um dos dois.
4. **Given** pedidos criados em dias diferentes, **When** a operação filtra pelo período de
   criação, **Then** recebe só os criados dentro do período, com os limites incluídos.
5. **Given** pedidos de vários Clientes, **When** a operação filtra por um Cliente, **Then**
   recebe só os pedidos dele; um Cliente sem pedidos dá a lista vazia.
6. **Given** os filtros de status, período e Cliente juntos, **When** a operação lista,
   **Then** recebe só os pedidos que atendem a todos.

---

### User Story 3 - Paginação previsível e erros claros (Priority: P2)

Quem lista escolhe a página e o tamanho dela, dentro de um máximo, e recebe junto o total de
pedidos e de páginas para navegar. A ordem é estável: a mesma consulta, sem pedidos novos,
devolve as mesmas páginas, sem repetir nem pular pedidos. Parâmetros inválidos dão 400 com
uma mensagem que diz o que corrigir.

**Why this priority**: Sem limite, uma lista grande pesa para quem consulta e para o sistema;
sem ordem estável, a navegação entre páginas perde ou repete pedidos.

**Independent Test**: Criar mais pedidos do que cabem numa página, percorrer todas as páginas
e conferir que a união delas é o conjunto completo, sem repetição; depois testar cada
parâmetro inválido.

**Acceptance Scenarios**:

1. **Given** 45 pedidos visíveis e tamanho 20, **When** as páginas 1, 2 e 3 são pedidas,
   **Then** elas trazem 20, 20 e 5 pedidos, sem repetição, e cada resposta informa o total de
   45 e de 3 páginas.
2. **Given** 45 pedidos visíveis e tamanho 20, **When** a página 4 é pedida, **Then** a
   resposta é 200 com a lista vazia e o mesmo total de 45 e 3 páginas.
3. **Given** dois pedidos criados no mesmo instante, **When** a lista é paginada, **Then** a
   ordem entre eles é sempre a mesma.
4. **Given** um tamanho de página acima do máximo, menor que 1, ou uma página menor que 1,
   **When** a lista é pedida, **Then** a resposta é 400 em ProblemDetails, com a mensagem do
   parâmetro inválido.
5. **Given** um status desconhecido, uma data inválida ou um período com o início depois do
   fim, **When** a lista é pedida, **Then** a resposta é 400 em ProblemDetails.
6. **Given** nenhum parâmetro de paginação, **When** a lista é pedida, **Then** vem a página 1
   com o tamanho padrão.

---

### User Story 4 - O Cliente não alcança pedidos de outros (Priority: P1)

O filtro por Cliente é exclusivo da operação. Um Cliente que tenta usá-lo recebe 403, mesmo
que informe o próprio identificador, e nenhuma combinação de parâmetros faz aparecer pedido de
outro Cliente na lista dele.

**Why this priority**: É a regra de segurança do requisito (Princípio III): o escopo do
Cliente não pode vazar por um parâmetro.

**Independent Test**: Com o token de um Cliente, pedir a lista com o filtro por Cliente
(o de outro e o próprio) e conferir 403; depois combinar os demais filtros e conferir que só
aparecem pedidos do dono.

**Acceptance Scenarios**:

1. **Given** um Cliente autenticado, **When** ele lista com o filtro por Cliente apontando
   para outro Cliente, **Then** a resposta é 403 e nada é listado.
2. **Given** um Cliente autenticado, **When** ele lista com o filtro por Cliente apontando
   para ele mesmo, **Then** a resposta também é 403 (a regra não depende do valor).
3. **Given** um Cliente autenticado, **When** ele usa os filtros de status e período, **Then**
   eles se aplicam só aos pedidos dele.

---

### Edge Cases

- Página depois da última: 200 com a lista vazia e os totais (não é erro).
- Nenhum pedido atende aos filtros: 200 com a lista vazia, total 0 e 0 páginas.
- Filtro por um Cliente que não existe: 200 com a lista vazia (a operação não recebe 404, e o
  filtro não serve para descobrir se um usuário existe).
- Identificador de Cliente malformado no filtro: 400.
- Período só com início ou só com fim: vale como período aberto do outro lado.
- Período com início igual ao fim: válido; traz os pedidos criados exatamente naquele instante.
- Datas sem fuso horário: 400, para não haver dúvida sobre o instante (as datas do contrato
  são com fuso).
- Status repetido no filtro: equivale a informá-lo uma vez.
- `PagamentoRejeitado` e `Cancelado` são status como os outros e podem ser filtrados.
- Pedido criado ou com status alterado enquanto alguém navega pelas páginas: a página seguinte
  pode deslocar um pedido (ver Assumptions); a consulta por identificador continua sendo a
  fonte do estado atual.
- Usuário autenticado sem nenhum dos perfis conhecidos: 403, como nas outras rotas de pedidos.
- Pedido em atendimento por outra operação no momento da listagem: aparece com o status já
  gravado; a listagem não bloqueia nem espera mudanças.

## Requirements *(mandatory)*

### Functional Requirements

**Rota e acesso**

- **FR-001**: O contrato v1 MUST ganhar a listagem de pedidos na coleção de pedidos, como
  acréscimo, sem alterar nem remover nada do que já existe (Princípio II); a verificação de
  compatibilidade do contrato MUST continuar verde.
- **FR-002**: A listagem MUST exigir autenticação; sem token ou com token inválido, a
  resposta MUST ser 401.
- **FR-003**: A listagem MUST estar disponível para os perfis Cliente, OperadorLogistica e
  Admin; um usuário autenticado sem nenhum desses perfis MUST receber 403.

**Escopo por perfil**

- **FR-004**: Para o Cliente, a listagem MUST trazer somente os pedidos dele, qualquer que
  seja a combinação de parâmetros.
- **FR-005**: Para OperadorLogistica e Admin, a listagem MUST trazer os pedidos de todos os
  Clientes.
- **FR-006**: O filtro por Cliente MUST ser exclusivo de OperadorLogistica e Admin; o Cliente
  que o informa MUST receber 403 com uma mensagem em português dizendo que o filtro é
  exclusivo da operação, independentemente do valor informado.
- **FR-007**: Um usuário com o perfil Cliente e também um perfil da operação MUST ter o escopo
  da operação (o perfil mais amplo prevalece, como na consulta por identificador).

**Filtros**

- **FR-008**: A listagem MUST aceitar o filtro por status com um ou mais valores do ciclo de
  vida do pedido; com mais de um valor, MUST trazer os pedidos em qualquer um deles.
- **FR-009**: A listagem MUST aceitar o filtro por período de criação, com início e fim
  opcionais e inclusivos, informados como data e hora com fuso.
- **FR-010**: A listagem MUST aceitar, para a operação, o filtro por identificador do Cliente.
- **FR-011**: Os filtros MUST se combinar (todos precisam ser atendidos).
- **FR-012**: Sem filtros, a listagem MUST trazer todos os pedidos do escopo do perfil.

**Paginação e ordem**

- **FR-013**: A listagem MUST ser paginada por número de página (começando em 1) e tamanho de
  página; sem esses parâmetros, MUST usar a página 1 e o tamanho padrão de 20.
- **FR-014**: O tamanho de página MUST ficar entre 1 e 100; fora disso, a resposta MUST ser
  400.
- **FR-015**: Uma página menor que 1 MUST dar 400; uma página depois da última MUST dar 200
  com a lista vazia e os totais.
- **FR-016**: A ordem MUST ser do pedido criado mais recentemente para o mais antigo, com um
  desempate fixo entre pedidos criados no mesmo instante, para que a mesma consulta devolva
  sempre a mesma ordem.
- **FR-017**: Cada resposta MUST informar a página e o tamanho usados, o total de pedidos que
  atendem aos filtros e o total de páginas.

**Resumo do pedido**

- **FR-018**: Cada item da lista MUST trazer o identificador, o número, o status, o valor
  total, a quantidade de itens, o identificador do Cliente, a data de criação e a data da
  última alteração do pedido.
- **FR-019**: O item da lista MUST NOT trazer os itens, o histórico, a entrega, o pagamento
  nem o estorno; o detalhe continua na consulta por identificador, que não muda.
- **FR-020**: Os valores do resumo MUST ser os mesmos que a consulta por identificador mostra
  para o mesmo pedido no mesmo momento (número, status, valor total e datas).

**Validação e erros**

- **FR-021**: Parâmetros inválidos (status desconhecido, data malformada ou sem fuso, período
  com início depois do fim, identificador de Cliente malformado, página ou tamanho fora do
  permitido ou não numéricos) MUST dar 400 em ProblemDetails, com mensagens em português que
  digam qual parâmetro corrigir e sem revelar detalhes internos.
- **FR-022**: Quando houver mais de um parâmetro inválido, a resposta 400 MUST listar todos
  eles, como nas outras validações do contrato.

**Observabilidade e documentação**

- **FR-023**: A listagem MUST NOT gravar nada nem publicar eventos; é só leitura.
- **FR-024**: A listagem MUST registrar um log de depuração com o perfil, os filtros usados
  (sem dados pessoais), a página e a quantidade de itens devolvida, no mesmo padrão de IDs de
  log das rotas de pedidos.
- **FR-025**: O documento do fluxo de consulta (fluxo 3) MUST passar a descrever a listagem,
  com exemplo, a tabela de respostas e os motivos, e o índice dos fluxos MUST refletir a nova
  rota.

### Key Entities

- **Resumo do pedido**: a visão de um pedido na lista: identificador, número, status, valor
  total, quantidade de itens, Cliente, criação e última alteração. É derivado do pedido, sem
  dado novo.
- **Página de pedidos**: o resultado da listagem: os resumos da página, a página e o tamanho
  usados, o total de pedidos que atendem aos filtros e o total de páginas.
- **Filtros da listagem**: status (um ou mais), início e fim do período de criação e Cliente
  (só para a operação).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 100% dos testes, um Cliente nunca recebe na lista um pedido de outro
  Cliente, com qualquer combinação de parâmetros.
- **SC-002**: Percorrer todas as páginas de uma consulta, sem pedidos novos no meio, devolve
  exatamente o conjunto de pedidos do total informado, sem repetição e sem falta.
- **SC-003**: A operação encontra os pedidos de um status num período em uma única consulta,
  sem precisar conhecer nenhum identificador.
- **SC-004**: Com 10.000 pedidos na base, qualquer página da listagem, com ou sem filtros,
  responde em até 1 segundo no ambiente de testes.
- **SC-005**: A verificação de compatibilidade do contrato v1 aponta zero mudanças
  incompatíveis.
- **SC-006**: Todos os parâmetros inválidos testados dão 400 com mensagem que identifica o
  parâmetro; nenhum dá 500.

## Assumptions

- A paginação é por número de página e tamanho (não por cursor), porque a issue fala em
  "página fora do intervalo" e o volume esperado não exige cursor. Com pedidos criados durante
  a navegação, uma página seguinte pode repetir um pedido da anterior; isso fica registrado
  como limitação no fluxo 3.
- Tamanho padrão 20 e máximo 100, valores comuns em APIs de listagem; podem ser revistos no
  clarify.
- Página depois da última responde 200 vazia, e não 404 ou 400, para que um cliente que
  navega não trate como erro o fim da lista.
- O filtro de período usa a data de criação, como pede a issue; a data da última alteração
  aparece no resumo, mas não é filtro nesta versão.
- O filtro por Cliente é pelo identificador do usuário; filtro por e-mail ou nome fica fora,
  junto com a busca textual.
- O Cliente que usa o filtro por Cliente recebe 403, e não um filtro ignorado, para que o uso
  indevido fique visível e testável (Princípio III; a constituição pede testes de 403).
- A ordem é só pela criação, do mais recente para o mais antigo; outras ordenações ficam fora.
- A listagem não tem `ETag`; o `ETag` continua só na consulta por identificador (REQ-18).
- Fora do escopo, como na issue: busca textual e exportação (CSV e similares).
- A listagem reaproveita a autenticação, os perfis e o formato de erro (ProblemDetails) já
  usados nas outras rotas de pedidos.
