# Data Model: Domínio de pedidos e persistência (REQ-04)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

As entidades ficam no `PedidoFulfillment.Domain`, e o mapeamento fica em
`PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/` (R-01). Todas as tabelas
abaixo nascem na migration `EntidadesNegocio`, posterior à `InicialIdentidade` do REQ-03.

## Diagrama

```text
Usuarios (REQ-03) 1 ──< Pedidos                  (ClienteId; Restrict)
Usuarios (REQ-03) 1 ──< HistoricosStatusPedido   (AutorId, opcional; Restrict)
Produtos          1 ──< ItensPedido              (Restrict)
Pedidos           1 ──< ItensPedido              (Cascade)
Pedidos           1 ──< HistoricosStatusPedido   (Cascade)
Pedidos           1 ──o Pagamentos               (0..1; Cascade)
Pedidos           1 ──o OrdensEntrega            (0..1; Cascade)
```

## Enumerações (Domain)

### StatusPedido (já existe, REQ-01)

`Pendente`, `Processando`, `ProntoParaEnvio`, `Despachado`, `Cancelado`,
`PagamentoRejeitado`. Gravado como texto (FR-006).

### SituacaoPagamento (nova, `Domain/Pagamentos`)

`Pendente`, `Aprovado`, `Recusado`. Gravada como texto. A numeração começa em 1, como no
`StatusPedido`.

## Produto (`Domain/Produtos/Produto.cs` → tabela `Produtos`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | Guid | `uniqueidentifier` PK | `Guid.CreateVersion7()` |
| Sku | string | `nvarchar(50)`, collation `SQL_Latin1_General_CP1_CI_AS` | obrigatório; único sem diferenciar maiúsculas (`IX_Produtos_Sku`) |
| Nome | string | `nvarchar(200)` | obrigatório, aparado |
| Descricao | string? | `nvarchar(1000)` | opcional |
| Preco | decimal | `decimal(18,2)` | ≥ 0 (`CK_Produtos_PrecoNaoNegativo`) |
| QuantidadeEstoque | int | `int` | ≥ 0 (`CK_Produtos_EstoqueNaoNegativo`) |
| Ativo | bool | `bit` | |
| CriadoEm, AtualizadoEm | DateTimeOffset | `datetimeoffset` | UTC |
| *Versao* (sombra) | — | `rowversion` | concorrência otimista (FR-003, R-04) |

Comportamento:
- `AjustarEstoque(variacao, instante)`: recusa um resultado negativo.
- `AlterarDados(nome, descricao, preco, instante)`.
- `Ativar(instante)` e `Desativar(instante)`.

Todos atualizam `AtualizadoEm`.

## Pedido (`Domain/Pedidos/Pedido.cs` → tabela `Pedidos`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | Guid | `uniqueidentifier` PK | v7 |
| Numero | string | `nvarchar(19)` | obrigatório; único (`IX_Pedidos_Numero`); gerado pelo REQ-05 |
| ClienteId | Guid | `uniqueidentifier` FK → `Usuarios.Id` | Restrict (FR-007) |
| Status | StatusPedido | `nvarchar(30)` | `CK_Pedidos_StatusValido`; `Pendente` na criação |
| ValorTotal | decimal | `decimal(18,2)` | soma dos subtotais; ≥ 0 |
| Moeda | string | `nvarchar(3)` | sempre `BRL` |
| CriadoEm, AtualizadoEm | DateTimeOffset | `datetimeoffset` | UTC |
| *Versao* (sombra) | — | `rowversion` | concorrência otimista (FR-021) |
| Itens | IReadOnlyCollection\<ItemPedido\> | — | navegação |
| Historico | IReadOnlyCollection\<HistoricoStatusPedido\> | — | navegação, em ordem de `Id` |
| Pagamento | Pagamento? | — | navegação 0..1 |
| OrdemEntrega | OrdemEntrega? | — | navegação 0..1 |

Índices: `IX_Pedidos_ClienteId_CriadoEm` e `IX_Pedidos_Status` (R-07).

Comportamento:
- **Criação** (`Pedido(clienteId, numero, itens, instante)`):
  - exige ao menos um item e produtos distintos (guarda de argumento);
  - calcula `ValorTotal`;
  - status `Pendente`;
  - acrescenta a entrada de criação no histórico (`StatusAnterior` nulo).
- **`MudarStatus(novo, autorId?, motivo?, instante)`**:
  - atribui o status;
  - atualiza `AtualizadoEm`;
  - acrescenta uma entrada no histórico.

  Não valida a transição; quem valida é o REQ-05. As três alterações vão na mesma
  gravação (FR-023).

