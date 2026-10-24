# Data Model: publicação garantida e reconciliação (REQ-12)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Uma migration só: `PagamentoNaoProcessado`, que recria a check constraint da situação do
pagamento. As tabelas e as colunas não mudam.

## Domínio

### SituacaoPagamento (alterada)

| Valor | Origem |
|---|---|
| `Pendente` (1) | REQ-04 |
| `Aprovado` | REQ-09 |
| `Recusado` | REQ-09 |
| `NaoProcessado` | **novo**: a cobrança se esgotou na reconciliação sem resposta válida do gateway (FR-012) |

Gravada como texto (`nvarchar`, `TamanhoEnum`). `CK_Pagamentos_SituacaoValida` passa a
aceitar o valor novo.

### Pagamento.RegistrarResultado (alterado)

Aceita `Aprovado`, `Recusado` e `NaoProcessado`; outro valor continua lançando
`ArgumentOutOfRangeException`. Com `NaoProcessado`, o identificador de transação é nulo, e
`ProcessadoEm` é o instante do esgotamento.

### Pedido.RegistrarPagamentoNaoProcessado (novo)

```text
void RegistrarPagamentoNaoProcessado(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
```

- **Pré-condição**: `AptoParaPagamento`, com todos os produtos dos itens presentes. Se não
  valer, lança `InvalidOperationException` sem alterar nada, como `RecusarPagamento`.
- **Efeito**, tudo junto:
  - cria o `Pagamento` com o `ValorTotal` e registra `NaoProcessado`;
  - devolve o estoque (`DevolverEstoque`, `EstoqueReservado = false`);
  - leva o pedido a `Cancelado`, sem autor, com o `motivo` aparado no histórico (até
    `TamanhoMaximoMotivo`);
  - atualiza `AtualizadoEm`.
- Depois disso, `AptoParaPagamento` e `AptoParaOrdemEntrega` são falsos: um evento
  `estoque.reservado` que chegue mais tarde é ignorado pelo consumidor.

O esgotamento na etapa de estoque não precisa de método novo: usa
`MudarStatus(Cancelado, null, motivo, instante)`, depois de conferir a transição em
`TransicoesStatusPedido.PermitidaParaOperacao`, como `ReservaEstoque.CancelarSemEstoqueAsync`.

## Application

### EtapaFluxoPedido (novo)

`Estoque`, `Pagamento`, `Entrega`.

### IEtapaFluxoPedido (novo)

| Membro | Descrição |
|---|---|
| `Etapa` | A etapa da regra |
| `AplicarAsync(idOrigem, pedidoId, ct) → bool` | Aplica a regra do consumidor: `true` se gravou o efeito, `false` se o pedido não estava apto, exceção se falhou |

Implementada por `ReservaEstoque`, `ProcessamentoPagamento` e `CriacaoOrdemEntrega`, sem
mudar o comportamento do consumo (research R-04).

### PedidoParado (novo, record)

| Campo | Tipo |
|---|---|
| `PedidoId` | `Guid` |
| `Etapa` | `EtapaFluxoPedido` |
| `AtualizadoEm` | `DateTimeOffset` |

### IConsultaPedidosParados (nova porta)

`ListarAsync(paradoAntesDe, quantidade, comFalha, ct)`: primeiro os pedidos parados fora de
`comFalha`, depois os de dentro, cada grupo ordenado por `AtualizadoEm` crescente, até
`quantidade` no total (research R-05).

### RegistroFalhasReconciliacao (novo, singleton, em memória)

| Operação | Efeito |
|---|---|
| `RegistrarFalha(pedidoId, agora) → int` | Incrementa e devolve o número de ciclos com falha |
| `Remover(pedidoId)` | Apaga a entrada (efeito gravado, pedido não mais parado ou esgotado) |
| `Podar(antesDe)` | Apaga as entradas com a última falha anterior a `antesDe` |
| `ComFalha` | Os identificadores com entrada |

Não é persistido; um reinício do Worker zera a contagem.

### ServicoReconciliacao (novo)

`ReconciliarAsync(PedidoParado, agora, ParametrosEsgotamento, ct) → ResultadoReconciliacao`,
com `ParametrosEsgotamento(LimiteEsgotamento, CiclosFalha)`, para o Application não depender
das opções do Infrastructure. O esgotamento usa o mesmo armazenamento do escopo, depois de
`DescartarAlteracoes()` (research R-07):

| Resultado | Quando |
|---|---|
| `Recuperado` | A regra gravou o efeito |
| `NaoMaisParado` | A regra devolveu `false`, ou o esgotamento encontrou o pedido fora da etapa |
| `Falhou` | A regra lançou exceção, e o pedido ainda não se esgota |
| `Esgotado` | A regra lançou exceção, a contagem chegou ao mínimo, o pedido está parado há mais que o limite de esgotamento e a etapa é estoque ou pagamento; o pedido foi cancelado |

Motivos de histórico, como constantes:
- `MotivoEstoqueNaoReservado = "Não foi possível reservar o estoque."`;
- `MotivoPagamentoNaoProcessado = "Não foi possível processar o pagamento."`.

## Infrastructure

### OpcoesReconciliacao (nova seção `Reconciliacao`)

Ver a tabela de chaves, padrões e faixas no research R-10. O validador acumula as falhas,
como `ValidadorOpcoesMensageria`, e exige `LimiteEsgotamentoSegundos > LimiteParadoSegundos`.

### TopologiaConhecida (novo)

Montada a partir dos `IDescritorConsumidor`:
- `Consumidores`: lista de (nome, tipos);
- `ExigeRota(tipo)`: verdadeiro se algum consumidor conhecido recebe o tipo.

Com os consumidores atuais, exigem rota `pedido.criado`, `estoque.reservado` e
`pagamento.aprovado`. Não exigem `estoque.insuficiente`, `pagamento.recusado` e
`entrega.criada`.

### EventoSaida (sem mudança de esquema)

`UltimoErro` passa a poder conter `SemRota: <texto do broker>`. A espera, as tentativas e a
ordem seguem o REQ-06.

## Estados do pedido na reconciliação

```text
parado (AtualizadoEm < agora - 10 min) ──regra ok──────────────▶ avança (como o consumidor)
   │
   ├─regra devolve false──────────────────────────────────────▶ intocado (não estava mais apto)
   │
   └─regra falha──▶ falhas += 1
                      ├─ falhas >= 3 e AtualizadoEm <= agora - 24 h e etapa estoque ▶ Cancelado ("Não foi possível reservar o estoque.")
                      ├─ falhas >= 3 e AtualizadoEm <= agora - 24 h e etapa pagamento Pagamento NaoProcessado + estoque devolvido + Cancelado
                      └─ senão (inclusive a etapa de entrega) ────────────────────────▶ tenta de novo no próximo ciclo, no fim da fila
```
