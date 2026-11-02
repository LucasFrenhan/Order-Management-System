---

description: "Lista de tarefas do REQ-20 — Análise estática com SonarQube Cloud e Quality Gate no CI"
---

# Tasks: Análise estática de código com Quality Gate no CI

**Input**: Design documents from `/specs/020-sonarqube-cloud/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/analise-no-ci.md](./contracts/analise-no-ci.md),
[quickstart.md](./quickstart.md)

**Tests**: o requisito muda o CI, não o código do sistema, então não há teste automatizado novo.
A verificação é pelo próprio CI e pelo painel: a análise do PR, PRs descartáveis que forçam o
Quality Gate a reprovar ([quickstart](./quickstart.md) §3), a recusa do push direto e a suíte
existente (que gera a cobertura) continuando verde.

**Organization**: em **dois PRs** (research R-10). O **PR 1** (integração, `Refs #18`) leva a
análise ao CI e cobre a US1, a US3 (configuração), a US2 (proteção versionada e PRs de fork) e
a US5 (documentação). Depois do merge, a análise da `main` vira a situação de partida, e o
**PR 2** (`Closes #18`) cobre a US4, termina a US2 (Quality Gate exigido) e a US3 (conferência
da cobertura no painel).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Ações do GitHub fixadas por SHA, com a versão em comentário; `permissions` mínimas; nenhuma
  ação de terceiros nova.
- `SONAR_TOKEN` só em `env:` dos passos do scanner; nunca em `run:` como argumento, nunca
  impresso. Organização `lucasfrenhan`, projeto `LucasFrenhan_Order-Management-System` (não
  são segredos).
- Única dependência nova: `dotnet-sonarscanner` 11.3.0 como ferramenta local (R-03). Nenhum
  pacote NuGet novo nos projetos.
- Nenhuma mudança de comportamento do sistema no PR 1; no PR 2, só correções apontadas pela
  situação de partida, com os testes existentes verdes.
- Merge na `main` só por **rebase** (ruleset). Commits: `tipo(escopo): descrição [REQ-20]`.
- Nenhum documento de `docs/fluxos/` muda (R-12); os PRs dizem isso em Validação.

---

## PR 1 — Integração

### Phase 1: Setup

- [ ] T001 [P] Acrescentar `dotnet-sonarscanner` **11.3.0** (`rollForward: false`, comando `dotnet-sonarscanner`) em `.config/dotnet-tools.json`, ao lado do `dotnet-ef`; conferir com `dotnet tool restore` e `dotnet sonarscanner --help` localmente, inclusive que a 11.3.0 lê o token da variável de ambiente `SONAR_TOKEN` (analyze A1).
- [ ] T002 [P] Em `coverage.runsettings`, incluir `PedidoFulfillment.Worker` e `PedidoFulfillment.PagamentosSimulados` no padrão de módulos (FR-012), mantendo migrations e `obj/` fora; conferir localmente que `--collect "Code Coverage;Format=cobertura,xml" --settings coverage.runsettings` gera `*.cobertura.xml` e `*.xml` com os seis módulos (R-05).

### Phase 2: User Story 1 - Todo PR é analisado e o resultado aparece no PR (P1) 🎯 MVP

**Goal**: o job `Build e testes` analisa o código e o PR mostra o Quality Gate.

**Independent Test**: o próprio PR 1 recebe a verificação `SonarCloud Code Analysis` e o resumo.

