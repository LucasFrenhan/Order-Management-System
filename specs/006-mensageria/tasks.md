---

description: "Lista de tarefas do REQ-06 — Mensageria, publicação de eventos e DLQ"
---

# Tasks: Mensageria, publicação de eventos e DLQ

**Input**: Design documents from `/specs/006-mensageria/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md),
[contracts/topologia-e-consumo.md](./contracts/topologia-e-consumo.md),
[contracts/eventos/](./contracts/eventos/), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige:
- testes unitários das regras;
- testes de serialização e de conformidade de schema dos contratos de mensageria.

A spec pede broker real para outbox, novas tentativas, DLQ e duplicados (SC-002 a SC-005).
Os testes de cada fase vêm antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A fundação traz:
- o projeto Worker;
- os contratos dos eventos;
- as tabelas do outbox e de processados, com a migration;
- a conexão e a topologia do broker;
- as fixtures de teste.

US1 (outbox e despachante) e US3 (consumo) dependem dela. A US2 testa os contratos, e a
US4 fecha o Worker na entrega.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- RabbitMQ.Client 7.2.2 nativo (API assíncrona), sem MassTransit.
- Nomes em português; tipos de evento no passado (`pedido.criado`).
- `TreatWarningsAsErrors` ligado, e `dotnet format --verify-no-changes` passando.
- O único pacote novo é o `JsonSchema.Net` 9.4.0, no projeto de testes de integração.
- Contrato HTTP v1 intocado. A API não publica no broker (clarify).
- Os testes com banco ou broker reais usam Testcontainers e respeitam
  `OMS_EXIGIR_DOCKER` (REQ-07).
- Nenhuma espera fixa em teste: espere por condição, com prazo (helper
  `EsperarAteAsync`).
- Commits: `tipo(escopo): descrição [REQ-06]`.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `dotnet test` na branch `feature/req-06-mensageria` e registrar a contagem de testes como linha de base (zero ignorados com o Docker no ar).
- [X] T002 Criar `src/PedidoFulfillment.Worker/PedidoFulfillment.Worker.csproj` (`Microsoft.NET.Sdk.Web`, referências a Application, Domain e Infrastructure) e adicioná-lo à `PedidoFulfillment.sln` (`dotnet sln add`). Criar `src/PedidoFulfillment.Worker/ProgramaWorker.cs`, sem top-level statements (achado H1): `namespace PedidoFulfillment.Worker; public sealed class ProgramaWorker { public static void Main(string[] args) { ... } }`, com um host mínimo que mapeia `/health`, só para compilar. Ele é completado na US4. A API gera `Program` no namespace global, e um segundo `Program` global no Worker deixaria a `FabricaApi` e o teste de arquitetura ambíguos (CS0433) no projeto de testes, que referencia os dois.
- [X] T003 [P] Em `Directory.Packages.props`, no `ItemGroup Label="Testes"`, acrescentar `JsonSchema.Net` `9.4.0`, com o comentário "conformidade de schema dos eventos (specs/006-mensageria, R-12); a 9.4.1 tinha menos de duas semanas". Acrescentar o `PackageReference` em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`, junto com a referência de projeto ao Worker. Atualizar o comentário do topo do `Directory.Packages.props` para citar `specs/006-mensageria/plan.md`.
- [X] T004 Atualizar `tests/PedidoFulfillment.Testes.Integracao/Arquitetura/DependenciasCamadasTestes.cs`:
  - constante `Worker`;
  - `Permitidas[Worker] = [Application, Infrastructure, Domain]`;
  - assembly do Worker no dicionário `Assemblies` (por um tipo público do Worker, por
    exemplo `PedidoFulfillment.Worker.ProgramaWorker`);
  - `A_solucao_tem_exatamente_as_cinco_camadas`;
  - um teste novo garantindo que a API não referencia o Worker e vice-versa.

  Rodar a suíte.

---

