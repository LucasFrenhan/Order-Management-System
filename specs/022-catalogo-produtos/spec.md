# Feature Specification: Cadastro e gestão do catálogo de produtos

**Feature Branch**: `feature/req-22-catalogo-produtos`

**Requisito**: REQ-22 (GitHub issue #53 — "[Catálogo] Cadastro e gestão de produtos (preço, estoque e ativação)")

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Gerenciar o catálogo de produtos pela API: cadastro, consulta, listagem, preço, estoque e ativação (issue #53)"

## Contexto

O catálogo de produtos é fixo: a API cria os produtos `CAT-001` a `CAT-010` na partida, só em
Development e nos testes (REQ-05). O cadastro e a listagem de produtos ficaram fora da v1
(REQ-01). O preço de cada item vem do cadastro no momento da criação do pedido, e o estoque é
reservado e devolvido pelo processo de workers (REQ-08), mas não há como criar produtos, mudar
preço, repor estoque ou desativar um produto pela API. O Cliente também não tem como descobrir
quais produtos existem: hoje ele precisa conhecer os identificadores de antemão.

Este requisito acrescenta ao contrato v1 a consulta e a listagem de produtos para quem cria
pedidos e a gestão do catálogo pela operação: cadastro, alteração de dados e preço, reposição e
ajuste de estoque, desativação e reativação.

## Clarifications

### Session 2026-11-03

Respostas dadas pelo usuário de uma vez, no início do clarify:

- Q: Quem gerencia o catálogo: só o Admin ou um perfil novo de catálogo? → A: Só o Admin; nenhum
  perfil novo (FR-008).
- Q: O que o Cliente vê do catálogo? → A: Só os produtos ativos, sem a quantidade em estoque
  (FR-003, FR-005).
- Q: Como é a listagem de produtos? → A: Igual à de pedidos (REQ-21): paginação por cursor, só
  com próxima e anterior, tamanho de 1 a 100 (padrão 20), sem total (FR-006).
- Q: O histórico dos ajustes de estoque precisa de uma tabela? → A: Não; fica só no log (FR-022).
- Q: A gestão do catálogo publica eventos novos? → A: Não; nenhum evento `produto.*`.
- Q: O catálogo inicial (`CAT-001` a `CAT-010`) continua? → A: Sim, só em Development e nos
  testes (FR-023).
- Q: Na listagem de produtos, "igual à de pedidos" também vale para a ordem e para a busca? → A:
  Não: o mesmo modelo de cursor, mas ordenado pelo nome (desempate pelo SKU) e com busca
  opcional; a busca é de texto otimizada para o português do Brasil (busca textual completa, por
  palavras, sem diferenciar acentos e maiúsculas, com plural e singular) (FR-007).
- Q: Com busca por texto, a ordem é pelo nome ou pela relevância? → A: Pelo nome (desempate pelo
  SKU), com ou sem busca; a busca só filtra, e o cursor é o mesmo (FR-006, FR-007).
- Q: Um produto recém-cadastrado ou alterado pode demorar a aparecer na busca por texto? → A:
  Pode: aparece na busca em até 10 segundos depois de gravado; a consulta por id e a listagem sem
  busca são imediatas (FR-007a, SC-006).
- Q: O OperadorLogistica vê a quantidade em estoque e os inativos, como o Admin? → A: Sim, como o
  Admin, mas só para leitura (FR-004, FR-005, US2).

## User Scenarios & Testing *(mandatory)*

Os usuários deste requisito são o **Cliente**, que precisa achar os produtos para montar um
pedido, e o **Admin**, que mantém o catálogo.

### User Story 1 - O Cliente encontra os produtos que pode pedir (Priority: P1)

O Cliente autenticado lista os produtos ativos e consulta um produto pelo identificador. Cada
produto traz o identificador (usado no pedido), o SKU, o nome, a descrição, o preço e se há
estoque disponível. Produtos desativados não aparecem para ele.

**Why this priority**: Sem isso o Cliente não consegue montar um pedido sem conhecer os
identificadores de antemão; é o que torna a criação de pedido utilizável fora dos testes.

**Independent Test**: Com produtos ativos e inativos no catálogo, listar e consultar com o token
de um Cliente e conferir que só os ativos aparecem, com os campos do contrato, e que o
identificador devolvido cria um pedido.

**Acceptance Scenarios**:

1. **Given** um catálogo com produtos ativos e inativos, **When** o Cliente lista os produtos,
   **Then** recebe 200 só com os ativos, ordenados pelo nome, com identificador, SKU, nome,
   descrição, preço e disponibilidade.
2. **Given** um produto ativo, **When** o Cliente o consulta pelo identificador, **Then** recebe
   200 com os mesmos campos da listagem.
3. **Given** um produto inativo ou inexistente, **When** o Cliente o consulta, **Then** recebe a
   mesma resposta 404 nos dois casos.
4. **Given** um produto da listagem, **When** o Cliente cria um pedido com o identificador dele,
   **Then** o pedido é criado com o preço exibido.
5. **Given** uma requisição sem token, **When** ela chega, **Then** a resposta é 401.

---

### User Story 2 - O Admin cadastra e altera produtos (Priority: P1)

O Admin cadastra um produto com SKU, nome, descrição opcional, preço e estoque inicial, e depois
altera o nome, a descrição e o preço. O SKU é único e não muda depois do cadastro. A mudança de
preço vale só para os pedidos criados depois dela.

**Why this priority**: É o critério central da issue: sem cadastro pela API, o catálogo só existe
em desenvolvimento.

**Independent Test**: Cadastrar um produto com o token do Admin, alterá-lo, criar um pedido antes
e outro depois da mudança de preço e conferir os preços de cada pedido; tentar as mesmas
operações com Cliente e OperadorLogistica e conferir 403.

**Acceptance Scenarios**:

1. **Given** o Admin autenticado, **When** ele cadastra um produto válido, **Then** recebe 201
   com o produto, ativo, e o endereço dele.
2. **Given** um SKU já cadastrado, inclusive com outra combinação de maiúsculas, **When** o
   Admin cadastra outro produto com ele, **Then** a resposta é 409 com o código de SKU duplicado.
3. **Given** um preço zero ou negativo, um SKU, nome ou descrição fora do tamanho permitido ou um
   estoque inicial negativo, **When** o Admin cadastra ou altera, **Then** a resposta é 400 com
   todos os erros.
4. **Given** um produto com pedidos já criados, **When** o Admin muda o preço, **Then** os
   pedidos antigos mantêm o preço e o total que tinham, e um pedido novo usa o preço novo.
5. **Given** o Cliente ou o OperadorLogistica, **When** tenta cadastrar ou alterar, **Then** a
   resposta é 403.
6. **Given** o Admin ou o OperadorLogistica, **When** lista ou consulta, **Then** vê também os
   inativos e a quantidade em estoque; o OperadorLogistica só lê.

---

### User Story 3 - O estoque é reposto e ajustado sem perder unidades (Priority: P1)

O Admin repõe ou ajusta o estoque de um produto informando a variação (positiva para reposição,
negativa para baixa) e o motivo. A operação convive com a reserva e a devolução feitas pelo
processo de workers ao mesmo tempo: nenhuma unidade é perdida nem criada, e o estoque nunca
fica negativo.

**Why this priority**: É o critério de aceite mais arriscado da issue: uma reposição concorrente
com a reserva não pode sobrescrever o que o outro gravou.

**Independent Test**: Repor o estoque de um produto enquanto vários pedidos são reservados ao
mesmo tempo e conferir que o estoque final é exatamente o inicial mais a reposição menos as
unidades reservadas.

**Acceptance Scenarios**:

1. **Given** um produto com 10 unidades, **When** o Admin repõe 5, **Then** o estoque passa a 15
   e a resposta traz a quantidade nova.
2. **Given** um produto com 10 unidades, **When** o Admin dá baixa de 3 com o motivo "Avaria",
   **Then** o estoque passa a 7.
3. **Given** um produto com 2 unidades, **When** o Admin tenta dar baixa de 3, **Then** a resposta
   é 422 com o código de estoque insuficiente, e o estoque não muda.
4. **Given** uma variação zero ou sem motivo, **When** o Admin ajusta, **Then** a resposta é 400.
5. **Given** um produto com 100 unidades, **When** o Admin repõe 50 enquanto o processo de workers
   reserva 30 pedidos de 1 unidade ao mesmo tempo, **Then** o estoque final é 120.
6. **Given** um ajuste que encontra o produto alterado por outra operação, **When** ele é gravado,
   **Then** é reavaliado sobre o estoque atual, como a reserva já faz, e só falha com 409 depois
   de esgotar as tentativas.

---

### User Story 4 - O Admin desativa e reativa produtos (Priority: P2)

O Admin desativa um produto que não deve mais ser vendido, sem apagá-lo: pedidos antigos
continuam válidos e consultáveis, e o produto deixa de aparecer para o Cliente e de poder ser
pedido. O Admin pode reativá-lo depois.

**Why this priority**: Fecha o ciclo de vida do produto; a desativação já é respeitada na
criação do pedido, falta poder fazê-la pela API.

**Independent Test**: Desativar um produto com pedidos existentes, conferir que os pedidos
continuam consultáveis, que o Cliente não o vê e que um pedido novo com ele é recusado;
reativar e conferir que volta.

**Acceptance Scenarios**:

1. **Given** um produto ativo, **When** o Admin o desativa, **Then** recebe 200 com o produto
   inativo; ele some da listagem e da consulta do Cliente.
2. **Given** um produto inativo, **When** um Cliente cria um pedido com ele, **Then** a resposta é
   422 com o código de produto indisponível, como já acontece hoje.
3. **Given** pedidos antigos com o produto, **When** ele é desativado, **Then** os pedidos
   continuam consultáveis, com os itens e os preços originais.
4. **Given** um produto inativo, **When** o Admin o reativa, **Then** ele volta a aparecer e a
   poder ser pedido.
5. **Given** um produto já ativo (ou já inativo), **When** o Admin o ativa (ou desativa) de novo,
   **Then** a resposta é 200 com o produto sem mudança (operação idempotente).
6. **Given** um pedido já criado e com estoque reservado, **When** o produto é desativado,
   **Then** o pedido segue o fluxo normal, e um cancelamento devolve o estoque ao produto
   inativo.

---

### Edge Cases

- Produto inexistente ou identificador malformado na consulta, na alteração, no ajuste ou na
  ativação: 404 e 400, respectivamente, como nas rotas de pedidos.
- SKU com espaços nas pontas: aparado antes da validação e da comparação de unicidade.
- Dois cadastros simultâneos com o mesmo SKU: um responde 201 e o outro 409, sem produto
  duplicado.
- Alteração que não muda nada (mesmo nome, descrição e preço): 200, sem erro.
- Ajuste de estoque com variação muito grande: limitado a 1.000.000 unidades por operação e a
  um estoque final de no máximo 1.000.000.000, para evitar estouro.
- Baixa de estoque que deixaria o estoque abaixo do que pedidos pendentes ainda vão reservar: é
  aceita; a reserva desses pedidos falha depois por estoque insuficiente, como já acontece
  (REQ-08).
- Mudança de preço durante a criação de um pedido: o pedido usa o preço lido na criação, sem
  erro.
- Desativação durante a criação de um pedido: se a criação leu o produto ativo, o pedido é
  criado; a desativação vale para os pedidos seguintes.
- Listagem do Cliente com catálogo vazio ou sem nenhum ativo: 200 com a lista vazia.
- Catálogo inicial de desenvolvimento: continua sendo criado só em Development e nos testes,
  sem sobrescrever produtos alterados pela API.

## Requirements *(mandatory)*

### Functional Requirements

**Consulta e listagem**

- **FR-001**: O contrato v1 MUST ganhar a consulta de um produto pelo identificador e a listagem
  de produtos, como acréscimos, sem alterar nada do que já existe (Princípio II).
- **FR-002**: A consulta e a listagem MUST exigir autenticação e estar disponíveis para Cliente,
  OperadorLogistica e Admin.
- **FR-003**: Para o Cliente, a listagem MUST trazer só os produtos ativos, e a consulta de um
  produto inativo MUST responder como a de um inexistente (404).
- **FR-004**: Para a operação (Admin e OperadorLogistica), a listagem e a consulta MUST incluir os
  inativos, com o filtro opcional por situação (ativos, inativos ou todos); o filtro por situação
  é exclusivo da operação, e o Cliente que o informa recebe 403, como o filtro por Cliente da
  listagem de pedidos (REQ-21).
- **FR-005**: Cada produto MUST trazer o identificador, o SKU, o nome, a descrição, o preço, a
  moeda e a disponibilidade (se há ao menos uma unidade em estoque). Para a operação (Admin e
  OperadorLogistica), MUST trazer também a quantidade em estoque, a situação (ativo ou inativo) e
  as datas de criação e de última alteração.
- **FR-006**: A listagem MUST ser ordenada pelo nome, com desempate pelo SKU, também quando há
  busca por texto (a busca só filtra; não há ordem por relevância), e paginada por
  cursor no mesmo modelo da listagem de pedidos (REQ-21): tamanho de 1 a 100, padrão 20,
  cursores da próxima e da anterior, sem total.
- **FR-007**: A listagem MUST aceitar uma busca opcional de texto (de 2 a 100 caracteres) sobre
  o nome, a descrição e o SKU, otimizada para o português do Brasil:
  - por palavras, sem diferenciar maiúsculas nem acentos ("cafe" acha "Café");
  - com as flexões de plural e singular ("camisetas" acha "Camiseta");
  - por início de palavra ("cami" acha "Camiseta");
  - com várias palavras, só os produtos que têm todas;
  - o SKU pelos trechos separados por hífen ou pelo começo do código ("001" e "cat" acham
    "CAT-001"; um trecho no meio de um código sem separador, como "250" em "CAF250", não acha).
  A busca se combina com os demais filtros e com o cursor. Uma busca sem nenhuma palavra útil
  (só pontuação, por exemplo) ou com mais de 10 palavras MUST dar 400.
- **FR-007a**: A busca por texto MAY refletir um cadastro ou uma alteração com atraso de até 10
  segundos; a consulta por identificador, a listagem sem busca e a regra de ativação (um produto
  desativado nunca aparece para o Cliente, nem na busca) MUST refletir a gravação na hora.

**Cadastro e alteração**

- **FR-008**: Só o Admin MUST poder cadastrar, alterar, ajustar o estoque, desativar e reativar
  produtos; os demais perfis recebem 403.
- **FR-009**: O cadastro MUST receber SKU (até 50 caracteres), nome (até 200), descrição opcional
  (até 1.000), preço e estoque inicial (de 0 a 1.000.000.000), e criar o produto ativo.
- **FR-010**: O SKU MUST ser único sem diferenciar maiúsculas; um SKU repetido MUST dar 409 com o
  código `PRODUTO_SKU_DUPLICADO`, também na disputa entre dois cadastros simultâneos.
- **FR-011**: O preço MUST ser maior que zero, com no máximo duas casas decimais e no máximo
  999.999,99, no cadastro e na alteração.
- **FR-012**: A alteração MUST mudar só o nome, a descrição e o preço; o SKU e o estoque não
  mudam por ela.
- **FR-013**: A mudança de preço MUST valer só para os pedidos criados depois dela; os pedidos
  existentes mantêm os preços e os totais.

**Estoque**

- **FR-014**: O ajuste de estoque MUST receber uma variação inteira diferente de zero, entre
  -1.000.000 e 1.000.000, e um motivo obrigatório de até 200 caracteres.
- **FR-015**: Um ajuste que deixaria o estoque negativo MUST dar 422 com o código
  `ESTOQUE_INSUFICIENTE` e não mudar nada; um que passaria de 1.000.000.000 MUST dar 422 com o
  código `ESTOQUE_ACIMA_DO_LIMITE`.
- **FR-016**: O ajuste MUST somar a variação ao estoque atual no momento da gravação, nunca
  gravar um valor calculado sobre uma leitura antiga: com o produto alterado por outra operação
  (uma reserva, uma devolução ou outro ajuste), MUST reavaliar sobre o estado atual, até 3 vezes;
  esgotadas, MUST dar 409 com o código `PRODUTO_ALTERADO_POR_OUTRA_OPERACAO`. A alteração de
  dados, a desativação e a reativação MUST seguir a mesma regra.
- **FR-017**: A resposta do ajuste MUST trazer o produto com a quantidade nova.

**Ativação**

- **FR-018**: A desativação MUST marcar o produto como inativo, sem apagá-lo; a reativação MUST
  marcá-lo como ativo. As duas MUST ser idempotentes.
- **FR-019**: Um produto inativo MUST continuar sendo recusado na criação de pedidos com o código
  `PRODUTO_INDISPONIVEL`, como hoje; pedidos existentes não mudam, e a devolução de estoque a um
  produto inativo continua funcionando.

**Validação, erros e contrato**

- **FR-020**: Dados inválidos MUST dar 400 em ProblemDetails com todos os erros e mensagens em
  português; os códigos novos MUST entrar no catálogo de códigos de erro do contrato.
- **FR-021**: O contrato v1 MUST continuar só com acréscimos (rotas, schemas e códigos de erro
  novos), com a conformidade do documento gerado e a verificação de compatibilidade verdes.

**Observabilidade e documentação**

- **FR-022**: Cadastro, alteração (com o preço anterior e o novo), ajuste de estoque (com a
  variação, o motivo, a quantidade nova e quem fez), desativação e reativação MUST gerar logs
  estruturados com IDs próprios, sem dados pessoais além do identificador do usuário. O motivo
  do ajuste vai ao log de propósito, porque o log é o histórico dos ajustes (clarify); ele é
  limitado a 200 caracteres.
- **FR-023**: O catálogo inicial de desenvolvimento (`CAT-001` a `CAT-010`) MUST continuar sendo
  criado só em Development e nos testes, apenas quando o produto ainda não existe, e MUST entrar
  na busca como qualquer produto; um produto do catálogo inicial alterado pela API não volta ao
  original na próxima partida.
- **FR-024**: Os documentos de fluxo da criação de pedido (fluxo 2) e da reserva de estoque
  (fluxo 4) MUST ser atualizados, e o catálogo MUST ganhar um documento de fluxo próprio, com a
  linha no índice.

### Key Entities

- **Produto** (existente): SKU único, nome, descrição, preço, quantidade em estoque, situação
  (ativo ou inativo) e datas. O SKU não muda depois do cadastro.
- **Ajuste de estoque**: uma variação com motivo, aplicada sobre o estoque atual; não é guardado
  como registro próprio nesta versão (fica no log).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um Cliente novo encontra um produto e cria um pedido com ele sem conhecer nenhum
  identificador de antemão.
- **SC-002**: Em 100% das execuções do teste de concorrência (reposição junto de 30 reservas
  simultâneas), o estoque final é exatamente o esperado.
- **SC-003**: Nenhum pedido existente muda de preço ou de total depois de uma mudança de preço
  ou de uma desativação.
- **SC-004**: Toda rota nova tem testes de sucesso, 401 e 403, e o contrato v1 aponta zero
  mudanças incompatíveis.
- **SC-005**: Com 10.000 produtos, qualquer página da listagem, com ou sem busca, responde em até
  1 segundo no ambiente de testes.
- **SC-006**: Um produto cadastrado ou renomeado é encontrado pela busca por texto em até 10
  segundos; um desativado deixa de aparecer para o Cliente na hora, também na busca.

## Assumptions

- A gestão do catálogo fica com o Admin; não é criado um perfil novo de catálogo nesta versão
  (criar perfil mexe na autenticação, que é escopo da #54).
- O OperadorLogistica consulta o catálogo como o Admin (inativos, quantidade e datas), por
  decisão do usuário (clarify), mas só lê: ele não gerencia produtos.
- O Cliente vê só a disponibilidade (há ou não estoque), não a quantidade, para não expor o nível
  de estoque.
- O ajuste é por variação (somar ou subtrair), não por valor absoluto: um valor absoluto
  calculado sobre uma leitura antiga apagaria reservas feitas no meio.
- O histórico de ajustes de estoque fica só no log nesta versão; uma tabela de movimentações
  pode vir depois.
- Nenhum evento de mensageria novo (`produto.*`) nesta versão: nada no sistema consome mudanças
  de catálogo hoje.
- O preço passa a ser obrigatoriamente maior que zero só no cadastro e na alteração pela API;
  produtos existentes não são revalidados.
- Fora do escopo, como na issue: categorias, imagens, variações de produto e importação em
  massa. Também fica fora apagar produtos.
