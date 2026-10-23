# Research: Pagamento assíncrono simulado com compensação do estoque (REQ-09)

**Data**: 2026-10-23 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Onde fica o consumidor de pagamento

- **Decisão**: `ProcessamentoPagamento`, em `src/PedidoFulfillment.Application/Pagamentos/`,
  implementa `IManipuladorEvento<EstoqueReservado>` com `Consumidor = "pagamento"` e
  `Tipos = ["estoque.reservado"]`. A base do REQ-06 cria `oms.pagamento`, as filas de espera
  e `oms.pagamento.dlq`. O Worker o registra com
  `AdicionarConsumidor<ProcessamentoPagamento, EstoqueReservado>()` (FR-001).
- **Justificativa**: é o mesmo caminho do consumidor de estoque (REQ-08, R-01) e o previsto
  em `specs/006-mensageria/contracts/topologia-e-consumo.md`. O manipulador só depende de
  portas do Application: `IArmazenamentoPedidos`, `IGatewayPagamento` (R-02), `TimeProvider`
  e `ILogger`.
- **Alternativas**: um projeto ou processo próprio de pagamento. Rejeitada: o pagamento
  grava nas mesmas tabelas e na mesma transação do pedido e do estoque (compensação na
  mesma gravação, clarify).

## R-02. Gateway simulado e resultado controlável

- **Decisão**:
  - porta `IGatewayPagamento` no Application (`Pagamentos/`), com
    `Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken)`;
  - `ResultadoCobranca` (record do Application): `Aprovado` (bool),
    `IdentificadorTransacao` (só na aprovação) e `Motivo` (só na recusa), com as fábricas
    `Aprovada(transacao)` e `Recusada(motivo)`;
  - implementação `GatewayPagamentoSimulado` na Infrastructure (`Pagamentos/`). Aprova
    quando `aleatorio.NextDouble() * 100 < TaxaAprovacaoPercentual`. Na aprovação, o
    identificador é `SIM-` + `Guid.CreateVersion7()` em 32 hexadecimais (36 caracteres,
    dentro do limite de 100, FR-007). Na recusa, o motivo é
    `Pagamento recusado pelo emissor.`;
  - o construtor público recebe `IOptions<OpcoesGatewayPagamento>` e usa `Random.Shared`;
    um construtor `internal` recebe também um `Random` com semente, para o teste
    estatístico (SC-002) ser determinístico;
  - `OpcoesGatewayPagamento`, seção `GatewayPagamento`, com
    `TaxaAprovacaoPercentual` (`double`, padrão 90). O `ValidadorOpcoesGatewayPagamento`
    exige 0 a 100 e nomeia a chave sem mostrar o valor, como os validadores do REQ-06
    (FR-005). `ValidateOnStart` impede o Worker de subir.
  - registro por `AdicionarGatewayPagamentoSimulado()` em
    `src/PedidoFulfillment.Infrastructure/Pagamentos/ExtensoesPagamento.cs`, chamado só pelo
    Worker.
- **Justificativa**: a porta separa a decisão (sorte) da regra (o que fazer com o
  resultado), e os testes fixam o resultado sem truques: taxa 100 ou 0 na configuração,
  nos testes de integração (FR-016), e um gateway falso nos unitários. A operação é
  assíncrona para que um gateway real substitua a simulação sem mudar o consumidor.
- **Alternativas**:
  - `Random` direto no consumidor. Rejeitada: os testes ficariam dependentes de sorte;
  - decidir pelo identificador do pedido (hash), para ser reprodutível. Rejeitada: a
    proporção fica viciada pela forma dos GUIDs v7, e a issue pede sorteio;
  - taxa em fração (0 a 1). Rejeitada: a issue e a spec falam em porcentagem.

## R-03. Regra do pagamento no domínio

