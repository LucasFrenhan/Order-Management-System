[← Voltar ao README](../README.md)

# Otimização do CI

Estudo do tempo do job "Build e testes": as medições locais que justificaram a
[issue #70](https://github.com/LucasFrenhan/Order-Management-System/issues/70) e as propostas
adotadas (1 a 4; a 3 fica para depois). Nada aqui muda as garantias do CI: build com avisos como
erros, testes unitários e de integração com Docker obrigatório, cobertura e análise do
SonarQube Cloud. O funcionamento atual do CI está em [Integração contínua](integracao-continua.md).

## Situação no CI (PR do REQ-22, run 41732771799, 2026-11-03)

| Passo do job "Build e testes" | Tempo |
|---|---|
| Setup, caches, ferramentas, início do Sonar | ~30 s |
| Restaurar + Compilar | ~37 s |
| **Testar** | **~9 min 50 s** |
| Encerrar o Sonar + artefatos | ~25 s |
| **Total do job** | **~11 min 30 s** |

Dentro de "Testar" (dos `.trx`):

- Unitários: 1.092 testes em 12 s.
- Integração: 665 testes em ~590 s de parede, com ~937 s somados: concorrência média de **1,6**
  num runner de 4 vCPUs.
- **141 s sem nenhum teste rodando no início**: o build de `docker/sqlserver/Dockerfile` (FTS,
  REQ-22), que reinstala o motor do SQL Server por `apt-get` a cada execução. A imagem final
  tem ~5 GB. Antes do REQ-22, o job levava ~8 min.
- Caminho crítico nas coleções seriais do xUnit:

  | Coleção | Testes | Soma | Janela |
  |---|---|---|---|
  | `ColecaoMensageria` | 177 | 384 s | 155–538 s |
  | `ColecaoBancoDados` | 205 | 273 s | 151–424 s |
  | `ColecaoQuedaBroker` | 11 | 199 s | 155–353 s |
  | `ColecaoObservabilidade` (serial, no fim) | 28 | 40 s | 546–586 s |

## Propostas

### 1. Imagem do SQL Server com FTS pronta

Publicar a imagem no GHCR com tag pelo hash do Dockerfile, num workflow que só roda quando o
Dockerfile muda. Os testes usam a imagem indicada em `OMS_IMAGEM_SQLSERVER` sem montar nada e,
sem a variável, montam localmente como hoje. O job de Demonstração aproveita a mesma imagem.
Alternativa sem registry: cache do buildx (`type=gha`), provavelmente mais lento para uma
camada de ~2,7 GB.

### 2. Mais paralelismo nos testes de integração

- `ColecaoBancoDados`: cada teste já cria o próprio banco (`oms_{guid}`); o contêiner vira um
  *assembly fixture* do xUnit v3 e as classes rodam em paralelo. Os testes de desempenho
  (`Desempenho*`, com limites de 500 ms a 1 s) ficam numa coleção serial, no mesmo contêiner.
- `ColecaoMensageria`: as filas do broker são compartilhadas, então a coleção é dividida em
  três, por área, cada uma com seu par SQL Server + RabbitMQ:
  - A: PagamentosHttp, Pagamentos, Estornos (~136 s)
  - B: Reconciliação, Estoque, Administração (~133 s)
  - C: Notificações, Entregas, Mensageria (~115 s)

### 3. Banco a partir de um modelo (a medir)

341 pontos criam um banco e aplicam 13 migrations. Migrar uma vez por fixture e restaurar um
backup a cada teste. Medir o custo de `NovoBancoMigradoAsync` antes.

### 4. Esperas de `RecuperacaoConsumoTestes`

199 s em 11 testes que esperam o backoff real de reconexão. Vira caminho crítico depois do
item 2.

### Fora do escopo

Compilação, restore e Sonar somam ~1 min e têm pouco a ganhar. Dividir em vários jobs complica
a cobertura do Sonar (o build precisa ficar entre `begin` e `end`).

## Como medir

Para refazer as medições, por exemplo quando a suíte crescer e o tempo voltar a subir:

1. **Compilar uma vez** e medir só os testes, com o mesmo comando do CI e o tempo total por
   fora (PowerShell: `Measure-Command { ... }`; Git Bash: `time`):

   ```bash
   dotnet build PedidoFulfillment.sln -c Release
   OMS_EXIGIR_DOCKER=true dotnet test PedidoFulfillment.sln -c Release --no-build \
     --logger "trx;LogFilePrefix=resultado" --results-directory medicao-1 \
     --collect "Code Coverage;Format=cobertura" --settings coverage.runsettings
   ```

2. **Simular outro número de threads** sem mexer no `xunit.runner.json`: acrescente
   `-- xUnit.MaxParallelThreads=4` ao fim do `dotnet test` (4 é o padrão do xUnit num runner de
   4 vCPUs). O modo agressivo do xUnit se testa com `xUnit.ParallelAlgorithm=aggressive`.

3. **Ler os `.trx`** com o script versionado:

   ```bash
   bash .github/scripts/medir-testes.sh medicao-1
   ```

   Para cada projeto, ele mostra a parede (do primeiro teste ao último), a soma das durações,
   a **concorrência média** (soma ÷ parede: perto de 1 quer dizer testes em fila), a **maior
   lacuna** sem nenhum teste rodando (contêiner subindo ou imagem sendo montada) e as classes
   que mais pesam, com a janela em que rodaram. Uma classe com soma alta e janela longa numa
   coleção serial é o caminho crítico. O mesmo script lê os `.trx` do CI, que ficam no artefato
   `resultados-testes` de cada run (`gh run download <run> -n resultados-testes`).

4. **Medir a imagem do SQL Server a frio** com outra tag, para não apagar a do cache:

   ```bash
   docker build --no-cache -t oms-sqlserver-medicao:frio docker/sqlserver
   docker rmi oms-sqlserver-medicao:frio
   ```

5. **Repetir** a configuração escolhida algumas vezes (aqui foram 6) para separar ganho de
   variação, e rodar sozinho, várias vezes, qualquer teste que falhar
   (`--filter "FullyQualifiedName~NomeDaClasse"`), para saber se a falha já existia.

A máquina local costuma ter mais CPUs que o runner: o número que vale é o do CI, que o resumo
da execução e o artefato `resultados-testes` mostram.

## Medições locais

Máquina: Windows 11, Docker 29.9.2 com 16 CPUs e 16 GB; mesmo comando de teste do CI
(Release, `--collect "Code Coverage"`, `OMS_EXIGIR_DOCKER=true`).

Medições de 2026-11-03. Os tempos são da execução de `dotnet test` da solução inteira; a parede
de integração vem dos `.trx`.

### Proposta 1: imagem pronta

| Cenário | Tempo |
|---|---|
| Build a frio (`--no-cache`): `apt-get` 80 s + gravar camadas 42 s | **124 s** |
| Build com o cache local do Docker | 1 s |
| Imagem exportada (`docker save`) | 1,4 GB |

Localmente não há o que ganhar: a imagem já fica no cache. No CI, cada run paga o build a frio
(141 s medidos no run 41732771799), que trocaria pelo pull de ~1,4 GB do GHCR. **Ganho
esperado no CI: ~1,5 a 2 min**, a confirmar no runner (o tempo do pull não dá para medir
localmente). O código já aceita a imagem pronta por `OMS_IMAGEM_SQLSERVER`.

### Proposta 2: paralelismo

| Rodada | `maxParallelThreads` | Total | Integração (parede) | Concorrência | Falhas |
|---|---|---|---|---|---|
| Base (`main`) | 16 (padrão local) | **510 s** | 500 s | 1,92 | 1 (`BuscaCatalogoTestes`, já existente) |
| Proposta 2 | 16 | 330 s (−35%) | 314 s | 4,92 | 2 (busca FTS: índice atrasado) |
| Proposta 2 | 4 (igual ao CI) | 451 s (−12%) | 435 s | 2,52 | 0 |
| Proposta 2, `parallelAlgorithm=aggressive` | 4 | 317 s (−38%) | 301 s | 9,59 | 4 (limites de tempo estourados) |
| **Proposta 2** | **8 (`"2x"` no CI)** | **352 s (−31%)** | 338 s | 3,79 | 1 (`BuscaCatalogoTestes`, já existente) |

Conclusões:

- Sem mexer no número de threads, o ganho no CI seria pequeno: o xUnit usa uma thread por
  vCPU (4 no runner), e os testes passam a maior parte do tempo esperando I/O.
- Com 8 threads, o ganho local é de 31% e nada novo quebra. Com mais do que isso, os testes com
  limite de tempo (desempenho, login, saúde) e a espera do índice full-text começam a falhar.
- O caminho crítico passa a ser `ColecaoQuedaBroker` (`RecuperacaoConsumoTestes`, ~240 s em 11
  testes), seguida da `ColecaoObservabilidade`, serial no fim (~50 s). É a proposta 4.
- Ressalva: a máquina local tem 16 CPUs para os contêineres; o runner tem 4. O ganho com 8
  threads precisa ser confirmado no CI.

### Achado: `BuscaCatalogoTestes` era instável (corrigido)

Falhou na base, sem nenhuma mudança: `'cafe': esperado [BUS-002], veio []`. O teste esperava
só o último produto (`BUS-008`) aparecer na busca e supunha que os anteriores já estavam
indexados, mas o Full-Text Search processa as mudanças em lote, sem ordem garantida. Agora ele
espera cada produto aparecer pelo nome antes de conferir os casos: 5 de 5 rodadas isoladas e a
rodada completa abaixo sem falha.

### Proposta 4: testes de queda do broker em duas coleções

`RecuperacaoConsumoTestes` (11 testes, ~210 s, todos na `ColecaoQuedaBroker`) virou uma base
abstrata com os auxiliares (`CenarioRecuperacaoConsumo`) e duas classes, cada uma numa coleção
com o próprio broker: `RecuperacaoBrokerTestes` (queda longa, quedas seguidas, broker zerado,
conexão e fila apagadas, prontidão) e `RecuperacaoConsumoTestes` (pedido com o broker fora,
mensagem em processamento, DLQ, credenciais, encerramento). O corpo dos testes, as esperas
(30 s de queda longa) e o backoff do Worker não mudaram.

| Rodada | `maxParallelThreads` | Total | Integração (parede) | Concorrência | Falhas |
|---|---|---|---|---|---|
| Base (`main`) | 16 | 510 s | 500 s | 1,92 | 1 (busca, instável) |
| Proposta 2 | 8 | 352 s | 338 s | 3,79 | 1 (busca, instável) |
| **Propostas 2 + 4 + correção da busca** | **8** | **326 s (−36%)** | 307 s | 4,04 | **0** |

As duas metades levaram 103 s e 114 s; a segunda só começou aos 95 s, esperando uma das 8
threads. O caminho crítico agora é `ColecaoMensageria` (189 s), seguida da
`ColecaoObservabilidade`, que roda sozinha no fim (68 s, `DisableParallelization`). São os
próximos alvos, se valer a pena.

### Estimativa para o CI

| | Hoje | Com 1 + 2 + 4 (medido com 8 threads; adotado 6) |
|---|---|---|
| Build da imagem | ~141 s | pull, ~20–40 s (estimado) |
| Testes de integração | ~450 s depois da imagem | ~−35% se o runner acompanhar |
| **Job** | **~11,5 min** | **~6,5 a 7,5 min** (a confirmar) |

### Variação (propostas 2 + 4 + correção da busca, 8 threads)

Seis execuções completas seguidas, a mesma configuração da última linha acima:

| Rodada | 1 | 2 | 3 | 4 | 5 | 6 |
|---|---|---|---|---|---|---|
| Total | 326 s | 320 s | 327 s | 318 s | 313 s | 344 s |
| Falhas | 0 | 0 | 0 | 0 | 0 | 0 |

Média de **325 s**, desvio padrão de ~11 s (313 a 344 s), contra 510 s da base: **−36%**, com
folga maior que a variação. Nenhuma falha em 6 × 665 execuções dos testes de integração. A base
foi medida uma vez só.

### Instabilidade com 8 threads (e por que ficaram 6)

As rodadas acima passaram o número de threads pela linha de comando. Depois de fixar 8 no
`xunit.runner.json`, mais 6 rodadas completas (já com as correções de porta e de repetidos)
deram 3 com falha, de 1 a 3 testes cada, sempre por prazo de espera estourado e cada vez em
testes diferentes (`ConsumoComNovaTentativaTestes`, `EsgotamentoTestes`,
`ReconciliacaoEtapasTestes`, `ExecucaoReenvioDlqTestes`). A concorrência efetiva foi a mesma das
rodadas de variação (~4): a configuração é equivalente, e a diferença é a carga da máquina.

| Experimento | Resultado |
|---|---|
| 8 threads pelo `xunit.runner.json`, 6 rodadas | 3 com falha por tempo; 288 a 341 s nos testes de integração |
| Uso de memória dos contêineres (`docker stats` a cada 15 s) | Pico de 11 GiB dos 15,2 GiB do Docker: não estourou |
| `MSSQL_MEMORY_LIMIT_MB=1536` em cada SQL Server | Pior: 16 a 19 falhas por rodada (`insufficient system memory in resource pool 'default'` no SQL Server compartilhado). Revertido |

Ficaram **6 threads**, uma folga entre o 4 (sem falhas, ganho de 12%) e o 8. A medição local
de 6 com coleta de memória foi interrompida (a máquina, com sete SQL Servers e o Windows
dividindo 31 GB, ficou sem memória); uma rodada completa depois, sem a coleta, passou sem falha
em 312 s. A confirmação é no runner: pelo menos 3 runs do PR. Se houver instabilidade, o
próximo passo é 4.

## Decisões

O que foi adotado na issue #70, o que ficou de fora e por quê.

| Decisão | Por quê |
|---|---|
| `maxParallelThreads` **fixo em 6** no `xunit.runner.json`, e não `"2x"` | `"2x"` daria 8 no runner, mas 32 numa máquina de 16 CPUs. Com 16 threads, os testes de busca full-text falharam; com 8, metade das rodadas teve um prazo de espera estourado; com 4, o ganho cai para 12%. O valor fixo deixa todas as máquinas na mesma configuração. |
| Modo **conservador** do xUnit, e não `parallelAlgorithm=aggressive` | O agressivo chegou a 317 s, mas com concorrência de 9,6 e 4 falhas por limite de tempo (desempenho, login, saúde). |
| SQL Server dos testes de banco como **assembly fixture** | Cada teste já cria o próprio banco (`oms_{guid}`): a coleção serial não protegia nada. |
| Testes `Desempenho*` numa **coleção serial** (`ColecaoDesempenhoBanco`), no mesmo contêiner | Eles têm limites de 500 ms a 1 s; em paralelo entre si, disputariam o banco. |
| Mensageria em **três coleções por área**, cada uma com SQL Server e RabbitMQ próprios | As filas do broker são compartilhadas: dois Workers na mesma coleção roubariam mensagens um do outro. Dentro de cada coleção, os testes continuam seriais. |
| Testes de queda do broker em **duas classes** com uma base abstrata | Uma coleção é por classe; dividir a classe foi o único jeito de rodar as duas metades em paralelo sem mudar nenhum teste, espera ou backoff. |
| O broker do `MensageriaTeste` **tenta outra porta** se a escolhida for tomada | A porta AMQP fixa é escolhida livre antes de o Docker reservá-la. Com seis fixtures de mensageria subindo juntos, uma rodada falhou com `address already in use` e derrubou uma coleção inteira. Agora a sondagem é em `0.0.0.0`, como a publicação do Docker, e o contêiner sobe em outra porta, até 5 vezes. |
| `AvisosPorCaminhoTestes` descarta **repetidos pelo `MessageId`** antes de conferir as sequências | O teste lia a fila crua e exigia uma entrega só, mas o contrato é pelo menos uma vez: sob carga, uma rodada recebeu a sequência 3 duas vezes (`{2, 3, 3, 4}`), com o mesmo `MessageId`. Os consumidores já descartam o repetido; o teste agora faz o mesmo. |
| `BuscaCatalogoTestes` espera **cada produto** aparecer na busca | O Full-Text Search indexa em lote, sem ordem: esperar só o último produto deixava o teste instável, mesmo na `main`. |
| Imagem do SQL Server no **GHCR**, com tag pelo hash de `docker/sqlserver` | O build a frio custa ~2 min por run. A tag pelo hash dispensa um workflow separado: um PR que muda o Dockerfile monta e publica a imagem nova no próprio job. |
| Login no GHCR e push **não derrubam o CI** | Sem o GHCR, a imagem é montada no runner, como antes, e os testes seguem com um aviso. |
| **Logout** do GHCR antes dos testes | Os testes rodam código da aplicação; a credencial não fica disponível para eles, como o token do Sonar. |
| Sem a variável `OMS_IMAGEM_SQLSERVER`, os testes **montam a imagem** como antes | Na máquina local, a imagem fica no cache do Docker e o build leva 1 s; ninguém precisa de login no GHCR. |

Ficaram para depois:

- **Banco a partir de um modelo** (proposta 3): 341 pontos criam um banco e aplicam 13
  migrations. Não foi medido; o primeiro passo é cronometrar `NovoBancoMigradoAsync`.
- **Job de Demonstração com a imagem do GHCR**: ele ainda monta a imagem do SQL Server pelo
  `docker compose --build` (~3 min), mas roda em paralelo e não é o caminho crítico do PR.
- **`ColecaoMensageria` e `ColecaoObservabilidade`**: o novo caminho crítico local (189 s,
  mais 68 s da observabilidade rodando sozinha no fim).
