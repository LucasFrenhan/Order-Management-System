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

## Clarifications

### Session 2026-11-03

- Q: A paginação da listagem deve ser por número de página e tamanho, ou por cursor? → A: Por
  cursor, só com navegação para a próxima página e para a anterior; sem número de página,
  sem salto para uma página qualquer e sem total (FR-013 a FR-017).
- Q: Além do período de criação, a operação também deve poder filtrar pela data da última
  alteração do pedido? → A: Sim: período de criação e período de última alteração, os dois
  opcionais e combináveis; a ordem continua pela criação (FR-009, FR-009a).
- Q: Se um Cliente mandar o filtro por Cliente, a API deve recusar com 403 ou ignorar o
  filtro? → A: 403 sempre que o Cliente informar o filtro, mesmo com o próprio
  identificador (FR-006, US4).

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
conferir que cada lista traz só os pedidos do dono, na ordem certa.

**Acceptance Scenarios**:

1. **Given** um Cliente com 3 pedidos e outro Cliente com 2, **When** o primeiro lista os
   pedidos sem filtros, **Then** recebe 200 com os 3 pedidos dele, do mais recente para o mais
   antigo, sem cursor da próxima nem da anterior.
2. **Given** um Cliente sem pedidos, **When** ele lista os pedidos, **Then** recebe 200 com a
   lista vazia, sem cursores.
3. **Given** um pedido do Cliente, **When** ele aparece na lista, **Then** o resumo traz o
   identificador, o número, o status, o valor total, a quantidade de itens, a data de criação
   e a data da última alteração, e não traz os itens, o histórico nem a entrega.
4. **Given** uma requisição sem token, **When** ela chega, **Then** a resposta é 401.

---

### User Story 2 - A operação lista e filtra todos os pedidos (Priority: P1)

O OperadorLogistica ou o Admin lista os pedidos de todos os Clientes e filtra por status (um
ou mais), por período de criação, por período de última alteração e por Cliente. Os filtros
se combinam.

**Why this priority**: É o outro critério de aceite central da issue: a operação precisa
achar pedidos parados ou de um Cliente específico sem saber os identificadores.

**Independent Test**: Criar pedidos de vários Clientes em status e datas diferentes, listar
com o token da operação usando cada filtro e combinações, e conferir os resultados.

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
6. **Given** um pedido criado há uma semana que passou a `ProntoParaEnvio` ontem e outro que
   passou hoje, **When** a operação filtra por `ProntoParaEnvio` com a última alteração até o
   fim de ontem, **Then** recebe só o primeiro (o pedido parado).
7. **Given** os filtros de status, os dois períodos e Cliente juntos, **When** a operação
   lista, **Then** recebe só os pedidos que atendem a todos, na ordem de criação.

---

### User Story 3 - Navegação por cursor e erros claros (Priority: P2)

Quem lista escolhe o tamanho da página, dentro de um máximo, e recebe junto um cursor para a
próxima página e outro para a anterior, quando existem. A navegação é só para a frente ou para
trás, a partir da página atual: não há número de página, salto para uma página qualquer nem
total. A ordem é estável e a navegação não repete nem pula pedidos, mesmo que pedidos novos
sejam criados no meio. Parâmetros inválidos dão 400 com uma mensagem que diz o que corrigir.

**Why this priority**: Sem limite, uma lista grande pesa para quem consulta e para o sistema;
sem ordem estável, a navegação entre páginas perde ou repete pedidos.

**Independent Test**: Criar mais pedidos do que cabem numa página, percorrer a lista pelo
cursor da próxima até o fim e conferir que a união das páginas é o conjunto completo, sem
repetição; voltar pelo cursor da anterior e conferir as mesmas páginas; depois testar cada
parâmetro inválido.

**Acceptance Scenarios**:

1. **Given** 45 pedidos visíveis e tamanho 20, **When** a primeira página é pedida e depois
   as seguintes pelo cursor da próxima, **Then** elas trazem 20, 20 e 5 pedidos, sem
   repetição; a última não tem cursor da próxima.
2. **Given** a primeira página, **When** ela é devolvida, **Then** não tem cursor da anterior.
3. **Given** a terceira página, **When** o cursor da anterior é usado, **Then** volta a
   segunda página, com os mesmos pedidos e na mesma ordem, e ela tem os dois cursores.
