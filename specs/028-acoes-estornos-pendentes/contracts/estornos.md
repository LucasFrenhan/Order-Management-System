# Contrato: estornos pela operação (REQ-28)

[← Plano](../plan.md)

Acréscimo ao contrato v1 (`specs/001-contratos-api/contracts/openapi-v1.yaml`). Nada do que existe
muda. Decisões no [research](../research.md).

## Rotas

| Método e rota | Perfis | Sucesso | Erros |
|---------------|--------|---------|-------|
| `GET /api/v1/estornos/pendentes` | Admin, OperadorLogistica | 200 `PaginaEstornosResponseDTO` | 400, 401, 403 |
| `GET /api/v1/estornos/{id}` | Admin, OperadorLogistica | 200 `EstornoResponseDTO` | 400, 401, 403, 404 |
| `POST /api/v1/estornos/{id}/tentativas` | Admin, OperadorLogistica | 202 `EstornoResponseDTO` + `Location: /api/v1/estornos/{id}` | 400, 401, 403, 404, 409 `ESTORNO_NAO_PENDENTE` |
| `POST /api/v1/estornos/{id}/baixa` | Admin ativo no banco | 200 `EstornoResponseDTO` | 400, 401, 403, 404, 409 `ESTORNO_NAO_PENDENTE` ou `ESTORNO_RODADA_EM_ANDAMENTO` |

Todas passam pelo limite de requisições e respondem erros em `ProblemDetails`, como as demais.

## Exemplos

### Listagem

```http
GET /api/v1/estornos/pendentes?tamanho=2
Authorization: Bearer <token do OperadorLogistica>
```

```http
HTTP/1.1 200 OK

{ "itens": [
    { "estornoId": "0199f1a1-…", "pedidoId": "0199f1a2-…", "numero": "PED-20261024-000001",
      "valor": 149.70, "situacao": "Pendente", "solicitadoEm": "2026-10-24T15:20:00+00:00",
      "tentativas": 4, "ultimaFalhaEm": "2026-10-25T16:02:00+00:00",
      "ultimoErro": "Recusado: Cobrança desconhecida.", "alemDoLimiteAlerta": true,
      "atrasadoEm": "2026-10-25T16:02:00+00:00", "tentativaForcadaEm": null,
      "confirmadoEm": null, "baixa": null } ],
  "tamanho": 2, "proximo": "eyJ…", "anterior": null }
```

### Tentativa forçada

```http
POST /api/v1/estornos/0199f1a1-…/tentativas
Authorization: Bearer <token do Admin>
```

```http
HTTP/1.1 202 Accepted
Location: /api/v1/estornos/0199f1a1-…

{ "estornoId": "0199f1a1-…", "situacao": "Pendente", "tentativaForcadaEm": "2026-11-10T14:00:00+00:00", … }
```

Sem corpo na requisição. Repetir o pedido com a marca ainda presente devolve 202 com o mesmo
`tentativaForcadaEm`.

### Baixa

```http
POST /api/v1/estornos/0199f1a1-…/baixa
Authorization: Bearer <token do Admin>
Content-Type: application/json

{ "motivo": "Devolvido por transferência pelo financeiro em 10/11.", "referenciaExterna": "TED-778812" }
```

```http
HTTP/1.1 200 OK

{ "estornoId": "0199f1a1-…", "situacao": "Baixado", "tentativaForcadaEm": null,
  "baixa": { "autorId": "0199e000-…", "baixadoEm": "2026-11-10T14:05:00+00:00",
             "motivo": "Devolvido por transferência pelo financeiro em 10/11.",
             "referenciaExterna": "TED-778812" }, … }
```

### Rodada em andamento

```http
HTTP/1.1 409 Conflict

{ "title": "Rodada de estornos em andamento.",
  "detail": "Rodada de estornos em andamento; tente de novo em instantes.",
  "codigo": "ESTORNO_RODADA_EM_ANDAMENTO" }
```

## Schemas (resumo)

- `EstornoResponseDTO`: obrigatórios `estornoId`, `pedidoId`, `numero`, `valor`, `situacao`
  (`Pendente`, `Confirmado` ou `Baixado`), `solicitadoEm`, `tentativas` e `alemDoLimiteAlerta`;
  anuláveis `ultimaFalhaEm`, `ultimoErro`, `atrasadoEm`, `tentativaForcadaEm`, `confirmadoEm` e
  `baixa`. `alemDoLimiteAlerta` é sempre `false` fora de `Pendente`. Nunca traz o identificador da
  transação ou do estorno no gateway.
- `BaixaEstornoResponseDTO` (`baixa`): `autorId`, `baixadoEm`, `motivo` e `referenciaExterna`
  (anulável).
- `PaginaEstornosResponseDTO`: `itens`, `tamanho`, `proximo` e `anterior`, como em usuários e
  produtos.
- `BaixaEstornoRequestDTO`: `motivo` (obrigatório) e `referenciaExterna` (opcional).

A `situacao` da API é um enum próprio do contrato; a `SituacaoPagamento` interna continua fora dos
contratos (REQ-13).

## Parâmetros e validação

| Campo | Regra | Erro |
|-------|-------|------|
| `tamanho` | 1 a 100, padrão 20 | 400, `erros.tamanho` |
| `cursor` | Opaco, só para quem o recebeu | 400, `erros.cursor` |
| `{id}` | Guid | 400, `erros.id` |
| `motivo` | Obrigatório, 10 a 500 caracteres sem os espaços das pontas | 400, `erros.motivo` |
| `referenciaExterna` | Opcional; se presente, 1 a 100 caracteres sem os espaços das pontas | 400, `erros.referenciaExterna` |

A ordem da listagem é pelo instante do pedido de estorno, do mais antigo para o mais novo, e, no
empate, pelo `estornoId`.

## Códigos de erro novos

| Código | Status | `title` / `detail` | Quando |
|--------|--------|--------------------|--------|
| `ESTORNO_NAO_ENCONTRADO` | 404 | "Estorno não encontrado." / "Nenhum estorno com o identificador informado." | Id desconhecido |
| `ESTORNO_NAO_PENDENTE` | 409 | "Estorno não está pendente." / "O estorno já foi confirmado pelo gateway ou baixado." | Baixa ou tentativa forçada de estorno confirmado ou baixado |
| `ESTORNO_RODADA_EM_ANDAMENTO` | 409 | "Rodada de estornos em andamento." / "Rodada de estornos em andamento; tente de novo em instantes." | A baixa esperou 5 s e a rodada não terminou |

## Eventos novos

- [`pagamento.estorno-atrasado` v1](./eventos/pagamento.estorno-atrasado.v1.schema.json): uma vez por
  estorno, na falha que passa do limite de alerta. Sem aviso ao Cliente.
- [`pagamento.estorno-baixado` v1](./eventos/pagamento.estorno-baixado.v1.schema.json): na baixa.
  Junto sai o aviso `pedido.estorno-confirmado` (webhooks e SSE), igual ao da confirmação pelo
  gateway (contrato do REQ-18, sem mudança).

O `pagamento.estornado` continua saindo só na confirmação pelo gateway, sem mudança.

## Métrica

`oms.estornos.pendentes` (medidor), atributo `oms.alerta` (`true` ou `false`), atualizado no começo
de cada rodada que pega o lock.
