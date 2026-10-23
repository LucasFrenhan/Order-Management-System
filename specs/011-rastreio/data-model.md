# Data Model: Rastreio, despacho e histórico na consulta (REQ-11)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Nenhuma tabela nova. Uma migration muda a collation das colunas de rastreio (R-06).

## Pedido (REQ-04, REQ-05, REQ-10)

| Membro | Tipo | Mudança |
|--------|------|---------|
| `MotivoPedidoDespachado` | `const string` | **Novo**: `Pedido despachado.` |
| `PodeRegistrarRastreio` | `bool` | **Novo**: status `ProntoParaEnvio` ou `Despachado` |
| `RegistrarRastreio(transportadora, codigoRastreio, autorId, instante)` | método, devolve `ResultadoRastreio` | **Novo** (R-05) |
| `Historico` | coleção | Passa a ser lida junto com o pedido (R-04) |

### Regras de `RegistrarRastreio`

| Status atual | Ordem | Dados | Efeito | Resultado |
|--------------|-------|-------|--------|-----------|
| `ProntoParaEnvio` | existe ou não | quaisquer válidos | cria a ordem se faltar; transportadora, código e `DespachadoEm = instante`; `→ Despachado` com `Pedido despachado.` e o autor | `Despachado` |
| `Despachado` | existe | exatamente iguais aos gravados (comparação ordinal, depois de aparar) | nada | `SemMudanca` |
| `Despachado` | existe ou não | diferentes (inclusive só na caixa), ou sem ordem | cria a ordem se faltar; atualiza transportadora e código; `DespachadoEm` mantido (ou `instante`, se nulo); `Pedido.AtualizadoEm = instante`; status e histórico sem mudança | `Corrigido` |
| outro | — | — | `InvalidOperationException` (o serviço responde 409 antes) | — |

Transportadora e código chegam já sem espaços nas pontas e dentro dos tamanhos (validados
na entrada e pelas guardas de `OrdemEntrega`).

## OrdemEntrega (REQ-04, REQ-10)

| Membro | Mudança |
|--------|---------|
| `RegistrarDespacho(transportadora, codigo, instante)` | Sem mudança: primeiro registro, com `DespachadoEm = instante` |
| `CorrigirRastreio(transportadora, codigo, instante)` | **Novo**: troca transportadora e código, `AtualizadoEm = instante`; `DespachadoEm` mantido, ou `instante` se nulo |
| `Transportadora`, `CodigoRastreio` | Collation `SQL_Latin1_General_CP1_CI_AS` (migration `RastreioSemCaixa`); o índice único filtrado continua |

## ResultadoRastreio (novo, Domain)

`enum ResultadoRastreio { Despachado, Corrigido, SemMudanca }`.

## Contratos HTTP (Application, `Contratos/Pedidos`)

| Tipo | Mudança |
|------|---------|
| `PedidoResponseDTO` | **+** `Historico` (`[Required]`, padrão `[]`) e `Entrega` (anulável) (R-02, R-03) |
| `HistoricoStatusPedidoResponseDTO` | **Novo**: `StatusAnterior?`, `StatusNovo`, `OcorridoEm`, `Motivo?` |
| `EntregaPedidoResponseDTO` | **Novo**: `Transportadora?`, `CodigoRastreio?`, `DespachadoEm?`, `CriadoEm` |
| `RegistrarRastreioRequestDTO` | **Novo**: `Transportadora` (1–100), `CodigoRastreio` (1–50) |
| `CodigosErro.PedidoRastreioDuplicado` | **Novo**: `PEDIDO_RASTREIO_DUPLICADO` |

## Aplicação

| Tipo | Mudança |
|------|---------|
| `IServicoPedidos.RegistrarRastreioAsync` | **Novo** (R-05, R-07) |
| `IArmazenamentoPedidos.RastreioEmUsoAsync(transportadora, codigo, pedidoId)` | **Novo** (R-06) |
| `RastreioDuplicadoException` | **Novo**: violação do índice único na gravação (R-06) |
| `MapeamentoPedido.ParaResposta` | Histórico ordenado e entrega (R-02) |
| `EventosPedidos` | **+** 4004 `RastreioRegistrado`, 4005 `RastreioDuplicado` (R-10) |