4. **Given** alguém navegando pela lista, **When** um pedido novo é criado entre uma página e
   a seguinte, **Then** a página seguinte não repete nem pula nenhum pedido que já estava na
   lista; o pedido novo aparece ao recomeçar da primeira página.
5. **Given** dois pedidos criados no mesmo instante, **When** a lista é paginada, **Then** a
   ordem entre eles é sempre a mesma e nenhum dos dois se perde na troca de página.
6. **Given** um tamanho de página acima do máximo, menor que 1 ou não numérico, **When** a
   lista é pedida, **Then** a resposta é 400 em ProblemDetails, com a mensagem do parâmetro
   inválido.
7. **Given** um cursor malformado, adulterado ou gerado com outros filtros, **When** a lista é
   pedida, **Then** a resposta é 400 em ProblemDetails, dizendo para recomeçar da primeira
   página.
8. **Given** um status desconhecido, uma data inválida ou um período (de criação ou de última
   alteração) com o início depois do fim, **When** a lista é pedida, **Then** a resposta é 400 em ProblemDetails.
9. **Given** nenhum parâmetro de paginação, **When** a lista é pedida, **Then** vem a primeira
   página com o tamanho padrão.

---

### User Story 4 - O Cliente não alcança pedidos de outros (Priority: P1)

O filtro por Cliente é exclusivo da operação. Um Cliente que tenta usá-lo recebe 403, mesmo
que informe o próprio identificador, e nenhuma combinação de parâmetros, cursor incluído, faz
aparecer pedido de outro Cliente na lista dele.

**Why this priority**: É a regra de segurança do requisito (Princípio III): o escopo do
Cliente não pode vazar por um parâmetro.

**Independent Test**: Com o token de um Cliente, pedir a lista com o filtro por Cliente
(o de outro e o próprio) e conferir 403; usar um cursor obtido pela operação e conferir 400;
depois combinar os demais filtros e conferir que só aparecem pedidos do dono.

**Acceptance Scenarios**:

1. **Given** um Cliente autenticado, **When** ele lista com o filtro por Cliente apontando
   para outro Cliente, **Then** a resposta é 403 e nada é listado.
2. **Given** um Cliente autenticado, **When** ele lista com o filtro por Cliente apontando
   para ele mesmo, **Then** a resposta também é 403 (a regra não depende do valor).
3. **Given** um Cliente autenticado, **When** ele usa os filtros de status e de período, **Then**
   eles se aplicam só aos pedidos dele.
4. **Given** um cursor devolvido para a operação ou para outro Cliente, **When** um Cliente o
   usa, **Then** a resposta é 400 (cursor gerado para outra consulta) e nada é listado.

---

### Edge Cases

- Fim da lista: a última página não tem cursor da próxima, e a primeira não tem cursor da
  anterior; não existe "página fora do intervalo", porque não há número de página.
- Nenhum pedido atende aos filtros na primeira página: 200 com a lista vazia e sem cursores.
- Página vazia vinda de um cursor (os pedidos à frente saíram do filtro no meio da
  navegação): 200 com a lista vazia, sem cursor nesse sentido e com o cursor do sentido oposto,
  para o cliente poder voltar.
- Cursor que aponta para um pedido que, depois, mudou de status e saiu do filtro: a
  navegação continua do ponto em que estava, sem erro (o cursor guarda a posição na ordem,
  não depende de o pedido ainda atender ao filtro).
- Pedido que muda de status durante a navegação com filtro de status: pode sair ou entrar na
  lista entre uma página e outra; a navegação não repete nem pula os pedidos que continuam no
  filtro.
- Filtro por um Cliente que não existe: 200 com a lista vazia (a operação não recebe 404, e o
  filtro não serve para descobrir se um usuário existe).
- Identificador de Cliente malformado no filtro: 400, para qualquer perfil (inclusive o
  Cliente, que recebe 403 só com um identificador bem formado).
- Período (de criação ou de última alteração) só com início ou só com fim: vale como período
  aberto do outro lado.
- Pedido alterado durante a navegação com filtro de última alteração: pode sair ou entrar no
  filtro entre uma página e outra, como no filtro de status; a ordem e o cursor seguem a
  criação, que não muda.
- Período com início igual ao fim: válido; traz os pedidos criados exatamente naquele instante.
- Datas sem fuso horário: 400, para não haver dúvida sobre o instante (as datas do contrato
  são com fuso).
