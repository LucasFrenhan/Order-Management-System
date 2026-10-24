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

## Clarifications

### Session 2026-10-24 (specify)

- Q: Como o reenvio da DLQ é pedido, e por quem? → A: Por uma rota administrativa protegida
  na API, só para o perfil Admin, com os filtros na requisição. O autor registrado é o
  usuário autenticado. A rota é um acréscimo compatível ao contrato HTTP v1.

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

Um Admin reenvia, pela API, as mensagens da DLQ de um consumidor para a fila original,
escolhendo quais por tipo de evento, período em que falharam ou motivo, com um limite de
reenvios por mensagem e o registro de quem fez o reenvio.

**Why this priority**: Hoje a única forma é o painel do broker, sem filtro nem registro; um
reenvio errado pode repetir uma falha em ciclo.

**Independent Test**: Colocar na DLQ de um consumidor mensagens de tipos e motivos diferentes,
reenviar só as de um tipo e conferir que só elas voltaram à fila original, com o contador de
reenvios e o autor registrados, e que as demais continuam na DLQ.

**Acceptance Scenarios**:

1. **Given** mensagens na DLQ de um consumidor, **When** quem opera pede o reenvio delas,
   **Then** elas voltam para a fila original do consumidor e são processadas como uma
   entrega nova, com as tentativas do REQ-06 recomeçando do zero.
2. **Given** mensagens de tipos, períodos e motivos diferentes, **When** o reenvio é pedido
   com um filtro (tipo, período em que falharam ou trecho do motivo), **Then** só as que
   atendem ao filtro são reenviadas; as demais continuam na DLQ, na mesma ordem.
3. **Given** uma mensagem que já foi reenviada o número máximo de vezes, **When** um novo
   reenvio é pedido, **Then** ela não é reenviada, continua na DLQ e aparece no resultado
   como bloqueada pelo limite.
4. **Given** um reenvio, **When** ele termina, **Then** o resultado informa quantas mensagens
   foram reenviadas, quantas foram bloqueadas pelo limite e quantas não atendiam ao filtro,
   e o log registra quem pediu, o consumidor, o filtro e as contagens.
5. **Given** uma mensagem reenviada cujo evento já tinha sido processado pelo consumidor (por
   exemplo, a reconciliação recuperou o pedido), **When** ela chega de novo, **Then** o
   consumidor a descarta como duplicada ou a ignora pela regra de pedido não apto, sem
   efeito repetido.
6. **Given** uma mensagem reenviada que falha de novo em todas as tentativas, **When** ela
   volta à DLQ, **Then** carrega o contador de reenvios atualizado.
7. **Given** um pedido de reenvio, **When** ele chega sem token, **Then** a resposta é 401;
   com um token de Cliente ou de OperadorLogistica, a resposta é 403; nos dois casos, nada é
   reenviado.
8. **Given** um pedido de reenvio para um consumidor que não existe, ou com um filtro
   inválido (por exemplo, período com fim antes do início), **When** ele chega, **Then** a
   resposta é de erro de validação, e nada é reenviado.
9. **Given** o broker fora do ar, **When** um Admin pede o reenvio, **Then** a resposta
   informa que o serviço de mensageria está indisponível, sem detalhes internos, e nada é
   reenviado.

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
  reenvio.
- **Dois reenvios ao mesmo tempo para o mesmo consumidor**: cada mensagem é reenviada no
  máximo uma vez.
- **Requisição de reenvio interrompida** (por exemplo, o Admin fecha a conexão ou a API cai
  no meio): as mensagens já reenviadas ficam na fila original e as demais continuam na DLQ,
  como na queda do broker.
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

- **FR-008**: Um Admin MUST poder reenviar as mensagens da DLQ de um consumidor para a fila
  original dele, sem o painel do broker, por uma rota administrativa da API protegida por
  token e restrita ao perfil Admin. Sem token, a resposta MUST ser 401; com outro perfil,
  403. Consumidor desconhecido ou filtro inválido MUST responder com erro de validação, e o
  broker fora do ar MUST responder com indisponibilidade, sem revelar detalhes internos.
- **FR-009**: O reenvio MUST aceitar filtros opcionais e combináveis: tipo de evento,
  período em que a mensagem foi para a DLQ e trecho do motivo. Sem filtro, todas as
  mensagens do consumidor são candidatas.
- **FR-010**: Cada mensagem reenviada MUST levar um contador de reenvios. Uma mensagem que já
  atingiu o limite configurável de reenvios MUST NOT ser reenviada: ela continua na DLQ e é
  contada como bloqueada.
