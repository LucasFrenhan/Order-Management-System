# Contrato: uso da persistência pelos requisitos seguintes

**Requisitos**: FR-003, FR-009, FR-021, FR-023 | **Modelo**: [data-model.md](../data-model.md)

Este REQ não muda o contrato HTTP v1. Este documento fixa o que o REQ-05, o REQ-06 e os
workers podem esperar do armazenamento.

## Gravação

- **Unidade de gravação**: cada chamada a `SaveChangesAsync` do `ContextoBancoDados` é
  atômica. Um pedido com seus itens e a entrada de criação do histórico entra numa
  chamada só (FR-009). Uma mudança de status e sua entrada de histórico também (FR-023).
- **Instantes**: todo instante passado ao domínio tem deslocamento zero (UTC). O domínio
  recusa os demais com `ArgumentException`.
- **Valores monetários**: o domínio arredonda para 2 casas antes de gravar.

## Conflito de concorrência

| Situação | O que é lançado |
|----------|-----------------|
| `Produto` ou `Pedido` gravado a partir de uma leitura desatualizada | `PedidoFulfillment.Application.Comum.ConflitoConcorrenciaException` |

- **Propriedades**: `Entidade` (o nome do tipo, por exemplo `"Produto"` ou `"Pedido"`) e
  `Identificador` (`string`, a chave primária; numa chave composta, as partes vêm
  separadas por `/`). Alterar ou apagar uma linha que outra operação já apagou também gera
  esse conflito, em qualquer entidade de negócio.
- **Identity**: as entidades do Identity (`Usuario`, `Perfil` e as tabelas de ligação)
  continuam com o comportamento do REQ-03. O `UserManager` devolve
  `IdentityResult.Failed` com o código `ConcurrencyFailure`, sem lançar exceção.
- **Estado do contexto**: depois do conflito, nada da chamada foi gravado, e o contexto
  continua com as entidades rastreadas. Quem trata o conflito descarta o contexto (ou chama
  `ChangeTracker.Clear()`), relê e decide se tenta de novo.
- **O que não gera conflito**: itens, histórico, pagamento e ordem de entrega não têm
  versão própria. A alteração do pedido que os acompanha é que detecta o conflito.

## Violações de integridade

Violações de unicidade, check e chave estrangeira continuam chegando como
`DbUpdateException` do EF Core. A Infrastructure classifica essas violações com
`ErrosBancoDados.EhViolacaoUnicidade` e `ErrosBancoDados.EhViolacaoRestricao`. A tradução
para os códigos do contrato v1 (por exemplo, 409 e 422) é responsabilidade de cada
requisito.

## Nomes que outros requisitos podem citar

- **Tabelas**: `Produtos`, `Pedidos`, `ItensPedido`, `HistoricosStatusPedido`,
  `Pagamentos` e `OrdensEntrega`.
- **Restrições**: `IX_Pedidos_Numero` (o número do pedido é gerado pelo REQ-05, que trata
  a colisão) e `IX_Pagamentos_PedidoId` (um pagamento por pedido).
