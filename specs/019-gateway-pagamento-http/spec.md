# Feature Specification: Gateway de pagamento por HTTP com API simulada

**Feature Branch**: `feature/req-19-gateway-pagamento-http`

**Requisito**: REQ-19 (GitHub issue #46 — "[Pagamentos] Gateway de pagamento por HTTP com API simulada (latência, indisponibilidade, timeout e retry)")

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "REQ-19 (issue #46): trocar o gateway de pagamento em memória por uma API de pagamento simulada acessada por HTTP, com latência variável, indisponibilidade, recusa de chamadas e timeout, e preparar o OMS (consumidor de pagamento e processador de estornos) para classificar os erros, tentar de novo com segurança (idempotência do lado da API, consulta pela chave, circuit breaker) e instrumentar o HTTP de saída. Escopo completo no corpo da issue #46."

## Contexto

O processo de workers cobra o Cliente na etapa de pagamento (REQ-09), cobra de novo pela
reconciliação quando o pedido fica parado (REQ-12) e estorna os pedidos pagos que foram
cancelados (REQ-13). As três chamadas passam hoje por um gateway simulado **dentro do próprio
processo de workers**: o resultado é sorteado em memória, na hora, sem rede. Por isso:

- a chamada nunca demora, nunca fica sem resposta e nunca é recusada por indisponibilidade:
  só aprova, recusa ou, nos testes, falha;
- as chaves de idempotência (o pedido) ficam na memória do processo de workers e se perdem
  quando ele reinicia; um serviço de pagamento real guarda as chaves do lado dele;
- não existe o caso mais delicado de uma integração real: a cobrança é feita, mas a resposta
  não chega. Repetir a chamada só é seguro se quem a recebe reconhecer a repetição;
- duas limitações documentadas dependem de "um gateway real": a cobrança não pode ser
  consultada pela chave (`docs/fluxos/05-pagamento.md`) e as chaves se perdem no reinício
  (`docs/fluxos/10-estorno.md`);
- não há chamada de saída nos traces (`docs/fluxos/12-rastreabilidade-e-metricas.md`).

Este requisito põe o serviço de pagamento **fora do processo de workers**, numa API simulada
acessada pela rede, que se comporta como um serviço externo real: responde em tempos
variados, às vezes fica indisponível, recusa chamadas e deixa de responder. Do lado do OMS,
quem espera a resposta passa a classificar cada falha e a tentar de novo de forma segura, sem
nunca cobrar ou estornar o Cliente duas vezes.

## Clarifications

### Session 2026-11-02 (specify)

- Q: O comportamento da API simulada pode ser mudado com ela no ar, ou só na partida? → A: Os
  dois. A configuração define o comportamento na partida, e uma rota de controle da própria API
  simulada muda as chances e força um modo (por exemplo, "fora do ar") sem reiniciar, para a
  demonstração e os testes. A rota existe só na API simulada e exige uma credencial de
  controle própria (FR-007a).
- Q: Com o circuito aberto, o que acontece com as mensagens de pagamento que chegam? → A: O
  consumo da fila de pagamento é pausado enquanto o circuito está aberto: as mensagens ficam na
  fila, sem passar pelas novas tentativas nem ir para a DLQ, e são cobradas quando o circuito
  fecha (FR-016a).

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o Cliente, que não pode ser cobrado duas vezes nem ficar
cobrado sem registro; a operação, que precisa enxergar quando o serviço de pagamento está
lento ou fora do ar e confiar que os pedidos não ficam sem desfecho; e quem demonstra e testa
o projeto, que precisa reproduzir as falhas de uma integração real sob demanda.

### User Story 1 - O pagamento passa por um serviço externo, pela rede (Priority: P1)

A cobrança, a cobrança pela reconciliação e o estorno passam a ser pedidos a uma API de
pagamento simulada, separada do OMS, por requisições de rede. Com a API respondendo bem, o
fluxo do pedido continua igual ao de hoje: aprovado vai para `Processando`, recusado vai para
`PagamentoRejeitado` com o estoque devolvido, e o estorno pendente vira "estornado".

**Why this priority**: É a base do requisito; sem a API separada, nenhuma das falhas reais
existe para ser tratada.

**Independent Test**: Com o ambiente local no ar (banco, broker e API de pagamento), criar um
pedido e conferir que ele chega a `Processando` com o identificador de transação gerado pela
API; com a API configurada para recusar sempre, conferir `PagamentoRejeitado` e o estoque
devolvido; cancelar um pedido pago e conferir o pagamento "estornado".

**Acceptance Scenarios**:

1. **Given** um pedido com estoque reservado e a API de pagamento respondendo, **When** o
   consumidor de pagamento o trata, **Then** a API recebe uma requisição de cobrança com o
   valor do pedido e a chave do pedido, e o resultado é gravado como hoje (aprovação ou
   recusa, status, estoque e evento).
2. **Given** um pedido cancelado com estorno pendente, **When** o processo de workers trata
   os estornos, **Then** a API recebe uma requisição de estorno referente à transação da
   cobrança, e o pagamento passa para "estornado" com o identificador devolvido pela API.
3. **Given** um pedido parado na etapa de pagamento, **When** a reconciliação o trata,
   **Then** a cobrança também passa pela API, com a mesma chave do pedido.
4. **Given** a API de pagamento, **When** ela recebe uma requisição sem a credencial
   configurada do OMS, **Then** recusa a chamada como não autorizada, e o OMS trata isso como
   erro de configuração (FR-014), não como recusa do pagamento.

---

### User Story 2 - A API simulada se comporta como um serviço real (Priority: P1)

A API simulada não responde sempre igual nem na hora: o tempo de resposta varia como numa API
real, a maioria rápida e algumas lentas; às vezes ela está indisponível, recusa a chamada por
excesso de requisições ou por erro interno, ou não responde a tempo. Também pode efetivar a
cobrança e não entregar a resposta. As chances de cada comportamento são configuráveis, e os
testes podem fixá-los para nunca depender de sorte.

**Why this priority**: É o que o usuário pediu na issue: o valor do requisito está nas
falhas, não no transporte.

**Independent Test**: Chamar a API simulada diretamente várias vezes com as chances padrão e
conferir a variação dos tempos e a presença de cada tipo de resposta; com o modo
determinístico, conferir que cada comportamento pode ser forçado.

**Acceptance Scenarios**:

1. **Given** a API com as chances padrão, **When** ela recebe uma série de cobranças,
   **Then** os tempos de resposta variam dentro de uma faixa configurável, com a maior parte
   rápida e uma parte lenta, e não um valor fixo.
2. **Given** a API configurada para indisponibilidade, **When** ela recebe uma chamada,
   **Then** responde que o serviço está indisponível, sem processar a cobrança.
3. **Given** a API configurada para limitar a taxa, **When** ela recebe uma chamada, **Then**
   recusa por excesso de requisições, informando em quanto tempo tentar de novo, sem
   processar a cobrança.
4. **Given** a API configurada para erro interno, **When** ela recebe uma chamada, **Then**
   responde com erro do servidor, sem processar a cobrança.
5. **Given** a API configurada para não responder, **When** ela recebe uma chamada, **Then**
   segura a resposta além do tempo que o OMS espera, sem processar a cobrança.
6. **Given** a API configurada para "processar e não responder", **When** ela recebe uma
   cobrança, **Then** efetiva a cobrança, guarda o resultado pela chave e não entrega a
   resposta a tempo.
7. **Given** a API com uma recusa de negócio sorteada, **When** ela recebe uma cobrança,
   **Then** responde que o pagamento foi recusado, com o motivo, e guarda a recusa pela chave.
8. **Given** a API em modo determinístico, **When** os testes pedem um comportamento,
   **Then** a API o reproduz sempre, sem sorteio.
9. **Given** a API no ar com as chances da configuração, **When** quem demonstra ou o teste
   muda as chances ou força um modo pela rota de controle, com a credencial de controle,
   **Then** as chamadas seguintes seguem o comportamento novo, sem reiniciar a API; sem a
   credencial de controle, a mudança é recusada e nada muda.
10. **Given** um modo forçado pela rota de controle, **When** ele é desfeito pela mesma rota,
   **Then** a API volta às chances da configuração.

---

### User Story 3 - Quem espera o pagamento classifica os erros e tenta de novo com segurança (Priority: P1)

O consumidor de pagamento, a reconciliação e o processador de estornos deixam de tratar toda
falha do mesmo jeito. Uma recusa de negócio é um resultado, não uma falha. Uma falha
passageira (indisponível, excesso de requisições, erro interno, sem resposta a tempo, conexão
recusada) é tentada de novo algumas vezes, com espera crescente e aleatória e respeitando o
tempo pedido pela API, sempre com a mesma chave. Um erro de contrato ou de configuração não é
repetido, e vai para o log. Nenhuma combinação de falhas cobra ou estorna o Cliente duas
vezes.

**Why this priority**: É a outra metade do pedido da issue: o lado de quem espera o
pagamento precisa lidar com os erros, e a segurança do dinheiro do Cliente depende disso.

**Independent Test**: Com a API em modo determinístico, forçar cada tipo de falha e conferir
o desfecho do pedido, o número de chamadas recebidas pela API e que a cobrança efetiva
aconteceu no máximo uma vez.

**Acceptance Scenarios**:

1. **Given** a API recusando a primeira chamada como indisponível e aprovando a segunda,
   **When** o consumidor cobra o pedido, **Then** ele espera e tenta de novo com a mesma
   chave, e o pedido termina com o pagamento aprovado, com uma única cobrança efetiva.
2. **Given** a API recusando por excesso de requisições com um tempo de espera informado,
   **When** o consumidor tenta de novo, **Then** espera pelo menos o tempo informado, dentro
   do limite total de espera da chamada.
3. **Given** a API recusando o pagamento (recusa de negócio), **When** o consumidor recebe a
   resposta, **Then** não tenta de novo: o pedido vai para `PagamentoRejeitado`, como hoje.
4. **Given** a API respondendo um erro de contrato (requisição rejeitada como inválida, ou
   resposta que o OMS não entende), **When** o consumidor recebe a resposta, **Then** não
   repete a chamada, registra o erro, e a mensagem segue as novas tentativas e a DLQ da base
   de consumo.
5. **Given** a API falhando em todas as tentativas da chamada, **When** as tentativas se
   esgotam, **Then** nada é gravado no pedido, e o caso segue o caminho de falha de hoje
   (novas tentativas da base de consumo, DLQ e reconciliação), conforme o FR-011.
6. **Given** a API levando mais que o tempo limite numa tentativa, **When** o tempo se
   esgota, **Then** o OMS abandona a espera daquela tentativa e trata como falha passageira.
7. **Given** um estorno pendente e a API falhando, **When** o processo de workers tenta o
   estorno, **Then** aplica a mesma classificação e as mesmas tentativas; esgotadas, o estorno
   continua pendente e é tentado na rodada seguinte, como hoje (REQ-13).

---

### User Story 4 - Resultado incerto: a cobrança pode ter acontecido sem a resposta chegar (Priority: P1)

Quando a chamada termina sem resposta (tempo esgotado ou conexão caída depois do envio), o
OMS não sabe se a cobrança aconteceu. Antes de decidir, ele consulta a API pela chave do
pedido, ou repete a chamada com a mesma chave, e recebe o resultado original. A API guarda as
chaves do lado dela, então isso vale também depois de o processo de workers reiniciar.

**Why this priority**: É o caso que transforma uma falha técnica em dinheiro cobrado a mais
ou cobrado sem registro.

**Independent Test**: Com a API forçada a "processar e não responder" na primeira chamada,
cobrar um pedido e conferir que ele termina com o pagamento aprovado e a mesma transação, e
que a API registrou uma única cobrança efetiva; repetir reiniciando o processo de workers
entre as tentativas.

**Acceptance Scenarios**:

1. **Given** uma cobrança efetivada pela API cuja resposta não chegou, **When** o OMS tenta
   de novo, **Then** recebe o resultado original pela chave, e o pedido grava a mesma
   transação, sem segunda cobrança.
2. **Given** o processo de workers reiniciado entre a cobrança efetivada e a nova tentativa,
   **When** a mensagem é reentregue, **Then** a API ainda reconhece a chave e devolve o
   resultado original.
3. **Given** uma cobrança efetivada sem resposta e o pedido cancelado pela operação antes da
   reentrega, **When** a reentrega ou a reconciliação encontra o pedido cancelado sem
   pagamento, **Then** o OMS consulta a cobrança pela chave e, se houve cobrança aprovada,
   registra o pagamento com o estorno pendente (como no REQ-13, R-07); se não houve, nada é
   feito.
4. **Given** um estorno efetivado pela API cuja resposta não chegou, **When** o estorno é
   tentado de novo, **Then** a API devolve o mesmo estorno, e o pagamento passa para
   "estornado" uma única vez.
5. **Given** a consulta pela chave também falha, **When** o OMS não consegue saber o
   resultado, **Then** trata como falha passageira e não grava nada; a decisão fica para a
   próxima tentativa.

---

### User Story 5 - Com o serviço fora do ar, o OMS para de insistir (Priority: P2)

Quando a API de pagamento falha em sequência, o OMS deixa de chamá-la por um tempo, em vez
de esgotar as tentativas de cada pedido contra um serviço que não responde. Passado o tempo,
ele testa com poucas chamadas e volta ao normal quando a API responde.

**Why this priority**: Protege o próprio OMS e o serviço externo numa queda longa, mas o
fluxo continua correto sem isso (só mais lento e mais ruidoso).

**Independent Test**: Com a API fora do ar, criar vários pedidos e conferir que, depois de
um número de falhas, o OMS deixa de chamar a API (sem novas requisições recebidas) e registra
o circuito aberto; com a API de volta, conferir que os pedidos são cobrados.

**Acceptance Scenarios**:

1. **Given** uma sequência de falhas passageiras acima do limite configurado, **When** chega
   a próxima cobrança, **Then** o OMS não chama a API, trata como falha passageira, e registra
   que o circuito abriu.
2. **Given** o circuito aberto e o tempo de pausa passado, **When** chega a próxima chamada,
   **Then** o OMS testa a API; se ela responde, o circuito fecha e as chamadas voltam ao
   normal; se falha, a pausa recomeça.
3. **Given** o circuito aberto, **When** os pedidos de pagamento chegam, **Then** o consumo da
   fila de pagamento fica pausado: as mensagens esperam na fila, sem passar pelas novas
   tentativas da base nem ir para a DLQ, e a pausa é registrada em log.
4. **Given** o consumo pausado, **When** o circuito fecha, **Then** o consumo da fila de
   pagamento é retomado sozinho, e as mensagens que esperavam são cobradas.
5. **Given** a mensagem que estava em tratamento quando o circuito abriu, **When** a chamada
   dela falha por circuito aberto, **Then** ela volta para a fila sem contar como nova
   tentativa e sem ir para a DLQ.
6. **Given** o circuito aberto, **When** a rodada de estornos roda, **Then** os estornos
   continuam pendentes, sem chamada e sem contar tentativa falha, e são tentados na rodada
   seguinte.
7. **Given** o circuito aberto, **When** a reconciliação encontra pedidos parados na etapa de
   pagamento, **Then** não chama a API e trata como falha do ciclo, como hoje com o gateway
   fora do ar (REQ-12).
8. **Given** o consumo de pagamento pausado, **When** chegam mensagens para os outros
   consumidores (estoque, entrega, notificações), **Then** eles continuam consumindo
   normalmente.

---

### User Story 6 - A operação enxerga a chamada ao pagamento (Priority: P2)

Cada chamada à API de pagamento aparece no trace do pedido, com o tempo, o resultado e as
tentativas, e o contexto do trace chega à API simulada. Os logs e as métricas mostram a
latência, as falhas por tipo, as tentativas, os resultados incertos e o circuito aberto.

**Why this priority**: Sem isso, uma lentidão ou queda do serviço de pagamento só aparece
como pedidos parados.

**Independent Test**: Com a telemetria ligada, criar um pedido com uma falha forçada seguida
de sucesso e conferir, no trace do pedido, as duas tentativas da chamada de saída, e nos logs
os registros da falha passageira e da nova tentativa.

**Acceptance Scenarios**:

1. **Given** a telemetria ligada, **When** o consumidor cobra um pedido, **Then** o trace do
   pedido tem a chamada de saída à API de pagamento, com cada tentativa, o tempo e o código da
   resposta, e a API simulada recebe o contexto do trace.
2. **Given** uma falha passageira, um resultado incerto, um erro de contrato ou o circuito
   aberto, **When** acontece, **Then** há um log próprio, com o pedido, a operação (cobrança
   ou estorno), o tipo da falha e a tentativa, sem dados pessoais, identificador de transação
   ou credencial.
3. **Given** a telemetria ligada, **When** as chamadas acontecem, **Then** há métricas da
   duração das chamadas e da contagem por resultado (aprovado, recusado, falha passageira por
   tipo, erro de contrato, incerto, circuito aberto).

---

### Edge Cases

- **Resposta lenta, mas dentro do tempo limite**: é sucesso; nada é repetido.
- **Resposta chega depois do tempo limite**: o OMS já desistiu daquela tentativa; a próxima
  tentativa, com a mesma chave, recebe o resultado guardado pela API.
- **Duas mensagens do mesmo pedido ao mesmo tempo** (reentrega e reconciliação): as duas
  chamadas usam a mesma chave; a API efetiva uma única cobrança e devolve o mesmo resultado
  para as duas; a gravação no pedido segue a concorrência otimista de hoje.
- **Mesma chave com valor diferente**: a API recusa a chamada como conflito, sem cobrar; o OMS
  trata como erro de contrato (não repete e registra), porque o valor do pedido não muda.
- **Chamada concorrente com a mesma chave ainda em processamento na API**: a API responde que
  a operação está em andamento, e o OMS trata como falha passageira.
- **Estorno de cobrança que a API não conhece** (por exemplo, a API perdeu os dados por ter
  sido recriada): a API recusa o estorno; o OMS trata como recusa do estorno, que fica
  pendente com o motivo no log, como uma recusa hoje (REQ-13, FR-003).
- **API recriada (chaves perdidas) com cobranças já gravadas no OMS**: o OMS não cobra de novo
  pedidos já pagos (idempotência pelo estado, como hoje); só as chamadas em andamento perdem a
  proteção da chave. A API simulada guarda as chaves de forma durável para que reiniciar não
  apague o histórico.
- **Credencial errada ou ausente**: toda chamada é recusada como não autorizada; o OMS não
  repete, registra erro de configuração, e os pedidos seguem o caminho de falha (FR-011).
- **Endereço da API errado ou DNS falhando**: falha de conexão, tratada como passageira.
- **Cancelamento do processo** (parada do processo de workers no meio de uma chamada): a
  chamada é abandonada, nada é gravado, e a mensagem volta para a fila, como hoje.
- **Tempo total de tentativas maior que o tempo que a mensagem pode ficar em processamento**:
  as tentativas de uma chamada têm um limite total de espera, para o consumo não ficar preso.
- **Recusa de negócio repetida**: a mesma chave devolve a mesma recusa; não há nova tentativa
  de cobrança depois de uma recusa, como hoje.

## Requirements *(mandatory)*

### Functional Requirements

**API de pagamento simulada**

- **FR-001**: O projeto MUST ter uma API de pagamento simulada, separada do OMS e executada
  como um serviço próprio no ambiente local e no CI, que o processo de workers chama pela rede
  para cobrar, estornar e consultar uma cobrança ou um estorno pela chave.
- **FR-002**: A API simulada MUST exigir uma credencial de acesso configurada nas duas pontas,
  e MUST recusar como não autorizada a chamada sem ela. A credencial MUST NOT aparecer em logs
  nem em traces.
- **FR-003**: O tempo de resposta da API simulada MUST variar a cada chamada, sorteado dentro
  de uma faixa configurável que tenha a maior parte das respostas rápida e uma parte lenta,
  como uma API real; com os valores padrão (Assumptions), a maior parte responde bem abaixo do
  tempo limite do OMS e uma parte pequena passa dele.
- **FR-004**: A API simulada MUST poder, com chances configuráveis e independentes: aprovar,
  recusar o pagamento (recusa de negócio, com motivo), responder que está indisponível,
  recusar por excesso de requisições informando quando tentar de novo, responder erro
  interno, não responder a tempo sem processar, e processar a cobrança ou o estorno sem
  responder a tempo. Só a aprovação, a recusa de negócio e o "processar sem responder" efetivam
  a operação.
- **FR-005**: A API simulada MUST ser idempotente pela chave do pedido, guardando as chaves do
  lado dela de forma que sobrevivam ao reinício dela e do processo de workers: a mesma chave
  MUST devolver o mesmo resultado e o mesmo identificador, sem efetivar de novo; a mesma chave
  com valor diferente MUST ser recusada como conflito; uma chave ainda em processamento MUST
  ser respondida como "em andamento".
- **FR-006**: A API simulada MUST permitir consultar uma cobrança e um estorno pela chave,
  respondendo o resultado guardado ou que a chave não existe.
- **FR-007**: A API simulada MUST ter um modo determinístico para os testes, que reproduz
  qualquer comportamento do FR-004 sem sorteio e sem latência artificial.
- **FR-007a**: A API simulada MUST ter uma rota de controle que muda as chances do FR-004 e
  força ou desfaz um modo (por exemplo, "fora do ar", "sem resposta", "processar sem
  responder") com ela no ar, sem reiniciar. A rota MUST exigir uma credencial de controle
  própria, diferente da credencial do OMS, e MUST recusar a mudança sem ela. Desfeito o modo
  forçado, valem as chances da configuração.
- **FR-008**: A API simulada MUST ter verificação de saúde, para o ambiente local e o CI só
  começarem quando ela estiver pronta.

**Lado do OMS: chamada, classificação e novas tentativas**

- **FR-009**: O consumidor de pagamento, a cobrança pela reconciliação e o processador de
  estornos MUST chamar a API de pagamento pela rede, mantendo o comportamento de negócio de
  hoje: as regras de aptidão, os status, a devolução do estoque, os eventos e o estorno
  pendente não mudam.
- **FR-010**: Toda resposta ou falha da chamada MUST ser classificada em: **resultado**
  (aprovação ou recusa de negócio da cobrança; confirmação ou recusa do estorno), **falha
  passageira** (indisponível, excesso de requisições, erro interno, "em andamento", tempo
  esgotado, falha de conexão, circuito aberto), **resultado incerto** (tempo esgotado ou
  conexão perdida depois do envio da requisição) ou **erro permanente** (requisição rejeitada
  como inválida, conflito de chave, não autorizado, resposta fora do contrato).
- **FR-011**: Uma falha passageira MUST ser tentada de novo dentro da mesma chamada, com a
  mesma chave, até um número configurável de vezes, com espera crescente e aleatória entre as
  tentativas, respeitando o tempo pedido pela API quando ela o informa, e com um limite total
  de espera por chamada. Esgotadas as tentativas, nada MUST ser gravado no pedido, e o caso
  MUST seguir o caminho de falha que já existe: novas tentativas e DLQ da base de consumo
  (REQ-06) e reconciliação (REQ-12) para a cobrança; a rodada seguinte (REQ-13) para o
  estorno.
- **FR-012**: Cada tentativa MUST ter um tempo limite configurável; passado o tempo, o OMS MUST
  abandonar a espera e tratar como falha passageira ou resultado incerto (FR-010).
- **FR-013**: Num resultado incerto, o OMS MUST NOT gravar nada sem antes saber o resultado: a
  tentativa seguinte MUST usar a mesma chave (ou consultar pela chave) e só o resultado
  devolvido pela API decide o desfecho. O mesmo vale para o estorno.
- **FR-014**: Um erro permanente MUST NOT ser repetido dentro da chamada; MUST ir para o log
  como erro, com o tipo, e a falha MUST seguir o caminho de falha de hoje (FR-011), sem gravar
  nada no pedido.
- **FR-015**: Quando a reentrega ou a reconciliação encontra o pedido cancelado sem pagamento,
  o OMS MUST consultar a cobrança pela chave; se houver cobrança aprovada, MUST registrar o
  pagamento com o estorno pendente, como no REQ-13 (R-07); se não houver, ou a chave não
  existir, nada MUST ser feito. Uma falha nessa consulta MUST ser tratada como falha
  passageira.
- **FR-016**: Uma sequência de falhas passageiras acima de um limite configurável MUST abrir um
  circuito que suspende as chamadas à API por um tempo configurável; passado o tempo, poucas
  chamadas de teste MUST decidir se o circuito fecha ou se a pausa recomeça. Uma recusa de
  negócio MUST NOT contar como falha para o circuito.
- **FR-016a**: Enquanto o circuito estiver aberto, o consumo da fila de pagamento MUST ficar
  pausado, com as mensagens esperando na fila, e MUST ser retomado sozinho quando o circuito
  fechar. A mensagem cuja chamada falhou por circuito aberto MUST voltar para a fila sem contar
  como nova tentativa e sem ir para a DLQ. Os demais consumidores MUST continuar consumindo.
  Com o circuito aberto, o processador de estornos MUST pular as chamadas sem contar tentativa
  falha, e a reconciliação MUST tratar a etapa de pagamento como falha do ciclo, como hoje.
  A pausa e a retomada MUST ser registradas em log.
- **FR-017**: Em nenhuma combinação de falhas, novas tentativas, reentregas, reconciliação,
  reinício do processo de workers ou instâncias simultâneas o OMS MUST cobrar ou estornar o
  mesmo pedido mais de uma vez na API.
- **FR-018**: O endereço da API, a credencial, o tempo limite por tentativa, o número de
  tentativas, as esperas, o limite total por chamada e os parâmetros do circuito MUST ser
  configuráveis, com valores padrão (Assumptions) e validação na partida, como as demais
  opções do processo de workers; um valor inválido MUST impedir a partida, nomeando a chave
  sem mostrar o valor.
- **FR-019**: Uma chamada lenta ou sem resposta à API MUST NOT segurar recursos do banco do
  OMS (transação aberta, bloqueios) por mais que o tempo total da chamada.

**Observabilidade**

- **FR-020**: Cada tentativa de chamada à API MUST aparecer no trace do pedido como uma
  chamada de saída, com a operação, o tempo, o código da resposta e o número da tentativa, e
  o contexto do trace MUST ser propagado para a API simulada.
- **FR-021**: Falha passageira (com tipo e tentativa), resultado incerto, consulta pela chave,
  erro permanente, circuito aberto e circuito fechado MUST ser registrados em log com
  identificadores próprios, sem dados pessoais, identificadores de transação ou credenciais.
- **FR-022**: MUST haver métricas da duração das chamadas à API e da contagem de chamadas por
  operação e por resultado (FR-010), além do estado do circuito.

**Compatibilidade e qualidade**

- **FR-023**: Os contratos HTTP v1 da API do OMS e os contratos de eventos v1 MUST permanecer
  inalterados. O identificador de transação passa a vir da API simulada, com o mesmo papel e o
  mesmo campo de hoje.
- **FR-024**: A suíte MUST ter testes de integração, com a API simulada real, banco e broker,
  de: aprovação, recusa, latência alta dentro do limite, indisponível, excesso de requisições
  com tempo pedido, erro interno, sem resposta, processar sem responder (com e sem reinício do
  processo de workers), erro permanente, consulta pela chave no pedido cancelado, circuito
  aberto e fechado, e o estorno com falhas, conferindo em todos que a API efetivou no máximo
  uma cobrança e um estorno por pedido. MUST haver testes unitários da classificação, da
  política de tentativas e da API simulada, e a suíte MUST continuar verde no CI.
- **FR-025**: Os documentos de fluxo de pagamento, estorno, reconciliação e rastreabilidade
  MUST ser atualizados, e as limitações resolvidas (chaves em memória e cobrança não
  consultável) MUST sair das seções de limitações, com a issue #46 no lugar.

### Key Entities *(include if feature involves data)*

- **API de pagamento simulada**: serviço separado do OMS que cobra, estorna e consulta pela
  chave, com comportamento de falha configurável e chaves guardadas de forma durável.
- **Operação de pagamento na API** (cobrança ou estorno): a chave (o pedido), o valor, o
  resultado (aprovado, recusado, confirmado), o identificador gerado pela API, o motivo da
  recusa e o instante; no máximo uma por chave e por tipo.
- **Classificação da chamada**: resultado, falha passageira, resultado incerto ou erro
  permanente (FR-010).
- **Política de novas tentativas**: tempo limite por tentativa, número de tentativas, espera
  crescente e aleatória, limite total por chamada.
- **Circuito**: fechado, aberto ou em teste; limite de falhas e tempo de pausa.
- **Pagamento** (REQ-09, REQ-13): sem situação nova; o identificador de transação e o do
  estorno passam a ser os da API simulada.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Com a API simulada nas chances padrão (incluindo latência variável e falhas),
  100% dos pedidos de uma carga de demonstração terminam com um desfecho de pagamento
  (aprovado, recusado ou cancelado pela reconciliação), e nenhum fica parado sem desfecho além
  do prazo de esgotamento da reconciliação.
- **SC-002**: Em 100% das execuções com falhas forçadas, reentregas, reinício do processo de
  workers e instâncias simultâneas, a API simulada registra no máximo uma cobrança e um
  estorno efetivos por pedido.
- **SC-003**: Uma falha passageira isolada (uma tentativa falha seguida de sucesso) não leva a
  mensagem para a nova tentativa da base de consumo: o pedido é cobrado na mesma chamada, e o
  tempo do pedido criado ao "pagamento aprovado" cresce no máximo o tempo das esperas
  configuradas.
- **SC-004**: Com a API fora do ar, o número de requisições que ela recebe durante a queda cai
  depois que o circuito abre, nenhuma mensagem de pagamento vai para a DLQ por causa da queda,
  e, com a API de volta, os pedidos pendentes são cobrados sem intervenção manual.
- **SC-005**: 100% das cobranças efetivadas sem resposta são gravadas no pedido com o
  identificador original, ou, com o pedido cancelado, registradas com o estorno pendente.
- **SC-006**: Com as chances padrão, o tempo do fluxo normal (do pedido criado ao "entrega
  criada") continua dentro da meta do REQ-10 para 95% dos pedidos (10 segundos no ambiente de
  demonstração), e a suíte continua 100% verde no CI.
- **SC-007**: Toda chamada à API de pagamento pode ser encontrada no trace do pedido, com as
  tentativas, a partir do identificador do pedido.

## Assumptions

- A API simulada fica no próprio repositório, como um serviço separado do OMS, e roda no
  ambiente local (como o banco e o broker) e no CI. Não é uma API do OMS: não usa a
  autenticação por token do OMS; a credencial do FR-002 é uma chave compartilhada entre os
  dois serviços, como num gateway real.
- **Valores padrão**, ajustáveis no plano: latência da maior parte das respostas entre algumas
  dezenas e poucas centenas de milissegundos, com uma cauda de alguns segundos; chances padrão
  de 90% de aprovação e de poucos por cento para cada falha; tempo limite de cerca de 3 s por
  tentativa; até 3 tentativas por chamada, com espera crescente e aleatória e limite total de
  cerca de 10 s; circuito abrindo após algumas falhas seguidas e pausando por cerca de 30 s.
- As novas tentativas dentro da chamada (FR-011) somam às da base de consumo (REQ-06) e da
  reconciliação (REQ-12); o plano ajusta os valores para que o tempo total de uma mensagem em
  processamento continue limitado e a DLQ continue sendo o último recurso.
- A chamada à API pode sair da transação do consumidor, ou continuar nela com o tempo limitado
  pelo FR-019; a escolha fica para o plano.
- O gateway em memória de hoje pode continuar como opção de configuração para testes que não
  precisam da rede; o ambiente local e o CI usam a API simulada.
- A API simulada guarda as chaves num armazenamento próprio, não no banco do OMS, para se
  comportar como um serviço externo.
- A recusa de negócio continua sem nova tentativa de cobrança (como no REQ-09): o pedido
  termina `PagamentoRejeitado`.
- Ficam fora do escopo: integração com um gateway real, confirmação assíncrona da cobrança por
  webhook do gateway (pagamento "em análise"), nova tentativa de cobrança depois de recusa,
  estorno parcial e alteração dos contratos v1.
- O ambiente local do REQ-02, agora com a API simulada, é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
