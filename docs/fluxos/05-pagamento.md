# Fluxo 5: pagamento

**Onde roda**: Worker, consumidor `pagamento` (fila `oms.pagamento`), ao receber `estoque.reservado`.
**Origem**: REQ-09 ([spec](../../specs/009-pagamento/spec.md),
[contrato](../../specs/009-pagamento/contracts/consumidor-pagamento.md)), REQ-13
([spec](../../specs/013-cancelamento-com-estorno/spec.md), chave de idempotência e cobrança
para pedido cancelado) e REQ-15 ([spec](../../specs/015-visibilidade-cancelamentos/spec.md),
sem evento na cobrança para pedido cancelado). O estorno está no [fluxo 10](10-estorno.md).
A base de consumo está no [fluxo 8](08-publicacao-e-consumo-de-eventos.md).

## Caminho feliz

1. Chega o `estoque.reservado`. O valor vem do pedido gravado (`valorTotal`), sem recálculo.
2. O pedido está **apto**: estoque reservado, sem pagamento e em `Pendente`, `Processando` ou
   `ProntoParaEnvio`.
3. O gateway (simulado) é chamado **uma vez** e aprova, devolvendo um identificador de
   transação (`SIM-…`).
4. Na mesma transação: o pagamento é registrado como `Aprovado`; o pedido vai de `Pendente` a
   `Processando`, sem autor, com o motivo "Pagamento aprovado."; e o `pagamento.aprovado` é
   gravado no outbox.

```json
{ "tipo": "pagamento.aprovado", "versao": 1,
  "dados": { "pedidoId": "…", "valor": 149.70, "identificadorTransacao": "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12" } }
```

A ordem de entrega (fluxo 6) consome esse evento.

## Resultados possíveis

| Pedido lido | Gateway | Pagamento | Status | Estoque | Evento | Por quê |
|-------------|---------|-----------|--------|---------|--------|---------|
| `Pendente`, apto | aprova | `Aprovado` | `Processando` | mantido | `pagamento.aprovado` | — |
| `Processando` ou `ProntoParaEnvio`, apto | aprova | `Aprovado` | **sem mudança** | mantido | `pagamento.aprovado` | A operação já moveu o pedido; o pagamento não o faz voltar |
| Não final, apto | recusa | `Recusado` | `PagamentoRejeitado` (final), sem autor, motivo do gateway | **devolvido** na mesma gravação | `pagamento.recusado` | O estoque não fica preso num pedido que não será pago |
| Status final, sem reserva ou já pago | **não chamado** | — | sem mudança | sem mudança | nenhum (log 7003) | Idempotência pelo estado: nunca cobra duas vezes o mesmo pedido |
| Cancelado pela operação **enquanto** o gateway aprovava (a gravação dá conflito e a releitura encontra o pedido `Cancelado` sem pagamento) | chamado uma vez, aprova | `EstornoPendente`, com a transação | `Cancelado`, sem mudança | já devolvido pelo cancelamento | nenhum, nem `pagamento.aprovado` (logs 7009 e 7005, origem `CobrancaAposCancelamento`) | O Cliente foi cobrado: o pagamento é registrado e o estorno sai pelo fluxo 10 (REQ-13, R-07); para outros sistemas, o caso aparece só no `pagamento.estornado` (REQ-15, FR-014). Uma recusa nesse caso continua descartada |
| Não existe | — | — | — | — | novas tentativas e DLQ | — |
| Conflito nas 3 reavaliações | chamado uma vez | — | sem mudança | sem mudança | novas tentativas e DLQ | O resultado do gateway vale para as reavaliações: uma mensagem não "sorteia" duas vezes |

Exemplo de recusa:

```json
{ "tipo": "pagamento.recusado", "versao": 1,
  "dados": { "pedidoId": "…", "valor": 89.90, "motivo": "Pagamento recusado pelo emissor." } }
```

## O gateway simulado

- Aprova com a chance `GatewayPagamento:TaxaAprovacaoPercentual` (padrão 90, de 0 a 100).
  Fora da faixa, o Worker não sobe.
- **O pedido é a chave de idempotência** da cobrança e do estorno (REQ-13, R-03): repetir a
  cobrança do mesmo pedido devolve o mesmo resultado e a mesma transação, sem novo sorteio.
  Exemplo: o consumidor cobra, cai antes de gravar, e a reconciliação cobra de novo; o gateway
  devolve a aprovação original.
- As chaves ficam na memória do Worker e se perdem quando ele reinicia. Isso só é aceitável
  porque a simulação não tem efeito externo; um gateway real guarda as chaves do lado dele.
- Os testes usam taxa 100 ou 0, para nunca depender de sorte.

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| Gateway lança exceção ou não responde | A mensagem falha e segue as novas tentativas e a DLQ. Se o pedido continua parado, a reconciliação tenta de novo a cada ciclo e, com 3 ciclos de falha e mais de 24 h, registra o pagamento como `NaoProcessado`, devolve o estoque e cancela o pedido (fluxo 9) | Um pedido não pode ficar parado para sempre segurando estoque |
| Worker cai depois da decisão e antes de gravar | Nada fica gravado; a reentrega cobra de novo com a mesma chave e recebe o mesmo resultado | Chave de idempotência por pedido (#28, REQ-13) |
| Worker cai depois de gravar e antes do ack | A reentrega é descartada como duplicada | Pagamento, status, devolução, evento e registro de processado estão numa transação só |
| Conflito com o cancelamento pela API ou com outra reserva | Reavaliado com o estado atual até 3 vezes; depois, novas tentativas e DLQ | Nada é gravado pela metade |
| Broker fora do ar depois do pagamento | O evento fica pendente no outbox | O resultado do pagamento não é desfeito |
| Mensagem fora do contrato | Direto para a DLQ | — |

## Efeito na API

- Aprovado: o pedido aparece em `Processando`. Só a operação pode cancelá-lo (fluxo 3), e o
  cancelamento deixa o estorno pendente (fluxo 10).
- Recusado: `PagamentoRejeitado` é final, e qualquer mudança de status responde 409.
- As situações `NaoProcessado`, `EstornoPendente` e `Estornado` são internas: não aparecem na
  API nem nos eventos.

## Observabilidade

Logs 7001 (aprovado), 7002 (recusado), 7003 (ignorado), 7004 (conflito) e 7009 (cobrança
aprovada para pedido cancelado). Os logs do estorno (7005 a 7012) estão no fluxo 10.

## Limitações

- Não há nova tentativa de cobrança depois de uma recusa: o pedido termina em
  `PagamentoRejeitado`, e o Cliente cria outro.
- Se o Worker cair depois de o gateway aprovar e antes de gravar, e a operação cancelar o
  pedido antes da reentrega, a reentrega encontra o pedido cancelado e não chama o gateway: o
  sistema não sabe que houve cobrança. Com o simulado não há efeito; **com um gateway real**,
  seria preciso consultar a cobrança pela chave (REQ-13, R-07).
