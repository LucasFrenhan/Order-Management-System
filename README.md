# Order Management System (OMS)

API de gestão de pedidos em .NET 10, desenvolvida com **Specification-Driven Development**
(Spec Kit). Toda funcionalidade nasce de uma especificação aprovada em `specs/`, e a
constituição do projeto fica em [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Estado atual

| Requisito | Situação |
|-----------|----------|
| REQ-01 — Contratos da API v1 ([spec](specs/001-contratos-api/spec.md)) | Contrato publicado; rotas mapeadas com validação, autorização e erros RFC 7807 |
| REQ-02 a REQ-06 | Pendentes |

> As operações da API ainda respondem **501 (não implementada)**. As regras de negócio
> chegam com o REQ-03 (autenticação) e o REQ-05 (pedidos).

## Pré-requisitos

- SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`)

## Ambiente local

O banco (SQL Server 2022) e o broker (RabbitMQ) rodam em contêineres; a API roda pelo SDK.
Pré-requisito: Docker com Compose v2 (`docker compose version`; no Windows,
`winget install Docker.DockerDesktop`).

```powershell
docker compose up -d --wait   # retorna só quando os dois serviços estão saudáveis
docker compose ps             # sqlserver e rabbitmq aparecem como "healthy"
docker compose down           # para; os dados continuam nos volumes
docker compose down -v        # para e apaga os dados locais (recomeçar do zero)
```

| Dependência | Endereço local | Credenciais de desenvolvimento |
|-------------|----------------|--------------------------------|
| SQL Server | `localhost,1433` | usuário `sa`, senha `OmsDev_2026!Local` |
| RabbitMQ (AMQP) | `localhost:5672` | usuário `oms`, senha `oms-dev-local` |
| Painel do RabbitMQ | <http://localhost:15672> | usuário `oms`, senha `oms-dev-local` |

Essas credenciais valem **só para os contêineres locais** e não são segredos reais.

**Porta ocupada ou outra credencial**: copie `.env.example` para `.env` (ignorado pelo Git)
e troque as variáveis `OMS_*`, sem editar arquivos versionados. Ao rodar a API, sobrescreva
também a configuração correspondente, por exemplo
`$env:ConnectionStrings__SqlServer = "Server=localhost,14330;..."`.

## Compilar, testar e executar

```powershell
dotnet build PedidoFulfillment.sln
dotnet test PedidoFulfillment.sln

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API
```

Em Development (e no ambiente `Testes`):

- `/scalar/v1`: interface navegável do contrato
- `/openapi/v1.yaml`: contrato aprovado ([`openapi-v1.yaml`](specs/001-contratos-api/contracts/openapi-v1.yaml))
- `/openapi/v1.json`: documento gerado pelo código (usado no teste de conformidade)

Em produção esses endereços não existem. Para obter um token de teste enquanto o login não
está implementado, gere um JWT HS256 com a chave de `appsettings.Development.json`, emissor e
audiência `PedidoFulfillment`, o claim `sub` (UUID do usuário) e os claims `role`.

Roteiro completo de validação: [`quickstart.md`](specs/001-contratos-api/quickstart.md).

## Estrutura

```text
src/
  PedidoFulfillment.Domain/          enums de domínio
  PedidoFulfillment.Application/     DTOs de contrato, interfaces de serviço, Resultado
  PedidoFulfillment.API/             Minimal APIs (só roteamento), segurança, erros, OpenAPI
tests/
  PedidoFulfillment.Testes.Unitarios/   validação dos DTOs
  PedidoFulfillment.Testes.Integracao/  conformidade do contrato, segurança, erros, serialização
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```