## Phase 2: Foundational (Blocking Prerequisites)

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T005 [P] Testes de serialização em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SerializacaoEventosTestes.cs`. Para cada um dos 4 eventos:
  - envelope com os campos em camelCase;
  - `tipo` e `versao` corretos;
  - `ocorridoEm` em UTC;
  - decimais com 2 casas;
  - ida e volta (`Serializar` → `Desserializar`) igual por valor.
- [X] T006 [P] Testes de `EventoSaida` em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/EventoSaidaTestes.cs`:
  - `De(envelope, instante)` preenche `IdEvento`, `Tipo` e `Conteudo` (o JSON do envelope),
    com `Tentativas = 0`, `ProximaTentativaEm = instante` e `EnviadoEm` nulo;
  - `RegistrarFalha` incrementa as tentativas, com esperas de 2, 4, 8 e 10 s (teto) e o
    erro cortado em 500 caracteres;
  - `MarcarEnviado` preenche `EnviadoEm`.
- [X] T007 [P] Testes de `OpcoesMensageria` em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesMensageriaTestes.cs`:
  - os padrões são válidos;
  - 2 ou 4 intervalos, intervalos não crescentes, intervalo ≤ 0, lote 0 e despacho fora
    de 100–60000 são recusados, com mensagens que nomeiam a chave.

### Implementação da fundação

- [X] T008 [P] Criar os contratos em `src/PedidoFulfillment.Application/Contratos/Eventos/`, conforme o data-model e os schemas:
  - `EnvelopeEvento.cs` (`sealed record EnvelopeEvento<T>`, com membros `required`);
  - `PedidoCriado.cs` (com `ItemPedidoCriado`);
  - `EstoqueReservado.cs` (com `ItemEstoqueReservado`);
  - `PagamentoAprovado.cs`;
  - `PagamentoRecusado.cs`;
  - `TiposEvento.cs`: as constantes dos 4 tipos, `VersaoAtual = 1` e
    `IReadOnlyDictionary<string, Type> TipoDados`;
  - `SerializacaoEventos.cs`:
    - `JsonSerializerOptions Opcoes` (Web, `JsonStringEnumConverter`,
      `RespectRequiredConstructorParameters` e `RespectNullableAnnotations` ligados);
    - `string Serializar<T>(EnvelopeEvento<T>)`;
    - `EnvelopeEvento<T> Desserializar<T>(string)`.

  Comentários citando FR-001 a FR-003 e `contracts/eventos/`.
- [X] T009 [P] Criar `src/PedidoFulfillment.Application/Mensageria/EventoSaida.cs` (R-04 e data-model):
  - setters privados;
  - fábrica `De<T>`;
  - `MarcarEnviado(DateTimeOffset)`;
  - `RegistrarFalha(string erro, DateTimeOffset instante)`;
  - constante `TetoEsperaSegundos = 10`.

  Os testes da T006 devem passar.
- [X] T010 [P] Criar `src/PedidoFulfillment.Application/Mensageria/IManipuladorEvento.cs`, com `Consumidor`, `Tipos` e `ManipularAsync(EnvelopeEvento<TDados>, CancellationToken)`. Documentar que o efeito deve ser gravado pelas portas do Application resolvidas no mesmo escopo (que usam o `ContextoBancoDados` do escopo), porque a base faz o commit (contracts/topologia-e-consumo.md).
- [X] T011 [P] Criar `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesMensageria.cs`, com a seção `Mensageria`, os padrões do data-model e um `ValidadorOpcoesMensageria`. Os testes da T007 devem passar.
- [X] T012 Infrastructure: persistência.
  - Criar `Mensageria/EventoProcessado.cs`.
  - Criar `Persistencia/Configuracoes/ConfiguracaoEventoSaida.cs`:
    - tabela `EventosSaida`;
    - identity em `Id`;
    - índice único `IX_EventosSaida_IdEvento`;
    - `Tipo` com 100, `UltimoErro` com 500;
    - índice `IX_EventosSaida_Pendentes` (`ProximaTentativaEm`, `Id`) com
      `HasFilter("[EnviadoEm] IS NULL")`.
  - Criar `Persistencia/Configuracoes/ConfiguracaoEventoProcessado.cs`: tabela
    `EventosProcessados`, `HasKey(Consumidor, IdEvento).HasName("PK_EventosProcessados")` e
    `Consumidor` com 100.
  - Em `ContextoBancoDados.cs`, acrescentar os `DbSet` `EventosSaida` e
    `EventosProcessados`.
- [X] T013 Gerar a migration `MensageriaOutbox` (`dotnet ef migrations add MensageriaOutbox ... --output-dir Persistencia/Migracoes`). Revisar se ela só cria as 2 tabelas e os índices. Atualizar `ModeloSincronizadoTestes` e `MigracaoBancoTestes` para a quarta migration, e conferir `has-pending-model-changes`.
- [X] T014 Criar a conexão e a topologia em `src/PedidoFulfillment.Infrastructure/Mensageria/` (R-06, R-09):
  - `ConexaoRabbitMQ.cs`:
    - singleton `IAsyncDisposable`;
    - `Task<IConnection> ObterAsync(CancellationToken)`, preguiçoso e protegido por
      `SemaphoreSlim`;
    - `ConnectionFactory` das `OpcoesRabbitMQ`, com `AutomaticRecoveryEnabled`,
      `TopologyRecoveryEnabled` e `ClientProvidedName = "oms-worker"`.
  - `TopologiaMensageria.cs`:
    - constante `TrocaEventos = "oms.eventos"`;
    - `DeclararTrocaAsync(IChannel)`;
    - `DeclararConsumidorAsync(IChannel, string consumidor, IEnumerable<string> tipos, IReadOnlyList<int> intervalosMs)`,
      que declara `oms.C`, as ligações, `oms.C.espera.{ms}` (TTL e dead letter para
      `oms.C`) e `oms.C.dlq`, tudo durável;
    - os nomes por métodos `FilaPrincipal(c)`, `FilaEspera(c, ms)` e `FilaMortas(c)`.
  - `ExtensoesMensageria.cs`: `AdicionarMensageria(this IServiceCollection, IConfiguration)`,
    que registra as opções validadas na partida, a `ConexaoRabbitMQ` e o despachante (US1),
    e `AdicionarConsumidor<TManipulador, TDados>()` (US3).
- [X] T015 [P] Fixture de teste `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/MensageriaTeste.cs`:
  - coleção `ColecaoMensageria`;
  - um SQL Server e um RabbitMQ (`rabbitmq:4.3.6-management`) reais, com a mesma regra de
    `AmbienteDocker` das fixtures que já existem;
  - `NovoBanco()` e `NovoBancoMigradoAsync()`, um banco novo por teste, como na
    `BancoDadosTeste` (achado M2);
  - leitura das filas pelo próprio RabbitMQ.Client, sem `HttpClient` de gestão;
  - `ConfiguracoesBroker`;
  - `PausarBrokerAsync()` e `RetomarBrokerAsync()` (`PauseAsync` e `UnpauseAsync`);
  - utilitário `EsperarAteAsync(Func<Task<bool>> condicao, TimeSpan prazo)`;
  - utilitário `FilaTesteAsync(string chave)`: cria uma fila exclusiva ligada a
    `oms.eventos` e devolve as mensagens recebidas.
- [X] T016 [P] Criar `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaWorker.cs`: uma `WebApplicationFactory<PedidoFulfillment.Worker.ProgramaWorker>` (achado H1), com:
  - ambiente `Testes`;
  - string de conexão e broker configuráveis;
  - `Mensageria:IntervaloDespachoMs = 100`;
  - `Mensageria:IntervalosNovaTentativaMs = [100, 200, 400]`;
  - `ConfigureTestServices` para registrar manipuladores de teste.
- [X] T017 Mover `src/PedidoFulfillment.API/Saude/EndpointsSaude.cs` para `src/PedidoFulfillment.Infrastructure/Saude/EndpointsSaude.cs` (namespace `PedidoFulfillment.Infrastructure.Saude`), para a API e o Worker usarem o mesmo mapeamento de `/health` e `/health/pronto` (achado M1). Atualizar o `using` no `Program.cs` da API. Depois, rodar `dotnet build` e `dotnet test`: T004–T007 verdes, a suíte toda verde, zero avisos, e os testes de saúde do REQ-02 inalterados.

**Checkpoint**: contratos, tabelas, topologia e fixtures prontos.

---

## Phase 3: User Story 1 — Pedido criado vira evento, sem perda (Priority: P1) 🎯 MVP

**Goal**: a criação de pedido grava `pedido.criado` no outbox, e o Worker publica com
confirmação, aguentando o broker fora do ar.

**Independent Test**: criar um pedido e receber o evento numa fila de teste. Pausar o
broker, criar 20 pedidos, retomar e receber os 20.

### Testes da US1

- [X] T018 [P] [US1] Unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosCriacaoTestes.cs` (acrescentar):
  - a criação adiciona um `EventoSaida` do tipo `pedido.criado`, cujo `Conteudo` tem
    `pedidoId`, `numero`, `clienteId`, itens com preço, `valorTotal` e `moeda` iguais aos da
    resposta, e `idCorrelacao` igual ao `pedidoId`;
  - a falha 422 não adiciona evento.

  Em `ServicoPedidosIdempotenciaTestes.cs`, a repetição não adiciona evento. Atualizar
  `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs` (`Adicionar` com o evento e
  a lista `EventosGravados`).
