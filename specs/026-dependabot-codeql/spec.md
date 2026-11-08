# Feature Specification: Dependências vulneráveis e CodeQL na cadeia de build

**Feature Branch**: `feature/req-26-dependabot-codeql`

**Requisito**: REQ-26 (GitHub issue #59 — "[Segurança] Dependências vulneráveis (Dependabot) e CodeQL na cadeia de build")

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "REQ-26 (issue #59): proteger a cadeia de build com alertas de dependências vulneráveis, atualizações automáticas controladas (Dependabot para NuGet, ferramentas locais, GitHub Actions e imagens Docker, respeitando a regra das duas semanas), análise do CodeQL para C# e para os workflows em PR e na `main`, `dotnet list package --vulnerable` no CI reprovando com vulnerabilidade alta ou crítica, e triagem registrada dos alertas iniciais. Fora do escopo: SBOM e assinatura de imagens. Escopo completo no corpo da issue #59."

## Contexto

O REQ-20 trouxe a análise estática do SonarQube Cloud, com o Quality Gate exigido pelo ruleset
"Proteção da main" (só PR, só rebase, verificações `Build e testes`, `Demonstração` e
`SonarCloud Code Analysis`). Ficaram de fora, no REQ-07 e no REQ-20:

- **Dependências vulneráveis**: as versões estão fixas no `Directory.Packages.props`, em
  `.config/dotnet-tools.json`, nos `Dockerfile`s, no `docker-compose.yml` e nos workflows
  (actions fixadas por SHA). Nada avisa quando uma delas ganha uma vulnerabilidade conhecida ou
  fica desatualizada; a conferência com `dotnet list package --vulnerable` é manual e só foi
  feita quando um REQ trouxe pacote novo (por exemplo, o REQ-16).
- **CodeQL**: a análise semântica de segurança do GitHub, que segue o fluxo dos dados (injeção,
  SSRF, caminhos, desserialização) e complementa as regras do Sonar.

Situação do repositório em 2026-11-08 (público): Dependabot security updates desligado, alertas
de vulnerabilidade desligados, CodeQL disponível para `csharp` e `actions` mas não configurado.

O projeto tem uma regra já em uso: só adotar uma versão publicada há pelo menos duas semanas
(specs 002, 006, 016 e 019).

## Clarifications

### Session 2026-11-08 (specify)

- Q: As atualizações de segurança do Dependabot também esperam as duas semanas? → A: Não. Só
  as atualizações de versão esperam 14 dias; as que corrigem uma vulnerabilidade conhecida chegam
  assim que a correção sai, e passam pelas mesmas verificações (FR-004).
- Q: Como os PRs do Dependabot entram na `main`? → A: O dono revisa cada um e liga o auto-merge
  por rebase, como nos outros PRs; não há auto-merge automático (FR-007).
- Q: O CodeQL entra como verificação exigida no ruleset "Proteção da main"? → A: Sim: um alerta
  novo de segurança alta ou crítica (ou de erro) bloqueia o merge até ser corrigido ou dispensado
  com justificativa; numa queda do serviço, vale o procedimento do REQ-20 (FR-009).

### Session 2026-11-08 (clarify)

- Q: Uma vulnerabilidade alta ou crítica publicada para uma dependência que já está na `main`
  reprova todos os PRs, mesmo os que não mexem em dependências? → A: Sim. Todo PR reprova
  enquanto ela existir; sai pela correção (em geral o PR de segurança do Dependabot) ou por uma
  exceção registrada (FR-011, FR-013).
- Q: Como manter alinhadas as imagens fixadas no código dos testes (RabbitMQ, Mailpit) com as do
  ambiente, que o Dependabot atualiza? → A: Os testes passam a ler a versão de uma fonte única
  que o Dependabot atualiza; um PR de imagem atualiza ambiente e testes de uma vez (FR-019).
- Q: Qual conjunto de regras o CodeQL usa? → A: O padrão (`default`), só com regras de
  segurança de alta precisão, para que falsos positivos não travem PRs; a troca pelo estendido
  fica para depois, com a triagem limpa (FR-009).
- Q: As atualizações menores e de correção do NuGet vêm num PR único ou por família de pacotes?
  → A: Por família (por exemplo, EF Core e SqlClient com a ferramenta `dotnet-ef`,
  OpenTelemetry, bibliotecas de teste), mais um PR para o restante; Actions e Docker com um PR
  cada (FR-002).

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são o dono do repositório, que revisa e mergeia os PRs, e quem
avalia o projeto e precisa confiar que a cadeia de build não carrega dependências vulneráveis.

### User Story 1 - Uma dependência com vulnerabilidade alta ou crítica reprova o CI (Priority: P1)

Todo PR passa por uma verificação das dependências (diretas e transitivas) contra a base de
vulnerabilidades conhecidas. Se alguma tiver vulnerabilidade de severidade alta ou crítica, a
verificação `Build e testes` fica vermelha, com o pacote, a versão, a severidade e o link do
aviso no resumo do job, e o merge fica bloqueado.

**Why this priority**: É a defesa que não depende de nenhum serviço externo além do CI e que
transforma o problema em bloqueio, não em aviso. É o critério de aceite mais objetivo da issue.

**Independent Test**: Num PR de teste, fixar um pacote numa versão com vulnerabilidade alta
conhecida e conferir que o CI reprova com a mensagem certa; voltar a versão e conferir que fica
verde.

**Acceptance Scenarios**:

1. **Given** um PR que introduz um pacote com vulnerabilidade alta ou crítica (direta ou
   transitiva), **When** o CI roda, **Then** a verificação reprova e o resumo lista pacote,
   versão, severidade e link do aviso.
2. **Given** um PR em que as únicas vulnerabilidades são de severidade baixa ou moderada,
   **When** o CI roda, **Then** a verificação passa e o resumo lista essas vulnerabilidades como
   aviso.
3. **Given** um PR sem dependências vulneráveis, **When** o CI roda, **Then** a verificação
   passa e o resumo diz que nenhuma vulnerabilidade foi encontrada.
4. **Given** a base de vulnerabilidades inacessível durante o CI, **When** a verificação roda,
   **Then** ela reprova com uma mensagem que diz isso, em vez de passar sem ter conferido.

---

### User Story 2 - O CodeQL analisa todo PR e a `main` (Priority: P1)

Todo PR para a `main` e todo push na `main` passam pela análise do CodeQL para o código C# e
para os workflows do GitHub Actions. Os alertas aparecem no próprio PR (anotados nas linhas) e
na aba de segurança do repositório.

**Why this priority**: É a metade "análise de código" do objetivo, e a issue pede o CodeQL em
todo PR e na `main`.

**Independent Test**: Abrir um PR qualquer e conferir as verificações do CodeQL para as duas
linguagens; depois do merge, conferir a análise da `main` na aba de segurança.

**Acceptance Scenarios**:

1. **Given** um PR aberto ou atualizado para a `main`, **When** o CI roda, **Then** o CodeQL
   analisa C# e os workflows, e o PR recebe o resultado.
2. **Given** um PR que introduz uma falha de segurança detectável (por exemplo, um dado da
   requisição chegando sem tratamento a uma consulta ou a um caminho de arquivo), **When** o
   CodeQL termina, **Then** o alerta aparece anotado no PR com severidade e descrição.
3. **Given** um merge na `main`, **When** o push é analisado, **Then** a aba de segurança
   reflete o estado da `main`, que é a referência de comparação dos PRs.
4. **Given** o ruleset "Proteção da main", **When** um PR tem alerta novo do CodeQL de
   severidade de segurança alta ou crítica (ou de erro), **Then** o merge fica bloqueado até o
   alerta ser corrigido ou dispensado com justificativa (FR-009).

---

### User Story 3 - Atualizações chegam em PRs controlados (Priority: P2)

O Dependabot abre PRs de atualização para pacotes NuGet, ferramentas locais do .NET, actions dos
workflows (trocando o SHA fixado e o comentário da versão) e imagens Docker. Os PRs chegam
agrupados, numa frequência previsível, só com versões publicadas há pelo menos duas semanas, e
passam pelas mesmas verificações exigidas de qualquer PR.

**Why this priority**: Mantém as dependências em dia sem trabalho manual, mas depende de as
verificações (US1, US2) estarem no lugar para que o PR seja confiável.

**Independent Test**: Depois do merge, disparar a verificação do Dependabot pela aba de
dependências e conferir que os PRs abertos seguem o agrupamento, o prefixo de commit, a regra
das duas semanas e recebem todas as verificações exigidas, incluindo o Quality Gate.

**Acceptance Scenarios**:

1. **Given** uma versão nova de um pacote NuGet publicada há mais de duas semanas, **When** o
   Dependabot roda na frequência configurada, **Then** abre um PR (agrupado com as outras
   atualizações da mesma família de pacotes, FR-002) que altera o `Directory.Packages.props`.
2. **Given** uma versão publicada há menos de duas semanas, **When** o Dependabot roda, **Then**
   ela não é proposta (exceto correções de segurança, conforme FR-004).
3. **Given** uma action fixada por SHA com versão nova, **When** o Dependabot propõe a
   atualização, **Then** o PR troca o SHA e o comentário `# vX.Y.Z` juntos.
4. **Given** um PR do Dependabot, **When** o CI roda, **Then** recebe `Build e testes`,
   `Demonstração`, o Quality Gate do Sonar e o CodeQL, como qualquer PR, e só pode entrar por
   rebase com todas verdes.
5. **Given** uma atualização de versão maior (major), **When** o Dependabot a propõe, **Then**
   ela vem num PR separado das atualizações menores, para ser avaliada à parte.

---

### User Story 4 - Os alertas iniciais estão triados e registrados (Priority: P2)

Ao ligar os alertas do Dependabot e o CodeQL pela primeira vez, cada alerta encontrado é
triado: corrigido neste REQ, dispensado com justificativa ou registrado como pendência com
issue. A triagem fica num documento em `docs/`, e nenhum alerta aberto fica sem decisão.

**Why this priority**: Sem a triagem, a aba de segurança começa poluída e os alertas novos se
perdem no meio dos antigos.

**Independent Test**: Abrir a aba de segurança e o documento da triagem e conferir que cada
alerta existente tem uma decisão registrada.

**Acceptance Scenarios**:

1. **Given** os alertas encontrados na primeira análise, **When** a triagem termina, **Then**
   cada um está corrigido, dispensado com justificativa ou ligado a uma issue, e o documento
   lista todos.
2. **Given** um alerta dispensado como falso positivo, **When** alguém consulta o documento,
   **Then** encontra o motivo e a data da decisão.

---

### Edge Cases

- **PR do Dependabot sem acesso aos segredos**: os PRs do Dependabot não recebem os segredos
  comuns do repositório, então a análise do Sonar seria pulada e o Quality Gate exigido nunca
  chegaria. O token de análise precisa estar disponível também para o Dependabot (FR-006).
- **Verificação estrita com vários PRs do Dependabot**: o ruleset exige a branch atualizada com a
  `main`; depois de cada merge, os outros PRs precisam ser rebaseados (o Dependabot faz isso
  sozinho, ou pelo comando `@dependabot rebase`).
- **Vulnerabilidade nova numa dependência que já está na `main`**: todos os PRs passam a
  reprovar, mesmo os que não mexem em dependências, até o PR de segurança do Dependabot entrar
  ou uma exceção ser registrada (FR-011).
- **Vulnerabilidade sem correção publicada**: não há versão para atualizar. O CI segue
  reprovando até haver uma decisão registrada; a exceção é explícita, por pacote e aviso, com
  motivo e data de revisão (FR-013).
- **Correção de segurança publicada há menos de duas semanas**: conflita com a regra das duas
  semanas (FR-004).
- **Atualização que quebra o build ou os testes**: o PR do Dependabot fica vermelho e não entra;
  o dono decide entre corrigir na branch dele, ignorar a versão ou fechar.
- **Imagem atualizada no ambiente e esquecida nos testes**: não pode acontecer, porque os testes
  leem a versão da mesma fonte (FR-019).
- **Imagem com tag de CU (SQL Server) ou variantes `-noble`**: o Dependabot precisa propor só
  tags da mesma família, sem trocar a distribuição base.
- **Alerta do CodeQL nos workflows sobre o `pull_request_target`** do `prs-de-fork.yml`: é um
  uso deliberado e sem checkout do PR (REQ-20); deve ser triado e, se for o caso, dispensado com
  justificativa.
- **Queda do serviço de análise do CodeQL**: segue o mesmo procedimento
  do REQ-20 para o Sonar (desligamento temporário registrado da exigência).

## Requirements *(mandatory)*

### Functional Requirements

**Atualizações (Dependabot)**

- **FR-001**: O repositório MUST ter o Dependabot configurado para os ecossistemas NuGet
  (incluindo as ferramentas locais de `.config/dotnet-tools.json`), GitHub Actions e Docker
  (`Dockerfile`s e `docker-compose.yml`).
- **FR-002**: As atualizações de versão menores e de correção (minor/patch) MUST ser agrupadas:
  no NuGet, um PR por família de pacotes que evoluem juntos (por exemplo, EF Core e SqlClient com
  a ferramenta `dotnet-ef`, OpenTelemetry, bibliotecas de teste) e um PR para os demais pacotes;
  em GitHub Actions, nos `Dockerfile`s e no Compose, um PR por ecossistema. As atualizações maiores (major) MUST vir
  em PRs separados.
- **FR-003**: A frequência MUST ser semanal, com limite de PRs abertos por ecossistema para não
  inundar a fila.
- **FR-004**: As atualizações de versão MUST respeitar a regra das duas semanas: só propor
  versões publicadas há pelo menos 14 dias. As atualizações de segurança (que corrigem uma
  vulnerabilidade conhecida) MUST chegar assim que a correção é publicada, sem esperar as duas
  semanas.
- **FR-005**: Os commits dos PRs do Dependabot MUST seguir a convenção do projeto
  (`tipo(escopo): descrição`), com tipo `build` para dependências e `ci` para actions, sem ID de
  requisito (constituição 1.1.1).
- **FR-006**: Os PRs do Dependabot MUST receber todas as verificações exigidas pelo ruleset,
  incluindo a análise do Sonar; o token de análise MUST estar disponível também para o
  Dependabot, sem ampliar o acesso de PRs de forks.
- **FR-007**: Os PRs do Dependabot MUST entrar pela mesma regra de qualquer PR (só rebase, todas
  as verificações verdes, sem contorno do ruleset). O dono revisa cada PR do Dependabot e liga o
  auto-merge por rebase; nenhum PR do Dependabot é mergeado sem essa ação.
- **FR-019**: Cada imagem de terceiros usada pelos testes de integração e pelo ambiente local
  (RabbitMQ, Mailpit, base do SQL Server) MUST ter a versão declarada num único lugar que o
  Dependabot atualiza, e os testes MUST ler a versão dali, sem cópia no código. Um PR de imagem
  do Dependabot atualiza o ambiente e os testes juntos. As tags das imagens construídas pelo
  projeto (como a do SQL Server com Full-Text Search) não podem ficar presas a uma versão da base
  que o Dependabot muda sem atualizá-las.

**Alertas**

- **FR-008**: O repositório MUST ter os alertas do Dependabot e as atualizações de segurança do
  Dependabot ativados.

**CodeQL**

- **FR-009**: O CodeQL MUST analisar C# e os workflows do GitHub Actions em todo PR para a `main`
  e em todo push na `main`. O ruleset "Proteção da main" MUST exigir o resultado do CodeQL e
  bloquear o merge de PR com alerta novo de segurança alta ou crítica, ou de severidade erro,
  até o alerta ser corrigido ou dispensado com justificativa. O CodeQL MUST usar o conjunto de
  regras padrão (`default`); o estendido e o de qualidade ficam fora deste REQ.
- **FR-010**: Os alertas do CodeQL MUST aparecer anotados no PR e na aba de segurança do
  repositório.

**Verificação no CI**

- **FR-011**: A verificação `Build e testes` MUST conferir todas as dependências NuGet (diretas e
  transitivas) de todos os projetos contra a base de vulnerabilidades conhecidas e reprovar
  quando houver alguma de severidade alta ou crítica, em qualquer PR, inclusive os que não
  alteram dependências (a vulnerabilidade pode ter sido publicada depois de a dependência entrar
  na `main`).
- **FR-012**: O resumo do job MUST listar cada vulnerabilidade encontrada (pacote, versão,
  severidade, link do aviso), separando as que reprovam das que são só aviso; sem nenhuma, MUST
  dizer isso.
- **FR-013**: Uma exceção a FR-011 MUST ser explícita: por pacote e aviso, com motivo e data de
  revisão de no máximo 90 dias, versionada no repositório e citada no resumo do job. Sem exceção registrada, nenhuma
  vulnerabilidade alta ou crítica passa.
- **FR-014**: Se a conferência não conseguir consultar a base de vulnerabilidades, a verificação
  MUST reprovar com mensagem clara, nunca passar sem ter conferido.

**Triagem e documentação**

- **FR-015**: Todos os alertas existentes na ativação (Dependabot e CodeQL) MUST ser triados neste
  REQ: corrigidos, dispensados com justificativa ou ligados a uma issue.
- **FR-016**: A triagem, a configuração (ecossistemas, agrupamento, frequência, regra das duas
  semanas, exceções) e o procedimento para tratar um PR do Dependabot ou um alerta novo MUST ficar
  num documento em `docs/`, com link de volta ao README e entrada na linha `docs/` da seção
  Estrutura.
- **FR-017**: Qualquer acréscimo ao README (por exemplo, um badge ou uma linha sobre segurança da
  cadeia de build) MUST ter permissão do dono antes, pela regra do projeto; o detalhe fica no
  documento de FR-016.
- **FR-018**: A configuração do repositório que não fica em arquivo (alertas, security updates,
  ruleset, segredo do Dependabot) MUST ser registrada no documento de FR-016 e, no caso do
  ruleset, no arquivo versionado `.github/rulesets/protecao-main.json`.

### Key Entities

- **Dependência**: pacote NuGet, ferramenta local do .NET, action de workflow ou imagem Docker,
  com versão fixa num manifesto do repositório.
- **Aviso de vulnerabilidade**: registro público de uma falha numa versão de dependência, com
  severidade (baixa, moderada, alta, crítica) e, quando houver, a versão corrigida.
- **Alerta**: um aviso (Dependabot) ou achado (CodeQL) aberto contra o repositório, com estado
  (aberto, corrigido, dispensado) e, se dispensado, o motivo.
- **Exceção de vulnerabilidade**: decisão registrada de aceitar temporariamente uma
  vulnerabilidade alta ou crítica, com pacote, aviso, motivo e data de revisão.
- **Registro de triagem**: lista dos alertas iniciais com a decisão tomada para cada um.
- **Fonte das versões de imagem**: o lugar único onde cada imagem de terceiros tem a versão
  declarada, lido pelo ambiente local e pelos testes e atualizado pelo Dependabot.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Um PR que introduz uma dependência com vulnerabilidade alta ou crítica conhecida é
  reprovado em 100% dos casos, e o motivo está no resumo do job sem precisar abrir o log.
- **SC-002**: 100% dos PRs para a `main` e dos pushes na `main` depois do merge têm a análise do
  CodeQL para as duas linguagens.
- **SC-003**: Na primeira semana depois do merge, o Dependabot abre pelo menos um PR (ou confirma
  que tudo está atualizado) em cada um dos ecossistemas configurados, e nenhum PR propõe versão
  com menos de duas semanas de publicada, exceto as atualizações de segurança (FR-004).
- **SC-004**: Os PRs do Dependabot chegam com todas as verificações exigidas pelo ruleset, e pelo
  menos um deles é mergeado por rebase sem nenhum contorno.
- **SC-005**: Ao fim do REQ, 0 alertas abertos sem decisão registrada na aba de segurança.
- **SC-006**: O tempo da verificação `Build e testes` cresce no máximo 1 minuto com a conferência
  de vulnerabilidades.
- **SC-007**: Um PR com alerta novo do CodeQL de segurança alta ou crítica não pode ser mergeado
  enquanto o alerta estiver aberto.

## Assumptions

- O repositório é público, então os alertas do Dependabot, as atualizações de segurança e o
  CodeQL não têm custo.
- "Alta ou crítica" usa a severidade da base de avisos do GitHub, a mesma que a conferência de
  pacotes do .NET consulta.
- A conferência de FR-011 cobre o NuGet. Para actions e imagens Docker, a proteção vem do
  Dependabot (alertas e PRs) e do CodeQL dos workflows; uma varredura de imagens fica fora do
  escopo.
- A frequência semanal segue a prática comum e o volume do projeto (um mantenedor); com o
  agrupamento por família (FR-002), a fila fica em poucos PRs por semana.
- O secret scanning (também desligado hoje) não está no escopo da issue; pode virar uma issue
  à parte.
- O procedimento de queda de serviço do REQ-20 (desligar temporariamente uma exigência do
  ruleset de forma registrada) vale para o CodeQL.
- Fora do escopo, conforme a issue: SBOM e assinatura de imagens.
- Este REQ não muda nenhum fluxo de negócio: `docs/fluxos/` não é alterado.
