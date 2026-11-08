---

description: "Lista de tarefas do REQ-26 — Dependências vulneráveis e CodeQL na cadeia de build"
---

# Tasks: Dependências vulneráveis e CodeQL na cadeia de build

**Input**: Design documents from `/specs/026-dependabot-codeql/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/](./contracts/), [quickstart.md](./quickstart.md)

**Tests**: o roteiro de vulnerabilidades é validado com exemplos fixos (quickstart §2), e a leitura
das imagens do compose ganha testes automatizados (Princípio V). O restante é configuração de CI,
validada nos PRs (quickstart §3 a §8).

**Organization**: uma fase por história. **O MVP são as duas histórias P1**: US1 (vulnerabilidade
alta reprova o CI) e US2 (CodeQL em todo PR e na `main`). A entrega é em dois PRs (research
[R-10](./research.md#r-10--entrega-em-dois-prs)): as fases 1 a 7 formam o **PR 1** (`Refs #59`);
a fase 8 acontece depois do merge e forma o **PR 2** (`Closes #59`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história da spec (US1, US2, US3, US4)

## Convenções válidas para todas as tarefas

- Actions fixadas por SHA com o comentário `# vX.Y.Z`, só versões com duas semanas ou mais;
  `permissions` mínimas.
- Nenhum pacote NuGet novo.
- Mudança de configuração do repositório (alertas, segredo, ruleset) só com o aval do dono, pedido
  na hora.
- README só cresce com permissão (CLAUDE.md).
- Um commit por fase: `tipo(escopo): descrição [REQ-26]`.

---

## Phase 1: Setup

**Purpose**: registrar o ponto de partida.

- [X] T001 Registrar a linha de base: `dotnet build PedidoFulfillment.sln` e `dotnet test PedidoFulfillment.sln` (anotar o total de testes para a T027) e o retrato do Docker (contêineres, volumes e imagens) para conferir na T028
- [X] T002 [P] Conferir e anotar o estado do repositório da tabela "Estado no GitHub" do [data-model](./data-model.md): alertas do Dependabot, security updates, `code-scanning/default-setup` (`not-configured`), `gh secret list --app dependabot` e o ruleset 26400051

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: o restore para de transformar os avisos do NuGet Audit em erro, condição para a US1.

**⚠️ CRITICAL**: a US1 não funciona sem esta fase.

- [X] T003 Em `Directory.Build.props`, acrescentar `<WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>` com comentário citando `specs/026-dependabot-codeql/research.md` R-01 ("o NuGet Audit continua avisando; quem reprova alta e crítica é a verificação do CI; o NU1900 continua erro"). Conferir que `dotnet build PedidoFulfillment.sln` segue sem avisos

**Checkpoint**: restore e build iguais aos de antes (nenhuma vulnerabilidade hoje).

---

## Phase 3: User Story 1 - Uma dependência com vulnerabilidade alta ou crítica reprova o CI (Priority: P1) 🎯 MVP

**Goal**: o `Build e testes` reprova alta e crítica sem exceção vigente, avisa baixa e moderada, e mostra tudo no resumo do job.

**Independent Test**: os exemplos do quickstart §2 dão as saídas esperadas; num PR descartável com `System.Text.Json` 8.0.0, o passo "Vulnerabilidades" reprova (quickstart §3).

- [X] T004 [P] [US1] Criar `.github/vulnerabilidades-aceitas.json` com `{"excecoes": []}`
- [X] T005 [P] [US1] Criar os exemplos em `.github/scripts/exemplos-vulnerabilidades/`, no formato da saída `--format json` versão 1 (research R-02): `sem-vulnerabilidades.json`; `so-moderada.json` (uma `Moderate` direta); `alta-transitiva.json` (a mesma `High` transitiva em dois projetos, mais uma `Low`); `com-problema.json` (`problems` com `level: error`); `excecoes-vigente.json` e `excecoes-vencida.json` para a `High` de `alta-transitiva.json` (`revisarAte` 2026-12-01 e 2026-11-01)
- [X] T006 [US1] Implementar `.github/scripts/verificar-vulnerabilidades.ps1` (PowerShell 7, research R-02) conforme [contracts/verificacao-vulnerabilidades.md](./contracts/verificacao-vulnerabilidades.md): opções `-Entrada`, `-Excecoes` e `-Hoje`; códigos de saída 0, 1 e 2; validação do arquivo de exceções com as regras do data-model ("`pacote` obrigatório, comparado sem diferenciar maiúsculas", "`aviso` URL exatamente como `advisoryurl`", "`motivo` não vazio", "`registradaEm` e `revisarAte` `AAAA-MM-DD`, `revisarAte` no máximo 90 dias depois de `registradaEm`", chave `pacote` + `aviso` sem repetição, arquivo ausente = lista vazia, inválido = saída 2); exceção vencida não vale; "Exceções que podem sair"; tabela do resumo agrupando a mesma vulnerabilidade de vários projetos, na ordem reprova → exceção → aviso e severidade decrescente; anotação `::error::` em toda reprovação; resumo em `$GITHUB_STEP_SUMMARY` ou na saída padrão. `Set-StrictMode` e `$ErrorActionPreference = 'Stop'`; mais `exemplos-vulnerabilidades/conferir.ps1`, que roda os casos e sai com 1 se algum der outro código
- [X] T007 [US1] Rodar os casos do quickstart §2 (`conferir.ps1`, com `-Hoje 2026-11-08`) e conferir códigos e linhas; rodar contra a solução real (`dotnet restore` + roteiro sem `-Entrada`): saída 0 e "Nenhuma vulnerabilidade conhecida nas dependências NuGet."
- [X] T008 [US1] Em `.github/workflows/build-testes.yml`, acrescentar o passo "Vulnerabilidades" logo depois de "Restaurar" (`shell: pwsh`, rodando `conferir.ps1` e depois `verificar-vulnerabilidades.ps1`), com comentário citando o REQ-26 (FR-011 a FR-014) e atualizar o cabeçalho do arquivo

**Checkpoint**: US1 pronta localmente; a prova no CI (quickstart §3) vai na T032.

---

## Phase 4: User Story 2 - O CodeQL analisa todo PR e a `main` (Priority: P1) 🎯 MVP

**Goal**: análise do CodeQL para `actions` e `csharp` em todo PR, na `main` e semanal, com alertas anotados no PR.

**Independent Test**: no PR 1 aparecem `Analyze (actions)` e `Analyze (csharp)` terminados (quickstart §4).

- [X] T009 [US2] Criar `.github/workflows/codeql.yml` conforme [contracts/codeql.md](./contracts/codeql.md): gatilhos `pull_request` e `push` na `main` e `schedule` semanal (segunda, 09:00 UTC); `permissions` de workflow `contents: read` e de job `contents: read`, `security-events: write`, `actions: read`; matriz `language: [actions, csharp]`, `build-mode: none`, `fail-fast: false`; `actions/checkout` no mesmo SHA do `build-testes.yml`; `github/codeql-action/init` e `analyze` em `9b3254085494b55a0f7c4c2b3d2d52cc712ce370 # v4.39.1`, com `category: /language:${{ matrix.language }}`; conjunto de regras padrão; concorrência `codeql-${{ github.ref }}` cancelando só em PR; cabeçalho explicando o REQ-26 e o motivo do setup avançado (R-06)
- [X] T010 [US2] Validar a sintaxe do `codeql.yml` e do `build-testes.yml` alterado com `actionlint` pela imagem `rhysd/actionlint` fixada numa versão com duas semanas ou mais (`docker run --rm -v "$PWD:/repo" -w /repo rhysd/actionlint:<versão>`); sem Docker, a validação fica para o próprio PR (T030). Conferir que nenhuma action ficou sem SHA (`grep -n "uses:" .github/workflows/*.yml`)

**Checkpoint**: a análise roda de fato no PR 1 (T030); o plano B do C# (`build-mode: manual`) só entra se a extração falhar.

---

## Phase 5: User Story 3 - Atualizações chegam em PRs controlados (Priority: P2)

**Goal**: Dependabot configurado, PRs dele com todas as verificações, e imagens dos testes lidas da mesma fonte que ele atualiza.

**Independent Test**: depois do merge, PRs do Dependabot agrupados, com `build(deps)`/`ci(deps)`, nenhuma versão com menos de 14 dias e todas as verificações exigidas (quickstart §6); `grep` sem versão de imagem no código dos testes (quickstart §7).

### Imagens numa fonte única (FR-019, R-08)

- [X] T011 [P] [US3] Escrever os testes de `ImagensDoAmbiente` em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ImagensDoAmbienteTestes.cs`, sem Docker, com compose de exemplo em texto: serviço existente devolve a imagem; serviço ausente e serviço sem `image:` lançam `InvalidOperationException` com o nome do serviço e o caminho; "a imagem precisa ter tag e não pode ser `latest`"; chaves de outros serviços e de blocos aninhados (`environment`, `healthcheck`) não confundem a leitura; e, contra o `docker-compose.yml` real, `rabbitmq` e `mailpit` resolvem
- [X] T012 [US3] Implementar `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ImagensDoAmbiente.cs`: acha o `docker-compose.yml` subindo a partir de `AppContext.BaseDirectory` (como `ImagemSqlServerTeste.DiretorioDockerfile`), lê linha a linha o bloco `services:` (serviço com 2 espaços, `image:` com 4) e expõe `RabbitMq` e `Mailpit` (lazy) e um método de leitura testável a partir de texto; sem biblioteca de YAML
- [X] T013 [P] [US3] Trocar as versões fixas pela leitura: `rabbitmq:4.3.6-management` em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MensageriaTeste.cs` e `tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs`, e `axllent/mailpit:v1.33.0` em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MailpitTeste.cs`
- [X] T014 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ImagemSqlServerTeste.cs`, trocar o nome fixo `oms-sqlserver-testes:2022-CU26-fts` por `oms-sqlserver-testes:<16 primeiros hex do SHA-256 do conteúdo de docker/sqlserver>` (arquivos em ordem de caminho relativo), mantendo `OMS_IMAGEM_SQLSERVER` como atalho; atualizar o comentário
- [X] T015 [P] [US3] Em `docker-compose.yml`, trocar a tag `oms-sqlserver:2022-CU26-fts` por `oms-sqlserver:fts` e o comentário "atualizar de forma manual e deliberada" do cabeçalho por um que diga que o Dependabot propõe as atualizações (REQ-26) e que os testes leem as versões daqui; conferir com `docker compose config`
- [X] T016 [US3] Rodar os testes de `ImagensDoAmbiente` (quickstart §7) e a suíte de integração inteira com Docker; `grep -rn "rabbitmq:\|mailpit:v\|CU26" tests/` sem resultado

### Dependabot (FR-001 a FR-007, R-04, R-05)

- [X] T017 [US3] Criar `.github/dependabot.yml` conforme [contracts/dependabot.md](./contracts/dependabot.md): versão 2; blocos `nuget` (`/`), `github-actions` (`/`), `docker` (`/docker/sqlserver`, `/src/PedidoFulfillment.API`, `/src/PedidoFulfillment.Worker`, `/src/PedidoFulfillment.PagamentosSimulados`) e `docker-compose` (`/`); em todos `schedule` semanal (segunda, `06:00`, `America/Sao_Paulo`), `cooldown.default-days: 14`, `open-pull-requests-limit: 5`, `commit-message` com `prefix: build` (`ci` em Actions) e `include: scope`; grupos na ordem do contrato, todos com `update-types: [minor, patch]`; `ignore` de `FluentAssertions` `>= 8.0.0` e de `oms-sqlserver` no compose; comentário no topo citando o REQ-26
- [X] T018 [US3] Ligar pelo `gh` os alertas do Dependabot e as atualizações de segurança (`gh api -X PUT repos/LucasFrenhan/Order-Management-System/vulnerability-alerts` e `.../automated-security-fixes`; o dono autorizou fazer via `gh`) e conferir com a tabela do data-model (FR-008)
- [ ] T019 [US3] Cadastrar o `SONAR_TOKEN` como segredo de Dependabot (FR-006). O valor não fica acessível pelo `gh` (só o dono o tem), então entregar ao dono o passo a passo: SonarQube Cloud → My Account → Security → gerar um token (ou reaproveitar o atual) e, no GitHub, Settings → Secrets and variables → Dependabot → New repository secret `SONAR_TOKEN`; como alternativa, `gh secret set SONAR_TOKEN --app dependabot` no terminal dele. Conferir com `gh secret list --app dependabot`

**Checkpoint**: configuração pronta; o comportamento do Dependabot só aparece depois do merge (T038, T039).

---

## Phase 6: User Story 4 - Os alertas iniciais estão triados e registrados (Priority: P2)

**Goal**: documento com a configuração, os procedimentos e a tabela de triagem.

**Independent Test**: cada alerta aberto tem decisão registrada (quickstart §8, depois do merge).

- [X] T020 [US4] Criar `docs/seguranca-cadeia-build.md` com `[← Voltar ao README](../README.md)` no topo: o que protege a cadeia de build (verificação de vulnerabilidades, Dependabot, CodeQL); a regra das duas semanas e a exceção das atualizações de segurança; os grupos e a frequência; como tratar um PR do Dependabot (revisar, `gh pr merge <n> --rebase --auto`, `@dependabot rebase`, `@dependabot ignore`); como registrar uma exceção de vulnerabilidade (campos e prazo de 90 dias); que um PR do Dependabot que acrescente um pacote novo ao `Directory.Packages.props` (por exemplo, um pino transitivo de segurança) só entra com um comentário de justificativa junto da entrada, como no grupo "Correções de segurança transitivas" (constituição: dependência nova exige justificativa); que os segredos de Dependabot só chegam aos PRs abertos pelo Dependabot, nunca a forks (FR-006); como tratar um alerta do CodeQL (corrigir ou dispensar com motivo); o SDK do `global.json` como atualização manual; a configuração fora de arquivo (tabela "Estado no GitHub", FR-018); procedimento de queda do CodeQL; e a seção "Triagem inicial" com a tabela do data-model, ainda vazia
- [X] T021 [US4] Acrescentar o link de `docs/seguranca-cadeia-build.md` na linha `docs/` da seção Estrutura do `README.md` (permitido sem pedir pela regra do CLAUDE.md)
- [X] T022 [US4] Registrar na tabela de triagem os alertas do Dependabot que aparecerem logo depois da T018 (`gh api repos/LucasFrenhan/Order-Management-System/dependabot/alerts?state=open`), com decisão; se não houver nenhum, registrar "nenhum alerta na ativação (AAAA-MM-DD)"

---

## Phase 7: Polish do PR 1

**Purpose**: documentação cruzada, verificações finais e abertura do PR 1.

- [X] T023 [P] Atualizar `docs/integracao-continua.md`: o passo "Vulnerabilidades" no job **Build e testes** (o que o deixa vermelho e onde ver o resumo), o workflow `codeql` e o link para `docs/seguranca-cadeia-build.md`; trocar a menção à tag `2022-CU26-fts`
- [X] T024 [P] Conferir no `docs/analise-estatica.md` se algo cita "dependências vulneráveis" ou o CodeQL como fora do escopo e apontar para o documento novo
- [X] T025 Perguntar ao dono, antes de mexer, se o `README.md` deve ganhar algo além da linha da T021 (por exemplo, o badge do CodeQL ou uma linha sobre segurança da cadeia de build, critério de aceite da issue), propondo a alternativa de deixar o detalhe só em `docs/seguranca-cadeia-build.md`; aplicar só o que for autorizado
- [X] T043 Ajustar a regra "só começar uma nova spec com tudo mergeado" do `CLAUDE.md`: os PRs abertos pelo Dependabot não contam (`gh pr list --state open --search "-author:app/dependabot"` precisa vir vazio), mas uma atualização de segurança do Dependabot aberta é avisada ao dono antes de começar o REQ; atualizar a memória `fluxo-sdd-por-req` (achado G1 do analyze)
- [X] T026 Conferir os links relativos dos documentos alterados e que `docs/fluxos/` não precisa mudar (nenhum fluxo de negócio muda; o PR diz isso)
- [X] T027 Rodar `dotnet build PedidoFulfillment.sln`, `dotnet test PedidoFulfillment.sln` (comparar o total com a T001) e `dotnet format PedidoFulfillment.sln --verify-no-changes`
- [X] T028 Conferir o Docker contra o retrato da T001: nada criado pelos testes fica na máquina (imagens `oms-sqlserver-testes:*` novas incluídas, exceto a da tag atual se o retrato já tinha a anterior)
- [X] T029 Marcar as tarefas feitas neste arquivo, fazer `git push` e abrir o PR 1 em português (Resumo, Pontos de atenção, Validação) com `Refs #59`, citando os documentos atualizados e que `docs/fluxos/` não muda
- [X] T030 Acompanhar o CI do PR 1: `Build e testes` (com o passo "Vulnerabilidades" e a mensagem de nenhuma vulnerabilidade), `Demonstração`, Quality Gate e `Analyze (actions)`/`Analyze (csharp)`; medir o SC-006 comparando a duração do passo "Vulnerabilidades" e a do job com o último run do `build-testes` na `main` (`gh run view <id> --json jobs`) e anotar no PR; se o C# falhar na extração, aplicar o plano B de [contracts/codeql.md](./contracts/codeql.md) e registrar no research
- [X] T031 [US2] Registrar no PR 1 os alertas que o CodeQL anotar e corrigir os que forem do código do PR; os demais vão para a triagem (T037)
- [X] T032 [US1] Prova do SC-001 (quickstart §3): branch descartável a partir da do PR 1 com `System.Data.SqlClient` 4.8.5 (High) no `Directory.Packages.props` e no projeto de testes unitários (quickstart §3), PR em rascunho, conferir a reprovação e o resumo, anotar o link do run no PR 1, fechar sem merge e apagar a branch
- [X] T033 Ligar o auto-merge do PR 1 (`gh pr merge <n> --rebase --auto`), sem apagar a branch

**Checkpoint**: PR 1 mergeado; o Dependabot e o CodeQL passam a valer na `main`.

---

## Phase 8: Depois do merge — PR 2 (`Closes #59`)

**Purpose**: comportamento que só existe com a configuração na `main`: análise da `main`, PRs do Dependabot, triagem e exigência do CodeQL.

- [X] T034 Sincronizar a `main`, criar `feature/req-26-dependabot-codeql-triagem` a partir dela e conferir as análises da `main` (quickstart §4)
- [X] T035 [US2] Acrescentar a regra `code_scanning` de [contracts/codeql.md](./contracts/codeql.md) (`tool: CodeQL`, `security_alerts_threshold: high_or_higher`, `alerts_threshold: errors`) a `.github/rulesets/protecao-main.json` e aplicar pelo `gh` ao ruleset 26400051 (`gh api -X PUT repos/LucasFrenhan/Order-Management-System/rulesets/26400051 --input .github/rulesets/protecao-main.json`); conferir que as verificações exigidas atuais continuam
- [X] T036 [US2] Prova do SC-007 (quickstart §5): PR descartável com um alerta de segurança alta (caminho de arquivo a partir de parâmetro da requisição), conferir o merge bloqueado pela regra, fechar sem merge e apagar a branch
- [X] T037 [US4] Triar os alertas do CodeQL da `main` (`gh api repos/LucasFrenhan/Order-Management-System/code-scanning/alerts?state=open`) e os do Dependabot: corrigir neste PR, dispensar com `dismissed_reason` e `dismissed_comment`, ou abrir issue; registrar cada um na tabela de `docs/seguranca-cadeia-build.md` (origem, alerta, regra/aviso, severidade, decisão, data). Avaliar em especial o `pull_request_target` de `.github/workflows/prs-de-fork.yml`
- [X] T038 [US3] Disparar ou esperar a primeira verificação do Dependabot e conferir cada ecossistema na tela de dependências (quickstart §6); registrar em `docs/seguranca-cadeia-build.md` o que ele fez com a base do SQL Server (`2022-CU26-ubuntu-22.04`, R-04) e ajustar o `dependabot.yml` neste PR se for preciso
- [X] T039 [US3] Conferir os PRs do Dependabot (`gh pr list --author app/dependabot`): títulos `build(deps)`/`ci(deps)`, grupos, major separado, nenhuma versão com menos de 14 dias e todas as verificações exigidas, incluindo o Quality Gate; com o aval do dono, mergear pelo menos um por rebase (SC-004) e anotar o número no PR 2
- [X] T040 Atualizar `docs/proximos-passos.md` (a #59 sai da fila e o texto de abertura cita o REQ-26) e, se autorizado na T025, o `README.md`
- [X] T041 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`; conferir o Docker como na T028
- [X] T042 Marcar as tarefas, abrir o PR 2 em português com `Closes #59` (Resumo, Pontos de atenção, Validação: links dos runs da T032 e da T036, PR do Dependabot mergeado, tabela da triagem, `docs/fluxos/` sem mudança) e ligar o auto-merge por rebase, sem apagar a branch

---

## Dependencies & Execution Order

- **Setup (1)** → **Foundational (2)** → US1 (3). US2 (4), US3 (5) e US4 (6) dependem só do Setup e podem andar em paralelo com a US1.
- **Polish do PR 1 (7)** depende das fases 3 a 6.
- A T043 entra na fase 7, antes da T026, apesar do número (acrescentada pelo `/speckit-analyze`).
- **Fase 8** depende do merge do PR 1. Dentro dela: T034 → T035 → T036; T037, T038 e T039 em paralelo depois da T034; T040 a T042 no fim.
- Dentro da US3: T011 → T012 → T013/T014 → T016; T015 e T017 independentes; T018 antes da T022.

## Parallel Example

```text
# Depois da T003:
T004 + T005 (US1)   |   T009 (US2)   |   T011 (US3)   |   T017 (US3)   |   T020 (US4)

# Depois da T012:
T013 + T014 + T015
```

## Implementation Strategy

1. **MVP (US1 + US2)**: fases 1 a 4 já entregam o bloqueio de vulnerabilidades e a análise do
   CodeQL; poderiam sair sozinhas num PR se o Dependabot atrasasse.
2. **Incremento (US3 + US4)**: Dependabot, fonte única das imagens e o documento, ainda no PR 1.
3. **Depois do merge**: exigência do CodeQL, triagem e prova do fluxo do Dependabot no PR 2.

Commits por fase: `chore(build)` (fase 2), `ci(vulnerabilidades)` (fase 3), `ci(codeql)` (fase 4),
`test(imagens)` e `ci(dependabot)` (fase 5), `docs(seguranca)` (fase 6), `docs(ci)` (fase 7), todos
com `[REQ-26]`.
