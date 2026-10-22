# Research: Reserva de estoque assíncrona com concorrência otimista (REQ-08)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Onde fica o consumidor de estoque

- **Decisão**: `ReservaEstoque`, em `src/PedidoFulfillment.Application/Estoque/`, implementa
  `IManipuladorEvento<PedidoCriado>` com `Consumidor = "estoque"` e
  `Tipos = ["pedido.criado"]`. A base do REQ-06 cria `oms.estoque`, as filas de espera e
  `oms.estoque.dlq`. O Worker registra o consumidor em `ProgramaWorker` com
  `AdicionarConsumidor<ReservaEstoque, PedidoCriado>()` (FR-001).
- **Justificativa**: é o caminho descrito em `specs/006-mensageria/contracts/topologia-e-consumo.md`.
  O manipulador só depende de portas do Application (`IArmazenamentoPedidos`,
  `TimeProvider`, `ILogger`), já registradas pela Infrastructure no Worker.
- **Alternativas**: um projeto ou serviço próprio de estoque. Rejeitada: o estoque usa as
  mesmas tabelas e a mesma transação do pedido, e o REQ-06 já fez do Worker o hospedeiro de
  todos os consumidores.

## R-02. Regra de reserva e devolução no domínio

- **Decisão**: a regra fica em `Pedido`, que já protege a consistência dos próprios itens:
  - `bool EstoqueReservado` (novo, só leitura fora da entidade);
  - `IReadOnlyList<FaltaEstoque> ReservarEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`:
    confere todos os itens (FR-004). Se algum falta, devolve as faltas e não altera nada
    (tudo ou nada). Senão, chama `Produto.AjustarEstoque(-quantidade)` em cada um, marca
    `EstoqueReservado = true` e devolve lista vazia (FR-005);
  - `void DevolverEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`:
    só com `EstoqueReservado`; soma a quantidade de cada item ao produto, ativo ou não, e
    desmarca a reserva (FR-010);
  - `FaltaEstoque` (record do Domain): `ProdutoId`, `QuantidadeSolicitada`,
    `QuantidadeDisponivel` e `Inativo`.
- **Justificativa**: é regra de negócio com teste unitário obrigatório (Princípio V), e
  tanto o worker quanto o `ServicoPedidos` usam a mesma regra. `Produto.AjustarEstoque` já
  impede estoque negativo, e a conferência prévia garante que ele nunca chega a lançar na
  reserva.
- **Alternativas**: um serviço de domínio separado. Rejeitada: a reserva altera o próprio
  pedido (`EstoqueReservado`), e os itens com as quantidades já estão nele.

## R-03. Concorrência otimista e reavaliação

- **Decisão**: o manipulador repete o ciclo "ler, decidir, gravar" até **3 vezes**, como o
  `ServicoPedidos.AtualizarStatusAsync` do REQ-05 (FR-008):
  1. `ObterPedidoAsync(id, paraAlterar: true)` e `ObterProdutosAsync(ids, paraAlterar: true)`;
  2. decide (R-04), aplica no domínio e grava o `EventoSaida`;
  3. `SalvarAsync`. Com `ConflitoConcorrenciaException` (versão de `Produto` ou de
     `Pedido`), chama `DescartarAlteracoes()` e volta ao passo 1, com o estado atual.

  Esgotadas as 3 vezes, relança o conflito. A base desfaz a transação e aplica as novas
  tentativas do REQ-06 (1 + 3) e, por fim, a DLQ (FR-009).
- **Dentro da transação da base**: a base abre a transação antes do manipulador e grava o
  registro de processado depois dele. Com transação aberta, o EF Core cria um savepoint a
  cada `SaveChanges` e, se ele falha, volta ao savepoint: o que a tentativa gravou some, e
  a transação continua utilizável para a próxima. `DescartarAlteracoes()` limpa o
  rastreamento; o registro de processado é adicionado pela base só depois do manipulador.
- **Ordem das atualizações**: o EF Core ordena os comandos de um `SaveChanges` por tabela e
  chave, então reservas concorrentes atualizam os produtos na mesma ordem, o que evita
  ciclo de bloqueio entre elas. O que restar de deadlock (o SQL Server desfaz a transação
  inteira da vítima) não pode ser reavaliado na mesma transação: propaga para a base, que
  tenta de novo com transação nova (R-07).
