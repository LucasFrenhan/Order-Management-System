# Quickstart: validação do REQ-18

**Spec**: [spec.md](./spec.md) | **Contratos**: [api-notificacoes.md](./contracts/api-notificacoes.md), [webhooks.md](./contracts/webhooks.md) | **Modelo**: [data-model.md](./data-model.md)

## 1. Testes

```powershell
$env:OMS_EXIGIR_DOCKER = "1"
dotnet test PedidoFulfillment.sln
dotnet format --verify-no-changes
```

**Esperado**: tudo verde, com SQL Server e RabbitMQ reais (Testcontainers):
- um `pedido.status-alterado` por mudança, em cada caminho do research R-01, com a sequência e
  a origem certas; nenhum na criação nem na correção do rastreio;
- um aviso `pedido.estorno-confirmado` por estorno confirmado;
- a leitura dos avisos sem pular o de uma transação lenta (research R-13);
- webhooks com receptor de teste: `2xx`, `500` com nova tentativa, lento (10 s), `410`,
  redirecionamento, esgotamento, assinatura válida, endereço interno recusado;
- SSE: filtro por Cliente e por pedido, `conectado`, reconexão com `Last-Event-ID`,
  `sincronizacao-necessaria`, `429`, broker parado;
- rotas de destinos com `401`, `403` e `404`, inclusive o destino global gerido por outro Admin;
- `ETag` com `304`;
- os schemas `pedido.status-alterado.v1` e `notificacao.v1`;
- os testes do REQ-01 ao REQ-17 continuam verdes, e o contrato OpenAPI confere com o gerado.

## 2. Local

```powershell
docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http      # terminal 1
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # terminal 2
```

Em Development, `Notificacoes:ChaveCifra` vem do `appsettings.Development.json` e
`Notificacoes:PermitirRedeLocal` é `true`, para o receptor local da seção 3.

Pegue um token de Cliente e um de Admin em `POST /api/v1/auth/login`, como nos REQs
anteriores.

## 3. Webhook (US2)

1. Suba um receptor que mostre o que chega, por exemplo `npx http-echo-server 5099` (ou
   qualquer servidor que responda `200` e imprima cabeçalhos e corpo).
2. Como Cliente: `POST /api/v1/notificacoes/destinos` com
   `{ "url": "http://localhost:5099/oms" }`. **Esperado**: `201` com `segredo`; um
   `GET /api/v1/notificacoes/destinos` em seguida não mostra o segredo.
3. Crie um pedido como o mesmo Cliente. **Esperado**: em até 10 s, o receptor recebe
   `Pendente → Processando` e depois `Processando → ProntoParaEnvio`, cada um com
   `webhook-id`, `webhook-timestamp` e `webhook-signature`. Confira a assinatura com uma
   biblioteca do Standard Webhooks e o segredo do passo 2.
4. Pare o receptor e mude o status de um pedido (por exemplo, despache com rastreio como
   operação). **Esperado**: `GET /api/v1/notificacoes/destinos/{id}/entregas` mostra a entrega
   `Pendente` com `tentativas` subindo e `proximaTentativaEm` espaçando; suba o receptor de novo
   e a entrega vira `Entregue` na tentativa seguinte.
5. Faça o receptor responder `410`. **Esperado**: o destino fica `ativo: false` com o motivo;
   `POST /api/v1/notificacoes/destinos/{id}/reativacao` o reativa.
6. Tente cadastrar `https://127.0.0.1/x` com `PermitirRedeLocal` = `false`. **Esperado**: `400`
   `NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO`.

## 4. SSE (US3)

```powershell
curl.exe -N -H "Authorization: Bearer $tokenCliente" http://localhost:5230/api/v1/notificacoes/eventos
```

**Esperado**:
- o primeiro evento é `conectado`, com um `id`;
- ao criar um pedido do mesmo Cliente, chegam os avisos em até 12 s; os pedidos de outro
  Cliente não aparecem;
- a cada 30 s sem aviso, um `sinal`;
- interrompa o `curl`, mude um pedido e reconecte com `-H "Last-Event-ID: <último id>"`:
  o aviso perdido chega antes dos novos;
- reconecte com `-H "Last-Event-ID: 00000000-0000-0000-0000-000000000001"`: chega
  `sincronizacao-necessaria`;
- abra 4 conexões com o mesmo token: a quarta recebe `429`;
- com `?pedidoId=` de um pedido de outro Cliente: `404`;
- com `docker compose stop rabbitmq`, mude um pedido pela API: o aviso continua chegando pelo
  SSE e pelo webhook. Suba o RabbitMQ no fim.

## 5. `ETag` (US5)

```powershell
curl.exe -i -H "Authorization: Bearer $tokenCliente" http://localhost:5230/api/v1/pedidos/{id}
curl.exe -i -H "Authorization: Bearer $tokenCliente" -H 'If-None-Match: "<etag>"' http://localhost:5230/api/v1/pedidos/{id}
```

**Esperado**: a primeira devolve `200` com `ETag`; a segunda, `304` sem corpo. Depois de uma
mudança no pedido, a segunda volta a dar `200` com outro `ETag`.

## 6. Observabilidade

Com o Aspire Dashboard do REQ-16 (`http://localhost:18888`): logs 10001 a 10014, métricas
`oms.notificacoes.*`, e o span `notificacao enviar` ligado ao trace do pedido.
