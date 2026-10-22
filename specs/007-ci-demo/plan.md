# Implementation Plan: CI de build e testes e ambiente de demonstração

**Branch**: `feature/req-07-ci-demo` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/007-ci-demo/spec.md`

**Requisito**: REQ-07 (GitHub issue #15)

## Summary

- **Workflow**: `build-testes`, com dois jobs em paralelo em `ubuntu-latest`, que já tem
  Docker:
  - **"Build e testes"**: restaura com cache, compila em Release com avisos como erros e
    roda as duas suítes com `OMS_EXIGIR_DOCKER=true`. Gera TRX e cobertura em formato
    Cobertura com o coletor que já vem no SDK de testes. Um script do repositório escreve
    o resumo e reprova se houver teste ignorado.
  - **"Demonstração"**: sobe o perfil `demo` e confere a prontidão e o login.
- **Ações**: só oficiais (`actions/*`), fixadas por SHA, com `contents: read`.
- **Demonstração**:
  - `Dockerfile` em duas etapas (`sdk:10.0.402-noble` → `aspnet:10.0.13-noble`), rodando
    como o usuário `app`;
  - serviço `api` no Compose sob o perfil `demo`, com healthcheck em bash e `/dev/tcp`
    (a imagem não tem `curl`);
  - a API roda em Development, apontando para os serviços do Compose.
- **Código**: só nos testes. As fixtures de Docker passam a falhar quando a exigência
  está ligada. A API não muda.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402); YAML do GitHub Actions; Dockerfile;
bash

**Primary Dependencies**:
- `actions/checkout` v7.0.1, `actions/setup-dotnet` v6.0.0, `actions/cache` v6.1.0 e
  `actions/upload-artifact` v7.0.2;
- imagens `mcr.microsoft.com/dotnet/sdk:10.0.402-noble` e `aspnet:10.0.13-noble`.

Nenhum pacote NuGet novo.

**Storage**: N/A. A demonstração usa o SQL Server do Compose, com o volume que já existe.

**Testing**: xUnit v3 com Testcontainers, que já existiam; cobertura pelo
`Microsoft.CodeCoverage`, que já vem com o `Microsoft.NET.Test.Sdk`.

**Target Platform**: GitHub Actions (`ubuntu-latest`); Docker Compose v2 na máquina local

**Project Type**: web-service (entrega e infraestrutura de build)

**Performance Goals**:
- verificação completa em até 15 minutos (SC-006);
- demonstração pronta em menos de 3 minutos, com as imagens baixadas (SC-005).

**Constraints**:
- `contents: read` e nenhum segredo;
- ações fixadas por SHA;
- o comando `docker compose up -d --wait` continua igual;
- a API e o contrato não mudam.

**Scale/Scope**:
- 1 workflow com 2 jobs e 1 script;
- 1 `Dockerfile`, 1 `.dockerignore` e 1 `coverage.runsettings`;
- 1 serviço novo no Compose;
- 1 classe de teste nova e 2 fixtures ajustadas.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada após o specify e o clarify (2 perguntas); commit `[REQ-07]` |
| II. Contratos versionados | ✅ | Nenhum contrato da API muda. O contrato da verificação e da demonstração está em `contracts/` |
| III. Segurança por padrão | ✅ | `contents: read`, ações por SHA e nenhum segredo. O contêiner roda sem privilégios. A demonstração usa só valores de desenvolvimento já versionados e marcados como tais. O contrato navegável só aparece porque a demonstração roda em Development |
| IV. Desacoplamento assíncrono | ➖ N/A | não há regra de negócio |
| V. Qualidade verificável | ✅ | É o objetivo do requisito: testes automatizados em todo PR, sem skip silencioso |
| Stack | ✅ | sem mudanças |
| Dependências NuGet justificadas | ✅ | nenhuma nova; as imagens base estão justificadas na R-07 |
| Nomes em português | ✅ | jobs, script, variáveis (`OMS_EXIGIR_DOCKER`, `OMS_API_PORTA`) e mensagens |
| Branch dedicada | ✅ | `feature/req-07-ci-demo`, a partir de `main` |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/007-ci-demo/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/verificacao-e-demonstracao.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
.github/
├── workflows/
│   ├── contrato-compatibilidade.yml   # já existe, sem mudança
│   └── build-testes.yml               # NOVO (R-01 a R-05, R-10)
└── scripts/resumo-testes.sh           # NOVO (R-05)
.dockerignore                          # NOVO (R-07)
coverage.runsettings                   # NOVO (R-04)
docker-compose.yml                     # + serviço api sob o perfil demo (R-09)
.env.example                           # + OMS_API_PORTA
README.md                              # selo, Demonstração rápida, Integração contínua (R-11)
src/PedidoFulfillment.API/Dockerfile   # NOVO (R-07)
tests/PedidoFulfillment.Testes.Integracao/
├── Infraestrutura/AmbienteDocker.cs           # NOVO (R-06)
├── Infraestrutura/AmbienteDockerTestes.cs     # NOVO
├── Infraestrutura/BancoDadosTeste.cs          # falha com a exigência ligada
└── Saude/SaudeComDependenciasTestes.cs        # idem
```

**Structure Decision**: nenhuma camada da solução muda. Tudo é de entrega, exceto a regra
de exigência de Docker, que fica nos utilitários de teste.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Testes com Testcontainers lentos ou instáveis no runner (imagem do SQL Server com ~1,5 GB) | Uma única execução, sem nova tentativa automática (spec, Edge Cases). Limite de 20 minutos por job, contra a meta de 15 |
| Healthcheck por `/dev/tcp` depender do bash da imagem | A imagem Noble tem bash. O job "Demonstração" quebra se isso mudar, e a imagem está fixada |
| Senha com `$` interpolada pelo Compose | Aviso no `.env.example`. O valor padrão não tem `$` |
| A demonstração e a API pelo SDK disputarem a porta 5230 | Aviso no README e `OMS_API_PORTA` |
| Mudança no formato do TRX ou do Cobertura quebrar o script | O script lê só atributos estáveis (`Counters`, `lines-covered`, `lines-valid`). O quickstart §3 o roda localmente |
| A proteção da branch é manual | Documentada no README. Fica fora do que o código consegue garantir |
