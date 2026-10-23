# Research: Ordem de entrega assíncrona para pedidos pagos (REQ-10)

**Data**: 2026-10-23 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Onde fica o consumidor e como ele se chama

- **Decisão**: `CriacaoOrdemEntrega`, em `src/PedidoFulfillment.Application/Entregas/`,
  implementa `IManipuladorEvento<PagamentoAprovado>` com `Consumidor = "entrega"` e
  `Tipos = ["pagamento.aprovado"]`. A base do REQ-06 cria `oms.entrega`, as filas de espera
  e `oms.entrega.dlq`. O Worker o registra com
  `AdicionarConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>()` (FR-001).
- **Justificativa**: é o mesmo caminho dos consumidores de estoque (REQ-08) e de pagamento
  (REQ-09). O nome segue a constituição (português, nome do domínio), como `estoque` e
  `pagamento`; "fulfillment" continua sendo o nome do requisito e da solução. O manipulador
  só depende de portas do Application: `IArmazenamentoPedidos`, `TimeProvider` e `ILogger`.
- **Alternativas**:
  - fila `oms.fulfillment`. Rejeitada: os nomes de fila e consumidor do projeto são em
    português;
  - um processo próprio de logística. Rejeitada: a ordem, o status e o evento são gravados
    na mesma transação do pedido (FR-004).

## R-02. Regra da ordem de entrega no domínio

- **Decisão**: em `Pedido`, ao lado do pagamento do REQ-09:
  - `bool AptoParaOrdemEntrega` (só leitura): `EstoqueReservado`, `Pagamento` com situação
    `Aprovado`, `OrdemEntrega is null` e status `Processando` ou `ProntoParaEnvio` (FR-002);
  - `OrdemEntrega CriarOrdemEntrega(DateTimeOffset instante)`: exige o pedido apto
    (`InvalidOperationException` fora disso), cria `new OrdemEntrega(Id, instante)`,
    preenche `OrdemEntrega` e, em `Processando`, muda para `ProntoParaEnvio` com o motivo
    `Ordem de entrega criada.` e sem autor. Em `ProntoParaEnvio`, só atualiza `AtualizadoEm`
    (FR-004, US3 cenário 3);
  - `MotivoOrdemEntregaCriada = "Ordem de entrega criada."` como constante do domínio.
- **Justificativa**: a condição e a criação ficam juntas e com a mesma guarda, testáveis sem
  banco (Princípio V), como `AptoParaPagamento` e `AprovarPagamento`. A `OrdemEntrega` já
  tem o construtor `(pedidoId, instante)` e os dados de despacho opcionais desde o REQ-04.
- **Alternativas**: criar a `OrdemEntrega` no consumidor e só mudar o status no pedido.
  Rejeitada: espalharia a regra "uma ordem por pedido" e a guarda.

## R-03. Transição feita pelo sistema

- **Decisão**: `Processando → ProntoParaEnvio` é feita por `Pedido.CriarOrdemEntrega`, sem
  passar por `TransicoesStatusPedido`, que continua sendo a regra da API (REQ-05). A
  transição já existe no contrato v1; nada muda na tabela da API.
- **Justificativa**: igual ao REQ-09 (R-04): mudança do sistema, com histórico sem autor.

## R-04. Concorrência e reavaliação

- **Decisão**: "ler, decidir, gravar" até 3 vezes em `ConflitoConcorrenciaException`, como
  `ReservaEstoque` e `ProcessamentoPagamento`, com `DescartarAlteracoes` e o savepoint do
  EF Core dentro da transação da base. A cada reavaliação, o pedido é relido; se deixou de
  estar apto (por exemplo, foi cancelado), a mensagem é confirmada sem gravar. Esgotadas as
  reavaliações, o conflito propaga para as novas tentativas da base (1 + 3) e, no fim, a
  DLQ (FR-007).
- **Versão do pedido**: `CriarOrdemEntrega` sempre altera uma coluna do pedido (status ou
  `AtualizadoEm`), então o `UPDATE` do pedido sai com a `rowversion` e o conflito com a API
  é detectado mesmo quando o status não muda.
- **Uma ordem por pedido (FR-005)**: três barreiras:
  1. o registro de processado da base descarta a reentrega do mesmo evento;
  2. a `rowversion` do pedido serializa dois processamentos simultâneos;
  3. o índice único em `OrdensEntrega.PedidoId` (relação 1:1 do REQ-04) barra uma segunda
     linha no banco.
- **Alternativas**: bloqueio pessimista do pedido. Rejeitada: o projeto usa concorrência
  otimista (REQ-04, REQ-08, REQ-09).

## R-05. Leitura do pedido com a ordem de entrega

- **Decisão**: `ArmazenamentoPedidos.ObterPedidoAsync` passa a incluir `OrdemEntrega`, além
  de `Itens` e `Pagamento`, para o consumidor saber se o pedido já tem ordem. A API não expõe
  a ordem (FR-012), então o contrato HTTP não muda. A `OrdemEntrega` nova, ligada ao pedido
  rastreado, é inserida pelo EF Core no mesmo `SaveChanges` (chave `ValueGeneratedNever`;
  entidade nova pela navegação), como o `Pagamento` do REQ-09. Nenhum produto é lido
  (FR-006).
- **Justificativa**: `Include` a mais na mesma consulta, sem mudar a interface.
- **Alternativas**: um método novo no armazenamento. Rejeitada: sem ganho.

## R-06. Corrida com o cancelamento e o PATCH pela API

