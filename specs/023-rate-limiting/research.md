# Research: limite de requisições compartilhado (REQ-23)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md) | **Data**: 2026-11-04

## R-01: armazenamento compartilhado: SQL Server ou Redis

**Decisão (a confirmar pelo usuário)**: SQL Server, em uma tabela própria, atrás da porta
`IArmazenamentoContagens`, para que uma troca futura pelo Redis mexa só na implementação.

### Medição

Teste prático em 2026-11-04, com contêineres descartáveis na mesma máquina e a mesma operação
nos dois (R-03): SQL Server 2022 CU26, com 500 mil linhas na tabela, e Redis 7.4, com um script
Lua. O cliente foi .NET 10, com `Microsoft.Data.SqlClient` 7.1.1 e `StackExchange.Redis` 2.8.58.
Sem rede real entre as máquinas: a latência de rede somaria aos dois igualmente.

| Cenário | Simultâneas | SQL p50 / p95 / p99 (ms) | Redis p50 / p95 / p99 (ms) | SQL/s | Redis/s |
|---------|:-----------:|--------------------------|----------------------------|:-----:|:-------:|
| Novas: duas chaves novas por tentativa | 16 | 4,05 / 5,69 / 8,83 | 0,72 / 0,89 / 1,20 | 3.752 | 21.632 |
| Quente: mesmo IP, um e-mail novo por tentativa, sem atingir o limite | 32 | 52,18 / 55,02 / 58,24 | 0,74 / 0,89 / 1,02 | 608 | 43.015 |
| Barrada: mesma contagem esgotada | 16 | 1,75 / 2,09 / 2,37 | 0,65 / 0,74 / 0,88 | 9.242 | 24.271 |
| Atomicidade: limite 100, 2.000 tentativas | 32 | 4,03 / 28,22 / 50,74 | 0,70 / 0,85 / 1,31 | 5.003 | 44.250 |

Resultados de correção, iguais nos dois:

- **Atomicidade**: exatamente 100 aceitas de 2.000, com 32 simultâneas (FR-007).
- **Tentativa barrada não consome o outro limite**: confirmado (FR-002).
- **Erros**: nenhum. Nenhum deadlock no SQL Server em mais de 60 mil operações.

**O que a medição não incluiu**: o programa de teste foi descartado, sem entrar no repositório. O lote medido não tinha
a coluna `BloqueioRegistrado`, nem a limpeza rodando em paralelo, e o bloqueio local (R-05) não estava ativo. A
coluna acrescenta uma escrita só na primeira recusa de cada janela; os testes de integração cobrem esse conjunto.

### Leitura dos números

- **Os dois atendem ao SC-008 (p95 até 50 ms) no uso real.** O Redis é de 3 a 60 vezes mais rápido,
  mas o ganho é de poucos milissegundos numa rota que já vai ao banco e calcula o hash da senha.
- **O cenário quente do SQL Server passa de 50 ms, mas não ocorre com os limites configurados.**
  Ele aceita 608 tentativas por segundo do mesmo IP, porque usa um limite altíssimo só para
  medir. Com o padrão de 100 por minuto, a contagem esgota em menos de um segundo e passa ao caminho
  "barrada", de 2 ms. O que limita o cenário é o `COMMIT` em série na mesma linha, cerca de 1,6 ms.
- **O teste não mede o efeito sobre o resto do banco.** Uma inundação compete com pedidos e
  workers pela mesma instância. R-05 reduz esse risco, e o `quickstart` traz uma verificação sob carga.

### Comparação

| Critério | SQL Server | Redis |
|----------|------------|-------|
| Atomicidade (FR-007) | Transação em um só lote, sem ida e volta no meio | Script Lua |
| Referência de tempo (FR-008) | `SYSUTCDATETIME()` | `PTTL` do servidor |
| Remoção de vencidas (FR-011) | Rotina no Worker (R-07) | Automática, por expiração |
| Falha em conjunto com o login (FR-012) | Sim: sem banco, o login já não funciona | Não: o limite cai sozinho, e as rajadas passam com o login no ar |
| Isolamento em ataque | Compartilha a instância (mitigação em R-05) | Isolado |
| Novo em produção | Só uma migration | Serviço a operar, monitorar e proteger, e reinício zera as contagens sem persistência |
| Dependência (constituição) | Nenhuma | `StackExchange.Redis`, com justificativa escrita |
| Compose, CI e testes | Nada novo | Contêiner a mais, módulo do Testcontainers e mais uma prontidão |

