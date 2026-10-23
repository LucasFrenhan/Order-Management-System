# Data Model: Ordem de entrega assíncrona (REQ-10)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Nenhuma tabela ou coluna nova (R-10). Muda o comportamento de entidades que já existem.

## Pedido (REQ-04, REQ-08, REQ-09)

| Membro | Tipo | Mudança |
|--------|------|---------|
| `OrdemEntrega` | `OrdemEntrega?` | Passa a ser preenchida pelo consumidor; lida junto com o pedido (R-05) |
| `AptoParaOrdemEntrega` | `bool` (calculado, não gravado) | **Novo**: `EstoqueReservado && Pagamento?.Situacao == Aprovado && OrdemEntrega is null && Status is Processando or ProntoParaEnvio` |
| `CriarOrdemEntrega(instante)` | método, devolve `OrdemEntrega` | **Novo** (R-02) |
| `MotivoOrdemEntregaCriada` | `const string` | **Novo**: `Ordem de entrega criada.` |

### Regras

- `CriarOrdemEntrega` lança `InvalidOperationException` se o pedido não está
  `AptoParaOrdemEntrega`.
- `CriarOrdemEntrega`:
  - `OrdemEntrega` nova com `PedidoId = Id`, `CriadoEm = AtualizadoEm = instante`, sem
    transportadora, código de rastreio e data de despacho;
  - `Processando → ProntoParaEnvio`, histórico sem autor com `Ordem de entrega criada.`;
  - `ProntoParaEnvio`: status e histórico sem mudança, `AtualizadoEm = instante` (a versão
    do pedido muda, R-04);
  - estoque, reserva e pagamento sem mudança (FR-006).

### Transições de status feitas pelo fulfillment

```text
Processando ──────── ordem criada ──▶ ProntoParaEnvio
ProntoParaEnvio ──── ordem criada ──▶ (sem mudança)
Pendente, Cancelado, Despachado, PagamentoRejeitado ──▶ ignorado (FR-002)
Qualquer status com ordem existente, sem pagamento aprovado ou sem reserva ──▶ ignorado
```

A tabela de transições da API (`TransicoesStatusPedido`) não muda (R-03).

## OrdemEntrega (REQ-04)

Sem mudança de estrutura. No máximo uma por pedido (índice único em `PedidoId`).

| Campo | Na criação (REQ-10) | No despacho (issue #11) |
|-------|---------------------|-------------------------|
| `Id` | GUID v7 | — |
| `PedidoId` | pedido | — |
| `Transportadora` | `null` | preenchida |
| `CodigoRastreio` | `null` | preenchido |
| `DespachadoEm` | `null` | preenchido |
| `CriadoEm` / `AtualizadoEm` | instante do processamento | `AtualizadoEm` muda |

## Evento `entrega.criada` v1 (novo)

Envelope do REQ-06. Conteúdo (`dados`):

| Campo | Tipo | Origem |
|-------|------|--------|
| `pedidoId` | uuid | `Pedido.Id` |
| `numero` | texto, `PED-AAAAMMDD-NNNNNN` | `Pedido.Numero` |
| `ordemEntregaId` | uuid | `OrdemEntrega.Id` |
| `itens[]` | ao menos 1 | `Pedido.Itens` |
| `itens[].produtoId` | uuid | `ItemPedido.ProdutoId` |
| `itens[].quantidade` | inteiro ≥ 1 | `ItemPedido.Quantidade` |

Schema: [entrega.criada.v1.schema.json](./contracts/eventos/entrega.criada.v1.schema.json).

## Aplicação (novos tipos)

| Tipo | Projeto | Papel |
|------|---------|-------|
| `EntregaCriada`, `ItemEntregaCriada` | Application (`Contratos/Eventos`) | Contrato v1 (R-07) |
| `CriacaoOrdemEntrega` | Application (`Entregas/`) | Consumidor de `pagamento.aprovado` (R-01) |
| `EventosEntrega` | Application (`Entregas/`) | Logs 8001 a 8003 (R-08) |
