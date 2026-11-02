# Feature Specification: Notificação de mudança de status do pedido (webhooks e SSE)

**Feature Branch**: `feature/req-18-notificacao-status`

**Requisito**: REQ-18 (GitHub issue #43 — "[Pedidos] Estudo: notificação de mudança de status do pedido (webhooks ou push)")

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "Notificação de mudança de status do pedido (issue #43), com webhooks e SSE, conforme specs/018-notificacao-status/research.md"

## Contexto

Hoje quem acompanha um pedido só descobre o que aconteceu depois do `201` consultando
`GET /api/v1/pedidos/{id}` de tempos em tempos (polling). Além disso, duas mudanças de status
não geram evento nenhum: o despacho com rastreio (REQ-11) e a mudança de status feita pela
operação na API (REQ-05). O estudo da issue #43 (`research.md`) mapeou os caminhos e propôs a
base deste requisito.

Este requisito avisa quem acompanha um pedido, sem polling, por dois canais:

| Canal | Para quem | Como |
|-------|-----------|------|
| **Webhooks** | Sistemas de fora (lojas, ERPs, parceiros) | O OMS chama, por `POST`, uma URL cadastrada, com o corpo assinado, e tenta de novo se o receptor falhar |
| **Server-Sent Events (SSE)** | Aplicações conectadas (front-end, painéis, ferramentas de linha de comando) | O cliente abre uma conexão autenticada com a API e recebe os avisos enquanto ela estiver aberta |

A consulta do pedido continua sendo a fonte da verdade: a notificação avisa, o `GET`
confirma.

**Mudança em relação ao estudo**: o `research.md` propunha adiar o SSE. O usuário decidiu
incluí-lo neste requisito (2026-11-01). Como a notificação nasce no processo de workers e a
conexão SSE fica na API, a API passa a receber os avisos da mensageria, o que revê a decisão
do REQ-14 de a API não ter nenhuma permissão no broker (FR-024).

## Clarifications

### Session 2026-11-01 (specify)

- Q: Quais canais entram? → A: Webhooks e SSE. SignalR fica fora (o SSE cobre o caso só
  servidor → cliente, sem pacote novo), e push móvel ou de navegador foi descartado no estudo.
- Q: O estorno confirmado (que não muda o status) gera notificação? → A: Sim, como tipo próprio
  (`pedido.estorno-confirmado`), seguindo a recomendação do estudo; é o que o Cliente quer saber
  depois de um cancelamento de pedido pago. Pode ser revisto no clarify.
- Q: A correção do código de rastreio (sem mudar o status) gera notificação? → A: Não nesta
  versão, seguindo a recomendação do estudo. Pode ser revisto no clarify.
- Q: Quem cadastra destinos de webhook? → A: Cliente e Admin. O destino do Cliente recebe só
  os pedidos dele; o do Admin é global (FR-007).
- Q: O `ETag` / `304` no `GET` entra neste requisito? → A: Sim, como história de prioridade
  menor (US5, FR-025).

## User Scenarios & Testing *(mandatory)*

Os usuários deste requisito são: o **sistema de fora** de um Cliente ou da operação, que quer
reagir à mudança do pedido sem consultar a API; a **aplicação conectada** (um front-end ou
painel), que quer mostrar a mudança na hora; e a **operação**, que acompanha a saúde das
entregas de webhook.

### User Story 1 - Toda mudança de status vira um aviso único (Priority: P1)

Qualquer mudança de status de um pedido, por qualquer caminho (processo de workers,
Cliente, operação, reconciliação, despacho com rastreio), gera exatamente um aviso
`pedido.status-alterado`, gravado junto com a mudança, com o status anterior, o novo e um
número de sequência por pedido. A confirmação do estorno gera um aviso
`pedido.estorno-confirmado`. Os dois canais partem desse aviso.

**Why this priority**: É a base dos dois canais e fecha a lacuna de hoje: o despacho e as
mudanças pela API não avisam ninguém.

**Independent Test**: Mudar o status de pedidos por cada caminho e conferir, numa fila
ligada ao exchange de eventos, um `pedido.status-alterado` por mudança, com a sequência
certa e válido pelo schema, e nenhum para a criação nem para a correção do rastreio.

**Acceptance Scenarios**:

1. **Given** um pedido `Pendente`, **When** o pagamento é aprovado e ele vai a `Processando`,
   **Then**, na mesma gravação, sai `pedido.status-alterado` com `Pendente` → `Processando`,
   a origem `Worker` e a sequência 2.
2. **Given** um pedido `ProntoParaEnvio`, **When** a operação registra o rastreio e ele vai a
   `Despachado`, **Then** sai `pedido.status-alterado` com a origem `Operacao`.
3. **Given** um pedido `Pendente`, **When** o Cliente o cancela, **Then** saem, na mesma
   gravação, `pedido.cancelado` (como hoje, REQ-15) e `pedido.status-alterado` com a origem
   `Cliente`.
4. **Given** um pedido criado, **When** a criação termina, **Then** nenhum
   `pedido.status-alterado` sai (o Cliente já recebeu o `201`).
5. **Given** um pedido `Despachado`, **When** a operação corrige o código de rastreio,
   **Then** nenhum aviso sai.
6. **Given** um pedido cancelado com estorno pendente, **When** o estorno é confirmado,
   **Then** sai, além do `pagamento.estornado` de hoje, a notificação
   `pedido.estorno-confirmado` para os canais.
7. **Given** uma mudança de status que falha ao gravar, **When** a operação termina,
   **Then** nenhum aviso é publicado.

---

### User Story 2 - Sistema de fora recebe o aviso por webhook (Priority: P1)

Um Cliente ou um Admin cadastra uma URL de destino e recebe um segredo, mostrado uma única
vez. A cada aviso que lhe diz respeito, o OMS faz um `POST` assinado nessa URL. Se o receptor
estiver fora do ar ou lento, o OMS tenta de novo por até 24 horas, sem afetar a API, o
processo de workers nem os outros destinos.

**Why this priority**: É o canal para quem integra servidor a servidor, os interessados
citados na issue, e o único que avisa quem não está conectado no momento.

**Independent Test**: Cadastrar um destino apontando para um receptor de teste, mudar o
status de um pedido e conferir um `POST` com a assinatura válida para o segredo devolvido no
cadastro; depois derrubar o receptor e conferir as novas tentativas e o esgotamento.

**Acceptance Scenarios**:

1. **Given** um Cliente autenticado, **When** ele cadastra uma URL válida, **Then** recebe
   `201` com o destino e o segredo; consultas seguintes não mostram o segredo.
2. **Given** um destino de um Cliente, **When** um pedido desse Cliente muda de status,
   **Then** o destino recebe um `POST` com o aviso, o identificador da notificação, o
   instante e a assinatura, e responde `2xx`; a entrega fica `Entregue`.
3. **Given** um destino de um Cliente, **When** um pedido de outro Cliente muda de status,
   **Then** o destino não recebe nada.
4. **Given** um destino de um Admin, **When** qualquer pedido muda de status, **Then** o
   destino recebe o aviso.
5. **Given** um receptor que responde `500` ou não responde em 10 segundos, **When** a
   entrega falha, **Then** ela é tentada de novo com espera crescente, com o mesmo
   identificador de notificação, até dar certo ou completar 24 horas desde a primeira
   tentativa; depois disso fica `Esgotada` e o destino é desativado, com o motivo gravado.
6. **Given** um receptor que responde `410 Gone`, **When** a entrega é feita, **Then** o
   destino é desativado na hora.
7. **Given** dois destinos, um fora do ar e outro no ar, **When** um aviso sai, **Then** o
   destino no ar recebe no prazo normal, sem esperar o outro.
8. **Given** um pedido que muda duas vezes em sequência, **When** as entregas chegam fora de
   ordem, **Then** o receptor consegue identificar a mais recente pela sequência e as
   repetidas pelo identificador da notificação.

---

### User Story 3 - Aplicação conectada recebe o aviso na hora por SSE (Priority: P2)

Uma aplicação autenticada abre uma conexão de eventos com a API e recebe, enquanto ela
estiver aberta, os avisos dos pedidos que o usuário pode consultar. Se a conexão cair, a
aplicação reconecta e confirma o estado pelo `GET`.

**Why this priority**: Atende quem mostra o pedido na tela (escolha do usuário em
2026-11-01), mas depende do aviso da US1 e não tem a garantia de entrega do webhook.

**Independent Test**: Abrir a conexão como Cliente, mudar o status de um pedido dele e de um
pedido de outro Cliente, e conferir que só o primeiro aviso chega, em até 10 segundos.

**Acceptance Scenarios**:

1. **Given** um Cliente com a conexão aberta, **When** um pedido dele muda de status,
   **Then** ele recebe o aviso na conexão, com o mesmo conteúdo do webhook.
2. **Given** um Cliente com a conexão aberta, **When** um pedido de outro Cliente muda de
   status, **Then** ele não recebe nada.
3. **Given** um Admin ou um OperadorLogistica com a conexão aberta, **When** qualquer pedido
   muda de status, **Then** ele recebe o aviso.
4. **Given** uma conexão aberta para um pedido específico, **When** outro pedido do mesmo
   Cliente muda de status, **Then** ela não recebe esse aviso.
5. **Given** um pedido específico de outro Cliente, **When** um Cliente tenta abrir a conexão
   só para ele, **Then** recebe `404`, como na consulta do pedido.
6. **Given** uma requisição sem token ou com token inválido, **When** ela tenta abrir a
   conexão, **Then** recebe `401`.
7. **Given** uma conexão aberta, **When** nenhum aviso sai por um tempo, **Then** a conexão
   recebe sinais periódicos de vida e não é derrubada por inatividade.
8. **Given** uma conexão aberta, **When** o token do usuário expira, **Then** a API encerra a
   conexão, e a aplicação precisa reconectar com um token novo.
9. **Given** um usuário com o número máximo de conexões abertas, **When** ele tenta abrir mais
   uma, **Then** recebe `429` com `ProblemDetails`.
10. **Given** várias instâncias da API, **When** um aviso sai, **Then** cada conexão aberta
    recebe o aviso, em qualquer instância que esteja.

---

### User Story 4 - Dono e operação gerenciam os destinos e acompanham as entregas (Priority: P2)

O dono de um destino lista os próprios destinos, remove um destino, reativa um destino
desativado e consulta as últimas entregas, com a situação, as tentativas e o último código
HTTP. A operação acompanha a saúde das entregas por logs e métricas.

**Why this priority**: Sem isso, um destino desativado ou uma entrega esgotada fica invisível,
e o receptor não tem como se recuperar sozinho.

**Independent Test**: Cadastrar, listar, desativar (por esgotamento), reativar, consultar as
entregas e remover um destino, conferindo as respostas e que outro usuário recebe `404`.

**Acceptance Scenarios**:

1. **Given** um Cliente com destinos, **When** ele lista os destinos, **Then** vê só os dele,
   sem o segredo.
2. **Given** um destino desativado, **When** o dono pede a reativação, **Then** o destino volta
   a receber os avisos novos e a contagem de falhas é zerada; as entregas já esgotadas não
   são reenviadas.
3. **Given** um destino com entregas pendentes, **When** o dono o remove, **Then** as entregas
   pendentes são descartadas e nada mais é enviado para ele.
4. **Given** um destino de outro usuário, **When** alguém tenta consultar, remover ou reativar,
   **Then** recebe `404`.
5. **Given** um usuário com o número máximo de destinos, **When** ele tenta cadastrar mais um,
   **Then** recebe `409` com `ProblemDetails`.
6. **Given** um OperadorLogistica, **When** ele tenta cadastrar um destino, **Then** recebe
   `403`.

---

### User Story 5 - Consulta mais barata para quem continua no polling (Priority: P3)

O `GET /api/v1/pedidos/{id}` passa a devolver um identificador da versão do pedido, e quem
consulta de novo informando esse identificador recebe `304` sem corpo quando nada mudou.

**Why this priority**: Custo pequeno e útil para todos os clientes, inclusive quem recebeu o
aviso e vai confirmar; não é o objetivo principal.

**Independent Test**: Consultar um pedido, repetir a consulta com o identificador recebido e
conferir o `304`; mudar o status e conferir que a consulta seguinte volta `200` com outro
identificador.

**Acceptance Scenarios**:

1. **Given** um pedido consultado, **When** a consulta é repetida com o mesmo identificador de
   versão e nada mudou, **Then** a resposta é `304`, sem corpo.
2. **Given** um pedido consultado, **When** ele muda (status, entrega, rastreio ou estorno) e a
   consulta é repetida com o identificador antigo, **Then** a resposta é `200` com o pedido
   atual e um identificador novo.
3. **Given** um pedido de outro Cliente, **When** um Cliente consulta com qualquer
   identificador, **Then** a resposta continua `404`.

---

### Edge Cases

- **Receptor lento**: uma chamada que passa de 10 segundos conta como falha; um receptor lento
  não segura os envios para os outros destinos.
- **Redirecionamento**: o OMS não segue `3xx`; conta como falha.
- **URL que aponta para a rede interna**: o cadastro é recusado com `400`; se o nome passar a
  apontar para um endereço interno depois do cadastro, o envio é recusado, conta como falha e
  gera log de endereço recusado.
- **Destino removido no meio de uma entrega**: o envio em andamento termina, e nada mais é
  tentado.
- **Destino cadastrado depois do aviso**: não recebe avisos anteriores ao cadastro.
- **Reentrega da mensagem ao processo de workers**: não cria entrega repetida para o mesmo
  destino e o mesmo aviso.
- **Receptor recebe duas vezes**: se o `2xx` se perde, o mesmo aviso chega de novo com o mesmo
  identificador; o receptor deduplica.
- **Broker fora do ar**: as mudanças de status gravam normalmente; os avisos ficam no outbox e
  saem quando o broker volta. As conexões SSE continuam abertas, com sinais de vida, mas não
  recebem os avisos do período; a aplicação confirma pelo `GET`. As outras rotas da API não
  são afetadas.
- **Conexão SSE que cai**: os avisos do período em que ela esteve fechada não são reenviados
  pela conexão; a aplicação reconecta e consulta o `GET`.
- **Cliente com várias abas**: cada conexão recebe os avisos, até o limite por usuário.
- **Chave de cifra dos segredos trocada ou ausente**: o processo que precisa dela não sobe
  (mesma regra da chave do JWT), em vez de enviar sem assinatura.
- **Pedido que muda de status antes da implantação**: não gera aviso retroativo.
- **Admin que também é Cliente**: o destino do Admin é global; a origem do aviso segue o
  perfil usado na mudança, como no REQ-15.

## Requirements *(mandatory)*

### Functional Requirements

**Aviso de mudança de status**

- **FR-001**: Toda mudança de status de um pedido, por qualquer caminho, MUST gerar exatamente
  um evento `pedido.status-alterado`, gravado no outbox na mesma gravação da mudança. A
  criação do pedido MUST NOT gerar esse evento, e uma mudança que não grava MUST NOT gerar
  evento.
- **FR-002**: `pedido.status-alterado` MUST trazer: o identificador e o número do pedido, o
  status anterior, o status novo, a sequência da mudança no histórico do pedido (a criação é
  1), a origem (`Worker`, `Cliente` ou `Operacao`) e o instante. MUST NOT trazer o texto livre
  do motivo, dados pessoais, valores, itens, dados de pagamento nem o código de rastreio.
- **FR-003**: A sequência MUST ser única e crescente por pedido.
- **FR-004**: A confirmação de um estorno MUST gerar uma notificação
  `pedido.estorno-confirmado` para os dois canais, com o identificador e o número do pedido e
  o instante, sem valores nem identificadores do gateway. O `pagamento.estornado` do REQ-15
  continua como está.
- **FR-005**: A correção do código de rastreio, sem mudança de status, MUST NOT gerar
  notificação.
- **FR-006**: O tipo novo de evento MUST ser acréscimo à v1 do contrato de eventos, e o corpo
  das notificações MUST ter schema versionado próprio, com testes de serialização e de
  conformidade (Princípio II e V). Os tipos e consumidores existentes MUST NOT mudar.

**Webhooks: cadastro**

- **FR-007**: Usuários com perfil Cliente ou Admin MUST poder cadastrar destinos de webhook.
  O destino de um Cliente MUST receber só os avisos dos pedidos dele; o destino de um Admin
  MUST receber os avisos de todos os pedidos.
- **FR-008**: O cadastro MUST exigir URL absoluta com `https`, sem usuário e senha na URL, e
  cujo host resolva só para endereços públicos; fora disso, `400` com `ProblemDetails`. Só em
  Development e nos testes uma configuração MAY liberar `http` e a rede local.
- **FR-009**: O cadastro MUST devolver um segredo gerado pelo OMS uma única vez; o segredo MUST
  ficar guardado cifrado e MUST NOT aparecer em consultas, logs ou métricas. Trocar o segredo
  é remover e cadastrar de novo.
- **FR-010**: Cada usuário MUST ter no máximo 5 destinos (configurável); acima disso, `409`.
- **FR-011**: O dono MUST poder listar, remover e reativar os próprios destinos e consultar as
  últimas entregas de cada um (situação, tentativas, último código HTTP, instantes). Destino
  de outro usuário MUST responder `404`. O OperadorLogistica MUST NOT gerenciar destinos
  (`403`).

**Webhooks: entrega**

- **FR-012**: Cada aviso MUST gerar uma entrega por destino ativo que deve recebê-lo, cada uma
  com tentativas, espera e situação próprias. A falha ou a lentidão de um destino MUST NOT
  atrasar os outros nem a API e o processo de workers.
- **FR-013**: A entrega MUST ser um `POST` com o corpo da notificação, um identificador de
  notificação igual em todas as tentativas, o instante do envio e uma assinatura HMAC-SHA256
  do identificador, do instante e do corpo com o segredo do destino, no formato do padrão
  Standard Webhooks.
- **FR-014**: Só `2xx` MUST contar como sucesso; redirecionamentos não são seguidos. Cada
  chamada MUST ter limite de 10 segundos.
- **FR-015**: Uma entrega que falha MUST ser tentada de novo com espera crescente (30 s, 2 min,
  10 min, 30 min, 1 h e depois a cada 2 h) até 24 h desde a primeira tentativa; depois disso
  MUST ficar `Esgotada` e o destino MUST ser desativado, com o motivo gravado. Um `410 Gone`
  MUST desativar o destino na hora.
- **FR-016**: O envio MUST conferir o endereço resolvido no momento da conexão e recusar
  endereços internos (loopback, redes privadas, link-local e metadados de nuvem, CGNAT,
  multicast e equivalentes IPv6), mesmo que o cadastro tenha passado.
- **FR-017**: A entrega MUST ser "pelo menos uma vez", sem garantia de ordem; o receptor
  identifica repetidas pelo identificador e atrasadas pela sequência.
- **FR-018**: Entregas finalizadas (entregues, esgotadas ou descartadas) MUST ser apagadas
  depois de 7 dias pela limpeza periódica do REQ-14.

**SSE**

- **FR-019**: A API MUST oferecer uma rota de eventos (`text/event-stream`) autenticada pelo
  token do REQ-03. Sem token ou com token inválido, `401`.
- **FR-020**: Pela conexão, o Cliente MUST receber só os avisos dos próprios pedidos; Admin e
  OperadorLogistica MUST receber os de todos os pedidos, como na consulta. A conexão MAY ser
  aberta para um único pedido; pedido inexistente ou de outro Cliente MUST responder `404`.
- **FR-021**: Cada aviso na conexão MUST ter o mesmo conteúdo do corpo do webhook, com o
  identificador da notificação como identificador do evento. Avisos perdidos enquanto a
  conexão estava fechada MUST NOT ser reenviados; a aplicação confirma pelo `GET`.
- **FR-022**: A conexão MUST receber um sinal de vida a cada 30 segundos sem aviso e MUST ser
  encerrada pela API quando o token expira.
- **FR-023**: Cada usuário MUST ter no máximo 3 conexões abertas por instância da API
  (configurável); acima disso, `429` com `ProblemDetails`.
- **FR-024**: Para levar os avisos até as conexões, a API MUST passar a ler da mensageria só
  os avisos de notificação, com a menor permissão possível, sem publicar nem ler outros
  eventos. Com várias instâncias, cada instância MUST receber todos os avisos. A falha do
  broker MUST NOT derrubar a API nem afetar as outras rotas.

**Consulta**

- **FR-025**: O `GET /api/v1/pedidos/{id}` MUST devolver um identificador de versão do pedido
  e responder `304`, sem corpo, quando a requisição traz o mesmo identificador e o pedido não
  mudou. A resposta `200` e as regras de autorização MUST NOT mudar.

**Contrato, segurança e observabilidade**

- **FR-026**: As rotas novas MUST seguir as regras da API: autenticação, autorização por
  política, validação com `400` em `ProblemDetails` e testes de integração de sucesso, `401`
  e `403`. As rotas existentes MUST NOT mudar (só acréscimos ao contrato v1).
- **FR-027**: O trace do pedido (REQ-16) MUST alcançar a criação de cada entrega e o envio, sem
  enviar o contexto de trace ao receptor de fora.
- **FR-028**: MUST haver logs com IDs novos para: destino cadastrado, removido, reativado e
  desativado; entrega criada, entregue, com falha e esgotada; endereço recusado; conexão SSE
  aberta, encerrada e recusada pelo limite. A URL do destino MUST aparecer só com esquema e
  host.
- **FR-029**: MUST haver métricas de entregas por resultado, latência do aviso até o `2xx`,
  entregas pendentes, destinos desativados e conexões SSE abertas.
- **FR-030**: A suíte MUST ter testes de integração, com banco e broker reais, de cada caminho
  de mudança de status, da entrega com receptor de teste (sucesso, falha, lentidão, `410`,
  esgotamento), da assinatura, da recusa de endereços internos, do SSE (autorização, filtro
  por pedido, limite, várias conexões) e das rotas de destinos, além de testes unitários das
  regras novas, e MUST continuar verde no CI.

### Key Entities *(include if feature involves data)*

- **`pedido.status-alterado`** (evento novo, v1): uma mudança de status de um pedido, com o
  status anterior e o novo, a sequência no histórico e a origem.
- **Notificação** (corpo do webhook e do evento SSE): o aviso enviado para fora, do tipo
  `pedido.status-alterado` ou `pedido.estorno-confirmado`, com identificador, instante, dados
  mínimos do pedido e o link para a consulta.
- **Destino de notificação**: URL cadastrada por um usuário, com dono (ou global, para o
  Admin), segredo cifrado, situação (ativo ou desativado, com o motivo) e contagem de falhas.
- **Entrega de notificação**: o envio de uma notificação para um destino, com situação
  (`Pendente`, `Entregue`, `Esgotada`, `Descartada`), tentativas, próximo envio, último código
  HTTP e o contexto de trace do aviso.
- **Conexão de eventos**: conexão SSE aberta por um usuário autenticado, opcionalmente
  filtrada por um pedido; não é guardada no banco.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% das mudanças de status depois da implantação têm exatamente um
  `pedido.status-alterado`, inclusive o despacho e as mudanças pela API, que hoje não têm
  evento.
- **SC-002**: Com o broker e o receptor no ar, o receptor do webhook e a conexão SSE recebem o
  aviso em até 10 segundos depois da mudança, no ambiente de demonstração.
- **SC-003**: Com um receptor fora do ar, 100% das entregas são tentadas de novo até darem
  certo ou completarem 24 horas, e os outros destinos continuam recebendo no prazo do SC-002.
- **SC-004**: 100% das notificações enviadas nos testes têm assinatura válida e passam no
  schema, e nenhuma traz e-mail, endereço, token, valores ou dados de pagamento.
- **SC-005**: Em nenhum teste um Cliente recebe aviso, por webhook ou por SSE, de pedido de
  outro Cliente.
- **SC-006**: Nenhuma URL que aponte para endereço interno recebe chamada, no cadastro ou no
  envio.
- **SC-007**: O tempo de resposta das rotas existentes e do fluxo normal do pedido continua
  dentro das metas dos REQ-05 e REQ-10, e os contratos HTTP e de eventos v1 continuam
  compatíveis (verificação de contrato sem quebra).
- **SC-008**: A suíte continua 100% verde no CI.

## Assumptions

- O conteúdo da notificação é mínimo e aponta para o `GET`; quem quer o detalhe consulta com o
  próprio token, que aplica a autorização de sempre.
- O segredo de assinatura é cifrado com uma chave da configuração, tratada como a chave do
  JWT; a perda da chave obriga a recadastrar os destinos.
- O SSE não tem reenvio de avisos perdidos: a garantia de entrega fica com o webhook, e a
  conexão serve para mostrar a mudança na hora. Não há front-end no projeto; a demonstração e
  os testes usam um cliente HTTP.
- O token tem a duração atual do REQ-03 (60 minutos); a conexão SSE não dura mais que ele.
- A API passa a ler da mensageria só para o SSE; o usuário do broker da API continua sem
  permissão de publicar e sem acesso às filas dos consumidores do processo de workers. Isso
  revê a decisão do REQ-14, por escolha do usuário em 2026-11-01.
- Novas tentativas e esgotamento das entregas de webhook ficam no banco, e não em filas por
  destino no broker; a DLQ do consumidor de notificação (REQ-06) e o reenvio do REQ-14 seguem
  valendo para a etapa que cria as entregas.
- Nenhum pacote NuGet novo é esperado; se o plano precisar de algum, ele será justificado no
  `plan.md`.
- Ficam fora do escopo: SignalR, push móvel e de navegador, e-mail e SMS, notificação da
  correção do rastreio, reenvio manual de entrega esgotada, troca do segredo sem recadastro e
  avisos retroativos.
- O ambiente local do REQ-02 (banco e broker no ar) é pré-requisito para os testes de
  integração. Sem Docker, eles são ignorados localmente e reprovam no CI (REQ-07).
- O documento de fluxo novo (`docs/fluxos/`) e as atualizações dos fluxos 3, 7, 10, 11 e 12
  entram na fase de Polish, como manda o `CLAUDE.md`.
