# Feature Specification: Análise estática de código com Quality Gate no CI

**Feature Branch**: `feature/req-20-sonarqube`

**Requisito**: REQ-20 (GitHub issue #18 — "[Qualidade] Análise estática de código com SonarQube Cloud")

**Created**: 2026-11-02

**Status**: Draft

**Input**: User description: "REQ-20 (issue #18): integrar o SonarQube Cloud ao CI para análise estática de código em todo PR e todo push na main, com os resultados visíveis no PR, Quality Gate bloqueando o merge e a cobertura de testes (unitários e de integração com Testcontainers) aparecendo no Sonar. Organização lucasfrenhan, projeto LucasFrenhan_Order-Management-System, Automatic Analysis desativada e secret SONAR_TOKEN já criados pelo usuário. Escopo completo no corpo da issue #18."

## Contexto

O projeto prova a qualidade por testes automatizados (Princípio V) e pela compatibilidade do
contrato v1. Não existe uma análise estática que acompanhe, ao longo do tempo, bugs,
vulnerabilidades, pontos sensíveis de segurança, código duplicado e a cobertura dos testes, nem
um critério objetivo que impeça um PR de piorar esses indicadores.

Hoje:

- a verificação `build-testes` roda só nos PRs, desde que deixou de rodar no push da `main`
  (para não repetir o que o PR já verificou);
- a cobertura é medida no CI, mas só aparece num resumo do job e não é comparada entre PRs;
- **a `main` não tem nenhuma regra de proteção**: nada impede o merge de um PR com a
  verificação vermelha, nem um push direto.

O serviço de análise já está preparado pelo dono do repositório: organização `lucasfrenhan`,
projeto `LucasFrenhan_Order-Management-System`, análise automática do serviço desativada e a
credencial de análise guardada como segredo do repositório.

## Clarifications

### Session 2026-11-02 (specify)

- Q: Quais verificações a proteção da `main` exige e o dono pode contorná-la? → A: Exige
  `Build e testes`, `Demonstração` e o Quality Gate, só por PR, sem contorno nem para o dono;
  numa queda do serviço de análise, a regra é desligada temporariamente de forma registrada
  (FR-014, FR-015).

## User Scenarios & Testing *(mandatory)*

Os "usuários" deste requisito são quem abre e revisa PRs (o dono do repositório e quem avalia o
projeto) e quem acompanha a saúde do código ao longo do tempo.

### User Story 1 - Todo PR é analisado e o resultado aparece no próprio PR (Priority: P1)

Ao abrir ou atualizar um PR para a `main`, o código é analisado automaticamente. O PR mostra se
o código novo atende ao critério de qualidade (Quality Gate), com um resumo dos problemas
encontrados e um link para o detalhe.

**Why this priority**: É a base do requisito: sem a análise no PR, não há o que bloquear nem o
que acompanhar.

**Independent Test**: Abrir um PR qualquer e conferir, no próprio PR, a verificação da análise
com o resultado do Quality Gate e o resumo com bugs, vulnerabilidades, pontos de segurança,
cobertura e duplicação do código novo.

**Acceptance Scenarios**:

1. **Given** um PR aberto para a `main`, **When** o CI roda, **Then** o código é compilado,
   testado e analisado, e o PR recebe uma verificação com o resultado do Quality Gate e um
   resumo com link para o detalhe.
2. **Given** um PR atualizado com um novo push, **When** o CI roda de novo, **Then** a análise
   e o resumo refletem o código mais recente.
3. **Given** um PR vindo de um fork, sem acesso à credencial de análise, **When** o CI roda,
   **Then** compila e testa normalmente, e só a etapa de análise é pulada, sem quebrar a
   verificação.

---

### User Story 2 - Um PR que piora a qualidade não pode ser mergeado (Priority: P1)

Se o código novo de um PR traz bug, vulnerabilidade, ponto de segurança sem revisão, cobertura
abaixo do limite ou duplicação acima do limite, o Quality Gate falha e o merge fica bloqueado
até o problema ser resolvido.

**Why this priority**: É o objetivo central da issue: um critério objetivo que impede a piora,
e não só um relatório.

**Independent Test**: Abrir um PR com um bug introduzido de propósito (por exemplo, uma
possível referência nula) e outro com código novo sem testes; conferir que os dois ficam com o
Quality Gate vermelho e que o botão de merge fica bloqueado.

**Acceptance Scenarios**:

1. **Given** um PR com um bug introduzido de propósito, **When** a análise termina, **Then** o
   Quality Gate falha e o merge fica bloqueado.
2. **Given** um PR com código novo sem testes, abaixo do limite de cobertura do código novo,
   **When** a análise termina, **Then** o Quality Gate falha e o merge fica bloqueado.
3. **Given** um PR que corrige o problema, **When** a análise roda de novo e o Quality Gate
   passa, **Then** o merge é liberado (desde que as demais verificações exigidas também passem).
4. **Given** a regra de proteção da `main`, **When** alguém tenta fazer push direto na `main`,
   **Then** o push é recusado: as mudanças entram só por PR com as verificações exigidas
   aprovadas.

---

### User Story 3 - A cobertura dos testes aparece completa e confiável (Priority: P1)

A cobertura mostrada na análise soma os testes unitários e os de integração (que rodam com
banco, broker e a API de pagamento simulada reais). Código gerado e código de teste não entram
na conta.

**Why this priority**: O Quality Gate usa a cobertura do código novo. Uma cobertura zerada ou
distorcida bloquearia PRs bons ou deixaria passar PRs sem teste.

**Independent Test**: Depois da primeira análise da `main`, conferir que a cobertura é diferente
de 0%, que trechos cobertos só por testes de integração aparecem como cobertos e que as
migrations e os projetos de teste não aparecem como código de produção.

**Acceptance Scenarios**:

1. **Given** a análise da `main`, **When** o painel do projeto é aberto, **Then** a cobertura é
   diferente de 0% e soma os testes unitários e de integração.
2. **Given** um trecho de código exercitado só por um teste de integração (por exemplo, a
   persistência), **When** a cobertura é lida, **Then** o trecho aparece como coberto.
3. **Given** as migrations do banco e os projetos de teste, **When** a análise roda, **Then**
   eles não aparecem como código de produção e não baixam a cobertura.
4. **Given** as especificações e a configuração do Spec Kit, **When** a análise roda, **Then**
   elas ficam fora da análise.

---

### User Story 4 - A situação de partida está medida e os pontos de segurança revisados (Priority: P2)

A primeira análise da `main` registra a situação de partida (bugs, vulnerabilidades, problemas
de manutenção, cobertura e duplicação). Os pontos sensíveis de segurança já esperados, como as
credenciais somente de desenvolvimento dos arquivos de configuração local, são revisados um a
um e marcados com a justificativa, sem desligar a regra que os detecta. Bugs e vulnerabilidades
encontrados são corrigidos; os problemas de manutenção restantes viram dívida técnica
registrada.

**Why this priority**: Sem a situação de partida, o primeiro PR depois deste requisito
herdaria a revisão de todos os achados antigos; e o critério "nenhum ponto de segurança sem
revisão" exige que os antigos estejam revisados.

**Independent Test**: Conferir o registro da situação de partida no estudo do requisito e, no
painel do projeto, nenhum ponto de segurança pendente de revisão, nenhum bug e nenhuma
vulnerabilidade abertos.

**Acceptance Scenarios**:

1. **Given** a primeira análise da `main`, **When** ela termina, **Then** os números de partida
   ficam registrados no estudo do requisito, com a data.
2. **Given** os pontos de segurança das credenciais de desenvolvimento, **When** são revisados,
   **Then** cada um fica marcado como seguro com a justificativa (valores só de contêiner
   local, documentados no README), e a regra continua ativa para código novo.
3. **Given** bugs e vulnerabilidades encontrados na situação de partida, **When** o requisito
   termina, **Then** estão corrigidos ou, se forem falso positivo, marcados com a
   justificativa.
4. **Given** problemas de manutenção da situação de partida, **When** o requisito termina,
   **Then** os que não forem corrigidos ficam registrados como dívida técnica.

---

### User Story 5 - Quem chega ao projeto sabe onde ver a análise e o que fazer quando ela falha (Priority: P3)

O README mostra o estado atual do Quality Gate e da cobertura, explica onde ver a análise de um
PR e o que fazer quando o Quality Gate falha, e recomenda (opcionalmente) ver os mesmos
problemas no editor.

**Why this priority**: Facilita a avaliação do projeto e o uso diário, mas o requisito funciona
sem isso.

**Independent Test**: Abrir o README e, só com ele, encontrar o painel da análise, o resultado
atual e o caminho para corrigir um Quality Gate vermelho.

**Acceptance Scenarios**:

1. **Given** o README, **When** alguém o abre, **Then** vê o estado atual do Quality Gate e da
   cobertura, com link para o painel.
2. **Given** um PR com o Quality Gate vermelho, **When** o autor segue a seção do README,
   **Then** encontra os problemas e sabe como corrigi-los ou justificá-los.

---

### Edge Cases

- **Serviço de análise fora do ar ou lento**: a verificação da análise não conclui e o merge
  fica bloqueado até ela rodar de novo; o dono do repositório pode rodar a verificação de novo.
  Compilar e testar não dependem do serviço.
- **Credencial de análise inválida, expirada ou ausente** (fora de forks): a etapa de análise
  falha com uma mensagem clara, sem expor a credencial, e o Quality Gate não aparece; o merge
  fica bloqueado.
- **PR de fork**: build e testes rodam; a análise é pulada sem falhar (a credencial não está
  disponível para forks). O merge desses PRs fica a critério do dono, que pode analisar depois
  numa branch própria.
- **Testes de integração sem Docker no CI**: continuam falhando como hoje (REQ-07), e a análise
  não roda sobre uma cobertura incompleta.
- **Código gerado na compilação** (mensagens de log, OpenAPI): fora da análise e da cobertura,
  como já é fora da cobertura hoje.
- **PR que só muda documentação ou especificações**: a análise roda e passa (sem código novo).
- **Mudança grande de uma vez** (como a primeira análise com o repositório inteiro): vale para
  a `main`; nos PRs, o Quality Gate olha só o código novo do PR.
- **Ponto de segurança novo em configuração de desenvolvimento** (por exemplo, uma senha local
  nova): bloqueia o PR até ser revisado e justificado, como os da situação de partida.

## Requirements *(mandatory)*

### Functional Requirements

**Análise no CI**

- **FR-001**: Todo PR para a `main` MUST ter o código compilado, testado e analisado
  automaticamente, e o resultado do Quality Gate MUST aparecer no próprio PR, com um resumo e
  um link para o detalhe.
- **FR-002**: Todo push na `main` (o merge de um PR) MUST ter o código analisado, para manter a
  referência de comparação dos PRs e o estado mostrado no README atualizados.
- **FR-003**: A análise MUST usar os resultados de compilação e de testes da mesma execução do
  CI, sem depender de uma análise automática separada do serviço.
- **FR-004**: Em PRs vindos de forks, sem a credencial de análise, o CI MUST compilar e testar
  normalmente e pular só a análise, sem falhar.
- **FR-005**: A credencial de análise MUST vir só do segredo do repositório e MUST NOT aparecer
  em logs, arquivos versionados ou artefatos do CI.
- **FR-006**: A análise MUST conhecer o histórico completo do repositório, para identificar o
  código novo e a autoria de cada linha.
- **FR-007**: O CI MUST manter o padrão de segurança dos workflows existentes: ações fixadas
  por versão exata, permissões mínimas e versões fixas das ferramentas.

**Escopo da análise e cobertura**

- **FR-008**: A cobertura enviada à análise MUST somar os testes unitários e os de integração
  (incluindo os que usam banco, broker e a API de pagamento simulada reais).
- **FR-009**: As migrations do banco e o código gerado na compilação MUST ficar fora da análise
  de problemas e da cobertura.
- **FR-010**: Os projetos de teste MUST ser tratados como código de teste, e não como código de
  produção.
- **FR-011**: As especificações (`specs/`) e a configuração do Spec Kit (`.specify/`) MUST
  ficar fora da análise.
- **FR-012**: A API de pagamento simulada MUST ser analisada como código de produção do
  repositório (ela roda no ambiente local, na demonstração e no CI).

**Quality Gate e proteção da `main`**

- **FR-013**: O Quality Gate MUST avaliar o código novo com, no mínimo: nenhum bug novo,
  nenhuma vulnerabilidade nova, todos os pontos de segurança revisados, cobertura do código
  novo de pelo menos 80% e duplicação do código novo de no máximo 3%.
- **FR-014**: A `main` MUST ter uma regra de proteção que exija, para o merge, as verificações
  `Build e testes` e `Demonstração` e a verificação do Quality Gate, aprovadas. A regra MUST
  valer também para o dono do repositório, sem contorno; numa indisponibilidade do serviço de
  análise, a regra só pode ser desligada temporariamente nas configurações do repositório, de
  forma registrada.
- **FR-015**: Mudanças na `main` MUST entrar só por PR (sem push direto), com as verificações
  exigidas aprovadas.

**Situação de partida**

- **FR-016**: A primeira análise da `main` MUST ser registrada no estudo do requisito, com
  bugs, vulnerabilidades, pontos de segurança, problemas de manutenção, cobertura e duplicação,
  e a data.
- **FR-017**: Todos os pontos de segurança da situação de partida MUST ser revisados um a um,
  com a decisão e a justificativa registradas no serviço de análise e no estudo do requisito;
  a regra que os detecta MUST NOT ser desligada.
- **FR-018**: Bugs e vulnerabilidades da situação de partida MUST ser corrigidos neste
  requisito ou, se forem falso positivo, marcados com a justificativa. Problemas de
  manutenção não corrigidos MUST ficar registrados como dívida técnica.

**Documentação**

- **FR-019**: O README MUST mostrar o estado atual do Quality Gate e da cobertura, com link
  para o painel do projeto, e MUST explicar onde ver a análise de um PR e o que fazer quando o
  Quality Gate falha.
- **FR-020**: O README MUST registrar a organização e o projeto do serviço de análise (não são
  segredos) e MAY recomendar a análise no editor, em modo conectado.

### Key Entities

- **Análise**: o resultado de uma execução para um PR ou para a `main`: problemas encontrados,
  cobertura, duplicação e o resultado do Quality Gate.
- **Quality Gate**: o conjunto de condições sobre o código novo que decide se a análise passa.
- **Ponto de segurança (security hotspot)**: trecho sensível que exige revisão humana; fica
  pendente até ser marcado como seguro (com justificativa) ou corrigido.
- **Situação de partida (baseline)**: os números da primeira análise da `main`, registrados com
  a data.
- **Regra de proteção da `main`**: as verificações exigidas para o merge e a proibição do push
  direto.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos PRs para a `main` abertos depois deste requisito (fora os de fork)
  mostram o resultado do Quality Gate no próprio PR.
- **SC-002**: Um PR com um bug introduzido de propósito e um PR com código novo sem testes
  ficam ambos com o Quality Gate reprovado e com o merge bloqueado.
- **SC-003**: A cobertura do projeto no painel é diferente de 0% e fica a no máximo 2 pontos
  percentuais da cobertura que o CI já mede hoje para o mesmo código.
- **SC-004**: Ao fim do requisito, há 0 pontos de segurança pendentes de revisão, 0 bugs e 0
  vulnerabilidades abertos no painel da `main`.
- **SC-005**: A credencial de análise aparece em 0 logs e 0 arquivos versionados.
- **SC-006**: Um PR de fork completa build e testes sem falha por falta da credencial.
- **SC-007**: O tempo total da verificação de um PR cresce no máximo 5 minutos em relação a
  hoje.
- **SC-008**: Uma pessoa que não conhece o projeto encontra, só pelo README, o painel da
  análise e o caminho para corrigir um Quality Gate reprovado.

## Assumptions

- O serviço de análise usado é o SonarQube Cloud, no plano gratuito para repositórios
  públicos; a organização `lucasfrenhan`, o projeto `LucasFrenhan_Order-Management-System`, a
  análise automática desativada e o segredo da credencial já foram criados pelo dono do
  repositório (2026-11-02). Se o repositório virar privado sem plano pago, a alternativa
  (SonarQube Server auto-hospedado) fica registrada no estudo.
- O Quality Gate é o padrão do serviço ("Sonar way"), que já atende ao FR-013; um Quality Gate
  próprio só é criado se o padrão não atender e o plano permitir.
- A análise da `main` no push (FR-002) revê em parte a decisão de rodar a verificação só nos
  PRs: a análise da `main` é a referência dos PRs e do estado no README. Se possível, o push na
  `main` só analisa, sem repetir a demonstração.
- A medição de cobertura que o CI já faz pode ser reaproveitada; se a análise exigir outra
  ferramenta de cobertura, ela é uma dependência nova e precisa de justificativa no plano
  (constituição).
- A decoração do PR (verificação e resumo) é feita pelo próprio serviço de análise, pela
  integração com o GitHub já instalada na organização.
- Os problemas de manutenção (code smells) não bloqueiam este requisito; só bugs,
  vulnerabilidades e pontos de segurança da situação de partida bloqueiam.
- A regra de proteção da `main` só pode exigir a verificação do Quality Gate depois que ela
  rodar pela primeira vez; por isso a proteção é ligada no fim do requisito.
- Fora do escopo: SonarQube Server auto-hospedado, varredura de dependências vulneráveis
  (Dependabot) e CodeQL, correção de todos os code smells da situação de partida e qualquer
  mudança de comportamento do sistema.