- **Justificativa**: atende ao título da issue (concorrência otimista, tratando o conflito)
  sem bloqueio pessimista. O teste de disputa (SC-002) usa reservas em paralelo com escopos
  e transações próprios, como a base faz.
- **Alternativas**:
  - `UPDATE ... WHERE QuantidadeEstoque >= @q` atômico, sem versão. Rejeitada: contorna a
    concorrência otimista pedida na issue e espalharia a regra de disponibilidade no SQL.
  - Sem reavaliação local, deixando tudo para as novas tentativas da base. Rejeitada:
    cada conflito custaria de 1 s a 4 s de espera e chegaria à DLQ numa disputa intensa.

## R-04. O que o worker faz com cada pedido

| Situação ao ler o pedido | Ação | Evento |
|--------------------------|------|--------|
| Não existe | lança exceção → novas tentativas e DLQ (FR-003) | — |
| `Cancelado`, `Despachado` ou `PagamentoRejeitado` | nada; log `PedidoIgnoradoNaReserva` (FR-002) | — |
| `EstoqueReservado` já marcado | nada; log `PedidoIgnoradoNaReserva` | — |
| `Pendente`, `Processando` ou `ProntoParaEnvio`, todos os itens disponíveis | reserva; status não muda (FR-005; clarify 2) | `estoque.reservado` |
| Idem, com algum item indisponível | `MudarStatus(Cancelado, autor null, "Estoque insuficiente.")`; nenhum estoque muda (FR-006) | `estoque.insuficiente` |

- A segunda linha de "nada" protege contra o mesmo pedido chegar em dois eventos
  diferentes; a reentrega do mesmo evento já é descartada pela base (`EventosProcessados`).
- O cancelamento respeita `TransicoesStatusPedido.PermitidaParaOperacao`, que permite
  `Cancelado` a partir dos três status não finais.
- Os itens do evento vêm do pedido gravado, não do conteúdo de `pedido.criado` (FR-003): o
  evento só identifica o pedido.

## R-05. Devolução no cancelamento pela API

- **Decisão**: em `ServicoPedidos.AtualizarStatusAsync`, quando o novo status é `Cancelado`
  e o pedido tem `EstoqueReservado`, o serviço lê os produtos dos itens com
  `paraAlterar: true` e chama `pedido.DevolverEstoque` antes do `SalvarAsync`. Tudo vai na
  mesma gravação da mudança de status (FR-010).
- **Corrida com o worker** (FR-011): pedido e produtos têm versão. O laço que já existe
  (`TentativasStatus = 3`) relê pedido e produtos depois de um conflito:
  - se o worker reservou antes, o cancelamento relido vê `EstoqueReservado` e devolve;
  - se o cancelamento gravou antes, o worker relido vê `Cancelado` e ignora (R-04).
- **Esgotado**: o serviço responde a falha que já existe para isso (409,
  `PEDIDO_TRANSICAO_INVALIDA`, "alterado por outra operação"). O contrato HTTP v1 não muda
  (FR-016).
- **Alternativas**: devolver por evento (a API publica e o worker devolve). Rejeitada no
  clarify: a devolução ficaria atrasada e precisaria de um contrato novo de cancelamento.

## R-06. Contrato `estoque.insuficiente` v1

- **Decisão**: `EstoqueInsuficiente` em `Contratos/Eventos/Eventos.cs`, com
  `ItemEstoqueInsuficiente`:
  - `pedidoId` (uuid);
  - `itens[]` (mínimo 1), com `produtoId` (uuid), `quantidadeSolicitada` (inteiro ≥ 1),
    `quantidadeDisponivel` (inteiro ≥ 0, o estoque lido naquele momento) e `inativo`
    (booleano).
- **Catálogo**: `TiposEvento.EstoqueInsuficiente = "estoque.insuficiente"`, no mapa de
  tipos. Nome no passado, como pede a constituição.
