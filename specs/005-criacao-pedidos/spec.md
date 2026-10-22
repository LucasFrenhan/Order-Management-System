# Feature Specification: Criação, consulta e status de pedidos

**Feature Branch**: `feature/req-05-criacao-pedidos`

**Requisito**: REQ-05 (GitHub issue #6 — "[Core] API de Criação de Pedidos e Controle de Idempotência")

**Created**: 2026-10-22

**Status**: Approved

**Input**: User description: "REQ-05 (GitHub issue #6 — \"[Core] API de Criação de Pedidos e Controle de Idempotência\"): implementar o endpoint POST /api/v1/pedidos com validações de regra de negócio e suporte ao cabeçalho de idempotência. Critérios de aceite: endpoint restrito a usuários com a role Cliente; captura do cabeçalho X-Idempotency-Key — se a chave já existir no banco/cache, retorna a resposta do pedido previamente criado; validação de existência e preço dos produtos solicitados; pedido salvo no banco de dados com o status Pendente; o UserId do token JWT é associado automaticamente ao pedido. Contexto: o contrato v1 (REQ-01) já define a rota, CriarPedidoRequestDTO/PedidoResponseDTO, o número PED-AAAAMMDD-NNNNNN, os códigos IDEMPOTENCIA_CONTEUDO_DIVERGENTE, IDEMPOTENCIA_EM_PROCESSAMENTO, PRODUTO_INEXISTENTE e PRODUTO_INDISPONIVEL e a regra de idempotência (escopo usuário+chave, retenção mínima de 24 h, impressão do corpo normalizado; armazenamento definido no REQ-05). O REQ-04 entregou Pedido, Produto, histórico, concorrência otimista e catálogo inicial. Specs anteriores atribuem ao REQ-05 também as regras do GET /pedidos/{id} e do PATCH /pedidos/{id}/status e a remoção do ServicoNaoImplementado; a issue #11 (consulta e rastreio) também cita o GET. A publicação do evento PedidoCriado é do REQ-06. Branch feature/req-05-criacao-pedidos, empilhada sobre a do REQ-04 (PR #16 em revisão)."

## Contexto

O REQ-01 publicou o contrato v1 das três operações de pedido: criar, consultar e mudar o
status. O contrato fixa formatos, perfis, códigos de erro e a regra de idempotência. O
REQ-03 colocou a autenticação e as regras de acesso por perfil em funcionamento. O REQ-04
criou o armazenamento de produtos, pedidos e histórico de status. Ainda assim, as três
operações respondem "não implementada" (501) depois de passar pela autorização.

Este requisito dá comportamento às três operações, sem mudar o contrato v1:

- **Criação**: um Cliente cria um pedido com os produtos e as quantidades. O sistema
  define os preços pelo catálogo, o dono pelo token e o status `Pendente`. Uma repetição
  da mesma requisição nunca cria um segundo pedido.
- **Consulta**: o dono vê o próprio pedido; Admin e OperadorLogistica veem qualquer
  pedido.
- **Mudança de status**: as transições seguem a tabela do contrato v1, e cada mudança fica
  no histórico.

A publicação de eventos de pedido (por exemplo, "pedido criado") é do REQ-06.

## Clarifications

### Session 2026-10-22

- Q: Quais operações de pedido o REQ-05 implementa? → A: As três do contrato v1: criação,
  consulta e mudança de status. As respostas 501 deixam de existir. Para a issue de
  consulta e rastreio (gh #11), ficam o rastreio e o histórico na consulta.
- Q: Ao criar o pedido, o que torna um produto "indisponível" (422 `PRODUTO_INDISPONIVEL`)?
  → A: Produto inativo ou com estoque menor que a quantidade pedida, lido no momento da
  criação, sem reservar estoque. O worker de estoque (issue gh #8) continua sendo a
  garantia final contra disputas.
- Q: Na repetição idempotente (mesma chave e mesmo conteúdo), a resposta traz o pedido como
  era na criação ou como está agora? → A: A resposta original: o mesmo corpo da primeira
  resposta (201, status `Pendente`), guardado com a chave. O estado atual fica com a
  consulta.
- Q: Uma tentativa que falha com 422 (produto inexistente ou indisponível) deve reservar a
  chave de idempotência? → A: Não. Só uma criação bem-sucedida reserva a chave. Depois de
  um 422 (ou 400, ou 500), a mesma chave pode ser reenviada, inclusive com o conteúdo
  corrigido.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Criar um pedido (Priority: P1)

Um Cliente autenticado informa os produtos e as quantidades e recebe o pedido criado, com
número, preços do catálogo, total em reais e status `Pendente`. Ele não informa preço,
total nem dono: o sistema define esses dados.

**Why this priority**: É a operação central do sistema e o ponto de partida de todo o
fluxo de estoque, pagamento e entrega.

**Independent Test**: Com um token de Cliente, criar um pedido com dois produtos do
catálogo inicial e conferir a resposta 201, o `Location` e os campos do pedido. Repetir com
um produto inexistente e com um produto sem estoque e conferir os 422.

**Acceptance Scenarios**:

1. **Given** um Cliente autenticado e produtos ativos com estoque suficiente, **When** ele
   cria um pedido com uma chave de idempotência nova, **Then** recebe 201 com
   `PedidoResponseDTO` e `Location` apontando para o pedido. O pedido tem:
   - status `Pendente`;
   - `clienteId` igual ao usuário do token;
   - número no padrão `PED-AAAAMMDD-NNNNNN`, com a data de criação em UTC;
   - preço unitário e nome de cada item iguais aos do catálogo naquele momento;
   - subtotal de cada item igual a preço × quantidade, e `valorTotal` igual à soma dos
     subtotais;
   - moeda `BRL`.
2. **Given** o pedido criado, **When** ele é lido depois (consulta ou banco de dados),
   **Then** está guardado com os mesmos dados e com a entrada de criação no histórico.
3. **Given** um item cujo produto não existe, **When** o Cliente cria o pedido, **Then**
   recebe 422 com o código `PRODUTO_INEXISTENTE`, e nenhum pedido é criado.
4. **Given** um item cujo produto está inativo, ou cujo estoque é menor que a quantidade
   pedida, **When** o Cliente cria o pedido, **Then** recebe 422 com o código
   `PRODUTO_INDISPONIVEL`, e nenhum pedido é criado.
5. **Given** um pedido com mais de um item com problema, **When** o Cliente cria o pedido,
   **Then** recebe um único 422. O `detail` cita todos os produtos com problema, e o código
   é `PRODUTO_INEXISTENTE` se algum produto não existir; senão, é `PRODUTO_INDISPONIVEL`.
6. **Given** um token de Admin ou OperadorLogistica, **When** ele tenta criar um pedido,
   **Then** recebe 403 (comportamento já existente, mantido).
7. **Given** uma requisição fora do formato do contrato (sem itens, quantidade 0, produto
   repetido, cabeçalho de idempotência ausente ou inválido), **When** o Cliente cria o
   pedido, **Then** recebe 400 (comportamento já existente, mantido), e nenhum pedido é
   criado.

---

### User Story 2 - Repetir a criação sem duplicar o pedido (Priority: P1)

Depois de uma falha de rede, o Cliente reenvia a mesma requisição com a mesma chave de
idempotência. Ele recebe o mesmo pedido, e nenhum pedido novo é criado. Se usar a mesma
chave para um pedido diferente, recebe um erro claro.

**Why this priority**: A issue #6 exige isso de forma explícita. Sem idempotência, uma
nova tentativa pode cobrar e entregar duas vezes.

**Independent Test**: Criar um pedido, repetir a mesma requisição com a mesma chave e
conferir que a resposta é igual e que só existe um pedido. Repetir com outro conteúdo e
conferir o 409. Disparar várias requisições iguais ao mesmo tempo e conferir que só um
pedido existe.

**Acceptance Scenarios**:

1. **Given** um pedido criado com a chave K, **When** o mesmo Cliente reenvia a mesma
   requisição com K, **Then** recebe 201 com o mesmo corpo e o mesmo `Location` da
   resposta original, e o total de pedidos não muda.
2. **Given** um pedido criado com K, **When** o mesmo Cliente reenvia com K os mesmos
   itens em outra ordem, **Then** a requisição é considerada a mesma (cenário 1).
3. **Given** um pedido criado com K, **When** o mesmo Cliente envia com K um conteúdo
   diferente (outro produto ou outra quantidade), **Then** recebe 409 com o código
   `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`, e nenhum pedido é criado.
4. **Given** uma requisição com K ainda em processamento, **When** o mesmo Cliente envia
   outra com K, **Then** recebe 409 com o código `IDEMPOTENCIA_EM_PROCESSAMENTO`, ou a
   resposta do pedido, se a primeira já terminou.
5. **Given** a chave K usada por um Cliente, **When** outro Cliente cria um pedido com a
   mesma K, **Then** o pedido dele é criado normalmente: a chave vale por usuário.
6. **Given** uma tentativa com K que terminou em 422, **When** o Cliente corrige o pedido
   e reenvia com a mesma K, **Then** o pedido é criado. Só uma criação bem-sucedida
   reserva a chave.
7. **Given** um pedido criado com K há mais de 24 horas, **When** o Cliente reenvia com K,
   **Then** o comportamento pode ser de chave nova. A garantia é de no mínimo 24 horas.

---

### User Story 3 - Consultar um pedido (Priority: P2)

O Cliente consulta um pedido seu pelo identificador. Admin e OperadorLogistica consultam
qualquer pedido. Um Cliente nunca descobre que o pedido de outra pessoa existe.

**Why this priority**: Permite acompanhar o pedido, mas não bloqueia a criação.

**Independent Test**: Criar um pedido com o Cliente A. Consultá-lo com A (200), com o
Cliente B (404), com Admin e com OperadorLogistica (200) e com um identificador
inexistente (404).

**Acceptance Scenarios**:

1. **Given** um pedido do Cliente A, **When** A o consulta, **Then** recebe 200 com o
   `PedidoResponseDTO` atual, inclusive status e `atualizadoEm` depois de mudanças.
2. **Given** um pedido do Cliente A, **When** o Cliente B o consulta, **Then** recebe 404,
   com a mesma resposta de um pedido inexistente.
3. **Given** qualquer pedido, **When** um Admin ou OperadorLogistica o consulta, **Then**
   recebe 200.
4. **Given** um identificador sem pedido, **When** qualquer perfil o consulta, **Then**
   recebe 404. Um identificador mal formado continua recebendo 400.

---

### User Story 4 - Mudar o status de um pedido (Priority: P2)

Admin e OperadorLogistica avançam o pedido no ciclo logístico ou o cancelam. O Cliente
pode cancelar o próprio pedido enquanto ele está `Pendente`. Toda mudança segue a tabela
do contrato v1 e fica registrada no histórico, com o autor e o motivo.

**Why this priority**: Dá suporte à operação logística, mas depende de o pedido existir.

**Independent Test**: Criar um pedido e percorrer Pendente → Processando →
ProntoParaEnvio → Despachado com OperadorLogistica. Tentar uma transição inválida e
conferir o 409. Cancelar com o Cliente dono um pedido Pendente (200) e um pedido
Processando (403).

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente`, **When** Admin ou OperadorLogistica muda para
   `Processando`, **Then** recebe 200 com o pedido atualizado (status e `atualizadoEm`).
   O histórico ganha uma entrada com o status anterior e o novo, o autor e o instante.
2. **Given** a tabela de transições do contrato v1, **When** Admin ou OperadorLogistica
   pede cada transição permitida (Processando → ProntoParaEnvio, ProntoParaEnvio →
   Despachado e Pendente/Processando/ProntoParaEnvio → Cancelado com motivo), **Then**
   cada uma é aceita com 200.
3. **Given** uma transição fora da tabela (por exemplo, Despachado → Pendente, qualquer
   saída de um estado final ou para o mesmo status), **When** ela é pedida, **Then** a
   resposta é 409 com o código `PEDIDO_TRANSICAO_INVALIDA`, e o `detail` cita o status
   atual e o solicitado. O pedido não muda.
4. **Given** um pedido `Pendente` do Cliente A, **When** A pede `Cancelado` com motivo,
   **Then** recebe 200 com o pedido cancelado, e o histórico guarda A como autor e o
   motivo.
5. **Given** um pedido do Cliente A em `Processando`, **When** A pede `Cancelado`,
   **Then** recebe 403. O mesmo vale para qualquer outra transição pedida por um Cliente
   no próprio pedido.
6. **Given** um pedido do Cliente A, **When** o Cliente B pede qualquer transição, **Then**
   recebe 404.
7. **Given** um identificador sem pedido, **When** qualquer perfil pede uma transição,
   **Then** recebe 404.
8. **Given** um pedido `Pendente`, **When** duas mudanças de status chegam ao mesmo tempo
   (por exemplo, o cancelamento do Cliente e o avanço do operador), **Then** só uma é
   aplicada a partir de `Pendente`. A outra é avaliada de novo sobre o status resultante:
   é aplicada se a transição ainda for permitida, ou recebe 409/403 conforme a tabela.
   Nenhuma mudança se perde em silêncio.

---

### Edge Cases

- Duas criações simultâneas com a mesma chave e o mesmo conteúdo: exatamente um pedido é
  criado. As outras recebem a resposta do pedido criado ou 409
  `IDEMPOTENCIA_EM_PROCESSAMENTO`.
- Duas criações simultâneas com chaves diferentes: dois pedidos, com números diferentes.
- Duas criações no mesmo dia: números diferentes, com a mesma data no padrão. A virada do
  dia é em UTC.
- O preço de um produto muda depois da criação: o pedido mantém o preço da criação (REQ-04).
  Uma repetição idempotente devolve a resposta original.
- O pedido muda de status depois da criação: a repetição idempotente devolve a resposta
  original da criação (status `Pendente`). Para o estado atual, o Cliente usa a consulta.
- Estoque suficiente na criação e esgotado logo depois: a criação não reserva estoque. A
  reserva e a falta de estoque são tratadas pelo worker de estoque (issue gh #8).
- Mudança para `PagamentoRejeitado` pela API: 400, como no contrato v1. Esse status só é
  definido pelo fluxo de pagamento.
- `Cancelado` sem motivo: 400, como no contrato v1.
- Banco de dados fora do ar durante qualquer operação: 500 genérico em ProblemDetails, sem
  detalhes internos. Nenhum pedido fica gravado pela metade, e a chave de idempotência não
  fica reservada.
- Cliente cujo token tem um identificador de usuário que não existe mais: a criação é
  recusada com erro genérico, e nenhum pedido é criado.

## Requirements *(mandatory)*

### Functional Requirements

**Criação**

- **FR-001**: A criação MUST gravar o pedido com status `Pendente`, dono igual ao usuário
  do token e moeda `BRL`, e responder 201 com `PedidoResponseDTO` e `Location`
  (`/api/v1/pedidos/{id}`), conforme o contrato v1.
- **FR-002**: Para cada item, o sistema MUST usar o nome e o preço do produto no catálogo
  no momento da criação. O subtotal é preço × quantidade, e o total é a soma dos
  subtotais. O Cliente não informa nenhum desses valores.
- **FR-003**: Um item com produto inexistente MUST resultar em 422 `PRODUTO_INEXISTENTE`.
  Um item com produto inativo, ou com estoque menor que a quantidade pedida, MUST resultar
  em 422 `PRODUTO_INDISPONIVEL`. Com vários itens com problema, a resposta é única: o
  código é `PRODUTO_INEXISTENTE` se houver algum produto inexistente, e o `detail` cita os
  identificadores de todos os produtos com problema. Nenhum pedido é criado.
- **FR-004**: A criação MUST NOT alterar o estoque. A reserva de estoque é do worker de
  estoque (issue gh #8).
- **FR-005**: Cada pedido MUST receber um número único no padrão `PED-AAAAMMDD-NNNNNN`, em
  que `AAAAMMDD` é a data de criação em UTC e `NNNNNN` distingue os pedidos do mesmo dia.
  Criações simultâneas MUST NOT gerar o mesmo número nem falhar por colisão de número.
- **FR-006**: O pedido, seus itens e a entrada de criação no histórico MUST ser gravados
  numa operação única, junto com o registro da chave de idempotência (FR-009).

**Idempotência**

- **FR-007**: A chave de idempotência MUST valer por par (usuário, chave). A mesma chave
  usada por outro usuário é independente.
- **FR-008**: A impressão do conteúdo MUST ser calculada sobre o corpo normalizado, com os
  itens ordenados por produto. Dois corpos com os mesmos pares (produto, quantidade) em
  ordens diferentes são o mesmo conteúdo.
- **FR-009**: Uma criação bem-sucedida MUST registrar a chave, a impressão do conteúdo e a
  resposta enviada. A chave MUST ficar disponível por no mínimo 24 horas a partir da
  primeira requisição.
- **FR-010**: Uma requisição com chave registrada e mesma impressão MUST receber 201 com o
  mesmo corpo e o mesmo `Location` da resposta original, sem criar pedido nem alterar
  dados.
- **FR-011**: Uma requisição com chave registrada e impressão diferente MUST receber 409
  `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`.
- **FR-012**: Enquanto uma requisição com a chave K está em processamento, outra
  requisição do mesmo usuário com K MUST receber 409 `IDEMPOTENCIA_EM_PROCESSAMENTO`, ou a
  resposta registrada, se a primeira terminou com sucesso. Em nenhuma hipótese dois
  pedidos são criados para o mesmo par (usuário, chave).
- **FR-013**: Uma tentativa que termina em erro (400, 422 ou 500) MUST NOT reservar a
  chave: a mesma chave pode ser usada de novo.

**Consulta**

- **FR-014**: A consulta MUST devolver o estado atual do pedido em `PedidoResponseDTO`.
  O Cliente só vê os próprios pedidos; Admin e OperadorLogistica veem qualquer um.
- **FR-015**: Pedido inexistente e pedido de outro Cliente MUST receber a mesma resposta
  404, com o mesmo título e a mesma mensagem.

**Mudança de status**

- **FR-016**: A mudança de status MUST seguir a tabela de transições do contrato v1:

  | De | Para | Perfis |
  |----|------|--------|
  | Pendente | Processando | Admin, OperadorLogistica |
  | Processando | ProntoParaEnvio | Admin, OperadorLogistica |
  | ProntoParaEnvio | Despachado | Admin, OperadorLogistica |
  | Pendente, Processando, ProntoParaEnvio | Cancelado | Admin, OperadorLogistica |
  | Pendente | Cancelado | Cliente dono do pedido |

  `Despachado`, `Cancelado` e `PagamentoRejeitado` são finais. Qualquer outra transição,
  inclusive para o mesmo status, MUST receber 409 `PEDIDO_TRANSICAO_INVALIDA`, com o
  status atual e o solicitado no `detail`.
- **FR-017**: Um Cliente MUST receber 404 em pedido de outra pessoa e 403 em qualquer
  transição do próprio pedido que não seja Pendente → Cancelado.
- **FR-018**: Toda mudança aceita MUST atualizar o status e `atualizadoEm` e gravar a
  entrada de histórico (status anterior e novo, autor, instante e motivo) numa operação
  única, e responder 200 com o pedido atualizado.
- **FR-019**: Mudanças simultâneas no mesmo pedido MUST NOT se sobrescrever. Quando uma
  mudança encontra o pedido alterado desde a leitura, ela MUST ser avaliada de novo sobre
  o estado atual, até 3 vezes, e só então aplicada ou recusada conforme a tabela (FR-016).
  Se ainda houver conflito depois disso, a resposta é 409 `PEDIDO_TRANSICAO_INVALIDA`.

**Observabilidade**

- **FR-020**: A API MUST registrar em log a criação do pedido (identificador, número,
  quantidade de itens e valor total), a repetição idempotente e cada mudança de status
  (identificador, status anterior e novo, papel do autor: operação ou Cliente, o papel que decidiu a regra). Os logs MUST NOT conter e-mail,
  token nem o corpo da requisição.

**Contrato e transição**

- **FR-021**: O contrato v1 MUST permanecer inalterado: rotas, formatos, respostas e
  códigos de erro. O teste de conformidade do REQ-01 MUST continuar passando.
- **FR-022**: As três operações de pedido MUST deixar de responder 501. A implementação
  provisória que respondia 501 MUST ser removida.
- **FR-023**: As regras de acesso por perfil já existentes (401 e 403 na autorização) MUST
  continuar iguais.

### Key Entities *(include if feature involves data)*

- **Pedido, item, histórico de status e produto**: já existem (REQ-04). Este requisito os
  usa sem mudar a estrutura.
- **Registro de idempotência**: associa o par (usuário, chave) à impressão do conteúdo, ao
  pedido criado e à resposta enviada, com a data da primeira requisição. Existe por no
  mínimo 24 horas.
- **Número do pedido**: código legível `PED-AAAAMMDD-NNNNNN`, único, gerado na criação.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% das repetições com a mesma chave e o mesmo conteúdo devolvem a resposta
  original sem criar pedido.
- **SC-002**: Em um teste com 20 requisições simultâneas com a mesma chave e o mesmo
  conteúdo, repetido 10 vezes, existe exatamente 1 pedido em cada repetição. Nenhuma
  requisição recebe 500.
- **SC-003**: Em um teste com 50 criações simultâneas com chaves diferentes, os 50 pedidos
  têm números distintos e válidos.
- **SC-004**: 100% das linhas da tabela de transições, e as transições fora dela, têm teste
  com o perfil autorizado, com um perfil não autorizado e com o Cliente.
- **SC-005**: 95% das criações de pedido com até 10 itens concluem em menos de 500 ms no
  ambiente local.
- **SC-006**: A suíte de testes continua 100% verde, e o teste de conformidade do contrato
  v1 passa sem alteração no contrato.

## Assumptions

- A criação não reserva estoque (FR-004). A verificação de estoque na criação é só um
  filtro inicial: a garantia vem do worker de estoque, com concorrência otimista (REQ-04,
  issue gh #8).
- A repetição idempotente devolve a resposta original, e não o estado atual do pedido. É o
  que o contrato v1 chama de "mesma resposta".
- A retenção mínima da chave é de 24 horas. A limpeza de chaves antigas, se houver, é
  decisão do plano e não pode apagar chaves com menos de 24 horas.
- A publicação do evento de pedido criado e o outbox (Princípio IV) são do REQ-06. Este
  requisito não publica mensagens.
- Rastreio, histórico de status na consulta e listagem de pedidos ficam fora do escopo:
  não estão no contrato v1. O rastreio e o histórico ficam para a issue gh #11.
- Cadastro de produtos continua fora do escopo. Em Development e nos testes, o catálogo
  inicial do REQ-04 fornece os produtos.
- O ambiente local do REQ-02 (banco de dados no ar) é pré-requisito. A suíte de testes
  continua rodando sem Docker; os testes que precisam de banco real são ignorados quando o
  Docker não está disponível, como nos REQs anteriores.
