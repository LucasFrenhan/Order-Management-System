# Contrato de webhook (REQ-18)

O que o receptor recebe e como deve tratar. Formato do
[Standard Webhooks](https://www.standardwebhooks.com/) v1, para o receptor usar uma biblioteca
pronta.

## Requisição

```http
POST {url do destino}
Content-Type: application/json
User-Agent: OMS-Webhooks/1
webhook-id: 0193f0a4-1d2c-7e3f-8a9b-0c1d2e3f4a5b
webhook-timestamp: 1793541607
webhook-signature: v1,K5oZfzN95Z9UVu1EsfQmfVNQhnkZ2pj9o9NDN/H/pI4=

{"idNotificacao":"0193f0a4-…","tipo":"pedido.status-alterado","versao":1,"ocorridoEm":"2026-11-01T14:00:07Z","dados":{…}}
```

| Cabeçalho | Conteúdo |
|-----------|----------|
| `webhook-id` | `idNotificacao`; igual em todas as tentativas da mesma entrega |
| `webhook-timestamp` | segundos Unix do envio (muda a cada tentativa) |
| `webhook-signature` | `v1,` + Base64 de HMAC-SHA256 de `{webhook-id}.{webhook-timestamp}.{corpo}`, com a chave = bytes do segredo sem o prefixo `whsec_` (decodificado de Base64) |

O corpo segue [`notificacoes/notificacao.v1.schema.json`](./notificacoes/notificacao.v1.schema.json)
e é assinado exatamente como enviado. Não há `traceparent`, token nem dado pessoal.

## Resposta esperada

| Resposta do receptor | O OMS faz |
|----------------------|-----------|
| `2xx` em até 10 s | `Entregue`; zera as falhas seguidas do destino |
| `410 Gone` | desativa o destino na hora (motivo `410 Gone`) |
| Qualquer outra, `3xx` (não seguido), sem resposta em 10 s, erro de conexão ou endereço interno | falha; nova tentativa em 30 s, 2 min, 10 min, 30 min, 1 h e depois a cada 2 h |
| Falhas por 24 h desde a primeira tentativa | `Esgotada`; desativa o destino (motivo `Esgotada`) |

O corpo da resposta é ignorado e não é gravado.

## O que o receptor deve fazer

1. Conferir a assinatura com o segredo recebido no cadastro e recusar `webhook-timestamp` a
   mais de 5 minutos do relógio (replay).
2. Deduplicar por `webhook-id`: a entrega é **pelo menos uma vez**.
3. Ignorar o aviso de `pedido.status-alterado` com `sequencia` menor ou igual à última já
   tratada do mesmo pedido: **não há garantia de ordem**.
4. Responder `2xx` rápido e processar depois; confirmar o estado com
   `GET /api/v1/pedidos/{pedidoId}` usando o próprio token.

## Quem recebe

| Destino | Recebe |
|---------|--------|
| De um Cliente | avisos dos pedidos desse Cliente, gravados depois do cadastro |
| Global (Admin) | avisos de todos os pedidos, gravados depois do cadastro |
| Inativo | nada, até a reativação; avisos do período inativo não são recuperados |
