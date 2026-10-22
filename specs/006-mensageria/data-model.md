# Data Model: Mensageria, publicação de eventos e DLQ (REQ-06)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

A migration nova, `MensageriaOutbox`, acrescenta duas tabelas. As entidades dos REQ-04 e
REQ-05 não mudam.

## EventoSaida (`Application/Mensageria/EventoSaida.cs` → tabela `EventosSaida`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Id | long | `bigint` identity, PK | ordem de registro e de publicação |
| IdEvento | Guid | `uniqueidentifier`, único (`IX_EventosSaida_IdEvento`) | o mesmo `idEvento` do envelope |
| Tipo | string | `nvarchar(100)` | por exemplo `pedido.criado` |
| Conteudo | string | `nvarchar(max)` | o envelope serializado |
| CriadoEm | DateTimeOffset | `datetimeoffset` | gravado junto com o pedido |
| EnviadoEm | DateTimeOffset? | `datetimeoffset` nulo | preenchido depois da confirmação do broker |
| Tentativas | int | `int` | falhas de publicação |
| ProximaTentativaEm | DateTimeOffset | `datetimeoffset` | igual a `CriadoEm` no início |
| UltimoErro | string? | `nvarchar(500)` | mensagem da última falha |

- **Índice** `IX_EventosSaida_Pendentes` (`ProximaTentativaEm`, `Id`), filtrado por
  `[EnviadoEm] IS NULL`.
- **Fábrica** `EventoSaida.De<T>(EnvelopeEvento<T>, instante)`: serializa com
  `SerializacaoEventos`.
- **Métodos**: `MarcarEnviado(instante)` e `RegistrarFalha(erro, instante)`. Este último
  incrementa `Tentativas`, corta `UltimoErro` em 500 caracteres e calcula
  `ProximaTentativaEm = instante + min(2^Tentativas s, 10 s)`.

## EventoProcessado (`Infrastructure/Mensageria/EventoProcessado.cs` → tabela `EventosProcessados`)

| Campo | Tipo C# | Coluna | Regras |
|-------|---------|--------|--------|
| Consumidor | string | `nvarchar(100)`, PK (1/2) | nome da fila do consumidor |
| IdEvento | Guid | `uniqueidentifier`, PK (2/2) | |
| ProcessadoEm | DateTimeOffset | `datetimeoffset` | |

## Contratos (Application/Contratos/Eventos)

| Tipo | Campos |
|------|--------|
| `EnvelopeEvento<T>` | `IdEvento`, `Tipo`, `Versao`, `OcorridoEm`, `IdCorrelacao`, `Dados` |
| `PedidoCriado` | `PedidoId`, `Numero`, `ClienteId`, `Itens` (`ItemPedidoCriado`: `ProdutoId`, `Quantidade`, `PrecoUnitario`), `ValorTotal`, `Moeda` |
| `EstoqueReservado` | `PedidoId`, `Itens` (`ItemEstoqueReservado`: `ProdutoId`, `Quantidade`) |
| `PagamentoAprovado` | `PedidoId`, `Valor`, `IdentificadorTransacao` |
| `PagamentoRecusado` | `PedidoId`, `Valor`, `Motivo` |
| `TiposEvento` | as constantes `PedidoCriado = "pedido.criado"` e as demais, `VersaoAtual = 1` e o mapa tipo → `Type` |

## Opções (`Mensageria`)

| Chave | Padrão | Validação |
|-------|--------|-----------|
| `IntervaloDespachoMs` | 1000 | 100 a 60000 |
| `TamanhoLote` | 50 | 1 a 500 |
| `IntervalosNovaTentativaMs` | `[1000, 2000, 4000]` | exatamente 3 valores positivos e crescentes |
| `PrefetchConsumidor` | 10 | 1 a 1000 |
