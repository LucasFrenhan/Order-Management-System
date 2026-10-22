# Research: Mensageria, publicação de eventos e DLQ (REQ-06)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Projetos e camadas

- **Decisão**: um quinto projeto, `src/PedidoFulfillment.Worker`, com SDK `Microsoft.NET.Sdk.Web`.
  - **Rotas**: só `/health` e `/health/pronto`, reaproveitando as verificações da
    Infrastructure (FR-018).
  - **Referências**: Application, Infrastructure e Domain, como a API.
  - **Hospeda**: o despachante do outbox (R-05) e os consumidores (R-07).
- **Onde fica o código de mensageria**:
  - **Contratos dos eventos**: no Application (`Contratos/Eventos`), como os DTOs HTTP.
  - **Interfaces de consumo**: no Application (`Mensageria/IManipuladorEvento<T>`).
  - **Implementação com RabbitMQ.Client**: na Infrastructure (`Mensageria/`).
- **Teste de arquitetura**: passa a aceitar cinco camadas, com
  `Worker → Application, Infrastructure, Domain`, e a verificar que a API não referencia o
  Worker e vice-versa.
- **Justificativa**:
  - O Worker com SDK Web reaproveita health checks, opções e o `WebApplicationFactory` dos
    testes, sem pacote novo.
  - Os workers de negócio (issues #8 a #10) só implementam `IManipuladorEvento<T>` no
    Application e se registram no Worker.
- **Alternativas**: `Microsoft.NET.Sdk.Worker`, sem HTTP. Rejeitada: a prontidão do
  contêiner (FR-018, FR-019) exigiria outra forma de verificação.

## R-02. Contratos dos eventos (versão 1)

- **Decisão**:
  - **Envelope genérico**: `EnvelopeEvento<T>`, com:
    - `idEvento` (uuid v7);
    - `tipo` (`pedido.criado`, `estoque.reservado`, `pagamento.aprovado` ou
      `pagamento.recusado`);
    - `versao` (1);
    - `ocorridoEm` (UTC);
    - `idCorrelacao` (o id do pedido);
    - `dados` (T).
  - **Conteúdo de cada evento** (records `sealed`, com membros `required`):
    - `PedidoCriado`: `pedidoId`, `numero`, `clienteId`, `itens[]` (`produtoId`,
      `quantidade`, `precoUnitario`), `valorTotal` e `moeda`;
    - `EstoqueReservado`: `pedidoId` e `itens[]` (`produtoId`, `quantidade`);
    - `PagamentoAprovado`: `pedidoId`, `valor` e `identificadorTransacao`;
    - `PagamentoRecusado`: `pedidoId`, `valor` e `motivo`.
  - **Catálogo**: `TiposEvento`, uma classe estática com os nomes, a versão e o mapa
    tipo → `Type`, para ler a mensagem.
  - **Serialização**: `JsonSerializerDefaults.Web` (camelCase), com
    `JsonStringEnumConverter`. As opções ficam numa só classe, `SerializacaoEventos`, usada
    por quem publica, por quem consome e pelos testes.
  - **Schemas publicados**: `specs/006-mensageria/contracts/eventos/*.schema.json`
    (JSON Schema 2020-12), um por evento, com o envelope incluído (FR-003).
- **Justificativa**: os nomes no passado seguem a constituição (`pedido.criado`). O tipo
  vai no envelope e na chave de roteamento.

## R-03. Validação na leitura (consumo)

- **Decisão**: o consumidor lê a mensagem em três passos. Qualquer falha neles é
  "irrecuperável" e manda a mensagem direto para a DLQ (FR-014).
  1. Deserializa só o envelope e confere se `tipo` e `versao` são conhecidos.
  2. Deserializa `dados` no tipo correspondente. Os membros `required` e
     `JsonSerializerOptions.RespectRequiredConstructorParameters` /
     `RespectNullableAnnotations` recusam campo ausente ou nulo.
  3. Confere se o tipo da mensagem é um dos que o manipulador consome.
- **Justificativa**: em produção, a validação estrutural do próprio `System.Text.Json` é
  suficiente, sem biblioteca de schema. O schema completo é verificado nos testes (R-11).

## R-04. Outbox: tabela `EventosSaida`

- **Decisão**:
  - **Classe**: `EventoSaida` no Application (`Mensageria/EventoSaida.cs`), mapeada na
    Infrastructure, como o `RegistroIdempotencia`.
  - **Colunas**:
    - `Id`: `bigint` identity, que define a ordem;
    - `IdEvento`: único;
    - `Tipo` (`nvarchar(100)`);
    - `Conteudo`: o JSON do envelope;
    - `CriadoEm`;
    - `EnviadoEm` (nulo enquanto pendente);
    - `Tentativas`;
    - `ProximaTentativaEm`;
    - `UltimoErro` (`nvarchar(500)`, nulo).

    Índice filtrado `IX_EventosSaida_Pendentes` (`ProximaTentativaEm`, `Id`), com
    `WHERE EnviadoEm IS NULL`.
  - **Gravação**: o `ServicoPedidos.CriarAsync` (REQ-05) monta o envelope `PedidoCriado` a
    partir do pedido e chama `IArmazenamentoPedidos.Adicionar(pedido, registro, evento)`.
    A assinatura ganha o evento, gravado na mesma operação (FR-005). A repetição
    idempotente não passa por aí (FR-009).
  - **Migration**: `MensageriaOutbox`, com `EventosSaida` e `EventosProcessados` (R-08).
- **Justificativa**: transação única com o pedido. É o padrão outbox que a constituição
  cita.

## R-05. Despachante do outbox (no Worker)

- **Decisão**: `DespachanteEventosSaida`, um `BackgroundService` do Worker.
  - **Laço**: a cada `IntervaloDespacho` (padrão 1 s), chama `DespacharLoteAsync`.
  - **Lote**: até 50 pendentes com `ProximaTentativaEm <= agora`, em ordem de `Id`, lidos
    numa transação com `WITH (UPDLOCK, READPAST, ROWLOCK)`. Com mais de um Worker, cada
    instância pega linhas diferentes, sem bloquear a outra.
  - **Publicação**: um por vez, em ordem, na troca `oms.eventos`, com:
    - chave de roteamento = `Tipo`;
    - `persistent`, `MessageId = IdEvento`, `Type = Tipo` e
      `ContentType = application/json`;
    - confirmação do broker (`PublisherConfirmationsEnabled` e
      `PublisherConfirmationTrackingEnabled` do RabbitMQ.Client 7). O `BasicPublishAsync`
      só retorna depois do ack. Com `mandatory: false`, uma mensagem sem fila ligada é
      descartada sem erro (edge case da spec). Só um nack do broker ou uma falha de canal
      lança exceção (achado M3).
  - **Resultado**:
    - sucesso: `EnviadoEm = agora`;
    - falha: `Tentativas++`, `UltimoErro`, `ProximaTentativaEm = agora + min(2^tentativas s, 10 s)`,
      e o lote para, para preservar a ordem (FR-006, FR-007).
  - **Confirmação**: o commit acontece depois do lote. Se o Worker cair entre a
    confirmação do broker e o commit, o evento é publicado de novo (pelo menos uma vez,
    FR-008).
- **Justificativa**: com teto de 10 s e laço de 1 s, um broker que volta é percebido em no
  máximo uns 11 s, dentro dos 30 s do FR-007. `READPAST` deixa o Worker escalar sem
  publicação duplicada no caminho normal.
- **Alternativas**:
  - CDC ou trigger. Rejeitada: complexidade de operação.
  - Publicar na API (clarify). Rejeitada.

## R-06. Topologia

- **Decisão** (FR-010, FR-011), toda durável:
  - `oms.eventos`: troca `topic`, onde os eventos são publicados.
  - Por consumidor `C` (nome em kebab-case, por exemplo `estoque`):
    - fila `oms.C`, ligada a `oms.eventos` pelas chaves que o manipulador consome;
    - filas de espera `oms.C.espera.{ms}`, uma por intervalo (1000, 2000 e 4000), com
      `x-message-ttl = {ms}`, `x-dead-letter-exchange = ""` (troca padrão) e
      `x-dead-letter-routing-key = oms.C`. A mensagem espera ali, sem bloquear a fila
      principal, e volta sozinha para `oms.C` (FR-012);
    - DLQ `oms.C.dlq`.
  - **Criação**: `TopologiaMensageria.DeclararAsync` cria tudo de forma idempotente na
    partida do Worker (FR-018). A troca também é declarada antes de cada sessão do
    despachante.
- **Justificativa**: filas de espera com TTL são o jeito nativo do RabbitMQ de atrasar sem
  plugin. Uma fila por intervalo evita o bloqueio da cabeça da fila que um TTL por
  mensagem causaria.

## R-07. Base de consumo

- **Decisão**:
  - **Contrato** no Application: `IManipuladorEvento<TDados>`, com
    `string Consumidor { get; }` (o nome da fila),
    `IReadOnlyCollection<string> Tipos { get; }` e
    `Task ManipularAsync(EnvelopeEvento<TDados> evento, CancellationToken)`.
  - **Implementação** na Infrastructure: `ConsumidorEventos<TDados>`, um `BackgroundService`
    genérico, registrado por `services.AdicionarConsumidor<TManipulador, TDados>()`. Ele:
    1. abre um canal com `BasicQos(prefetch: 10)` e consome `oms.C` sem ack automático
       (FR-016);
    2. para cada mensagem:
       - se a leitura falha (R-03), publica em `oms.C.dlq`, com os cabeçalhos
         `x-motivo`, `x-tentativas` e `x-falhou-em`, e dá ack (FR-014);
       - senão, cria um escopo de DI, abre uma transação no `ContextoBancoDados`, confere
         em `EventosProcessados` (`Consumidor`, `IdEvento`) e, se já existe, faz commit e
         ack sem chamar o manipulador (FR-015);
       - senão, chama o manipulador, grava `EventosProcessados` e faz commit e ack. O
         efeito e o registro ficam na mesma transação;
       - se o manipulador lança, faz rollback. Com o cabeçalho `x-tentativas` = n menor
         que 3, republica em `oms.C.espera.{intervalo[n]}` com `x-tentativas = n + 1` e
         `x-ultimo-erro`; senão, publica em `oms.C.dlq` com o motivo (FR-013). Nos dois
         casos, confirma a publicação e depois dá ack na original.
  - **Intervalos**: vêm de `OpcoesMensageria.IntervalosNovaTentativa` (padrão
    `[1000, 2000, 4000]` ms). Os testes usam valores menores, e o nome das filas de espera
    acompanha o valor.
- **Justificativa**: o descarte de duplicados por transação no mesmo banco cobre a
  reentrega (FR-008, FR-015) para manipuladores que gravam no banco, que é o caso dos
  workers #8 a #10.
- **Risco aceito**: entre publicar na espera ou na DLQ e dar ack, uma queda duplica a
  mensagem. O descarte de duplicados resolve.

## R-08. Tabela `EventosProcessados`

- **Decisão**:
  - **Colunas**: `Consumidor` (`nvarchar(100)`), `IdEvento` (uuid) e `ProcessadoEm`.
  - **Chave primária**: `PK_EventosProcessados (Consumidor, IdEvento)`.
  - **Classe**: `EventoProcessado`, na Infrastructure (`Mensageria/`). Só a base de
    consumo usa.
- **Justificativa**: a chave primária garante o registro único mesmo com reentrega
  concorrente.

## R-09. Conexão com o broker

- **Decisão**: `ConexaoRabbitMQ` (singleton, Infrastructure), com:
  - `ConnectionFactory` montada das `OpcoesRabbitMQ` (REQ-02);
  - `AutomaticRecoveryEnabled = true` e `TopologyRecoveryEnabled = true`;
  - `ClientProvidedName = "oms-worker"`.

  A conexão é criada na primeira vez que alguém pede, e cada componente usa canais
  próprios. A `VerificacaoRabbitMQ` do REQ-02 continua igual. Só o Worker abre conexão de
  mensageria: a API não abre (FR-006).
- **Opções**: `OpcoesMensageria`, na seção `Mensageria`, com:
  - `IntervaloDespachoMs`: padrão 1000;
  - `TamanhoLote`: padrão 50;
  - `IntervalosNovaTentativaMs`: padrão `[1000, 2000, 4000]`;
  - `PrefetchConsumidor`: padrão 10.

  Os valores são validados na partida.

## R-10. Worker: hospedagem e entrega

- **Decisão**:
  - **`ProgramaWorker.cs`** (com `Main` explícito, sem top-level statements, para não haver dois `Program` globais nos testes; achado H1):
    - `AdicionarInfraestrutura`;
    - `AdicionarMensageria`, com a conexão, as opções e o despachante;
    - os health checks;
    - `Banco:PrepararNaInicializacao=false` no `appsettings.json` do Worker: quem aplica
      migrations e dados é a API.
  - **Arquivos de configuração**: `appsettings.json` e `appsettings.Development.json`, com
    os mesmos padrões de conexão da API em desenvolvimento.
  - **Porta local**: `launchSettings.json` com a porta 5231.
  - **Imagem**: `src/PedidoFulfillment.Worker/Dockerfile` igual ao da API (R-07 do REQ-07).
    O contrato OpenAPI não é necessário.
  - **Compose**: serviço `worker` no perfil `demo`, com `depends_on: api: service_healthy`
    (a API cria as tabelas), as mesmas variáveis de conexão, `OMS_WORKER_PORTA` (padrão
    5231) e o mesmo healthcheck por bash. O CI de demonstração já espera todos os serviços
    ficarem saudáveis (`--wait`). Basta conferir também `/health/pronto` do Worker (FR-019).
- **Justificativa**: o Worker não compete com a API para aplicar migrations.

## R-11. Testes

- **Decisão**:
  - **Unitários**:
    - `SerializacaoEventosTestes`: ida e volta dos 4 eventos, nomes camelCase, datas UTC e
      decimais;
    - `LeitorMensagemTestes`: corpo inválido, tipo ou versão desconhecida e campo
      obrigatório ausente viram "irrecuperável";
    - `PoliticaNovaTentativaTestes`: próxima espera ou DLQ por tentativa;
    - `ServicoPedidosCriacaoTestes`: a criação adiciona o evento `PedidoCriado` com os dados
      do pedido; a repetição não adiciona.
  - **Conformidade de schema** (`Testes.Integracao/Mensageria/SchemaEventosTestes`): para
    cada evento, o JSON serializado é válido no schema publicado, e exemplos inválidos
    (campo ausente, tipo errado) são recusados. Usa o `JsonSchema.Net` (R-12).
  - **Integração com SQL Server e RabbitMQ reais** (a fixture de contêineres já existente,
    ampliada):
    - `OutboxComBrokerTestes`: criar pedido pela API, rodar o despachante (Worker por
      `WebApplicationFactory`) e receber `pedido.criado` numa fila de teste em até 5 s;
      pausar o RabbitMQ (`PauseAsync`), criar 20 pedidos, despausar e receber os 20 em até
      30 s (SC-003); uma criação com 422 não gera evento.
    - `ConsumoComNovaTentativaTestes`, com um manipulador de verificação: falha 2 vezes e
      depois processa; falha sempre e chega à DLQ com `x-tentativas = 3` e motivo (4
      execuções); mensagem inválida vai direto para a DLQ (1 execução); o mesmo evento
      entregue duas vezes produz o efeito uma vez; parar e voltar o consumidor sem perder
      mensagens.
    - `WorkerTestes`: prontidão, topologia declarada e configuração ausente.
  - **Testes que já existem**:
    - o teste de arquitetura passa a esperar 5 camadas;
    - o `ModeloSincronizadoTestes` e o `MigracaoBancoTestes` passam a esperar a quarta
      migration.
- **Justificativa**: Princípio V. O SC-004 exige contar as execuções, o que o manipulador
  de verificação faz.

## R-12. Pacotes

- **Decisão**:
  - **Novo**: `JsonSchema.Net` 9.4.0 (MIT), só no projeto de testes de integração, para a
    conformidade de schema que a constituição exige. A 9.4.1 saiu há menos de duas
    semanas, por isso a 9.4.0.
  - **Já existentes**: `RabbitMQ.Client` 7.2.2 (desde o REQ-02) e Testcontainers.RabbitMq.
- **Alternativas**:
  - NJsonSchema. Rejeitada: traz o Newtonsoft.Json.
  - Validador próprio. Rejeitada: reimplementar JSON Schema.
