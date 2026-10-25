# Research: limpeza de mensageria e reprocessamento da DLQ (REQ-14)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

Não há `NEEDS CLARIFICATION` no contexto técnico. As decisões abaixo resolvem o "como" das
escolhas que a spec, o specify e o clarify deixaram para o plano.

## R-01 Limpeza: serviço periódico com lock e lotes

**Decisão**: serviço em segundo plano novo, `LimpezaMensageria` (Infrastructure), no molde do
`ReconciliadorPedidos` e do `ProcessadorEstornos`:

- primeira rodada na partida, depois a cada `Limpeza:IntervaloSegundos` (padrão 3 600);
- uma instância por rodada, com `sp_getapplock 'oms-limpeza'` numa conexão própria,
  liberado com `sp_releaseapplock` no fim (FR-006);
- quatro alvos, cada um apagado em lotes de `Limpeza:TamanhoLote` (padrão 1 000), até
  `Limpeza:LotesPorRodada` (padrão 10) lotes por alvo; o resto fica para a rodada seguinte
  (FR-005):

| Alvo | Condição | Retenção padrão | Mínimo |
|------|----------|-----------------|--------|
| `EventosSaida` | `EnviadoEm IS NOT NULL AND EnviadoEm < agora - retenção` | 168 h (7 dias) | 1 h |
| `EventosProcessados` | `ProcessadoEm < agora - retenção` | 168 h (7 dias) | 24 h |
| `RegistrosIdempotencia` | `CriadoEm < agora - retenção` | 48 h | 24 h (contrato do REQ-05) |
| `PedidosReenvioDlq` | situação final e `RegistradoEm < agora - retenção` | 2 160 h (90 dias) | 24 h |

- cada lote é um `DELETE TOP (n)` (EF Core `ExecuteDeleteAsync` sobre uma consulta com
  `OrderBy` e `Take`), numa transação curta própria, para segurar os bloqueios só pelo tempo
  do lote;
- índices novos para a condição de cada alvo (R-07), para o `DELETE` não varrer a tabela.

**Por que não disputa com o despachante e os consumidores** (FR-005): o despachante só lê
linhas pendentes (`EnviadoEm IS NULL`, índice filtrado `IX_EventosSaida_Pendentes`, com
`READPAST`), que a limpeza nunca toca; os consumidores só inserem em `EventosProcessados` e
leem o par (consumidor, evento) recente; a criação de pedidos só lê e insere chaves recentes.
Lotes curtos com o índice da data mantêm os bloqueios em linhas antigas, fora do caminho deles.

**Alternativas rejeitadas**:
- partição por data e `TRUNCATE` de partição: exige edição Enterprise ou recriar as tabelas;
- `DELETE` sem lote: um só comando sobre milhões de linhas escala o bloqueio para a tabela;
- apagar no próprio despachante depois de enviar: perde a janela de investigação de 7 dias.

## R-02 Janela de reentrega e retenção mínima dos processados

**Decisão**: o mínimo de `Limpeza:RetencaoProcessadosHoras` é 24 h, e o padrão é 168 h. A
janela real de reentrega com registro de processado é curta:

- novas tentativas do REQ-06: segundos (1 s, 2 s, 4 s);
- republicação do outbox depois de uma queda entre a confirmação do broker e o commit:
  segundos a minutos.

O reenvio da DLQ não depende do registro: uma mensagem só está na DLQ porque nunca foi
processada com sucesso por aquele consumidor. Se uma cópia duplicada da mesma mensagem tiver
sido processada antes e o registro já tiver sido apagado, os consumidores ainda ignoram o
pedido pela regra de pedido não apto (REQ-08 a REQ-10, spec Edge Cases).

## R-03 Pedido de reenvio: registro na API, execução no Worker

**Decisão** (clarify, revisão do specify): a API não fala com o broker. Duas rotas novas
(contrato):

- `POST /api/v1/admin/dlq/{consumidor}/reenvios`, com o filtro no corpo: valida e grava um
  `PedidoReenvioDlq` pendente, com o `sub` do token como autor, e responde **202** com o
  pedido (identificador e `Location` da consulta);
- `GET /api/v1/admin/dlq/reenvios/{id}`: devolve o pedido, com a situação e as contagens.

As duas usam a política nova `PoliticaAdministracao` (só `Admin`; Cliente e
OperadorLogistica, 403). O endpoint só roteia: a regra fica no `ServicoReenvioDlq`
(Application), com a porta `IArmazenamentoReenvios` (Infrastructure, EF).

A lista de consumidores que a API aceita vem de uma constante compartilhada no Application,
`ConsumidoresConhecidos.Nomes` (`estoque`, `pagamento`, `entrega`), porque a API não registra
os consumidores. Um teste confere que ela bate com os descritores registrados no Worker
(`IDescritorConsumidor`, REQ-12), para um consumidor novo não ficar de fora.