- **Decisão**: em `Pedido`, ao lado da reserva do REQ-08:
  - `void AprovarPagamento(string identificadorTransacao, DateTimeOffset instante)`:
    cria o `Pagamento` com `ValorTotal`, registra `Aprovado` com o identificador e, se o
    status é `Pendente`, muda para `Processando` com o motivo `Pagamento aprovado.` e sem
    autor. Em `Processando` ou `ProntoParaEnvio`, o status não muda (FR-008, clarify);
  - `void RecusarPagamento(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`:
    cria o `Pagamento` com `ValorTotal`, registra `Recusado`, chama `DevolverEstoque` e muda
    para `PagamentoRejeitado` com o motivo e sem autor (FR-009);
  - os dois exigem um pedido apto: estoque reservado, sem `Pagamento` e em `Pendente`,
    `Processando` ou `ProntoParaEnvio`. Fora disso, `InvalidOperationException`;
  - `bool AptoParaPagamento` (só leitura) expõe a mesma condição ao consumidor, para a
    decisão de ignorar (FR-002) e a guarda usarem a mesma regra;
  - `MotivoPagamentoAprovado = "Pagamento aprovado."` fica como constante do domínio.
- **Justificativa**: o pagamento, a mudança de status e a devolução precisam acontecer
  juntos e com as mesmas guardas; no domínio, ficam testáveis sem banco (Princípio V). O
  `Pagamento` já tem `RegistrarResultado` desde o REQ-04, e `Pedido.Pagamento` já é a
  navegação 1:1 mapeada, com índice único em `PedidoId`.
- **Alternativas**: montar o `Pagamento` no consumidor e só mudar o status no pedido.
  Rejeitada: espalharia a regra "um pagamento por pedido" e a ordem das operações.

## R-04. Transições de status feitas pelo sistema

- **Decisão**: `Pendente → Processando` e `Pendente/Processando/ProntoParaEnvio →
  PagamentoRejeitado` são feitas por `Pedido.AprovarPagamento` e `Pedido.RecusarPagamento`,
  sem passar por `TransicoesStatusPedido`, que continua sendo a regra da API (REQ-05). Na
  API, `PagamentoRejeitado` segue sem transição de saída nem de entrada (FR-010).
- **Justificativa**: `TransicoesStatusPedido` descreve quem pode mudar o status pela API.
  O pagamento é mudança do sistema, com histórico sem autor, como o cancelamento por falta
  de estoque do REQ-08 (que só reaproveitou uma transição já permitida).
- **Alternativas**: acrescentar as transições à tabela da API. Rejeitada: permitiria a
  operação marcar `PagamentoRejeitado` à mão, o que o contrato v1 não prevê.

## R-05. Decisão uma vez, reavaliação com o estado atual

- **Decisão**: o consumidor repete "ler, decidir, gravar" até 3 vezes em
  `ConflitoConcorrenciaException`, como `ReservaEstoque` (REQ-08, R-03), com
  `DescartarAlteracoes` e o savepoint do EF Core dentro da transação da base. O gateway é
  chamado uma única vez por processamento, na primeira leitura em que o pedido está apto, e
  o resultado é reaproveitado nas reavaliações. Em cada reavaliação, o pedido é relido; se
  deixou de estar apto (por exemplo, foi cancelado pela API), a mensagem é confirmada sem
  gravar (FR-002, US4 cenário 5). Esgotadas as reavaliações, o conflito propaga para as
  novas tentativas da base e, no fim, a DLQ (FR-012).
- **Justificativa**: chamar o gateway de novo a cada conflito faria uma mesma mensagem
  sortear resultados diferentes, e, com um gateway real, cobrar duas vezes. A reentrega
  depois de uma falha técnica faz nova cobrança simulada, o que é aceitável só porque a
  simulação não tem efeito externo (Edge Cases).
- **Alternativas**: chave de idempotência na cobrança. Adiada: só faz sentido com um
  gateway real.

## R-06. Leitura do pedido com o pagamento e dos produtos só na recusa

- **Decisão**:
  - `ArmazenamentoPedidos.ObterPedidoAsync` passa a incluir `Pagamento`, para o consumidor
    saber se o pedido já foi pago (FR-002). A API não expõe o pagamento, então o contrato
    HTTP não muda;
  - os produtos são lidos para alteração (`ObterProdutosAsync(..., paraAlterar: true)`) só
    quando a cobrança é recusada, para a devolução. Na aprovação, nenhum produto é tocado;
  - o `Pagamento` novo, ligado ao pedido rastreado, é inserido pelo EF Core no mesmo
    `SaveChanges` (chave `ValueGeneratedNever`; entidade nova pela navegação).
