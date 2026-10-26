# Data Model: Visibilidade dos cancelamentos e dos estornos para outros sistemas

**Requisito**: REQ-15 (issue #29) | **Research**: [research.md](./research.md)

Nenhuma tabela, coluna ou migration nova (R-07). O que muda são os eventos de saída e o retorno
de dois métodos do domínio.

## Eventos novos (contrato v1)

### `pedido.cancelado` — `PedidoCancelado`

| Campo | Tipo | Obrigatório | Regra |
|-------|------|-------------|-------|
| `pedidoId` | uuid | sim | identificador do pedido; igual ao `idCorrelacao` do envelope |
| `numero` | texto `PED-AAAAMMDD-NNNNNN` | sim | número do pedido |
| `motivo` | `MotivoCancelamento` | sim | `Cliente`, `Operacao`, `EstoqueInsuficiente` ou `ReconciliacaoEsgotada` |
| `etapaEsgotada` | `EtapaEsgotada` ou `null` | sim (pode ser nulo) | `Estoque`, `Pagamento` ou `Entrega` quando o motivo é `ReconciliacaoEsgotada`; `null` nos demais |
| `canceladoEm` | data-hora UTC | sim | o instante gravado no histórico de status |
| `estoqueDevolvido` | booleano | sim | o estoque reservado voltou aos produtos neste cancelamento |
| `estornoPendente` | booleano | sim | o pagamento aprovado passou a "estorno pendente" neste cancelamento |
| `ordemEntregaCancelada` | booleano | sim | a ordem de entrega sem despacho foi cancelada neste cancelamento |

Sem valor, itens, identificador do Cliente, autor ou texto livre do motivo (FR-002, FR-010).

**Origem de cada campo por caminho** (R-01, R-02):

| Caminho | `motivo` | `etapaEsgotada` | `estoqueDevolvido` | `estornoPendente` | `ordemEntregaCancelada` |
|---------|----------|-----------------|--------------------|-------------------|-------------------------|
| API, usuário sem perfil de operação | `Cliente` | `null` | se havia reserva | `false` (o Cliente só cancela em `Pendente`, sem cobrança) | `false` |
| API, Admin ou OperadorLogistica | `Operacao` | `null` | se havia reserva | se o pagamento estava aprovado | se havia ordem sem despacho |
| Falta de estoque | `EstoqueInsuficiente` | `null` | `false` | `false` | `false` |
| Reconciliação, estoque | `ReconciliacaoEsgotada` | `Estoque` | `false` | `false` | `false` |
| Reconciliação, pagamento | `ReconciliacaoEsgotada` | `Pagamento` | `true` | `false` | `false` |
| Reconciliação, entrega | `ReconciliacaoEsgotada` | `Entrega` | `true` | `true` | `false` (a etapa é justamente a ordem que não foi criada) |

### `pagamento.estornado` — `PagamentoEstornado`

| Campo | Tipo | Obrigatório | Regra |
|-------|------|-------------|-------|
| `pedidoId` | uuid | sim | identificador do pedido |
| `numero` | texto `PED-AAAAMMDD-NNNNNN` | sim | número do pedido |
| `valor` | decimal, 2 casas, > 0 | sim | valor estornado (o valor cobrado; sem estorno parcial) |
| `identificadorTransacao` | texto, até 100 | sim | transação da cobrança no gateway |
| `identificadorEstorno` | texto, até 100 | sim | identificador do estorno no gateway |
| `estornadoEm` | data-hora UTC | sim | o instante gravado no pagamento |

## Enums do contrato (novos)

- `MotivoCancelamento`: `Cliente`, `Operacao`, `EstoqueInsuficiente`, `ReconciliacaoEsgotada`.
- `EtapaEsgotada`: `Estoque`, `Pagamento`, `Entrega` (espelha `EtapaFluxoPedido`, mas é do
  contrato: uma etapa interna nova não muda o contrato sem decisão).

Acrescentar um valor a um desses enums muda o contrato e exige decisão (Princípio II): um
consumidor que valida pelo schema recusaria o valor novo.

## Domínio

| Tipo | Mudança |
|------|---------|
| `Pedido.Cancelar` | sem mudança de assinatura; passa a ser usado também pela falta de estoque e pela reconciliação da etapa de estoque (R-01) |
| `Pedido.RegistrarPagamentoNaoProcessado` | passa a devolver `ResultadoCancelamento(EstoqueDevolvido: true, EstornoSolicitado: false, OrdemCancelada: false)` |
| `ResultadoCancelamento` | sem mudança; o comentário passa a citar o evento além dos logs |

## Transições observadas pela guarda (R-03)

| Entidade | Transição | Evento exigido na mesma gravação |
|----------|-----------|----------------------------------|
| `Pedido.Status` | qualquer → `Cancelado` | `pedido.cancelado` com o mesmo `idCorrelacao` |
| `Pagamento.Situacao` | `EstornoPendente` → `Estornado` | `pagamento.estornado` com o mesmo `idCorrelacao` |

O ciclo de vida do pedido e as situações do pagamento não mudam.
