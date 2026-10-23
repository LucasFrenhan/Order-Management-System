# Contrato: topologia, publicação e consumo

**Requisitos**: FR-005 a FR-020 | **Research**: R-04 a R-09 | **Schemas**: [eventos/](./eventos/)

Este documento fixa o que os workers das issues #8 a #10 e a operação podem esperar.

## Eventos (versão 1)

| Tipo (chave de roteamento) | Publicado por | Schema |
|----------------------------|---------------|--------|
| `pedido.criado` | criação de pedido (REQ-05), via outbox | [pedido.criado.v1](./eventos/pedido.criado.v1.schema.json) |
| `estoque.reservado` | worker de estoque (#8) | [estoque.reservado.v1](./eventos/estoque.reservado.v1.schema.json) |
| `pagamento.aprovado` | worker de pagamento (#9) | [pagamento.aprovado.v1](./eventos/pagamento.aprovado.v1.schema.json) |
| `pagamento.recusado` | worker de pagamento (#9) | [pagamento.recusado.v1](./eventos/pagamento.recusado.v1.schema.json) |

O envelope é comum a todos: `idEvento`, `tipo`, `versao`, `ocorridoEm` (UTC),
`idCorrelacao` (o id do pedido) e `dados`. Mudanças seguem o Princípio II: na mesma versão,
só campos opcionais novos; o resto exige versão nova.

## Mensagem no broker

| Propriedade | Valor |
|-------------|-------|
| Troca | `oms.eventos` (`topic`, durável) |
| Chave de roteamento | o `tipo` |
| `MessageId` | o `idEvento` |
| `Type` | o `tipo` |
| `ContentType` | `application/json` |
| Persistência | `persistent` |
| Corpo | o envelope em JSON UTF-8 |

**Garantia**: pelo menos uma vez. Um mesmo `idEvento` pode chegar mais de uma vez, e o
consumidor deve ser idempotente; a base de consumo descarta duplicados por consumidor.

## Filas por consumidor `C`

| Fila | Papel |
|------|-------|
| `oms.C` | principal, ligada a `oms.eventos` pelas chaves consumidas |
| `oms.C.espera.1000`, `.2000`, `.4000` | espera antes de nova tentativa (TTL); voltam sozinhas para `oms.C` |
| `oms.C.dlq` | mensagens mortas |

**Cabeçalhos** que a base acrescenta:
- `x-tentativas`: novas tentativas já feitas;
- `x-ultimo-erro`: motivo da última falha;
- `x-motivo`: na DLQ;
- `x-falhou-em`: na DLQ (UTC).

## Comportamento da base de consumo

| Situação | O que acontece |
|----------|----------------|
| Manipulador termina sem exceção | efeito e registro de processado na mesma transação; ack |
| Evento já processado por este consumidor | ack, sem chamar o manipulador |
| Exceção, `x-tentativas` < 3 | republica na fila de espera do próximo intervalo (1 s, 2 s, 4 s); ack na original |
| Exceção, `x-tentativas` = 3 | publica em `oms.C.dlq` com o motivo; ack (4 execuções no total) |
| Corpo inválido, tipo ou versão desconhecida, campo obrigatório ausente | publica direto em `oms.C.dlq`; ack, sem chamar o manipulador |

## Para escrever um worker (issues #8 a #10)

1. Implementar `IManipuladorEvento<TDados>` no Application, com `Consumidor` (o nome da
   fila, por exemplo `estoque`) e `Tipos` (por exemplo `pedido.criado`).
2. Gravar o efeito pelas portas do Application (por exemplo `IArmazenamentoPedidos`) resolvidas
   no mesmo escopo; elas usam o `ContextoBancoDados` do escopo. A base faz commit junto
   com o registro de processado.
3. Para publicar um evento, gravar um `EventoSaida` no mesmo escopo (outbox). Nunca
   publicar direto no broker.
4. Registrar no `ProgramaWorker.cs` do Worker com `AdicionarConsumidor<TManipulador, TDados>()`.

Exemplo: o consumidor de estoque do REQ-08 (`ReservaEstoque`, em
`src/PedidoFulfillment.Application/Estoque/`), descrito em
[consumidor de estoque](../../008-estoque/contracts/consumidor-estoque.md). Ele também mostra
como reavaliar um conflito de concorrência dentro da transação da base.