- [X] T019 [P] [US1] Integração em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/OutboxComBrokerTestes.cs` (coleção `ColecaoMensageria`; API pela `FabricaApi`, com a preparação ligada e o broker do contêiner; Worker pela `FabricaWorker`):
  - **Cenário 1**: criar um pedido. Em até 5 s, a fila de teste ligada a `pedido.criado`
    recebe uma mensagem com:
    - `MessageId` igual ao `idEvento`;
    - `persistent`;
    - corpo válido no schema;
    - os dados do pedido.

    `EventosSaida.EnviadoEm` fica preenchido.
  - **Cenário 2**: com o Worker no ar, pausar o broker, criar 20 pedidos (todos com 201),
    retomar o broker e receber os 20 eventos distintos em até 30 s (SC-003).
  - **Cenário 3**: uma criação com 422 não grava nada em `EventosSaida`.
  - **Cenário 4**: a repetição idempotente não grava evento novo.
  - **Worker parado**: criar 2 pedidos sem o Worker (os eventos ficam pendentes), subir o
    Worker e receber os 2.
  - **Duas instâncias**: dois despachantes simultâneos com 30 pendentes publicam os 30,
    nenhum se perde, e cada `idEvento` aparece ao menos uma vez.

### Implementação da US1

- [X] T020 [US1] Outbox na criação:
  - Em `src/PedidoFulfillment.Application/Pedidos/IArmazenamentoPedidos.cs`, mudar para
    `Adicionar(Pedido, RegistroIdempotencia, EventoSaida)`.
  - Em `ServicoPedidos.CriarAsync`, montar `EnvelopeEvento<PedidoCriado>` a partir do
    pedido (com `idEvento` v7, `ocorridoEm` igual a `agora` e `idCorrelacao` igual ao id do
    pedido) e passar `EventoSaida.De(envelope, agora)`.
  - Em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`,
    adicionar o evento ao contexto.
  - Os testes da T018 devem passar.