- Status repetido no filtro: equivale a informá-lo uma vez.
- `PagamentoRejeitado` e `Cancelado` são status como os outros e podem ser filtrados.
- Cursor usado com um tamanho de página diferente do original: aceito; a página seguinte vem
  com o novo tamanho, a partir da mesma posição.
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
  seja a combinação de parâmetros, cursor incluído.
- **FR-005**: Para OperadorLogistica e Admin, a listagem MUST trazer os pedidos de todos os
  Clientes.
- **FR-006**: O filtro por Cliente MUST ser exclusivo de OperadorLogistica e Admin; o Cliente
  que o informa MUST receber 403 com uma mensagem em português dizendo que o filtro é
  exclusivo da operação, independentemente do valor informado, desde que seja um
  identificador bem formado (um malformado dá 400 antes, pela validação de FR-021).
- **FR-007**: Um usuário com o perfil Cliente e também um perfil da operação MUST ter o escopo
  da operação (o perfil mais amplo prevalece, como na consulta por identificador).

**Filtros**

- **FR-008**: A listagem MUST aceitar o filtro por status com um ou mais valores do ciclo de
  vida do pedido; com mais de um valor, MUST trazer os pedidos em qualquer um deles.
- **FR-009**: A listagem MUST aceitar o filtro por período de criação, com início e fim
  opcionais e inclusivos, informados como data e hora com fuso.
- **FR-009a**: A listagem MUST aceitar o filtro por período de última alteração, com as mesmas
  regras do período de criação; os dois períodos podem ser usados juntos, e a ordem continua
  pela criação (FR-016).
- **FR-010**: A listagem MUST aceitar, para a operação, o filtro por identificador do Cliente.
- **FR-011**: Os filtros MUST se combinar (todos precisam ser atendidos).
- **FR-012**: Sem filtros, a listagem MUST trazer todos os pedidos do escopo do perfil.

**Paginação por cursor e ordem**

- **FR-013**: A listagem MUST ser paginada por cursor: sem cursor, devolve a primeira página;
  com o cursor da próxima ou da anterior devolvido numa resposta, devolve a página seguinte ou
  a anterior. Não há número de página, salto para uma página qualquer nem total.
- **FR-014**: O tamanho de página MUST ficar entre 1 e 100, com padrão 20; fora disso, ou não
  numérico, a resposta MUST ser 400.
- **FR-015**: Cada resposta MUST trazer o cursor da próxima página, ausente na última, e o
  cursor da página anterior, ausente na primeira, além do tamanho usado.
- **FR-016**: A ordem MUST ser do pedido criado mais recentemente para o mais antigo, com um
  desempate fixo entre pedidos criados no mesmo instante. A navegação MUST NOT repetir nem
  pular pedidos que continuam atendendo aos filtros, mesmo com pedidos criados ou alterados
  entre uma página e outra.
- **FR-017**: O cursor MUST ser opaco para quem consulta e valer só para a consulta que o
  gerou (mesmo usuário e mesmos filtros). Um cursor malformado, adulterado, de outro usuário
  ou usado com outros filtros MUST dar 400, com uma mensagem em português dizendo para
  recomeçar da primeira página, sem revelar o conteúdo do cursor.

**Resumo do pedido**

- **FR-018**: Cada item da lista MUST trazer o identificador, o número, o status, o valor
  total, a quantidade de itens (linhas de item, isto é, produtos distintos, e não a soma das
  quantidades), o identificador do Cliente, a data de criação e a data da
  última alteração do pedido.
- **FR-019**: O item da lista MUST NOT trazer os itens, o histórico, a entrega, o pagamento
  nem o estorno; o detalhe continua na consulta por identificador, que não muda.
- **FR-020**: Os valores do resumo MUST ser os mesmos que a consulta por identificador mostra
  para o mesmo pedido no mesmo momento (número, status, valor total e datas).

**Validação e erros**

- **FR-021**: Parâmetros inválidos (status desconhecido, data malformada ou sem fuso, período
  de criação ou de última alteração com início depois do fim, identificador de Cliente malformado, tamanho fora do permitido ou
  não numérico, cursor inválido) MUST dar 400 em ProblemDetails, com mensagens em português
  que digam qual parâmetro corrigir e sem revelar detalhes internos.
