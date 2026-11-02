# Research: gateway de pagamento por HTTP com API simulada

**Requisito**: REQ-19 (issue [#46](https://github.com/LucasFrenhan/Order-Management-System/issues/46)) | **Data**: 2026-11-02 | **Spec**: [spec.md](./spec.md)

Decisões técnicas do plano. Cada uma tem a decisão, o motivo e as alternativas descartadas.
Os valores padrão aqui são os que a spec deixou para o plano (Assumptions).

## R-01 Onde fica a API simulada

**Decisão**: projeto novo `src/PedidoFulfillment.PagamentosSimulados` (ASP.NET Core Minimal API,
.NET 10), **sem referência a nenhum projeto do OMS**, com `Dockerfile` próprio, como serviço
`pagamentos` no `docker-compose.yml`, fora de perfil (sobe com `docker compose up -d --wait`,
como o banco e o broker), na porta local `5232`. No perfil `demo`, o Worker o chama pela rede
interna (`http://pagamentos:8080`). Classe `ProgramaPagamentos` com `Main` explícito, como o
`ProgramaWorker`, para não haver dois `Program` globais no projeto de testes.

**Motivo**: a spec pede um serviço separado (FR-001), que se comporte como externo. Sem
referência ao OMS, o contrato HTTP é a única ligação, como num gateway real. Fora de perfil,
porque o Worker pelo SDK também precisa dele (FR-009a).

**Alternativas**: WireMock.Net (pacote novo e comportamento por stubs, sem a latência sorteada
e a rota de controle pedidas); hospedar a API simulada dentro da API do OMS (deixaria de ser
externa e misturaria autenticação); perfil próprio no compose (o Worker pelo SDK ficaria sem
gateway por padrão).

## R-02 Contrato HTTP da API simulada

**Decisão**: rotas versionadas em `/v1`, JSON, chave de idempotência no cabeçalho
`Idempotency-Key` (o id do pedido), credencial em `X-Api-Key`. A recusa de negócio é um
**resultado** (`201`/`200` com `situacao: "recusada"`), não um erro HTTP. Falhas técnicas usam
os códigos que um cliente HTTP real trata: `503`, `429` com `Retry-After`, `500`, `401`, `400`,
`409` com `codigo` (`operacao-em-andamento` com `Retry-After`, ou `chave-reutilizada`), `404`
na consulta. Detalhes em [contracts/api-pagamentos-simulada.md](./contracts/api-pagamentos-simulada.md).

**Motivo**: separar o resultado de negócio das falhas no próprio código HTTP deixa a
classificação do FR-010 direta e testável. `Idempotency-Key` e `Retry-After` são as convenções
de mercado (IETF draft *Idempotency-Key*, RFC 9110).

**Alternativas**: `402` para a recusa (como o Stripe): misturaria resultado e erro na mesma
faixa, e a recusa não deve abrir circuito nem ser repetida; corpo sem código no `409`: o OMS
não distinguiria "em andamento" (passageira) de "chave reutilizada" (permanente).

## R-03 Comportamento simulado: sorteio, modo forçado e roteiro

**Decisão**: a cada chamada de cobrança ou estorno, a API decide o comportamento nesta ordem:

1. **roteiro**: se há comportamentos enfileirados pela rota de controle, consome o próximo
   (para os testes: "indisponível, depois aprova");
2. **modo forçado**: se há um modo ativo pela rota de controle, aplica-o a todas as chamadas;
3. **sorteio**: pelas chances da configuração (ou as trocadas pela rota de controle).

Comportamentos: `aprovar`, `recusar`, `indisponivel` (503), `limite-taxa` (429 com
`Retry-After`), `erro-interno` (500), `sem-resposta` (segura a resposta por
`SemRespostaMs`, sem processar), `processar-sem-responder` (efetiva, guarda e segura a
resposta). A latência é sorteada à parte: com chance `CaudaPercentual`, entre `CaudaMinimoMs`
e `CaudaMaximoMs`; senão, entre `LatenciaMinimaMs` e `LatenciaMaximaMs`. O modo determinístico
(`Simulacao:Deterministico = true`) zera as chances de falha e a latência e só aprova, a menos
que o roteiro ou o modo forçado digam outra coisa (FR-007). `Semente` opcional fixa o sorteio.

**Padrões**: latência 40–250 ms; cauda de 5% entre 1–5 s; aprovação 90% (o restante das
respostas efetivas é recusa); indisponível 2%; limite de taxa 1% (`Retry-After: 1`); erro
interno 1%; sem resposta 1%; processar sem responder 1%; `SemRespostaMs` 15000.

**Motivo**: o sorteio atende a demonstração (FR-003, FR-004); o roteiro dá aos testes
determinismo exato por chamada sem depender de valores mágicos; o modo forçado atende a
demonstração "fora do ar" (FR-007a, clarify).

**Alternativas**: cenários pelo valor da cobrança (descartado no specify); só configuração na
partida (descartado no specify).

## R-04 Armazenamento da API simulada

**Decisão**: `ConcurrentDictionary<(TipoOperacao, string chave), OperacaoPagamento>` em memória
(clarify). A primeira chamada de uma chave cria a operação em `EmAndamento` (com
`TryAdd`, atômico); uma chamada concorrente com a mesma chave recebe `409
operacao-em-andamento`. Concluída, a operação guarda o resultado; uma nova chamada com a mesma
chave devolve o resultado com `200` e o cabeçalho `Idempotent-Replayed: true`. Chave igual com
valor diferente: `409 chave-reutilizada`. Uma operação que terminou em falha sem efetivar
(`503`, `429`, `500`, `sem-resposta`) é **removida**, para a próxima tentativa ser uma operação
nova. Cada operação conta `Efetivacoes` (sempre 0 ou 1), lida pela rota de controle nos
testes (SC-002).

**Motivo**: é o comportamento de idempotência de um gateway real, e o contador dá aos testes a
prova de "no máximo uma cobrança efetiva".

**Alternativas**: SQLite ou base no SQL Server (descartadas no clarify).

## R-05 Autenticação da API simulada

**Decisão**: duas chaves de configuração obrigatórias, `Seguranca:ChaveApi` (cobrança, estorno
e consulta) e `Seguranca:ChaveControle` (rotas `/controle`), diferentes entre si e com pelo
menos 16 caracteres, validadas na partida. Comparação em tempo constante
(`CryptographicOperations.FixedTimeEquals`). Sem a chave certa: `401` com corpo
`ProblemDetails`, sem dizer qual chave faltou. `/health` é público. Nenhuma chave em log: o
middleware de log da API simulada não registra cabeçalhos. No compose, valores de
desenvolvimento por variável `OMS_PAGAMENTOS_*`, como as demais credenciais locais.

**Motivo**: clarify (FR-002, FR-007a); o OMS recebe só a `ChaveApi`.

**Alternativas**: JWT do OMS (acoplaria o serviço "externo" à identidade do OMS); mTLS
(complexidade sem ganho para a simulação).

## R-06 Cliente HTTP e resiliência no OMS

**Decisão**: `GatewayPagamentoHttp` (Infrastructure) implementa `IGatewayPagamento` sobre um
`HttpClient` tipado de `IHttpClientFactory`, com o pipeline padrão de
**`Microsoft.Extensions.Http.Resilience`** (`AddResilienceHandler` com as estratégias, nesta
ordem de fora para dentro): **tempo total** por chamada → **novas tentativas** → **circuito** →
**tempo por tentativa**.

| Estratégia | Padrão | Configuração |
|------------|--------|--------------|
| Tempo total da chamada (todas as tentativas e esperas) | 10 s | `GatewayPagamento:TempoTotalMs` |
| Tentativas por chamada (incluindo a primeira) | 3 | `GatewayPagamento:Tentativas` |
| Espera entre tentativas | exponencial com jitter, base 300 ms; `Retry-After` vence se informado, limitado ao tempo total | `GatewayPagamento:EsperaBaseMs` |
| Tempo por tentativa | 3 s | `GatewayPagamento:TempoTentativaMs` |
| Circuito: proporção de falhas na janela | 50% | `GatewayPagamento:Circuito:ProporcaoFalhas` |
| Circuito: mínimo de chamadas na janela | 6 | `GatewayPagamento:Circuito:MinimoChamadas` |
| Circuito: janela de amostragem | 30 s | `GatewayPagamento:Circuito:JanelaSegundos` |
| Circuito: pausa | 30 s | `GatewayPagamento:Circuito:PausaSegundos` |

As novas tentativas e o circuito tratam como falha só a **falha passageira** (R-07); a recusa
de negócio e o erro permanente nunca são repetidos nem contam para o circuito.

**Pacote novo**: `Microsoft.Extensions.Http.Resilience` (10.x, da Microsoft, traz o Polly 8).
**Justificativa** (constituição, "dependência justificada"): é a biblioteca oficial de
resiliência do `IHttpClientFactory`; já entrega o jitter, o respeito ao `Retry-After`, o
circuito com estado meio-aberto, os tempos por tentativa e total e a telemetria das estratégias,
testados pela Microsoft. Escrever isso à mão seria mais código próprio, mais testes e menos
parecido com o que um projeto real usa, que é o objetivo do REQ.

**Alternativas**: política própria (retry e circuito escritos no projeto): sem pacote, mas
reimplementa o Polly com mais risco; `Polly` direto, sem a integração HTTP: perde o
`Retry-After` pronto e a integração com o `IHttpClientFactory`; `AddStandardResilienceHandler`:
traz um *rate limiter* e *hedging* que não se aplicam, e a ordem e os padrões precisariam ser
desfeitos.

## R-07 Classificação das respostas e falhas (FR-010)

| Situação | Classe | Novas tentativas | Circuito | No OMS |
|----------|--------|------------------|----------|--------|
| `201`/`200` com `aprovada` / `confirmado` | resultado | não | sucesso | aprovação / estorno confirmado |
| `201`/`200` com `recusada` / `recusado` | resultado | não | sucesso | recusa (como hoje) |
| `503`, `500`, `502`, `504` | passageira | sim | falha | — |
| `429` | passageira | sim, esperando o `Retry-After` | falha | — |
| `409 operacao-em-andamento` | passageira | sim, esperando o `Retry-After` | não conta | — |
| tempo da tentativa esgotado (`TimeoutRejectedException`) | **incerto** (a requisição pode ter sido processada) | sim, com a mesma chave | falha | — |
| conexão recusada, DNS, conexão perdida (`HttpRequestException`) | passageira; incerto se a requisição já tinha sido enviada | sim | falha | — |
| circuito aberto (`BrokenCircuitException`) | passageira, sem chamada | não | — | `GatewayPagamentoIndisponivelException` (R-09) |
| `400`, `401`, `403`, `409 chave-reutilizada`, corpo fora do contrato | **permanente** | não | não conta | `FalhaGatewayPagamentoException` (Permanente) |
| tempo total esgotado ou tentativas esgotadas | passageira | — | — | `FalhaGatewayPagamentoException` (Passageira) |

Exceções novas no Application (`Pagamentos/`): `FalhaGatewayPagamentoException` com
`TipoFalhaGateway` (`Passageira`, `Permanente`) e
`GatewayPagamentoIndisponivelException` (circuito aberto, com `DisponivelEm`), que herda de
`DependenciaIndisponivelException` (R-09). Os chamadores continuam vendo "exceção = falha
técnica" (contrato de hoje do `IGatewayPagamento`), e o que muda é só o tratamento do circuito.

**Resultado incerto (FR-013)**: o OMS **não grava nada** e repete com a mesma chave; a API
devolve o resultado original (R-04). Se as tentativas se esgotam, a decisão fica para a próxima
entrega da mensagem, para a reconciliação ou para a rodada de estornos, sempre com a mesma
chave. Não é preciso uma consulta antes de repetir: repetir com a mesma chave **é** a consulta
idempotente. A consulta explícita fica para o FR-015 (R-08).

## R-08 Pedido cancelado sem pagamento: consulta pela chave (FR-015)

**Decisão**: o `IGatewayPagamento` ganha `ConsultarCobrancaAsync(pedidoId)`, que devolve
`ConsultaCobranca` (`NaoEncontrada`, `Aprovada(transacao)`, `Recusada`). No
`ProcessamentoPagamento`, quando a mensagem encontra o pedido `Cancelado` **sem pagamento e com
o estoque já devolvido pelo cancelamento**, o consumidor consulta a cobrança: se `Aprovada`,
chama `Pedido.RegistrarCobrancaAposCancelamento` (REQ-13, R-07, já existe) e o estorno segue
pelo fluxo 10; se `NaoEncontrada` ou `Recusada`, ignora como hoje (log 7003). Falha da consulta
segue a classificação do R-07.

A reconciliação não lista pedidos cancelados (só os parados numa etapa), então o caso só
aparece pela reentrega da mensagem de pagamento, que é exatamente o cenário da limitação do
fluxo 5. A consulta acontece só para pedidos cancelados que ainda recebem um
`estoque.reservado`, o que é raro.

**Alternativas**: consultar em todo pedido cancelado pela reconciliação (exigiria listar
cancelados, sem ganho); não consultar e manter a limitação (contraria a issue).

## R-09 Circuito aberto pausa o consumo de pagamento (FR-016a)

**Decisão**:

- Application/Mensageria ganha `DependenciaIndisponivelException(DateTimeOffset
  disponivelEm)`, genérica.
- Na base de consumo (`ConsumidorEventos`), essa exceção **não** passa pela política de novas
  tentativas: a mensagem recebe `BasicNack(requeue: true)` (sem contar tentativa, sem DLQ), e a
  sessão termina com o motivo `pausa` e o instante de retomada.
- O `SupervisorInscricao` trata o motivo `pausa`: marca o consumidor como **pausado** (não como
  "sem inscrição"), espera até o instante de retomada e abre a sessão de novo. As outras
  mensagens já entregues (prefetch) voltam à fila quando o canal fecha, como no REQ-17.
- A prontidão (`/health/pronto`) considera **pausado como saudável**: é uma decisão do OMS, não
  uma perda (o Worker não deve ser reiniciado por isso).
- Na retomada, a primeira mensagem é a chamada de teste do circuito meio-aberto: se a API
  responde, o circuito fecha; se falha, abre de novo e o consumo pausa de novo.
- Os outros consumidores não são afetados: a pausa é por inscrição.
- Logs 5020 `ConsumoPausado` (consumidor, até quando) e 5021 `ConsumoRetomado` (consumidor,
  duração da pausa).

**Estornos**: `ServicoEstornos` trata `GatewayPagamentoIndisponivelException` como
`ResultadoRodadaEstorno.Adiado` (novo), **sem** contar tentativa; o `ProcessadorEstornos`
encerra a rodada no primeiro `Adiado` (os demais também seriam adiados). **Reconciliação**:
nada muda; a exceção conta como falha do ciclo (spec, US5 cenário 7).

**Alternativas**: pausar pelo `BasicCancel` mantendo o canal (mais estado para o supervisor
coordenar com o REQ-17); manter a inscrição e devolver cada mensagem com `nack` (laço quente de
reentregas durante a queda).

## R-10 A chamada continua dentro da transação do consumidor (FR-019)

**Decisão**: a chamada ao gateway continua dentro da transação aberta pela base de consumo,
limitada pelo **tempo total da chamada** (R-06, 10 s). Antes da chamada, o consumidor só lê
(o registro de evento processado e o pedido, em `READ COMMITTED`, que não segura bloqueios de
leitura depois da instrução); as gravações vêm depois da resposta. O efeito é uma conexão do
pool ocupada por até 10 s por mensagem em tratamento, limitada pelo prefetch do consumidor de
pagamento.

**Motivo**: o FR-019 pede que a chamada não segure recursos do banco por mais que o tempo total
da chamada, e isso já vale. Tirar a chamada da transação exigiria dividir o manipulador em duas
fases na base de consumo do REQ-06, que todos os consumidores usam, para um ganho pequeno no
volume do projeto. A decisão e o custo ficam registrados no fluxo 14.

**Alternativas**: chamar antes de abrir a transação (manipulador em duas fases, mudança na base
de todos os consumidores); outbox de cobrança (comando assíncrono para o gateway, fora do
escopo).

## R-11 Gateway em memória só nos testes (FR-009a)

**Decisão**: o `GatewayPagamentoSimulado` sai do Infrastructure e vai para o projeto de testes
de integração, como `GatewayPagamentoMemoria`, com a taxa de aprovação na chave de teste
`GatewayPagamentoMemoria:TaxaAprovacaoPercentual`. A `FabricaWorker` o registra por padrão, no
lugar do `GatewayPagamentoHttp`; os testes do REQ-19 passam `ApiPagamentos` (a URL da API
simulada real) e usam o gateway HTTP. O Worker em produção registra só o
`GatewayPagamentoHttp` (`AdicionarGatewayPagamento`), e a configuração não tem como escolher o
gateway em memória. Os testes unitários do gateway em memória migram para o projeto de
integração ou são substituídos pelos da API simulada.

## R-12 Telemetria do HTTP de saída (FR-020 a FR-022)

**Decisão**:

- **Traces**: `AddSource("System.Net.Http")` no `ExtensoesObservabilidade`. O .NET 10 emite o
  span de cliente HTTP nativamente (com `http.request.method`, `server.address`, `url.full` com
  a query ocultada por padrão, `http.response.status_code`, `error.type`) e propaga o
  `traceparent`. Cada tentativa é um span de cliente, filho do span `cobrar pagamento` ou
  `estornar pagamento` (novo, no `TelemetriaOms`), que leva `oms.pedido.id`,
  `oms.pagamento.operacao`, `oms.resultado` e o total de tentativas. O número de cada tentativa
  vai no atributo `oms.pagamento.tentativa` do span de cliente, por um `DelegatingHandler`
  depois do pipeline de resiliência. Isso vale também para os webhooks do REQ-18, que passam a
  ter span de saída (acréscimo, sem dado sensível, pela query ocultada e sem cabeçalhos).
- **Sem pacote** `OpenTelemetry.Instrumentation.Http`: a fonte nativa basta.
- **Métricas** (`TelemetriaOms`): `oms.pagamentos.chamadas` (contador, `oms.pagamento.operacao` =
  `cobranca|estorno|consulta`, `oms.resultado` = `aprovado|recusado|confirmado|nao-encontrado|
  passageira|incerto|permanente|circuito-aberto`), `oms.pagamentos.duracao` (histograma em
  segundos, por operação e resultado, a chamada inteira com as tentativas) e
  `oms.pagamentos.circuito` (gauge: 0 fechado, 1 meio-aberto, 2 aberto). O medidor nativo
  `System.Net.Http` entra em `MedidoresNativos` (duração por requisição).
- **Logs** (`EventosGatewayPagamento`, 7013–7020): 7013 `FalhaPassageiraGateway` (Warning:
  pedido, operação, tipo, tentativa), 7014 `ResultadoIncertoGateway` (Warning), 7015
  `ErroPermanenteGateway` (Error: pedido, operação, status/código), 7016 `CircuitoAberto`
  (Error: pausa, até quando), 7017 `CircuitoMeioAberto` (Information), 7018 `CircuitoFechado`
  (Information), 7019 `ConsultaCobranca` (Information: pedido, resultado), 7020
  `EstornosAdiados` (Warning: quantos ficaram para a próxima rodada). Nenhum log leva a chave
  da API, o identificador de transação, o valor da cobrança junto com dado pessoal ou o corpo.
- **API simulada**: não exporta telemetria (clarify); só lê o `traceparent` para os próprios
  logs de console.

## R-13 Testes

- **Unitários**: classificação (tabela do R-07, com respostas montadas), opções do gateway e
  da API simulada, decisão do comportamento simulado (roteiro → modo → sorteio, com semente),
  armazenamento idempotente da API simulada (concorrência com a mesma chave, chave reutilizada,
  remoção da falha não efetiva), pausa no `SupervisorInscricao` com `TimeProvider` falso.
- **Integração**: a API simulada real em Kestrel numa porta local, pelo
  `WebApplicationFactory<ProgramaPagamentos>` com `UseKestrel` (.NET 10), modo determinístico e
  roteiro por teste. Cenários do FR-024, conferindo `Efetivacoes <= 1` pela rota de controle;
  "processar sem responder" com o Worker reiniciado (nova `FabricaWorker`) entre as tentativas;
  circuito aberto com o consumo pausado (mensagens continuam na fila, nenhuma na DLQ) e
  retomado; webhooks e demais testes seguem com o gateway em memória.
- Para manter a suíte rápida, os testes de integração usam tempos curtos (tentativa 300 ms,
  total 1,5 s, pausa 1 s) e `SemRespostaMs` de 2 s.

## R-14 Documentos de fluxo

**Decisão**: documento novo `docs/fluxos/14-gateway-de-pagamento.md` (a chamada, a
classificação, as tentativas, o resultado incerto, o circuito e a pausa, a API simulada e a
rota de controle, logs e métricas, limitações), com linha no índice. Atualizações: fluxo 5
(sem a limitação da cobrança não consultável; identificador vindo da API; link para o 14),
fluxo 8 (pausa por dependência indisponível na base de consumo), fluxo 9 (circuito aberto
conta como falha do ciclo), fluxo 10 (estornos adiados com o circuito aberto; sem a limitação
das chaves em memória do Worker, substituída pela da API reiniciada) e fluxo 12 (span e
métricas do HTTP de saída; sai a limitação "não há instrumentação de HTTP de saída").
O README ganha a API simulada no ambiente local, e o `docs/proximos-passos.md` registra o
REQ-19.
