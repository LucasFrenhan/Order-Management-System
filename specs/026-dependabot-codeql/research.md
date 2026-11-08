# Research: Dependências vulneráveis e CodeQL na cadeia de build (REQ-26)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md) | **Data**: 2026-11-08

Situação de partida, conferida em 2026-11-08:

- Repositório público; grafo de dependências ativo; alertas do Dependabot e security updates
  desligados; CodeQL "default setup" disponível para `actions` e `csharp`, mas `not-configured`.
- `Directory.Build.props` tem `TreatWarningsAsErrors=true` e não há configuração de NuGet Audit.
- `dotnet list PedidoFulfillment.sln package --vulnerable --include-transitive` não acha nenhuma
  vulnerabilidade (17 s localmente, com o restore feito).

---

## R-01 — O restore já reprova vulnerabilidades, e isso impede o resumo

**Achado** (experimento num projeto descartável com `System.Text.Json` 8.0.0 e
`TreatWarningsAsErrors`): o NuGet Audit do restore emite NU1903 para a vulnerabilidade alta, o
`TreatWarningsAsErrors` a promove a erro e o restore falha. Com o restore falho, o
`dotnet list package --vulnerable` não roda ("Falha na restauração"). Hoje, portanto, qualquer
vulnerabilidade, inclusive baixa ou moderada, já derruba o CI e o build local, mas só com a linha
do erro no log, sem resumo, sem distinguir severidade e sem exceção possível além de
`NuGetAuditSuppress`.

**Decisão**: `Directory.Build.props` ganha
`<WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>`. O restore continua
auditando e mostrando os avisos (localmente também), mas quem reprova é a verificação do CI (R-02),
que aplica a regra da spec: alta e crítica reprovam, baixa e moderada são aviso, exceções
registradas. O NU1900 (falha ao obter os dados de vulnerabilidade) continua erro, o que já cumpre
parte do FR-014.

**Alternativas**:
- Manter o restore como porta e usar `NuGetAuditLevel=high` com `NuGetAuditSuppress` para as
  exceções: o restore falho impede o resumo (FR-012), e o motivo e a data da exceção não teriam
  onde ficar de forma legível.
- Ler o NuGet Audit do log do build: frágil e sem formato estável.

## R-02 — Verificação de vulnerabilidades no job `Build e testes`

**Decisão**: passo "Vulnerabilidades" logo depois de "Restaurar", rodando
`.github/scripts/verificar-vulnerabilidades.ps1`, que:

1. roda `dotnet list PedidoFulfillment.sln package --vulnerable --include-transitive --format json`
   (saída JSON estável, versão 1; projetos → frameworks → `topLevelPackages` e
   `transitivePackages` → `vulnerabilities[{severity, advisoryurl}]`);
2. reprova se o comando falhar ou se houver `problems` de nível `error` (FR-014);
3. cruza cada vulnerabilidade com `.github/vulnerabilidades-aceitas.json` (R-03);
4. escreve no `$GITHUB_STEP_SUMMARY` uma tabela por pacote: versão, severidade, aviso, direta ou
   transitiva e situação (reprova, aviso ou exceção) (FR-012);
5. sai com código 1 se sobrar alguma `High` ou `Critical` sem exceção válida.

O roteiro aceita `-Entrada <arquivo.json>` para ler uma saída gravada em vez de chamar o
`dotnet`, o que permite testá-lo com exemplos fixos (quickstart §2) sem depender da base de
avisos; `exemplos-vulnerabilidades/conferir.ps1` roda esses casos no próprio passo do CI, antes
da verificação real.

**PowerShell 7, não Bash + `jq`** (revisto na implementação, 2026-11-08): o `jq` não existe na
máquina de desenvolvimento (Windows com Git Bash), e o roteiro precisa rodar nos dois lugares. O
`pwsh` vem no runner `ubuntu-latest` e no ambiente local e lê JSON sem ferramenta extra
(`ConvertFrom-Json`).

