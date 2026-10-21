# Quickstart: validação do REQ-02

**Spec**: [spec.md](./spec.md) | **Saúde**: [contracts/saude.md](./contracts/saude.md)

## Pré-requisitos

- SDK do .NET 10.
- Docker com Compose v2 (`docker compose version`). Instalação no Windows:
  `winget install Docker.DockerDesktop`.

## 1. Testes (sem Docker)

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde. Os testes com Testcontainers aparecem como ignorados ("Docker
indisponível") se o Docker não estiver rodando. Inclui o teste de arquitetura (FR-003) e o
503 de prontidão com dependências fora do ar.

## 2. Subir as dependências (User Story 1)

```powershell
docker compose up -d --wait
docker compose ps
```

**Esperado**: o comando retorna só quando `sqlserver` e `rabbitmq` estão `healthy`, em
menos de 2 minutos com as imagens já baixadas (SC-002).

- Painel do broker: `http://localhost:15672`, com usuário `oms` e senha `oms-dev-local`.
- Persistência: `docker compose down` seguido de `docker compose up -d --wait` mantém os
  dados.
- Limpeza: `docker compose down -v` apaga os volumes.
- Porta ocupada: copie `.env.example` para `.env`, troque `OMS_SQL_PORTA` e sobrescreva
  `ConnectionStrings__SqlServer` ao rodar a API.

## 3. API com o ambiente local (User Story 2)

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API
```

| Requisição | Esperado |
|------------|----------|
| `GET /health` | 200 `{"status":"Healthy"}` |
| `GET /health/pronto` | 200, `sqlserver` e `rabbitmq` `Healthy` (SC-006) |
| `docker compose stop rabbitmq` e `GET /health/pronto` | 503, `rabbitmq` `Unhealthy`, em até ~5 s, sem dados de conexão |
| `GET /openapi/v1.json` | nenhum caminho `/health` |

## 4. Configuração ausente fora de Development

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Jwt__ChaveAssinatura = "chave-local-de-teste-com-mais-de-32-bytes"
dotnet run --project src/PedidoFulfillment.API
```

**Esperado**: a API não inicia, e a mensagem nomeia `ConnectionStrings:SqlServer` e
`RabbitMQ:*` sem mostrar valores (FR-012).