- [X] T021 [US1] Criar `src/PedidoFulfillment.Infrastructure/Mensageria/DespachanteEventosSaida.cs` (`BackgroundService`, R-05):
  - laço com `PeriodicTimer(IntervaloDespachoMs)`;
  - `DespacharLoteAsync`:
    1. escopo de DI e transação no contexto;
    2. `FromSqlRaw("SELECT TOP (@lote) * FROM EventosSaida WITH (UPDLOCK, READPAST, ROWLOCK) WHERE EnviadoEm IS NULL AND ProximaTentativaEm <= @agora ORDER BY Id").ToListAsync()`,
       **sem compor** com LINQ (nada de `.Where`, `.OrderBy` ou `.Take` depois), e com um
       comentário explicando o porquê. Composta, a SQL vira subconsulta, e as dicas de lock
       perdem o efeito (achado M4; é o mesmo tipo de bug do `NEXT VALUE FOR` no REQ-05);
    3. canal com `new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true)`;
    4. `DeclararTrocaAsync`;
    5. para cada evento, `BasicPublishAsync(TrocaEventos, Tipo, mandatory: false, propriedades, corpo)`,
       com `Persistent = true`, `MessageId`, `Type` e `ContentType`;
    6. sucesso: `MarcarEnviado`;
    7. exceção: `RegistrarFalha` e fim do lote;
    8. `SaveChanges` e commit.
  - Falha de conexão: loga e espera o próximo ciclo, sem derrubar o processo.
  - Logs com `LoggerMessage`: `EventoPublicado` (5001) e `FalhaPublicacao` (5002), com
    `IdEvento`, `Tipo` e `Tentativas` (FR-020).