**Por que SQL Server**: cumpre todos os requisitos medidos, não traz nada novo para operar, e a
decisão do usuário ("o bloqueio é do domínio") pede estado compartilhado, não um produto. Com o
banco fora, não existe login a proteger, o que torna o FR-012 mais simples.

**Quando rever**: se a verificação sob carga do `quickstart` mostrar impacto nos pedidos, ou se o
Redis entrar no projeto por outro motivo. Aí basta outra implementação da porta.

**Alternativa descartada**: tabela em memória do SQL Server (In-Memory OLTP), porque exige um
filegroup próprio, muda a preparação do banco e os dados não duráveis não sobrevivem a reinício.

## R-02: limitador nativo do ASP.NET Core

**Decisão**: não usar `AddRateLimiter`. Usar um middleware próprio, ligado às duas rotas por
metadados de endpoint. A issue sugeria o nativo, mas três pontos do desenho não cabem nele:

1. O particionador é síncrono (`Func<HttpContext, RateLimitPartition>`), e a chave do login depende
   do corpo. Ler o corpo de forma síncrona bloquearia uma thread por requisição.
2. Encadear dois limitadores consome um e depois o outro. A regra "barrada por um não consome o
   outro" (FR-002) exige decidir os dois de uma vez, na mesma transação.
3. Os limitadores da plataforma guardam o estado no processo. Um limitador próprio sobre o banco
   usaria o mesmo armazenamento e ficaria só com a interface do nativo.

**Por que middleware e não filtro de endpoint**: nas Minimal APIs, o corpo é lido antes dos filtros.
Um JSON inválido falharia antes de contar, o que contraria o caso de borda da spec. O middleware
roda antes, sem depender do formato do corpo.

Continua sem pacote novo, como a issue pedia.

## R-03: atomicidade dos dois limites do login

**Decisão**: um único lote T-SQL por requisição, em uma transação, com as chaves em ordem:

1. Lê as contagens com `UPDLOCK, HOLDLOCK`, tratando uma janela vencida como zero.
2. Se alguma contagem atingiu o limite, não incrementa nenhuma e devolve a espera de cada uma.
   Só a primeira recusa da janela grava `BloqueioRegistrado`, para o log único (FR-020).
3. Se nenhuma atingiu, incrementa as duas, ou reinicia a janela vencida, com
   `FimJanela = DATEADD(SECOND, @janela, SYSUTCDATETIME())`.

**Execução pelo EF Core** (constituição; analyze C1): o lote roda por `Database.SqlQueryRaw`, com parâmetros, em um
`ContextoBancoDados` criado com a string de conexão do pool próprio (R-04). A medição usou `SqlClient` direto, e o
acréscimo do EF Core é conferido pelo p95 da T015.

Uma só ida e volta mantém os bloqueios por microssegundos. A ordem fixa das chaves impede ciclos
entre transações. O caminho "barrada" não escreve depois da primeira recusa, o que o deixa barato
justamente sob ataque.

**Deadlock**: o lote usa `SET DEADLOCK_PRIORITY HIGH`, e a limpeza, `LOW`. Um erro 1205 ganha
uma nova tentativa. Se persistir, segue o FR-012, sem acionar a pausa de R-04.

**Alternativas descartadas**: `MERGE`, sem ganho aqui e com histórico de problemas de concorrência;
consumir e compensar em dois passos, porque abre uma janela em que uma tentativa barrada conta.

## R-04: tempo máximo e falhas do armazenamento (FR-012)

**Decisão**:

