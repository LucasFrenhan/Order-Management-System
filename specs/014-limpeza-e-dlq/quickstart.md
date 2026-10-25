# Quickstart: validação do REQ-14

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/limpeza-e-reenvio.md](./contracts/limpeza-e-reenvio.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- limpeza: só o que passou da retenção é apagado; pendentes, chaves com menos de 24 h e pedidos
  de reenvio em aberto ficam; lotes e duas instâncias;
- rotas de Admin: 202 e registro com autor, 401, 403, 400 e 404; a rota funciona com o broker
  fora;
- execução do reenvio: filtros, ordem preservada, limite, retomada e queda do broker no meio;
- aviso da DLQ; permissões do usuário da API no broker;
- os testes do REQ-01 ao REQ-13 continuam verdes.

## 2. Local: ambiente com os dois usuários do broker

```powershell
docker compose up -d --wait
docker compose exec rabbitmq rabbitmqctl list_users
docker compose exec rabbitmq rabbitmqctl list_permissions -p /
```

**Esperado**: `oms` (Worker) e `oms-api` (API), com `oms-api` em `^$ ^$ ^$`. Num ambiente
local antigo, o usuário novo aparece na primeira subida, sem recriar o volume. A prontidão
da API (`http://localhost:5230/health/pronto`) continua mostrando o broker saudável.

## 3. Local: reenvio da DLQ (US2)

Com a API e o Worker locais (como no quickstart do REQ-13), ponha uma mensagem na DLQ de
`estoque`, por exemplo publicando pelo painel em `oms.estoque` o corpo `{}` (vai direto para
`oms.estoque.dlq`, com o motivo "Envelope sem tipo."). Como Admin:

```http
POST /api/v1/admin/dlq/estoque/reenvios
Authorization: Bearer <token do admin>
Content-Type: application/json

{ "trechoMotivo": "sem tipo" }
```

**Esperado**:
- 202 com o pedido `Pendente` e o `Location`; log 4006 na API;
- em até 1 minuto, no Worker, log 5009 com as contagens; o `GET` do `Location` mostra
  `Concluido`;
- a mensagem volta à DLQ (o corpo continua fora do contrato) com `x-reenvios = 1`, e o pedido
  conta 1 reenviada, mesmo que ela volte durante a execução; depois de 3 pedidos, ela passa a
  ser contada como bloqueada pelo limite.

Com o OperadorLogistica, as duas rotas respondem 403.

## 4. Local: aviso e limpeza

- Com mais de 10 mensagens em `oms.estoque.dlq`, o Worker registra o 5011 em até 5 minutos.
- Para ver a limpeza sem esperar 7 dias, suba o Worker com
  `$env:Limpeza__RetencaoEnviadosHoras = "1"` e `$env:Limpeza__IntervaloSegundos = "60"`: os
  eventos enviados há mais de 1 h somem, e o log 5013 mostra as contagens.
