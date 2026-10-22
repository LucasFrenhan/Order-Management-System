---

description: "Lista de tarefas do REQ-07 — CI de build e testes e ambiente de demonstração"
---

# Tasks: CI de build e testes e ambiente de demonstração

**Input**: Design documents from `/specs/007-ci-demo/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/verificacao-e-demonstracao.md](./contracts/verificacao-e-demonstracao.md),
[quickstart.md](./quickstart.md)

**Tests**:
- A regra de exigência de Docker tem teste unitário (Princípio V).
- O resto é infraestrutura de entrega, validada por execução: o script de resumo roda
  localmente, a demonstração sobe localmente e o workflow roda de verdade no GitHub, com
  um teste quebrado de propósito (quickstart §6).

**Organization**: por história de usuário. Não há fase de fundação bloqueante: a parte A
(CI) e a parte B (demonstração) são independentes até a US5, que junta as duas.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Nada muda em `src/` além do `Dockerfile`. A API e o contrato ficam intocados (FR-021).
- Nenhum pacote NuGet, ferramenta `dotnet` ou ação de terceiros novos (R-12). As ações
  usam os SHAs da R-02, com o comentário `# vX.Y.Z`.
- Nomes, mensagens e comentários em português. Commits:
  `tipo(escopo): descrição [REQ-07]`.
- `TreatWarningsAsErrors` continua ligado. `dotnet format --verify-no-changes` precisa
  passar.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build PedidoFulfillment.sln` e `dotnet test PedidoFulfillment.sln` na branch `feature/req-07-ci-demo`, que parte da `main` com o REQ-04, e registrar a contagem de testes como linha de base (zero ignorados com o Docker no ar). A contagem depende do que já estiver na `main` (287 só com o REQ-04; mais se o REQ-05 já tiver entrado). Achado L2.
- [X] T002 [P] Criar `.gitattributes` na raiz com `*.sh text eol=lf`, `Dockerfile text eol=lf` e `*.yml text eol=lf`, para que scripts e arquivos de contêiner tenham LF também na cópia de trabalho no Windows.

---

## Phase 2: User Story 2 — Testes de integração nunca são pulados no CI (Priority: P1)

Esta história vem antes da US1 porque a verificação depende dela: sem a exigência, o
workflow poderia ficar verde sem testar nada.

**Goal**: com `OMS_EXIGIR_DOCKER=true`, a falta de Docker reprova os testes; sem a
variável, nada muda.

**Independent Test**: os testes unitários da regra passam; localmente, sem a variável, a
suíte continua igual.

- [X] T003 [P] [US2] Teste em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/AmbienteDockerTestes.cs`:
  - `Obrigatorio("true")` e `Obrigatorio("TRUE")` são verdadeiros;
  - `null`, `""`, `"false"` e `"1"` são falsos;
  - `MensagemObrigatorio` é igual a
    "Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true).".
- [X] T004 [US2] Criar `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/AmbienteDocker.cs` (`public static`), com `Obrigatorio(string?)`, `Obrigatorio()` (lê a variável `OMS_EXIGIR_DOCKER`) e a constante `MensagemObrigatorio`, conforme a R-06. A T003 deve passar.
- [X] T005 [US2] Nas duas fixtures, no `catch (DockerUnavailableException excecao)`, acrescentar `when (!AmbienteDocker.Obrigatorio())` e um segundo `catch (DockerUnavailableException excecao)` que lança `new InvalidOperationException(AmbienteDocker.MensagemObrigatorio, excecao)`:
  - `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/BancoDadosTeste.cs`;
  - a classe `ConteineresDependencias` de `tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs`.

  Atualizar os comentários das duas classes: sem Docker, os testes são ignorados, exceto
  com `OMS_EXIGIR_DOCKER=true`, quando falham.
- [X] T006 [US2] Rodar a suíte inteira com o Docker no ar, com e sem `OMS_EXIGIR_DOCKER=true`: tudo verde e zero ignorados nos dois casos.

**Checkpoint**: a exigência de Docker funciona e não muda o comportamento local.

---

## Phase 3: User Story 1 — Todo PR é verificado automaticamente (Priority: P1) 🎯 MVP

**Goal**: o workflow `build-testes` compila e testa em todo PR e push na `main`.

**Independent Test**: com a branch enviada ao GitHub, o job "Build e testes" roda e fica
verde.

- [X] T007 [P] [US1] Criar `coverage.runsettings` na raiz (R-04), com `<DataCollector friendlyName="Code Coverage">`, `<Format>cobertura</Format>` e:
  - em `<ModulePaths>`, `<Include>`: `.*PedidoFulfillment\.(Domain|Application|Infrastructure|API)\.dll$`;
  - exclusão de fontes na estrutura exata do `Microsoft.CodeCoverage` (achado M3):
    `<CodeCoverage><Sources><Exclude><Source>.*[\\/]Persistencia[\\/]Migracoes[\\/].*</Source></Exclude></Sources></CodeCoverage>`,
    dentro de `<Configuration>`. A T012 confere que funcionou.