- **Justificativa**: menos linhas bloqueadas e menos chance de conflito na aprovação, que é
  o caso de 90%. A versão do pedido (`rowversion`, REQ-04) muda nas duas saídas, e é ela que
  resolve a corrida com a API (R-07).
- **Alternativas**: um método novo no armazenamento só para o pagamento. Rejeitada:
  `Include` a mais na mesma consulta é mais simples e não muda a interface.

## R-07. Corrida com o cancelamento e o PATCH pela API

- **Decisão**: nada novo na API. O `ServicoPedidos` já relê e reavalia em conflito de
  versão do pedido (REQ-05; devolução no REQ-08), e o consumidor faz o mesmo (R-05):
  - cancelamento antes: o estoque volta pela API, e o consumidor encontra o pedido
    `Cancelado`, sem reserva, e o ignora;
  - pagamento antes: aprovado, o pedido fica `Processando`, e um cancelamento posterior da
    operação devolve o estoque (REQ-08), sem estorno (clarify); recusado, o pedido fica
    `PagamentoRejeitado`, e a API recusa o cancelamento (status final);
  - o Cliente só cancela em `Pendente`: depois de aprovado, recebe a resposta de transição
    inválida que já existe.
- **Justificativa**: a versão do pedido serializa as duas gravações, e os dois lados
  conferem o estado relido antes de agir, o que impede estoque devolvido duas vezes
  (FR-011).
- **Alternativas**: bloqueio pessimista do pedido. Rejeitada: o projeto usa concorrência
  otimista (REQ-04, REQ-08).

## R-08. Logs

- **Decisão**: `EventosPagamento` (`LoggerMessage`), faixa 7001 a 7004, só com
  identificadores, valores e situação (FR-014):

  | EventId | Nome | Nível |
  |---------|------|-------|
  | 7001 | `PagamentoAprovado` | Information |
  | 7002 | `PagamentoRecusado` | Information |
  | 7003 | `PedidoIgnoradoNoPagamento` (status, reservado, já pago) | Information |
  | 7004 | `ConflitoNoPagamento` (número da reavaliação) | Warning |

- **Justificativa**: segue as faixas 5xxx (mensageria) e 6xxx (estoque). O identificador da
  transação entra no log 7001; não é dado pessoal.

## R-09. Testes existentes com o novo consumidor no Worker

- **Decisão**: com o pagamento registrado, os pedidos reservados nos testes do REQ-08
  passariam a `Processando` ou `PagamentoRejeitado`, e o Cliente não conseguiria mais
  cancelá-los. Os testes do REQ-08 continuam isolados: `CenarioEstoque.Worker` remove o
  `IHostedService` do consumidor de pagamento pelo `Servicos` da `FabricaWorker` (só no
  teste). A `FabricaWorker` passa a configurar `GatewayPagamento:TaxaAprovacaoPercentual`
  com 100 por padrão, e os testes de pagamento usam 100 ou 0 conforme o caminho.
- **Justificativa**: cada suíte testa o seu consumidor, sem depender de sorte. Remover o
  serviço hospedado no teste não muda o código de produção.
- **Alternativas**:
  - uma chave de configuração para desligar consumidores. Rejeitada: código de produção
    só para teste;
  - reescrever as expectativas do REQ-08 para `Processando`. Rejeitada: misturaria o
    comportamento dos dois consumidores em testes do estoque.

## R-10. Contratos

- **Decisão**: `estoque.reservado`, `pagamento.aprovado` e `pagamento.recusado` v1 são
  usados sem mudança (FR-013). Os schemas e os testes de serialização e conformidade
  existem desde o REQ-06. Os testes de integração do pagamento validam as mensagens
  publicadas contra os schemas, como os do estoque.
- **Justificativa**: Princípio II; o REQ-06 já desenhou os dois eventos para esta issue.

## R-11. Persistência

- **Decisão**: nenhuma migration. As tabelas `Pagamentos` (valor, situação, transação,
  processamento, `PedidoId` único) e `HistoricoStatusPedido`, a coluna
  `Pedidos.EstoqueReservado` e a `rowversion` do pedido já existem.
- **Justificativa**: o REQ-04 mapeou o pagamento pensando nesta issue; o teste
  `ModeloSincronizadoTestes` confirma que o modelo não mudou.
