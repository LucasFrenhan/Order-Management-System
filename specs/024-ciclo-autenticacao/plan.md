# Implementation Plan: Ciclo de vida da autenticação

**Branch**: `feature/req-24-ciclo-autenticacao` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/024-ciclo-autenticacao/spec.md`

**Requisito**: REQ-24 (GitHub issue #55)

## Summary

- **Sete rotas novas e dois campos no login**, só por acréscimo ([contrato](./contracts/autenticacao.md)).
- **Sessão com rotação**: tabelas `SessoesAutenticacao` e `TokensRenovacao`. Um `UPDATE` condicional decide a disputa, e a reutilização encerra a sessão ([R-02](./research.md#r-02-sessão-e-rotação-fr-002-fr-004-fr-005)).
- **Tokens opacos de 256 bits, guardados só como SHA-256** (R-01), sem o e-mail na chamada.
- **O Worker gera o token na hora de enviar**: a API grava só uma solicitação, igual com ou sem conta. O token em claro nunca é gravado, o que dispensa a cifra e resolve também o tempo de resposta ([R-03](./research.md#r-03-envio-de-e-mail-quem-envia-e-como-o-token-fica-em-sigilo-fr-003-fr-013-fr-021)).
- **MailKit por SMTP puro** no Worker e **Mailpit** no compose e nos testes, por decisão do usuário (R-04, R-05). O provedor real fica fora do escopo.
- **Token de acesso de 15 minutos** por padrão (decisão do usuário).
- **Limite do REQ-23 generalizado** para as rotas novas (R-08).
- **Migration `CicloAutenticacao`**; logs 3010 a 3018; duas métricas; um código de erro novo; **um pacote novo**.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.403)

**Primary Dependencies**: as que já existem, ASP.NET Core 10, Identity, EF Core SqlServer 10.0.13, OpenTelemetry e Testcontainers; e, **nova, MailKit 4.18.2**, com justificativa em R-04.

**Storage**: SQL Server 2022 CU26; quatro tabelas novas ([data-model](./data-model.md)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory, Testcontainers MsSql e Mailpit; conformidade do contrato e oasdiff.

**Target Platform**: API e Worker .NET 10; SQL Server, RabbitMQ e Mailpit em contêiner

**Project Type**: web-service

**Performance Goals**: pedido de recuperação com e sem conta com medianas a menos de 50 ms (SC-005); e-mail pendente entregue em até 5 min depois da volta do SMTP (SC-007).

**Constraints**:
- contrato v1 só com acréscimos, inclusive o 409 do registro;
- regras na Application; a API só traduz HTTP; o acesso a dados e o SMTP ficam na Infrastructure;
- token, senha e e-mail completo nunca em logs, métricas ou spans;
- bloqueio de conta do REQ-03 e limite do REQ-23 sem mudança de comportamento nas rotas existentes.

**Scale/Scope**: 7 rotas, ~8 DTOs, 3 serviços, 1 processador no Worker, 4 tabelas, 1 pacote, ~90 testes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Avaliação | Situação |
|-------------------|-----------|----------|
| I. Specification-First | Spec, clarify e plano antes do código; issue #55 | ✅ |
| II. Contratos imutáveis | Só acréscimos: rotas, dois campos na resposta do login e um código. O 409 do registro continua. A duração do token muda só o valor do `expiraEm`, que o contrato já informa | ✅ |
| III. Segurança por padrão | Rotas públicas só de autenticação, como a constituição permite; a troca de senha exige token e perfil; tokens só como hash; respostas sem revelar contas | ✅ |
| IV. Desacoplamento assíncrono | O envio não é síncrono: tabela na mesma transação e processador no Worker, "mecanismo equivalente" ao outbox, como os webhooks do REQ-18 | ✅ |
| V. Qualidade verificável | Unitários das regras; integração com sucesso, 401 e 403; concorrência; envio real pelo Mailpit | ✅ |
| Endpoint só roteia | As rotas delegam aos serviços da Application | ✅ |
| Persistência em SQL Server via EF Core | Tabelas e operações pelo EF Core, inclusive os `UPDATE` condicionais, por `ExecuteUpdate`. `SqlConnection` só para o lock de aplicação, como as demais rotinas | ✅ |
| 400 em ProblemDetails | `FiltroValidacao` nas rotas novas | ✅ |
| Nova dependência NuGet | MailKit, com justificativa em R-04 | ✅ justificada |

**Re-check pós-design**: sem violações.

## Project Structure

### Documentation (this feature)

```text
specs/024-ciclo-autenticacao/
├── plan.md
├── research.md          # R-01 a R-11
├── data-model.md
├── quickstart.md
├── contracts/autenticacao.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
docker-compose.yml                                   # + mailpit; Email__* no Worker
Directory.Packages.props                             # + MailKit