- [X] T022 [US1] Registrar o despachante em `AdicionarMensageria`. No `Program.cs` do Worker, chamar `AdicionarInfraestrutura` e `AdicionarMensageria`. Rodar T018–T019 e a suíte, e corrigir até ficarem verdes.

**Checkpoint**: o evento `pedido.criado` chega ao broker com garantia de outbox.

---

## Phase 4: User Story 2 — Contratos versionados e verificados (Priority: P1)

- [X] T023 [P] [US2] Conformidade em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/SchemaEventosTestes.cs`. Copiar os schemas de `specs/006-mensageria/contracts/eventos/` para a saída com `<None Include ... CopyToOutputDirectory>` no csproj de testes. Para cada evento:
  - o JSON de `SerializacaoEventos.Serializar` é válido no schema (`JsonSchema.Evaluate`),
    inclusive um pedido com `valorTotal` 829,70;
  - são inválidos: sem `idEvento`, `versao = 2`, `tipo` trocado, campo extra e `quantidade`
    texto.
- [X] T024 [P] [US2] Leitura em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/LeitorMensagemTestes.cs`:
  - corpo vazio;
  - JSON inválido;
  - `tipo` desconhecido;
  - `versao` 2;
  - `dados` sem campo `required`;
  - tipo não consumido pelo manipulador.

  Cada caso resulta em `ResultadoLeitura.Irrecuperavel(motivo)`. Uma mensagem válida
  resulta em `Valida(envelope)`.
- [X] T025 [US2] Criar `src/PedidoFulfillment.Infrastructure/Mensageria/LeitorMensagem.cs` (R-03), com `Ler<TDados>(ReadOnlyMemory<byte> corpo, IReadOnlyCollection<string> tiposAceitos)` → `ResultadoLeitura<TDados>`. As T023 e T024 devem passar.

---

## Phase 5: User Story 3 — Consumo com novas tentativas e DLQ (Priority: P1)

**Goal**: base de consumo com 3 novas tentativas não bloqueantes, DLQ e descarte de
duplicados.

**Independent Test**: um manipulador de verificação conta as execuções em cada cenário.

### Testes da US3

- [ ] T026 [P] [US3] Unitários em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/PoliticaNovaTentativaTestes.cs`. Com intervalos `[1000, 2000, 4000]`:
  - tentativas 0, 1 e 2 → espera de 1000, 2000 e 4000 ms;
  - tentativa 3 → DLQ;
  - cabeçalho ausente conta como 0;
  - cabeçalho inválido conta como 0.
- [ ] T027 [P] [US3] Integração em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/ConsumoComNovaTentativaTestes.cs` (coleção `ColecaoMensageria`, `FabricaWorker` com um `ManipuladorVerificacao` registrado: consumidor `verificacao`, tipo `pedido.criado`, comportamento configurável e contador de execuções por `idEvento`; as mensagens são publicadas direto na troca pelo teste):
  - **Cenário 1**: falha 2 vezes e depois processa. São 3 execuções, nada fica na DLQ, e o
    tempo entre as execuções cresce (≥ 100 ms e ≥ 200 ms).
  - **Cenário 2**: falha sempre. São exatamente 4 execuções, e `oms.verificacao.dlq` tem 1
    mensagem com `x-tentativas = 3`, `x-motivo` e `x-falhou-em`. A fila principal fica
    vazia (SC-004).
  - **Cenário 3**: com corpo inválido, há 0 execuções e a DLQ tem a mensagem com o motivo.
  - **Cenário 4**: o mesmo `idEvento` publicado 2 vezes resulta em 1 execução e 1 linha em
    `EventosProcessados` (SC-005).
  - **Cenário 5**: com o Worker parado, publicar 3 mensagens; ao subir o Worker, há 3
    execuções.
  - **Não bloqueante**: enquanto a mensagem A espera para nova tentativa, a mensagem B,
    publicada depois, é processada antes da segunda execução de A.
  - **Rollback**: um manipulador que grava algo no banco e depois lança não deixa nada
    gravado nem registra o evento como processado.
  - **Logs** (FR-020, achado L1): com `CapturaLogs`, os eventos `NovaTentativa`,
    `EnviadaParaDlq` e `DuplicadoDescartado` aparecem com `IdEvento`, fila e tentativa, sem
    dados pessoais.

