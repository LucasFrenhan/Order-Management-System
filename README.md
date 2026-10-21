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