- **Tempo máximo de 250 ms** (`TempoMaximoEsperaMs`, faixa de 50 a 5.000), bem acima do p99 medido
  (9 ms), para só cortar quando há problema real.
- **Pausa de 5 s depois de uma falha** (`PausaAposFalhaSegundos`, faixa de 0 a 300): a instância
  segue sem o limite durante a pausa, sem tentar o banco. Sem ela, cada login pagaria os 250 ms
  durante uma queda. A pausa é por instância, e os bloqueios locais de R-05 continuam valendo.
- **Pool próprio**: conexões com `Application Name=oms-limite-requisicoes` e `Max Pool Size=20`, em um pool
  separado do EF Core. Uma inundação fica sem conexão de limite, sem esgotar as dos pedidos.
- **Aquecimento na partida** (analyze U1): a primeira conexão pode passar dos 250 ms, o que abriria a pausa a cada
  reinício. Um serviço em segundo plano abre o pool ao subir, com `Min Pool Size=2`, sem o tempo máximo de 250 ms e sem
  impedir a API de subir. Falhar nisso só gera um log de nível Debug: o banco fora tem seu próprio tratamento.
- **Limitação conhecida** (analyze G3): uma inundação vinda de muitas origens pode esgotar as 20 conexões e deixar a
  instância sem o limite enquanto durar. O bloqueio local só ajuda contra origens já barradas. Sobra o bloqueio de conta,
  que protege as senhas, como aceito na decisão do FR-012.
- **Log e métrica** na transição para falha, uma vez por pausa (R-08).

Cancelamento pelo cliente não é falha: a requisição é abortada, sem log e sem métrica.

## R-05: bloqueio local para aliviar o banco

**Decisão**: quando o armazenamento recusa uma contagem, a instância guarda essa recusa em memória até o
fim da janela, e responde 429 sem ir ao banco nas tentativas seguintes.

- **Não fere o estado único (FR-006)**: a janela é fixa e só termina no prazo, então uma recusa
  conhecida continua certa até lá. Só as recusas ficam em memória, nunca as contagens.
- **Fecha uma brecha**: sem isso, uma inundação da mesma origem esgotaria as 20 conexões, e o FR-012
  liberaria as tentativas. Com isso, a origem já barrada não chega ao banco.
- **Limite de memória**: `MemoryCache` com até 100 mil entradas. Ao lotar, a recusa simplesmente volta a ser
  decidida pelo banco.
- **`Retry-After`**: calculado do tempo restante, arredondado para cima (SC-003: até 1 s de diferença).

Trocar o limite por configuração, com a API rodando, não é suportado: as opções valem a partir da partida.

## R-06: origem do cliente e intermediários confiáveis (FR-004)

**Decisão**: `UseForwardedHeaders` nativo, só para `X-Forwarded-For`, como primeiro item do pipeline:

```json
"LimiteRequisicoes": { "IntermediariosConfiaveis": ["10.0.0.0/8", "203.0.113.7"] }
```

- Aceita endereços e faixas CIDR. Um valor inválido impede a partida. O padrão é lista vazia: sem
  intermediário, vale o endereço da conexão, e o cabeçalho é ignorado.
- **As listas padrão da plataforma são limpas.** `KnownProxies` e `KnownIPNetworks` confiam em loopback
  por padrão. `ForwardLimit = null` faz percorrer toda a cadeia da direita para a esquerda, até o primeiro
  endereço não confiável, que é o cliente real. Um valor forjado à esquerda é ignorado.
- `Host` e `Proto` não entram, para não mudar outros comportamentos.
- **Efeito colateral aceito**: logs e traces passam a ver o cliente real.

**Normalização**: IPv4 pelo endereço exato; IPv4 mapeado em IPv6 como IPv4; IPv6 pelo `/64`. Sem endereço
de conexão, como no `TestServer`, a origem é o valor único `desconhecida`, que divide uma só contagem:
nunca deixa de limitar.

## R-07: limpeza das contagens vencidas (FR-011)