No Worker, o serviço em segundo plano `ExecutorReenviosDlq` (Infrastructure):

- roda a cada `ReenvioDlq:IntervaloSegundos` (padrão 60), com `sp_getapplock 'oms-reenvio-dlq'`;
- com o lock, pega primeiro os pedidos `EmExecucao` (de uma execução que caiu, porque só quem
  tem o lock executa) e depois os `Pendente`, por `RegistradoEm`, um de cada vez (FR-010);
- abre o canal com o broker **antes** de marcar o pedido; se o broker não responder, o pedido
  continua `Pendente` (log 5010) e é tentado na rodada seguinte (spec, US2 cenário 11);
- com o canal aberto, marca `EmExecucao` com `IniciadoEm`, executa (R-04) e grava `Concluido`
  com as contagens, ou `Falhou` com o motivo e as contagens parciais (broker caiu no meio).
  Um pedido `Falhou` não é retomado; o Admin pede outro.

**Por quê**: preserva a fronteira (só o Worker usa o broker) e o princípio IV; o autor vem do
token; o histórico fica no banco; a concorrência entre instâncias se resolve pelo lock.

## R-04 Execução de um pedido no broker, sem perda e na ordem

**Decisão**: num canal com confirmações de publicação (`publisherConfirmationTrackingEnabled`):

1. lê até `ReenvioDlq:MensagensPorPedido` (padrão 1 000) mensagens da cabeça de
   `oms.C.dlq` com `BasicGetAsync(autoAck: false)`, guardando as entregas sem ack; lê no máximo
   as que estavam na DLQ no início (`MessageCount` do `QueueDeclarePassive`), e devolve sem
   contar uma mensagem que já traz o `x-pedido-reenvio` deste pedido: uma reenviada que falha
   de novo volta ao fim da DLQ durante a execução e não pode ser reenviada outra vez pelo mesmo
   pedido (achado do quickstart, 2026-10-25);
2. para cada uma, decide pelo `FiltroReenvio` (Application, R-05):
   - fora do filtro: conta e guarda para devolver;
   - atende ao filtro, mas `x-reenvios >= ReenvioDlq:MaximoReenvios`: conta como bloqueada e
     guarda para devolver;
   - atende e pode: publica em `oms.C` pela troca padrão, com os cabeçalhos do R-06,
     `mandatory: true`, espera a confirmação e só então dá `BasicAck` na entrega da DLQ;
3. no fim, devolve as guardadas com `BasicNack(requeue: true)`, em ordem; o RabbitMQ as recoloca
   na posição original, então a ordem das que ficam na DLQ não muda;
4. se o canal cai no meio, o broker devolve sozinho todas as entregas sem ack para a DLQ.

**Filas clássicas**: a recolocação na posição original vale para filas clássicas, que é como
o REQ-06 declara as DLQs (sem `x-queue-type`). Numa fila quorum, uma mensagem devolvida vai
para o fim, e esta estratégia precisaria mudar; uma migração futura para quorum exige rever o
R-04. O teste de ordem da T014 prova o comportamento.

**Garantias** (FR-014, SC-004): uma mensagem só sai da DLQ (ack) depois de confirmada na fila
original, então nunca se perde. O único caso de cópia dupla é a queda exata entre a
confirmação da publicação e o ack; a cópia que sobra na DLQ carrega o mesmo `MessageId`, e o
consumidor descarta a duplicada pelo registro de processado (REQ-06). É o mesmo "pelo menos
uma vez" do resto da mensageria.

**Alternativas rejeitadas**:
- consumir a DLQ com `BasicConsume` e republicar as não selecionadas no fim: muda a ordem e
  dá às não selecionadas um novo `x-falhou-em`;
- plugin Shovel do RabbitMQ: move tudo, sem filtro nem limite de reenvios;
- ler pela API de gestão HTTP: exige o plugin de gestão em produção e outra credencial.

## R-05 Filtro

**Decisão**: `FiltroReenvio(string? TipoEvento, DateTimeOffset? FalhouDe, DateTimeOffset?
FalhouAte, string? TrechoMotivo)` no Application, com
`bool Atende(string? tipo, DateTimeOffset? falhouEm, string? motivo)`:

- tipo: igualdade ordinal com a propriedade `type` da mensagem;
- período: `x-falhou-em` (ISO 8601 gravado pelo REQ-06) dentro de `[FalhouDe, FalhouAte]`;
  mensagem sem o cabeçalho não atende a um filtro de período;
- motivo: `x-motivo` contém o trecho, sem diferenciar maiúsculas; mensagem sem o cabeçalho não
  atende a um filtro de motivo (spec Edge Cases);