### Implementação da US3

- [ ] T028 [US3] Criar `src/PedidoFulfillment.Infrastructure/Mensageria/PoliticaNovaTentativa.cs` (decide espera ou DLQ a partir de `x-tentativas`). A T026 deve passar.
- [ ] T029 [US3] Criar `src/PedidoFulfillment.Infrastructure/Mensageria/ConsumidorEventos.cs` (`BackgroundService<TManipulador, TDados>`, R-07):
  1. Na partida, obter a conexão, abrir um canal com confirmação, declarar a topologia do
     consumidor, aplicar `BasicQosAsync(0, Prefetch, false)` e iniciar o
     `AsyncEventingBasicConsumer` com `autoAck: false`.
  2. Para cada mensagem, seguir a tabela de `contracts/topologia-e-consumo.md`:
     - leitura com o `LeitorMensagem`;
     - escopo de DI e transação;
     - conferência em `EventosProcessados`;
     - manipulador;
     - registro e commit;
     - ack.
  3. Na exceção, rollback e republicação na espera ou na DLQ, com os cabeçalhos; depois,
     ack.
  4. Logs `NovaTentativa` (5003), `EnviadaParaDlq` (5004), `DuplicadoDescartado` (5005) e
     `EventoProcessado` (5006).

  Acrescentar `AdicionarConsumidor<TManipulador, TDados>()` em `ExtensoesMensageria`.
- [ ] T030 [US3] Rodar T026–T027 e a suíte e corrigir até ficarem verdes.

---

## Phase 6: User Story 4 — Processo de workers separado (Priority: P2)

- [ ] T031 [P] [US4] Integração em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/WorkerTestes.cs`:
  - **Prontidão**: com banco e broker, `/health/pronto` responde 200, e `oms.eventos`
    existe (declaração passiva no teste).
  - **Configuração**: sem `RabbitMQ:Senha`, ou com `Mensageria:TamanhoLote = 0`, o Worker
    não sobe, e a mensagem nomeia a chave sem valor.
  - **API sem o Worker**: com o Worker parado, a API cria pedidos normalmente (já coberto
    pela T019; citar no comentário).
- [ ] T032 [US4] Completar o Worker:
  - `ProgramaWorker.cs`: `AdicionarInfraestrutura`, `AdicionarMensageria` e
    `MapearEndpointsSaude` (da Infrastructure, T017), para `/health` e `/health/pronto`
    no mesmo formato da API.
  - `appsettings.json`: `Banco:PrepararNaInicializacao = false` e a seção `Mensageria` com
    os padrões.
  - `appsettings.Development.json`: as mesmas conexões de desenvolvimento da API.
  - `Properties/launchSettings.json`: perfil `http`, porta 5231.
- [ ] T033 [P] [US4] Criar `src/PedidoFulfillment.Worker/Dockerfile` (como o da API, sem o contrato OpenAPI, e com `ENTRYPOINT` para `PedidoFulfillment.Worker.dll`).
- [ ] T034 [US4] Acrescentar o serviço `worker` ao `docker-compose.yml`, no perfil `demo`:
  - `build` com o Dockerfile do Worker;
  - `depends_on: api: service_healthy`;
  - as mesmas variáveis de conexão da `api`;
  - `ports: ["${OMS_WORKER_PORTA:-5231}:8080"]`;
  - o healthcheck por bash.

  Acrescentar `OMS_WORKER_PORTA=5231` ao `.env.example` e um passo
  `curl -fsS http://localhost:5231/health/pronto` no job `demo` de
  `.github/workflows/build-testes.yml`.
