# Implementation Plan: Contratos da API v1 (OpenAPI e DTOs)

**Branch**: `feature/req-01-contratos-api` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/001-contratos-api/spec.md`

**Requisito**: REQ-01 (GitHub issue #2)

## Summary

Publicar o contrato v1 da API do OMS antes de qualquer regra de negócio. O contrato
aprovado é `contracts/openapi-v1.yaml`, escrito à mão. A API em .NET 10 mapeia as 5 rotas
com DTOs, autorização por policies e erros RFC 7807, e gera seu próprio documento OpenAPI
3.1 de forma nativa. Um teste de conformidade garante que esse documento gerado é igual
ao contrato aprovado.

Os handlers só delegam a interfaces da camada Application. Neste REQ as interfaces têm
uma implementação provisória que devolve 501. As regras reais chegam no REQ-03
(autenticação) e no REQ-05 (pedidos).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.x)

**Primary Dependencies**: ASP.NET Core Minimal APIs; `Microsoft.AspNetCore.OpenApi`;
`Scalar.AspNetCore`; `Microsoft.AspNetCore.Authentication.JwtBearer` (ver justificativas)

**Storage**: N/A neste REQ (SQL Server/EF Core no REQ-04)

**Testing**: xUnit v3, FluentAssertions 7.x, `Microsoft.AspNetCore.Mvc.Testing`
(WebApplicationFactory), `Microsoft.OpenApi.YamlReader` (leitura do contrato nos testes)

**Target Platform**: serviço web Linux/Windows (Kestrel); contêiner Docker a partir do REQ-02

**Project Type**: web-service (API REST)

**Performance Goals**: N/A para este REQ (sem processamento de negócio); o documento
OpenAPI é gerado uma vez e servido do cache

**Constraints**: contrato v1 só evolui com adições opcionais; documento e UI só em
`Development`/`Testes`; nomes em português; nenhum endpoint acessa dados nem contém regra

**Scale/Scope**: 5 operações, 10 schemas de DTO/enum, 3 schemas de erro, 3 policies

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada e clarificada; contrato YAML aprovado antes do código; commits com `[REQ-01]` |
| II. Contratos imutáveis e versionados | ✅ | prefixo `/api/v1`; FR-023; teste de conformidade (R-11); catálogo de `codigo` estável |
| III. Segurança por padrão | ✅ | JWT Bearer + policies em todas as rotas exceto register/login e documentação; documentação só fora de produção; erros sem dados sensíveis |
| IV. Desacoplamento assíncrono | ➖ N/A | nenhum efeito colateral neste REQ (eventos no REQ-06) |
| V. Qualidade verificável | ✅ | testes unitários de validação; integração 401/403 + sucesso por rota protegida; testes de serialização dos DTOs |
| Minimal API só roteia | ✅ | handlers delegam a `IServicoAutenticacao` / `IServicoPedidos` |
| Regras em Application/Domain | ✅ | dono do pedido e matriz de transições ficam no serviço (R-06) |
| 400 em ProblemDetails (RFC 7807) | ✅ | R-07, R-08 |
| Dependências NuGet justificadas | ✅ | tabela abaixo |
| Nomes em português | ✅ | DTOs, namespaces, policies e mensagens em português; sufixo `DTO` mantido conforme issue |
| Branch dedicada | ✅ | `feature/req-01-contratos-api` |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1: sem mudanças.

### Justificativa de dependências NuGet

| Pacote | Projeto | Justificativa |
|--------|---------|---------------|
| `Microsoft.AspNetCore.OpenApi` | API | geração nativa e oficial do documento OpenAPI (R-02) |
| `Scalar.AspNetCore` | API | UI navegável do contrato, exigida pela User Story 1 (R-03) |
| `Microsoft.AspNetCore.Authentication.JwtBearer` | API | validação de JWT; stack definida na constituição (R-06) |
| `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk` | testes | framework de teste exigido pela constituição |
| `FluentAssertions` (7.x) | testes | exigido pela constituição; série 7.x por licença (R-12) |
| `Microsoft.AspNetCore.Mvc.Testing` | testes | API em memória para testes de integração e conformidade |
| `Microsoft.OpenApi.YamlReader` | testes | leitura do contrato YAML no mesmo modelo do gerador (R-11) |

## Project Structure

### Documentation (this feature)

```text
specs/001-contratos-api/
├── spec.md
├── plan.md              # este arquivo
├── research.md          # decisões R-01 a R-13
├── data-model.md        # DTOs, enums, ciclo de vida, catálogo de erros
├── quickstart.md        # guia de validação
├── contracts/
│   └── openapi-v1.yaml  # contrato v1 aprovado (fonte da verdade)
├── checklists/
│   └── requirements.md
└── tasks.md             # gerado por /speckit-tasks
```

### Source Code (repository root)

```text
PedidoFulfillment.sln
Directory.Build.props                  # net10.0, Nullable, TreatWarningsAsErrors
Directory.Packages.props               # versões centralizadas (justificadas acima)

