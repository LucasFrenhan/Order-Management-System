# Data Model: Criação, consulta e status de pedidos (REQ-05)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Este requisito usa as entidades do REQ-04 (`Produto`, `Pedido`, `ItemPedido`,
`HistoricoStatusPedido`) sem mudar suas colunas. A migration nova, `ChavesIdempotencia`,
acrescenta uma tabela e uma sequência.

## RegistroIdempotencia (`Application/Pedidos/RegistroIdempotencia.cs` → tabela `ChavesIdempotencia`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| ClienteId | Guid | `uniqueidentifier`, PK (1/2) | usuário do token (FR-007) |
| Chave | Guid | `uniqueidentifier`, PK (2/2) | valor de `X-Idempotency-Key` |
| Impressao | string | `char(64)` | SHA-256 em hexadecimal maiúsculo do conteúdo normalizado (FR-008) |
| PedidoId | Guid | FK → `Pedidos.Id`, `Cascade` | pedido criado |
| Resposta | string | `nvarchar(max)` | JSON do `PedidoResponseDTO` enviado na criação (FR-009) |
| CriadoEm | DateTimeOffset | `datetimeoffset` | instante da criação; `IX_ChavesIdempotencia_CriadoEm` |

- **Chave primária**: `PK_ChavesIdempotencia (ClienteId, Chave)`. A violação dessa chave é
  o sinal de corrida entre requisições com a mesma chave (research R-04).
- **Gravação**: o registro é gravado só na mesma operação em que o pedido é criado (FR-006,
  FR-013).
- **Chave estrangeira do cliente**: não há FK para `Usuarios`. O `PedidoId` já leva ao
  dono, e assim a tabela fica independente do Identity.
- **Classe**: `sealed`, com construtor que recebe todos os campos e setters privados, sem
  comportamento.

## Sequência `SequenciaNumeroPedido`

| Propriedade | Valor |
|-------------|-------|
| Tipo | `bigint` |
| Início | 1 |
| Incremento | 1 |

O `GeradorNumeroPedido` (Infrastructure) lê `NEXT VALUE FOR SequenciaNumeroPedido` e monta
`PED-{AAAAMMDD}-{valor % 1.000.000, 6 dígitos}` com a data UTC do instante informado
(research R-05).

## TransicoesStatusPedido (`Domain/Pedidos/TransicoesStatusPedido.cs`)

Regra pura, sem estado:

| Método | Verdadeiro para |
|--------|-----------------|
| `PermitidaParaOperacao(de, para)` | Pendente → Processando; Processando → ProntoParaEnvio; ProntoParaEnvio → Despachado; Pendente/Processando/ProntoParaEnvio → Cancelado |
| `PermitidaParaCliente(de, para)` | Pendente → Cancelado |

Qualquer outra combinação é falsa, inclusive `de == para` e qualquer saída de
`Despachado`, `Cancelado` ou `PagamentoRejeitado`.

## Abstrações do Application (`Application/Pedidos/`)

| Tipo | Papel |
|------|-------|
| `IArmazenamentoPedidos` | `ObterProdutosAsync`, `ObterPedidoAsync(id, paraAlterar)`, `ObterRegistroIdempotenciaAsync`, `Adicionar(pedido, registro)`, `SalvarAsync`, `DescartarAlteracoes` (R-02) |
| `IGeradorNumeroPedido` | `ProximoAsync(DateTimeOffset instante)` → número no padrão do contrato |
| `ChaveIdempotenciaRegistradaException` | gravação recusada porque o par (cliente, chave) já existe |
| `ImpressaoConteudo` | `Calcular(CriarPedidoRequestDTO)` → hexadecimal de 64 caracteres |
| `ServicoPedidos` | implementa `IServicoPedidos` (R-01) |

## Mapeamento de falhas

| Situação | `TipoFalha` | HTTP | `codigo` |
|----------|-------------|------|----------|
| Produto inexistente | RegraNegocio | 422 | `PRODUTO_INEXISTENTE` |
| Produto inativo ou com estoque menor que a quantidade | RegraNegocio | 422 | `PRODUTO_INDISPONIVEL` |
| Mesma chave, conteúdo diferente | Conflito | 409 | `IDEMPOTENCIA_CONTEUDO_DIVERGENTE` |
| Corrida sem registro vencedor visível | Conflito | 409 | `IDEMPOTENCIA_EM_PROCESSAMENTO` |
| Pedido inexistente ou de outro Cliente | NaoEncontrado | 404 | — |
| Cliente em transição diferente de Pendente → Cancelado | SemPermissao | 403 | — |
| Transição fora da tabela, ou 3 conflitos seguidos | Conflito | 409 | `PEDIDO_TRANSICAO_INVALIDA` |

`TipoFalha.NaoImplementado` deixa de existir (research R-10).