- [X] T008 [US1] Criar `.github/workflows/build-testes.yml` (R-01 a R-03), com:
  - `name: build-testes`;
  - gatilhos `pull_request` (branches `[main]`) e `push` (branches `[main]`);
  - `permissions: contents: read`;
  - `concurrency` com `group: build-testes-${{ github.ref }}` e
    `cancel-in-progress: true`.

  Job `testes`, com `name: Build e testes`, `runs-on: ubuntu-latest`,
  `timeout-minutes: 20` e `env: OMS_EXIGIR_DOCKER: "true"`. Passos:
  1. checkout;
  2. `setup-dotnet` com `global-json-file: global.json`;
  3. `actions/cache` com a chave da R-03;
  4. `dotnet restore PedidoFulfillment.sln`;
  5. `dotnet build PedidoFulfillment.sln -c Release --no-restore`;
  6. `dotnet test PedidoFulfillment.sln -c Release --no-build --logger "trx;LogFilePrefix=resultado" --results-directory resultados --collect "Code Coverage;Format=cobertura" --settings coverage.runsettings`.

  Os passos de resumo e de artefato entram na US3.
- [X] T009 [US1] Validar a sintaxe localmente: o arquivo precisa ser YAML válido (`python -c "import yaml,sys; yaml.safe_load(open(sys.argv[1]))" .github/workflows/build-testes.yml`, se houver Python; senão, revisar com cuidado) e os SHAs precisam ser os da R-02.

**Checkpoint**: o workflow existe. A primeira execução real acontece na T023.

---

## Phase 4: User Story 3 — Resultado e cobertura visíveis no PR (Priority: P2)

**Goal**: o resumo traz as contagens, os testes que falharam e a cobertura; o artefato
fica disponível; teste ignorado reprova.

**Independent Test**: rodar o script localmente sobre os resultados reais (quickstart §3).

- [X] T010 [US3] Criar `.github/scripts/resumo-testes.sh` (bash, `set -euo pipefail`, R-05):
  - **Entrada**: o diretório de resultados (padrão `resultados`). O script escreve em
    `$GITHUB_STEP_SUMMARY`, ou na saída padrão se a variável não existir.
  - **TRX**: para cada `.trx`, encontrado com `find`:
    - extrai `total`, `passed`, `failed` e `notExecuted` da tag `<Counters`, com `sed -E`
      ou `grep -o`;
    - lista os `testName` dos `UnitTestResult` com `outcome="Failed"`.
  - **Resumo**:
    - tabela Markdown "Projeto | Total | Aprovados | Falhos | Ignorados". O nome do
      projeto vem do atributo `storage` da primeira tag `<UnitTest` do TRX: o nome da
      `.dll` sem a extensão. Com `LogFilePrefix`, o nome do arquivo não traz o projeto
      (achado M1);
    - seção "Testes com falha", quando houver.
  - **Cobertura**: para cada `.cobertura.xml`, soma `lines-covered` e `lines-valid` da tag
    `<coverage` e escreve "Cobertura de linhas: NN,N% (cobertas/válidas)".
  - **Saída**: código 1 com a mensagem "Há testes ignorados no CI (OMS_EXIGIR_DOCKER)." se
    a soma de `notExecuted` passar de 0; código 1 se nenhum `.trx` for encontrado; senão,
    código 0.
- [X] T011 [US3] Acrescentar ao job `testes` em `build-testes.yml`:
  - o passo "Resumo dos testes" (`if: always()`, `run: bash .github/scripts/resumo-testes.sh resultados`);
  - o passo de `actions/upload-artifact` (`if: always()`, `name: resultados-testes`,
    `path: resultados`, `retention-days: 14`, `if-no-files-found: warn`).
- [X] T012 [US3] Rodar o quickstart §3 localmente (Git Bash), com o Docker no ar:
  - o script imprime a tabela com a contagem da T001 e 0 ignorados, e a cobertura sem `Migracoes`
    (conferir que nenhum `.cobertura.xml` cita `Migracoes`), e sai com 0;
  - renomear um `.trx` para simular "nenhum arquivo" e conferir a saída 1;
  - apagar `resultados/`.

**Checkpoint**: o resumo e o artefato estão definidos e testados localmente.

---

## Phase 5: User Story 4 — Demonstração com um comando, só com Docker (Priority: P1)

**Goal**: `docker compose --profile demo up -d --wait --build` sobe banco, broker e API
saudáveis; o login do Admin funciona; o comando sem perfil não muda.

