# Implementation Plan: Autenticação e autorização de usuários

**Branch**: `feature/req-03-autenticacao-identity` | **Date**: 2026-10-21 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-autenticacao-identity/spec.md`

**Requisito**: REQ-03 (GitHub issue #4)

## Summary

Este REQ faz o registro e o login do contrato v1 funcionarem de verdade:
- **Armazenamento:** ASP.NET Core Identity sobre EF Core e SQL Server, num
  `ContextoBancoDados` que o REQ-04 vai estender. Nasce a primeira migration do banco.
- **Regras:** um `ServicoAutenticacao` no Application cuida de aparo do nome, 409 por e-mail
  duplicado, 401 genérico (inclusive com a conta bloqueada após 5 erros) e eventos de
  segurança com e-mail mascarado. O Identity fica atrás de `IGestorIdentidade`.
- **Token:** a API emite o JWT com as mesmas opções e claims que já valida desde o REQ-01.
- **Inicialização:** um serviço de preparação aplica migrations (só em Development e
  `Testes`), cria os perfis e os usuários iniciais configurados, e impede a API de subir sem
  banco ou com migration pendente. Uma configuração desliga a preparação.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**: ASP.NET Core Identity (`AddIdentityCore`) com
`Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13;
`Microsoft.EntityFrameworkCore.SqlServer` 10.0.13; `JsonWebTokenHandler` (já disponível pelo
`JwtBearer` 10.0.13); `Microsoft.Extensions.Logging.Abstractions` 10.0.13 no Application;
ferramenta local `dotnet-ef` 10.0.13

**Storage**: SQL Server 2022 (contêiner do REQ-02); banco `PedidoFulfillment` criado pela
migration `InicialIdentidade`

**Testing**: xUnit v3, FluentAssertions 7.x, WebApplicationFactory; Testcontainers.MsSql
4.15.0 para os cenários com banco real, ignorados sem Docker

**Target Platform**: API ASP.NET Core rodando pelo SDK; banco em contêiner

**Project Type**: web-service

**Performance Goals**: 95% dos logins corretos em < 1 s no ambiente local (SC-005)

**Constraints**: contrato v1 inalterado (FR-015); regra de senha idêntica à do contrato
(FR-005); nenhuma senha, hash, token ou e-mail completo em respostas de erro e logs
(SC-003); a suíte continua rodando sem Docker

