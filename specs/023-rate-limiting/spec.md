# Feature Specification: Limite de requisições compartilhado no login e no registro

**Feature Branch**: `feature/req-23-rate-limiting`

**Requisito**: REQ-23 (GitHub issue #58 — "[Segurança] Limite de requisições (rate limiting) no login e no registro")

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Limitar a taxa de requisições no login e no registro (issue #58), com armazenamento distribuído: o bloqueio é uma ideia do domínio, e não de uma instância. Um usuário que esgotou as tentativas de login conectado à API A continua bloqueado ao tentar pela API B; a mesma regra de limite vale para todas as instâncias."

## Contexto

As rotas públicas de login e de registro não têm limite de requisições, o que ficou fora do
escopo do REQ-03. Isso deixa a API aberta a força bruta contra senhas, a enumeração de contas e
ao abuso do registro. A única proteção hoje é o bloqueio de conta: 5 erros de senha seguidos
bloqueiam a conta por 15 minutos, com a mesma resposta 401 (fluxo 1, FR-009 do REQ-03).

A issue deixava em aberto registrar o limite por instância como limitação ou adotar um
armazenamento distribuído. **O usuário decidiu pelo armazenamento distribuído**: o limite é uma
regra do sistema, e o estado dele é único para todas as instâncias da API. Trocar de instância,
por balanceamento ou reinício, não devolve tentativas a ninguém.

### Decisão sobre o bloqueio de conta

A issue pede para decidir aqui se basta o bloqueio de conta junto com o limite de requisições.
**Os dois continuam, com papéis diferentes, e o bloqueio de conta não muda:**

| Proteção | O que conta | O que protege | Quem é atingido |
|----------|-------------|---------------|-----------------|
| Limite de requisições (este REQ) | Requisições, com qualquer resultado | Volume: rajadas, custo do processamento e abuso do registro | A origem que excedeu |
| Bloqueio de conta (REQ-03) | Erros de senha seguidos da conta | A senha da conta, inclusive contra ataques vindos de muitas origens | A conta |

O limite por origem sozinho não contém um ataque distribuído contra uma conta, e o bloqueio
sozinho não contém rajadas nem o registro. O bloqueio de conta já é compartilhado entre as
instâncias, porque fica no cadastro do usuário.

## Clarifications

### Session 2026-11-04

Respostas dadas pelo usuário durante o specify:

- Q: O login também tem um limite só por origem, além do limite por origem mais e-mail? → A: Sim. Sem ele, quem testa uma senha comum em muitos e-mails a partir de uma origem não seria contido nem pelo limite por e-mail nem pelo bloqueio de conta. O limite é mais folgado (padrão: 100 requisições por minuto).
- Q: O que acontece quando o armazenamento compartilhado está indisponível? → A: A requisição é aceita sem o limite, com log e métrica. O login não cai por causa do limite, e o bloqueio de conta continua protegendo as senhas.
- Q: Que tipo de janela conta as requisições? → A: Janela fixa, iniciada na primeira requisição de cada contagem. Ao fim dela, a contagem recomeça. O excesso de até 2 vezes o limite na virada entre duas janelas é aceito e fica como limitação conhecida no fluxo 1.
- Q: Os valores padrão dos limites podem ser 10 logins por minuto por origem mais e-mail, 100 logins por minuto por origem e 20 registros por hora por origem? → A: Sim, como proposto (FR-019).
- Q: Em IPv6, o limite por origem conta o endereço exato ou o bloco de rede `/64` inteiro? → A: O bloco `/64`, de tamanho fixo na regra. Em IPv4, conta o endereço exato.
- Q: Uma tentativa de login barrada pelo limite de origem mais e-mail também consome o limite só por origem? → A: Não. Só a tentativa que passou pelo limite de origem mais e-mail conta no limite por origem, para que quem insiste em um e-mail não trave o login do resto da sua rede.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - O limite do login vale em todas as instâncias (Priority: P1)

Quem insiste em tentativas de login contra um mesmo e-mail, de uma mesma origem, é barrado ao
exceder o limite, e continua barrado ainda que a tentativa seguinte caia em outra instância da
API. A resposta diz quando vale tentar de novo.

**Why this priority**: É o objetivo central do requisito e a decisão do usuário: conter a força
bruta sem que o balanceamento multiplique as tentativas pelo número de instâncias.

**Independent Test**: Com duas instâncias da API ligadas ao mesmo armazenamento, esgotar as
tentativas pela primeira e confirmar que a segunda recusa a tentativa seguinte, com o tempo de
espera informado.

**Acceptance Scenarios**:

1. **Given** uma origem que esgotou as tentativas de login de um e-mail pela instância A, **When** ela tenta o mesmo e-mail pela instância B, **Then** a resposta é 429 com `Retry-After`, sem que a senha seja avaliada.
2. **Given** duas instâncias recebendo tentativas simultâneas da mesma origem para o mesmo e-mail, **When** a soma ultrapassa o limite, **Then** o total aceito nas duas instâncias nunca passa do limite configurado.
3. **Given** uma origem barrada, **When** o tempo informado em `Retry-After` termina, **Then** a próxima tentativa é aceita e tratada normalmente, em qualquer instância.
4. **Given** uma origem barrada para um e-mail, **When** ela tenta outro e-mail, ou outra origem tenta o mesmo e-mail, **Then** essa tentativa não é barrada por aquele limite.
5. **Given** um e-mail escrito com outra caixa ou com espaços nas pontas, **When** a origem tenta de novo, **Then** ele conta como o mesmo e-mail.
6. **Given** um e-mail sem conta cadastrada, **When** a origem excede o limite, **Then** a resposta é a mesma de um e-mail com conta, sem revelar quais e-mails existem.
7. **Given** uma origem que tenta muitos e-mails diferentes, um de cada vez, **When** ela excede o limite de login por origem, **Then** a resposta é 429 para qualquer e-mail, em qualquer instância.
8. **Given** uma origem barrada pelo limite de um e-mail, **When** ela insiste nesse e-mail, **Then** as tentativas barradas não consomem o limite por origem, e os outros e-mails da mesma origem continuam entrando.

---

### User Story 2 - O limite do registro vale em todas as instâncias (Priority: P1)

Quem cria contas em rajada a partir de uma mesma origem é barrado ao exceder o limite, em
qualquer instância da API.

**Why this priority**: O registro cria dados permanentes e, pela resposta de e-mail já
cadastrado, permite enumerar contas. Sem limite, é a rota mais barata de abusar.

**Independent Test**: Exceder o limite de registro por uma instância, com e-mails diferentes, e
confirmar que a outra instância recusa o registro seguinte da mesma origem e que nenhuma conta
foi criada por ele.

**Acceptance Scenarios**:

1. **Given** uma origem que esgotou o limite de registro, **When** ela tenta registrar outro e-mail em qualquer instância, **Then** a resposta é 429 com `Retry-After`, e nenhuma conta é criada.
2. **Given** uma origem barrada no registro, **When** outra origem se registra, **Then** o registro é aceito normalmente.
3. **Given** uma origem barrada no registro, **When** ela faz login, **Then** o login não é barrado por esse limite, pois cada rota tem a sua contagem.

---

### User Story 3 - O cliente da API sabe o que aconteceu e o que fazer (Priority: P2)

Quem consome a API recebe uma resposta padronizada ao exceder o limite, com um código estável
para tratar no programa e o tempo de espera.

**Why this priority**: Sem resposta padronizada, os clientes não distinguem o limite de outras
falhas e tendem a repetir na hora, piorando a situação.

**Independent Test**: Exceder o limite e conferir a resposta contra o contrato v1 publicado.

**Acceptance Scenarios**:

1. **Given** uma requisição acima do limite, **When** a resposta é devolvida, **Then** ela é um 429 em `ProblemDetails`, em português, com `codigo` igual a `LIMITE_REQUISICOES_EXCEDIDO` e o cabeçalho `Retry-After` em segundos inteiros, de no mínimo 1.
2. **Given** o contrato v1 publicado, **When** é comparado à versão anterior, **Then** a única mudança nas duas rotas é o acréscimo da resposta 429.

---

### User Story 4 - A operação ajusta os limites e enxerga os bloqueios (Priority: P2)

A operação define os limites por configuração, sem mudar código, tem a partida barrada se a
configuração for inválida e vê nos logs e nas métricas quando o limite atua, sem dados pessoais.

**Why this priority**: Os limites certos dependem do tráfego real e mudam com o tempo. Sem
visibilidade, não dá para distinguir um ataque de um limite apertado demais.

**Independent Test**: Subir a API com um limite inválido e confirmar que ela não parte, citando a
chave sem o valor; depois, provocar um bloqueio e conferir o log e a métrica.

**Acceptance Scenarios**:

1. **Given** um limite zero, negativo ou ausente na configuração, **When** a API parte, **Then** a partida falha com a mensagem citando a chave, como nas demais opções.
2. **Given** um bloqueio de login, **When** o log é gravado, **Then** ele traz a política e o e-mail mascarado, nunca o e-mail completo, a senha ou o endereço de origem completo.
3. **Given** uma rajada que recebe muitos 429 seguidos, **When** os logs são gravados, **Then** a mesma contagem gera um único log por janela, e a métrica registra todas as recusas.

---

### Edge Cases

- **Corpo do login ausente, inválido ou sem e-mail**: sem e-mail para contar, a tentativa conta só no limite por origem, para que não sirva de rota livre. A resposta, se estiver dentro do limite, é o 400 de hoje.
- **Os dois limites do login excedidos ao mesmo tempo**: o `Retry-After` informa a maior espera, para que a próxima tentativa não seja barrada pelo outro limite.
- **Virada entre duas janelas**: quem usa todo o limite no fim de uma janela e todo o limite no início da seguinte passa até 2 vezes o limite em pouco tempo. A média continua limitada, e o bloqueio de conta segue protegendo a senha.
- **Requisição barrada pelo limite**: não avalia a senha nem conta para o bloqueio de conta. Assim, a rajada não bloqueia a conta além do que já passou pelo limite.
- **Login certo**: conta como requisição e não zera a contagem do limite. Quem zera a contagem de erros de senha continua sendo o bloqueio de conta.
- **Conta bloqueada e origem dentro do limite**: responde o 401 de hoje, sem revelar o bloqueio.
- **Origem atrás de um intermediário** (balanceador ou proxy): o limite vale para o cliente real, não para o endereço do intermediário; caso contrário, todos os clientes dividiriam a mesma contagem. Um cabeçalho de origem enviado por quem não é intermediário confiável é ignorado, para que ninguém escape do limite forjando um endereço.
- **Troca de endereço IPv6 dentro da mesma rede**: endereços do mesmo bloco `/64` dividem a contagem, para que trocar de endereço não renove as tentativas.
- **Vários clientes atrás do mesmo endereço público** (rede corporativa): no login, a contagem por e-mail evita que um atrapalhe o outro, e só o limite por origem, mais folgado, é dividido; no registro, dividem o limite. Os limites padrão consideram esse caso e podem ser ajustados por configuração.
- **Relógios diferentes entre as instâncias**: o resultado e o `Retry-After` não podem depender do relógio de cada instância.
- **Reinício ou implantação de instâncias**: a contagem sobrevive, e nenhuma instância nova recomeça do zero.
- **Armazenamento compartilhado indisponível ou lento**: a requisição segue sem o limite (FR-012), e a contagem recomeça quando ele voltar. Durante a falha, as rajadas passam, e o bloqueio de conta continua valendo.
- **Crescimento do armazenamento**: contagens vencidas são removidas, e um ataque com muitos e-mails diferentes não faz o armazenamento crescer sem limite.

## Requirements *(mandatory)*

### Functional Requirements

**Limites**

- **FR-001**: O sistema MUST limitar as requisições de login por origem mais e-mail, e as de registro por origem.
- **FR-002**: O sistema MUST limitar também as requisições de login só por origem, com um limite próprio e mais folgado. Exceder qualquer um dos dois limites barra a tentativa, e uma tentativa barrada por um deles não consome o outro.
- **FR-003**: O e-mail MUST ser comparado sem diferenciar maiúsculas e sem os espaços das pontas, como no cadastro. Uma tentativa de login sem e-mail utilizável conta só no limite por origem.
- **FR-004**: A origem MUST ser o cliente real. Por configuração, o sistema aceita a origem informada apenas por intermediários confiáveis, e ignora essa informação quando ela vem de qualquer outro. Em IPv4, a origem é o endereço exato; em IPv6, é o bloco `/64` do endereço. Um endereço IPv4 representado em IPv6 conta como IPv4.
- **FR-005**: Só as rotas `POST /api/v1/auth/login` e `POST /api/v1/auth/register` são limitadas. As demais rotas e a contagem de uma rota sobre a outra não são afetadas.

**Estado compartilhado**

- **FR-006**: O estado dos limites MUST ser único para todas as instâncias da API: uma requisição é decidida com a contagem de todas elas, e o resultado independe da instância que a atende.
- **FR-007**: A contagem MUST ser atômica entre as instâncias: requisições simultâneas nunca somam mais aceitas que o limite configurado.
- **FR-008**: Cada contagem MUST usar uma janela fixa, iniciada na sua primeira requisição, e recomeçar quando a janela termina. O início e o fim da janela MUST vir de uma única referência de tempo, para que o relógio de cada instância não altere o resultado nem o `Retry-After`.
- **FR-009**: A contagem MUST sobreviver ao reinício de qualquer instância da API.
- **FR-010**: O armazenamento MUST NOT guardar o e-mail nem o endereço de origem de forma legível.
- **FR-011**: Contagens vencidas MUST ser removidas, sem intervenção da operação.
- **FR-012**: Quando o armazenamento compartilhado está indisponível, ou não responde dentro de um tempo máximo configurável, a requisição MUST seguir sem o limite e receber a resposta normal da rota. A falha MUST gerar log e métrica, e não pode ser exposta ao cliente.

**Resposta**

- **FR-013**: Acima do limite, o sistema MUST responder 429 em `ProblemDetails`, com mensagem em português, `codigo` `LIMITE_REQUISICOES_EXCEDIDO` e o cabeçalho `Retry-After` em segundos inteiros, de no mínimo 1, sem revelar os valores dos limites, se a conta existe ou se ela está bloqueada.
- **FR-014**: A requisição barrada MUST NOT avaliar a senha, contar para o bloqueio de conta ou criar uma conta.
- **FR-015**: Esperar o tempo de `Retry-After` MUST bastar para a próxima requisição ser aceita, se não houver outras requisições das mesmas contagens. Insistir durante a espera não a prolonga. Com os dois limites do login excedidos, vale a maior espera.
- **FR-016**: O contrato v1 MUST mudar só por acréscimo: o 429 documentado nas duas rotas e o novo código de erro.

**Bloqueio de conta**

- **FR-017**: O bloqueio de conta do REQ-03 (5 erros de senha seguidos, 15 minutos, 401 igual ao de credenciais inválidas) MUST continuar como está.

**Configuração**

- **FR-018**: Os limites e as janelas de cada política MUST ser configuráveis e validados na partida: um valor inválido impede a API de subir, e a mensagem cita a chave, sem mostrar o valor. Não existe opção para desligar o limite.
- **FR-019**: Os valores padrão MUST ser: login, 10 requisições por minuto por origem mais e-mail e 100 requisições por minuto por origem; registro, 20 requisições por hora por origem.

**Observabilidade**

- **FR-020**: O sistema MUST registrar o primeiro bloqueio de cada contagem em cada janela, em log com a política e, na política com e-mail, o e-mail mascarado, sem senha, e-mail completo ou endereço de origem completo.
- **FR-021**: O sistema MUST expor métricas das requisições recusadas por política e das falhas do armazenamento compartilhado, sem dados pessoais nas dimensões.

**Compatibilidade e testes**

- **FR-022**: O tráfego dentro dos limites MUST receber exatamente as respostas de hoje, e os testes e a demonstração existentes não podem depender de limites desligados nem falhar por causa deles.
- **FR-023**: Os testes de integração MUST cobrir o 429 nas duas rotas e nos dois limites do login, o `Retry-After`, o estado compartilhado entre duas instâncias, a concorrência entre elas, a volta depois da espera e a requisição aceita com o armazenamento indisponível.

### Key Entities

- **Política de limite**: uma regra com a rota, o limite de requisições, a janela e o que forma a contagem (origem, ou origem mais e-mail). O login tem duas políticas, e o registro, uma.
- **Contagem de requisições**: o estado compartilhado de uma política para uma identidade dentro de uma janela: a identidade em forma não legível, quantas requisições foram contadas e quando a janela termina.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Em uma rajada de 100 tentativas de login da mesma origem contra o mesmo e-mail, repartidas entre duas instâncias, o número de tentativas com a senha avaliada dentro de uma janela nunca passa do limite configurado.
- **SC-002**: Depois de esgotado o limite em uma instância, 100% das tentativas seguintes em outra instância, dentro da janela, recebem 429.
- **SC-003**: Em 100% dos 429, o cabeçalho `Retry-After` está presente, e a mesma contagem informa o mesmo tempo em qualquer instância, com diferença de até 1 segundo.
- **SC-004**: Depois de esperar o tempo informado, a próxima tentativa é aceita em 100% dos casos.
- **SC-005**: Todos os testes e a demonstração existentes continuam passando, e as requisições dentro do limite recebem a mesma resposta de antes.
- **SC-006**: O comparador de contrato não aponta nenhuma mudança incompatível no contrato v1.
- **SC-007**: Nenhum log ou dimensão de métrica de bloqueio contém senha, e-mail completo ou endereço de origem completo.
- **SC-008**: A verificação do limite acrescenta, em 95% das requisições às duas rotas, no máximo 50 ms ao tempo de resposta.
- **SC-009**: Em uma rajada de login da mesma origem, com um e-mail diferente por tentativa e repartida entre duas instâncias, o número de tentativas com a senha avaliada dentro de uma janela nunca passa do limite por origem.
- **SC-010**: Com o armazenamento compartilhado indisponível, 100% das requisições às duas rotas recebem a resposta normal da rota, e a falha aparece no log e na métrica.

## Assumptions

- A escolha do armazenamento compartilhado (por exemplo, o banco que o sistema já usa, ou um serviço novo) e o mecanismo de contagem ficam para o plano, com as alternativas comparadas no `research.md`. A issue sugere o limitador nativo da plataforma, sem pacote novo; o plano confere se isso continua valendo com o estado compartilhado, e qualquer dependência nova exige justificativa escrita (constituição).
- O limite vale para a origem de rede: quem troca de endereço a cada tentativa não é contido por ele, e é o bloqueio de conta que protege a senha nesse caso.
- Um atacante ainda pode bloquear a conta de outra pessoa errando a senha de propósito, a partir de várias origens. Esse risco já existe desde o REQ-03 e continua como limitação conhecida, a registrar no fluxo 1.
- O excesso na virada da janela fixa é uma limitação aceita, a registrar no fluxo 1. Trocar o algoritmo depois não muda o contrato.
- A API sobe com uma instância no compose, e a execução com várias instâncias é verificada nos testes de integração. Um balanceador na demonstração não faz parte do escopo.
- Os ambientes de teste usam limites próprios, altos o bastante para os testes existentes, com o mesmo código e a mesma validação de produção. Só os testes do limite usam valores baixos.
- Fora do escopo: WAF, CDN, CAPTCHA, limites em outras rotas, limites por usuário autenticado e mudança nas regras do bloqueio de conta.
- O documento do fluxo 1 (autenticação) é atualizado no mesmo PR, com o 429, os logs, a configuração e as limitações.
- A issue #60 (rotação da chave do JWT) não é afetada, desde que a forma não legível da contagem não dependa da chave de assinatura do JWT.
