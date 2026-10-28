# Feature Specification: Recuperação dos consumidores depois de o broker reiniciar

**Feature Branch**: `feature/req-17-recuperacao-consumidores`

**Requisito**: REQ-17 (GitHub issue #41 — "[Mensageria] Consumidores do Worker não voltam depois de o RabbitMQ reiniciar")

**Created**: 2026-10-27

**Status**: Draft

**Input**: User description: "REQ-17 (issue #41): depois de o RabbitMQ reiniciar, as filas principais (oms.estoque, oms.pagamento, oms.entrega) ficam com 0 consumidores até o Worker ser reiniciado; o despachante volta a publicar, mas ninguém consome, e os pedidos ficam parados. A reconciliação (REQ-12) não resolve. Hipótese: a conexão compartilhada é descartada no meio da recuperação automática, e o consumidor só se inscreve uma vez, na partida. Escopo: confirmar a causa, avaliar as alternativas e registrar a escolhida, refletir os consumidores parados na prontidão, teste de integração com broker real que reinicia o contêiner e atualização do fluxo 08. Critérios de aceite: os três consumidores voltam a consumir sem reiniciar o Worker em até 1 min; nenhuma mensagem perdida nem efeito repetido; despachante, monitor da DLQ e reenvio da DLQ continuam funcionando durante e depois da queda; teste de integração cobrindo a reinicialização do broker. RabbitMQ.Client nativo, sem MassTransit."

## Contexto

O processo de workers mantém **uma conexão** com o broker (REQ-06), usada por todos os
componentes que falam com ele: os três consumidores (estoque, pagamento e entrega), o
despachante do outbox, o monitor da DLQ e o reenvio da DLQ (REQ-14). Cada consumidor se
inscreve na fila principal dele **uma vez**, na partida, e tenta de novo até conseguir se o
broker ainda não estiver no ar.

No quickstart do REQ-16 apareceu um defeito anterior a ele: quando o broker reinicia com o
processo de workers no ar, o despachante volta a publicar, mas as três filas principais ficam
**sem nenhum consumidor** até alguém reiniciar o processo de workers. Os pedidos param no
primeiro passo (por exemplo, em `Pendente`, com o `pedido.criado` na fila do estoque), e nada
avisa: a prontidão do processo de workers só confere se o broker aceita uma conexão nova, e
ele aceita. A reconciliação (REQ-12) também não ajuda, porque os eventos que ela grava vão
para as mesmas filas sem consumidor.

A hipótese da issue é que a conexão compartilhada, em recuperação automática, é vista como
fechada por quem a pede nesse intervalo (o despachante, a cada segundo, e o monitor da DLQ;
a prontidão não entra, porque abre uma conexão própria) e é descartada e
trocada por outra; os canais de consumo pertenciam à conexão descartada, e nada inscreve os
consumidores de novo. A mesma lacuna já está registrada no fluxo 08: uma fila de consumidor
apagada com o processo de workers no ar faz o consumidor dela perder a inscrição até o
processo reiniciar.

## Clarifications

### Session 2026-10-28

- Q: A fila apagada e a conexão derrubada à força, sem o broker inteiro cair, ficam no escopo
  deste REQ (User Story 2)? → A: Sim. Ficam no escopo a fila apagada, o canal fechado, a
  inscrição cancelada pelo broker e a conexão fechada à força, cada um com teste de
  integração; a limitação da fila apagada sai do fluxo 08.
- Q: Quando um consumidor perde a inscrição, a prontidão do Worker fica não pronta na hora ou
  só depois de uma tolerância? → A: Na hora, sem tolerância: a entrada `consumidores` mostra
  o estado atual das três inscrições, como a entrada `rabbitmq` mostra o do broker.
- Q: O REQ inclui uma métrica de consumidores inscritos, além da prontidão e dos logs? → A:
  Sim: um medidor de inscrição por consumidor (1 inscrito, 0 sem inscrição) e um contador de
  voltas por consumidor, os dois só com a telemetria ligada, como acréscimo às métricas do
  REQ-16.

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são quem opera o ambiente, que não deveria precisar reiniciar o
processo de workers depois de uma queda do broker, e o Cliente, cujo pedido precisa seguir o
fluxo sozinho quando o broker volta.

### User Story 1 - O consumo volta sozinho depois de o broker reiniciar (Priority: P1)

Quando o broker cai e volta, com o processo de workers no ar o tempo todo, os três
consumidores voltam a consumir as filas deles sem intervenção, e os pedidos parados na queda
seguem o fluxo.

**Why this priority**: É o defeito da issue. Sem ele, toda queda do broker para o fluxo de
pedidos até alguém perceber e reiniciar o processo de workers.

**Independent Test**: Com o processo de workers no ar e os três consumidores inscritos,
reiniciar o contêiner do broker (parar e iniciar, não só pausar), esperar até 1 min depois de
o broker voltar e conferir que cada fila principal tem um consumidor; criar um pedido e
conferir que ele chega ao fim do fluxo.

**Acceptance Scenarios**:

1. **Given** o processo de workers no ar com os três consumidores inscritos, **When** o
   broker para e volta, **Then** em até 1 min depois de o broker aceitar conexões cada fila
   principal tem exatamente um consumidor deste processo, sem reiniciar o processo de workers.
2. **Given** um pedido criado enquanto o broker estava fora, **When** o broker volta, **Then**
   o evento sai do outbox e o pedido segue o fluxo até o fim, sem ação manual.
3. **Given** mensagens já nas filas principais (paradas na queda ou entregues e ainda sem
   confirmação quando a conexão caiu), **When** o consumo volta, **Then** todas são
   processadas, e as que já tinham efeito gravado são descartadas como duplicadas, sem
   repetir o efeito.
4. **Given** o broker fora por mais tempo (vários minutos), **When** ele volta, **Then** o
   consumo volta no mesmo prazo, contado a partir da volta do broker.
5. **Given** quedas seguidas do broker, **When** ele volta da última, **Then** o consumo
   volta, e cada fila continua com um único consumidor deste processo (nenhuma inscrição
   duplicada).
6. **Given** o broker voltando sem as filas (dados do broker apagados), **When** o consumo
   volta, **Then** as filas, as filas de espera e as DLQs de cada consumidor existem de novo
   antes da inscrição.

---

### User Story 2 - Um consumidor que perde a inscrição por outro motivo também volta (Priority: P2)

Se um consumidor perde a inscrição sem o broker inteiro cair (o canal dele fecha por erro, o
broker cancela a inscrição porque a fila foi apagada, a conexão cai por rede), ele se inscreve
de novo sozinho, no mesmo prazo.

**Why this priority**: Resolve a limitação já registrada no fluxo 08 (fila apagada com o
processo no ar) e evita depender de um único jeito de queda; mas é mais raro que o reinício
do broker.

**Independent Test**: Com o processo de workers no ar, apagar a fila principal de um
consumidor (ou fechar à força a conexão do processo pelo broker) e conferir que, em até 1 min,
a fila existe de novo com um consumidor e que um evento publicado nela é processado.

**Acceptance Scenarios**:

1. **Given** a fila principal de um consumidor apagada com o processo no ar, **When** passa
   o prazo de recuperação, **Then** a fila existe de novo, com o consumidor inscrito, e os
   outros dois consumidores não foram afetados.
2. **Given** a conexão do processo fechada à força pelo broker, **When** passa o prazo de
   recuperação, **Then** os três consumidores voltam a consumir.
3. **Given** o processo de workers sendo encerrado, **When** a inscrição de um consumidor cai
   durante o encerramento, **Then** ele não tenta se inscrever de novo e o encerramento não
   trava.

---

### User Story 3 - A prontidão e os logs mostram o consumo parado e a volta (Priority: P2)

Enquanto algum consumidor estiver sem inscrição, a prontidão do processo de workers responde
como não pronta, e os logs registram a queda da conexão, cada tentativa de voltar e a volta
de cada consumidor.

**Why this priority**: Hoje a parada é silenciosa: a prontidão diz que está tudo bem. Mesmo
com a recuperação automática, quem opera precisa ver quando o consumo está parado e quanto
tempo levou para voltar.

**Independent Test**: Parar o broker, conferir que a prontidão do processo de workers passa a
não pronta, com a entrada dos consumidores indisponível; iniciar o broker e conferir que ela
volta a pronta depois de os consumidores se inscreverem, e que os logs mostram a sequência.

**Acceptance Scenarios**:

1. **Given** os três consumidores inscritos, **When** a prontidão do processo de workers é
   consultada, **Then** ela responde pronta, com uma entrada para os consumidores.
2. **Given** um consumidor sem inscrição (broker fora ou recuperação em curso), **When** a
   prontidão é consultada, **Then** ela responde não pronta, e a entrada dos consumidores
   aparece indisponível, sem revelar nomes de host, credenciais ou mensagens de erro.
3. **Given** o broker de volta e os consumidores inscritos, **When** a prontidão é consultada,
   **Then** ela volta a responder pronta.
4. **Given** uma queda e uma volta do broker, **When** os logs são lidos, **Then** há registro
   da perda da conexão (ou do canal) com o motivo, das tentativas que falharam e da volta de
   cada consumidor, cada um com um ID de log próprio e estável, e sem credenciais.
5. **Given** a prontidão da API, **When** ela é consultada, **Then** nada muda: a API não tem
   consumidores.

---

### Edge Cases

- **Mensagem em processamento quando a conexão cai**: o efeito já gravado não se repete; a
  confirmação perdida faz o broker reentregar a mensagem, que é descartada como duplicada.
  Se o efeito ainda não tinha sido gravado, a reentrega o executa uma vez.
- **Falha no meio do tratamento de uma mensagem com erro** (republicar na fila de espera ou na
  DLQ sem conseguir, porque a conexão caiu): a mensagem original não é confirmada e volta
  para a fila; nada é perdido, e a contagem de tentativas não é pulada.
- **Recuperação automática do cliente e reinscrição própria ao mesmo tempo**: o resultado é
  sempre um único consumidor por fila neste processo.
- **Broker volta, mas recusa as credenciais**: o consumo não volta; a prontidão continua não
  pronta e os logs mostram a falha, sem revelar as credenciais, até as credenciais voltarem a
  valer.
- **Rede instável (conexão cai e volta várias vezes em segundos)**: a recuperação não abre
  conexões nem canais em excesso; tentativas seguidas têm um intervalo entre elas.
- **Despachante, monitor da DLQ e reenvio durante a queda**: falham como hoje (o evento
  continua pendente, o ciclo do monitor e o pedido de reenvio registram a falha) e voltam a
  funcionar depois, sem reiniciar o processo e sem derrubar a conexão que os consumidores
  estão recuperando.
- **Pedido de reenvio da DLQ em execução quando o broker cai**: nenhuma mensagem é perdida
  (o que não foi confirmado continua na DLQ) e o resultado do pedido registra a falha, como
  já acontece com qualquer erro do broker no REQ-14.
- **Processo de workers sendo encerrado durante a recuperação**: o encerramento termina no
  prazo normal, sem ficar preso numa tentativa de reconexão.

## Requirements *(mandatory)*

### Functional Requirements

**Causa**

- **FR-001**: A causa da parada MUST ser confirmada com o broker real antes da correção e
  registrada no estudo do plano, com a sequência observada (queda, tentativa de recuperação,
  descarte ou troca da conexão, estado dos consumidores).
- **FR-002**: As alternativas de correção (não descartar uma conexão em recuperação; o
  consumidor perceber a perda da inscrição e se inscrever de novo; conexões separadas para
  publicar e consumir) MUST ser avaliadas no estudo, e a escolhida MUST ser registrada com o
  motivo.

**Recuperação do consumo**

- **FR-003**: Depois de o broker voltar a aceitar conexões, cada consumidor MUST voltar a
  consumir a fila principal dele em até 1 min, sem reiniciar o processo de workers, qualquer
  que tenha sido o tempo de queda.
- **FR-004**: Um consumidor MUST voltar a se inscrever também quando perde a inscrição sem o
  broker inteiro cair: canal fechado, inscrição cancelada pelo broker (fila apagada) ou
  conexão fechada à força.
- **FR-005**: Antes de se inscrever de novo, o consumidor MUST garantir que a topologia dele
  existe (fila principal, filas de espera e DLQ, com as mesmas ligações e argumentos de hoje).
- **FR-006**: Cada fila principal MUST ter no máximo um consumidor por processo de workers em
  qualquer momento, inclusive com quedas seguidas e com recuperações simultâneas.
- **FR-007**: As tentativas de voltar MUST ter um intervalo entre elas, sem abrir conexões ou
  canais em excesso, e MUST parar quando o processo de workers está sendo encerrado.

**Garantias de entrega preservadas**

- **FR-008**: Nenhuma mensagem MUST ser perdida por causa da queda: uma mensagem só é
  confirmada depois de processada ou republicada com confirmação, como hoje (REQ-06, FR-016).
- **FR-009**: Uma mensagem reentregue por causa da queda MUST continuar sendo descartada como
  duplicada quando o efeito já foi gravado (REQ-06, FR-015), sem efeito repetido.
- **FR-010**: A política de novas tentativas e de DLQ (REQ-06) e a ordem de confirmação MUST
  continuar iguais; a contagem de tentativas de uma mensagem não pode ser zerada nem pulada
  pela queda.

**Os outros componentes da mensageria**

- **FR-011**: O despachante do outbox, o monitor da DLQ e o reenvio da DLQ MUST continuar
  funcionando depois de o broker voltar, sem reiniciar o processo de workers, e durante a
  queda MUST falhar como hoje (evento pendente, ciclo com falha registrada, pedido de reenvio
  com o resultado de falha), sem perder dados.
- **FR-012**: Nenhum componente MUST descartar ou trocar a conexão compartilhada de um jeito
  que derrube a inscrição ou o trabalho dos outros enquanto a recuperação está em curso.

**Prontidão e logs**

- **FR-013**: A prontidão do processo de workers MUST ganhar uma entrada `consumidores`,
  pronta só quando os três estão inscritos e não pronta a partir do momento em que algum perde
  a inscrição, sem tolerância, até ele voltar; a resposta
  continua mostrando só nomes e estados, sem dados de conexão (REQ-02).
- **FR-014**: A prontidão da API MUST continuar como está.
- **FR-015**: O processo de workers MUST registrar em log, com IDs de log novos e estáveis: a
  perda da conexão ou do canal de consumo (com o motivo), a falha de uma tentativa de voltar
  e a volta de cada consumidor; os IDs de log existentes não mudam.
- **FR-016**: Os logs novos MUST seguir a correlação com o trace do REQ-16 quando a telemetria
  está ligada e MUST não incluir credenciais nem a cadeia de conexão.
- **FR-016a**: Com a telemetria ligada (REQ-16), o processo de workers MUST publicar duas
  métricas novas, rotuladas pelo consumidor: o estado da inscrição (1 inscrito, 0 sem
  inscrição) e a quantidade de vezes que o consumidor voltou a se inscrever depois de perder a
  inscrição. As seis métricas do REQ-16 não mudam; com a telemetria desligada, nada é
  publicado.

**Verificação e documentação**

- **FR-017**: Um teste de integração com broker real MUST reiniciar o contêiner do broker (parar
  e iniciar, não só pausar) com o processo de consumo no ar e provar que os três consumidores
  voltam a consumir no prazo, que um evento publicado depois é processado e que uma mensagem
  reentregue não repete o efeito.
- **FR-018**: Testes de integração MUST cobrir cada perda de inscrição sem a queda do broker
  (fila principal apagada, canal de consumo fechado, inscrição cancelada pelo broker e conexão
  fechada à força pelo broker) e a prontidão do processo de workers durante e depois da queda.
- **FR-019**: O fluxo 08 (`docs/fluxos/08-publicacao-e-consumo-de-eventos.md`) MUST ser
  atualizado: a seção "Falhas externas" passa a descrever a queda e a volta do broker com o
  processo no ar e a fila apagada; a limitação da fila apagada sai da tabela como pendência e
  passa a citar a issue #41; a prontidão e os logs novos entram nas seções correspondentes. O
  fluxo 12 (`docs/fluxos/12-rastreabilidade-e-metricas.md`) ganha as duas métricas novas
  (FR-016a).

### Key Entities

- **Inscrição de consumidor**: o vínculo entre um consumidor (estoque, pagamento ou entrega) e
  a fila principal dele no broker; tem um estado (inscrito ou sem inscrição), que a prontidão
  lê e que a recuperação restaura.
- **Conexão com o broker**: a conexão do processo de workers, compartilhada ou separada por
  papel conforme a alternativa escolhida; tem os estados aberta, em recuperação e fechada.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Depois de um reinício do broker com o processo de workers no ar, os três
  consumidores voltam a consumir em até 1 min em 100% das execuções do teste de integração,
  sem reiniciar o processo de workers.
- **SC-002**: Um pedido criado antes, durante ou depois da queda chega ao fim do fluxo sem
  nenhuma ação manual.
- **SC-003**: Nenhuma mensagem é perdida e nenhum efeito é aplicado duas vezes nas execuções
  do teste de queda (contagem de pedidos, reservas, pagamentos e ordens de entrega igual à
  esperada).
- **SC-004**: Durante a queda, a prontidão do processo de workers responde não pronta em toda
  consulta feita enquanto algum consumidor está sem inscrição, e volta a pronta em até 1 min
  depois de o broker voltar.
- **SC-005**: O passo 6 da reprodução da issue (`docker compose restart worker`) deixa de ser
  necessário: o quickstart da issue, seguido do começo ao fim, termina com o pedido entregue.
- **SC-006**: O tempo de fim a fim de um pedido sem queda do broker não piora de forma
  perceptível em relação à `main`.

## Assumptions

- A recuperação vale para o processo de workers; a API não consome e só abre conexões curtas
  para a prontidão, que continuam como estão.
- O prazo de 1 min conta a partir do momento em que o broker volta a aceitar conexões, não a
  partir da queda.
- A garantia de entrega continua "pelo menos uma vez", com o descarte de duplicados do REQ-06;
  este requisito não muda os contratos de evento (envelope v1, cabeçalhos e filas).
- Reiniciar o contêiner do broker no teste de integração com o mesmo volume de dados é a forma
  representativa da queda; o broker sem volume (filas apagadas) entra como caso de borda.
- A fila apagada com o processo no ar entra no escopo (User Story 2; Clarifications
  2026-10-28), porque a mesma reinscrição resolve as duas situações e o fluxo 08 já a registra
  como limitação.
- Não há reinício automático do processo de workers por prontidão não pronta no ambiente de
  demonstração; a entrada nova na prontidão é para diagnóstico e para orquestradores que a
  usem.
- A mensageria continua com o RabbitMQ.Client nativo (constituição); nenhuma dependência nova
  é esperada, e qualquer uma exigiria justificativa no plano.
- Com a telemetria ligada (REQ-16), os spans e as métricas de consumo continuam como estão;
  as duas métricas novas (FR-016a) são acréscimos, e os nomes delas são definidos no plano.
