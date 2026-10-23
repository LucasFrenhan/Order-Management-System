# Contrato: consumidor de pagamento (REQ-09)

Complementa [topologia e consumo](../../006-mensageria/contracts/topologia-e-consumo.md) do
REQ-06. Tudo o que não está aqui (envelope, novas tentativas, DLQ, duplicados) segue aquele
documento.

## Topologia

| Item | Valor |
|------|-------|
| Consumidor | `pagamento` |
| Fila | `oms.pagamento`, ligada a `oms.eventos` com `estoque.reservado` |
| Filas de espera | `oms.pagamento.espera.{1000,2000,4000}` |
| DLQ | `oms.pagamento.dlq` |
| Consome | `estoque.reservado` v1 ([schema](../../006-mensageria/contracts/eventos/estoque.reservado.v1.schema.json)) |
| Publica | `pagamento.aprovado` v1 ([schema](../../006-mensageria/contracts/eventos/pagamento.aprovado.v1.schema.json)) e `pagamento.recusado` v1 ([schema](../../006-mensageria/contracts/eventos/pagamento.recusado.v1.schema.json)) |

Os três contratos são usados sem mudança.

## Resultado por pedido

| Pedido lido | Gateway | Pagamento | Status | Estoque | Publica | Mensagem |
|-------------|---------|-----------|--------|---------|---------|----------|
| Não existe | — | — | — | — | — | nova tentativa; DLQ no fim |
| Status final, sem reserva ou já pago | não chamado | — | sem mudança | sem mudança | nada | ack |
| `Pendente`, apto | aprova | `Aprovado` | `Processando` | sem mudança | `pagamento.aprovado` | ack |
| `Processando` ou `ProntoParaEnvio`, apto | aprova | `Aprovado` | sem mudança | sem mudança | `pagamento.aprovado` | ack |
| Não final, apto | recusa | `Recusado` | `PagamentoRejeitado` | devolvido | `pagamento.recusado` | ack |
| Conflito de versão nas 3 reavaliações | chamado uma vez | — | sem mudança | sem mudança | nada | nova tentativa; DLQ no fim |

Apto: estoque reservado, sem pagamento e em `Pendente`, `Processando` ou `ProntoParaEnvio`.
O pagamento, o status, a devolução, o evento e o registro de processado são gravados na
mesma transação.

## Configuração

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `GatewayPagamento:TaxaAprovacaoPercentual` | 90 | 0 a 100 |

## Exemplos

`pagamento.aprovado`:

```json
{
  "idEvento": "0199f6b1-2c3d-7e4f-8a5b-6c7d8e9f0a11",
  "tipo": "pagamento.aprovado",
  "versao": 1,
  "ocorridoEm": "2026-10-23T14:02:11.204Z",
  "idCorrelacao": "0199f6b0-9a00-7000-8000-000000000001",
  "dados": {
    "pedidoId": "0199f6b0-9a00-7000-8000-000000000001",
    "valor": 259.80,
    "identificadorTransacao": "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12"
  }
}
```

`pagamento.recusado`:

```json
{
  "idEvento": "0199f6b1-2c3d-7e4f-8a5b-6c7d8e9f0a21",
  "tipo": "pagamento.recusado",
  "versao": 1,
  "ocorridoEm": "2026-10-23T14:02:11.377Z",
  "idCorrelacao": "0199f6b0-9a00-7000-8000-000000000002",
  "dados": {
    "pedidoId": "0199f6b0-9a00-7000-8000-000000000002",
    "valor": 89.90,
    "motivo": "Pagamento recusado pelo emissor."
  }
}
```

## Efeito na API

O contrato HTTP v1 não muda. Depois do pagamento:
- aprovado: o pedido aparece em `Processando`; só a operação o cancela, e o cancelamento
  devolve o estoque (REQ-08), sem estorno;
- recusado: o pedido aparece em `PagamentoRejeitado`, status final; qualquer mudança de
  status responde o 409 `PEDIDO_TRANSICAO_INVALIDA` que já existe.