- campos vazios não filtram. Validação na API: `FalhouAte >= FalhouDe`, trecho com até 200
  caracteres, tipo com até 100.

## R-06 Cabeçalhos da mensagem reenviada

**Decisão**: a cópia publicada em `oms.C` mantém o corpo, o `MessageId`, o `Type`, o
`ContentType` e os cabeçalhos da DLQ, com estas mudanças:

| Cabeçalho | Valor |
|-----------|-------|
| `x-tentativas` | removido: as tentativas recomeçam do zero (FR-013) |
| `x-ultimo-erro` | removido |
| `x-reenvios` | anterior + 1 (FR-012) |
| `x-reenviado-em` | instante do reenvio, ISO 8601 |
| `x-pedido-reenvio` | identificador do `PedidoReenvioDlq` (FR-015) |
| `x-motivo`, `x-falhou-em` | mantidos até a próxima falha, que os sobrescreve |

Como o `ConsumidorEventos` copia os cabeçalhos originais ao mandar para a espera e para a DLQ,
`x-reenvios` acompanha a mensagem se ela falhar de novo (US2 cenário 8), sem mudança na base
de consumo. Os cabeçalhos são de transporte: o envelope v1 não muda (FR-022).

## R-07 Persistência

**Decisão**: uma migration, `LimpezaEReenvioDlq`:

- tabela `PedidosReenvioDlq`: `Id` (`uniqueidentifier`, v7), `AutorId` (`uniqueidentifier`),
  `Consumidor` (`nvarchar(100)`), `TipoEvento` (`nvarchar(100)` nulo), `FalhouDe` e
  `FalhouAte` (`datetimeoffset` nulos), `TrechoMotivo` (`nvarchar(200)` nulo), `Situacao`
  (texto, check com `Pendente`, `EmExecucao`, `Concluido`, `Falhou`), `RegistradoEm`,
  `IniciadoEm` e `ConcluidoEm` (`datetimeoffset`, os dois últimos nulos), `Reenviadas`,
  `BloqueadasPeloLimite` e `ForaDoFiltro` (`int`, padrão 0, `>= 0`), `MotivoFalha`
  (`nvarchar(500)` nulo); índice em (`Situacao`, `RegistradoEm`);
- índices para a limpeza: `IX_EventosSaida_Enviados` em `EnviadoEm` filtrado por
  `[EnviadoEm] IS NOT NULL`; `IX_EventosProcessados_ProcessadoEm`;
  `IX_RegistrosIdempotencia_CriadoEm`.

O `PedidoReenvioDlq` é uma entidade do Application (como o `RegistroIdempotencia` do REQ-05),
porque é operacional, não do domínio de pedidos.

## R-08 Aviso de tamanho da DLQ

**Decisão**: o `ExecutorReenviosDlq` não é o lugar (roda a cada minuto e só com pedidos). Um
serviço próprio, `MonitorDlq`, a cada `ReenvioDlq:AvisoIntervaloSegundos` (padrão 300), faz
`QueueDeclarePassiveAsync(oms.C.dlq)` para cada consumidor conhecido e lê `MessageCount`:

- acima de `ReenvioDlq:AvisoLimite` (padrão 10): log 5011 (Warning) com o consumidor e a
  quantidade;
- fila inexistente (broker recriado): conta como zero;
- broker fora: log 5012 (Warning) e tenta no próximo intervalo.

Não precisa de lock: ler o tamanho é idempotente, e duas instâncias só repetem o aviso. O
aviso tem chave própria, `ReenvioDlq:AvisoHabilitado` (padrão `true`), para desligar o reenvio
não desligar o aviso junto.

## R-09 Usuário próprio da API no broker

**Decisão**:

- o contêiner `rabbitmq` do `docker-compose.yml` passa a iniciar por um script versionado,
  `docker/rabbitmq/iniciar.sh` (LF, pelo `.gitattributes`), que sobe o servidor, espera ele
  responder e cria, de forma idempotente, o usuário da API a partir de
  `OMS_RABBITMQ_API_USUARIO` e `OMS_RABBITMQ_API_SENHA` (padrões `oms-api` e
  `oms-api-dev-local`), com `rabbitmqctl set_permissions -p / oms-api "^$" "^$" "^$"`: pode
  abrir a conexão no vhost, mas nenhum recurso casa com os padrões de configurar, escrever e
  ler. O usuário do Worker continua o `RABBITMQ_DEFAULT_USER`;
- como o script roda a cada subida, um ambiente local existente ganha o usuário sem recriar o
  volume;
- a API passa a usar `RabbitMQ:Usuario`/`Senha` com o usuário próprio no compose (perfil
  `demo`), no `appsettings.Development.json` da API e no `.env.example`;
