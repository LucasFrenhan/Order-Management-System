# Quickstart: validação do REQ-05

**Spec**: [spec.md](./spec.md) | **Idempotência**: [contracts/idempotencia.md](./contracts/idempotencia.md) |
**Catálogo**: [catálogo inicial do REQ-04](../004-dominio-persistencia/contracts/catalogo-inicial.md)

## Pré-requisitos

- SDK do .NET 10, Docker com Compose v2 e `dotnet tool restore`.
- Ambiente no ar e API em Development:

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http
```

A API aplica a migration `ChavesIdempotencia` ao iniciar.

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**:
- Tudo verde, sem testes ignorados com o Docker no ar.
- O teste de conformidade do contrato v1 passa, e o de modelo sincronizado lista as três
  migrations.

## 2. Tokens

| Perfil | Como obter |
|--------|------------|
| Cliente | `POST /api/v1/auth/register` e depois `POST /api/v1/auth/login` |
| Admin | login com `admin@oms.local` / `AdminDev2026` |
| OperadorLogistica | login com `operador@oms.local` / `OperadorDev2026` |

## 3. Criação (User Story 1)

Endereço base: `http://localhost:5230/api/v1/pedidos`, com o token do Cliente e
`X-Idempotency-Key: <uuid novo>`.

| Corpo | Esperado |
|-------|----------|
| `CAT-001` × 2 e `CAT-002` × 1 | 201; `Location`; `status` `Pendente`; `valorTotal` 829.70; número `PED-<hoje UTC>-NNNNNN` |
| `CAT-009` × 1 (estoque 0) | 422 `PRODUTO_INDISPONIVEL` |
| `CAT-010` × 1 (inativo) | 422 `PRODUTO_INDISPONIVEL` |
| `CAT-008` × 2 (estoque 1) | 422 `PRODUTO_INDISPONIVEL` |
| produto inexistente e `CAT-009` | 422 `PRODUTO_INEXISTENTE`, com os dois no `detail` |
| com token de Admin | 403 |

Os IDs estão na tabela do catálogo (`0192b6a0-0000-7000-8000-00000000000N`).

## 4. Repetição (User Story 2)

1. Reenvie a criação bem-sucedida da §3 com a mesma chave.

   **Esperado**: 201, corpo idêntico e o mesmo `Location`. A contagem de pedidos não muda.
2. Reenvie com a mesma chave e os itens em outra ordem.

   **Esperado**: igual ao passo 1.
3. Reenvie com a mesma chave e outra quantidade.

   **Esperado**: 409 `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`.
4. Use a mesma chave com o token de outro Cliente.

   **Esperado**: 201, com outro pedido.

## 5. Consulta (User Story 3)

`GET` no `Location`:

| Token | Esperado |
|-------|----------|
| dono | 200 |
| outro Cliente | 404, igual ao de um id inexistente |
| Admin ou OperadorLogistica | 200 |

## 6. Status (User Story 4)

`PATCH .../{id}/status` com `{"status": "...", "motivo": "..."}`:

1. Operador: `Processando`, depois `ProntoParaEnvio`, depois `Despachado`. Cada passo
   responde 200.
2. Operador: `Pendente` num pedido despachado.

   **Esperado**: 409 `PEDIDO_TRANSICAO_INVALIDA`, com os dois status no `detail`.
3. Cliente dono de um pedido `Pendente`: `Cancelado` com motivo.

   **Esperado**: 200.
4. Cliente dono de um pedido `Processando`: `Cancelado`.

   **Esperado**: 403.
5. Outro Cliente: qualquer status.

   **Esperado**: 404.
6. Depois dos passos anteriores, faça um `GET`.

   **Esperado**: o status atual e o `atualizadoEm` novo.
7. Repita a criação original da §4.

   **Esperado**: a resposta continua com `Pendente` (resposta original).

## 7. Banco

```powershell
docker compose exec sqlserver sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PedidoFulfillment -Q "SELECT COUNT(*) FROM ChavesIdempotencia; SELECT StatusAnterior, StatusNovo, Motivo FROM HistoricosStatusPedido ORDER BY Id"'
```

**Esperado**:
- Uma chave para cada criação bem-sucedida, e nenhuma para os 422.
- O histórico com as mudanças da §6, com autor e motivo.