- **Schema**: `specs/008-estoque/contracts/eventos/estoque.insuficiente.v1.schema.json`,
  com o mesmo envelope dos outros (FR-013). O `.csproj` dos testes de integração passa a
  copiar também essa pasta para `Eventos/`, e `SchemaEventosTestes` cobre o tipo novo, já
  que percorre todo o `TiposEvento`.
- **`estoque.reservado`** do REQ-06 é usado sem mudanças (FR-014). Os itens vão ordenados
  por `produtoId`, como em `pedido.criado`.

## R-07. Mudanças na porta `IArmazenamentoPedidos`

- `ObterProdutosAsync(ids, bool paraAlterar, ct)`: o parâmetro novo segue o padrão de
  `ObterPedidoAsync`. A criação continua chamando com `false`.
- `Adicionar(EventoSaida evento)`: grava um evento do outbox sozinho, para a reserva e o
  cancelamento por falta.
- `SalvarAsync`, tratamento de deadlock:
  - com chave de idempotência sendo inserida: `ChaveIdempotenciaRegistradaException`, como
    hoje;
  - sem transação aberta (cancelamento pela API): `ConflitoConcorrenciaException`, para o
    laço do serviço reler;
  - com transação aberta (worker): propaga sem conversão. O SQL Server já desfez a
    transação inteira, e só a base pode tentar de novo.
- **Alternativa**: uma porta nova `IArmazenamentoEstoque`. Rejeitada: duplicaria leitura
  de pedido, gravação e descarte, e a devolução no `ServicoPedidos` precisaria das duas.

## R-08. Persistência

- Coluna `Pedidos.EstoqueReservado` (`bit`, não nula, padrão `0`), na migration
  `ReservaEstoque`. Pedidos que já existem ficam com `0`, o que é verdade: nenhum foi
  reservado antes deste requisito.
- Nenhuma tabela nova. `Produto` e `Pedido` já têm a versão de concorrência (REQ-04).
- Os testes de modelo sincronizado e de migrations passam a esperar a 5ª migration.

## R-09. Observabilidade

`EventosEstoque` (Application, `LoggerMessage`), faixa 6000:

| EventId | Nome | Nível | Campos |
|---------|------|-------|--------|
| 6001 | `EstoqueReservado` | Information | idEvento, pedidoId, itens |
| 6002 | `PedidoCanceladoSemEstoque` | Information | idEvento, pedidoId, itens em falta |
| 6003 | `PedidoIgnoradoNaReserva` | Information | idEvento, pedidoId, status, já reservado |
| 6004 | `ConflitoNaReserva` | Warning | idEvento, pedidoId, reavaliação (1 a 3) |
| 6005 | `EstoqueDevolvido` | Information | pedidoId, itens |

Só identificadores e quantidades, sem dados pessoais (FR-015).

## R-10. Testes

- **Unitários**:
  - `Pedido.ReservarEstoque` e `DevolverEstoque`: tudo ou nada, quantidade igual ao
    estoque, inativo, reserva repetida, devolução sem reserva;
  - `ReservaEstoque` com o armazenamento falso (`Falsos.cs`): cada linha de R-04, conflito
    seguido de sucesso e conflito nas 3 vezes (relança);
  - `ServicoPedidos.AtualizarStatusAsync`: cancelamento com e sem reserva;
  - serialização e ida e volta de `EstoqueInsuficiente`.
- **Integração** (SQL Server e RabbitMQ reais, Testcontainers):
  - `ReservaEstoqueTestes`, pelo Worker (`FabricaWorker`): US1, US2, US4 e reentrega, com
    fila de teste ligada a `estoque.*`;
  - `DisputaEstoqueTestes`: SC-002 com `CAT-008` (N = 1, M = 2) e com um produto de N = 5 e
    M = 8, rodando o manipulador em paralelo, cada um no seu escopo e transação;
  - `DevolucaoEstoqueTestes`, pela API: US5;
  - `SchemaEventosTestes`: o tipo novo, automaticamente;
  - modelo sincronizado e migrations.
- **Alternativa**: a disputa com dois Workers consumindo a mesma fila. Rejeitada como teste
  principal: o cliente processa um evento por vez por canal, e a intercalação ficaria por
  conta do acaso. Rodar o manipulador em paralelo força a disputa de forma reproduzível.
