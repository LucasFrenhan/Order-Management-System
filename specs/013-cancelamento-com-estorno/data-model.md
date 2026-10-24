# Data Model: cancelamento depois do pagamento, com estorno (REQ-13)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Só as mudanças sobre o modelo dos REQ-04 a REQ-12. Nenhuma tabela nova.

## Pagamento (`Pagamentos`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `Situacao` | texto (enum) | Ganha `EstornoPendente` e `Estornado`; `CK_Pagamentos_SituacaoValida` recriada |
| `EstornoSolicitadoEm` | `datetimeoffset?` | Preenchido ao pedir o estorno; UTC |
| `IdentificadorEstorno` | `nvarchar(100)?` | Preenchido na confirmação; aparado, até 100 |
| `EstornadoEm` | `datetimeoffset?` | Preenchido na confirmação |
| `TentativasEstorno` | `int`, padrão 0 | Tentativas que falharam; `>= 0` |
| `UltimaFalhaEstornoEm` | `datetimeoffset?` | Última falha; nulo enquanto nenhuma tentativa falhou |

Índice novo: `IX_Pagamentos_EstornoPendente` (`UltimaFalhaEstornoEm`, `EstornoSolicitadoEm`),
filtrado por `[Situacao] = 'EstornoPendente'`.

### Ciclo de vida

```text
Pendente ─┬─▶ Aprovado ──(cancelamento)──▶ EstornoPendente ──(gateway confirma)──▶ Estornado
          ├─▶ Recusado                          │      ▲
          └─▶ NaoProcessado                     └──────┘ falha: TentativasEstorno + 1
```

- `Aprovado → EstornoPendente`: só com o pedido indo para `Cancelado` na mesma gravação
  (`Pedido.Cancelar`, ou `Pedido.RegistrarCobrancaAposCancelamento` no R-07).
- `EstornoPendente → Estornado`: só com o identificador do estorno do gateway.
- `Estornado`, `Recusado` e `NaoProcessado` são finais. `EstornoPendente` nunca expira.
- `EstornoPendente` e `Estornado` são internos: não aparecem na API nem nos eventos.

## Ordem de entrega (`OrdensEntrega`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `CanceladaEm` | `datetimeoffset?` | Preenchido no cancelamento do pedido; só se `DespachadoEm` é nulo |

Uma ordem cancelada não volta e não recebe despacho (o pedido está `Cancelado`, e o rastreio
já responde 409 para esse status).

## Pedido (`Pedidos`)

Nenhuma coluna nova. Os status e as transições não mudam. Métodos novos no agregado:

| Método | Efeito, numa gravação | Quem chama |
|--------|------------------------|------------|
| `Cancelar(autorId?, motivo, produtos, instante)` | Devolve o estoque reservado; pagamento `Aprovado` → `EstornoPendente`; ordem sem despacho → cancelada; status → `Cancelado`, com histórico. Recusa status final | API (`ServicoPedidos`) e reconciliação (esgotamento da entrega) |
| `RegistrarCobrancaAposCancelamento(transacao, instante)` | Pedido `Cancelado` sem pagamento ganha o pagamento `Aprovado` e já `EstornoPendente`; status não muda | Consumidor de pagamento (R-07) |
| `ConfirmarEstorno(identificadorEstorno, instante)` | Pagamento `EstornoPendente` → `Estornado` | `ServicoEstornos` |
| `RegistrarFalhaEstorno(instante)` | `TentativasEstorno + 1`, `UltimaFalhaEstornoEm` | `ServicoEstornos` |

Os quatro atualizam `AtualizadoEm`, para a versão do pedido mudar e dois gravadores
concorrentes se detectarem (concorrência otimista do REQ-04).

Motivo novo de histórico, sem autor:
`Não foi possível criar a ordem de entrega.` (esgotamento da etapa de entrega).

## Resposta da consulta (`EntregaPedidoResponseDTO`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `canceladaEm` | `string (date-time)` ou `null` | Novo, opcional; `null` enquanto a ordem não foi cancelada |

## Opções (`Estornos`)

Ver [research R-09](./research.md#r-09-configuração) e o
[contrato](./contracts/estorno-e-cancelamento.md#configuração).