Falha logo depois do restore: o PR fica vermelho em ~1 min, sem gastar os testes. Custo medido:
~17 s com o restore feito (SC-006: até 1 min).

**Alternativas**: um projeto de teste que lê o `project.assets.json` (mais código, sem ganho);
uma action de terceiros (dependência nova, e o `dotnet list` já faz o trabalho).

## R-03 — Exceções de vulnerabilidade

**Decisão**: arquivo versionado `.github/vulnerabilidades-aceitas.json`, começando vazio
(`{"excecoes": []}`). Cada exceção tem `pacote`, `aviso` (URL do GHSA), `motivo`, `registradaEm`
e `revisarAte`. Esquema em [data-model.md](./data-model.md) e regras em
[contracts/verificacao-vulnerabilidades.md](./contracts/verificacao-vulnerabilidades.md).

- A exceção vale para o par pacote + aviso, em qualquer versão e projeto.
- Vencida (`revisarAte` no passado) ela deixa de valer e o CI reprova: obriga a revisar.
- Exceção que não casa com nenhuma vulnerabilidade atual vira aviso no resumo ("pode sair").
- Arquivo inválido reprova.

**Alternativas**: `NuGetAuditSuppress` com metadados no `Directory.Build.props` (lido por
`dotnet msbuild -getItem`): esconde o aviso local também, mas exige um projeto para avaliar e
mistura política de segurança com propriedades de build. Descartado pela simplicidade do JSON.

## R-04 — Configuração do Dependabot

**Decisão**: `.github/dependabot.yml` (versão 2), um bloco por ecossistema, todos com
`schedule: weekly` (segunda, 06:00, `America/Sao_Paulo`), `cooldown.default-days: 14` e
`open-pull-requests-limit: 5`.

| Ecossistema | Diretório(s) | Grupos (minor/patch) |
|---|---|---|
| `nuget` | `/` (lê o `Directory.Packages.props` e o `.config/dotnet-tools.json`) | `dotnet` (AspNetCore, EF Core, `Microsoft.Extensions.Logging.*`, `Microsoft.Data.SqlClient`, `System.*`, `dotnet-ef`), `opentelemetry`, `testes` (xunit, Test.Sdk, FluentAssertions, Testcontainers, JsonSchema.Net, `Microsoft.OpenApi.YamlReader`), `ferramentas` (`dotnet-sonarscanner`, `dotnet-reportgenerator-globaltool`), `demais` (`*`) |
| `github-actions` | `/` | `actions` (`*`) |
| `docker` | `/docker/sqlserver`, `/src/PedidoFulfillment.API`, `/src/PedidoFulfillment.Worker`, `/src/PedidoFulfillment.PagamentosSimulados` | `imagens` (`*`) |
| `docker-compose` | `/` | `compose` (`*`) |

- **Ordem dos grupos**: o Dependabot põe cada dependência no primeiro grupo que casar; o `demais`
  fica por último. Os grupos usam `update-types: [minor, patch]`, então os major saem em PRs
  separados (FR-002).
- **Duas semanas**: o `cooldown` só se aplica às atualizações de versão; as de segurança não o
  respeitam, que é exatamente o FR-004 decidido no specify.
- **Commits**: `commit-message.prefix` `build` (NuGet, Docker, Compose) e `ci` (Actions), com
  `include: scope`, o que gera `build(deps): ...` e `ci(deps): ...` (FR-005).
- **Actions fixadas por SHA**: o Dependabot troca o SHA e atualiza o comentário `# vX.Y.Z` junto.
- **Docker e Compose** são dois ecossistemas no Dependabot, então "Docker" vira dois PRs por
  semana no máximo (um para os `Dockerfile`s, outro para o `docker-compose.yml`). Ajuste de
  redação, sem mudar a intenção do FR-002.
- **Ignorados**:
  - `FluentAssertions` `>= 8.0.0`: a 8 mudou de licença; o projeto fixa `[7.2.2,8.0.0)`.
  - `oms-sqlserver` no Compose: imagem montada pelo projeto, não existe em registro.
