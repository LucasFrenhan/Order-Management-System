# Fluxo 5: pagamento

**Onde roda**: Worker, consumidor `pagamento` (fila `oms.pagamento`), ao receber `estoque.reservado`.
**Origem**: REQ-09 ([spec](../../specs/009-pagamento/spec.md),
[contrato](../../specs/009-pagamento/contracts/consumidor-pagamento.md)), REQ-13
([spec](../../specs/013-cancelamento-com-estorno/spec.md), chave de idempotência e cobrança
para pedido cancelado) e REQ-15 ([spec](../../specs/015-visibilidade-cancelamentos/spec.md),
sem evento na cobrança para pedido cancelado) e REQ-19
([spec](../../specs/019-gateway-pagamento-http/spec.md), gateway pela rede e consulta da
cobrança pela chave). O gateway por HTTP, as falhas dele e a API simulada estão no
[fluxo 14](14-gateway-de-pagamento.md). O estorno está no [fluxo 10](10-estorno.md).
A base de consumo está no [fluxo 8](08-publicacao-e-consumo-de-eventos.md).

## Caminho feliz

1. Chega o `estoque.reservado`. O valor vem do pedido gravado (`valorTotal`), sem recálculo.
2. O pedido está **apto**: estoque reservado, sem pagamento e em `Pendente`, `Processando` ou
   `ProntoParaEnvio`.
3. O gateway é chamado **uma vez**, pela rede, na API de pagamento simulada (fluxo 14), com o
   pedido como `Idempotency-Key`. Ele aprova, devolvendo um identificador de transação (`PAG-…`).
   Falhas passageiras são repetidas dentro dessa mesma chamada, com a mesma chave.
4. Na mesma transação: o pagamento é registrado como `Aprovado`; o pedido vai de `Pendente` a
   `Processando`, sem autor, com o motivo "Pagamento aprovado."; e o `pagamento.aprovado` é
   gravado no outbox.

```json
{ "tipo": "pagamento.aprovado", "versao": 1,
  "dados": { "pedidoId": "…", "valor": 149.70, "identificadorTransacao": "PAG-0199f6b12c3d7e4f8a5b6c7d8e9f0a12" } }
```

A ordem de entrega (fluxo 6) consome esse evento.

## Resultados possíveis

| Pedido lido | Gateway | Pagamento | Status | Estoque | Evento | Por quê |
|-------------|---------|-----------|--------|---------|--------|---------|
| `Pendente`, apto | aprova | `Aprovado` | `Processando` | mantido | `pagamento.aprovado` | — |
| `Processando` ou `ProntoParaEnvio`, apto | aprova | `Aprovado` | **sem mudança** | mantido | `pagamento.aprovado` | A operação já moveu o pedido; o pagamento não o faz voltar |
| Não final, apto | recusa | `Recusado` | `PagamentoRejeitado` (final), sem autor, motivo do gateway | **devolvido** na mesma gravação | `pagamento.recusado` | O estoque não fica preso num pedido que não será pago |
| Status final, sem reserva ou já pago (fora o caso da linha seguinte) | **não chamado** | — | sem mudança | sem mudança | nenhum (log 7003) | Idempotência pelo estado: nunca cobra duas vezes o mesmo pedido |
| `Cancelado` sem pagamento e com o estoque devolvido (a reentrega ou a reconciliação chega depois de um cancelamento) | **consultado** pela chave, sem cobrar (log 7019) | `EstornoPendente` se a consulta achar a cobrança aprovada; senão, nenhum | `Cancelado`, sem mudança | já devolvido | nenhum (logs 7009 e 7005 quando há cobrança) | A cobrança pode ter sido efetivada sem a resposta chegar e o pedido cancelado antes da reentrega: o Cliente não fica cobrado sem registro (REQ-19, FR-015). Uma falha na consulta segue as novas tentativas |
| Cancelado pela operação **enquanto** o gateway aprovava (a gravação dá conflito e a releitura encontra o pedido `Cancelado` sem pagamento) | chamado uma vez, aprova | `EstornoPendente`, com a transação | `Cancelado`, sem mudança | já devolvido pelo cancelamento | nenhum, nem `pagamento.aprovado` (logs 7009 e 7005, origem `CobrancaAposCancelamento`) | O Cliente foi cobrado: o pagamento é registrado e o estorno sai pelo fluxo 10 (REQ-13, R-07); para outros sistemas, o caso aparece só no `pagamento.estornado` (REQ-15, FR-014). Uma recusa nesse caso continua descartada |
| Não existe | — | — | — | — | novas tentativas e DLQ | — |
| Conflito nas 3 reavaliações | chamado uma vez | — | sem mudança | sem mudança | novas tentativas e DLQ | O resultado do gateway vale para as reavaliações: uma mensagem não "sorteia" duas vezes |