- [ ] T003 [US1] Em `.github/workflows/build-testes.yml`, gatilhos e jobs (R-02): acrescentar `push` na `main`; `cancel-in-progress` só em `pull_request`; job `Demonstração` com `if: github.event_name == 'pull_request'`; remover o passo `API de pagamento simulada` duplicado no job de demonstração (sobra do REQ-19); atualizar o comentário do topo (deixa de dizer que não roda no push da `main`).
- [ ] T004 [US1] No job `Build e testes` do mesmo arquivo, a sequência do [contrato](./contracts/analise-no-ci.md#workflow-build-testes): checkout com `fetch-depth: 0`; cache de `~/.sonar/cache` (`actions/cache` no SHA já usado); `dotnet tool restore`; um passo `id: sonar` que só grava `ativo=${{ secrets.SONAR_TOKEN != '' }}` em `$GITHUB_OUTPUT` (vira `true` ou `false`, nunca o valor); `dotnet sonarscanner begin` **antes do `dotnet restore`**, com `/k`, `/o`, `sonar.host.url`, `sonar.cs.vscoveragexml.reportsPaths=resultados/**/*.xml`, `sonar.cs.vstest.reportsPaths=resultados/**/*.trx`, `sonar.exclusions=specs/**,.specify/**,**/Persistencia/Migracoes/**,**/obj/**,**/bin/**` e `sonar.coverage.exclusions=**/Persistencia/Migracoes/**` (FR-009, FR-011); testes com `Format=cobertura,xml`; depois do resumo, mover os `*.cobertura.xml` para `resultados-cobertura/`; `dotnet sonarscanner end`. Os passos `begin` e `end` com `if: steps.sonar.outputs.ativo == 'true'` e o token **só no `env:` deles** (`SONAR_TOKEN: ${{ secrets.SONAR_TOKEN }}`), lido pelo scanner da variável de ambiente, sem argumento `/d:sonar.token` (FR-004, FR-005; analyze S1 e A1). O token não fica no `env:` do job: o `dotnet test`, que roda o código da aplicação e dos testes, não o vê.
- [ ] T005 [US1] No mesmo job, o artefato `resultados-testes` publica `resultados` e `resultados-cobertura` (o resumo continua lendo os Cobertura antes da mudança de pasta); conferir que `.github/scripts/resumo-testes.sh` não precisa mudar.

**Checkpoint**: o workflow analisa nos PRs do repositório e na `main`, e pula a análise sem o token.

### Phase 3: User Story 2 - Um PR que piora a qualidade não pode ser mergeado (P1), parte 1

**Goal**: proteção da `main` versionada e PRs de forks tratados.

- [ ] T006 [P] [US2] `.github/rulesets/protecao-main.json` com o estado atual do ruleset 26400051 (sem os campos de leitura `id`, `node_id`, `created_at`, `updated_at`, `_links`, `source*`, `current_user_can_bypass`): exclusão e force push bloqueados, histórico linear, PR com 0 aprovações e merge só por rebase, verificações `Build e testes` e `Demonstração` com a branch atualizada, sem contorno (FR-014, FR-015, FR-015a). Conferir com `gh api` que o JSON aplicado (`PUT`) não muda nada.
- [ ] T007 [P] [US2] `.github/workflows/prs-de-fork.yml` (R-07, FR-004a): `pull_request_target` com `types: [opened, reopened]`, job só com `if: github.event.pull_request.head.repo.full_name != github.repository`, `permissions: pull-requests: write`, um passo `gh pr close <número> --comment "<política>"` com `GH_TOKEN: ${{ github.token }}`; **sem checkout** e sem usar nenhum dado do PR além do número.

### Phase 4: User Story 5 - Documentação (P3), parte 1

- [ ] T008 [P] [US5] `README.md`: seção "Análise estática" (FR-019, FR-020): painel do projeto (`https://sonarcloud.io/project/overview?id=LucasFrenhan_Order-Management-System`), organização e projeto, onde ver a análise de um PR, o que fazer com um Quality Gate reprovado (corrigir; falso positivo ou ponto de segurança justificado no painel), o Quality Gate "Sonar way", a política de PRs de forks, o merge por rebase e o comando `gh api` para conferir e reaplicar o ruleset; recomendação opcional do SonarQube for IDE em modo conectado. Atualizar a seção "Integração contínua" (push na `main` só com testes e análise; comando local com `Format=cobertura,xml`) e a estrutura (`.github/rulesets/`, `prs-de-fork.yml`).
- [ ] T009 [P] [US5] `docs/estado-atual.md` (linha do REQ-20, parte 1) e `docs/proximos-passos.md` (REQ-20 em andamento; fila depois dele).

### Phase 5: Polish do PR 1

- [ ] T010 `dotnet build`, `dotnet test` (suíte inteira com Docker, zero ignorados) e `dotnet format --verify-no-changes`; varredura do diff (nenhum token, só os valores públicos de organização e projeto); registrar em Notes.
- [ ] T011 Push e PR 1 em português (Resumo, Pontos de atenção, Validação) com **`Refs #18`**, dizendo em Validação que nenhum documento de fluxo muda. Acompanhar `Build e testes` e `Demonstração` até ficarem verdes, inclusive depois de cada push. Conferir no PR: verificação `SonarCloud Code Analysis` e comentário do Quality Gate (US1, SC-001); no log do job, o token mascarado e nenhum valor dele (SC-005); a duração do job contra o último PR (SC-007); o nome exato do check e o `app.id` dele (`gh api repos/:owner/:repo/commits/<sha>/check-runs`) para a T016; e, pelos logs do scanner, que uma falha de autenticação apareceria sem o valor do token (caso de borda da credencial inválida, analyze D1). Registrar em Notes. O merge (rebase) é do dono.

---

## PR 2 — Situação de partida

Depois do merge do PR 1: sync (nenhum PR aberto, `main` igual à remota) e a branch
`feature/req-20-sonarqube` recriada a partir da `main`.

### Phase 6: User Story 4 - Situação de partida medida e pontos de segurança revisados (P2)

**Goal**: baseline registrada, bugs e vulnerabilidades resolvidos, pontos de segurança triados.

**Independent Test**: painel da `main` com 0 bugs, 0 vulnerabilidades e 0 pontos de segurança "To review" (SC-004), e a baseline no research.

- [ ] T012 [US4] Conferir que o push do merge rodou a análise da `main` e registrar a **situação de partida** em `specs/020-sonarqube-cloud/research.md` (R-10), com a data, pelas APIs públicas do projeto: `api/measures/component` (`bugs`, `vulnerabilities`, `security_hotspots`, `code_smells`, `coverage`, `duplicated_lines_density`, `ncloc`), `api/qualitygates/project_status`, as condições do Quality Gate em uso e a definição de "código novo" em vigor no projeto (FR-016, R-08; analyze N1: se a definição deixar o badge da `main` sem sentido, propor ao dono trocá-la no painel). Conferir também a lista de arquivos analisados: nada de `Persistencia/Migracoes`, `specs/`, `.specify/`; `tests/` como teste (FR-009 a FR-011).
- [ ] T013 [US4] Bugs e vulnerabilidades da baseline (`api/issues/search` com `types=BUG,VULNERABILITY`): corrigir cada um em `src/` com os testes existentes verdes, ou, se for falso positivo, preparar a justificativa para o dono marcar no painel; registrar o resultado na baseline (FR-018).
- [ ] T014 [US4] Pontos de segurança (`api/hotspots/search`): tabela em `research.md` com arquivo, linha, regra, decisão e justificativa (credenciais só de desenvolvimento dos contêineres locais e dos testes; R-10). **Tarefa do dono**: marcar cada um como "Safe" no painel com a justificativa da tabela (a credencial não passa pela sessão). Conferir depois pela API: 0 "To review" (FR-017, SC-004).
- [ ] T015 [P] [US4] Code smells da baseline que não forem corrigidos: seção "Dívida técnica" no `research.md` (contagem por regra e por projeto) e um item em `docs/proximos-passos.md` (FR-018).

### Phase 7: User Story 2 (P1), parte 2 — Quality Gate exigido

- [ ] T016 [US2] Acrescentar ao `.github/rulesets/protecao-main.json` a verificação do Quality Gate com o nome conferido na T011 e o `integration_id` do app do SonarQube Cloud (R-09), e aplicar com `gh api -X PUT`; conferir em `rules/branches/main`.
- [ ] T017 [US2] Validar o bloqueio (SC-002, quickstart §3): PR descartável com uma possível referência nula num serviço do Application e outro com um método público sem testes; os dois com o Quality Gate reprovado e o merge bloqueado; fechar os dois sem merge e apagar as branches. Conferir também que `git push origin HEAD:main` é recusado (FR-015). Registrar em Notes (links dos PRs fechados).

### Phase 8: User Story 3 (P1) e User Story 5 (P3), parte 2

- [ ] T018 [US3] Conferir no painel a cobertura diferente de 0% e a no máximo 2 pontos da cobertura do resumo do job **da mesma execução** (SC-003; os módulos medidos mudaram na T002), com um trecho coberto só por teste de integração (por exemplo, `ArmazenamentoPedidos`) aparecendo como coberto; registrar em Notes.
- [ ] T019 [P] [US5] `README.md`: badges do Quality Gate e da cobertura (`https://sonarcloud.io/api/project_badges/measure?project=LucasFrenhan_Order-Management-System&metric=alert_status` e `...&metric=coverage`, com link para o painel); `docs/estado-atual.md` e `docs/proximos-passos.md` com o REQ-20 entregue e a #18 fechada.

### Phase 9: Polish do PR 2

- [ ] T020 `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`; varredura do diff; registrar em Notes.
- [ ] T021 Push e PR 2 em português com **`Closes #18`**, citando em Validação a baseline, a triagem, os PRs descartáveis e que nenhum documento de fluxo muda. O PR 2 já passa pelas três verificações exigidas (`Build e testes`, `Demonstração` e o Quality Gate). Acompanhar até tudo verde, inclusive depois de cada push, e registrar em Notes.

---

## Dependencies & Execution Order

- **PR 1**: T001 e T002 → T003 → T004 → T005; T006, T007, T008 e T009 em paralelo com T003 a T005; T010 → T011.
- **Merge do PR 1** (rebase, pelo dono) → análise da `main`.
- **PR 2**: T012 → T013 e T014 (T014 depende do dono no painel) → T016 (só depois de 0 pontos de segurança pendentes e 0 bugs e vulnerabilidades: a `main` só passa a exigir o Quality Gate depois de limpa; os achados antigos não bloqueariam o PR 2, porque o gate do PR olha só o código novo) → T017; T015, T018 e T019 em paralelo com T013; T020 → T021.

### Parallel Opportunities

- T001 e T002; T006, T007, T008 e T009.
- T015, T018 e T019.

---

## Implementation Strategy

### MVP

PR 1: a análise aparece em todo PR e na `main`, com a cobertura completa, a proteção
versionada e os PRs de forks tratados.

### Incremental Delivery

1. Setup + US1 → `ci(sonar): analise do SonarQube Cloud no job de testes e no push da main [REQ-20]`.
2. US2 parte 1 → `ci(github): ruleset da main versionado e fechamento de PRs de forks [REQ-20]`.
3. US5 parte 1 → `docs(readme): analise estatica, protecao da main e politica de forks [REQ-20]`.
4. PR 1, merge.
5. US4 → `fix(...)` por correção e `docs(pesquisa): situacao de partida e triagem dos pontos de seguranca [REQ-20]`.
6. US2 parte 2 + US3 + US5 parte 2 → `ci(github): exige o Quality Gate na main [REQ-20]` e `docs(readme): badges do Quality Gate e da cobertura [REQ-20]`.
7. PR 2.

---

## Notes

- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR 2 ser mergeado.
- O fechamento automático de PRs de forks não pode ser exercitado de ponta a ponta sem uma
  segunda conta do GitHub (o dono não consegue fazer fork do próprio repositório); a T007 é
  verificada pela revisão do workflow (sem checkout, permissão mínima) e pela condição do `if`.
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo.