- **FR-022**: Quando houver mais de um parâmetro inválido, a resposta 400 MUST listar todos
  eles, como nas outras validações do contrato. O cursor é conferido depois dos demais
  parâmetros (depende do usuário e dos filtros já válidos), então só aparece no 400 quando
  todos os outros estão válidos.

**Observabilidade e documentação**

- **FR-023**: A listagem MUST NOT gravar nada nem publicar eventos; é só leitura.
- **FR-024**: A listagem MUST registrar um log de depuração com o perfil, os filtros usados
  (sem dados pessoais), se veio de um cursor e a quantidade de itens devolvida, no mesmo
  padrão de IDs de log das rotas de pedidos.
- **FR-025**: O documento do fluxo de consulta (fluxo 3) MUST passar a descrever a listagem,
  com exemplo, a tabela de respostas e os motivos, e o índice dos fluxos MUST refletir a nova
  rota.

### Key Entities

- **Resumo do pedido**: a visão de um pedido na lista: identificador, número, status, valor
  total, quantidade de itens, Cliente, criação e última alteração. É derivado do pedido, sem
  dado novo.
- **Página de pedidos**: o resultado da listagem: os resumos da página, o tamanho usado e os
  cursores da próxima e da anterior, quando existem.
- **Cursor**: um marcador opaco da posição na ordem da listagem, ligado ao usuário e aos
  filtros da consulta que o gerou, e à direção (próxima ou anterior).
- **Filtros da listagem**: status (um ou mais), início e fim do período de criação, início e
  fim do período de última alteração e Cliente
  (só para a operação).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 100% dos testes, um Cliente nunca recebe na lista um pedido de outro
  Cliente, com qualquer combinação de parâmetros e cursores.
- **SC-002**: Percorrer a lista inteira pelo cursor da próxima, mesmo com pedidos criados
  durante a navegação, devolve todos os pedidos que existiam no início, sem repetição e sem
  falta; voltar pelo cursor da anterior devolve as mesmas páginas.
- **SC-003**: A operação encontra, em uma única consulta e sem conhecer nenhum identificador,
  os pedidos de um status criados num período e os pedidos parados num status desde antes de
  um instante.
- **SC-004**: Com 10.000 pedidos na base, a primeira página e qualquer página alcançada pelo
  cursor, com ou sem filtros, respondem em até 1 segundo no ambiente de testes, sem piorar
  quanto mais fundo se navega.
- **SC-005**: A verificação de compatibilidade do contrato v1 aponta zero mudanças
  incompatíveis.
- **SC-006**: Todos os parâmetros inválidos testados, cursores adulterados incluídos, dão 400
  com mensagem que identifica o parâmetro; nenhum dá 500.

## Assumptions

- A paginação é por cursor, por decisão do usuário (clarify): a navegação não repete nem pula
  pedidos com inserções no meio e não fica mais lenta nas páginas fundas. Em troca, não há
  total nem salto para uma página qualquer; quem precisa recomeçar volta à primeira página.
- Tamanho padrão 20 e máximo 100, valores comuns em APIs de listagem.
- O cursor não expira por tempo: ele guarda uma posição na ordem, que continua válida.
- Há dois filtros de período, por decisão do usuário (clarify): o de criação, que a issue
  pede, e o de última alteração, para achar pedidos parados num status. A "última alteração"
  é a mesma data que aparece no resumo.
- O filtro por Cliente é pelo identificador do usuário; filtro por e-mail ou nome fica fora,
  junto com a busca textual.
- O Cliente que usa o filtro por Cliente recebe 403, e não um filtro ignorado, por decisão do
  usuário (clarify): o uso indevido fica visível e testável (Princípio III; a constituição
  pede testes de 403).
- A ordem é só pela criação, do mais recente para o mais antigo; outras ordenações ficam fora.
- A listagem não tem `ETag`; o `ETag` continua só na consulta por identificador (REQ-18).
- O critério de aceite da issue "página fora do intervalo" passa a ser coberto pelo fim da
  lista (sem cursor da próxima) e pelo cursor inválido (400).
- Fora do escopo, como na issue: busca textual e exportação (CSV e similares).
- A listagem reaproveita a autenticação, os perfis e o formato de erro (ProblemDetails) já
  usados nas outras rotas de pedidos.
