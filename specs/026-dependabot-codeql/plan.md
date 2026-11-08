# Implementation Plan: Dependências vulneráveis e CodeQL na cadeia de build

**Branch**: `feature/req-26-dependabot-codeql` | **Date**: 2026-11-08 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/026-dependabot-codeql/spec.md`

**Requisito**: REQ-26 (GitHub issue #59)

## Summary

- **Verificação de vulnerabilidades** no job `Build e testes`, logo depois do restore: um roteiro
  lê `dotnet list package --vulnerable --include-transitive --format json`, reprova alta e
  crítica, avisa baixa e moderada, aceita exceções versionadas com prazo e escreve o resumo do job
  (R-02, R-03). Para isso, os avisos do NuGet Audit (NU1901–NU1904) deixam de virar erro no
  restore: hoje o `TreatWarningsAsErrors` já derruba o restore com qualquer severidade e impede o
  resumo (R-01, comprovado em experimento).
- **Dependabot** semanal para NuGet (com as ferramentas locais), Actions, `Dockerfile`s e Compose,
  com `cooldown` de 14 dias (as atualizações de segurança não esperam), grupos por família e major
  separado; `SONAR_TOKEN` também como segredo de Dependabot para o Quality Gate rodar nos PRs dele
  (R-04, R-05).
- **CodeQL** em workflow versionado (setup avançado), `actions` e `csharp` sem compilação, regras
  padrão, em PR, na `main` e semanal; exigido no ruleset por uma regra `code_scanning` (alta e
  crítica, e erros) aplicada depois da primeira análise da `main` (R-06, R-07).
- **Imagens numa fonte única**: os testes leem RabbitMQ e Mailpit do `docker-compose.yml`, que o
  Dependabot atualiza; a imagem de testes do SQL Server passa a ter a tag pelo hash da pasta
  `docker/sqlserver` (R-08).
- **Triagem** dos alertas iniciais e o procedimento em `docs/seguranca-cadeia-build.md` (R-09).
- **Dois PRs** (R-10): integração (`Refs #59`); depois do merge, triagem e regra do CodeQL no
  ruleset (`Closes #59`).
- Nenhuma mudança de comportamento do sistema, de banco, de mensagens ou de contrato.

## Technical Context

**Language/Version**: YAML do GitHub Actions e do Dependabot; Bash + `jq` (roteiro do CI); C# 14 /
.NET 10 (SDK do `global.json`) nos testes; JSON (exceções e ruleset)

**Primary Dependencies**:
- novas: nenhuma dependência NuGet. Nova action `github/codeql-action` v4.39.1 (fixada por SHA,
  R-06), do próprio GitHub;
- já existentes: `dotnet list package` do SDK, `actions/checkout`, `gh` e `jq` no runner,
  Testcontainers nos testes.

**Storage**: N/A (arquivos versionados e estado no GitHub; [data-model](./data-model.md))

**Testing**: exemplos fixos para o roteiro de vulnerabilidades (quickstart §2); testes da leitura do
compose no projeto `PedidoFulfillment.Testes.Integracao` (sem Docker); a suíte inteira verde com as
imagens lidas do compose; PRs descartáveis para o bloqueio (quickstart §3 e §5)

**Target Platform**: runner `ubuntu-latest`; Dependabot e code scanning do GitHub (gratuitos em
repositório público)

**Project Type**: CI de um web-service .NET (API, Worker e API de pagamento simulada)

**Performance Goals**: `Build e testes` cresce no máximo 1 min (SC-006; medido ~17 s); o CodeQL roda
em paralelo, em workflow próprio, sem alongar o `Build e testes`

**Constraints**:
- actions fixadas por SHA, só versões com duas semanas ou mais; `permissions` mínimas;
- segredos de Dependabot só chegam a PRs do Dependabot, nunca a forks (R-05);
- a regra do CodeQL no ruleset só depois da análise da `main` (R-07, R-10);
- mudanças de configuração do repositório (alertas, segredo, ruleset) com o aval do dono;
- README só cresce com permissão (CLAUDE.md).

**Scale/Scope**: 1 workflow novo (`codeql.yml`), 1 alterado (`build-testes.yml`),
`dependabot.yml`, 1 roteiro + exemplos, 1 arquivo de exceções, `Directory.Build.props`,
`docker-compose.yml`, 4 arquivos de teste (leitura de imagens), ruleset, 1 documento novo em `docs/`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Issue #59, specify (`52d3b1f`) e clarify (`73dda9e`) `[REQ-26]` |
| II. Contratos versionados | ✅ | Nenhum contrato v1 ou de mensageria muda |
| III. Segurança por padrão | ✅ | Vulnerabilidades altas e críticas e alertas novos do CodeQL bloqueiam o merge; exceções explícitas e com prazo; segredo de Dependabot sem alcance para forks; `permissions` mínimas |
| IV. Desacoplamento assíncrono | ✅ | Não se aplica: nada muda no sistema |
| V. Qualidade verificável | ✅ | Verificações automáticas em todo PR; roteiro testado com exemplos; leitura das imagens com testes |
| RabbitMQ.Client nativo | ✅ | Não se aplica |
| Dependências justificadas | ✅ | Nenhum pacote NuGet novo; a leitura do compose dispensa biblioteca de YAML (R-08); a `codeql-action` justificada em R-06 |
| Qualidade e conformidade (revisão) | ✅ | O PR ganha evidência objetiva de dependências e de fluxo de dados |

**Pós-design (Phase 1)**: sem violações. O `WarningsNotAsErrors` não enfraquece a regra: o CI
continua reprovando alta e crítica, agora com resumo e exceções; localmente, os avisos continuam
aparecendo no restore.

## Project Structure

### Documentation (this feature)

```text
specs/026-dependabot-codeql/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── verificacao-vulnerabilidades.md
│   ├── dependabot.md
│   └── codeql.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
.github/
├── dependabot.yml                         # novo (R-04)
├── vulnerabilidades-aceitas.json          # novo, vazio (R-03)
├── rulesets/protecao-main.json            # + regra code_scanning (PR 2, R-07)
├── scripts/
│   ├── verificar-vulnerabilidades.sh      # novo (R-02)
│   └── exemplos-vulnerabilidades/*.json   # novos (quickstart §2)
└── workflows/
    ├── build-testes.yml                   # + passo "Vulnerabilidades"
    └── codeql.yml                         # novo (R-06)
Directory.Build.props                      # WarningsNotAsErrors NU1901–NU1904 (R-01)
docker-compose.yml                         # tag oms-sqlserver:fts; comentário sobre o Dependabot
tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/
├── ImagensDoAmbiente.cs                   # novo: lê image: do compose (R-08)
├── ImagensDoAmbienteTestes.cs             # novo (ou em pasta de testes da infraestrutura)
├── ImagemSqlServerTeste.cs                # tag pelo hash de docker/sqlserver
├── MensageriaTeste.cs                     # RabbitMQ do compose
└── MailpitTeste.cs                        # Mailpit do compose
tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs  # RabbitMQ do compose
docs/
├── seguranca-cadeia-build.md              # novo: configuração, procedimentos e triagem
├── integracao-continua.md                 # passo novo e workflow do CodeQL
└── proximos-passos.md                     # #59 sai da fila (Polish)
```

**Structure Decision**: tudo na infraestrutura de CI e nos testes de integração; nenhum projeto
novo. `docs/fluxos/` não muda (nenhum fluxo de negócio é alterado), e o PR diz isso.

## Complexity Tracking

Sem violações a justificar.