- **FR-011**: A mensagem reenviada MUST voltar como uma entrega nova, com as tentativas do
  REQ-06 recomeçando do zero, e manter o identificador do evento, para o descarte de
  duplicados continuar valendo.
- **FR-012**: O reenvio MUST NOT perder nem duplicar mensagens: cada mensagem selecionada
  sai da DLQ só depois de estar na fila original, e as não selecionadas continuam na DLQ.
- **FR-013**: Cada reenvio MUST registrar em log o usuário autenticado que pediu, o
  consumidor, o filtro e as contagens (reenviadas, bloqueadas pelo limite e fora do filtro),
  e devolver essas contagens na resposta. A mensagem reenviada MUST levar também o autor e o
  instante do último reenvio.
- **FR-014**: Um reenvio MUST tratar no máximo um número configurável de mensagens por vez.

**Aviso da DLQ**

- **FR-015**: O processo de workers MUST conferir periodicamente o tamanho da DLQ de cada
  consumidor conhecido e registrar um aviso quando passar de um limite configurável, com o
  consumidor e a quantidade.

**Configuração e compatibilidade**

- **FR-016**: Intervalos, retenções, tamanhos de lote, limite de reenvios e limite do aviso
  MUST ser configuráveis, com padrões (Assumptions) e validação na partida, como as demais
  opções do processo de workers.
- **FR-017**: Os contratos de eventos v1 MUST permanecer inalterados; os cabeçalhos novos da
  mensagem reenviada são de transporte, não do envelope. O contrato HTTP v1 MUST mudar só por
  acréscimo: a rota administrativa de reenvio e os schemas dela, com testes de conformidade e
  de autorização (401 e 403), como exige a constituição.
- **FR-018**: A suíte MUST ter testes de integração, com banco e broker reais, da limpeza
  (o que é e o que não é apagado, lotes e duas instâncias), do reenvio (filtros, limite,
  ausência de perda e de duplicação, autor) e do aviso, e MUST continuar verde no CI.

### Key Entities *(include if feature involves data)*

- **Evento de saída** (REQ-06): conteúdo, tipo, tentativas, último erro, próxima tentativa e
  instante de envio. Elegível para limpeza só depois de enviado.
- **Evento processado** (REQ-06): consumidor, identificador do evento e instante do
  processamento.
- **Chave de idempotência** (REQ-05): cliente, chave, impressão do conteúdo, pedido, resposta
  e instante da criação.
- **Mensagem da DLQ** (REQ-06): corpo do evento e cabeçalhos com tentativas, último erro,
  motivo, instante da falha e, a partir deste requisito, contador de reenvios.
- **Reenvio**: quem pediu (o Admin autenticado), consumidor, filtro, instante e contagens.
  Fica no log e volta na resposta da rota.

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

## Assumptions

- **Valores padrão**, ajustáveis no plano: limpeza a cada 1 h, em lotes de 1 000 linhas;
  eventos enviados guardados por 7 dias; registros de processado por 7 dias (mínimo de 1
  dia); chaves de idempotência por 48 h (mínimo de 24 h); limite de 3 reenvios por mensagem;
  até 1 000 mensagens por reenvio; aviso da DLQ acima de 10 mensagens, conferido a cada 5
  minutos.
- A "janela de reentrega" cobre as novas tentativas do REQ-06 (segundos), a republicação do
  outbox depois de uma queda (segundos a minutos) e o reenvio da DLQ (dias). Uma mensagem da
  DLQ nunca foi processada com sucesso por aquele consumidor (é por isso que está lá), então
  o reenvio não depende do registro de processado; os consumidores ainda se protegem pela
  regra de pedido não apto.
- A limpeza e o aviso rodam no processo de workers, como a reconciliação; o primeiro ciclo
  acontece logo após a subida.
- O reenvio vale para as DLQs dos consumidores conhecidos (estoque, pagamento e entrega).
- O reenvio é síncrono: a requisição do Admin termina quando as mensagens selecionadas (até o
  máximo por vez) foram reenviadas, e a resposta traz as contagens. A API passa a abrir uma
  conexão com o broker só para essa rota; a criação, a consulta e a mudança de status de
  pedidos continuam sem depender do broker.
- Só o perfil Admin reenvia; o OperadorLogistica não, porque o reenvio mexe na infraestrutura
  de mensageria, não num pedido.
- A purga de mensagens da DLQ (apagar sem reenviar) continua pelo painel do broker e fica fora
  do escopo.
- Ficam fora do escopo: métricas e alertas além dos logs, arquivamento dos dados apagados e
  a visibilidade dos cancelamentos (#29).
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