**Independent Test**: quickstart §4 e §5.

- [X] T013 [P] [US4] Criar `.dockerignore` na raiz, com as entradas da R-07.
- [X] T014 [P] [US4] Criar `src/PedidoFulfillment.API/Dockerfile` (R-07):
  - **`FROM mcr.microsoft.com/dotnet/sdk:10.0.402-noble AS compilacao`**, com
    `WORKDIR /fonte`:
    1. `COPY` de `global.json`, `Directory.Build.props` e `Directory.Packages.props`, e
       dos 4 `.csproj` de `src/` nos mesmos caminhos;
    2. `RUN dotnet restore src/PedidoFulfillment.API/PedidoFulfillment.API.csproj`;
    3. `COPY src/ src/`;
    4. `RUN dotnet publish src/PedidoFulfillment.API/PedidoFulfillment.API.csproj -c Release --no-restore -o /app`.
  - **`FROM mcr.microsoft.com/dotnet/aspnet:10.0.13-noble AS execucao`**, com
    `WORKDIR /app`, `COPY --from=compilacao /app .`, `ENV ASPNETCORE_HTTP_PORTS=8080`,
    `EXPOSE 8080`, `USER $APP_UID` e
    `ENTRYPOINT ["dotnet", "PedidoFulfillment.API.dll"]`.
  - **Comentários**: as versões fixas acompanham `global.json` e os pacotes 10.0.13.
- [X] T015 [US4] Acrescentar o serviço `api` em `docker-compose.yml`, conforme a R-09, com o healthcheck da R-08:
  - `profiles: [demo]`;
  - `build` com `context: .` e `dockerfile: src/PedidoFulfillment.API/Dockerfile`;
  - `depends_on` com `condition: service_healthy` para `sqlserver` e `rabbitmq`;
  - `environment` (Development, string de conexão para `sqlserver,1433`, RabbitMQ em
    `rabbitmq:5672` com `OMS_RABBITMQ_USUARIO` e `OMS_RABBITMQ_SENHA`);
  - `ports: ["${OMS_API_PORTA:-5230}:8080"]`;
  - `healthcheck` com `interval: 10s`, `timeout: 5s`, `retries: 12` e
    `start_period: 30s`.

  Atualizar o comentário do topo para citar o perfil `demo`.
- [X] T016 [P] [US4] Em `.env.example`, acrescentar `# Porta local da API no perfil demo` e `OMS_API_PORTA=5230`, além de uma nota: senhas com `$` precisam ser escritas como `$$` (R-09).
- [X] T017 [US4] Validar localmente o quickstart §4 e §5:
  - medir o tempo do `up --wait` (com as imagens já construídas e baixadas: menos de 3
    min, SC-005);
  - prontidão 200;
  - login de `admin@oms.local` com `tokenAcesso`;
  - `/scalar/v1` com 200;
  - `docker compose --profile demo ps` com o usuário da API diferente de root
    (`docker compose exec api id -u` diferente de 0);
  - `down -v` remove tudo;
  - `docker compose up -d --wait` sem perfil sobe só 2 serviços.

  A API local pelo SDK precisa estar parada.

**Checkpoint**: a demonstração funciona localmente.

---

## Phase 6: User Story 5 — A demonstração é verificada no CI (Priority: P2)

**Goal**: o job "Demonstração" roda em paralelo, sobe o perfil `demo` e confere a
prontidão e o login.

- [X] T018 [US5] Acrescentar o job `demo` em `build-testes.yml` (R-10), com `name: Demonstração`, `runs-on: ubuntu-latest` e `timeout-minutes: 20`, sem `needs`. Passos:
  1. checkout;
  2. "Subir a demonstração": registrar o instante, rodar
     `docker compose --profile demo up -d --wait --wait-timeout 300 --build` e escrever no
     `$GITHUB_STEP_SUMMARY` o tempo gasto;
  3. "Prontidão": `curl -fsS http://localhost:5230/health/pronto`;
  4. "Login do administrador": `curl -fsS -H "Content-Type: application/json"` com o
     corpo do admin em `/api/v1/auth/login`, seguido de `grep -q tokenAcesso`;
  5. "Registros da API" (`if: failure()`):
     `docker compose --profile demo logs api sqlserver`;
  6. "Encerrar" (`if: always()`): `docker compose --profile demo down -v`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T019 [P] Atualizar `README.md` (R-11):
  - **Selo** de `build-testes` logo abaixo do título.
  - **"Demonstração rápida"**, depois de "Estado atual": comando, endereços, usuários de
    desenvolvimento (**somente desenvolvimento**), aviso da porta 5230 compartilhada com a
    API pelo SDK, como trocar com `OMS_API_PORTA` e o comando de encerramento.
  - **"Integração contínua"**: os dois jobs, o que reprova, o artefato, a variável
    `OMS_EXIGIR_DOCKER` (local e CI) e o passo a passo da proteção da `main`.
  - **Estado atual**: a linha do REQ-07.
  - **Próximos passos**: remover a linha da #15.
  - **Estrutura**: `.github/`, `coverage.runsettings` e o `Dockerfile`.
  - **Roteiros de validação**: acrescentar o quickstart do REQ-07.