- [ ] T035 [US4] Validar localmente num projeto Compose isolado (`-p oms-validacao`, em outras portas):
  - os 4 serviços ficam saudáveis;
  - um pedido criado pela API da demonstração é publicado (a fila de teste recebe);
  - `down -v`.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T036 [P] Atualizar `README.md`:
  - **Estado atual**: linha do REQ-06.
  - **Próximos passos**: tirar o REQ-06; nas #8 a #10, citar `IManipuladorEvento` e o
    guia de `contracts/topologia-e-consumo.md`.
  - **Mensageria**: a seção nova, com:
    - a troca, as filas e a DLQ;
    - como rodar o Worker localmente (porta 5231) e onde ver no painel;
    - "Pendências futuras": limpeza e reprocessamento da DLQ.
  - **Demonstração rápida**: o Worker e o endereço 5231.
  - **Estrutura**: o `PedidoFulfillment.Worker/`.
  - **Roteiros de validação**: o quickstart do REQ-06.
- [ ] T037 [P] Atualizar os comentários que citam o REQ-06 como futuro: `ExtensoesInfraestrutura.cs` ("o REQ-06 acrescenta aqui a mensageria") e `CLAUDE.md`, se citar.
- [ ] T038 Varredura de segredos, `dotnet format --verify-no-changes` e a suíte local inteira (com Docker; zero ignorados).
- [ ] T039 Executar o quickstart (§1 a §6) e registrar em Notes.
- [ ] T040 Fazer push e abrir o PR com `Closes #7`. Acompanhar o CI (`build-testes`, com os dois jobs) até ficar verde e registrar os tempos em Notes.

---

## Dependencies & Execution Order

- **Setup → Foundational → US1**: a US1 precisa dos contratos, do outbox e da conexão.
- **US2**: os testes de schema (T023) podem rodar logo depois da T008. O `LeitorMensagem`
  (T025) é pré-requisito da US3.
- **US3**: depende da Foundational e da T025.
- **US4**: o `Program.cs` do Worker começa na T022 e é completado na T032; o Dockerfile e o
  Compose podem correr em paralelo com a US3.
- **Polish**: depois de tudo. A T040 exige push.

### Parallel Opportunities

- T003 com T002; T005, T006, T007, T008, T009, T010 e T011; T015 e T016.
- T018 e T019; T023 e T024; T026 e T027; T031 e T033; T036 e T037.

---

## Implementation Strategy

### MVP

Setup, Foundational e US1: o evento `pedido.criado` chega ao broker com outbox.

### Incremental Delivery

1. Setup + Foundational → commit `feat(mensageria): cria worker, contratos de eventos e outbox [REQ-06]`.
2. US1 → commit `feat(mensageria): publica pedido criado pelo outbox [REQ-06]`.
3. US2 → commit `test(mensageria): valida contratos de eventos contra os schemas [REQ-06]`.
4. US3 → commit `feat(mensageria): implementa consumo com novas tentativas e DLQ [REQ-06]`.
5. US4 → commit `feat(worker): entrega o worker na demonstracao e no CI [REQ-06]`.
6. Polish → `docs(mensageria): ... [REQ-06]`, push e PR.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Limpeza e reprocessamento da DLQ ficam em "Pendências futuras" da spec.
- **SC-002** (95% em 5 s) é verificado por amostra: um evento no cenário 1 da T019, mais o
  prazo dos 20 eventos do cenário 2 (achado L2).
- `/speckit-analyze` (2026-10-22), achados aplicados:
  - **H1**: Worker com `Main` explícito em `ProgramaWorker`, sem top-level statements (T002,
    T016, T032, R-10).
  - **M1**: `EndpointsSaude` movido para a Infrastructure (T017).
  - **M2**: fixture de mensageria com banco novo por teste (T015).
  - **M3**: `mandatory: false` e descarte sem rota alinhados (R-05).
  - **M4**: `FromSqlRaw` sem composição (T021).
  - **L1**: logs conferidos na T027.
  - **L2**: SC-002 por amostra.
  - **L3**: spec `Approved`.
- Fazer commit ao fim de cada fase.
