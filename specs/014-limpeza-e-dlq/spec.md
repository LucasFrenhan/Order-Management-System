# Feature Specification: Limpeza de dados de mensageria e reprocessamento da DLQ

**Feature Branch**: `feature/req-14-limpeza-e-dlq`

**Requisito**: REQ-14 (GitHub issue #30 — "[Mensageria] Limpeza de dados de mensageria e reprocessamento da DLQ")

**Created**: 2026-10-24

**Status**: Draft

**Input**: User description: "REQ-14 (issue #30): limpar os dados de mensageria e de idempotência que crescem sem limite e tornar o reprocessamento da DLQ uma operação controlada. (1) Limpeza: rotina periódica no processo de workers apaga os eventos já enviados do outbox, os registros de eventos processados por consumidor e as chaves de idempotência do REQ-05 com mais de 24 h, cada tipo com retenção configurável; só uma instância limpa por vez, sem disputar com o despachante nem com os consumidores; nunca apaga evento pendente nem registro de processado dentro da janela de reentrega; cada execução registra em log quanto apagou. (2) Reprocessamento da DLQ: reenviar as mensagens da DLQ de um consumidor para a fila original sem o painel do broker (comando, rota administrativa protegida ou job, a decidir no clarify); filtro por tipo, período ou motivo; limite de reenvios para não criar ciclo; registro de quem reenviou; aviso em log quando a DLQ cresce além de um limite. (3) Testes de integração com banco e broker reais. Mensageria com RabbitMQ.Client nativo. Origem: Pendências futuras da spec do REQ-06."

## Contexto

O fluxo do pedido grava três tipos de dado que só crescem:

- **eventos de saída** (REQ-06): cada evento fica no outbox mesmo depois de enviado, com o
  instante do envio;
- **eventos processados** (REQ-06): cada consumidor guarda o identificador de cada evento que
  processou, para descartar reentregas;
- **chaves de idempotência** (REQ-05): cada criação de pedido guarda a chave do Cliente, a
  impressão do conteúdo e a resposta enviada, com garantia mínima de 24 h.

Num ambiente com movimento, essas tabelas crescem sem limite, deixam as consultas mais lentas
e ocupam espaço sem uso. Ao mesmo tempo, apagar cedo demais tira proteções: um evento ainda
não enviado se perderia, e um registro de processado apagado antes de uma reentrega deixaria
o consumidor processar o mesmo evento de novo.

A outra ponta é a **DLQ** (REQ-06): uma mensagem que falhou em todas as tentativas vai para a
fila de mortas do consumidor, com o motivo nos cabeçalhos. Hoje, a única forma de reenviá-la é
pelo painel do broker, sem filtro, sem registro de quem reenviou e sem proteção contra ciclos.
Desde o REQ-12, a reconciliação recupera o pedido parado pelo estado, mas a mensagem continua
na DLQ, e ninguém fica sabendo quando a DLQ cresce.

Há ainda uma fronteira a preservar: até aqui, quem fala com o broker é o processo de workers;
a API só grava eventos no outbox e abre uma conexão para conferir a prontidão do broker. Hoje
ela usa as mesmas credenciais do processo de workers, com permissão total no broker.

## Clarifications

### Session 2026-10-24 (specify)

- Q: Como o reenvio da DLQ é pedido, e por quem? → A: Por uma rota administrativa protegida
  na API, só para o perfil Admin, com os filtros na requisição. O autor registrado é o
  usuário autenticado. A rota é um acréscimo compatível ao contrato HTTP v1. (Revisto na
  pergunta seguinte: a rota só registra o pedido; quem fala com o broker é o processo de
  workers.)
- Q: A API pode acessar o broker para reenviar, ou o isolamento entre a API e o broker se
  mantém? → A: O isolamento se mantém. A rota de Admin **registra um pedido de reenvio** (autor
  do token, consumidor, filtro, instante) e responde na hora com o identificador dele; uma
  rotina do processo de workers executa os pedidos pendentes no broker e grava o resultado,
  que o Admin consulta por outra rota. Além disso, a API passa a usar um **usuário próprio no
  broker**, que só consegue conectar para a prontidão, sem permissão para configurar, ler ou
  publicar; o processo de workers mantém o usuário dele.

### Session 2026-10-24 (clarify)

- Q: O que acontece com uma mensagem da DLQ que já atingiu o limite de reenvios? → A: Fica na
  DLQ, contada como bloqueada pelo limite em cada pedido que a alcança, e continua contando
  para o aviso de tamanho da DLQ. Retirá-la é manual, pelo painel do broker (a purga continua
  fora do escopo).
- Q: Quem pode consultar os pedidos de reenvio? → A: Só o Admin, como no pedido; Cliente e
  OperadorLogistica recebem 403 nas duas rotas administrativas.
- Q: Os padrões de retenção da limpeza estão bons? → A: Sim: eventos enviados por 7 dias,
  registros de processado por 7 dias (mínimo de 1 dia), chaves de idempotência por 48 h
  (mínimo de 24 h) e pedidos de reenvio por 90 dias, todos configuráveis.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são quem opera o ambiente, que precisa de tabelas com tamanho
estável e de um jeito seguro de reenviar mensagens com falha, e o Cliente, cuja chave de
idempotência precisa continuar valendo pelo menos 24 h.

### User Story 1 - Os dados de mensageria e de idempotência não crescem sem limite (Priority: P1)

Uma rotina periódica no processo de workers apaga, em lotes, os eventos de saída já enviados,
os registros de eventos processados e as chaves de idempotência mais antigos que a retenção
de cada tipo, sem tocar em nada que ainda possa ser necessário.

**Why this priority**: É o problema que cresce com o uso; sem ele, as tabelas aumentam para
sempre.

**Independent Test**: Gravar eventos enviados, eventos pendentes, registros de processado e
chaves de idempotência, uns mais antigos e outros mais novos que a retenção; rodar a limpeza
e conferir que só os antigos elegíveis sumiram, com um log da contagem por tipo.

**Acceptance Scenarios**:

1. **Given** eventos de saída enviados há mais que a retenção e outros enviados há menos,
   **When** a limpeza roda, **Then** só os mais antigos que a retenção são apagados.
2. **Given** um evento de saída **pendente** (nunca enviado), de qualquer idade, **When** a
   limpeza roda, **Then** ele não é apagado.
3. **Given** registros de eventos processados mais antigos que a retenção e outros mais
   novos, **When** a limpeza roda, **Then** só os mais antigos são apagados.
4. **Given** chaves de idempotência com mais e com menos de 24 h (com a retenção padrão),
   **When** a limpeza roda, **Then** só as mais antigas que a retenção são apagadas, e uma
   repetição dentro das 24 h continua devolvendo o mesmo pedido.
5. **Given** muitos registros elegíveis de uma vez, **When** a limpeza roda, **Then** ela os
   apaga em lotes limitados, sem segurar as tabelas por muito tempo, e continua no ciclo
   seguinte se não terminar.
6. **Given** dois processos de workers no ar, **When** chega a hora da limpeza, **Then** só
   um deles limpa naquele ciclo.
7. **Given** uma limpeza que apagou registros, **When** o log é lido, **Then** há um registro
   com a quantidade apagada de cada tipo; um ciclo que não apagou nada não gera aviso.

---

### User Story 2 - Mensagens da DLQ podem ser reenviadas de forma controlada (Priority: P1)

Um Admin pede, pela API, o reenvio das mensagens da DLQ de um consumidor para a fila
original, escolhendo quais por tipo de evento, período em que falharam ou motivo. A API só
registra o pedido; o processo de workers o executa no broker, com um limite de reenvios por
mensagem, e grava o resultado, que o Admin consulta depois.

**Why this priority**: Hoje a única forma é o painel do broker, sem filtro nem registro; um
reenvio errado pode repetir uma falha em ciclo.

**Independent Test**: Colocar na DLQ de um consumidor mensagens de tipos e motivos diferentes,
pedir pela API o reenvio só das de um tipo e conferir que o pedido fica registrado com o
autor, que o processo de workers o executa, que só aquelas mensagens voltaram à fila
original com o contador de reenvios e que as demais continuam na DLQ; a consulta do pedido
mostra o resultado.

**Acceptance Scenarios**:

1. **Given** mensagens na DLQ de um consumidor, **When** um Admin pede o reenvio delas,
   **Then** a API registra o pedido, com o autor do token, o consumidor, o filtro e o
   instante, e responde na hora com o identificador do pedido, sem falar com o broker.
2. **Given** um pedido de reenvio registrado, **When** o processo de workers o executa,
   **Then** as mensagens selecionadas voltam para a fila original do consumidor e são
   processadas como uma entrega nova, com as tentativas do REQ-06 recomeçando do zero, e o
   pedido fica concluído com as contagens.
3. **Given** um pedido de reenvio, **When** o Admin o consulta, **Then** vê a situação
   (pendente, em execução, concluído ou com falha), o autor, o filtro, os instantes e, depois
   de concluído, as contagens.
4. **Given** mensagens de tipos, períodos e motivos diferentes, **When** o reenvio é pedido
   com um filtro (tipo, período em que falharam ou trecho do motivo), **Then** só as que
   atendem ao filtro são reenviadas; as demais continuam na DLQ, na mesma ordem.
5. **Given** uma mensagem que já foi reenviada o número máximo de vezes, **When** um novo
   reenvio a alcança, **Then** ela não é reenviada, continua na DLQ e é contada como
   bloqueada pelo limite.
6. **Given** um pedido concluído, **When** o resultado é lido, **Then** informa quantas
   mensagens foram reenviadas, quantas foram bloqueadas pelo limite e quantas não atendiam ao
   filtro, e o log do processo de workers registra o pedido, o autor, o consumidor, o filtro
   e as contagens.
7. **Given** uma mensagem reenviada cujo evento já tinha sido processado pelo consumidor (por
   exemplo, a reconciliação recuperou o pedido), **When** ela chega de novo, **Then** o
   consumidor a descarta como duplicada ou a ignora pela regra de pedido não apto, sem
   efeito repetido.
8. **Given** uma mensagem reenviada que falha de novo em todas as tentativas, **When** ela
   volta à DLQ, **Then** carrega o contador de reenvios atualizado.
9. **Given** um pedido de reenvio ou uma consulta, **When** chega sem token, **Then** a
   resposta é 401; com um token de Cliente ou de OperadorLogistica, é 403; em nenhum caso um
   pedido é registrado.
10. **Given** um pedido para um consumidor que não existe, ou com um filtro inválido (por
    exemplo, período com fim antes do início), **When** ele chega, **Then** a resposta é de
    erro de validação, e nenhum pedido é registrado.
11. **Given** o broker ou o processo de workers fora do ar, **When** um Admin pede o reenvio,
    **Then** o pedido é registrado normalmente e fica pendente; ele é executado quando o
    processo de workers conseguir falar com o broker.
12. **Given** a consulta de um pedido que não existe, **When** ela chega, **Then** a resposta
    é 404.

---

### User Story 3 - A operação fica sabendo quando a DLQ cresce (Priority: P2)

O processo de workers confere periodicamente o tamanho da DLQ de cada consumidor e registra
um aviso quando ela passa de um limite configurável.

**Why this priority**: Uma mensagem na DLQ indica uma falha que a reconciliação pode ter
contornado sem que ninguém investigue a causa.

**Independent Test**: Colocar na DLQ de um consumidor mais mensagens que o limite e conferir
o aviso com o consumidor e a quantidade; abaixo do limite, nenhum aviso.

**Acceptance Scenarios**:

1. **Given** a DLQ de um consumidor com mais mensagens que o limite, **When** a verificação
   roda, **Then** há um aviso em log com o consumidor e a quantidade.
2. **Given** todas as DLQs abaixo do limite, **When** a verificação roda, **Then** não há
   aviso.
3. **Given** o broker fora do ar, **When** a verificação roda, **Then** ela registra a falha
   e tenta no ciclo seguinte, sem derrubar o processo de workers.

---

### User Story 4 - A API tem acesso mínimo ao broker (Priority: P2)

A API passa a se conectar ao broker com um usuário próprio, que só consegue abrir a conexão
para a prontidão: não configura, não lê e não publica em nenhuma fila ou troca. O processo de
workers continua com o usuário dele, com as permissões de que precisa.

**Why this priority**: Hoje a API usa as mesmas credenciais do processo de workers, com
permissão total. Se ela fosse comprometida, daria para publicar eventos falsos (por exemplo,
um pagamento aprovado). Com o usuário próprio, o dano possível pela API no broker fica nulo.

**Independent Test**: Subir o ambiente com os dois usuários, conferir que a prontidão da API
continua saudável e que, com as credenciais da API, publicar numa troca, ler uma fila ou
declarar uma fila é recusado pelo broker.

**Acceptance Scenarios**:

1. **Given** o ambiente local e o de demonstração, **When** sobem, **Then** o broker tem os
   dois usuários, e a API e o processo de workers usam cada um o seu.
2. **Given** as credenciais da API, **When** alguém tenta publicar, ler, declarar ou apagar
   qualquer fila ou troca, **Then** o broker recusa.
3. **Given** a API no ar, **When** a prontidão é consultada, **Then** o broker aparece
   saudável, como hoje.
4. **Given** as credenciais da API erradas, **When** a prontidão é consultada, **Then** o
   broker aparece indisponível, sem revelar as credenciais, e a criação de pedidos continua
   funcionando.
5. **Given** o processo de workers, **When** publica, consome e reenvia, **Then** tudo
   funciona como antes, com o usuário dele.

---

### Edge Cases

- **Evento pendente muito antigo** (broker fora por dias): nunca é apagado, qualquer que seja
  a idade.
- **Reentrega depois da limpeza**: a retenção dos registros de processado MUST ficar bem
  acima da janela de reentrega (novas tentativas de segundos, republicação do outbox depois
  de uma queda, reenvio da DLQ); mesmo que um registro já tenha sido apagado, os consumidores
  continuam protegidos pela regra de pedido não apto (REQ-08 a REQ-10).
- **Chave de idempotência dentro das 24 h**: nunca é apagada, mesmo que a retenção
  configurada seja menor; a configuração recusa valores abaixo de 24 h.
- **Limpeza ao mesmo tempo que o despachante e os consumidores**: a limpeza só apaga linhas
  que eles não usam mais (eventos já enviados, processados antigos) e em lotes curtos, sem
  bloquear a publicação nem o consumo.
- **Banco fora do ar durante a limpeza**: o ciclo termina com falha em log e é tentado no
  próximo intervalo.
- **DLQ vazia ou nenhuma mensagem atende ao filtro**: o reenvio termina sem efeito e informa
  zero reenviadas.
- **Mensagem na DLQ sem os cabeçalhos do REQ-06** (por exemplo, colocada à mão): é tratada
  como sem motivo e sem reenvios anteriores; o filtro por motivo não a seleciona.
- **Mensagem com corpo fora do contrato** (foi para a DLQ sem tentativa): reenviá-la a manda
  de volta para a DLQ na primeira leitura; o limite de reenvios impede o ciclo.
- **Broker fora do ar no meio de um reenvio**: as mensagens já reenviadas ficam na fila
  original, e as demais continuam na DLQ; nenhuma mensagem é perdida nem duplicada pelo
  reenvio. O pedido fica com falha e as contagens parciais, e o Admin pode pedir outro.
- **Dois pedidos de reenvio para o mesmo consumidor** (por exemplo, de dois Admins): são
  executados um de cada vez, na ordem em que foram registrados; cada mensagem é reenviada no
  máximo uma vez por pedido.
- **Processo de workers cai no meio da execução de um pedido**: as mensagens já reenviadas
  ficam na fila original e as demais na DLQ; o pedido volta a ser executado na subida
  seguinte, sobre o que restou na DLQ, e o limite de reenvios impede o ciclo.
- **Pedido de reenvio parado** (processo de workers fora do ar por muito tempo): fica
  pendente até ser executado; a consulta mostra há quanto tempo.
- **Credenciais da API sem permissão**: se algum código da API tentar usar o broker além da
  prontidão, o broker recusa; isso é tratado como erro de programação e aparece nos testes.
- **Consumidor sem DLQ declarada** (broker recriado): o reenvio informa zero mensagens.

## Requirements *(mandatory)*

### Functional Requirements

**Limpeza**

- **FR-001**: O processo de workers MUST rodar, sozinho e sem disparo manual, uma limpeza
  periódica, com intervalo configurável.
- **FR-002**: A limpeza MUST apagar os eventos de saída **enviados** há mais que a retenção
  configurada para eles, e MUST NOT apagar nenhum evento pendente.
- **FR-003**: A limpeza MUST apagar os registros de eventos processados mais antigos que a
  retenção configurada para eles. A retenção padrão MUST ficar bem acima da janela de
  reentrega, e a configuração MUST recusar valores abaixo de um mínimo que a cubra.
- **FR-004**: A limpeza MUST apagar as chaves de idempotência do REQ-05 mais antigas que a
  retenção configurada para elas, e a configuração MUST recusar valores abaixo de 24 h.
- **FR-005**: A limpeza MUST apagar em lotes de tamanho configurável, para não segurar as
  tabelas nem disputar com o despachante, os consumidores e a criação de pedidos; o que
  sobrar fica para o ciclo seguinte.
- **FR-006**: Só uma instância do processo de workers MUST limpar por ciclo, como a
  reconciliação do REQ-12.
- **FR-007**: Cada ciclo que apagou algo MUST registrar em log a quantidade apagada de cada
  tipo; a falha do ciclo MUST ser registrada e tentada no próximo intervalo, sem derrubar o
  processo.

**Reprocessamento da DLQ**

- **FR-008**: Um Admin MUST poder pedir o reenvio das mensagens da DLQ de um consumidor para
  a fila original dele, sem o painel do broker, por uma rota administrativa da API protegida
  por token e restrita ao perfil Admin. A rota MUST só registrar o pedido (autor do token,
  consumidor, filtro e instante) e responder na hora com o identificador dele, MUST NOT
  falar com o broker e MUST funcionar com o broker e o processo de workers fora do ar. Sem
  token, a resposta MUST ser 401; com outro perfil, 403. Consumidor desconhecido ou filtro
  inválido MUST responder com erro de validação, sem registrar nada.
- **FR-009**: O Admin MUST poder consultar um pedido de reenvio pelo identificador, com a
  situação (pendente, em execução, concluído ou com falha), o autor, o filtro, os instantes
  de registro, início e fim e, quando houver, as contagens e o motivo da falha. Pedido
  inexistente MUST responder 404; a mesma proteção do FR-008 vale para a consulta.
- **FR-010**: Uma rotina do processo de workers MUST executar os pedidos pendentes, sozinha
  e sem disparo manual, um de cada vez, na ordem de registro, com uma única instância por
  rodada (como a reconciliação do REQ-12), e gravar o resultado no pedido. Um pedido
  interrompido (queda do processo de workers ou do broker) MUST ser retomado ou marcado com
  falha, nunca ficar "em execução" para sempre.
- **FR-011**: O reenvio MUST aceitar filtros opcionais e combináveis: tipo de evento,
  período em que a mensagem foi para a DLQ e trecho do motivo. Sem filtro, todas as
  mensagens do consumidor são candidatas.
- **FR-012**: Cada mensagem reenviada MUST levar um contador de reenvios. Uma mensagem que já
  atingiu o limite configurável de reenvios MUST NOT ser reenviada: ela continua na DLQ e é
  contada como bloqueada.
- **FR-013**: A mensagem reenviada MUST voltar como uma entrega nova, com as tentativas do
  REQ-06 recomeçando do zero, e manter o identificador do evento, para o descarte de
  duplicados continuar valendo.
- **FR-014**: O reenvio MUST NOT perder nem duplicar mensagens: cada mensagem selecionada
  sai da DLQ só depois de estar na fila original, e as não selecionadas continuam na DLQ.
- **FR-015**: Cada execução MUST gravar no pedido as contagens (reenviadas, bloqueadas pelo
  limite e fora do filtro) e registrar em log o identificador do pedido, o autor, o
  consumidor, o filtro e as contagens. A mensagem reenviada MUST levar também o identificador
  do pedido de reenvio e o instante do reenvio.
- **FR-016**: Cada pedido MUST tratar no máximo um número configurável de mensagens; as que
  sobrarem continuam na DLQ para outro pedido.

**Acesso da API ao broker**

- **FR-017**: A API MUST se conectar ao broker com credenciais próprias, diferentes das do
  processo de workers, cujo usuário só pode abrir a conexão para a prontidão: sem permissão
  para configurar, ler ou publicar em nenhuma fila ou troca.
- **FR-018**: Os ambientes local e de demonstração e o CI MUST criar os dois usuários no
  broker, com credenciais configuráveis e padrões só de desenvolvimento, como as demais.
  Nenhuma credencial real é gravada no repositório.
- **FR-019**: Nenhum código da API MUST usar o broker além da prontidão; um teste MUST
  provar que, com as credenciais da API, publicar, ler e declarar são recusados pelo broker.

**Aviso da DLQ**

- **FR-020**: O processo de workers MUST conferir periodicamente o tamanho da DLQ de cada
  consumidor conhecido e registrar um aviso quando passar de um limite configurável, com o
  consumidor e a quantidade.

**Configuração e compatibilidade**

- **FR-021**: Intervalos, retenções, tamanhos de lote, limite de reenvios e limite do aviso
  MUST ser configuráveis, com padrões (Assumptions) e validação na partida, como as demais
  opções do processo de workers.
- **FR-022**: Os contratos de eventos v1 MUST permanecer inalterados; os cabeçalhos novos da
  mensagem reenviada são de transporte, não do envelope. O contrato HTTP v1 MUST mudar só por
  acréscimo: as rotas administrativas de pedido e de consulta de reenvio e os schemas delas,
  com testes de conformidade e de autorização (401 e 403), como exige a constituição.
- **FR-023**: A suíte MUST ter testes de integração, com banco e broker reais, da limpeza
  (o que é e o que não é apagado, lotes e duas instâncias), do reenvio (registro pela API,
  execução pelo processo de workers, filtros, limite, ausência de perda e de duplicação,
  autor, retomada depois de uma queda), do aviso e das permissões do usuário da API no
  broker, e MUST continuar verde no CI.

### Key Entities *(include if feature involves data)*

- **Evento de saída** (REQ-06): conteúdo, tipo, tentativas, último erro, próxima tentativa e
  instante de envio. Elegível para limpeza só depois de enviado.
- **Evento processado** (REQ-06): consumidor, identificador do evento e instante do
  processamento.
- **Chave de idempotência** (REQ-05): cliente, chave, impressão do conteúdo, pedido, resposta
  e instante da criação.
- **Mensagem da DLQ** (REQ-06): corpo do evento e cabeçalhos com tentativas, último erro,
  motivo, instante da falha e, a partir deste requisito, contador de reenvios, identificador
  do último pedido de reenvio e instante do reenvio.
- **Pedido de reenvio** (novo, gravado no banco): identificador, autor (o Admin
  autenticado), consumidor, filtro (tipo, período e trecho do motivo), situação (pendente,
  em execução, concluído, com falha), instantes de registro, início e fim, contagens e motivo
  da falha. É o registro de quem reenviou o quê.
- **Credenciais do broker**: um usuário para o processo de workers, com as permissões de
  publicar e consumir, e um para a API, que só conecta.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com o uso constante, o número de eventos enviados, de registros de processado e
  de chaves de idempotência guardados fica estável, limitado ao volume do período de
  retenção mais um ciclo de limpeza.
- **SC-002**: Nenhum evento pendente, nenhuma chave de idempotência com menos de 24 h e
  nenhum registro de processado dentro da retenção é apagado, em 100% das execuções.
- **SC-003**: A limpeza não aumenta o tempo do fluxo normal (do pedido criado ao "entrega
  criada") além da meta do REQ-10 (95% em até 10 segundos no ambiente de demonstração).
- **SC-004**: Em 100% dos reenvios, cada mensagem selecionada chega à fila original uma única
  vez, e nenhuma mensagem some da DLQ sem chegar à fila original.
- **SC-005**: Nenhuma mensagem é reenviada mais vezes que o limite configurado.
- **SC-006**: Uma DLQ acima do limite gera aviso em até um intervalo de verificação.
- **SC-007**: Com o processo de workers e o broker no ar, um pedido de reenvio começa a ser
  executado em até um intervalo da rotina (com o padrão, em até 1 minuto), e a rota que o
  registra responde no mesmo tempo das demais rotas da API, com o broker no ar ou não.
- **SC-008**: Com as credenciais da API, 100% das tentativas de publicar, ler ou declarar no
  broker são recusadas.

## Assumptions

- **Valores padrão**, ajustáveis no plano: limpeza a cada 1 h, em lotes de 1 000 linhas;
  eventos enviados guardados por 7 dias; registros de processado por 7 dias (mínimo de 1
  dia); chaves de idempotência por 48 h (mínimo de 24 h); limite de 3 reenvios por mensagem;
  até 1 000 mensagens por pedido de reenvio; rotina de reenvio a cada 1 minuto; aviso da DLQ
  acima de 10 mensagens, conferido a cada 5 minutos. Os pedidos de reenvio entram na limpeza,
  com retenção padrão de 90 dias, para o histórico de quem reenviou não crescer sem limite.
- A "janela de reentrega" cobre as novas tentativas do REQ-06 (segundos), a republicação do
  outbox depois de uma queda (segundos a minutos) e o reenvio da DLQ (dias). Uma mensagem da
  DLQ nunca foi processada com sucesso por aquele consumidor (é por isso que está lá), então
  o reenvio não depende do registro de processado; os consumidores ainda se protegem pela
  regra de pedido não apto.
- A limpeza e o aviso rodam no processo de workers, como a reconciliação; o primeiro ciclo
  acontece logo após a subida.
- O reenvio vale para as DLQs dos consumidores conhecidos (estoque, pagamento e entrega).
- O reenvio é assíncrono: a rota registra o pedido e responde com o identificador; o
  processo de workers o executa e grava o resultado. A API continua sem falar com o broker
  além da prontidão, preservando a fronteira (só o processo de workers usa o broker) e o
  princípio IV da constituição; o pedido gravado também é o histórico de reenvios.
- O usuário da API no broker só abre a conexão da prontidão. A permissão de conexão ao vhost
  existe, mas os padrões de configurar, ler e publicar não casam com nenhuma fila ou troca.
- A troca das credenciais nos ambientes existentes é uma mudança de configuração: quem já
  tem o ambiente local precisa recriar o broker ou criar o usuário novo, como o quickstart
  vai orientar.
- Só o perfil Admin reenvia; o OperadorLogistica não, porque o reenvio mexe na infraestrutura
  de mensageria, não num pedido.
- A purga de mensagens da DLQ (apagar sem reenviar) continua pelo painel do broker e fica fora
  do escopo.
- Ficam fora do escopo: métricas e alertas além dos logs, arquivamento dos dados apagados e
  a visibilidade dos cancelamentos (#29).
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
