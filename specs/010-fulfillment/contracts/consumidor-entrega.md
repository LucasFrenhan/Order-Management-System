# Contrato: consumidor de entrega (REQ-10)

Complementa [topologia e consumo](../../006-mensageria/contracts/topologia-e-consumo.md) do
REQ-06. Tudo o que não está aqui (envelope, novas tentativas, DLQ, duplicados) segue aquele
documento.

## Topologia

| Item | Valor |
|------|-------|
| Consumidor | `entrega` |
| Fila | `oms.entrega`, ligada a `oms.eventos` com `pagamento.aprovado` |
| Filas de espera | `oms.entrega.espera.{1000,2000,4000}` |
| DLQ | `oms.entrega.dlq` |
| Consome | `pagamento.aprovado` v1 ([schema](../../006-mensageria/contracts/eventos/pagamento.aprovado.v1.schema.json)), sem mudança |
| Publica | `entrega.criada` v1 ([schema](./eventos/entrega.criada.v1.schema.json)), **novo** |

## Resultado por pedido

| Pedido lido | Ordem de entrega | Status | Publica | Mensagem |
|-------------|------------------|--------|---------|----------|
| Não existe | — | — | — | nova tentativa; DLQ no fim |
| Não apto (status `Pendente`, `Cancelado`, `Despachado` ou `PagamentoRejeitado`; sem pagamento aprovado; sem reserva; ou já tem ordem) | — | sem mudança | nada | ack, com log do motivo |
| `Processando`, apto | criada, sem dados de despacho | `ProntoParaEnvio`, histórico sem autor `Ordem de entrega criada.` | `entrega.criada` | ack |
| `ProntoParaEnvio`, apto | criada, sem dados de despacho | sem mudança | `entrega.criada` | ack |
| Conflito de versão nas 3 reavaliações | — | sem mudança | nada | nova tentativa; DLQ no fim |

Apto: estoque reservado, pagamento `Aprovado`, sem ordem de entrega e em `Processando` ou
`ProntoParaEnvio`. A ordem, o status, o evento e o registro de processado são gravados na
mesma transação. Estoque e pagamento não mudam.

## Exemplo

`entrega.criada`:

```json
{
  "idEvento": "0199f6b2-4a10-7c3e-9b21-5d6e7f8a9b01",
  "tipo": "entrega.criada",
  "versao": 1,
  "ocorridoEm": "2026-10-23T14:02:12.018Z",
  "idCorrelacao": "0199f6b0-9a00-7000-8000-000000000001",
  "dados": {
    "pedidoId": "0199f6b0-9a00-7000-8000-000000000001",
    "numero": "PED-20261023-000042",
    "ordemEntregaId": "0199f6b2-4a0f-7d11-8c3b-1a2b3c4d5e6f",
    "itens": [
      { "produtoId": "0199e000-0000-7000-8000-000000000101", "quantidade": 2 },
      { "produtoId": "0199e000-0000-7000-8000-000000000102", "quantidade": 1 }
    ]
  }
}
```

## Efeito na API

O contrato HTTP v1 não muda. Depois do fulfillment, o pedido aparece em `ProntoParaEnvio`;
a ordem de entrega não aparece na consulta (issue #11). A operação ainda pode cancelá-lo, e o
cancelamento devolve o estoque (REQ-08); a ordem continua registrada, sem despacho.