- **SDK do `global.json`**: fica fora. O ecossistema `dotnet-sdk` existe, mas a issue não o pede,
  e o `rollForward: latestFeature` tolera a imagem `sdk` dos `Dockerfile`s andar à frente.
  Registrado em `docs/` como atualização manual.

**Risco a conferir na primeira execução**: a tag `2022-CU26-ubuntu-22.04` da base do SQL Server
não é semver; o Dependabot pode não propor nada ou propor `2025-...` como major. Se ele não
conseguir ler, a atualização da CU fica manual e documentada; se propuser a 2025, é um major em PR
separado, que o CI testa.

**Alternativas**: frequência diária (ruído demais para um mantenedor); um grupo único para o NuGet
(rejeitado no clarify); Renovate (app externo, sem necessidade).

## R-05 — PRs do Dependabot e os segredos

**Achado**: workflows disparados por PRs do Dependabot não recebem os segredos de Actions, só os
segredos de Dependabot, e o `GITHUB_TOKEN` é somente leitura por padrão.

**Decisão**:
- Cadastrar `SONAR_TOKEN` também como segredo de Dependabot (`gh secret set SONAR_TOKEN --app
  dependabot`, valor informado pelo dono). O passo "Análise disponível" já testa o segredo; com
  ele presente, o Quality Gate roda nos PRs do Dependabot (FR-006). PRs de forks continuam sem
  acesso, porque segredos de Dependabot só vão para PRs abertos pelo Dependabot.
- Publicar a imagem do SQL Server no GHCR pode falhar num PR do Dependabot que mude
  `docker/sqlserver` (token só leitura). O passo já trata isso: monta a imagem no runner e avisa.
  A publicação acontece no push da `main`, depois do merge.
- O workflow `prs-de-fork` não age: o PR do Dependabot vem do próprio repositório.

## R-06 — CodeQL: setup avançado, versionado

**Decisão**: workflow próprio `.github/workflows/codeql.yml` (setup avançado), e o default setup
continua desligado (os dois não convivem).

- Gatilhos: `pull_request` para a `main`, `push` na `main` e `schedule` semanal (para que regras
  novas do CodeQL apareçam mesmo sem push).
- Matriz `language: [actions, csharp]`, `build-mode: none` nas duas. O C# sem compilação evita
  repetir o build de ~1 min e não precisa do Docker; se a extração não reconhecer o C# 14 / .NET
  10 na versão do CodeQL, troca-se para `build-mode: manual` com `dotnet build` (conferido no
  primeiro PR).
- Conjunto de regras padrão (sem `queries:`), conforme o clarify.
- `github/codeql-action` v4.39.1 (2026-10-21, mais de duas semanas), fixada pelo SHA
  `9b3254085494b55a0f7c4c2b3d2d52cc712ce370`; `actions/checkout` no mesmo SHA do `build-testes`.
- `permissions`: `contents: read`, `security-events: write`, `actions: read`.
- Nome do job `Analyze (${{ matrix.language }})`.

**Por que não o default setup**: a configuração ficaria só nas telas do GitHub, sem revisão por
PR, e a action não seria atualizada pelo Dependabot com a regra das duas semanas. O avançado
segue o padrão do projeto (workflows versionados, SHAs fixados, permissões mínimas).

## R-07 — CodeQL exigido no ruleset

**Decisão**: regra `code_scanning` no ruleset "Proteção da main":

```json
{ "type": "code_scanning",
  "parameters": { "code_scanning_tools": [
    { "tool": "CodeQL", "security_alerts_threshold": "high_or_higher", "alerts_threshold": "errors" } ] } }
```

Ela bloqueia o merge enquanto a análise do CodeQL do PR não terminar ou houver alerta novo de
segurança alta/crítica ou de severidade erro (FR-009). Os nomes dos jobs do CodeQL não entram em
`required_status_checks`: a regra de code scanning já espera o resultado. A regra só é aplicada
depois de a `main` ter a primeira análise (R-10), para não bloquear o próprio PR que traz o CodeQL.