Exemplo de recusa:

```json
{ "tipo": "pagamento.recusado", "versao": 1,
  "dados": { "pedidoId": "…", "valor": 89.90, "motivo": "Pagamento recusado pelo emissor." } }
```

## O gateway

- A cobrança vai para a API de pagamento simulada, pela rede (REQ-19). A configuração, a
  classificação das respostas, as novas tentativas, o circuito e a API simulada estão no
  [fluxo 14](14-gateway-de-pagamento.md).
- **O pedido é a chave de idempotência** da cobrança e do estorno (REQ-13, R-03; REQ-19,
  FR-017): a API guarda as chaves do lado dela, e repetir a cobrança do mesmo pedido devolve o
  mesmo resultado e a mesma transação, sem cobrar de novo. Exemplo: o consumidor cobra, cai
  antes de gravar, e a reconciliação cobra de novo; a API devolve a aprovação original, também
  depois de o Worker reiniciar.
- A recusa vem da API (`"Pagamento recusado pelo emissor."`), com a chance
  `Simulacao:TaxaAprovacaoPercentual` da API simulada (padrão 90). A chave
  `GatewayPagamento:TaxaAprovacaoPercentual` do Worker deixou de existir.
- Os testes que não exercitam a rede usam um gateway em memória, só do projeto de testes
  (REQ-19, FR-009a); os do REQ-19 sobem a API simulada real.

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| API de pagamento sem resultado depois das tentativas da chamada (fluxo 14) | Nada é gravado. A mensagem falha e segue as novas tentativas e a DLQ. Se o pedido continua parado, a reconciliação tenta de novo a cada ciclo e, com 3 ciclos de falha e mais de 24 h, registra o pagamento como `NaoProcessado`, devolve o estoque e cancela o pedido (fluxo 9) | Um pedido não pode ficar parado para sempre segurando estoque |
| API de pagamento fora do ar (circuito aberto, fluxo 14) | A mensagem volta para a fila sem contar nova tentativa nem ir para a DLQ, e o consumo de pagamento pausa até a API voltar (fluxo 8) | Uma queda da API não enche a DLQ de mensagens sem defeito (REQ-19, FR-016a) |
| Worker cai depois da decisão e antes de gravar, ou a resposta não chega | Nada fica gravado; a reentrega cobra de novo com a mesma chave e recebe o mesmo resultado da API | Chave de idempotência por pedido (#28, REQ-13; REQ-19) |
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

Logs 7001 (aprovado), 7002 (recusado), 7003 (ignorado), 7004 (conflito), 7009 (cobrança
aprovada para pedido cancelado) e 7019 (cobrança do pedido cancelado consultada pela chave). Os
logs da chamada ao gateway (7013 a 7018) estão no fluxo 14; os do estorno (7005 a 7012 e 7020),
no fluxo 10. Com a telemetria ligada, a chamada aparece no trace do pedido como o span
`cobrar pagamento`, com um span HTTP por tentativa (fluxo 12).

## Limitações

- Não há nova tentativa de cobrança depois de uma recusa: o pedido termina em
  `PagamentoRejeitado`, e o Cliente cria outro.
- Resolvida pela #46 (REQ-19): a cobrança efetivada sem resposta, com o pedido cancelado antes
  da reentrega, agora é consultada pela chave e registrada com o estorno pendente.
