# Research: Recuperação dos consumidores depois de o broker reiniciar

**Requisito**: REQ-17 (issue #41) | **Data**: 2026-10-28 | **Spec**: [spec.md](./spec.md)

## R-01. Causa da parada (FR-001)

- **Análise** (código da `main` e RabbitMQ.Client 7.2.2):
  1. `ConexaoRabbitMQ` cria uma conexão com `AutomaticRecoveryEnabled = true` e
     `TopologyRecoveryEnabled = true` (REQ-06, R-09). O cliente grava as filas, ligações e
     inscrições declaradas e as refaz na recuperação.
  2. O broker para: ele fecha a conexão com `320 CONNECTION_FORCED`, e a recuperação automática
     começa. Enquanto ela tenta, `IConnection.IsOpen` é falso.
  3. O despachante pede a conexão a cada `IntervaloDespachoMs` (1 s), porque o canal dele fechou.
     `ObterAsync` vê `IsOpen = false`, chama `DisposeAsync` na conexão **em recuperação** e tenta
     criar outra. O `DisposeAsync` encerra a recuperação: as inscrições gravadas pelo cliente
     somem junto. O `MonitorDlq` e o reenvio da DLQ podem disparar o mesmo descarte; a prontidão
     não (`VerificacaoRabbitMQ` abre uma conexão própria, sem recuperação).
  4. Com o broker de volta, a conexão nova sai, e o despachante abre um canal e redeclara as
     filas. Os canais de consumo (`canalConsumo`) pertenciam à conexão descartada, e o
     `ConsumidorEventos` só se inscreve em `IniciarAsync`, uma vez, na partida. Resultado: filas
     com 0 consumidores, como na issue.
- **Mesmo sem o descarte**, a recuperação do cliente não cobriria tudo da spec: uma inscrição
  cancelada pelo broker (fila apagada, `basic.cancel`) não é refeita, e é exatamente a limitação
  "fila apagada com o Worker no ar" do fluxo 08.
- **Confirmação com o broker real**: o Docker não estava disponível durante o plano. A
  confirmação fica como a **primeira tarefa da implementação** (antes da correção): um teste de
  integração que reinicia o contêiner com o código da `main`, mais logs temporários de
  `ConnectionShutdownAsync`, `RecoverySucceededAsync`, `ConnectionRecoveryErrorAsync` e do
  `DisposeAsync`. A sequência observada é registrada nesta seção (subseção "Observado"). Se ela
  contrariar a análise, o plano volta para revisão antes da correção.

## R-02. Alternativa escolhida (FR-002)

- **Decisão**: o processo é dono da recuperação. A conexão passa a ter
  `AutomaticRecoveryEnabled = false` (e, por consequência, sem recuperação de topologia), e cada
  consumidor ganha um **supervisor de inscrição**: abre a sessão de consumo (canal de publicação,
  canal de consumo, topologia e `basic.consume`), espera o sinal de perda, fecha a sessão,
  espera o intervalo e abre outra. A conexão continua única e compartilhada.
- **Por quê**:
  - com a recuperação do cliente desligada, "conexão fechada" significa "conexão morta", e o
    `ObterAsync` atual (descartar e criar outra) fica correto, sem precisar adivinhar se a
    conexão está em recuperação;
  - o despachante (canal recriado quando fechado), o `MonitorDlq` e o reenvio da DLQ (canal por
    uso) já funcionam assim e não mudam;
  - um único dono por inscrição garante no máximo um consumidor por fila neste processo
    (FR-006): não há uma recuperação do cliente refazendo a inscrição ao mesmo tempo que a nossa;
  - o mesmo laço cobre o reinício do broker, a conexão fechada à força, o canal fechado por erro
    e a inscrição cancelada pelo broker (FR-003, FR-004), e redeclara a topologia antes de cada
    inscrição (FR-005).
- **Alternativas consideradas**:

  | Alternativa | Por que não |
  |-------------|-------------|
  | Não descartar a conexão em recuperação e deixar o cliente refazer as inscrições | Não há propriedade pública "em recuperação": seria preciso inferir pelos eventos `ConnectionShutdownAsync`/`RecoverySucceededAsync`. Não cobre a inscrição cancelada pelo broker (fila apagada) nem uma recuperação que o cliente não tenta. A topologia refeita pelo cliente mistura o que todos os canais declararam |
  | Supervisor de inscrição **com** a recuperação do cliente ligada | Os dois refazem a inscrição depois de uma queda: duas inscrições na mesma fila (fere FR-006) ou disputa entre elas |
  | Conexões separadas para publicar e para consumir | Sozinha, não resolve: a conexão de consumo cai do mesmo jeito no reinício do broker e precisa do mesmo supervisor. Dobra as conexões sem ganho para este requisito |

- **Efeito em decisões anteriores**: substitui as opções de recuperação de REQ-06, R-09. O resto
  de R-09 (conexão única, criada no primeiro pedido, canais por componente) continua.

## R-03. Sinal de perda da inscrição

- **Decisão**: uma sessão termina no primeiro destes sinais, guardado num
  `TaskCompletionSource` da sessão (`TrySetResult`, idempotente):
  - `IChannel.ChannelShutdownAsync` do canal de consumo (conexão perdida, broker parado, conexão
    fechada à força, erro de canal);
  - `AsyncEventingBasicConsumer.UnregisteredAsync` sem cancelamento pedido por nós (o broker
    cancela a inscrição quando a fila é apagada, e o canal continua aberto);
  - o canal de publicação da sessão fechado (`ChannelShutdownAsync`): sem ele, a sessão não
    consegue republicar na espera nem na DLQ.
- O motivo vem do `ShutdownEventArgs` (`Initiator`, `ReplyCode`, `ReplyText`) ou, no
  cancelamento, de `"cancelada pelo broker"`. Nenhum desses campos traz credenciais.
- Durante o encerramento do Worker (`stoppingToken` cancelado), os sinais são ignorados, e o
  laço sai sem reinscrever (US2, cenário 3).
- O heartbeat de 10 s (já configurado) detecta uma conexão TCP morta sem fechamento, e ela vira
  um `ChannelShutdownAsync`.

## R-04. Intervalo entre tentativas (FR-007)

- **Decisão**: a primeira tentativa depois da perda sai na hora; as seguintes esperam 1 s, 2 s,
  4 s, 8 s e depois 10 s fixos, até dar certo. Uma inscrição que deu certo zera a sequência. A
  partida usa a mesma sequência (antes era 2 s fixos).
- **Por quê**: com o broker de volta, a próxima tentativa sai em no máximo 10 s, mais o prazo de
  conexão de 5 s, bem dentro de 1 min (FR-003, SC-001). Os três consumidores e o despachante
  dividem a mesma conexão: a trava de `ObterAsync` faz uma criação por vez, e uma recusa de
  conexão volta rápido.
- **Sem configuração nova**: constantes no supervisor, como o `EsperaReconexao` atual. Os testes
  de integração cabem no prazo de 1 min sem encurtá-las.

## R-05. Confirmação e republicação presas à sessão (FR-008 a FR-010)

- **Decisão**: o callback de entrega usa os canais **da sessão que entregou**, não os campos da
  classe (que mudam a cada sessão). O `delivery tag` só vale no canal que entregou.
- Se a confirmação falhar porque o canal fechou (`AlreadyClosedException` ou
  `OperationInterruptedException`), o callback termina sem relançar: o broker devolve a mensagem
  à fila quando o canal fecha. Se o efeito já estava gravado, a reentrega é descartada como
  duplicada (5005); se não, ela é processada uma vez.
- Se a republicação na espera ou na DLQ falhar, a original não é confirmada e volta para a fila
  com os cabeçalhos de antes: a contagem de tentativas não é pulada nem zerada.
- Ao fechar uma sessão, os canais são fechados com `abort: true` e prazo de 2 s, como o
  `DescartarCanalAsync` do despachante, para não prender o laço.

## R-06. Estado das inscrições e prontidão (FR-013, FR-014)

- **Decisão**: um singleton `EstadoConsumidores` (Infrastructure), registrado em
  `AdicionarMensageria`, com o estado de cada consumidor conhecido (`IDescritorConsumidor`):
  começa **sem inscrição** e muda para inscrito depois do `basic.consume` e de volta no sinal de
  perda. A verificação `VerificacaoConsumidores`, nome `consumidores`, tag `pronto`, também é
  registrada em `AdicionarMensageria`: só o Worker a tem.
- `Healthy` só com todos inscritos; `Unhealthy` sem descrição nem dados, na hora, sem tolerância
  (clarify 2). Lê só memória: responde em microssegundos, sem ir ao broker.
- **Efeito**: o Worker agora fica pronto só depois de os três consumidores se inscreverem. O teste
  `Worker_fica_pronto_e_declara_a_troca_de_eventos` passa a esperar a prontidão (até 30 s) em vez
  de ler uma vez. A API não chama `AdicionarMensageria` e não muda (FR-014).
- **Compose**: o healthcheck do serviço `worker` lê `/health/pronto` (a cada 10 s, 12 tentativas,
  `start_period` de 20 s). Com a entrada nova, o `up --wait` passa a esperar os consumidores
  inscritos, e o contêiner aparece `unhealthy` durante uma queda do broker. Nenhum serviço
  depende da saúde do Worker e não há `restart:` no compose, então nada reinicia; o healthcheck
  não muda.

## R-07. Métricas (FR-016a)

- **Decisão**: dois instrumentos no meter `PedidoFulfillment`, acrescentados ao contrato do REQ-16
  como métricas 7 e 8:

  | # | Nome | Tipo | Unidade | Atributos |
  |---|------|------|---------|-----------|
  | 7 | `oms.mensageria.consumidor.inscrito` | ObservableGauge (long) | `{inscricao}` | `oms.consumidor` |
  | 8 | `oms.mensageria.consumidor.reinscricoes` | Counter (long) | `{reinscricao}` | `oms.consumidor` |

- O gauge lê o estado espelhado em `EstadoMetricas` (como a DLQ), porque o callback é estático e
  não pode resolver serviços; a prontidão lê o `EstadoConsumidores` do próprio host, para que
  vários Workers no mesmo processo de teste não se misturem.
- O contador soma 1 a cada inscrição que dá certo **depois de uma perda**; a primeira inscrição
  da partida não conta.
- Sem ouvinte (telemetria desligada), nada é publicado, como as outras seis.

## R-08. Logs (FR-015, FR-016)

- **Decisão**: quatro IDs novos na faixa da mensageria (5016 a 5019; 5001 a 5015 já são usados):

  | ID | Nome | Nível | Onde |
  |----|------|-------|------|
  | 5016 | `InscricaoPerdida` | Warning | supervisor, no sinal de perda |
  | 5017 | `FalhaReinscricao` | Warning | supervisor, numa tentativa que falha depois de uma perda |
  | 5018 | `ConsumidorReinscrito` | Information | supervisor, na volta |
  | 5019 | `ConexaoBrokerFechada` | Warning | `ConexaoRabbitMQ`, no `ConnectionShutdownAsync` não pedido pelo processo |

- O 5008 (`FalhaInicioConsumidor`) continua só para a partida, com o mesmo ID, nome e mensagem;
  como o supervisor faz também as tentativas da partida, ele passa a registrá-lo (analyze I1). Mensagens e campos no
  [contrato](./contracts/recuperacao-consumo.md#logs).
- Os logs saem pelo `ILogger`, e a correlação do REQ-16 (trace e span quando há atividade)
  continua valendo; o supervisor não abre spans próprios.

## R-09. Como os testes derrubam o broker (FR-017, FR-018)

- **Decisão**: o `MensageriaTeste` passa a ligar a porta AMQP do contêiner a uma **porta fixa** do
  host, escolhida livre na inicialização (`TcpListener` na porta 0). Assim `StopAsync` e
  `StartAsync` do Testcontainers reiniciam o mesmo contêiner sem trocar a porta que o Worker já
  usa. Novos métodos:
  - `ReiniciarBrokerAsync()`: para e inicia o contêiner (mesmo volume), espera a porta AMQP;
  - `RecriarBrokerAsync()`: `rabbitmqctl stop_app`, `reset`, `start_app` (filas e usuários
    apagados) e recria o usuário da API, como um broker sem dados;
  - `FecharConexoesAsync()`: `rabbitmqctl close_all_connections`, que fecha à força as conexões do
    Worker;
  - os já existentes `ApagarFilasConsumidorAsync(..., soPrincipal: true)` e
    `ConsumidorLigadoAsync` cobrem a fila apagada e a contagem de consumidores.
- Os testes que derrubam o broker ficam numa coleção própria, `ColecaoQuedaBroker`, com uma
  instância própria do `MensageriaTeste` (analyze C1): rodam em série entre si, sem reiniciar o
  broker das outras coleções, e em paralelo com elas, sem alongar a `ColecaoMensageria`. Os dados
  do broker sobrevivem a `stop`/`start`.
- O canal fechado sozinho, sem queda da conexão, não tem comando no broker: ele é coberto por
  testes unitários do supervisor (R-10) e, de ponta a ponta, pelo fechamento forçado da conexão.

## R-10. Supervisor testável sem broker

- **Decisão**: o laço fica numa classe própria, `SupervisorInscricao`, que recebe "abrir sessão"
  e "esperar" (`Func<TimeSpan, CancellationToken, Task>`, `Task.Delay` em produção), e cada
  sessão expõe a `Task` de perda. Testes unitários com sessões falsas e uma espera que só anota
  o intervalo pedido cobrem: perda por canal e por cancelamento, sinais repetidos (uma
  reinscrição só), falha ao abrir (intervalos 0, 1, 2, 4, 8 e 10 s), zerar a sequência depois de
  dar certo e encerramento durante a espera.
- **Sem pacote novo**: os testes unitários usam relógios próprios (`RelogioFixo`), sem
  `Microsoft.Extensions.TimeProvider.Testing`; a espera injetada evita depender de timers.
