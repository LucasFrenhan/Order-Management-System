# Quickstart: validação do REQ-12

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/publicacao-e-reconciliacao.md](./contracts/publicacao-e-reconciliacao.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- publicação num broker sem filas: as filas dos três consumidores existem antes da primeira
  publicação, e nenhum evento com consumidor é marcado como enviado sem chegar a uma fila;
- fila apagada com o Worker no ar: o evento fica pendente com `SemRota` e sai depois que a
  fila é recriada;
- reconciliação nas três etapas, pedido recente intocado, dois Workers com um efeito só,
  esgotamento do pagamento e predicados de seleção iguais aos do domínio;
- os testes do REQ-06 ao REQ-11 continuam verdes.

## 2. Local: broker vazio com eventos pendentes (US1)

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
```

Com o Worker **parado**, crie um pedido como Cliente (`POST /api/v1/pedidos`). O
`pedido.criado` fica pendente no outbox. Depois apague todas as filas `oms.*`, como num
broker recriado (o volume nomeado `oms-rabbitmq` guarda as filas duráveis, então recriar só o
contêiner não basta):

```powershell
docker compose exec rabbitmq sh -c "rabbitmqctl list_queues -q --no-table-headers name | grep '^oms\.' | xargs -r -n1 rabbitmqctl delete_queue"
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

**Esperado**: em poucos segundos, o pedido segue o fluxo até `ProntoParaEnvio`. No painel
(`http://localhost:15672`), `oms.estoque`, `oms.pagamento` e `oms.entrega` existem. Antes do
REQ-12, esse pedido ficava em `Pendente` para sempre.

## 3. Local: fila apagada com o Worker no ar (US1 cenários 3 e 4)

Com o Worker no ar, apague `oms.estoque` no painel e crie outro pedido.

**Esperado**:
- no log do Worker, `FalhaPublicacao` (5002) com `SemRota` para o `pedido.criado`;
- `oms.estoque` reaparece logo depois, recriada pelo despachante, e o evento sai na
  tentativa seguinte. Ele fica na fila até o consumidor de estoque voltar a ler; reinicie o
  Worker para o consumidor se ligar de novo.

Para conferir o outbox:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "SELECT TOP 5 Tipo, Tentativas, UltimoErro, EnviadoEm FROM EventosSaida ORDER BY Id DESC"
```

## 4. Local: pedido pago parado sem ordem (US2)

Para ciclos rápidos, suba o Worker com:

```powershell
$env:Reconciliacao__IntervaloCicloSegundos = "10"
$env:Reconciliacao__LimiteParadoSegundos = "30"
```

Simule o caso do REQ-10: com o Worker parado, pegue um pedido em `ProntoParaEnvio`, apague a
ordem dele, volte o status para `Processando` e atrase a última atualização:

```powershell
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "OmsDev_2026!Local" -d PedidoFulfillment `
  -Q "DECLARE @id uniqueidentifier = (SELECT TOP 1 Id FROM Pedidos WHERE Status = 'ProntoParaEnvio' ORDER BY CriadoEm DESC); DELETE FROM OrdensEntrega WHERE PedidoId = @id; UPDATE Pedidos SET Status = 'Processando', AtualizadoEm = DATEADD(hour, -1, SYSUTCDATETIME()) WHERE Id = @id"
```

Suba o Worker.

**Esperado**:
- no primeiro ciclo, o log `PedidoRecuperado` (9001), com a etapa `Entrega`, e o resumo
  `ResumoReconciliacao` (9004);
- o pedido volta a `ProntoParaEnvio`, com uma ordem nova e `entrega.criada` no outbox;
- um pedido criado agora não é tocado pela reconciliação.

## 5. Local: esgotamento do pagamento (US2 cenário 9)

O gateway simulado não falha por configuração. O esgotamento é validado pelo teste de
integração com um gateway que sempre falha (seção 1). No banco, o resultado esperado é:
pagamento `NaoProcessado`, estoque devolvido, pedido `Cancelado` com o motivo
`Não foi possível processar o pagamento.` e o log `PedidoEsgotado` (9002).
