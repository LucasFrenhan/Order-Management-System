# Implementation Plan: Estrutura da solução e ambiente local

**Branch**: `feature/req-02-setup-infraestrutura` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-setup-infraestrutura/spec.md`

**Requisito**: REQ-02 (GitHub issue #3)

## Summary

Este REQ faz três coisas:
- **Estrutura:** completa as camadas com o projeto `PedidoFulfillment.Infrastructure`, que
  tem um ponto único de registro, e protege a direção de dependências entre camadas com um
  teste de arquitetura.
- **Ambiente local:** um `docker-compose.yml` sobe SQL Server e RabbitMQ (painel na 15672)
  com verificação de saúde, volumes nomeados, versões fixas e credenciais de
  desenvolvimento trocáveis por variável de ambiente.
- **Configuração e saúde na API:** a API lê as configurações validando-as na inicialização
  e expõe `/health` e `/health/pronto` com verificações próprias de banco e broker.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**: ASP.NET Core Health Checks (nativo); `Microsoft.Data.SqlClient`
7.1.1; `RabbitMQ.Client` 7.2.2; Docker Compose v2

**Storage**: SQL Server 2022 (CU26, edição Developer) em contêiner; o banco da aplicação
nasce no REQ-04

**Testing**: xUnit v3, FluentAssertions 7.x, WebApplicationFactory; Testcontainers 4.15.0
(MsSql e RabbitMq) para o caminho feliz de prontidão, ignorado sem Docker

**Target Platform**: máquina do desenvolvedor (Windows/Linux/macOS com Docker); API via SDK

**Project Type**: web-service + ambiente local de dependências

**Performance Goals**: dependências saudáveis em < 2 min com imagens baixadas (SC-002);
cada verificação de prontidão ≤ 5 s (FR-018)

**Constraints**: nenhuma credencial de outros ambientes versionada; a suíte roda sem Docker;
os endereços de saúde ficam fora do OpenAPI v1; nada de pacotes de health check de terceiros

**Scale/Scope**: 1 projeto novo, 2 serviços de contêiner, 2 verificações de saúde, 2
endereços, 1 teste de arquitetura

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada e clarificada (2 perguntas); commits `[REQ-02]` |
| II. Contratos versionados | ✅ | contrato v1 intacto (saúde fora do OpenAPI; conformidade do REQ-01 segue verde); formato de saúde documentado em `contracts/saude.md` |
| III. Segurança por padrão | ✅ | só `/health*` ficam públicos, como o princípio permite para saúde; respostas sem dados de conexão; credenciais versionadas só de desenvolvimento |
| IV. Desacoplamento assíncrono | ➖ N/A | nenhum efeito colateral; o broker só sobe aqui (eventos no REQ-06) |
| V. Qualidade verificável | ✅ | teste de arquitetura; testes de saúde (503 sem Docker, 200 com Testcontainers); teste de configuração ausente |
| Stack (.NET 10, SQL Server, RabbitMQ.Client nativo) | ✅ | sem MassTransit/Wolverine; sem PostgreSQL |
| Endpoint só roteia | ✅ | lógica das verificações na Infrastructure; a API só mapeia |
| Dependências NuGet justificadas | ✅ | tabela abaixo |
| Nomes em português | ✅ | `AdicionarInfraestrutura`, `VerificacaoSqlServer`, `OpcoesRabbitMQ`, `/health/pronto` |
| Branch dedicada | ✅ | `feature/req-02-setup-infraestrutura` (empilhada sobre a do REQ-01) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1: sem mudanças.

### Justificativa de dependências NuGet

| Pacote | Projeto | Justificativa |
|--------|---------|---------------|
| `Microsoft.Data.SqlClient` 7.1.1 | Infrastructure | driver oficial do SQL Server; verificação de prontidão agora e base do EF Core no REQ-04 (R-05) |
| `RabbitMQ.Client` 7.2.2 | Infrastructure | cliente nativo exigido pela constituição; prontidão agora e publicação no REQ-06 (R-05) |
| `Testcontainers.MsSql` 4.15.0 | testes | constituição: Testcontainers quando aplicável; prontidão com SQL Server real (R-07) |
| `Testcontainers.RabbitMq` 4.15.0 | testes | idem, para o broker (R-07) |

Escolhi a 4.15.0 do Testcontainers em vez da 4.16.0 porque a 4.16.0 saiu há menos de duas semanas.

## Project Structure

### Documentation (this feature)

```text
specs/002-setup-infraestrutura/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── saude.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
docker-compose.yml                       # sqlserver + rabbitmq (R-02)
.env.example                             # variáveis OMS_* documentadas (R-03)
.gitignore                               # + .env

src/
├── PedidoFulfillment.Infrastructure/    # NOVO
│   ├── ServiceCollectionExtensions.cs   # AdicionarInfraestrutura(services, configuration)
│   ├── Configuracao/
│   │   ├── OpcoesSqlServer.cs
│   │   └── OpcoesRabbitMQ.cs
│   └── Saude/
│       ├── VerificacaoSqlServer.cs
│       └── VerificacaoRabbitMQ.cs
└── PedidoFulfillment.API/
    ├── Program.cs                       # chama AdicionarInfraestrutura e MapearEndpointsSaude
    ├── Saude/EndpointsSaude.cs          # /health, /health/pronto + escritor de resposta
    └── appsettings.Development.json     # ConnectionStrings:SqlServer + RabbitMQ

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   └── Configuracao/                    # validação de OpcoesRabbitMQ/OpcoesSqlServer
└── PedidoFulfillment.Testes.Integracao/
    ├── Arquitetura/DependenciasCamadasTestes.cs
    ├── Saude/SaudeTestes.cs             # /health, 503, formato sem segredos, fora do OpenAPI
    ├── Saude/SaudeComDependenciasTestes.cs   # Testcontainers; ignorado sem Docker
    ├── Configuracao/ConfiguracaoAusenteTestes.cs
    └── Seguranca/AutorizacaoEndpointsTestes.cs  # passa a permitir /health*
```

**Structure Decision**: Clean Architecture com quatro camadas, conforme a issue. O teste
de unidade passa a referenciar também a Infrastructure, para validar as opções.

## Complexity Tracking

Sem violações a justificar.

| Risco | Mitigação |
|-------|-----------|
| Docker ausente nesta máquina | testes de contêiner ignorados com aviso; quickstart §2–§4 exige Docker (R-08) |
| Banco `PedidoFulfillment` ainda não existe | a verificação conecta no `master` (R-05) |
| Validação de configuração quebrar os testes do REQ-01 | `FabricaApi` passa valores de teste via `UseSetting` (R-04) |
| Varredura de autorização (T060 do REQ-01) rejeitar `/health` | atualizar a lista de rotas públicas permitidas |
