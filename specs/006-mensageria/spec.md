# Feature Specification: Mensageria, publicação de eventos e DLQ

**Feature Branch**: `feature/req-06-mensageria`

**Requisito**: REQ-06 (GitHub issue #7 — "[Messaging] Configuração do RabbitMQ, Publicação de Eventos e DLQ")

**Created**: 2026-10-22

**Status**: Approved

**Input**: User description: "REQ-06 (GitHub issue #7): configurar o barramento de mensagens para publicar os eventos de mudança de estado do pedido, com políticas de retry e Dead Letter Queue. Critérios da issue: barramento apontando para o RabbitMQ do Docker; contratos de eventos PedidoCriado, EstoqueReservado, PagamentoAprovado, PagamentoRecusado; a API dispara PedidoCriado logo após salvar o pedido; retry no consumidor (ex.: 3 tentativas com intervalo exponencial); mensagens com erro irrecuperável vão para a DLQ. Decisões do usuário: RabbitMQ.Client nativo em vez de MassTransit (constituição); consumidores num projeto Worker separado; publicação por outbox transacional; testes de serialização e conformidade de schema dos contratos (constituição V)."

## Contexto

O REQ-05 cria pedidos, mas ninguém mais fica sabendo disso. Os próximos requisitos dependem
de eventos para funcionar de forma assíncrona:

- o worker de estoque reage a um pedido criado (issue gh #8);
- o de pagamento reage ao estoque reservado (#9);
- o de entrega reage ao pagamento aprovado (#10).

O Princípio IV da constituição exige que esses efeitos entre domínios trafeguem por
mensageria, de forma consistente com a transação do banco.

Este requisito entrega a base de mensageria:

- **Contratos**: os contratos versionados dos quatro eventos citados na issue.
- **Publicação confiável**: o evento entra na mesma gravação do pedido e é enviado depois,
  sem se perder se o broker estiver fora.
- **Primeiro evento**: a publicação de "pedido criado" pela criação de pedidos.
- **Processo de workers**: um processo separado da API, que vai hospedar os workers.
- **Base de consumo**: entrega pelo menos uma vez, descarte de duplicados, novas
  tentativas com intervalo crescente e fila de mensagens mortas (DLQ) para o que não pode
  ser processado.

A issue cita MassTransit, mas a constituição proíbe frameworks de abstração de mensageria.
A base usa o cliente nativo do broker.

Os consumidores de negócio (estoque, pagamento e entrega) ficam para as issues #8 a #10. A
base deste requisito é exercitada por um consumidor de verificação usado nos testes.

## Clarifications

### Session 2026-10-22

- Q: As mudanças de status feitas pelo PATCH (REQ-05) também devem gerar eventos? → A:
  Não. Neste requisito, só os quatro contratos da issue, e só "pedido criado" é publicado.
  Um evento de cancelamento entra com contrato próprio quando o requisito de estoque ou de
  compensação (issues #8 e #9) precisar dele.
- Q: Como interpretar as "3 tentativas" da issue antes de a mensagem ir para a DLQ? → A: A
  primeira entrega mais até 3 novas tentativas (cerca de 1 s, 2 s e 4 s): 4 execuções no
  total, e a mensagem chega à DLQ uns 7 s depois da primeira falha.
- Q: Quem publica os eventos pendentes do outbox: o processo de workers ou a própria API?
  → A: O processo de workers. A API só grava o evento no banco, junto com o pedido, e não
  publica nada no broker. Com o Worker parado, os eventos esperam como pendentes.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são os workers dos próximos requisitos e quem opera o
sistema. O valor está em eventos que chegam, chegam uma única vez para o efeito de negócio
e, quando não podem ser processados, ficam guardados para análise em vez de sumir.

### User Story 1 - Pedido criado vira evento, sem perda (Priority: P1)

Quando um pedido é criado, o evento "pedido criado" é publicado no barramento com os dados
do pedido. Se o broker estiver fora no momento, o pedido é criado do mesmo jeito, e o
evento é publicado quando o broker voltar. Se a criação falhar, nenhum evento sai.

**Why this priority**: É o critério central da issue e o gatilho de todo o fluxo
assíncrono (estoque → pagamento → entrega).

**Independent Test**: Criar um pedido e conferir que uma fila de teste ligada ao barramento
recebe "pedido criado" com os dados do pedido. Repetir com o broker parado: a criação
responde normalmente, e o evento chega depois que o broker volta.

**Acceptance Scenarios**:

1. **Given** o broker no ar, **When** um Cliente cria um pedido, **Then** em até 5
   segundos o evento "pedido criado" é publicado com:
   - identificador do evento, versão do contrato e instante;
   - identificador, número e dono do pedido;
   - itens (produto, quantidade, preço unitário);
   - valor total e moeda.
2. **Given** o broker fora do ar, **When** um Cliente cria um pedido, **Then** a criação
   responde 201 como sempre, e o evento fica pendente. **When** o broker volta, **Then** o
   evento é publicado em até 30 segundos.
3. **Given** uma criação que falha (422, 409 ou erro do banco), **When** ela termina,
   **Then** nenhum evento é publicado.
4. **Given** uma repetição idempotente da criação (REQ-05), **When** ela é respondida,
   **Then** nenhum evento novo é publicado. O evento é do pedido, não da requisição.
5. **Given** o processo que publica parado e reiniciado no meio do envio, **When** ele
   volta, **Then** todo evento pendente é publicado. Um mesmo evento pode chegar mais de
   uma vez, sempre com o mesmo identificador.

---

### User Story 2 - Contratos de eventos versionados e verificados (Priority: P1)

Os quatro eventos da issue têm contrato público, versionado e documentado:
- "pedido criado";
- "estoque reservado";
- "pagamento aprovado";
- "pagamento recusado".

Testes automáticos garantem que o que é publicado segue o contrato e que uma mensagem fora
do contrato é detectada.

**Why this priority**: Os workers das issues #8 a #10 são escritos contra esses contratos.
A constituição (Princípios II e V) exige contratos versionados com testes de serialização e
de conformidade.

**Independent Test**: Serializar cada evento e validar contra o schema publicado. Validar
exemplos inválidos (campo obrigatório ausente, tipo errado) e ver a recusa.

**Acceptance Scenarios**:

1. **Given** cada um dos quatro eventos, **When** ele é serializado, **Then** o resultado
   segue o schema publicado da sua versão, com nomes de campos em português e camelCase,
   como o contrato HTTP.
2. **Given** uma mensagem sem campo obrigatório, com tipo errado ou de versão desconhecida,
   **When** ela é validada, **Then** é recusada.
3. **Given** um evento serializado, **When** ele é lido de volta, **Then** os valores são os
   mesmos (ida e volta sem perda, inclusive datas em UTC e valores com 2 casas).

---

### User Story 3 - Consumo com novas tentativas e fila de mensagens mortas (Priority: P1)

Um worker que recebe um evento e falha por um problema passageiro tenta de novo, com
intervalo crescente. Se continuar falhando, a mensagem vai para a fila de mensagens mortas
(DLQ) com o motivo. Uma mensagem que nunca vai dar certo, por estar fora do contrato, vai
direto para a DLQ, sem novas tentativas. O consumo também não repete o efeito de um evento
que chega duas vezes.

**Why this priority**: É a outra metade da issue. Sem isso, os workers das issues #8 a #10
reimplementariam a mesma lógica, ou perderiam mensagens.

**Independent Test**: Com um consumidor de verificação:
- fazer falhar duas vezes e conferir o sucesso na terceira tentativa, com intervalo
  crescente;
- fazer falhar sempre e conferir a mensagem na DLQ depois das tentativas;
- enviar uma mensagem mal formada e conferir que ela vai direto para a DLQ;
- entregar o mesmo evento duas vezes e conferir que o efeito acontece uma vez.

**Acceptance Scenarios**:

1. **Given** um consumidor que falha temporariamente, **When** ele recebe um evento,
   **Then** a mensagem é reprocessada até 3 vezes, com intervalos crescentes (1 s, 2 s e
   4 s, aproximadamente), e conta como processada se uma das tentativas der certo.
2. **Given** um consumidor que falha em todas as tentativas, **When** a última termina,
   **Then** a mensagem vai para a DLQ da sua fila, com o motivo da última falha e o número
   de tentativas, e sai da fila principal.
3. **Given** uma mensagem que não pode ser lida (corpo inválido, fora do schema ou versão
   desconhecida), **When** ela chega, **Then** vai direto para a DLQ, sem novas tentativas,
   com o motivo.
4. **Given** um evento já processado com sucesso por um consumidor, **When** o mesmo evento
   (mesmo identificador) chega de novo a ele, **Then** é confirmado sem repetir o efeito.
5. **Given** o worker parado enquanto há mensagens na fila, **When** ele volta, **Then** as
   mensagens são processadas. Nada se perde por o consumidor estar fora.
6. **Given** uma mensagem na DLQ, **When** alguém de operação consulta a fila pelo painel do
   broker, **Then** consegue ver o evento original e o motivo.

---

### User Story 4 - Processo de workers separado da API (Priority: P2)

Os consumidores rodam num processo próprio, que sobe e cai sem afetar a API. Ele usa a
mesma configuração de banco e broker, pode ser verificado como pronto e entra no ambiente
de demonstração.

**Why this priority**: Prepara o terreno para as issues #8 a #10. Sem consumidores de
negócio ainda, o processo só hospeda a base e a publicação dos eventos pendentes.

**Independent Test**: Subir o processo de workers localmente e na demonstração e conferir
que ele fica pronto, conectado ao broker, e que publica eventos pendentes. Parar o processo
e conferir que a API continua criando pedidos normalmente.

**Acceptance Scenarios**:

1. **Given** banco e broker no ar, **When** o processo de workers inicia, **Then** ele fica
   pronto, com a topologia de filas e trocas criada, e passa a publicar os eventos
   pendentes.
2. **Given** o processo de workers parado, **When** um Cliente cria pedidos, **Then** a API
   funciona normalmente. Os eventos ficam pendentes e são publicados quando o processo
   voltar.
3. **Given** a configuração de broker ausente ou inválida, **When** o processo de workers
   inicia, **Then** ele não sobe, e a mensagem nomeia a chave com problema sem mostrar
   valores, como a API já faz.
4. **Given** o ambiente de demonstração, **When** ele sobe, **Then** o processo de workers
   também sobe e fica saudável, e a verificação da demonstração no CI confere isso.

---

### Edge Cases

- Dois pedidos criados ao mesmo tempo: dois eventos, cada um com o seu identificador. A
  ordem de publicação segue a ordem de gravação.
- Evento pendente que falha na publicação por erro do broker (sem confirmação): continua
  pendente e é tentado de novo. Nunca é marcado como enviado sem a confirmação do broker.
- Broker fora do ar por muito tempo: os eventos se acumulam como pendentes, sem limite
  neste requisito. O tempo de espera entre tentativas de publicação cresce até um teto,
  para não sobrecarregar o broker quando ele voltar.
- Mensagem publicada sem nenhuma fila ligada: o barramento a descarta (só os consumidores
  que existem a recebem). Os contratos não exigem que haja consumidor.
- Mensagem que chega à DLQ e é reenviada à mão pela operação: volta a ser processada como
  um evento novo para as tentativas, e o descarte de duplicados evita efeito repetido se
  ela já tinha sido processada.
- Consumidor que falha depois de produzir o efeito, mas antes de confirmar: o evento pode
  ser reentregue. O descarte de duplicados só registra o evento como processado junto com
  o efeito; por isso a base exige que o consumidor grave as duas coisas juntas.
- Credenciais do broker inválidas: o processo de workers não fica pronto, e a prontidão
  dele mostra o broker como indisponível, sem revelar credenciais.

## Requirements *(mandatory)*

### Functional Requirements

**Contratos de eventos**

- **FR-001**: O sistema MUST definir contratos versionados (versão 1) para os eventos
  "pedido criado", "estoque reservado", "pagamento aprovado" e "pagamento recusado". Todos
  compartilham um envelope com:
  - identificador único do evento;
  - tipo (nome no passado, como `pedido.criado`);
  - versão do contrato;
  - instante de ocorrência em UTC;
  - identificador de correlação, que é o identificador do pedido.
- **FR-002**: O conteúdo de cada evento MUST conter:
  - **pedido criado**: identificador, número e dono do pedido; itens (produto, quantidade,
    preço unitário); valor total e moeda.
  - **estoque reservado**: identificador do pedido; itens reservados (produto e
    quantidade).
  - **pagamento aprovado**: identificador do pedido; valor; identificador da transação
    externa.
  - **pagamento recusado**: identificador do pedido; valor; motivo da recusa.
- **FR-003**: Os contratos MUST ser publicados como schemas no repositório, com campos em
  português e camelCase, valores monetários com 2 casas e datas em UTC, como o contrato
  HTTP v1. Mudanças seguem o Princípio II: só acréscimos compatíveis na mesma versão, ou
  versão nova.
- **FR-004**: Testes automatizados MUST verificar, para cada evento:
  - a serialização conforme o schema;
  - a ida e volta sem perda;
  - a recusa de mensagens fora do schema.

**Publicação confiável (outbox)**

- **FR-005**: Ao criar um pedido, o evento "pedido criado" MUST ser registrado como
  pendente na mesma gravação do pedido. Se a gravação falhar, nenhum evento é registrado;
  se der certo, o evento existe.
- **FR-006**: O processo de workers MUST publicar os eventos pendentes em ordem de registro
  e só marcá-los como enviados depois da confirmação do broker. A API MUST NOT publicar
  no broker: ela só registra o evento (clarify). Com o broker no
  ar, o evento MUST ser publicado em até 5 segundos após a criação do pedido.
- **FR-007**: Com o broker fora, os eventos MUST continuar pendentes, sem afetar a criação
  de pedidos, e MUST ser publicados em até 30 segundos depois que o broker voltar. As
  tentativas de publicação usam espera crescente, com teto.
- **FR-008**: A entrega MUST ser "pelo menos uma vez". Um mesmo evento pode ser publicado
  mais de uma vez em caso de falha, sempre com o mesmo identificador.
- **FR-009**: Repetições idempotentes da criação (REQ-05) MUST NOT gerar evento novo.

**Topologia do barramento**

- **FR-010**: Os eventos MUST ser publicados numa troca de eventos do domínio, com o tipo
  do evento como chave de roteamento. Cada consumidor tem a sua fila, ligada aos tipos que
  consome, e a sua DLQ.
- **FR-011**: Filas, trocas e mensagens MUST ser duráveis: sobrevivem ao reinício do
  broker.

**Base de consumo**

- **FR-012**: A base de consumo MUST reprocessar uma mensagem que falhou até 3 vezes, além
  da primeira tentativa, com intervalos crescentes de cerca de 1 s, 2 s e 4 s. Durante a
  espera, a mensagem não bloqueia as demais.
- **FR-013**: Esgotadas as tentativas, a mensagem MUST ir para a DLQ da fila, com o motivo
  da última falha, o número de tentativas e o instante. Ela sai da fila principal.
- **FR-014**: Uma mensagem que não pode ser lida (corpo inválido, fora do schema ou versão
  desconhecida) MUST ir direto para a DLQ, sem novas tentativas.
- **FR-015**: A base MUST descartar duplicados por consumidor: um evento já processado com
  sucesso por aquele consumidor é confirmado sem executar o efeito de novo. O registro de
  processado é gravado na mesma operação do efeito do consumidor.
- **FR-016**: Uma mensagem só MUST ser confirmada ao broker depois de processada (ou de
  enviada à DLQ). A queda do worker no meio do processamento faz a mensagem ser
  reentregue.

**Processo de workers**

- **FR-017**: Os consumidores MUST rodar num processo separado da API, com a mesma
  validação de configuração de banco e broker que a API faz na inicialização.
- **FR-018**: O processo de workers MUST criar a topologia de que precisa ao iniciar, de
  forma idempotente, e expor uma verificação de prontidão (banco e broker).
- **FR-019**: O processo de workers MUST entrar no perfil de demonstração (REQ-07), e a
  verificação da demonstração no CI MUST conferir que ele fica saudável.

**Observabilidade**

- **FR-020**: Publicação, novas tentativas, envio à DLQ e descarte de duplicado MUST ser
  registrados em log, com:
  - identificador e tipo do evento;
  - fila;
  - número da tentativa;
  - motivo.

  Os logs MUST NOT conter dados pessoais além dos identificadores.

**Compatibilidade**

- **FR-021**: O contrato HTTP v1 MUST permanecer inalterado. A criação de pedidos continua
  respondendo igual, mesmo com o broker fora do ar.
- **FR-022**: A suíte de testes MUST continuar verde, e os testes de integração de
  mensageria usam um broker real (Testcontainers), com a mesma exigência de Docker do CI
  (REQ-07).

### Key Entities *(include if feature involves data)*

- **Evento de domínio**: envelope (identificador, tipo, versão, instante, correlação) com o
  conteúdo de um dos quatro contratos.
- **Evento pendente (outbox)**: registro gravado com o pedido, com o evento serializado, o
  tipo, a situação (pendente ou enviado), o número de tentativas de publicação e as datas.
- **Evento processado (por consumidor)**: registro de que um consumidor já processou um
  evento com sucesso, usado para descartar duplicados.
- **DLQ**: fila de mensagens que não puderam ser processadas, com o evento original e o
  motivo.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos pedidos criados geram exatamente um evento "pedido criado"
  registrado. Nenhuma criação que falhou gera evento.
- **SC-002**: Com o broker no ar, 95% dos eventos são publicados em até 5 segundos após a
  criação do pedido.
- **SC-003**: Num teste com o broker parado durante 20 criações e religado depois, 100% dos
  20 eventos são publicados em até 30 segundos depois que o broker volta.
- **SC-004**: Mensagens que falham sempre chegam à DLQ depois de exatamente 4 tentativas
  (1 + 3) em 100% dos casos. Mensagens fora do contrato chegam à DLQ sem nova tentativa.
- **SC-005**: Um evento entregue duas vezes ao mesmo consumidor produz o efeito uma vez em
  100% dos casos.
- **SC-006**: 100% dos quatro contratos têm testes de serialização, ida e volta e recusa de
  mensagem inválida.
- **SC-007**: A suíte continua 100% verde no CI, com os testes de mensageria executados
  contra um broker real, e o contrato HTTP v1 não muda.

## Assumptions

- Os consumidores de negócio são das issues #8 a #10. Neste requisito, a base de consumo é
  exercitada por um consumidor de verificação usado só nos testes.
- Neste requisito, só "pedido criado" é publicado pelo sistema. Os outros três contratos
  ficam definidos para os workers que os publicarão.
- As mudanças de status por PATCH (REQ-05) não geram eventos (clarify). Um evento de
  cancelamento, com contrato próprio, entra quando o requisito de estoque ou de compensação
  pedir.
- O número de tentativas segue a sugestão da issue ("3 tentativas com intervalo
  exponencial"): a primeira entrega mais até 3 novas tentativas, 4 execuções no total
  (clarify; FR-012, SC-004).
- A ordem só é garantida na publicação, por ordem de registro. A ordem de consumo entre
  eventos de pedidos diferentes não é garantida.
- A limpeza de eventos enviados e de registros de duplicados antigos fica fora do escopo
  (o volume é pequeno), como as chaves de idempotência do REQ-05. Ver Pendências
  futuras.
- A DLQ é inspecionada e reenviada à mão pelo painel do broker. Uma ferramenta de
  reprocessamento fica fora do escopo. Ver Pendências futuras.

## Pendências futuras

Pedidas pelo usuário em 2026-10-22, para estudar a melhor abordagem e virar um requisito
próprio depois:

- **Limpeza de dados de mensageria e idempotência** — **resolvido no
  [REQ-14](../014-limpeza-e-dlq/spec.md)** (limpeza periódica no Worker, em lotes, com
  retenção por tipo). Registro original: eventos enviados do outbox, registros
  de eventos processados por consumidor e chaves de idempotência do REQ-05 (essas, só
  depois de 24 h). A decidir:
  - retenção de cada tipo;
  - onde roda, por exemplo um job periódico no processo de workers;
  - como evitar disputa entre instâncias;
  - métricas do que foi apagado.
- **Reprocessamento da DLQ** — **resolvido no [REQ-14](../014-limpeza-e-dlq/spec.md)**
  (pedido do Admin pela API, executado pelo Worker, com filtro, limite de reenvios e aviso de
  tamanho). Registro original: hoje é manual, pelo painel do broker. A decidir:
  - como reenviar mensagens da DLQ para a fila original (comando, rota administrativa
    protegida ou job);
  - filtros por tipo, período ou motivo;
  - limite de reenvios para não criar ciclo;
  - registro de quem reenviou;
  - alerta quando a DLQ cresce.
- **Evento publicado antes de a fila do consumidor existir** — **resolvido no
  [REQ-12](../012-publicacao-garantida/spec.md)** (declaração da topologia pelo despachante,
  `mandatory` e reconciliação de pedidos parados). Registro original (encontrado no REQ-08,
  em 2026-10-22, e pedido pelo usuário para tratar depois): num broker vazio, o despachante
  pode publicar um evento pendente antes de o consumidor declarar a fila dele. Como a
  publicação usa `mandatory: false` e a troca não tem fila ligada para aquela chave, a
  mensagem é descartada, embora o outbox a marque como enviada. Em uso normal a fila
  durável já existe; o risco é a primeira subida de um broker novo (ou recriado) com
  eventos pendentes. Os testes do REQ-08 contornam declarando a fila antes
  (`CenarioEstoque.DeclararFilaEstoqueAsync`). A decidir:
  - o despachante só começar depois de os consumidores do mesmo processo declararem a
    topologia;
  - declarar a topologia de todos os consumidores conhecidos na partida do despachante;
  - publicar com `mandatory: true` e tratar o retorno sem rota como falha de publicação
    (o evento continua pendente);
  - teste que reproduza o caso com um broker novo.

  **Consumidor novo para um evento já publicado** (decidido no clarify do REQ-10, em
  2026-10-23): o mesmo problema aparece quando um consumidor novo entra para um evento que
  já circulava sem fila. O caso concreto é o `pagamento.aprovado`: antes do REQ-10, nenhuma
  fila o recebia, e o broker descartou os eventos dos pedidos pagos até então. Esses pedidos
  ficam em `Processando` sem ordem de entrega, e o REQ-10 não os recupera (spec do 010,
  Assumptions). Proposta para o REQ de mensageria:
  - **evitar perdas futuras**: declarar na partida do despachante a topologia de todos os
    consumidores conhecidos e publicar com `mandatory: true`, tratando o retorno sem rota
    como falha (o evento continua pendente no outbox);
  - **recuperar o que já se perdeu**: o `mandatory` não ajuda, porque o outbox já marcou
    esses eventos como enviados. A recuperação parte do estado do pedido, com uma
    reconciliação idempotente (comando ou job no processo de workers) que procura pedidos
    aptos sem o efeito esperado (no REQ-10, `AptoParaOrdemEntrega` sem ordem) e aplica a
    mesma regra do consumidor, com o mesmo evento de saída;
  - rejeitado: republicar os eventos antigos do outbox. Isso depende de eles ainda
    existirem, e a limpeza pendente acima os apagaria. Além disso, a publicação vai para
    todas as filas ligadas àquela chave, não só para a do consumidor novo;
  - até lá, a operação pode mover esses pedidos pela API, mas eles continuam sem ordem de
    entrega.
- O ambiente local do REQ-02 (broker no ar) é pré-requisito para os testes de integração
  de mensageria. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