## ItemPedido (`Domain/Pedidos/ItemPedido.cs` → tabela `ItensPedido`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| PedidoId | Guid | PK (1/2), FK → `Pedidos.Id` | Cascade |
| ProdutoId | Guid | PK (2/2), FK → `Produtos.Id` | Restrict (FR-004); `IX_ItensPedido_ProdutoId` |
| NomeProduto | string | `nvarchar(200)` | cópia do nome na compra |
| Quantidade | int | `int` | > 0 (`CK_ItensPedido_QuantidadePositiva`) |
| PrecoUnitario | decimal | `decimal(18,2)` | cópia do preço na compra; ≥ 0 |
| Subtotal | decimal | `decimal(18,2)` | `PrecoUnitario × Quantidade`; ≥ 0 |

A chave composta impede o mesmo produto duas vezes no pedido (FR-008). O item é criado a
partir do `Produto` (snapshot) e não muda depois.

## HistoricoStatusPedido (`Domain/Pedidos/HistoricoStatusPedido.cs` → tabela `HistoricosStatusPedido`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | long | `bigint` identity PK | define a ordem (R-06) |
| PedidoId | Guid | FK → `Pedidos.Id` | Cascade; `IX_HistoricosStatusPedido_PedidoId_Id` |
| StatusAnterior | StatusPedido? | `nvarchar(30)` nulo | nulo só na entrada de criação |
| StatusNovo | StatusPedido | `nvarchar(30)` | |
| OcorridoEm | DateTimeOffset | `datetimeoffset` | UTC |
| AutorId | Guid? | FK → `Usuarios.Id` | Restrict; nulo quando é um worker |
| Motivo | string? | `nvarchar(500)` | motivo de cancelamento do contrato v1 |

`CK_HistoricosStatusPedido_StatusValido` cobre `StatusAnterior` (quando não nulo) e
`StatusNovo`. A entrada é só de inclusão: a entidade não tem métodos de alteração.

## Pagamento (`Domain/Pagamentos/Pagamento.cs` → tabela `Pagamentos`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | Guid | PK | v7 |
| PedidoId | Guid | FK → `Pedidos.Id`, único (`IX_Pagamentos_PedidoId`) | Cascade; no máximo 1 por pedido |
| Valor | decimal | `decimal(18,2)` | ≥ 0 |
| Situacao | SituacaoPagamento | `nvarchar(30)` | `CK_Pagamentos_SituacaoValida`; `Pendente` na criação |
| IdentificadorTransacao | string? | `nvarchar(100)` | referência externa |
| ProcessadoEm | DateTimeOffset? | `datetimeoffset` | |
| CriadoEm, AtualizadoEm | DateTimeOffset | `datetimeoffset` | |

Comportamento: `RegistrarResultado(situacao, identificadorTransacao, instante)` preenche
`ProcessadoEm`.

## OrdemEntrega (`Domain/Entregas/OrdemEntrega.cs` → tabela `OrdensEntrega`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | Guid | PK | v7 |
| PedidoId | Guid | FK → `Pedidos.Id`, único (`IX_OrdensEntrega_PedidoId`) | Cascade; no máximo 1 por pedido |
| Transportadora | string? | `nvarchar(100)` | |
| CodigoRastreio | string? | `nvarchar(50)` | único por transportadora quando informado (índice filtrado) |
| DespachadoEm | DateTimeOffset? | `datetimeoffset` | |
| CriadoEm, AtualizadoEm | DateTimeOffset | `datetimeoffset` | |

Comportamento: `RegistrarDespacho(transportadora, codigoRastreio, instante)` exige os dois
textos e preenche `DespachadoEm`.

## Usuario (REQ-03, sem mudança de colunas)

Ganha só as relações de entrada: `Pedidos.ClienteId` e `HistoricosStatusPedido.AutorId`,
ambas com `Restrict`. A migration `EntidadesNegocio` não altera nenhuma tabela do Identity.

## Erro de concorrência (`Application/Comum/ConflitoConcorrenciaException.cs`)

Lançada pelo `ContextoBancoDados` no lugar de `DbUpdateConcurrencyException` (R-04). Traz:
- `Entidade`: o nome do tipo, por exemplo `Produto`;
- `Identificador` (`string`): a chave primária em texto. Numa chave composta, as partes vêm
  separadas por `/`, como em `ItensPedido` (`<PedidoId>/<ProdutoId>`).

O EF também lança conflito ao alterar ou apagar uma linha que outra operação já apagou, em
qualquer entidade. Por isso o identificador não pode supor `Guid`.

A tradução vale só para as entidades do Domain. As do Identity mantêm a
`DbUpdateConcurrencyException` original, que o `UserStore` converte em `IdentityResult`
(research R-04).

Ver [contracts/persistencia.md](./contracts/persistencia.md).
