# Data Model: Reserva de estoque assíncrona (REQ-08)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Só o que muda em relação aos REQ-04 e REQ-06.

## Pedido (alterado)

| Campo | Tipo | Regra |
|-------|------|-------|
| `EstoqueReservado` | `bool`, coluna `bit NOT NULL DEFAULT 0` | `true` depois de `ReservarEstoque`; volta a `false` em `DevolverEstoque`. Só a entidade altera (R-02) |

Operações novas:

| Operação | Pré-condição | Efeito |
|----------|--------------|--------|
| `ReservarEstoque(produtos, instante)` | `!EstoqueReservado`; `produtos` contém todos os itens | Se algum item tem produto inativo ou `QuantidadeEstoque < Quantidade`, devolve as `FaltaEstoque` e não altera nada. Senão, abate cada quantidade, marca `EstoqueReservado`, atualiza `AtualizadoEm` e devolve lista vazia |
| `DevolverEstoque(produtos, instante)` | `EstoqueReservado`; `produtos` contém todos os itens | Soma cada quantidade ao produto (ativo ou não), desmarca `EstoqueReservado` e atualiza `AtualizadoEm` |

Pré-condição violada lança `InvalidOperationException`: é erro de programação, porque quem
chama já conferiu o estado (R-04, R-05).

A versão de concorrência do pedido (REQ-04) muda a cada gravação dessas operações e decide
a corrida entre a reserva do worker e o cancelamento pela API (FR-011).

## FaltaEstoque (novo, Domain)

| Campo | Tipo | Regra |
|-------|------|-------|
| `ProdutoId` | `Guid` | Produto do item |
| `QuantidadeSolicitada` | `int` ≥ 1 | Quantidade do item |
| `QuantidadeDisponivel` | `int` ≥ 0 | Estoque lido no momento da reserva |
| `Inativo` | `bool` | Produto desativado no momento da reserva |

Não é persistido; vira o conteúdo de `estoque.insuficiente`.

## Produto (sem mudança de esquema)

`AjustarEstoque(variacao, instante)` já existe e impede estoque negativo. A reserva usa
variação negativa e a devolução, positiva. A versão de concorrência detecta a disputa
(FR-008).

## Histórico de status

O cancelamento por falta de estoque acrescenta uma entrada com:
- `StatusAnterior`: o status lido (`Pendente`, `Processando` ou `ProntoParaEnvio`);
- `StatusNovo`: `Cancelado`;
- `AutorId`: `null` (mudança do sistema);
- `Motivo`: `Estoque insuficiente.`

A reserva não acrescenta entrada, porque o status não muda.

## Eventos

| Tipo | Contrato | Quando |
|------|----------|--------|
| `estoque.reservado` v1 | REQ-06, sem mudança | Reserva gravada |
| `estoque.insuficiente` v1 | Novo: [schema](./contracts/eventos/estoque.insuficiente.v1.schema.json) | Pedido cancelado por falta de estoque |

Os dois são gravados como `EventoSaida` na mesma transação do efeito e publicados pelo
despachante do REQ-06.

## Migration

`ReservaEstoque`: `ALTER TABLE Pedidos ADD EstoqueReservado bit NOT NULL DEFAULT 0`. É a 5ª
migration do projeto.