- **Decisão**: nada novo na API. O `ServicoPedidos` já relê e reavalia em conflito de versão
  (REQ-05, REQ-08), e o consumidor faz o mesmo (R-04):
  - cancelamento antes: o estoque volta, e o consumidor encontra o pedido `Cancelado`, sem
    reserva, e o ignora;
  - fulfillment antes: o pedido fica `ProntoParaEnvio` com a ordem; um cancelamento
    posterior pela operação é permitido (contrato v1) e devolve o estoque (REQ-08). A ordem
    continua registrada, sem despacho (Edge Cases);
  - operação move o pedido para `ProntoParaEnvio` antes: o consumidor cria a ordem sem mudar
    o status;
  - operação move o pedido para `Despachado` antes: status final, o consumidor ignora.
- **Justificativa**: a versão do pedido serializa as duas gravações, e os dois lados
  conferem o estado relido antes de agir (US3 cenário 5).

## R-07. Contrato `entrega.criada` v1

- **Decisão**:
  - record `EntregaCriada` em `Contratos/Eventos/Eventos.cs`: `PedidoId`, `Numero`,
    `OrdemEntregaId` e `Itens` (`ItemEntregaCriada`: `ProdutoId`, `Quantidade`);
  - `TiposEvento.EntregaCriada = "entrega.criada"`, acrescentado a `TipoDados`;
  - schema em `specs/010-fulfillment/contracts/eventos/entrega.criada.v1.schema.json`
    (draft 2020-12, envelope do REQ-06, `additionalProperties: false`, `numero` com o padrão
    `^PED-\d{8}-\d{6}$`, `itens` com `minItems: 1`, `quantidade` com `minimum: 1`);
  - o projeto de integração copia os schemas de `specs/010-fulfillment/contracts/eventos/`,
    como faz com os do REQ-06 e do REQ-08;
  - testes: serialização em camelCase e ida e volta (unitários, `SerializacaoEventosTestes`)
    e conformidade e recusa de mensagem fora do schema (integração, `SchemaEventosTestes`)
    (FR-009, SC-004).
- **Justificativa**: mesmo caminho do `estoque.insuficiente` (REQ-08). Os itens saem do
  pedido gravado, não do evento de entrada (que não os traz). O nome segue a constituição
  (passado), equivalente ao "PedidoDespachávelEvent" da issue.
- **Alternativas**:
  - `pedido.despachavel`. Rejeitada: não está no passado;
  - incluir endereço ou cliente. Rejeitada: o sistema não guarda endereço, e o evento não
    leva dados pessoais (Assumptions).

## R-08. Logs

- **Decisão**: `EventosEntrega` (`LoggerMessage`), faixa 8001 a 8003, só com
  identificadores e situação (FR-011):

  | EventId | Nome | Nível |
  |---------|------|-------|
  | 8001 | `OrdemEntregaCriada` (pedido, ordem, status mudou ou não) | Information |
  | 8002 | `PedidoIgnoradoNaEntrega` (status, reservado, pagamento aprovado, já tem ordem) | Information |
  | 8003 | `ConflitoNaEntrega` (número da reavaliação) | Warning |

- **Justificativa**: segue as faixas 5xxx (mensageria), 6xxx (estoque) e 7xxx (pagamento).

## R-09. Testes existentes com o novo consumidor no Worker

- **Decisão**: com o consumidor de entrega registrado, os pedidos aprovados nos testes do
  REQ-09 passariam a `ProntoParaEnvio`. Os testes do REQ-09 continuam isolados:
  `CenarioPagamento.WorkerProntoAsync` remove o `IHostedService` do consumidor de entrega
  pelo `Servicos` da `FabricaWorker`, como o `CenarioEstoque` já faz com o de pagamento. O
  `CenarioEstoque` também passa a remover o de entrega, por simetria (sem pagamento, ele
  não recebe nada, mas a fila ficaria declarada). Os testes de entrega sobem o Worker
  completo, com a taxa de aprovação em 100, e limpam `oms.entrega` antes, porque
  `pagamento.aprovado` de outros testes pode ter ficado na fila.
- **Justificativa**: cada suíte testa o seu consumidor; o código de produção não muda para
  os testes.
- **Alternativas**: reescrever as expectativas do REQ-09 para `ProntoParaEnvio`. Rejeitada:
  misturaria os dois consumidores nos testes do pagamento.

## R-10. Persistência

- **Decisão**: nenhuma migration. A tabela `OrdensEntrega` (transportadora, rastreio e data
  de despacho anuláveis, `PedidoId` único, índice filtrado de rastreio) e a `rowversion` do
  pedido já existem desde o REQ-04; o teste `ModeloSincronizadoTestes` confirma que o modelo
  não mudou.
- **Justificativa**: o REQ-04 mapeou a ordem pensando nesta issue.

## R-11. Documentação afetada

- **Decisão**:
  - README: linha do REQ-10 na tabela de requisitos entregues, roteiro sem a issue #10, e o
    fluxo do pedido terminando em `ProntoParaEnvio`;
  - `specs/006-mensageria/contracts/topologia-e-consumo.md`: `entrega.criada` no catálogo e
    o consumidor `entrega`;
  - `specs/009-pagamento/quickstart.md`: nota de que, com o Worker completo, o pedido
    aprovado segue para `ProntoParaEnvio`.
- **Justificativa**: o mesmo tratamento dado ao quickstart do REQ-08 no REQ-09.
