# Contrato: rastreio e histórico no contrato HTTP v1 (REQ-11)

Evolução compatível de [openapi-v1.yaml](../../001-contratos-api/contracts/openapi-v1.yaml)
(research R-01). Tudo o que não aparece aqui continua como está. O arquivo do REQ-01 é a
fonte da verdade e recebe estas mudanças na implementação.

## `PATCH /api/v1/pedidos/{id}/rastreio` (novo)

| Item | Valor |
|------|-------|
| `operationId` | `RegistrarRastreioPedido` |
| Perfis | Admin, OperadorLogistica (Cliente: 403) |
| Corpo | `RegistrarRastreioRequestDTO` |
| 200 | `PedidoResponseDTO` (despachado, corrigido ou sem mudança) |
| 400 | identificador ou corpo inválido (`ErroValidacao`) |
| 401 / 403 | sem token / perfil sem permissão |
| 404 | pedido inexistente |
| 409 | `PEDIDO_TRANSICAO_INVALIDA` (status diferente de `ProntoParaEnvio` e `Despachado`, ou conflitos persistentes) ou `PEDIDO_RASTREIO_DUPLICADO` |

| Status atual | Resultado |
|--------------|-----------|
| `ProntoParaEnvio` | ordem criada se faltar; transportadora, código e data de despacho gravados; `Despachado`; histórico `ProntoParaEnvio → Despachado`, motivo `Pedido despachado.` |
| `Despachado`, dados iguais | nada muda |
| `Despachado`, dados diferentes | transportadora e código corrigidos; status, data de despacho e histórico mantidos |
| `Pendente`, `Processando`, `Cancelado`, `PagamentoRejeitado` | 409 `PEDIDO_TRANSICAO_INVALIDA` |

Duplicidade: outro pedido com a mesma transportadora e o mesmo código, sem diferenciar
maiúsculas e minúsculas, depois de remover os espaços nas pontas.

## Schemas

```yaml
RegistrarRastreioRequestDTO:
  type: object
  required: [transportadora, codigoRastreio]
  properties:
    transportadora:
      type: string
      minLength: 1
      maxLength: 100
    codigoRastreio:
      type: string
      minLength: 1
      maxLength: 50

HistoricoStatusPedidoResponseDTO:
  type: object
  required: [statusNovo, ocorridoEm]
  properties:
    statusAnterior:
      oneOf: [{ $ref: '#/components/schemas/StatusPedido' }, { type: 'null' }]
      description: Ausente (null) na criação do pedido.
    statusNovo:
      $ref: '#/components/schemas/StatusPedido'
    ocorridoEm:
      type: string
      format: date-time
    motivo:
      type: [string, 'null']

EntregaPedidoResponseDTO:
  type: object
  required: [criadoEm]
  properties:
    transportadora:
      type: [string, 'null']
    codigoRastreio:
      type: [string, 'null']
    despachadoEm:
      type: [string, 'null']
      format: date-time
    criadoEm:
      type: string
      format: date-time
```

`PedidoResponseDTO` ganha, como propriedades opcionais no schema (a v1 só aceita adições opcionais; a API sempre devolve `historico`):

```yaml
historico:
  type: array
  description: Mudanças de status em ordem cronológica, sem o autor.
  items:
    $ref: '#/components/schemas/HistoricoStatusPedidoResponseDTO'
entrega:
  oneOf: [{ $ref: '#/components/schemas/EntregaPedidoResponseDTO' }, { type: 'null' }]
  description: Ordem de entrega do pedido; null quando ainda não existe.
```

A forma exata (`oneOf` com `null` ou `type` com `null`) segue o que o gerador de OpenAPI do
projeto produz, para o teste de conformidade passar; o significado é o descrito aqui.

## Exemplo de resposta (pedido despachado)

```json
{
  "id": "0f8fad5b-d9cb-469f-a165-70867728950e",
  "numero": "PED-20261021-000123",
  "status": "Despachado",
  "historico": [
    { "statusAnterior": null, "statusNovo": "Pendente", "ocorridoEm": "2026-10-21T15:30:00Z", "motivo": null },
    { "statusAnterior": "Pendente", "statusNovo": "Processando", "ocorridoEm": "2026-10-21T15:30:01Z", "motivo": "Pagamento aprovado." },
    { "statusAnterior": "Processando", "statusNovo": "ProntoParaEnvio", "ocorridoEm": "2026-10-21T15:30:01Z", "motivo": "Ordem de entrega criada." },
    { "statusAnterior": "ProntoParaEnvio", "statusNovo": "Despachado", "ocorridoEm": "2026-10-21T16:10:00Z", "motivo": "Pedido despachado." }
  ],
  "entrega": {
    "transportadora": "Correios",
    "codigoRastreio": "BR123456789BR",
    "despachadoEm": "2026-10-21T16:10:00Z",
    "criadoEm": "2026-10-21T15:30:01Z"
  }
}
```

(Campos atuais do pedido omitidos no exemplo.)

## Compatibilidade

- Nenhuma operação, parâmetro ou campo existente muda; `PATCH /status` aceita
  `ProntoParaEnvio → Despachado` como antes (FR-009b).
- Clientes que ignoram campos desconhecidos continuam funcionando (SC-005).
- O workflow `contrato-compatibilidade` (oasdiff) roda no PR, porque o `.yaml` muda.