**Decisão**: uma rotina própria no Worker, `LimpezaContagensLimite`, a cada 60 s, com lock de aplicação
próprio, `DELETE TOP` por `FimJanela < SYSUTCDATETIME()` e `DEADLOCK_PRIORITY LOW`, por `Database.ExecuteSql`.

- **Por que não um alvo da `LimpezaMensageria`**: ela roda de hora em hora, com até 10 mil linhas por
  alvo. Uma única origem pode criar 6 mil contagens de e-mail por hora, então poucas origens já
  venceriam a limpeza.
- **Capacidade**: lotes de 1.000 (`TamanhoLote`), até 100 por rodada, ou 6 milhões de linhas por hora.
- **Configuração**: `Limpeza:IntervaloContagensSegundos` e `Limpeza:LotesContagensPorRodada`. Obedece
  a `Limpeza:Habilitada` e usa a métrica `oms.limpeza.linhas_apagadas`, alvo `contagens_limite`.
- **Worker fora**: a limitação continua certa, pois a janela vencida é tratada como zero. Só a tabela cresce.

## R-08: forma não legível, logs e métricas (FR-010, FR-020, FR-021)

- **Chave da contagem**: `HMAC-SHA256(ChaveContagem, "politica\0origem\0EMAIL")`, em `binary(32)`. Um hash
  sem segredo seria reversível, porque os IPv4 são poucos. A chave é **própria**, e não derivada da chave do
  JWT: é o que mantém a #60 fora deste REQ.
- **`LimiteRequisicoes:ChaveContagem`**: Base64 de 32 bytes, obrigatória, igual em todas as instâncias, como
  `Notificacoes:ChaveCifra`. Só a API a usa. Trocá-la só zera as contagens.
- **E-mail da contagem**: sem espaços nas pontas e em maiúsculas, como o `NormalizedEmail` do Identity.
- **Logs**: 3007 `LimiteRequisicoesExcedido` (Warning, um por contagem por janela, com política e e-mail
  mascarado por `MascaraEmail`) e 3008 `FalhaArmazenamentoLimite` (Warning, um por pausa, com o tipo da falha).
- **Métricas**: `oms.limite_requisicoes.recusadas` e `oms.limite_requisicoes.falhas_armazenamento`. A
  primeira tem o atributo `oms.limite.politica` (`login_origem_email`, `login_origem`, `registro`).

## R-09: leitura do e-mail antes da rota

**Decisão**: o middleware usa `EnableBuffering`, lê até 16 KB, tenta `JsonDocument`, procura `email` sem
diferenciar maiúsculas e volta o corpo ao início. Corpo maior, JSON inválido, `email` ausente, não texto ou
com mais de 256 caracteres contam só no limite por origem (FR-003). A rota segue com a sua validação de hoje,
e as respostas de sempre não mudam (FR-022).

## R-10: testes sem banco e limites de teste

- **`FabricaApi` sem banco**: a maior parte da suíte roda sem banco, numa porta fechada (research R-11 do
  REQ-03). Ela passa a usar `ArmazenamentoContagensMemoria`, um dublê de teste que segue a porta. Com
  `StringConexao`, usa o armazenamento real.
- **Limites altos por padrão** nas fábricas (1.000.000, o teto da validação): o código e a validação são
  os de produção (FR-022), sem opção de desligar.
- **Testes com banco sem a saída por tempo** (analyze U2): os testes rodam com até 6 threads, e uma operação acima de
  250 ms liberaria a requisição e faria um teste de limite exato falhar sem haver bug. Por isso, `TempoMaximoEsperaMs=5000`
  e `PausaAposFalhaSegundos=0` nas fábricas. Só os testes de falha do armazenamento usam tempos curtos.
- **Origem nos testes**: o `TestServer` não informa endereço. Um `IStartupFilter` define o endereço da
  conexão por teste, antes dos cabeçalhos encaminhados.
- **Duas instâncias**: duas `FabricaApi` com o mesmo banco e a mesma chave, repartindo as tentativas.
- **Tempo**: janelas de 1 a 2 s nos testes de integração, pois a referência de tempo é o banco.
