# Quickstart: validação do REQ-06

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/topologia-e-consumo.md](./contracts/topologia-e-consumo.md)

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com os testes de mensageria executados contra o RabbitMQ e o SQL
Server reais (Testcontainers):
- schemas;
- outbox com o broker pausado;
- novas tentativas e DLQ;
- duplicados.

## 2. Local: API e Worker

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1 (aplica a migration MensageriaOutbox)
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

**Esperado**:
- `http://localhost:5231/health/pronto` responde 200;
- no painel do RabbitMQ (`http://localhost:15672`), a troca `oms.eventos` existe.

## 3. Pedido criado vira evento

1. No painel, crie a fila `teste.pedidos` e ligue-a a `oms.eventos` com a chave
   `pedido.criado`.
2. Crie um pedido, como no [quickstart do REQ-05](../005-criacao-pedidos/quickstart.md) §3.

**Esperado**:
- em segundos, `teste.pedidos` recebe a mensagem com `tipo` `pedido.criado`, o
  `idEvento`, o pedido e os itens;
- em `EventosSaida`, a linha está com `EnviadoEm` preenchido.

## 4. Broker fora do ar

```powershell
docker compose stop rabbitmq
```

1. Crie dois pedidos.

   **Esperado**: 201 nos dois; em `EventosSaida`, duas linhas pendentes, com `Tentativas`
   crescendo.
2. Religue o broker:

   ```powershell
   docker compose start rabbitmq
   ```

   **Esperado**: em até 30 s, as duas mensagens chegam a `teste.pedidos`.

## 5. Worker parado

1. Pare o Worker (Ctrl+C) e crie um pedido.

   **Esperado**: 201, e o evento fica pendente.
2. Suba o Worker de novo.

   **Esperado**: o evento é publicado.

## 6. Demonstração

```powershell
docker compose --profile demo up -d --wait --build
docker compose --profile demo ps
```

**Esperado**:
- `api` e `worker` ficam `healthy`;
- `http://localhost:5231/health/pronto` responde 200.

O CI (job "Demonstração") confere o mesmo.