**Scale/Scope**: 2 operações implementadas, 7 tabelas do Identity, 1 migration, 1 serviço
de preparação, 4 eventos de log

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada após specify (3 perguntas) e clarify (3 perguntas); commits `[REQ-03]` |
| II. Contratos versionados | ✅ | OpenAPI v1 intocado (FR-015); bloqueio responde o 401 já previsto, sem 423/429; conteúdo do token documentado em `contracts/token-acesso.md` |
| III. Segurança por padrão | ✅ | só register/login públicos (já eram); hash com sal; 401 genérico e tempo equalizado (R-05); bloqueio após 5 erros; nenhum segredo fora de desenvolvimento versionado |
| IV. Desacoplamento assíncrono | ➖ N/A | nenhum efeito colateral entre domínios; eventos de negócio no REQ-06 |
| V. Qualidade verificável | ✅ | unitários das regras (R-11); integração com banco real para registro, login, bloqueio, perfis e tabela 401/403 com tokens emitidos pelo login |
| Stack (.NET 10, Minimal APIs, SQL Server via EF Core, Identity + JWT Bearer) | ✅ | exatamente o exigido |
| Endpoint só roteia | ✅ | `EndpointsAutenticacao` não muda; regras no `ServicoAutenticacao` |
| Regras em serviços de aplicação | ✅ | `ServicoAutenticacao` no Application; Identity só na Infrastructure (R-01) |
| Dependências NuGet justificadas | ✅ | tabela abaixo |
| Nomes em português | ✅ | `ServicoAutenticacao`, `GestorIdentidade`, `EmissorTokenJwt`, `ContextoBancoDados`, `PreparacaoBancoDados`, `Usuario`, `Perfil`, tabelas `Usuarios`/`Perfis` |
| Branch dedicada | ✅ | `feature/req-03-autenticacao-identity` (empilhada sobre a do REQ-02, ainda em revisão no PR #13) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1: sem mudanças.

### Justificativa de dependências NuGet

| Pacote | Projeto | Justificativa |
|--------|---------|---------------|
| `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13 | Infrastructure | constituição: autenticação com ASP.NET Core Identity; stores do Identity sobre EF Core (R-02) |
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 | Infrastructure | constituição: SQL Server via EF Core; base também do REQ-04 (R-07) |
| `Microsoft.EntityFrameworkCore.Design` 10.0.13 | API (`PrivateAssets=all`) | exigido pela ferramenta de migrations no projeto de inicialização; não vai para o binário publicado (R-07) |
| `Microsoft.Extensions.Logging.Abstractions` 10.0.13 | Application | `ILogger` e o gerador `LoggerMessage` para os eventos de segurança do FR-019 (R-10) |
| `dotnet-ef` 10.0.13 (ferramenta local) | `.config/dotnet-tools.json` | comando reproduzível para aplicar migrations fora de Development (FR-017, R-07) |

Todas na série 10.0.13, a mesma do ASP.NET Core já usado. O `Microsoft.Data.SqlClient`
continua fixado em 7.1.1 (REQ-02), acima da versão mínima que o EF Core 10 pede.

## Project Structure

### Documentation (this feature)

```text
specs/003-autenticacao-identity/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── token-acesso.md
│   └── configuracao.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
.config/dotnet-tools.json                     # NOVO: dotnet-ef 10.0.13

src/
├── PedidoFulfillment.Application/
│   └── Autenticacao/
│       ├── ServicoAutenticacao.cs            # NOVO: regras + eventos (LoggerMessage)
│       ├── IGestorIdentidade.cs              # NOVO: CriarAsync, VerificarCredenciaisAsync
│       ├── ResultadoCredenciais.cs           # NOVO: Valida/Invalida/Bloqueada/BloqueadaAgora
│       ├── IEmissorToken.cs                  # NOVO
│       └── MascaraEmail.cs                   # NOVO
├── PedidoFulfillment.Infrastructure/
│   ├── ExtensoesInfraestrutura.cs            # + DbContext, Identity, gestor, preparação
│   ├── Configuracao/
│   │   ├── OpcoesBanco.cs                    # NOVO: PrepararNaInicializacao
│   │   └── OpcoesUsuariosIniciais.cs         # NOVO: + ValidadorOpcoesUsuariosIniciais
│   ├── Identidade/
│   │   ├── Usuario.cs                        # NOVO: IdentityUser<Guid> + Nome
│   │   ├── Perfil.cs                         # NOVO: IdentityRole<Guid>
│   │   └── GestorIdentidade.cs               # NOVO: UserManager, transação, bloqueio
│   └── Persistencia/
│       ├── ContextoBancoDados.cs             # NOVO: IdentityDbContext, tabelas em português
│       ├── PreparacaoBancoDados.cs           # NOVO: IHostedService (R-08)
│       └── Migracoes/                        # NOVO: InicialIdentidade
└── PedidoFulfillment.API/
    ├── Program.cs                            # registra ServicoAutenticacao e EmissorTokenJwt
    ├── Seguranca/EmissorTokenJwt.cs          # NOVO
    ├── Provisorio/ServicoNaoImplementado.cs  # deixa de implementar IServicoAutenticacao
    └── appsettings.Development.json          # + UsuariosIniciais (SOMENTE DESENVOLVIMENTO)

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Autenticacao/ServicoAutenticacaoTestes.cs
│   ├── Autenticacao/MascaraEmailTestes.cs
│   └── Configuracao/OpcoesUsuariosIniciaisTestes.cs
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaApi.cs          # PrepararNaInicializacao=false por padrão
    ├── Infraestrutura/CapturaLogs.cs         # NOVO: ILoggerProvider de teste (SC-003)
    ├── Autenticacao/EmissorTokenTestes.cs    # sem Docker
    ├── Autenticacao/PreparacaoInicializacaoTestes.cs  # banco inacessível, preparação desligada
    └── Autenticacao/AutenticacaoComBancoTestes.cs     # Testcontainers: ponta a ponta, bloqueio, perfis, 401/403
```

**Structure Decision**: mantém as quatro camadas do REQ-02. As regras ficam no Application,
o Identity e a persistência na Infrastructure, e a emissão do token na API, junto da
validação.

## Complexity Tracking

Sem violações a justificar.

| Risco | Mitigação |
|-------|-----------|
| Preparação ligada quebrar os testes atuais, que não têm banco | `FabricaApi` desliga a preparação por padrão (R-11) |
| Regras de senha do Identity divergirem do contrato | regras de caractere do Identity desligadas; uma só fonte (R-04) |
| Registro concorrente passar pela checagem do Identity | índice único em `NormalizedEmail` e tradução da violação para 409 (R-03) |
| EF Core 10 com `Microsoft.Data.SqlClient` 7.1.1 fixado (EF referencia uma versão menor) | API pública estável entre as versões; os testes com banco real comprovam |
| Mensagem de falha de conexão vazar o servidor | exceção interna descartada na mensagem de inicialização (R-08) |
| Testes de bloqueio dependerem de esperar 15 min | o teste move `LockoutEnd` para o passado com o `UserManager` (R-11) |
