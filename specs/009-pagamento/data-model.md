# Data Model: Pagamento assíncrono simulado (REQ-09)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Nenhuma tabela ou coluna nova (R-11). Muda o comportamento de entidades que já existem.

## Pedido (REQ-04, REQ-08)

| Membro | Tipo | Mudança |
|--------|------|---------|
| `Pagamento` | `Pagamento?` | Passa a ser preenchido pelo consumidor; lido junto com o pedido (R-06) |
| `AptoParaPagamento` | `bool` (calculado, não gravado) | **Novo**: `EstoqueReservado && Pagamento is null && Status is Pendente or Processando or ProntoParaEnvio` |
| `AprovarPagamento(identificadorTransacao, instante)` | método | **Novo** (R-03) |
| `RecusarPagamento(motivo, produtos, instante)` | método | **Novo** (R-03) |
| `MotivoPagamentoAprovado` | `const string` | **Novo**: `Pagamento aprovado.` |

### Regras

- `AprovarPagamento` e `RecusarPagamento` lançam `InvalidOperationException` se o pedido
  não está `AptoParaPagamento`.
- Valor do pagamento: `ValorTotal` do pedido (FR-006).
- `AprovarPagamento`:
  - `Pagamento` novo, `Aprovado`, com `IdentificadorTransacao` (obrigatório, até 100
    caracteres) e `ProcessadoEm = instante`;
  - `Pendente → Processando`, histórico sem autor com `Pagamento aprovado.`;
  - `Processando` e `ProntoParaEnvio`: status e histórico sem mudança (clarify);
  - `EstoqueReservado` continua `true` (a mercadoria segue para o envio).
- `RecusarPagamento`:
  - `Pagamento` novo, `Recusado`, sem identificador de transação, com
    `ProcessadoEm = instante`;
  - `DevolverEstoque(produtos, instante)`: cada produto recebe a quantidade do item, mesmo
    inativo; `EstoqueReservado = false`;
  - status atual → `PagamentoRejeitado`, histórico sem autor com o `motivo` (obrigatório,
    até 500 caracteres, o limite do contrato `pagamento.recusado`).

### Transições de status feitas pelo pagamento

```text
Pendente ─────────── aprovado ──▶ Processando
Pendente ─────────── recusado ──▶ PagamentoRejeitado (final)
Processando ──────── aprovado ──▶ (sem mudança)
Processando ──────── recusado ──▶ PagamentoRejeitado
ProntoParaEnvio ──── aprovado ──▶ (sem mudança)
ProntoParaEnvio ──── recusado ──▶ PagamentoRejeitado
Cancelado, Despachado, PagamentoRejeitado ──▶ ignorado (FR-002)
```

A tabela de transições da API (`TransicoesStatusPedido`) não muda (R-04).

## Pagamento (REQ-04)

Sem mudança de estrutura. No máximo um por pedido (índice único em `PedidoId`). O
consumidor cria o pagamento já com o resultado; `Pendente` não é gravado por este REQ.

| Campo | Aprovado | Recusado |
|-------|----------|----------|
| `Valor` | `Pedido.ValorTotal` | `Pedido.ValorTotal` |
| `Situacao` | `Aprovado` | `Recusado` |
| `IdentificadorTransacao` | `SIM-` + 32 hexadecimais | `null` |
| `ProcessadoEm` | instante do processamento | instante do processamento |

## Produto (REQ-04)

Só na recusa: `QuantidadeEstoque` aumenta a quantidade do item (`AjustarEstoque`), com a
versão de concorrência atualizada.

## Configuração (nova)

| Chave | Tipo | Padrão | Regra |
|-------|------|--------|-------|
| `GatewayPagamento:TaxaAprovacaoPercentual` | número | 90 | de 0 a 100; fora disso o Worker não sobe (FR-005) |

## Aplicação (novos tipos)

| Tipo | Projeto | Papel |
|------|---------|-------|
| `IGatewayPagamento` | Application | Porta da cobrança (R-02) |
| `ResultadoCobranca` | Application | `Aprovado`, `IdentificadorTransacao?`, `Motivo?` |
| `ProcessamentoPagamento` | Application | Consumidor de `estoque.reservado` (R-01) |
| `EventosPagamento` | Application | Logs 7001 a 7004 (R-08) |
| `GatewayPagamentoSimulado` | Infrastructure | Sorteio com a taxa configurada (R-02) |
| `OpcoesGatewayPagamento`, `ValidadorOpcoesGatewayPagamento` | Infrastructure | Configuração e validação na partida |