Queda do serviço: mesmo procedimento do REQ-20 (`docs/analise-estatica.md`), desligando
temporariamente a regra e registrando.

## R-08 — Imagens de terceiros numa fonte única (FR-019)

**Achado**: `rabbitmq:4.3.6-management` está repetida em `MensageriaTeste.cs` e
`SaudeComDependenciasTestes.cs`, e `axllent/mailpit:v1.33.0` em `MailpitTeste.cs`. A imagem dos
testes do SQL Server se chama `oms-sqlserver-testes:2022-CU26-fts` e é montada com
`WithDeleteIfExists(false)`: depois de o Dependabot trocar o `FROM`, uma imagem antiga com o mesmo
nome continuaria valendo na máquina local.

**Decisão**:
- **Fonte única = `docker-compose.yml`**, que o Dependabot já atualiza. Uma classe
  `ImagensDoAmbiente` nos testes de integração acha o arquivo subindo a partir da pasta dos testes
  (como `ImagemSqlServerTeste` faz com o `Dockerfile`) e lê o `image:` do serviço pedido
  (`rabbitmq`, `mailpit`), com uma leitura linha a linha do bloco `services:` (indentação de 2 e 4
  espaços), sem biblioteca de YAML nova. Erro claro se o serviço ou a imagem não existir.
- **SQL Server**: a tag da imagem dos testes passa a ser o hash do conteúdo de `docker/sqlserver`
  (`oms-sqlserver-testes:<16 hex>`), como no CI. Mudou o `Dockerfile`, muda o nome e a imagem é
  montada de novo. No Compose, a tag vira `oms-sqlserver:fts`, sem a CU (o `--build` dos comandos
  documentados já monta de novo).
- Testes unitários da leitura (serviço existente, ausente, imagem sem tag ou `latest`) e um teste
  que confere que `rabbitmq` e `mailpit` resolvem a partir do compose real.

**Alternativas**: um arquivo `.env` com as versões (o Dependabot não atualiza `.env`); um
`Dockerfile` por imagem só com o `FROM` (obrigaria o Compose a montar imagens triviais); uma
biblioteca de YAML nos testes (dependência nova sem necessidade, constituição).

## R-09 — Alertas e triagem inicial

**Decisão**:
- Ligar pela API, com o aval do dono: `PUT /repos/{dono}/{repo}/vulnerability-alerts` e
  `PUT /repos/{dono}/{repo}/automated-security-fixes` (FR-008).
- Triagem em `docs/seguranca-cadeia-build.md`, numa tabela: origem (Dependabot ou CodeQL), alerta
  (número e link), regra ou aviso, severidade, decisão (corrigido no PR, dispensado com motivo, ou
  issue), data. Os dispensados também ficam com o motivo no próprio GitHub (`dismissed_comment`).
- Candidato conhecido: `pull_request_target` em `prs-de-fork.yml` (uso deliberado, sem checkout do
  PR, REQ-20).

## R-10 — Entrega em dois PRs

Como no REQ-20, parte do resultado só existe depois do merge: o Dependabot só lê a configuração
da branch padrão, e a regra do CodeQL no ruleset precisa da análise da `main`.

1. **PR 1** (`Refs #59`): `dependabot.yml`, `codeql.yml`, a verificação de vulnerabilidades e as
   exceções, o `WarningsNotAsErrors`, as imagens na fonte única e o rascunho de
   `docs/seguranca-cadeia-build.md`. Antes do merge: alertas e security updates ligados e o
   segredo do Dependabot cadastrado. Validação: o CodeQL roda no PR, a verificação reprova num
   PR descartável com pacote vulnerável (quickstart §3).
2. **Depois do merge**: análise da `main`, primeiros PRs do Dependabot e triagem dos alertas.
3. **PR 2** (`Closes #59`): correções da triagem, regra `code_scanning` no ruleset versionado e
   aplicado, documento completo, README (só com permissão). Antes dele, pelo menos um PR do
   Dependabot mergeado por rebase (SC-004).
