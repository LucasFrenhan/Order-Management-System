# Quickstart: validar a listagem de pedidos (REQ-21)

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/api-listagem-pedidos.md](./contracts/api-listagem-pedidos.md)

## Pré-requisitos

- .NET 10 SDK e Docker (os testes de integração sobem SQL Server e RabbitMQ em Testcontainers).
- Para o roteiro manual: o ambiente local do projeto no ar (`docker compose up -d`) e a API
  rodando, como no roteiro principal do README.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~ListagemPedidos"
dotnet test
dotnet format --verify-no-changes
```

Esperado: todos verdes. Os testes da listagem cobrem os cenários das US1–US4, o empate de
`CriadoEm`, o pedido criado no meio da navegação e a meta de 1 s com 10.000 pedidos (SC-004).

## 2. Roteiro manual

Use um token de Cliente (`$TOKEN_CLIENTE`) com alguns pedidos criados e um token de
OperadorLogistica ou Admin (`$TOKEN_OPERACAO`), obtidos em `POST /api/v1/auth/login`.

| # | Requisição | Esperado |
|---|------------|----------|
| 1 | `GET /api/v1/pedidos` sem token | 401 |
| 2 | `GET /api/v1/pedidos?tamanho=2` com `$TOKEN_CLIENTE` | 200; só os pedidos do Cliente, do mais recente ao mais antigo; `anterior: null` |
| 3 | Repetir com `cursor=<proximo do passo 2>` | 200; os 2 seguintes, sem repetir; `anterior` preenchido |
| 4 | Repetir com `cursor=<anterior do passo 3>` | 200; a mesma página do passo 2 |
| 5 | `GET /api/v1/pedidos?clienteId=<id do próprio Cliente>` com `$TOKEN_CLIENTE` | 403 "O filtro por cliente é exclusivo da operação." |
| 6 | `GET /api/v1/pedidos?status=Pendente&status=Processando` com `$TOKEN_OPERACAO` | 200; pedidos de todos os Clientes nesses status |
| 7 | `GET /api/v1/pedidos?status=ProntoParaEnvio&atualizadoAte=<ontem 23:59:59-03:00>` com `$TOKEN_OPERACAO` | 200; os pedidos parados nesse status desde antes de ontem |
| 8 | `GET /api/v1/pedidos?tamanho=0&status=Enviado&criadoDe=2026-11-03T10:00:00` | 400 com os três erros em `erros` |
| 9 | Passo 3 com o cursor alterado num caractere, ou com `status=Pendente` a mais | 400 `erros.cursor` "O cursor é inválido para esta consulta; recomece da primeira página." |
| 10 | Passo 3 com o cursor do Cliente usado pelo `$TOKEN_OPERACAO` | 400 (cursor de outro usuário) |

## 3. Contrato

- O teste de conformidade do contrato (`tests/PedidoFulfillment.Testes.Integracao/Contrato`)
  confere a rota nova contra `specs/001-contratos-api/contracts/openapi-v1.yaml`.
- No PR, o workflow **Compatibilidade do contrato** (oasdiff) precisa ficar verde: só
  acréscimos.

## 4. Documentação

- `docs/fluxos/03-consulta-e-mudanca-de-status.md` descreve a listagem (exemplo, respostas,
  motivos e limitações), e o índice `docs/fluxos/README.md` cita a rota nova.
