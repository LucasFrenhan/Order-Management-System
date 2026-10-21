# Quickstart: validação do REQ-01

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/openapi-v1.yaml](./contracts/openapi-v1.yaml)

Guia para provar que o contrato v1 está publicado e conforme. Não contém implementação.

## Pré-requisitos

- SDK do .NET 10 (`dotnet --list-sdks` deve listar `10.x`). Instalação:
  `winget install Microsoft.DotNet.SDK.10`.
- Não precisa de banco nem do RabbitMQ neste REQ.

## 1. Build e testes

```powershell
dotnet build PedidoFulfillment.sln
dotnet test PedidoFulfillment.sln
```

**Esperado**: build sem avisos tratados como erro e todos os testes verdes. Isso inclui:

| Suíte | O que prova | Requisitos |
|-------|-------------|------------|
| Conformidade do contrato | `/openapi/v1.json` gerado é equivalente a `contracts/openapi-v1.yaml` (operações, códigos de resposta, segurança, schemas, enums) | FR-001–FR-005, FR-024, SC-001, SC-005 |
| Segurança dos endpoints | Cada rota protegida devolve 401 sem token, com token expirado e com assinatura inválida; 403 com perfil não autorizado; register/login sem token não devolvem 401 | FR-006, FR-007, SC-002 |
| Formato de erro | 400 de validação traz `erros`; JSON mal formado e cabeçalho/ID sem formato de UUID dão 400; 401/403/404/500 têm `type`, `title`, `status`, `instance` e `traceId`; 500 não traz stack trace | FR-009, FR-010, SC-003 |
| Serialização de DTOs | Enums como texto, camelCase, datas ISO 8601 UTC, campos desconhecidos ignorados, senha ausente nas respostas | FR-008, FR-015, FR-019, FR-021 |
| Validação de DTOs (unitários) | Regras de cada DTO conforme [data-model.md](./data-model.md) | FR-012–FR-020 |

## 2. Navegar no contrato (User Story 1)

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API
```

1. Abra `https://localhost:<porta>/scalar/v1`.
   **Esperado**: 5 operações agrupadas em *Autenticacao* e *Pedidos*, com exemplos, perfis
   autorizados por operação, regras de idempotência e tabela de transições de status.
2. Abra `https://localhost:<porta>/openapi/v1.yaml`.
   **Esperado**: conteúdo idêntico a `contracts/openapi-v1.yaml`.
3. Abra `https://localhost:<porta>/openapi/v1.json` (documento gerado pelo código).
   **Esperado**: OpenAPI 3.1 com `bearerAuth` e os schemas `CriarPedidoRequestDTO`,
   `PedidoResponseDTO`, `ItemPedidoDTO`, `LoginResponseDTO`.
4. Na UI, chame `POST /api/v1/pedidos` sem token.
   **Esperado**: 401 `application/problem+json` com `traceId`.
5. Chame `POST /api/v1/auth/register` com corpo `{}`.
   **Esperado**: 400 com `erros.nome`, `erros.email` e `erros.senha`.
6. Chame `POST /api/v1/auth/register` com corpo válido.
   **Esperado neste REQ**: 501 `ProblemDetails` (implementação provisória, ver
   [research.md R-05](./research.md)). Passa a 201 no REQ-03.

## 3. Ambiente de produção

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
dotnet run --project src/PedidoFulfillment.API
```

**Esperado**: `/openapi/v1.yaml`, `/openapi/v1.json` e `/scalar/v1` devolvem 404 (R-04).

## 4. Revisão de PR (constituição)

- Toda mudança no contrato v1 é só aditiva e opcional (SC-006).
- Commits no formato `tipo(escopo): descrição [REQ-01]`.
- Pacotes NuGet novos constam da tabela de justificativas em [plan.md](./plan.md).
