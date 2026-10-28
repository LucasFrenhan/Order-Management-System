# Quickstart: recuperação dos consumidores depois de o broker reiniciar

**Requisito**: REQ-17 | **Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/recuperacao-consumo.md](./contracts/recuperacao-consumo.md)

## Pré-requisitos

- Docker em execução.
- Na raiz do repositório, com o `.env` do ambiente de demonstração (veja `.env.example`).

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Recuperacao|FullyQualifiedName~SupervisorInscricao|FullyQualifiedName~VerificacaoConsumidores"
dotnet test
dotnet format --verify-no-changes
```

Esperado: tudo verde. Os testes de reinício do broker levam até ~1 min cada.

## 2. Reprodução da issue #41, agora sem reiniciar o Worker

1. `docker compose --profile demo up -d --wait --build` (o `--wait` agora também espera os três
   consumidores inscritos, pelo healthcheck do Worker).
2. `docker compose exec rabbitmq rabbitmqctl list_queues name messages consumers`: 1 consumidor
   em `oms.estoque`, `oms.pagamento` e `oms.entrega`.
3. `curl -s localhost:5231/health/pronto` (porta `OMS_WORKER_PORTA`): `Healthy`, com
   `"consumidores": "Healthy"`.
4. `docker compose stop rabbitmq`, esperar ~10 s.
5. `curl -s localhost:5231/health/pronto`: 503, com `"consumidores": "Unhealthy"`;
   `docker compose ps worker` mostra `unhealthy` depois de algumas verificações, e nada reinicia.
6. `docker compose start rabbitmq`, esperar até 1 min.
7. Repetir o passo 2: **1 consumidor** em cada fila principal (antes do REQ-17: 0).
8. Repetir o passo 3: `Healthy`.
9. Criar um pedido ([fluxo 02](../../docs/fluxos/02-criacao-de-pedido.md)) e acompanhar até
   `ProntoParaEnvio`, com a ordem de entrega criada
   ([fluxo 06](../../docs/fluxos/06-ordem-de-entrega.md)): segue sem
   `docker compose restart worker`.
10. `docker compose logs worker | grep -E "5016|5017|5018|5019"`: a perda de cada consumidor, as
    tentativas e a volta, sem credenciais.

## 3. Fila apagada com o Worker no ar

1. `docker compose exec rabbitmq rabbitmqctl delete_queue oms.estoque`
2. Em até 1 min, `list_queues`: `oms.estoque` existe de novo, com 1 consumidor; as outras não
   mudaram.

## 4. Conexão fechada à força

1. `docker compose exec rabbitmq rabbitmqctl close_all_connections "teste REQ-17"`
2. Em até 1 min, os três consumidores estão de volta, e o log tem 5019 e 5018.

## 5. Métricas (perfil com o Aspire Dashboard, REQ-16)

Com a telemetria ligada, no painel: `oms.mensageria.consumidor.inscrito` cai para 0 nos três
consumidores durante o passo 4 da seção 2 e volta a 1; `oms.mensageria.consumidor.reinscricoes`
soma 1 para cada um.