- nos testes, o `MensageriaTeste` cria o mesmo usuário no contêiner do Testcontainers
  (`ExecAsync` com `rabbitmqctl`), e a fábrica da API passa a usá-lo.

**Teste** (FR-019, SC-008): com as credenciais da API, abrir a conexão funciona, e
`ExchangeDeclare`, `QueueDeclare`, `BasicPublish` numa troca existente e `BasicGet` numa fila
existente são recusados com `ACCESS_REFUSED`. Um teste de arquitetura confere que a API não
registra nenhum serviço de mensageria do Worker (`ConexaoRabbitMQ`, despachante, consumidores).

**Alternativas rejeitadas**:
- arquivo de definições (`load_definitions`): exige o hash da senha no arquivo, e as senhas
  configuráveis por variável não chegariam a ele;
- serviço extra de uma vez só (`curl` na API de gestão): complica o `docker compose up
  --wait` e depende do plugin de gestão;
- tirar a prontidão do broker da API: a prontidão da API deixaria de mostrar o broker, que o
  REQ-02 pediu.

## R-10 Configuração

**Decisão**: duas seções novas, validadas na partida como as demais:

`Limpeza`:

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Habilitada` | `true` | — |
| `IntervaloSegundos` | 3 600 | 60 a 86 400 |
| `TamanhoLote` | 1 000 | 1 a 10 000 |
| `LotesPorRodada` | 10 | 1 a 1 000 |
| `RetencaoEnviadosHoras` | 168 | 1 a 8 760 |
| `RetencaoProcessadosHoras` | 168 | 24 a 8 760 |
| `RetencaoChavesHoras` | 48 | 24 a 8 760 |
| `RetencaoPedidosReenvioHoras` | 2 160 | 24 a 87 600 |

`ReenvioDlq`:

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Habilitado` | `true` | — |
| `IntervaloSegundos` | 60 | 1 a 3 600 |
| `MaximoReenvios` | 3 | 1 a 100 |
| `MensagensPorPedido` | 1 000 | 1 a 10 000 |
| `AvisoLimite` | 10 | 1 a 1 000 000 |
| `AvisoIntervaloSegundos` | 300 | 10 a 86 400 |
| `AvisoHabilitado` | `true` | — |

A `FabricaWorker` dos testes desliga `Limpeza:Habilitada` e `ReenvioDlq:Habilitado` por padrão,
como já faz com a reconciliação e os estornos.

## R-11 Logs

| Id | Nome | Nível | Quando |
|----|------|-------|--------|
| 4006 | `PedidoReenvioRegistrado` | Information | API registrou o pedido (id, autor, consumidor) |
| 5009 | `ReenvioDlqConcluido` | Information | Pedido concluído (id, autor, consumidor, filtro, contagens) |
| 5010 | `ReenvioDlqFalhou` | Warning | Pedido com falha (id, consumidor, contagens parciais, erro) |
| 5011 | `DlqAcimaDoLimite` | Warning | DLQ acima do limite (consumidor, quantidade, limite) |
| 5012 | `FalhaVerificacaoDlq` | Warning | Verificação da DLQ falhou |
| 5013 | `LimpezaConcluida` | Information se apagou; Debug se não | Fim da rodada (quantidade por alvo) |
| 5014 | `FalhaLimpeza` | Warning | Rodada de limpeza falhou |
| 5015 | `RodadaIgnorada` | Debug | Outra instância tem o lock (limpeza ou reenvio; o nome da rotina no log) |

Sem dados pessoais: o autor é o identificador do usuário, não o e-mail.

## R-12 Testes

- **Unitários**: `FiltroReenvio` (cada campo, combinações, cabeçalhos ausentes);
  `PedidoReenvioDlq` (transições de situação); `ServicoReenvioDlq` (validação, consumidor
  desconhecido, registro com autor); opções das duas seções; leitura e montagem dos
  cabeçalhos de reenvio.
- **Integração** (SQL Server e RabbitMQ reais):
  - limpeza: o que é e o que não é apagado em cada alvo, pendente antigo intocado, chave com
    menos de 24 h intocada, lotes (o resto fica para a rodada seguinte), duas instâncias com o
    lock, log 5013;
  - rotas: 202 e registro com autor; 401, 403 (Cliente e OperadorLogistica) nas duas rotas;
    400 para consumidor desconhecido e filtro inválido; 404 na consulta; a rota funciona com
    o broker fora; conformidade do OpenAPI;
  - execução: filtros, ordem preservada das que ficam, limite de reenvios, mensagem reenviada
    processada pelo consumidor, falha de novo volta com `x-reenvios`, duas instâncias, pedido
    `EmExecucao` retomado, broker fora no meio (falha com contagens parciais, nada perdido);
  - aviso 5011 acima do limite e silêncio abaixo;
  - permissões do usuário da API (R-09).
