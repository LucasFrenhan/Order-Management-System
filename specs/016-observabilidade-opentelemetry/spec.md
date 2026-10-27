# Feature Specification: Rastreabilidade de ponta a ponta do pedido (traces, métricas e logs correlacionados)

**Feature Branch**: `feature/req-16-observabilidade-opentelemetry`

**Requisito**: REQ-16 (GitHub issue #37 — "[Observabilidade] Estudo: rastreabilidade com OpenTelemetry (traces, métricas e logs correlacionados)")

**Created**: 2026-10-26

**Status**: Draft

**Input**: User description: "REQ-16 (issue #37): rastreabilidade com OpenTelemetry, na decisão "adotar parcialmente" registrada em specs/016-observabilidade-opentelemetry/research.md. Escopo: traces de ponta a ponta (API → outbox → RabbitMQ → consumidores de estoque, pagamento e entrega), propagação pelo outbox e pelo reenvio da DLQ, spans das rotinas periódicas com link ao pedido, logs correlacionados sem mudar EventIds, 6 métricas próprias, exportação OTLP com Aspire Dashboard como contêiner no compose; latência por etapa adiada. Envelope v1 e API pública não mudam. Critérios de aceite da issue #37."

## Contexto

Hoje a observabilidade do OMS se resume a logs estruturados com IDs de evento. Para entender
por que um pedido travou, por exemplo um pagamento que foi para a DLQ e depois foi reenviado, a
operação procura nos logs da API e do processo de workers e remonta a sequência à mão. Os números
que importam (pendentes no outbox, tamanho da DLQ, mensagens com falha) só existem como texto de
log, ou nem isso. O documento `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md` registra a limitação
"Não há métricas nem alertas além dos logs".

O estudo da issue #37 ([research.md](./research.md)) concluiu por **adotar parcialmente** o
padrão aberto de telemetria (traces, métricas e logs exportados por um protocolo neutro em
fornecedor). Este requisito materializa essa decisão.

O caminho de um pedido atravessa dois processos e um intervalo assíncrono:

```text
POST /api/pedidos → gravação (pedido + evento no outbox) → [espera no outbox]
  → despachante publica → broker → consumidor de estoque → outbox → broker
  → consumidor de pagamento → outbox → broker → consumidor de entrega
```

com novas tentativas, DLQ, reenvio pedido pelo Admin, reconciliação, estorno assíncrono e
limpeza por fora. É justamente o trecho que nenhum mecanismo pronto acompanha sozinho: o outbox
quebra a cadeia, porque a publicação acontece depois, em outro processo.

## Clarifications

### Session 2026-10-26 (specify)

Decisões herdadas do estudo e confirmadas pelo usuário ao aprovar o `research.md`:

- Q: Adotar, adotar parcialmente ou não adotar? → A: Adotar parcialmente: traces de ponta a
  ponta, logs correlacionados e seis métricas próprias; a latência por etapa do pedido fica para
  depois (o trace já a mostra pedido a pedido).
- Q: Como o trace atravessa o outbox? → A: O contexto de rastreamento é guardado junto do evento
  pendente e retomado na publicação, viajando como cabeçalho de transporte da mensagem, como
  `x-tentativas` e `x-reenvios`. O envelope v1 não muda.
- Q: Como as rotinas sem requisição de origem se ligam ao pedido? → A: Cada rodada é um trace
  próprio; cada pedido tratado nela é um passo com o identificador do pedido e uma ligação para o
  trace de criação dele.
- Q: Onde ver os dados localmente? → A: Num painel de telemetria em contêiner, dentro do
  `docker-compose.yml`, sem trocar o compose pelo Aspire (a demo continua exigindo só o Docker).
- Q: Aceita acoplar a um fornecedor? → A: Não. A exportação usa o protocolo aberto, e o destino
  se troca por configuração.

### Session 2026-10-26 (clarify)

- Q: O painel local sobe por padrão no perfil de demonstração ou só num perfil próprio? → A:
  Nos dois: no perfil `demo` (a API e o processo de workers da demo exportam para ele) e no
  perfil `observabilidade`, para quem roda a API pelo SDK.
- Q: Com que frequência a métrica do tamanho da DLQ é atualizada? → A: A DLQ passa a ser lida a
  cada 1 min para a métrica; o aviso 5011 continua no máximo a cada 5 min, como hoje, sem
  repetir o log a cada leitura.
- Q: Com várias instâncias do processo de workers, quem publica as métricas de estado (outbox e
  DLQ)? → A: Toda instância lê e publica, identificada pela instância; no painel, usa-se o maior
  valor entre elas. Os contadores (mensagens, execuções, linhas apagadas) são somados.
- Q: Os passos de banco mostram o texto da consulta? → A: Sim, o texto parametrizado (por
  exemplo, `SELECT … WHERE Id = @p0`), nunca os valores dos parâmetros, em todos os ambientes.

## User Scenarios & Testing *(mandatory)*

Os usuários deste requisito são a **operação** (quem investiga um pedido travado ou acompanha a
saúde da mensageria) e os **desenvolvedores** (quem reproduz um problema localmente ou na demo).
Nenhum Cliente percebe diferença: a API pública e os eventos não mudam.

### User Story 1 - Seguir um pedido de ponta a ponta num único trace (Priority: P1)

A operação parte de um pedido e vê, numa única linha do tempo, a requisição de criação, a
gravação, o tempo de espera no outbox, a publicação e o processamento por cada consumidor
(estoque, pagamento e entrega), incluindo as novas tentativas, sem procurar nos logs de dois
processos.

**Why this priority**: É o objetivo central da issue e o caso que motivou o estudo; sem ele, as
outras partes perdem o fio que as liga.

**Independent Test**: Criar um pedido com a telemetria ligada, esperar a ordem de entrega e
conferir no destino local um único trace que vai da rota de criação até o consumidor de entrega.

**Acceptance Scenarios**:

1. **Given** a telemetria ligada, **When** um pedido é criado e chega à ordem de entrega,
   **Then** existe um único trace com a requisição de criação, a publicação de cada evento do
   fluxo (`pedido.criado`, `estoque.reservado`, `pagamento.aprovado`) e o processamento por
   cada um dos três consumidores, na ordem em que aconteceram.
2. **Given** o broker fora do ar por um tempo depois da criação, **When** ele volta e o evento
   pendente é publicado, **Then** a publicação e o consumo continuam no trace da requisição
   original, e o intervalo de espera no outbox aparece no trace.
3. **Given** um consumidor que falha na primeira entrega e acerta na nova tentativa, **When** o
   pedido avança, **Then** as duas tentativas aparecem no mesmo trace, com a falha marcada como
   erro na primeira.
4. **Given** um consumidor que esgota as novas tentativas, **When** a mensagem vai para a DLQ,
   **Then** a ida para a DLQ aparece no trace do pedido, marcada como erro e com o motivo.
5. **Given** um evento gravado por um consumidor (por exemplo, `estoque.reservado` gravado no
   processamento de `pedido.criado`), **When** ele é publicado, **Then** sua publicação é filha
   do processamento que o gravou.
6. **Given** uma mensagem repetida já processada pelo consumidor, **When** ela é descartada,
   **Then** o descarte aparece no trace como duplicado, sem erro.

---

### User Story 2 - Mensagem reenviada da DLQ ligada ao pedido e ao pedido de reenvio (Priority: P1)

Quando o Admin pede o reenvio de mensagens da DLQ (REQ-14), cada mensagem reenviada continua no
trace original do pedido, e a execução do reenvio fica ligada ao pedido de reenvio feito na API.

**Why this priority**: É o caso exato citado na issue (pagamento que foi para a DLQ e depois foi
reenviado) e um dos critérios de aceite; sem cuidado específico, o reenvio partiria o trace.

**Independent Test**: Levar uma mensagem à DLQ, registrar um pedido de reenvio, deixar o
processo de workers executá-lo e conferir que o processamento da mensagem reenviada está no trace
original do pedido e ligado à execução do reenvio, que por sua vez está ligada à requisição do
Admin.

**Acceptance Scenarios**:

1. **Given** uma mensagem de um pedido na DLQ, **When** o Admin pede o reenvio e ele é
   executado, **Then** o processamento da cópia reenviada aparece no trace do pedido, com uma
   ligação para a execução do reenvio.
2. **Given** um pedido de reenvio registrado na API, **When** o processo de workers o executa,
   **Then** a execução é um trace próprio, com o identificador do pedido de reenvio, o
   consumidor e as contagens (reenviadas, bloqueadas, fora do filtro), ligado à requisição que o
   registrou.
3. **Given** uma mensagem na DLQ fora do filtro ou acima do limite de reenvios, **When** o
   reenvio é executado, **Then** ela não ganha passo de reenvio no trace do pedido.

---

### User Story 3 - Rotinas periódicas visíveis e ligadas ao pedido (Priority: P2)

A reconciliação (REQ-12), o processamento de estornos (REQ-13), a execução de reenvios e a
limpeza (REQ-14) aparecem como traces próprios por rodada, e cada pedido tratado numa rodada
leva ao trace de criação desse pedido.

**Why this priority**: Essas rotinas mudam o estado do pedido sem requisição de origem; hoje elas
só deixam logs isolados. Depende da User Story 1 para que a ligação ao pedido tenha para onde
apontar.

**Independent Test**: Deixar um pedido parado até a reconciliação agir sobre ele e conferir uma
rodada com o pedido tratado, ligada ao trace de criação dele.

**Acceptance Scenarios**:

1. **Given** um pedido parado que a reconciliação recupera ou esgota, **When** a rodada roda,
   **Then** existe um trace da rodada com um passo para esse pedido, o resultado e uma ligação
   para o trace de criação do pedido.
2. **Given** um estorno pendente, **When** a rodada de estornos o confirma ou falha, **Then** o
   passo do estorno aparece na rodada, com o resultado e a ligação para o pedido.
3. **Given** um evento gravado por uma rotina (por exemplo, `pedido.cancelado` pela
   reconciliação), **When** ele é publicado e consumido, **Then** a publicação continua o trace da
   rodada.
4. **Given** uma rodada de limpeza, **When** ela termina, **Then** o trace da rodada mostra as
   linhas apagadas por alvo.
5. **Given** uma instância do processo de workers que não pega o lock da rodada, **When** ela
   desiste, **Then** nenhum trace é gerado para essa tentativa.
6. **Given** um pedido criado com a telemetria desligada ou antes deste requisito, **When** uma
   rotina o trata, **Then** o passo aparece sem ligação, e a rotina funciona normalmente.

---

### User Story 4 - Logs ligados ao trace (Priority: P2)

Cada log emitido durante uma requisição, um processamento de mensagem ou uma rodada de rotina
carrega o identificador do trace e do passo em que foi emitido, de modo que, a partir do trace, a
operação vê todos os logs da API e do processo de workers daquele pedido.

**Why this priority**: Fecha o problema original (remontar a sequência à mão) sem mudar os logs
que a operação já conhece.

**Independent Test**: Criar um pedido e conferir que os logs de criação, publicação e
processamento (4001, 5001, 5006, 6001, 7001, 8001) aparecem no destino ligados ao mesmo trace, com
os mesmos IDs de evento e mensagens de hoje.

**Acceptance Scenarios**:

1. **Given** a telemetria ligada, **When** um pedido percorre o fluxo, **Then** os logs da API e
   do processo de workers desse pedido trazem o mesmo identificador de trace.
2. **Given** qualquer log documentado em `docs/fluxos/`, **When** ele é emitido, **Then** o ID
   de evento, o nível e a mensagem são os mesmos de antes deste requisito.
3. **Given** a saída de console dos processos (`docker compose logs`), **When** um log é emitido
   dentro de um trace, **Then** o identificador do trace aparece também no console.

---

### User Story 5 - Métricas da mensageria e das rotinas (Priority: P2)

A operação acompanha, num painel, seis números que hoje só existem como texto de log ou não
existem: pendentes no outbox, idade do pendente mais antigo, tamanho da DLQ por consumidor,
mensagens por consumidor e resultado, execuções das rotinas por resultado e linhas apagadas pela
limpeza por alvo.

**Why this priority**: O outbox parado é hoje o sinal mais importante e não aparece em lugar
nenhum; mas o valor das métricas não depende das User Stories 1 a 4, e elas podem vir depois.

**Independent Test**: Parar o broker com pedidos sendo criados e conferir no destino local o
número de pendentes e a idade do mais antigo subindo; levar mensagens à DLQ e conferir o tamanho
por consumidor.

**Acceptance Scenarios**:

1. **Given** o broker fora do ar e pedidos sendo criados, **When** o tempo passa, **Then** a
   métrica de pendentes no outbox sobe e a idade do mais antigo cresce; com o broker de volta, as
   duas voltam a perto de zero.
2. **Given** mensagens na DLQ de um consumidor, **When** a métrica é lida, **Then** ela mostra o
   tamanho da DLQ daquele consumidor; o aviso 5011 continua saindo acima do limite, como hoje.
3. **Given** mensagens processadas, duplicadas, em nova tentativa e enviadas para a DLQ, **When**
   a métrica de mensagens é lida, **Then** ela mostra a contagem por consumidor e por resultado.
4. **Given** rodadas da reconciliação, dos estornos e dos reenvios, **When** a métrica de
   execuções é lida, **Then** ela mostra as contagens por rotina e resultado.
5. **Given** uma rodada de limpeza, **When** ela apaga linhas, **Then** a métrica de linhas
   apagadas soma as linhas por alvo.

---

### User Story 6 - Ver tudo localmente e trocar de destino por configuração (Priority: P3)

O desenvolvedor sobe a demo com o Docker e abre um painel local com traces, métricas e logs, sem
instalar nada além do Docker. Em outro ambiente, a operação aponta a exportação para outro
destino (Jaeger, Grafana, Azure Monitor ou um coletor) só mudando configuração. Com a exportação
desligada ou o destino fora do ar, a API e o processo de workers funcionam normalmente.

**Why this priority**: Torna as outras histórias demonstráveis, mas não muda o que é coletado.

**Independent Test**: Subir o perfil de demonstração, criar um pedido e ver o trace no painel
local; depois derrubar o painel e conferir que pedidos continuam sendo criados e processados.

**Acceptance Scenarios**:

1. **Given** o perfil de demonstração, **When** ele sobe, **Then** o painel de telemetria sobe
   junto, e a API e o processo de workers já exportam para ele.
2. **Given** a API rodando pelo SDK (fluxo de desenvolvimento do REQ-02), **When** o
   desenvolvedor sobe só o painel, **Then** a API exporta para ele sem mudar código.
3. **Given** a exportação desligada (sem destino configurado), **When** os processos sobem,
   **Then** eles funcionam como antes deste requisito, e a suíte de testes atual passa sem
   mudança.
4. **Given** um destino configurado e fora do ar, **When** pedidos são criados e processados,
   **Then** nada falha nem fica mais lento de forma perceptível; a telemetria que não pôde ser
   enviada é descartada.
5. **Given** a documentação, **When** a operação quer outro destino, **Then** encontra como
   apontá-lo só por configuração.

---

### Edge Cases

- **Telemetria ligada só na API ou só no processo de workers**: o lado ligado registra o que
  vê; o contexto continua viajando pelo outbox e pela mensagem, e o trace se completa quando o
  outro lado é ligado.
- **Evento gravado sem contexto** (telemetria desligada na gravação, ou evento pendente de antes
  deste requisito): é publicado normalmente e começa um trace novo no consumo.
- **Contexto inválido gravado ou recebido** (texto corrompido, cabeçalho adulterado): é
  ignorado; a publicação ou o consumo seguem, num trace novo, sem erro.
- **Amostragem**: quando o pedido não é amostrado na criação, nenhum passo dele é registrado
  depois (nem nos consumidores); quando é, o trace vem completo. Métricas e logs não são
  amostrados.
- **Despachante e rotinas ociosos**: as consultas periódicas sem pedido (o despachante a cada
  1 s, as rotinas sem trabalho) não geram traces.
- **Reentrega pela republicação do outbox** (queda entre a confirmação do broker e a gravação):
  a segunda publicação do mesmo evento aparece no mesmo trace, e o consumo dela como duplicado.
- **Mensagem reenviada que falha de novo**: volta para a DLQ dentro do trace do pedido, e um
  próximo pedido de reenvio a liga de novo.
- **Mensagem na DLQ publicada antes deste requisito** (sem contexto): o reenvio funciona; o
  processamento da cópia começa um trace novo ligado à execução do reenvio.
- **Painel local reiniciado**: os dados guardados nele se perdem; é esperado para o uso local.
- **Contexto de entrada na API**: um chamador que envia `traceparent` continua o próprio trace,
  inclusive a decisão de amostragem dele. Nenhum dado sensível entra por isso; fica registrado
  como limitação no documento de fluxo da criação de pedido.
- **Dados sensíveis**: nenhum trace, métrica ou log exportado pode conter e-mail, senha, token,
  corpo de requisição ou de mensagem, nem valores de parâmetros de consultas ao banco.

## Requirements *(mandatory)*

### Functional Requirements

**Traces**

- **FR-001**: O sistema MUST registrar um trace para cada requisição à API, exceto as rotas de
  saúde (`/health`, `/health/pronto`), com a rota, o método, o status de resposta e, para
  requisições autenticadas, o identificador do usuário (nunca o e-mail).
- **FR-002**: O sistema MUST guardar, junto de cada evento gravado no outbox, o contexto de
  rastreamento ativo no momento da gravação, quando houver.
- **FR-003**: Ao publicar um evento do outbox com contexto guardado, o sistema MUST continuar o
  trace desse contexto, de modo que a publicação seja filha da operação que gravou o evento.
- **FR-004**: O contexto MUST viajar até os consumidores como cabeçalho de transporte da
  mensagem, sem alterar o envelope v1 nem os schemas dos eventos (Princípio II).
- **FR-005**: O processamento de cada mensagem por um consumidor MUST ser registrado como filho
  da publicação que a originou, com o consumidor, o tipo do evento, o identificador do evento,
  o pedido, a tentativa e o resultado (processado, duplicado, nova tentativa, DLQ ou
  irrecuperável).
- **FR-006**: As novas tentativas e a ida para a DLQ MUST permanecer no trace do pedido, e as
  falhas MUST ser marcadas como erro, com o tipo do erro.
- **FR-007**: O reenvio da DLQ MUST registrar cada execução de pedido de reenvio como um trace
  próprio, com o identificador do pedido de reenvio, o consumidor e as contagens, ligado ao
  trace da requisição que o registrou na API.
- **FR-008**: Cada mensagem efetivamente reenviada MUST continuar o trace de origem que a
  mensagem carregava e ter uma ligação para a execução do reenvio; sem trace de origem, ela
  continua só ligada à execução.
- **FR-009**: Cada rodada da reconciliação, do processamento de estornos e da limpeza que
  efetivamente roda (com o lock) MUST ser registrada como um trace próprio; cada pedido
  tratado MUST ser um passo dela com o identificador do pedido, o resultado e uma ligação para o
  trace de criação do pedido, quando conhecido.
- **FR-010**: O sistema MUST guardar o contexto de criação de cada pedido criado com a
  telemetria ligada, para permitir a ligação do FR-009.
- **FR-011**: Rodadas que não pegam o lock, ciclos do despachante sem eventos a publicar e
  consultas ao banco sem passo pai MUST NOT gerar traces.
- **FR-012**: As consultas ao banco feitas dentro de uma requisição, de um processamento ou de
  uma rodada MUST aparecer como passos do trace, com o texto parametrizado da consulta e nunca
  os valores dos parâmetros.
- **FR-013**: Contexto ausente ou inválido, guardado ou recebido, MUST ser ignorado sem erro: a
  operação segue, num trace novo.

**Logs**

- **FR-014**: Cada log emitido dentro de um trace MUST carregar o identificador do trace e do
  passo, tanto no destino da telemetria quanto na saída de console.
- **FR-015**: Os IDs de evento, os nomes, os níveis e as mensagens dos logs existentes MUST NOT
  mudar.

**Métricas**

- **FR-016**: O sistema MUST publicar seis métricas, com nomes e unidades no padrão da
  convenção semântica:
  1. eventos pendentes no outbox;
  2. idade do evento pendente mais antigo, em segundos;
  3. mensagens na DLQ, por consumidor;
  4. mensagens tratadas pelos consumidores, por consumidor e resultado;
  5. execuções das rotinas (reconciliação, estornos, reenvio da DLQ), por rotina e resultado;
  6. linhas apagadas pela limpeza, por alvo.
- **FR-016a**: Cada instância do processo de workers MUST publicar as métricas identificada
  pela instância; as métricas de estado (1 a 3) são publicadas por todas as instâncias, e a
  documentação MUST orientar a usar o maior valor entre elas, enquanto as contagens (4 a 6) se
  somam.
- **FR-017**: O valor das métricas de outbox e de DLQ publicado pelo processo MUST refletir o
  estado com atraso de no máximo 30 s (outbox) e 1 min (DLQ), sem que a leitura delas pelo
  exportador consulte o banco ou o broker a cada vez. O envio ao destino MUST ocorrer a cada
  30 s por padrão, configurável; o atraso de ponta a ponta é a soma dos dois e fica
  documentado.
- **FR-018**: O aviso de DLQ acima do limite (log 5011) MUST continuar como hoje, no máximo uma
  vez a cada intervalo do aviso (padrão 5 min), mesmo com a DLQ lida a cada 1 min para a
  métrica; a métrica o complementa.

**Exportação, configuração e segurança**

- **FR-019**: A exportação MUST usar o protocolo aberto de telemetria, com destino, nome do
  serviço e atributos do ambiente configuráveis pelas variáveis padrão do protocolo, sem código
  específico de fornecedor.
- **FR-020**: Sem destino configurado, a telemetria MUST ficar desligada, e o comportamento dos
  processos MUST ser o de antes deste requisito.
- **FR-021**: Com o destino fora do ar ou lento, a API e o processo de workers MUST continuar
  funcionando, sem esperar pelo envio; a telemetria não enviada é descartada.
- **FR-022**: A amostragem dos traces MUST ser configurável por uma taxa (padrão: tudo), e a
  decisão tomada na origem do pedido MUST valer para todos os passos dele, inclusive depois do
  outbox e do broker.
- **FR-023**: Nenhum trace, métrica ou log exportado MUST conter e-mail, senha, token, cabeçalho
  de autorização, corpo de requisição ou de mensagem, nem valores de parâmetros de consultas
  (Princípio III).
- **FR-024**: O `docker-compose.yml` MUST incluir um painel local de telemetria em contêiner,
  com versão fixa, no perfil `demo` e no perfil `observabilidade` (para quem roda a API pelo
  SDK); a API e o processo de workers do perfil de demonstração MUST exportar para ele. O compose
  não é trocado pelo Aspire.
- **FR-025**: A documentação MUST explicar como apontar a exportação para outros destinos
  (Jaeger, Grafana, Azure Monitor ou um coletor) só por configuração.

**Documentação dos fluxos**

- **FR-026**: Os documentos de `docs/fluxos/` afetados MUST ser atualizados no mesmo PR, e a
  limitação "Não há métricas nem alertas além dos logs" MUST sair da seção "Limitações" do
  documento 11, ficando só a falta de alertas.

### Key Entities *(include if feature involves data)*

- **Evento pendente do outbox** (`EventoSaida`): ganha o contexto de rastreamento da operação
  que o gravou (opcional). Nada mais muda.
- **Pedido**: ganha o contexto de rastreamento da requisição que o criou (opcional), usado só
  para ligar as rotinas ao pedido.
- **Pedido de reenvio da DLQ** (REQ-14): ganha o contexto de rastreamento da requisição que o
  registrou (opcional).
- **Mensagem no broker**: carrega o contexto num cabeçalho de transporte, ao lado de
  `x-tentativas`, `x-reenvios` e `x-pedido-reenvio`. O corpo (envelope v1) não muda.
- **Trace / passo / ligação**: a linha do tempo de uma operação, cada etapa dela e a referência
  de um passo a outro trace (usada pelas rotinas e pelo reenvio).
- **Métrica**: um número com nome, unidade e atributos (consumidor, resultado, rotina, alvo).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em 100% dos pedidos criados com a telemetria ligada e amostrados, o caminho da
  criação até a ordem de entrega aparece num único trace, com os três consumidores.
- **SC-002**: A partir do identificador de um pedido, a operação encontra em menos de 2 minutos
  a sequência completa (requisição, publicações, processamentos, tentativas, DLQ e reenvio) e os
  logs dos dois processos, sem abrir os logs à mão.
- **SC-003**: 100% das mensagens reenviadas da DLQ que tinham trace de origem aparecem no trace
  original do pedido e ligadas à execução do reenvio.
- **SC-004**: Um despachante parado é percebido no destino pela métrica de idade do pendente mais
  antigo em até 1 minuto (30 s de leitura mais 30 s de envio, com a configuração padrão), sem
  depender de ler logs.
- **SC-005**: Com a exportação desligada ou o destino fora do ar, 100% da suíte de testes atual
  passa e o tempo de resposta da criação de pedido não piora mais do que 5% em relação a hoje.
- **SC-006**: Uma varredura da telemetria exportada nos testes encontra zero ocorrências de
  e-mail, senha, token ou valores de parâmetros de consulta.
- **SC-007**: Nenhum ID de evento de log documentado muda, e nenhum schema de evento nem rota da
  API muda (verificado pelos testes de contrato existentes).
- **SC-008**: O desenvolvedor vê o primeiro trace no painel local em até 5 minutos depois de
  subir a demo, sem instalar nada além do Docker.

## Assumptions

- A decisão e os detalhes técnicos estão no [research.md](./research.md) (R-01 a R-12); o
  `plan.md` os herda e justifica os pacotes novos, como exige a constituição.
- O cliente nativo do broker já em uso registra a publicação e o consumo e propaga o contexto
  sozinho; nenhuma biblioteca de abstração de mensageria entra (constituição).
- Não há chamadas HTTP de saída hoje (os gateways de pagamento e estorno são simulados); a
  instrumentação delas entra quando houver um gateway real.
- A métrica de latência por etapa do pedido fica fora deste requisito (adiada no estudo).
- Alertas, escolha de fornecedor pago e troca do compose pelo Aspire AppHost estão fora do
  escopo.
- O painel local guarda os dados em memória, sem autenticação, e serve só para
  desenvolvimento e demonstração; em outros ambientes, o destino é configurado pela operação.
- Pedidos, eventos pendentes e pedidos de reenvio gravados antes deste requisito não ganham
  contexto retroativo.
- Com várias instâncias do processo de workers, cada uma publica as próprias métricas,
  identificada pela instância: os contadores (métricas 4, 5 e 6) são somados no destino; as de
  estado (1, 2 e 3) são lidas por todas, e o painel usa o maior valor (Clarifications).