src/
├── PedidoFulfillment.Domain/
│   ├── Pedidos/StatusPedido.cs
│   └── Usuarios/PerfilUsuario.cs
├── PedidoFulfillment.Application/
│   ├── Contratos/
│   │   ├── Autenticacao/              # RegistrarUsuarioRequestDTO, UsuarioResponseDTO,
│   │   │                              # LoginRequestDTO, LoginResponseDTO
│   │   ├── Pedidos/                   # CriarPedidoRequestDTO, ItemPedidoDTO,
│   │   │                              # ItemPedidoResponseDTO, PedidoResponseDTO,
│   │   │                              # AtualizarStatusPedidoRequestDTO
│   │   └── Erros/CodigosErro.cs       # catálogo estável de `codigo`
│   ├── Autenticacao/IServicoAutenticacao.cs
│   ├── Pedidos/IServicoPedidos.cs
│   └── Comum/Resultado.cs             # sucesso | erro de negócio (codigo, status)
└── PedidoFulfillment.API/
    ├── Program.cs
    ├── Endpoints/
    │   ├── EndpointsAutenticacao.cs   # MapGroup("/api/v1/auth")
    │   └── EndpointsPedidos.cs        # MapGroup("/api/v1/pedidos")
    ├── Seguranca/
    │   ├── PoliticasAutorizacao.cs    # nomes e registro das policies
    │   └── ConfiguracaoJwt.cs         # validação + OnChallenge/OnForbidden → ProblemDetails
    ├── Erros/
    │   ├── ConfiguracaoProblemDetails.cs
    │   └── FiltroValidacao.cs
    ├── OpenApi/
    │   └── TransformadorSegurancaOpenApi.cs   # bearerAuth + 401/403 nas rotas protegidas
    └── Provisorio/
        └── ServicoNaoImplementado.cs  # 501 até REQ-03/REQ-05 (remover depois)

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   └── Contratos/                     # validação dos DTOs
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/                # FabricaApi (WebApplicationFactory), GeradorTokenTeste
    ├── Contrato/ConformidadeOpenApiTestes.cs
    ├── Seguranca/AutorizacaoEndpointsTestes.cs
    ├── Erros/FormatoErroTestes.cs
    └── Serializacao/SerializacaoDtoTestes.cs
```

**Structure Decision**: Clean Architecture simples, conforme a issue de setup (REQ-02).
Este REQ cria só Domain, Application e API, o mínimo para publicar o contrato. O projeto
Infrastructure, o `docker-compose.yml` e as strings de conexão ficam no REQ-02. O teste
de conformidade lê o YAML diretamente de `specs/001-contratos-api/contracts/` (copiado
para a saída do build), assim existe uma única fonte da verdade.

## Complexity Tracking

Sem violações a justificar.

Riscos e pontos de atenção:

| Item | Mitigação |
|------|-----------|
| SDK .NET 10 ausente na máquina | instalar antes de `/speckit-implement` (quickstart) |
| 501 provisório pode vazar para o contrato | 501 não é documentado; teste de conformidade falha se aparecer no documento gerado |
| Diferenças de representação entre gerador nativo e YAML (ex.: `decimal` → `number/double`, nullability 3.1) | comparação semântica no teste (R-11); ajustes por transformer de schema quando necessário |
| Ordem de entrega: REQ-01 cria a solução antes do REQ-02 (setup) | REQ-02 passa a completar a estrutura (Infrastructure, Docker); registrar isso no PR |
