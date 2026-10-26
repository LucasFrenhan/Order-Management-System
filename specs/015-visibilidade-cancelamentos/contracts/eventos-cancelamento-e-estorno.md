# Contrato: eventos de cancelamento e de estorno (v1)

**Requisito**: REQ-15 (issue #29) | **Schemas**: [pedido.cancelado](./eventos/pedido.cancelado.v1.schema.json), [pagamento.estornado](./eventos/pagamento.estornado.v1.schema.json)

Os dois tipos são acréscimos à v1 do contrato de eventos (Princípio II). O envelope é o mesmo dos
demais (`specs/006-mensageria/contracts/eventos/`): `idEvento`, `tipo`, `versao`, `ocorridoEm`,
`idCorrelacao` (o pedido) e `dados`. Os tipos existentes não mudam.

## Como receber

- Troca `oms.eventos` (`topic`, durável). Ligar uma fila própria com a chave de roteamento
  `pedido.cancelado` e/ou `pagamento.estornado`.
- Nenhum consumidor interno do OMS recebe esses tipos. Sem fila ligada, a mensagem é descartada
  pelo broker e o OMS a considera enviada (os tipos não exigem rota; fluxo 8).
- Entrega pelo menos uma vez: deduplicar por `idEvento`. Não há ordem garantida entre tipos
  diferentes, nem entre `estoque.insuficiente` e o `pedido.cancelado` da mesma gravação.

## `pedido.cancelado`

Sai em toda chegada de um pedido a `Cancelado`, por qualquer caminho, na mesma gravação do
cancelamento. Não sai para o pedido que termina em `PagamentoRejeitado` (esse continua avisado
só por `pagamento.recusado`).

```json
{
  "idEvento": "0199f1a2-7c1e-7d40-9a51-3f2b6c8e1a10",
  "tipo": "pedido.cancelado",
  "versao": 1,
  "ocorridoEm": "2026-10-26T14:05:12.345+00:00",
  "idCorrelacao": "0199f1a0-1b2c-7e3d-8f40-5a6b7c8d9e0f",
  "dados": {
    "pedidoId": "0199f1a0-1b2c-7e3d-8f40-5a6b7c8d9e0f",
    "numero": "PED-20261026-000042",
    "motivo": "Operacao",
    "etapaEsgotada": null,
    "canceladoEm": "2026-10-26T14:05:12.345+00:00",
    "estoqueDevolvido": true,
    "estornoPendente": true,
    "ordemEntregaCancelada": true
  }
}
```

| `motivo` | Quando |
|----------|--------|
| `Cliente` | O dono do pedido, sem perfil de operação, cancela pela API (só em `Pendente`) |
| `Operacao` | Admin ou OperadorLogistica cancela pela API |
| `EstoqueInsuficiente` | O consumidor de estoque não consegue reservar; sai junto com `estoque.insuficiente` |
| `ReconciliacaoEsgotada` | A reconciliação desiste do pedido; `etapaEsgotada` diz em qual etapa (`Estoque`, `Pagamento` ou `Entrega`) |

`etapaEsgotada` é sempre enviado: texto com `ReconciliacaoEsgotada`, `null` nos demais motivos.
O valor, os itens e o motivo digitado na API não vão no evento; quem precisar relaciona pelo
`pedidoId` com `pedido.criado` ou `entrega.criada`.

## `pagamento.estornado`

Sai quando o processo de workers confirma o estorno no gateway, na mesma gravação da
confirmação, uma única vez por pedido. Falhas e novas tentativas não geram evento.

```json
{
  "idEvento": "0199f1a3-0a1b-7c2d-8e3f-4a5b6c7d8e9f",
  "tipo": "pagamento.estornado",
  "versao": 1,
  "ocorridoEm": "2026-10-26T14:06:01.002+00:00",
  "idCorrelacao": "0199f1a0-1b2c-7e3d-8f40-5a6b7c8d9e0f",
  "dados": {
    "pedidoId": "0199f1a0-1b2c-7e3d-8f40-5a6b7c8d9e0f",
    "numero": "PED-20261026-000042",
    "valor": 149.70,
    "identificadorTransacao": "SIM-0199f1a1c2d37e4f8a5b6c7d8e9f0a1b",
    "identificadorEstorno": "SIM-EST-0199f1a3d4e57f6a8b9c0d1e2f3a4b5c",
    "estornadoEm": "2026-10-26T14:06:01.002+00:00"
  }
}
```

Um `pedido.cancelado` com `estornoPendente: false` pode ser seguido de um `pagamento.estornado`:
é a cobrança aprovada pelo gateway depois de o pedido ter sido cancelado (REQ-13, FR-006), que
não gera `pagamento.aprovado`. Para conciliar dinheiro, a fonte é o `pagamento.estornado`.

## Compatibilidade

- Tipos novos na v1: um consumidor existente não os recebe (ligações por tipo) e, se recebesse,
  ignoraria o tipo desconhecido.
- Acrescentar campo opcional é compatível; acrescentar valor a `motivo` ou `etapaEsgotada`, ou
  tornar um campo obrigatório, não é, e exige decisão e nova versão.
- O contrato HTTP v1 não muda.
