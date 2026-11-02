# Contrato HTTP: notificações (REQ-18)

Acréscimos ao contrato v1 (`specs/001-contratos-api/contracts/openapi-v1.yaml`), aplicados na
implementação. Nenhuma rota existente muda; o `GET /api/v1/pedidos/{id}` só ganha o cabeçalho
`ETag` e a resposta `304`. Erros em `ProblemDetails` (RFC 7807), como as demais rotas.

## Destinos de webhook

Política `PoliticaDestinosNotificacao`: perfil Cliente ou Admin. OperadorLogistica: `403`. Sem
token: `401`. O usuário com perfil de operação (Admin) gerencia os destinos **globais**, de
qualquer Admin; o Cliente, só os próprios. Quem tem os dois perfis cadastra e gerencia como
Admin (destinos globais). Destino fora do escopo: `404`.

### `POST /api/v1/notificacoes/destinos`

```json
{ "url": "https://loja.exemplo.com/oms/webhooks" }
```

`201` com `Location: /api/v1/notificacoes/destinos/{id}`:

```json
{
  "id": "0193…",
  "url": "https://loja.exemplo.com/oms/webhooks",
  "global": false,
  "ativo": true,
  "motivoDesativacao": null,
  "desativadoEm": null,
  "falhasSeguidas": 0,
  "criadoEm": "2026-11-01T14:00:00Z",
  "segredo": "whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw"
}
```

`segredo` aparece **só** nesta resposta (FR-009).

| Situação | Resposta | Código |
|----------|----------|--------|
| URL ausente, relativa, sem `https`, com usuário/senha ou acima de 2048 caracteres | `400` (erros de validação por campo) | — |
| Host não resolve ou resolve para endereço interno | `400` "O endereço do destino não é público." | `NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO` |
| Limite de destinos atingido | `409` "O limite de 5 destinos foi atingido." | `NOTIFICACAO_DESTINO_LIMITE` |

### `GET /api/v1/notificacoes/destinos`

`200` com a lista do escopo do usuário (o mesmo objeto, sem `segredo`), ordenada por `criadoEm`.

### `DELETE /api/v1/notificacoes/destinos/{id}`

`204`. As entregas pendentes viram `Descartada`; nada mais é enviado. `404` fora do escopo.

### `POST /api/v1/notificacoes/destinos/{id}/reativacao`

`200` com o destino ativo e `falhasSeguidas` = 0. Destino já ativo: `200` sem mudança
(idempotente). Entregas esgotadas não são reenviadas. `404` fora do escopo.

### `GET /api/v1/notificacoes/destinos/{id}/entregas?limite=50`

`200` com as últimas entregas (padrão 50, máximo 200), da mais nova para a mais antiga:

```json
[
  {
    "idNotificacao": "0193…",
    "tipo": "pedido.status-alterado",
    "pedidoId": "0193…",
    "situacao": "Pendente",
    "tentativas": 2,
    "ultimoCodigoHttp": 503,
    "ultimoErro": null,
    "criadaEm": "2026-11-01T14:05:01Z",
    "proximaTentativaEm": "2026-11-01T14:07:31Z",
    "finalizadaEm": null
  }
]
```

`limite` fora de 1–200: `400`. `404` fora do escopo.

## Eventos em tempo real (SSE)

### `GET /api/v1/notificacoes/eventos[?pedidoId={id}]`

Política de consulta de pedido (todos os perfis). Resposta `200`, `Content-Type:
text/event-stream`, `Cache-Control: no-cache`. O Cliente recebe só os próprios pedidos; Admin
e OperadorLogistica, todos (FR-020).

| Situação | Resposta |
|----------|----------|
| Sem token ou token inválido | `401` |
| `pedidoId` inválido | `400` |
| `pedidoId` inexistente ou de outro Cliente | `404`, como o `GET` do pedido |
| Mais de 3 conexões do usuário nesta instância | `429` "O limite de conexões de eventos foi atingido." (`NOTIFICACAO_SSE_LIMITE_CONEXOES`) |
| `Last-Event-ID` que não é um GUID | tratado como desconhecido: `sincronizacao-necessaria` |

Eventos do stream:

| `event` | `id` | `data` | Quando |
|---------|------|--------|--------|
| `conectado` | último aviso confirmado (ou o GUID zero, tabela vazia) | `{"instante":"…"}` | primeiro evento de uma conexão sem `Last-Event-ID` (FR-021a) |
| `pedido.status-alterado` | `idNotificacao` | corpo `notificacao.v1` | cada mudança visível ao usuário |
| `pedido.estorno-confirmado` | `idNotificacao` | corpo `notificacao.v1` | cada estorno confirmado visível ao usuário |
| `sincronizacao-necessaria` | — | `{"motivo":"AvisoDesconhecido" \| "LimiteReenvio" \| "ClienteLento"}` | o reenvio não é possível; a aplicação confirma pelo `GET` |
| `sinal` | — | `{}` | a cada 30 s sem outro evento (FR-022) |

Exemplo:

```text
event: conectado
id: 0193f0a2-6c1e-7b5a-9d3e-2f4b8c1a0e55
data: {"instante":"2026-11-01T14:00:00Z"}

event: pedido.status-alterado
id: 0193f0a4-1d2c-7e3f-8a9b-0c1d2e3f4a5b
data: {"idNotificacao":"0193f0a4-1d2c-7e3f-8a9b-0c1d2e3f4a5b","tipo":"pedido.status-alterado","versao":1,"ocorridoEm":"2026-11-01T14:00:07Z","dados":{"pedidoId":"0193…","numero":"PED-20261101-000042","statusAnterior":"Pendente","statusNovo":"Processando","sequencia":2,"link":"/api/v1/pedidos/0193…"}}
```

**Reconexão**: o cliente envia `Last-Event-ID` com o último `id` recebido (o `EventSource` faz
isso sozinho). A API reenvia até 500 avisos perdidos, em ordem de gravação, antes dos novos.
O `id` de `conectado` também serve de `Last-Event-ID`.

**Encerramento**: a API fecha a conexão no `exp` do token, no desligamento e quando a fila da
conexão enche (depois de `sincronizacao-necessaria` com `ClienteLento`).

## `ETag` no `GET /api/v1/pedidos/{id}`

- `200` ganha `ETag: "<versão>"` (forte, Base64Url do `rowversion` do pedido).
- Com `If-None-Match` igual à versão atual: `304`, sem corpo, com o mesmo `ETag`.
- Com `If-None-Match` diferente, ausente ou malformado: `200` normal.
- A autorização vem antes: pedido de outro Cliente é `404` com ou sem `If-None-Match`.

## Códigos de erro novos (`CodigosErro`)

| Código | Rota |
|--------|------|
| `NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO` | `POST /destinos` |
| `NOTIFICACAO_DESTINO_LIMITE` | `POST /destinos` |
| `NOTIFICACAO_SSE_LIMITE_CONEXOES` | `GET /eventos` |
