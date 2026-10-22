# Feature Specification: CI de build e testes e ambiente de demonstração

**Feature Branch**: `feature/req-07-ci-demo`

**Requisito**: REQ-07 (GitHub issue #15 — "[DevOps] CI de build e testes e ambiente de demonstração com Docker Compose")

**Created**: 2026-10-22

**Status**: Draft

**Input**: User description: "REQ-07 (GitHub issue #15): (A) pipeline de CI que, em todo pull_request e push na main, restaura, compila (warnings como erro) e roda os testes unitários e de integração, inclusive os que usam SQL Server e RabbitMQ reais via Testcontainers, falhando se os testes de integração forem ignorados por falta de Docker (variável OMS_EXIGIR_DOCKER=true no CI); publica resultados TRX e cobertura como artefato e resumo; actions fixadas por SHA, permissions mínimas; badge e instrução de branch protection no README. (B) ambiente de demonstração: Dockerfile multi-stage da API (sdk/aspnet 10.0 com versão fixa, usuário não root, .dockerignore), serviço api no docker-compose sob profile demo (depends_on healthy, Development, conexões apontando para os serviços do compose via variáveis OMS_*, porta OMS_API_PORTA padrão 5230, healthcheck em /health/pronto), sem segredos novos; job de CI que sobe o profile demo e verifica /health/pronto; seção \"Demonstração rápida\" no README. Fora do escopo: deploy/registry, SAST/Dependabot, roteiro .http."

## Contexto

Hoje o único workflow do repositório verifica a compatibilidade do contrato, e só quando
um PR altera o contrato. Nenhum PR passa automaticamente por build nem por testes. Assim, o
Princípio V da constituição ("a qualidade deve ser demonstrada por verificações
automatizadas") só é cumprido quando alguém roda os testes na própria máquina.

Os testes que precisam de banco e de broker reais são ignorados quando não há Docker. Isso
é cômodo no desenvolvimento local, mas um ambiente de CI sem Docker passaria "verde" sem
testar nada.

Para ver o sistema funcionando, hoje é preciso ter o SDK do .NET, subir as dependências e
iniciar a API em passos separados. Quem só quer assistir a uma demonstração enfrenta uma
barreira desnecessária.

Este requisito resolve as duas coisas:
- **Verificação automática**: todo PR e todo push na branch principal são verificados
  automaticamente, com os testes de integração obrigatoriamente executados.
- **Demonstração**: um único comando sobe o sistema inteiro, só com Docker.

Nenhum comportamento da API muda.

## Clarifications

### Session 2026-10-22

- Q: A cobertura de código deve ter um percentual mínimo que reprove a verificação? → A:
  Não. Ela é só informativa: o resumo mostra o percentual, sem reprovar. O código gerado
  (migrations) fica fora do cálculo, para o número refletir o código escrito.
- Q: A verificação da demonstração deve rodar em todo PR ou só quando mudam os arquivos
  que a afetam? → A: Em todo PR, num job separado que roda em paralelo com build e
  testes. Ele faz parte do workflow `build-testes` e também pode ser exigido pela
  proteção da branch.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Todo PR é verificado automaticamente (Priority: P1)

Quem abre ou atualiza um PR, ou faz push na branch principal, recebe automaticamente um
resultado de verificação. O resultado só fica verde se a solução compila sem erros nem
avisos e se todos os testes, unitários e de integração, passam.

**Why this priority**: Sem isso, um PR pode quebrar a build ou os testes e ainda assim ser
mergeado. Todos os requisitos seguintes dependem dessa rede de proteção.

**Independent Test**: Abrir um PR com a solução atual e ver o resultado verde. Abrir um PR
com um teste quebrado e ver o resultado vermelho. Abrir um PR com um aviso de compilação
novo e ver o resultado vermelho.

**Acceptance Scenarios**:

1. **Given** um PR para a branch principal, **When** ele é aberto ou recebe um commit,
   **Then** a verificação `build-testes` roda e aparece no PR.
2. **Given** um push na branch principal, **When** ele acontece, **Then** a verificação
   roda.
3. **Given** um PR com um teste falhando, **When** a verificação roda, **Then** o resultado
   é vermelho, e o nome do teste aparece no resultado.
4. **Given** um PR com erro de compilação ou com um aviso de compilação novo, **When** a
   verificação roda, **Then** o resultado é vermelho.
5. **Given** a solução atual, sem alterações, **When** a verificação roda, **Then** o
   resultado é verde.
6. **Given** dois commits seguidos no mesmo PR, **When** o segundo chega com a primeira
   verificação ainda em andamento, **Then** a verificação antiga é cancelada e só a nova
   conta.

---

### User Story 2 - Testes de integração nunca são pulados no CI (Priority: P1)

No CI, os testes que usam banco e broker reais precisam rodar de verdade. Se o ambiente de
CI não tiver Docker, a verificação falha, em vez de ignorar esses testes e ficar verde.

**Why this priority**: Um verde que não testou nada é pior que nenhuma verificação,
porque dá falsa segurança. A issue exige isso de forma explícita.

**Independent Test**: No CI, conferir que o resultado mostra os testes com banco real
executados, com zero ignorados. Simular a ausência de Docker com a exigência ligada e
conferir que os testes falham, em vez de serem ignorados.

**Acceptance Scenarios**:

1. **Given** a verificação no CI, **When** os testes terminam, **Then** o número de testes
   ignorados é zero, e os testes com banco e broker reais aparecem como executados.
2. **Given** a exigência de Docker ligada e nenhum Docker disponível, **When** um teste que
   precisa de banco ou broker real roda, **Then** ele falha com uma mensagem que diz que o
   Docker é obrigatório neste ambiente.
3. **Given** um desenvolvedor sem Docker na própria máquina, sem a exigência ligada,
   **When** ele roda os testes, **Then** os testes com banco e broker reais continuam sendo
   ignorados com "Docker indisponível", como hoje.

---

### User Story 3 - Resultado e cobertura visíveis no PR (Priority: P2)

Quem revisa um PR vê, sem baixar nada, quantos testes passaram, falharam e foram ignorados,
e o percentual de cobertura de código. Os arquivos completos ficam disponíveis para
download.

**Why this priority**: Facilita a revisão, mas a proteção principal já vem da US1 e da US2.

**Independent Test**: Abrir a execução da verificação e conferir o resumo com contagens e
cobertura, e os arquivos de resultado e cobertura para download.

**Acceptance Scenarios**:

1. **Given** uma verificação concluída, verde ou vermelha, **When** alguém abre o resumo
   da execução, **Then** vê o total de testes aprovados, falhos e ignorados de cada projeto
   de teste e o percentual de cobertura de linhas.
2. **Given** uma verificação concluída, **When** alguém procura os arquivos da execução,
   **Then** encontra os resultados dos testes e o relatório de cobertura, mantidos por um
   prazo limitado.
3. **Given** uma verificação com teste falhando, **When** alguém abre o resumo, **Then** o
   teste que falhou aparece pelo nome.

---

### User Story 4 - Demonstração com um comando, só com Docker (Priority: P1)

Uma pessoa avaliadora, com Docker e sem o SDK do .NET, sobe banco, broker e API com um
único comando. Em seguida, já consegue fazer login com o usuário de desenvolvimento e
navegar pelo contrato da API.

**Why this priority**: É a outra metade da issue e o que permite apresentar o projeto sem
preparar a máquina de quem assiste.

**Independent Test**: Numa máquina só com Docker, rodar o comando da demonstração.
Conferir os três serviços saudáveis, a prontidão da API respondendo 200 e o login do
administrador de desenvolvimento devolvendo um token. Depois, derrubar tudo com um
comando.

**Acceptance Scenarios**:

1. **Given** uma máquina com Docker e as imagens já baixadas, **When** a pessoa roda o
   comando da demonstração, **Then** banco, broker e API ficam saudáveis em menos de 3
   minutos.
2. **Given** a demonstração no ar, **When** se consulta a prontidão da API, **Then** a
   resposta é 200, com banco e broker prontos.
3. **Given** a demonstração no ar, **When** se faz login com o administrador de
   desenvolvimento, **Then** a resposta traz um token, e a interface de navegação do
   contrato está acessível.
4. **Given** o comando de sempre para subir só as dependências, sem a demonstração,
   **When** um desenvolvedor o roda, **Then** sobem apenas banco e broker, como hoje, e
   ele continua rodando a API pelo SDK.
5. **Given** a demonstração no ar, **When** a pessoa roda o comando de encerramento com
   remoção de dados, **Then** os três serviços e os volumes são removidos.
6. **Given** portas locais já ocupadas, **When** a pessoa define outras portas nas
   variáveis do ambiente local (incluindo a da API), **Then** a demonstração sobe nas
   portas escolhidas.

---

### User Story 5 - A demonstração é verificada no CI (Priority: P2)

O modo de demonstração não pode quebrar em silêncio. O CI sobe a demonstração e confere que
a API fica pronta.

**Why this priority**: Protege a US4 ao longo do tempo, mas depende dela.

**Independent Test**: No CI, conferir que a verificação da demonstração sobe os três
serviços e falha se a prontidão da API não responder 200.

**Acceptance Scenarios**:

1. **Given** um PR, **When** a verificação roda, **Then** um job de demonstração, em
   paralelo com build e testes, sobe a demonstração,
   espera os serviços ficarem saudáveis e confirma que a prontidão da API responde 200 e
   que o login do administrador devolve um token.
2. **Given** uma alteração que impede a API da demonstração de subir, **When** a
   verificação roda, **Then** o resultado é vermelho, e os registros da API aparecem no
   resultado da execução.
3. **Given** a verificação da demonstração terminada, **When** ela acaba, com sucesso ou
   não, **Then** os serviços e volumes criados por ela são removidos.

---

### Edge Cases

- PR que só altera documentação: a verificação roda do mesmo jeito. Não há filtro de
  caminhos, porque a proteção da branch exige o resultado em todo PR.
- PR aberto a partir de um fork: a verificação roda com as mesmas permissões mínimas, sem
  acesso a segredos (não há segredos envolvidos).
- Download de pacotes falhando por instabilidade de rede: a verificação falha e pode ser
  executada de novo. Não há repetição automática, para não mascarar problemas.
- Contêineres dos testes demorando a subir no CI: os testes usam os mesmos limites de tempo
  da máquina local. Uma demora maior reprova a verificação, sem tolerância especial.
- Banco da demonstração ainda inicializando quando a API sobe: a API só começa depois que
  banco e broker estão saudáveis.
- Porta da API já ocupada na máquina: o comando falha com a mensagem do Docker. A pessoa
  troca a porta pela variável de ambiente (US4, cenário 6).
- Demonstração e desenvolvimento local ao mesmo tempo: a demonstração usa a mesma porta da
  API rodando pelo SDK. As duas não sobem juntas sem trocar a porta, e o README avisa isso.

## Requirements *(mandatory)*

### Functional Requirements

**Verificação de build e testes**

- **FR-001**: Uma verificação chamada `build-testes` MUST rodar em todo PR para a branch
  principal e em todo push nela.
- **FR-002**: A verificação MUST restaurar dependências, compilar a solução em modo de
  release tratando avisos como erros, como já define a configuração do projeto, e rodar
  os dois projetos de teste. Qualquer falha MUST deixar o resultado vermelho.
- **FR-003**: A verificação MUST usar a versão do SDK fixada no repositório, e não uma
  versão escolhida pelo ambiente.
- **FR-004**: Os pacotes baixados MUST ser reaproveitados entre execuções, enquanto as
  definições de dependências não mudarem.
- **FR-005**: Execuções antigas do mesmo PR ou da mesma branch MUST ser canceladas quando
  uma nova começa.

**Testes de integração obrigatórios**

- **FR-006**: Uma variável de ambiente `OMS_EXIGIR_DOCKER` MUST controlar o que acontece
  quando o Docker não está disponível para os testes que precisam de banco ou broker reais:
  - com o valor `true`, esses testes MUST falhar, com a mensagem "Docker obrigatório neste
    ambiente (OMS_EXIGIR_DOCKER=true)";
  - sem a variável, ou com outro valor, eles continuam ignorados com "Docker indisponível",
    como hoje.
- **FR-007**: A verificação MUST definir `OMS_EXIGIR_DOCKER=true` e rodar num ambiente com
  Docker. O resultado MUST mostrar zero testes ignorados.

**Relatórios**

- **FR-008**: A verificação MUST publicar no resumo da execução:
  - o total de testes aprovados, falhos e ignorados por projeto;
  - os nomes dos testes que falharam;
  - o percentual de cobertura de linhas, sem contar o código gerado (migrations).

  O resumo MUST ser publicado também quando há falha. A cobertura é informativa e MUST
  NOT reprovar a verificação.
- **FR-009**: Os resultados dos testes e o relatório de cobertura MUST ser publicados como
  arquivos da execução, mantidos por 14 dias.

**Segurança da verificação**

- **FR-010**: A verificação MUST ter só permissão de leitura do repositório.
- **FR-011**: Toda ação de terceiros usada MUST ser fixada por identificador imutável de
  versão, como no workflow de contrato que já existe.
- **FR-012**: A verificação MUST NOT usar segredos do repositório. Todos os valores
  envolvidos são de desenvolvimento ou de teste.

**Ambiente de demonstração**

- **FR-013**: O repositório MUST ter uma imagem da API construída em duas etapas: uma de
  compilação, com o SDK, e uma de execução, só com o runtime. As versões das imagens base
  MUST ser fixas, sem "latest". A API MUST rodar como usuário sem privilégios de
  administrador.
- **FR-014**: A construção da imagem MUST ignorar artefatos locais e pastas que não fazem
  parte da API: saídas de compilação, controle de versão, testes e especificações.
- **FR-015**: O arquivo de composição MUST ter o serviço da API num perfil `demo`, que só
  sobe quando pedido. O comando atual, sem perfil, MUST continuar subindo apenas banco e
  broker.
- **FR-016**: Na demonstração, a API MUST:
  - iniciar só depois de banco e broker estarem saudáveis;
  - rodar no ambiente de desenvolvimento, aplicando migrations, criando perfis, usuários
    iniciais e catálogo, e expondo o contrato navegável;
  - apontar para os serviços da composição, reaproveitando as variáveis locais que já
    existem para portas e credenciais;
  - expor a porta definida por `OMS_API_PORTA`, com padrão 5230;
  - ter verificação de saúde baseada na prontidão.
- **FR-017**: `OMS_API_PORTA` MUST constar do exemplo de variáveis locais (`.env.example`).
  Nenhum segredo novo MUST ser versionado: a demonstração usa os valores de
  desenvolvimento já existentes e documentados como tais.
- **FR-018**: O comando de encerramento com remoção de volumes MUST remover os três
  serviços e todos os dados da demonstração.

**Verificação da demonstração**

- **FR-019**: O workflow `build-testes` MUST ter, em todo PR e push, um job de
  demonstração que roda em paralelo com build e testes. Ele sobe a demonstração, espera a
  saúde dos três serviços e confirma que a prontidão responde 200 e que o login do
  administrador de desenvolvimento devolve um token.
  - Em falha, o passo MUST mostrar os registros da API.
  - Ao fim, com sucesso ou não, MUST remover tudo o que criou.

**Documentação**

- **FR-020**: O README MUST ter:
  - o selo de status da verificação `build-testes`;
  - uma seção "Demonstração rápida", com o comando, os endereços (API, contrato navegável
    e painel do broker), os usuários de desenvolvimento e o comando de encerramento;
  - instruções para configurar a proteção da branch principal exigindo os dois jobs do
    workflow `build-testes` (build e testes, e demonstração). Essa configuração é manual
    no GitHub.

**Compatibilidade**

- **FR-021**: Nenhum comportamento da API e nenhum contrato MUST mudar. A suíte local
  continua rodando sem Docker, com os testes de banco ignorados.

### Key Entities *(include if feature involves data)*

Não há entidades de dados. Os artefatos são de entrega:

- **Verificação `build-testes`**: execução automática por PR e push, com resultado verde
  ou vermelho, resumo e arquivos.
- **Imagem da API**: pacote executável da API para a demonstração.
- **Perfil `demo`**: conjunto de serviços (banco, broker e API) que sobe com um comando.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% dos PRs para a branch principal recebem o resultado de `build-testes`.
- **SC-002**: Um PR com um teste quebrado, ou com um aviso de compilação novo, recebe
  resultado vermelho em 100% das vezes.
- **SC-003**: No CI, 0 testes ignorados. A ausência de Docker com a exigência ligada falha
  a verificação.
- **SC-004**: O resumo de toda execução mostra as contagens de testes e o percentual de
  cobertura.
- **SC-005**: Numa máquina só com Docker e com as imagens baixadas, a demonstração fica
  pronta em menos de 3 minutos, e o login do administrador devolve um token.
- **SC-006**: A verificação completa (build, testes e demonstração) termina em até 15
  minutos no CI.
- **SC-007**: A suíte local continua 100% verde, com e sem Docker, e o contrato v1 não
  muda.

## Assumptions

- O repositório fica no GitHub, e a verificação usa GitHub Actions, como o workflow de
  contrato que já existe. O executor padrão tem Docker, o que permite rodar os
  testes com Testcontainers.
- A proteção da branch principal é configurada manualmente por quem administra o
  repositório. Este requisito só documenta como fazer.
- A demonstração roda no ambiente de desenvolvimento: usa os mesmos usuários,
  credenciais e chave de assinatura de desenvolvimento que já estão versionados e marcados
  como tais. Ela não serve para produção.
- A demonstração usa HTTP simples, sem certificado, na porta da API.
- O percentual de cobertura é informativo. Não há limite mínimo que reprove a verificação
  neste requisito.
- Publicação da imagem em registro, deploy, análise estática e varredura de dependências
  ficam fora do escopo, como diz a issue.
