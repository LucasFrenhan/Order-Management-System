# Contrato: endereços de saúde (REQ-02)

Endereços operacionais, públicos (sem token), **fora do contrato de negócio v1** e do
documento OpenAPI. Respostas em `application/json`.

## `GET /health` — vida (FR-015)

Não consulta dependências.

| Status | Corpo |
|--------|-------|
| 200 | `{"status":"Healthy"}` |

## `GET /health/pronto` — prontidão (FR-016, FR-018)

Verifica banco e broker, cada um com limite de 5 segundos.

| Status | Quando |
|--------|--------|
| 200 | todas as dependências `Healthy` |
| 503 | alguma dependência `Unhealthy` |

Corpo:

```json
{
  "status": "Unhealthy",
  "dependencias": {
    "sqlserver": "Healthy",
    "rabbitmq": "Unhealthy"
  }
}
```

Regras:

- `status` ∈ `Healthy`, `Unhealthy`. `dependencias` sempre contém `sqlserver` e `rabbitmq`.
- O corpo **nunca** contém strings de conexão, hosts, usuários, senhas, mensagens de exceção
  nem rastreamento de pilha.
- Para acrescentar dependências (por exemplo, a outbox no REQ-06), basta adicionar chaves,
  o que é uma mudança compatível.
