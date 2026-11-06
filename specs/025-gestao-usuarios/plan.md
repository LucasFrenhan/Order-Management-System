# Implementation Plan: Gestão de usuários e perfis pelo Admin

**Branch**: `feature/req-25-gestao-usuarios` | **Date**: 2026-11-06 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/025-gestao-usuarios/spec.md`

**Requisito**: REQ-25 (GitHub issue #54)

## Summary

- **Sete rotas novas em `/api/v1/admin/usuarios`**, só por acréscimo ([contrato](./contracts/gestao-usuarios.md)).
- **Desativação é um estado próprio** (`DesativadoEm`, `ReativadoEm`), porque a redefinição de senha zera o bloqueio de conta ([R-01](./research.md#r-01-como-guardar-a-desativação-fr-015-fr-019)).
- **Política nova confere o Admin ativo no banco a cada chamada de gestão** (R-02). As demais rotas seguem o token.
- **Um lock de aplicação serializa as alterações**, o que garante o último Admin entre instâncias (R-03). As regras ficam em uma classe pura, em ordem definida (R-04).
- **O convite reaproveita a rota `redefinicao-senha`**, sem rota pública nova (R-05).
- **Webhooks**: uma regra de datas, na distribuição e no envio (R-06).
- **Busca por `Contains`** em nome e e-mail, com meta de desempenho a medir na implementação (R-07).
- **Migration `GestaoUsuarios`**; logs 3019 a 3025; quatro códigos de erro; **nenhum pacote novo**.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.403)

**Primary Dependencies**: as que já existem: ASP.NET Core 10, Identity, EF Core SqlServer, OpenTelemetry, MailKit e Testcontainers. Nenhuma nova.

**Storage**: SQL Server 2022; duas colunas e um índice em `Usuarios` ([data-model](./data-model.md)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory, Testcontainers MsSql e Mailpit; conformidade do contrato e oasdiff.

**Target Platform**: API e Worker .NET 10; SQL Server, RabbitMQ e Mailpit em contêiner

**Project Type**: web-service

**Performance Goals**: login de conta desativada com mediana a menos de 50 ms da de uma senha errada (SC-004); busca com p95 abaixo de 500 ms em 100 mil contas, ainda não medida (R-07).

**Constraints**:
- contrato v1 só com acréscimos; contas ativas não veem mudança nas rotas existentes;
- regras na Application; a API só traduz HTTP; o acesso a dados fica na Infrastructure;
- logs de auditoria só com ids e perfil;
- decisão do REQ-24 mantida: nenhuma consulta por requisição fora das rotas de gestão.

**Scale/Scope**: 7 rotas, ~6 DTOs, 1 serviço, 1 gestor, 1 política, 2 colunas, ~80 testes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Avaliação | Situação |
|-------------------|-----------|----------|
| I. Specification-First | Spec, clarify e plano antes do código; issue #54 | ✅ |
| II. Contratos imutáveis | Só acréscimos: rotas, schemas e códigos. `redefinicao-senha` mantém corpo e respostas, e só ganha uma frase na descrição | ✅ |
| III. Segurança por padrão | Todas as rotas exigem token e política; a conferência do Admin ativo faz parte da política; nenhum dado de credencial nas respostas; nenhuma resposta pública revela a desativação | ✅ |
| IV. Desacoplamento assíncrono | O convite usa a tabela e o processador de e-mails do REQ-24, cuja exceção à mensageria já foi aprovada. A gestão não gera evento: não há outro domínio interessado | ✅ |
| V. Qualidade verificável | Unitários das regras; integração com sucesso, 401 e 403 em toda rota; concorrência do último Admin; envio real pelo Mailpit | ✅ |
| Endpoint só roteia | As rotas delegam ao serviço; o manipulador da política delega ao gestor | ✅ |
| Persistência em SQL Server via EF Core | Tudo pelo EF Core, inclusive `ExecuteUpdate` e o `sp_getapplock` no comando da transação | ✅ |
| 400 em ProblemDetails | `FiltroValidacao` nas rotas novas | ✅ |
| Nova dependência NuGet | Nenhuma | ✅ |

**Re-check pós-design**: sem violações.

## Project Structure

### Documentation (this feature)

```text
specs/025-gestao-usuarios/
├── plan.md
├── research.md          # R-01 a R-10
├── data-model.md
├── quickstart.md
├── contracts/gestao-usuarios.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsUsuarios.cs               # novo: 7 rotas
│   ├── OpenApi/                                     # parâmetros da listagem
│   ├── Seguranca/PoliticasAutorizacao.cs            # + PoliticaGestaoUsuarios
│   ├── Seguranca/RequisitoAdminAtivo.cs             # novo: requisito e manipulador
│   └── Program.cs
├── PedidoFulfillment.Application/
│   ├── Contratos/Usuarios/*.cs                      # DTOs novos
│   ├── Contratos/Erros/CodigosErro.cs               # + 4 códigos
│   ├── Comum/EnvelopeCursor.cs                      # novo: parte comum dos cursores
│   ├── Produtos/CodificadorCursorProdutos.cs        # usa o EnvelopeCursor, mesmo formato
│   ├── Usuarios/                                    # novo
│   │   ├── IServicoGestaoUsuarios.cs, ServicoGestaoUsuarios.cs
│   │   ├── IGestorUsuarios.cs
│   │   ├── RegrasGestaoUsuarios.cs                  # regras puras, em ordem (R-04)
│   │   ├── CodificadorCursorUsuarios.cs, PosicaoCursorUsuarios.cs, FiltroListagemUsuarios.cs
│   │   └── EventosGestaoUsuarios.cs                 # logs 3020 a 3025
│   ├── Autenticacao/
│   │   ├── IArmazenamentoSessoes.cs                 # + ConviteUsuario; consumo do token de senha
│   │   ├── IGestorIdentidade.cs, ResultadoCredenciais.cs  # + Desativada; redefinição devolve a finalidade
│   │   ├── ServicoAutenticacao.cs, ServicoSenhas.cs
│   │   └── EventosAutenticacao.cs                   # + log 3019
│   └── Emails/{TipoEmail,ModelosEmail}.cs           # + convite
└── PedidoFulfillment.Infrastructure/
    ├── Configuracao/OpcoesEmail.cs                  # + ValidadeConviteHoras
    ├── Identidade/
    │   ├── Usuario.cs                               # + DesativadoEm, ReativadoEm
    │   ├── GestorIdentidade.cs                      # login, redefinição, troca e confirmação
    │   └── GestorUsuarios.cs                        # novo: listagem, lock e alterações
    ├── Autenticacao/ArmazenamentoSessoes.cs         # renovação recusa conta desativada
    ├── Emails/ProcessadorEmails.cs                  # descarta conta desativada; envia convite
    ├── Notificacoes/ProcessadorNotificacoes.cs      # regra de datas (R-06)
    └── Persistencia/
        ├── ContextoBancoDados.cs                    # índice
        └── Migracoes/<data>_GestaoUsuarios.cs

tests/
├── PedidoFulfillment.Testes.Unitarios/Usuarios/     # regras, cursor, serviço
└── PedidoFulfillment.Testes.Integracao/
    ├── Usuarios/GestaoUsuarios*Testes.cs            # rotas, 401/403, concorrência
    ├── Usuarios/ContaDesativada*Testes.cs           # login, renovação, e-mail, webhooks
    └── Usuarios/Convite*Testes.cs                   # convite ponta a ponta com o Mailpit

specs/001-contratos-api/contracts/openapi-v1.yaml    # + rotas, schemas, códigos
docs/fluxos/01-autenticacao.md                       # perfis, gestão, convite, limitações
docs/fluxos/13-notificacao-de-status.md              # conta desativada
docs/fluxos/16-envio-de-email.md                     # convite
docs/proximos-passos.md                              # fila
```

**Structure Decision**: os projetos existentes, com a pasta `Usuarios` na Application, no molde de `Produtos`. O gestor novo fica separado do `GestorIdentidade`, que continua cuidando só de credenciais. O `README.md` não recebe acréscimos sem permissão.

## Complexity Tracking

| Ponto | Por quê | Alternativa mais simples descartada porque |
|-------|---------|-------------------------------------------|
| Consulta ao banco por chamada nas rotas de gestão, exceção à regra do REQ-24 | Decisão do usuário: um Admin removido perde a gestão na hora | Só o token deixaria um ex-Admin alterar usuários por até 15 minutos |
| Lock de aplicação em todas as alterações | É o que garante o último Admin entre instâncias (R-03) | Contar e depois retirar, sem o lock, deixa dois Admins removerem um ao outro |
| `EnvelopeCursor` mexe em código do REQ-22 | Evita a terceira cópia do mesmo código de cursor (R-07) | Copiar aumentaria a duplicação medida pelo Sonar; os testes de cursor protegem o formato |
