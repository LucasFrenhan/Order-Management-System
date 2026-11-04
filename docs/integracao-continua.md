# Integração contínua

[← Voltar ao README](../README.md)

O workflow [`build-testes`](../.github/workflows/build-testes.yml) roda em todo PR para a
`main` e no push da `main` (o merge). São dois jobs:

- **Build e testes**: compila em Release, com avisos como erros, e roda os testes unitários
  e de integração com SQL Server, RabbitMQ e a API de pagamento simulada reais
  (Testcontainers e Kestrel). O resumo da execução mostra as contagens por projeto, os testes
  que falharam e a cobertura de linhas, sem as migrations nem o código gerado. A análise
  estática do SonarQube Cloud envolve esse build e esses testes (veja
  [Análise estática](analise-estatica.md)). Os resultados (TRX, Cobertura e o relatório de
  cobertura enviado ao SonarQube) ficam 14 dias no artefato `resultados-testes`.
  - **Imagem do SQL Server**: os testes usam a imagem própria com Full-Text Search
    (`docker/sqlserver/Dockerfile`, REQ-22). O CI a baixa pronta do GHCR
    (`ghcr.io/lucasfrenhan/oms-sqlserver-testes`, com tag pelo hash de `docker/sqlserver`) e
    só a monta e publica quando a pasta muda, o que levaria ~2 min em toda rodada (issue #70).
    Os testes recebem a imagem por `OMS_IMAGEM_SQLSERVER`; sem a variável, como na máquina
    local, o Testcontainers monta a imagem uma vez e a reaproveita pelo cache do Docker. O
    resumo da execução diz se a imagem foi baixada ou montada.
  - **Paralelismo**: os testes de integração rodam até 6 de cada vez
    ([`xunit.runner.json`](../tests/PedidoFulfillment.Testes.Integracao/xunit.runner.json)),
    em qualquer máquina. Os testes de banco compartilham um SQL Server (cada um com o próprio
    banco) e rodam em paralelo; os de mensageria ficam em coleções seriais por área, cada uma
    com o próprio SQL Server e RabbitMQ, porque as filas do broker são compartilhadas; os de
    desempenho ficam numa coleção serial. Com mais de 8, os testes com limite de tempo falham
    ([medições](otimizacao-ci.md)).
- **Demonstração**: só nos PRs; sobe o perfil `demo` (montando também a imagem do SQL Server
  com Full-Text Search) e confere a prontidão da API, do Worker e da API de pagamento simulada
  e o login do administrador de desenvolvimento.

No push da `main`, só o job de testes roda: a análise da `main` é a referência de comparação
dos PRs e do estado mostrado na análise estática.

O check fica vermelho com:
- erro ou aviso de compilação;
- teste falhando ou **ignorado**;
- falta de Docker;
- demonstração que não fica pronta;
- falha da análise (por exemplo, credencial inválida).

No CI, `OMS_EXIGIR_DOCKER=true` faz os testes com banco e broker reais falharem sem Docker,
em vez de serem ignorados. Para reproduzir localmente:

```powershell
$env:OMS_EXIGIR_DOCKER = "true"
dotnet test PedidoFulfillment.sln -c Release --logger "trx;LogFilePrefix=resultado" --results-directory resultados --collect "Code Coverage;Format=cobertura" --settings coverage.runsettings
bash .github/scripts/resumo-testes.sh resultados
```

## Proteção da `main`

A `main` tem o ruleset **Proteção da main**, versionado em
[`.github/rulesets/protecao-main.json`](../.github/rulesets/protecao-main.json) e válido para
todos, inclusive o dono do repositório (REQ-20):

- mudanças só por PR, mergeado **por rebase** (histórico linear; os commits de etapa e de fase
  entram em sequência na `main`);
- verificações exigidas, com a branch atualizada: **Build e testes**, **Demonstração** e
  **SonarCloud Code Analysis** (o Quality Gate, aceito só quando vem do app do SonarQube Cloud);
- push direto, force push e exclusão da `main` recusados.

Para conferir ou reaplicar a regra a partir do arquivo versionado:

```bash
gh api repos/LucasFrenhan/Order-Management-System/rules/branches/main
gh api -X PUT repos/LucasFrenhan/Order-Management-System/rulesets/26400051 --input .github/rulesets/protecao-main.json
```

Uma verificação exigida só pode ser cadastrada depois de rodar uma vez, e uma que não roda
num PR o bloqueia: por isso a **Compatibilidade do contrato**, que só roda quando o contrato
muda, não é exigida. Numa indisponibilidade do SonarQube Cloud, o merge espera; se for
preciso, a regra é desligada temporariamente em Settings → Rules, de forma registrada.
