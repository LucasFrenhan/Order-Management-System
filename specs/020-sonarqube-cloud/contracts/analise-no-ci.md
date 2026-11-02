# Contrato: análise estática no CI

**Requisito**: REQ-20 | **Decisões**: [research](../research.md) R-01 a R-11

O que o CI faz, em que momento, e o que fica exigido para o merge na `main`.

## Workflow `build-testes`

| Evento | Job `Build e testes` | Análise | Job `Demonstração` |
|--------|----------------------|---------|--------------------|
| `pull_request` para a `main`, do próprio repositório | roda | sim (análise de PR, decoração no PR) | roda |
| `pull_request` vindo de fork | roda | **pulada** (sem `SONAR_TOKEN`) | roda |
| `push` na `main` (merge) | roda | sim (análise da `main`, referência e badge) | **não roda** |

Concorrência: `build-testes-<ref>`, cancelando a execução anterior só em PRs.

Passos do job `Build e testes`, em ordem:

1. checkout com `fetch-depth: 0` (código novo e autoria das linhas);
2. setup do .NET (`global.json`), cache do NuGet e cache de `~/.sonar/cache`;
3. `dotnet tool restore` (`dotnet-ef` e `dotnet-sonarscanner` 11.3.0);
4. passo `sonar`: grava só `ativo=true|false` (se o segredo existe), sem o valor;
5. **`dotnet sonarscanner begin`** *(só com `ativo == true`)* com os parâmetros abaixo, antes
   do restore;
6. `dotnet restore` e `dotnet build -c Release` (avisos como erros, como hoje);
7. `dotnet test` com `OMS_EXIGIR_DOCKER=true`, `.trx` e
   `--collect "Code Coverage;Format=cobertura,xml" --settings coverage.runsettings`;
8. resumo dos testes e da cobertura (script atual, lê os `*.cobertura.xml`);
9. move os `*.cobertura.xml` para `resultados-cobertura/`;
10. **`dotnet sonarscanner end`** *(só com `ativo == true`)*;
11. publica os artefatos (`resultados` e `resultados-cobertura`).

O `SONAR_TOKEN` entra só por `env:` nos passos 5 e 10, e o scanner o lê da variável de
ambiente (sem `/d:sonar.token`). Ele não está no `env:` do job: o passo 7, que roda código da
aplicação e dos testes, não o vê.

### Parâmetros do `begin`

| Parâmetro | Valor |
|-----------|-------|
| `/k:` | `LucasFrenhan_Order-Management-System` |
| `/o:` | `lucasfrenhan` |
| `/d:sonar.host.url` | `https://sonarcloud.io` |
| `/d:sonar.cs.vscoveragexml.reportsPaths` | `resultados/**/*.xml` |
| `/d:sonar.cs.vstest.reportsPaths` | `resultados/**/*.trx` |
| `/d:sonar.exclusions` | `specs/**,.specify/**,**/Persistencia/Migracoes/**,**/obj/**,**/bin/**` |
| `/d:sonar.coverage.exclusions` | `**/Persistencia/Migracoes/**` |

A branch, o número do PR e a branch de destino são detectados pelo scanner a partir das
variáveis do GitHub Actions.

## Workflow `prs-de-fork`

| Evento | Condição | Ação |
|--------|----------|------|
| `pull_request_target` (`opened`, `reopened`) | `head.repo.full_name != github.repository` | comenta a política ("o repositório não aceita PRs de forks") e fecha o PR |

Sem checkout, sem rodar código do PR; `permissions: pull-requests: write`.

## Proteção da `main` (`.github/rulesets/protecao-main.json`)

| Regra | Valor |
|-------|-------|
| Exclusão da `main` | bloqueada |
| Force push | bloqueado |
| Histórico | linear |
| Pull request | obrigatório, 0 aprovações, merge só por rebase |
| Verificações exigidas, com a branch atualizada | `Build e testes`, `Demonstração` e, no PR 2, `SonarCloud Code Analysis` (com o `integration_id` do app) |
| Contorno | nenhum |

Aplicar ou conferir:

```bash
gh api -X PUT repos/LucasFrenhan/Order-Management-System/rulesets/26400051 --input .github/rulesets/protecao-main.json
gh api repos/LucasFrenhan/Order-Management-System/rules/branches/main
```

## Quality Gate

"Sonar way", no código novo: confiabilidade A, segurança A, manutenibilidade A, 100% dos
pontos de segurança revisados, cobertura ≥ 80%, duplicação ≤ 3%.
