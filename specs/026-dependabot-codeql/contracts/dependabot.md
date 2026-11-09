# Contrato: configuração do Dependabot

**Requisitos**: FR-001 a FR-008, FR-019 | **Research**: R-04, R-05, R-08

Arquivo `.github/dependabot.yml`, versão 2. Comum a todos os blocos:

| Chave | Valor |
|---|---|
| `schedule` | `interval: weekly`, `day: monday`, `time: "06:00"`, `timezone: America/Sao_Paulo` |
| `cooldown` | `default-days: 14` (só atualizações de versão; as de segurança chegam na hora) |
| `open-pull-requests-limit` | `5`; `10` no NuGet (revisto depois do PR 2: os majors abertos ocupavam as vagas dos grupos) |
| `commit-message` | `prefix: build(deps)` no NuGet; `prefix: build` (`ci` em Actions) com `include: scope` nos demais (revisto no PR 2: os grupos do NuGet saíam sem o escopo) |
| `groups.*.update-types` | `[minor, patch]` (major em PR próprio) |

## Blocos

| `package-ecosystem` | `directory`/`directories` | Grupos, na ordem | `ignore` |
|---|---|---|---|
| `nuget` | `/` | `dotnet`: `Microsoft.AspNetCore.*`, `Microsoft.EntityFrameworkCore*`, `Microsoft.Extensions.Logging.*`, `Microsoft.Data.SqlClient`, `System.*`, `dotnet-ef` · `opentelemetry`: `OpenTelemetry*` · `testes`: `xunit*`, `Microsoft.NET.Test.Sdk`, `FluentAssertions`, `Testcontainers*`, `JsonSchema.Net`, `Microsoft.OpenApi.YamlReader` · `ferramentas`: `dotnet-sonarscanner`, `dotnet-reportgenerator-globaltool` · `demais`: `*`, com `exclude-patterns` iguais aos padrões dos outros grupos (sem isso ele os absorve; revisto no PR 2) | `FluentAssertions` `>= 8.0.0`; `Microsoft.OpenApi.YamlReader` `>= 3.0.0` (incompatível com o ASP.NET Core 10, PR #93) |
| `github-actions` | `/` | `actions`: `*` | — |
| `docker` | `/docker/sqlserver`, `/src/PedidoFulfillment.API`, `/src/PedidoFulfillment.Worker`, `/src/PedidoFulfillment.PagamentosSimulados` | `imagens`: `*` | — |
| `docker-compose` | `/` | `compose`: `*` | `oms-sqlserver` |

## Comportamento esperado de um PR do Dependabot

- Título e commit `build(deps): bump the dotnet group with N updates` (ou `ci(deps): …`).
- Branch no próprio repositório: o `prs-de-fork` não age.
- Recebe `Build e testes` (com a análise do Sonar, pelo segredo de Dependabot), `Demonstração` e
  o CodeQL. Só entra por rebase, com todas verdes, depois que o dono liga o auto-merge
  (`gh pr merge <n> --rebase --auto`).
- Depois de cada merge, os outros PRs abertos do Dependabot são rebaseados por ele mesmo
  (`rebase-strategy` padrão) ou por `@dependabot rebase`.
- Atualização que quebra: o dono corrige na branch do PR, comenta `@dependabot ignore this
  [major|minor] version` ou fecha.

## Configuração fora do arquivo

- Alertas do Dependabot e security updates ligados (FR-008).
- Segredo `SONAR_TOKEN` cadastrado para o Dependabot (FR-006).
