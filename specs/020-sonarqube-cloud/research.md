# Research: análise estática com SonarQube Cloud e Quality Gate no CI

**Requisito**: REQ-20 (issue #18) | **Spec**: [spec.md](./spec.md) | **Data**: 2026-11-02

Decisões de como o CI faz a análise. O que a spec já decidiu (proteção da `main` sem contorno,
merge por rebase, sem PRs de forks) não é rediscutido aqui.

## R-01. A análise roda dentro do job "Build e testes"

**Decisão**: o scanner envolve a compilação e os testes que o job `Build e testes` já faz:
`sonarscanner begin` → `dotnet build` → `dotnet test` (com cobertura) → resumo →
`sonarscanner end`. Nenhum job novo de build.

**Motivo**: o scanner do SonarQube para .NET precisa observar o `dotnet build` (ele injeta os
analisadores Roslyn na compilação) e ler a cobertura da mesma execução (FR-003). Num job
separado, a solução seria compilada e testada duas vezes, e o tempo do PR dobraria (SC-007:
no máximo 5 min a mais).

**Alternativas**: job próprio de análise (recompila e retesta: +8 a 10 min); a action
`SonarSource/sonarqube-scan-action` (não serve para C#: o .NET exige o scanner próprio); a
análise automática do serviço (não cobre C#, e foi desativada).

## R-02. Gatilhos: PR (como hoje) e push na `main`

**Decisão**: o workflow `build-testes` volta a rodar também no push da `main`, mas só o job
`Build e testes` (com a análise); o job `Demonstração` continua só nos PRs. A concorrência
cancela execuções antigas só nos PRs; no push da `main`, cada merge é analisado até o fim.

**Motivo**: FR-002. A análise da `main` é a referência de comparação dos PRs, alimenta o
badge do README e é a situação de partida (R-10). Revê em parte o commit `e75fc1a` ("roda a
verificação só nos PRs"): o custo volta, mas só do job de testes, uma vez por merge.

**Alternativas**: workflow separado só para a `main` (duplicaria os passos de build e teste);
analisar só os PRs (sem referência nem badge: rejeitado pela spec).

## R-03. Scanner: `dotnet-sonarscanner` 11.3.0 como ferramenta local

**Decisão**: `dotnet-sonarscanner` **11.3.0** em `.config/dotnet-tools.json`, ao lado do
`dotnet-ef`, com `rollForward: false`, instalado no job por `dotnet tool restore`.

**Justificativa da dependência** (constituição, Restrições técnicas): é o único scanner
suportado pelo SonarQube Cloud para projetos C#/.NET; é uma ferramenta de build, não entra em
nenhum assembly do OMS; versão fixa, como o `dotnet-ef`.

**Versão**: a 11.3.0 (publicada em 2026-09-02) é a última da série 11. A 12.0.0 (2026-10-15)
tem mais de duas semanas, mas é versão maior com 18 dias; fica para uma atualização deliberada
depois que a série amadurecer.

## R-04. Java e cache

**Decisão**: sem `actions/setup-java`. O scanner 11.x baixa e guarda o próprio runtime Java
(provisionamento automático do JRE pelo SonarQube Cloud), e o `ubuntu-latest` ainda traz o
Java 17. Cache de `~/.sonar/cache` com `actions/cache`, fixado por SHA como as outras ações.

**Motivo**: menos uma ação de terceiros e menos uma versão para manter; o cache evita baixar
o JRE e os plugins a cada execução (SC-007).

## R-05. Cobertura: o mesmo coletor, também em XML

**Decisão**: o coletor `Code Coverage` (do `Microsoft.NET.Test.Sdk`, já usado desde o REQ-07)
passa a gerar dois formatos na mesma execução: `--collect "Code Coverage;Format=cobertura,xml"`.
O XML (formato "Visual Studio Coverage") vai para o Sonar em
`sonar.cs.vscoveragexml.reportsPaths`; o Cobertura continua alimentando o resumo do job
(`.github/scripts/resumo-testes.sh`). Os resultados dos testes (`.trx`) vão em
`sonar.cs.vstest.reportsPaths`.

**Verificado em 2026-11-02**: com `Format=cobertura,xml`, a suíte unitária gerou
`*.cobertura.xml` e `*.xml` (raiz `<results>`, com `line_coverage` por módulo), sem pacote
novo.

**Separação dos arquivos**: o padrão `*.xml` também casaria com `*.cobertura.xml`. Depois do
resumo, um passo move os `*.cobertura.xml` para fora da pasta lida pelo Sonar, antes do
`sonarscanner end`.

**Módulos**: o `coverage.runsettings` passa a incluir `PedidoFulfillment.Worker` e
`PedidoFulfillment.PagamentosSimulados` (FR-012), além de Domain, Application, Infrastructure
e API. As migrations e `obj/` continuam fora.

**Alternativas**: `coverlet.collector` (pacote novo, sem ganho); converter o Cobertura com uma
ferramenta (`dotnet-coverage merge`, mais uma ferramenta).

## R-06. Escopo da análise

| Parâmetro | Valor | Requisito |
|-----------|-------|-----------|
| `sonar.exclusions` | `specs/**`, `.specify/**`, `**/Persistencia/Migracoes/**`, `**/obj/**`, `**/bin/**` | FR-009, FR-011 |
| `sonar.coverage.exclusions` | `**/Persistencia/Migracoes/**` | FR-009 |
| Projetos de teste | detectados como teste pelo scanner (referenciam o `Microsoft.NET.Test.Sdk`, `IsTestProject`) | FR-010 |
| API de pagamento simulada | analisada como produção | FR-012 |

Os parâmetros ficam no comando `begin` do workflow (o scanner para .NET não usa
`sonar-project.properties`). Organização `lucasfrenhan`, projeto
`LucasFrenhan_Order-Management-System`, servidor `https://sonarcloud.io`.

## R-07. Credencial e PRs de forks

**Decisão**: o `SONAR_TOKEN` entra só no `env:` dos dois passos do scanner (`begin` e `end`),
na variável `SONARQUBE_SCANNER_PARAMS` (JSON com `sonar.token`), que o scanner lê; a 11.3.0 não lê `SONAR_TOKEN`, verificado com um token falso. Nunca em linha de comando (`/d:sonar.token`), em arquivo ou
no `env:` do job. Como o `if:` de um passo não enxerga o `env:` do próprio passo, um passo
anterior grava só `ativo=${{ secrets.SONAR_TOKEN != '' }}` (`true` ou `false`) na saída, e os
passos do scanner rodam com `if: steps.sonar.outputs.ativo == 'true'`. Assim o `dotnet test`,
que roda código da aplicação e dos testes, nunca vê o token. Em PRs de forks (sem acesso a
segredos), a saída é `false`: os passos do scanner são pulados, e build e testes rodam como
hoje (FR-004, FR-005; analyze S1 e A1).

**Fechamento dos PRs de forks** (FR-004a): workflow novo `prs-de-fork.yml`, no evento
`pull_request_target` (`opened`, `reopened`), só quando a branch de origem é de um fork.
Comenta a política e fecha o PR com `gh pr close`. **Não faz checkout nem roda código do
fork**, que é o risco conhecido do `pull_request_target`; permissão só
`pull-requests: write`.

## R-08. Quality Gate e decoração do PR

**Decisão**: Quality Gate padrão do SonarQube Cloud ("Sonar way"), que já atende ao FR-013:
no código novo, nota A de confiabilidade (nenhum bug) e de segurança (nenhuma
vulnerabilidade), 100% dos pontos de segurança revisados, cobertura ≥ 80% e duplicação ≤ 3%;
ele também exige nota A de manutenibilidade, o que vai além do FR-013 e é aceito. As condições são conferidas pela
API depois da primeira análise e registradas em R-10. A decoração do PR (check run e resumo)
vem do app do SonarQube Cloud já instalado; o nome esperado da verificação é
`SonarCloud Code Analysis`, conferido no primeiro PR.

**Sem `sonar.qualitygate.wait`**: o job não espera o resultado do Quality Gate. A verificação
própria do app é a exigida pela `main`; esperar no job só somaria tempo e duplicaria o sinal.

## R-09. Proteção da `main` versionada

**Decisão**: o ruleset "Proteção da main" (id 26400051) fica versionado em
`.github/rulesets/protecao-main.json`, com o comando `gh api` para aplicá-lo documentado no
README (FR-015a). No fim do requisito, o JSON ganha a verificação do Quality Gate, com o
`integration_id` do app do SonarQube Cloud, para que só o app possa satisfazê-la (um status
com o mesmo nome publicado por outra origem não vale).

## R-10. Sequência e situação de partida

A primeira análise completa da `main` só existe depois que o workflow com o scanner chega à
`main`. Por isso o requisito sai em **dois PRs**, os dois com `[REQ-20]`:

1. **PR de integração** (`Refs #18`): workflow, scanner, cobertura, exclusões, fechamento de
   PRs de forks, ruleset versionado, README. A análise do próprio PR já aparece nele (o código
   novo é só o do PR). Depois do merge, o push na `main` gera a **situação de partida**.
2. **PR da situação de partida** (`Closes #18`): registra a baseline neste research; corrige os
   bugs e as vulnerabilidades encontrados; traz a triagem dos pontos de segurança; acrescenta a
   verificação do Quality Gate ao ruleset; badges do README. Este PR já passa pela análise e
   pelo Quality Gate.

**Triagem dos pontos de segurança** (FR-017): a revisão ("Safe" com a justificativa) é feita
pelo dono no painel do SonarQube Cloud. A credencial dele não passa pela sessão. A lista de
pontos esperados e o texto de cada justificativa são preparados no PR 2. Pontos esperados:
senha do `sa`, senhas do RabbitMQ, chave JWT de desenvolvimento, chave de cifra dos webhooks,
chaves da API de pagamento simulada e senhas dos usuários iniciais, em `appsettings.*.json`,
`docker-compose.yml`, `.env.example` e testes. Justificativa padrão: valores só para os
contêineres locais, documentados como de desenvolvimento no README e nos comentários.

### Situação de partida

Primeira análise completa da `main`: **2026-11-02 23:00 UTC**, commit `eb76ba8` (merge por
rebase do PR #48), lida pelas APIs públicas do projeto.

| Medida | Valor |
|--------|-------|
| Linhas de código (`ncloc`) | 11.008 |
| Testes | 1.473 |
| Cobertura de linhas | **97,0%** (o resumo do job da mesma execução deu 97,1%: diferença de 0,1 ponto, SC-003) |
| Bugs | 1 (nota C de confiabilidade) |
| Vulnerabilidades | 2 (nota C de segurança) |
| Pontos de segurança (security hotspots) | **0** |
| Code smells | 63 (nota A de manutenibilidade; dívida de 405 min) |
| Duplicação | 0,5% |
| Quality Gate da `main` | Aprovado |

**Quality Gate em uso**: "Sonar way" (padrão da organização), sobre o código novo:
`new_reliability_rating > 1`, `new_security_rating > 1`, `new_maintainability_rating > 1`,
`new_coverage < 80`, `new_duplicated_lines_density > 3` e `new_security_hotspots_reviewed < 100`
reprovam. Atende ao FR-013 (R-08).

**Código novo**: a organização usa "versão anterior" (`previous_version`); como o projeto não
informa versão, o período começa na primeira análise (2026-11-02 21:06, a análise automática
antes de ser desativada). O badge da `main` reflete as mudanças desde então; nos PRs, o código
novo é sempre o do PR. Fica como está (analyze N1).

**Pontos de segurança**: nenhum. As credenciais só de desenvolvimento ficam em JSON, YAML e
`.env.example`, que a regra de senha fixa do C# (S2068) não analisa; nos testes, as constantes
de teste não foram sinalizadas. A triagem do FR-017 não teve item a revisar; a regra continua
ativa, e uma credencial nova no código C# bloqueia o PR até ser revisada.

**Bugs e vulnerabilidades** (FR-018), os três corrigidos no PR 2:

| Regra | Onde | Decisão |
|-------|------|---------|
| S2583 (bug: condição sempre verdadeira) | `Infrastructure/Configuracao/OpcoesEstornos.cs:41` | Falso positivo do analisador: a função local acumulava as falhas numa lista capturada, e ele não acompanha esse efeito. Reescrito com a função local devolvendo a mensagem (ou nulo), fluxo explícito; os testes de `OpcoesEstornos` continuam verdes |
| S2245 (vulnerabilidade: gerador aleatório sem segurança criptográfica), 2 ocorrências | `PagamentosSimulados/Simulacao/ControleSimulacao.cs:25` | O `Random` sorteia o comportamento da simulação e precisa aceitar a semente `Simulacao:Semente` (testes reproduzíveis); um gerador criptográfico não aceita semente. Supressão só nesse construtor, com `[SuppressMessage]` e a justificativa no código; a regra continua ativa no projeto |

### Dívida técnica (code smells da situação de partida)

Não bloqueiam este requisito (spec, FR-018). 63 no total: 44 informativos, 13 menores, 6
maiores, nenhum crítico ou bloqueador.

| Qtde | Regra | Do que se trata | Observação |
|-----:|-------|-----------------|------------|
| 20 | CA1873 | Argumento de log calculado mesmo com o nível desligado | Desempenho; pequeno nos volumes do projeto |
| 13 | S1313 | Endereço IP fixo no código | Intencional: são as faixas privadas e reservadas da proteção contra SSRF dos webhooks (REQ-18); candidatos a "Won't fix" com justificativa |
| 6 | CA1859 | Tipo de interface onde o concreto evitaria indireção | Desempenho |
| 6 | CA1861 | Array constante como argumento repetido | Desempenho |
| 5 | S7688 (shell) | `[` em vez de `[[` em script bash | `.github/scripts/resumo-testes.sh` e `docker/rabbitmq/iniciar.sh` |
| 4 | CA1822 | Membro que poderia ser `static` | Um deles (`FabricaPagamentos.ChaveApi`, apontado no PR #48) já corrigido no PR 2 |
| 2 | CA1826 | `Enumerable` sobre coleção indexável | Desempenho |
| 2 | CA1869 | `JsonSerializerOptions` criado a cada uso | Desempenho |
| 1 | S6966 | `RunAsync` em vez de `Run` | Programa da API |
| 1 | ASP0027 | `public partial class Program` não é mais necessário | API |
| 1 | CA2263 | Sobrecarga genérica de `CreateLogger` | Infrastructure |
| 2 | xUnit1044/1045 | Tipo de dado de teste não serializável | Só afeta a listagem no Test Explorer |

Por projeto: Infrastructure 24, testes de integração 10, API de pagamento simulada 9, testes
unitários 8, Application 5, scripts de shell 5, API 2. Registrado em `docs/proximos-passos.md`
como dívida a tratar quando um REQ mexer no código afetado.

## R-11. README e editor

**Decisão**: badges públicos do Quality Gate e da cobertura (o projeto é público, não precisam
de token); seção "Análise estática" com o link do painel, como ver a análise de um PR, o que
fazer com um Quality Gate reprovado (corrigir, ou justificar um falso positivo no painel), a
política de PRs de forks e o comando para reaplicar o ruleset. Recomendação opcional do
SonarQube for IDE em modo conectado (FR-020).

## R-12. Documentos de fluxo

O requisito muda o CI, não o comportamento do sistema: nenhum documento de `docs/fluxos/`
muda. O PR diz isso em Validação (regra do `CLAUDE.md`).