- [X] T020 [P] Varredura de segredos nos arquivos novos (`git grep -n -i -E "password|senha|pwd|chave"`). Só podem aparecer os valores de desenvolvimento que já existiam (`OmsDev_2026!Local`, `oms-dev-local`, `AdminDev2026`) e nomes de variáveis. Conferir também que `grep -n "secrets." .github/workflows/build-testes.yml` não encontra nada (FR-012, achado L1).
- [X] T021 Rodar `dotnet format PedidoFulfillment.sln --verify-no-changes` e a suíte local inteira, com e sem Docker se possível.
- [X] T022 Commitar as fases e fazer push da branch para disparar a primeira execução (aceitar o push como parte da validação, porque o workflow só roda no GitHub).
- [ ] T023 Abrir o PR (com `Closes #15`) e acompanhar a execução com `gh pr checks --watch` e `gh run view`:
  - os dois jobs ficam verdes;
  - o resumo mostra 0 ignorados e a cobertura;
  - o artefato `resultados-testes` existe;
  - o tempo total fica em até 15 minutos.

  Em caso de falha, corrigir na branch e repetir.
- [ ] T024 Validar a reprovação (quickstart §6):
  - num commit temporário na branch, quebrar um teste unitário (por exemplo, inverter uma
    afirmação em `AmbienteDockerTestes`) e conferir que "Build e testes" fica vermelho,
    com o nome do teste no resumo;
  - reverter com `git revert` e conferir que volta a ficar verde.

  Registrar o resultado em Notes.

---

## Dependencies & Execution Order

- **Setup**: sem dependências.
- **US2**: depende da Setup. Vem primeiro, porque a US1 exige a variável.
- **US1**: depende da US2, já que o workflow define `OMS_EXIGIR_DOCKER=true`.
- **US3**: depende da US1, porque edita o mesmo workflow.
- **US4**: independente das US1–US3. Pode correr em paralelo com elas.
- **US5**: depende da US1 (workflow) e da US4 (perfil `demo`).
- **Polish**: depois de tudo. A T022–T024 exigem push.

### Parallel Opportunities

- T002 em paralelo com T001.
- T003 em paralelo com T007 e T013–T016.
- A US4 inteira em paralelo com a US1 e a US3.
- T019 e T020.

---

## Implementation Strategy

### MVP

1. Setup, US2 e US1.
2. Push e confirmação de que "Build e testes" roda verde.

### Incremental Delivery

1. Setup + US2 → commit `test(ci): exige docker nos testes de integracao com OMS_EXIGIR_DOCKER [REQ-07]`.
2. US1 + US3 → commit `ci(build): cria workflow build-testes com resumo e cobertura [REQ-07]`.
3. US4 → commit `feat(demo): cria imagem da API e perfil demo no compose [REQ-07]`.
4. US5 → commit `ci(demo): verifica a demonstracao no workflow [REQ-07]`.
5. Polish → commit `docs(ci): atualiza README com demonstracao e integracao continua [REQ-07]`, depois push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- O repositório usa `core.autocrlf=true` e guarda LF. O `.gitattributes` (T002) garante LF
  também na cópia de trabalho no Windows, para o script rodar no Git Bash.
- Os testes "de verdade" do workflow são as execuções no GitHub (T023, T024). Não há como
  executar o GitHub Actions localmente sem ferramenta nova.
- **README e PR #17** (achado M2): a T019 mexe nas mesmas seções do README que o PR #17
  (REQ-05). Se o #17 entrar na `main` antes, a branch é rebaseada sobre a `main` antes do
  merge, e o README fica com as duas linhas ("Estado atual" com REQ-05 e REQ-07).
- **Tempos** (achado L3): os tempos de SC-005 e SC-006 são observados e registrados aqui;
  a proteção dura é o `timeout-minutes: 20`.
- `/speckit-analyze` (2026-10-22), achados aplicados:
  - **M1**: nome do projeto tirado do `storage` do TRX (T010, R-05).
  - **M2**: nota de rebase e README.
  - **M3**: estrutura exata da exclusão de cobertura (T007).
  - **L1**: conferência de `secrets.` (T020).
  - **L2**: contagem de referência da T001.
  - **L3**: tempos registrados em Notes.
  - **L4**: spec `Approved`.
- Fazer commit ao fim de cada fase.
