# Fluxo 4: reserva de estoque

**Onde roda**: Worker, consumidor `estoque` (fila `oms.estoque`), ao receber `pedido.criado`.
**Origem**: REQ-08 ([spec](../../specs/008-estoque/spec.md),
[contrato](../../specs/008-estoque/contracts/consumidor-estoque.md)) e REQ-15 (`pedido.cancelado` na falta de estoque).
A base de consumo (novas tentativas, DLQ e duplicados) está no [fluxo 8](08-publicacao-e-consumo-de-eventos.md).

## Caminho feliz

1. Chega o `pedido.criado`. Os itens são lidos do pedido gravado, não do evento.
2. Se todos os itens estão disponíveis (produto ativo e estoque maior ou igual à quantidade),
   o estoque de cada um é abatido e o pedido é marcado com `EstoqueReservado`. O status
   continua `Pendente`.
3. Na mesma transação, o `estoque.reservado` é gravado no outbox e o evento consumido é
   registrado como processado.

```json
{ "tipo": "estoque.reservado", "versao": 1, "idCorrelacao": "0199f1a2-…",
  "dados": { "pedidoId": "0199f1a2-…", "itens": [ { "produtoId": "…0001", "quantidade": 2 } ] } }
```

O pagamento (fluxo 5) consome esse evento.

## Resultados possíveis

| Pedido lido | Estoque | Status | Evento de saída | Mensagem | Por quê |
|-------------|---------|--------|-----------------|----------|---------|
| Não final, todos os itens disponíveis | abatido | sem mudança | `estoque.reservado` | ack | — |
| Não final, algum item indisponível | **nada abatido** | `Cancelado`, sem autor, motivo "Estoque insuficiente." | `estoque.insuficiente`, só com os itens em falta, e `pedido.cancelado` com o motivo `EstoqueInsuficiente` (REQ-15) | ack | Tudo ou nada: reservar parte do pedido deixaria estoque preso num pedido que não pode ser atendido |
| Status final (`Cancelado`, `Despachado`, `PagamentoRejeitado`) | sem mudança | sem mudança | nenhum | ack, com log 6003 | O pedido não precisa mais de estoque, por exemplo porque foi cancelado antes do consumo |
| Já reservado | sem mudança | sem mudança | nenhum | ack, com log 6003 | Idempotência pelo estado: um segundo evento não reserva duas vezes |
| Não existe | — | — | — | novas tentativas e DLQ | Inconsistência que precisa de investigação |
| Conflito de concorrência nas 3 reavaliações | sem mudança | sem mudança | nenhum | novas tentativas e DLQ | Ver abaixo |

Exemplo de falta:

```json
{ "tipo": "estoque.insuficiente", "versao": 1,
  "dados": { "pedidoId": "…", "itens": [ { "produtoId": "…0008", "quantidadeSolicitada": 2, "quantidadeDisponivel": 1, "inativo": false } ] } }
```

Na mesma gravação sai o `pedido.cancelado` (REQ-15), para quem só acompanha os cancelamentos:

```json
{ "tipo": "pedido.cancelado", "versao": 1,
  "dados": { "pedidoId": "…", "numero": "PED-20261024-000002", "motivo": "EstoqueInsuficiente", "etapaEsgotada": null,
             "canceladoEm": "…", "estoqueDevolvido": false, "estornoPendente": false, "ordemEntregaCancelada": false } }
```

Os dois saem pelo outbox na ordem em que foram gravados, mas o contrato não garante ordem
entre tipos diferentes: quem consome os dois não deve depender dela.

## Casos de borda

| Caso | Comportamento | Por quê |
|------|---------------|---------|
| Quantidade exatamente igual ao estoque | Reservado; o estoque fica em 0 | Disponível é "maior ou igual" |
| Produto desativado entre a criação e a reserva | Conta como falta (`inativo: true`) e o pedido é cancelado | Produto inativo não é vendido |
| Produto desativado depois da reserva | A reserva continua valendo | A mercadoria já está separada |
| Estoque reposto depois do cancelamento por falta | O pedido continua `Cancelado` | Não há nova tentativa automática; o Cliente cria outro pedido |
| Pedido movido pela operação para `Processando` antes da reserva | É reservado normalmente | Qualquer status não final precisa do estoque |
| Dois pedidos disputando o último item (`CAT-008` no catálogo de desenvolvimento) | Um reserva; o outro perde na concorrência otimista do produto, é reavaliado com o estoque atual e é cancelado por falta | Nunca há estoque negativo |
| Cancelamento pela API enquanto a reserva acontece | Um dos dois perde e é reavaliado (fluxo 3) | Concorrência otimista no pedido |

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| Worker cai depois de abater e antes do ack | A reserva, o status, o evento de saída e o registro de processado estão numa transação só. A reentrega é descartada como duplicada, ou refaz tudo se nada foi gravado | Nunca há reserva dupla |
| Banco fora do ar | A mensagem falha e segue as novas tentativas (1 s, 2 s, 4 s) e, no fim, a DLQ | A reconciliação (fluxo 9) recupera o pedido depois |
| Broker fora do ar depois da reserva | O `estoque.reservado` fica pendente no outbox e sai quando o broker voltar | A reserva não é desfeita |
| Mensagem fora do contrato | Direto para a DLQ, sem tentativa | Não adianta tentar de novo |

## Observabilidade

Logs 6001 (reservado), 6002 (cancelado por falta), 6003 (ignorado), 6004 (conflito) e
6005 (estoque devolvido no cancelamento pela API).

## Limitações

- Pedido que falha sempre na reserva é cancelado pela reconciliação depois de 24 h (fluxo 9),
  com o `pedido.cancelado` da etapa `Estoque` (#29, REQ-15).