src/
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsAutenticacao.cs           # + 7 rotas
│   ├── LimiteRequisicoes/LeitorEmailCorpo.cs        # renomeado de LeitorEmailLogin
│   ├── Seguranca/ConfiguracaoJwt.cs                 # padrão de 15 min
│   └── Program.cs, appsettings*.json
├── PedidoFulfillment.Application/
│   ├── Contratos/Autenticacao/*.cs                  # DTOs novos; LoginResponseDTO + 2 campos
│   ├── Contratos/Erros/CodigosErro.cs               # + TOKEN_INVALIDO
│   ├── Autenticacao/
│   │   ├── GeradorTokenOpaco.cs                     # token e SHA-256 (R-01)
│   │   ├── IArmazenamentoSessoes.cs, ConfiguracaoSessoes.cs
│   │   ├── IServicoSessoes.cs, ServicoSessoes.cs    # renovação e logout
│   │   ├── IServicoSenhas.cs, ServicoSenhas.cs      # troca, recuperação e redefinição
│   │   ├── IServicoConfirmacaoEmail.cs, ServicoConfirmacaoEmail.cs
│   │   ├── IMetricasAutenticacao.cs
│   │   ├── ServicoAutenticacao.cs                   # o login cria a sessão
│   │   ├── IGestorIdentidade.cs                     # + operações de senha e e-mail
│   │   └── EventosAutenticacao.cs                   # + logs 3010 a 3015
│   ├── Emails/                                      # novo
│   │   ├── IFilaEmails.cs, TipoEmail.cs, MensagemEmail.cs, IEnviadorEmail.cs
│   │   ├── ModelosEmail.cs                          # textos em português
│   │   └── EventosEmails.cs                         # logs 3016 a 3018
│   └── LimiteRequisicoes/{PoliticaLimite,ConfiguracaoLimites,ServicoLimiteRequisicoes}.cs  # rotas novas
└── PedidoFulfillment.Infrastructure/
    ├── Configuracao/{OpcoesEmail,OpcoesSessoes,OpcoesLimiteRequisicoes,OpcoesLimpeza}.cs
    ├── Identidade/GestorIdentidade.cs               # + senha e confirmação
    ├── Autenticacao/ArmazenamentoSessoes.cs, MetricasAutenticacao.cs  # novos
    ├── Emails/                                      # novo
    │   ├── FilaEmails.cs, EnviadorEmailSmtp.cs      # MailKit
    │   ├── ProcessadorEmails.cs                     # BackgroundService no Worker
    │   └── ExtensoesEmails.cs
    ├── Observabilidade/TelemetriaOms.cs             # + 2 métricas
    ├── Mensageria/LimpezaMensageria.cs              # + 2 alvos
    └── Persistencia/
        ├── {SessaoAutenticacao,TokenRenovacao,TokenUsoUnico,SolicitacaoEmail}.cs + Configuracoes
        ├── PreparacaoBancoDados.cs                  # usuários iniciais confirmados
        └── Migracoes/<data>_CicloAutenticacao.cs

tests/
├── PedidoFulfillment.Testes.Unitarios/Autenticacao/, Emails/, LimiteRequisicoes/
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/{FabricaApi,FabricaWorker,MailpitTeste}.cs
    ├── Autenticacao/{Sessoes,TrocaSenha,RecuperacaoSenha,ConfirmacaoEmail}*Testes.cs
    └── Emails/                                      # processador e Mailpit

specs/001-contratos-api/contracts/openapi-v1.yaml    # + rotas, schemas e campos
specs/003-autenticacao-identity/contracts/token-acesso.md  # 15 minutos
specs/016-observabilidade-opentelemetry/contracts/telemetria.md
specs/023-rate-limiting/contracts/limite-requisicoes.md
docs/fluxos/01-autenticacao.md, 11-limpeza-e-reenvio-da-dlq.md, 12-rastreabilidade-e-metricas.md
docs/fluxos/16-envio-de-email.md                     # novo; índice docs/fluxos/README.md
docs/proximos-passos.md
```

**Structure Decision**: os projetos existentes. A pasta `Emails`, na Application e na Infrastructure, segue o molde de `Notificacoes`. Os serviços novos ficam em interfaces próprias, em vez de crescer o `IServicoAutenticacao`.

## Complexity Tracking

| Ponto | Por quê | Alternativa mais simples descartada porque |
|-------|---------|-------------------------------------------|
| Pacote novo (MailKit), que não é violação, mas exige justificativa | O .NET não tem hoje um cliente SMTP recomendado (R-04) | `SmtpClient` é considerado obsoleto e gera o aviso DE0005 |
| Dependência operacional nova: o Worker passa a exigir a seção `Email` fora de Development | A recuperação não pode falhar em silêncio (R-04) | Um envio opcional deixaria a produção subir sem recuperação de senha |
