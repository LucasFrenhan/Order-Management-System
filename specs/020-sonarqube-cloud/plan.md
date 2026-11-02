# Implementation Plan: Análise estática de código com Quality Gate no CI

**Branch**: `feature/req-20-sonarqube` | **Date**: 2026-11-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/020-sonarqube-cloud/spec.md`

**Requisito**: REQ-20 (GitHub issue #18)

## Summary

- **Análise no job existente**: o `dotnet-sonarscanner` 11.3.0 envolve a compilação e os
  testes do job `Build e testes` (`begin` → build → testes → `end`), sem compilar duas vezes
  (research R-01, R-03).
- **Gatilhos**: PRs como hoje e, de novo, o push na `main`, só com o job de testes e a análise
  (R-02). Em PRs de forks, a análise é pulada; um workflow `prs-de-fork` comenta a política e
  fecha esses PRs, sem rodar código deles (R-07).
- **Cobertura sem pacote novo**: o coletor atual passa a gerar Cobertura (resumo do job) e XML
  (Sonar) na mesma execução, verificado localmente; a cobertura passa a incluir o Worker e a
  API de pagamento simulada (R-05). Exclusões de migrations, `obj/`, `specs/` e `.specify/`
  (R-06).
- **Quality Gate** "Sonar way" e decoração pelo app do SonarQube Cloud (R-08). A proteção da
  `main` (ruleset 26400051, já ativo) fica versionada em `.github/rulesets/` e ganha a
  verificação do Quality Gate no fim (R-09).
- **Dois PRs** (R-10): integração (`Refs #18`); depois do merge, a análise da `main` vira a
  situação de partida, e o segundo PR (`Closes #18`) corrige bugs e vulnerabilidades, traz a
  triagem dos pontos de segurança, exige o Quality Gate e acrescenta os badges.
- Nenhuma mudança de comportamento do sistema, de banco ou de contrato.

## Technical Context

**Language/Version**: YAML do GitHub Actions; C# 14 / .NET 10 (SDK do `global.json`) para a
compilação analisada; JSON do ruleset

**Primary Dependencies**:
- **novo**: `dotnet-sonarscanner` 11.3.0 (ferramenta local, `.config/dotnet-tools.json`),
  justificado em R-03;
- já existentes: coletor `Code Coverage` do `Microsoft.NET.Test.Sdk` (agora também em XML),
  `actions/checkout`, `actions/setup-dotnet`, `actions/cache`, `actions/upload-artifact`
  (fixadas por SHA), `gh` no runner para o `prs-de-fork`.

**Storage**: N/A (estado no SonarQube Cloud e no GitHub; [data-model](./data-model.md))

**Testing**: a própria suíte (xUnit v3, Testcontainers) gera a cobertura; a validação do
requisito é por PRs descartáveis e pelo painel ([quickstart](./quickstart.md))

**Target Platform**: runner `ubuntu-latest` do GitHub Actions; SonarQube Cloud (plano Free,
repositório público)

**Project Type**: CI de um web-service .NET (API, Worker e API simulada)

**Performance Goals**: o job `Build e testes` cresce no máximo 5 min (SC-007); cache do
`~/.sonar/cache` e nenhuma compilação extra

**Constraints**:
- `SONAR_TOKEN` só por `env:` dos passos do scanner, nunca em logs nem arquivos (FR-005);
- ações fixadas por SHA, `permissions` mínimas (FR-007);
- `pull_request_target` sem checkout do código do PR (R-07);
- o check exigido do Quality Gate só pode ser cadastrado depois de rodar uma vez (R-09, R-10).

**Scale/Scope**: 2 workflows (1 alterado, 1 novo), `coverage.runsettings`,
`.config/dotnet-tools.json`, ruleset versionado, README; no PR 2, correções pontuais apontadas
pela situação de partida.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Issue #18, specify (`0f6da35`) e clarify (`eb1b725`) `[REQ-20]` |
| II. Contratos versionados | ✅ | Nenhum contrato v1 muda |
| III. Segurança por padrão | ✅ | Vulnerabilidades e pontos de segurança bloqueiam o merge; credenciais de desenvolvimento triadas uma a uma, sem desligar a regra; token só como segredo; `pull_request_target` sem código do PR; `main` sem contorno |
| IV. Desacoplamento assíncrono | ✅ | Não se aplica: nada muda no sistema |
| V. Qualidade verificável | ✅ | Quality Gate automático em todo PR, com cobertura medida (unitários e integração) |
| RabbitMQ.Client nativo | ✅ | Não se aplica |
| Dependências justificadas | ✅ | `dotnet-sonarscanner` justificado em R-03; nenhum pacote NuGet novo nos projetos (cobertura com o coletor existente, R-05) |
| Qualidade e conformidade (revisão) | ✅ | A revisão ganha evidência objetiva: Quality Gate e cobertura no PR |
| Branch dedicada | ✅ | `feature/req-20-sonarqube`, a partir da `main` sincronizada após o PR #47 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/020-sonarqube-cloud/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/analise-no-ci.md       # gatilhos, passos, parâmetros, ruleset, Quality Gate
├── checklists/requirements.md
└── tasks.md                         # /speckit-tasks
```

### Source Code (repository root)

```text
.github/
├── workflows/build-testes.yml        # scanner no job "Build e testes", push na main, demo só em PR
├── workflows/prs-de-fork.yml         # NOVO: comenta e fecha PRs de forks
└── rulesets/protecao-main.json       # NOVO: cópia versionada do ruleset 26400051
.config/dotnet-tools.json             # + dotnet-sonarscanner 11.3.0
coverage.runsettings                  # + Worker e PagamentosSimulados
README.md                             # seção "Análise estática"; badges no PR 2
docs/proximos-passos.md, docs/estado-atual.md
src/**                                # só no PR 2: correções de bugs e vulnerabilidades da baseline
```

**Structure Decision**: tudo no CI e na configuração do repositório. O código de `src/` só muda
no PR 2, se a situação de partida apontar bugs ou vulnerabilidades.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Análise sobe o tempo do PR | Mesmo job, sem recompilar; cache do Sonar; meta de no máximo 5 min (SC-007) |
| `*.cobertura.xml` lido como XML do Visual Studio | Os Cobertura saem da pasta lida pelo Sonar depois do resumo (R-05) |
| Push na `main` volta a gastar CI | Só o job de testes, uma vez por merge; a demonstração não roda |
| `pull_request_target` com permissão de escrita | Nenhum checkout nem execução do código do PR; só `gh pr close` com comentário fixo |
| Check exigido inexistente bloquearia todos os PRs | O check do Quality Gate só entra no ruleset no PR 2, depois de rodar; o `Compatibilidade do contrato` não é exigido (só roda com contrato alterado) |
| Status falso com o mesmo nome do check do Sonar | O ruleset amarra o check ao `integration_id` do app do SonarQube Cloud (R-09) |
| Pontos de segurança da baseline travando o primeiro PR depois da integração | Triagem completa no PR 2, antes de exigir o Quality Gate (R-10) |
| Situação de partida com muitos code smells | Não bloqueiam (spec); viram dívida registrada no research |
| Serviço do Sonar fora do ar | Merge espera; o ruleset pode ser desligado de forma registrada (spec, clarify) |
| Documentos de fluxo | Nenhum fluxo do sistema muda; o PR diz isso (R-12) |
