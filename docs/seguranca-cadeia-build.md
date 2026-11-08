# Segurança da cadeia de build

[← Voltar ao README](../README.md)

Três defesas protegem as dependências e o código que entram na `main` (REQ-26,
[spec](../specs/026-dependabot-codeql/spec.md)):

| Defesa | Onde roda | O que bloqueia |
|---|---|---|
| Verificação de vulnerabilidades | passo **Vulnerabilidades** do job **Build e testes** ([CI](integracao-continua.md)) | pacote NuGet (direto ou transitivo) com vulnerabilidade **alta ou crítica** conhecida |
| Dependabot | [`.github/dependabot.yml`](../.github/dependabot.yml) e os alertas do repositório | nada sozinho: abre PRs de atualização e de correção de segurança |
| CodeQL | workflow [`codeql`](../.github/workflows/codeql.yml), C# e workflows | alerta novo de segurança **alta ou crítica**, ou de severidade erro, pelo ruleset |

A análise de qualidade do SonarQube Cloud continua à parte, em [Análise estática](analise-estatica.md).

## Verificação de vulnerabilidades

O roteiro [`verificar-vulnerabilidades.ps1`](../.github/scripts/verificar-vulnerabilidades.ps1)
lê `dotnet list package --vulnerable --include-transitive` de toda a solução, logo depois do
restore, e escreve no resumo do job uma tabela com pacote, versão, severidade, aviso, se é
direta ou transitiva e os projetos.

- **Alta ou crítica** reprova o job, em qualquer PR, mesmo que o PR não mexa em dependências:
  um aviso novo publicado para um pacote que já está na `main` trava todos os PRs até a
  correção (em geral, o PR de segurança do Dependabot) ou uma exceção.
- **Baixa ou moderada** aparece como aviso, sem reprovar.
- **Sem consulta** (a base de avisos fora do ar, JSON inválido): o job reprova, nunca passa sem
  ter conferido.
- Na máquina local, o restore mostra os mesmos avisos (NU1901 a NU1904), sem falhar: o
  `Directory.Build.props` os tira do `TreatWarningsAsErrors`, porque com o restore falho o
  resumo e as exceções não seriam possíveis. Para conferir como o CI:
  `dotnet restore PedidoFulfillment.sln; pwsh .github/scripts/verificar-vulnerabilidades.ps1`.
- O próprio passo confere o roteiro antes, com as saídas gravadas de
  [`exemplos-vulnerabilidades/`](../.github/scripts/exemplos-vulnerabilidades/)
  (`conferir.ps1`).

### Exceções

Só quando não há como corrigir já (por exemplo, sem versão corrigida publicada). Cada exceção
entra em [`.github/vulnerabilidades-aceitas.json`](../.github/vulnerabilidades-aceitas.json), por
PR:

```json
{
  "excecoes": [
    {
      "pacote": "Exemplo.Pacote",
      "aviso": "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx",
      "motivo": "Sem versão corrigida; o código vulnerável não é usado (issue #NN).",
      "registradaEm": "2026-11-08",
      "revisarAte": "2026-12-08"
    }
  ]
}
```

- `aviso` é a URL exatamente como aparece no resumo do job; a exceção vale para esse par
  pacote + aviso, em qualquer projeto.
- `revisarAte` fica no máximo 90 dias depois de `registradaEm`. Vencida, a exceção deixa de
  valer e o CI volta a reprovar: renove com um motivo novo ou corrija.
- Uma exceção que já não casa com nenhuma vulnerabilidade aparece no resumo em "Exceções que
  podem sair".

## Dependabot

| Ecossistema | O que atualiza | Grupos de minor e patch |
|---|---|---|
| NuGet | `Directory.Packages.props` e `.config/dotnet-tools.json` | `dotnet` (AspNetCore, EF Core, `Microsoft.Extensions.Logging.*`, SqlClient, `System.*`, `dotnet-ef`), `opentelemetry`, `testes`, `ferramentas` (`dotnet-sonarscanner`, ReportGenerator) e `demais` |
| GitHub Actions | os SHAs fixados nos workflows, com o comentário `# vX.Y.Z` | `actions` |
| Docker | os `FROM` de `docker/sqlserver` e dos `Dockerfile`s da API, do Worker e da API simulada | `imagens` |
| Compose | as imagens de `docker-compose.yml`, que os testes de integração também usam | `compose` |

- **Quando**: toda segunda, 06:00 (horário de Brasília), no máximo 5 PRs abertos por
  ecossistema.
- **Regra das duas semanas**: as atualizações de versão só propõem versões publicadas há 14
  dias ou mais (`cooldown`). As **atualizações de segurança** não esperam: chegam assim que a
  correção sai, num PR próprio.
- **Major** vem sempre em PR separado, para ser avaliado à parte.
- **Ignorados**: FluentAssertions 8 ou maior (licença comercial; o projeto fica na 7.x) e a
  imagem `oms-sqlserver` do Compose, que o projeto monta.
- **Commits**: `build(deps): ...` (NuGet, Docker e Compose) e `ci(deps): ...` (Actions), sem ID
  de requisito, pela exceção da constituição 1.1.1. No NuGet o escopo vai fixo no prefixo: com
  `include: scope`, os grupos que misturam pacotes de produção e de teste saíam `build: ...`.
- **Fica manual**:
  - o SDK do `global.json` (o `rollForward: latestFeature` tolera a imagem `sdk` dos
    `Dockerfile`s andar à frente);
  - a CU da base do SQL Server em `docker/sqlserver/Dockerfile`: o Dependabot não entende tags
    como `2022-CU26-ubuntu-22.04` e não propõe nada (conferido na primeira execução, quando a
    `2022-CU27-ubuntu-22.04` já existia havia mais de dois meses). Confira as tags em
    <https://mcr.microsoft.com/v2/mssql/server/tags/list> e troque a CU num PR próprio.
- **Imagens dos testes**: os testes de integração leem as versões do RabbitMQ e do Mailpit do
  `docker-compose.yml`, então um PR do Dependabot atualiza o ambiente local e os testes juntos.
  A imagem de testes do SQL Server tem a tag pelo hash da pasta `docker/sqlserver`: uma base nova
  monta uma imagem nova.

### Tratar um PR do Dependabot

1. Confira o que mudou (as notas de versão vêm no PR) e as verificações: **Build e testes**
   (com a análise do SonarQube Cloud), **Demonstração** e o CodeQL, como em qualquer PR.
2. Tudo verde: `gh pr merge <n> --rebase --auto`. Nenhum PR do Dependabot entra sem essa ação
   do dono.
3. Depois de cada merge, os outros PRs dele precisam ficar em dia com a `main` (o ruleset
   exige). O Dependabot faz o rebase sozinho; se não fizer, comente `@dependabot rebase`.
4. Atualização que quebra: corrija na própria branch do PR, ou comente
   `@dependabot ignore this minor version` (ou `major`), ou feche o PR.
5. **Pacote novo**: se o PR acrescentar uma entrada nova ao `Directory.Packages.props` (por
   exemplo, um pino transitivo de segurança), ela só entra com um comentário de justificativa
   junto, como no grupo "Correções de segurança transitivas" (a constituição exige
   justificativa para toda dependência nova).

Os PRs do Dependabot recebem o `SONAR_TOKEN` pelo segredo **de Dependabot** (Settings →
Secrets and variables → Dependabot), cadastrado à parte do segredo de Actions. Esses segredos
só chegam aos PRs abertos pelo próprio Dependabot, nunca a PRs de forks.

## CodeQL

O workflow analisa o C# (sem compilar) e os workflows do GitHub Actions em todo PR para a
`main`, em todo push na `main` e toda segunda, com o conjunto de regras padrão do CodeQL (só
regras de segurança de alta precisão). Os alertas aparecem anotados no PR e em Security → Code
scanning.

- O ruleset **Proteção da main** exige o resultado: alerta **novo** de segurança alta ou
  crítica, ou de severidade erro, bloqueia o merge até ser corrigido ou dispensado.
- **Falso positivo** (ou risco aceito): dispense o alerta em Security → Code scanning com o
  motivo ("False positive", "Used in tests" ou "Won't fix") e um comentário, e registre na
  triagem abaixo. Não desligue a regra.
- **Default setup** do CodeQL fica desligado: a configuração é a do workflow versionado.
- **CodeQL fora do ar**: o merge espera; se for preciso, a regra de code scanning é retirada
  temporariamente do ruleset em Settings → Rules, com registro aqui, e volta quando o serviço
  voltar (o mesmo procedimento do SonarQube Cloud, REQ-20).

## Configuração fora dos arquivos

| Configuração | Valor | Como conferir |
|---|---|---|
| Alertas do Dependabot | ligados | `gh api repos/LucasFrenhan/Order-Management-System/vulnerability-alerts` responde 204 |
| Atualizações de segurança do Dependabot | ligadas | `gh api repos/LucasFrenhan/Order-Management-System/automated-security-fixes` → `"enabled": true` |
| CodeQL default setup | desligado | `gh api repos/LucasFrenhan/Order-Management-System/code-scanning/default-setup --jq .state` → `not-configured` |
| Segredo `SONAR_TOKEN` de Dependabot | cadastrado | `gh secret list --app dependabot` |
| Ruleset "Proteção da main" | com a regra de code scanning do CodeQL | [`.github/rulesets/protecao-main.json`](../.github/rulesets/protecao-main.json) e [Proteção da `main`](integracao-continua.md#proteção-da-main) |

## Triagem inicial

Alertas existentes quando as defesas foram ligadas, cada um com uma decisão.

| Origem | Alerta | Regra / aviso | Severidade | Decisão | Data |
|---|---|---|---|---|---|
| Dependabot | nenhum alerta na ativação | — | — | nada a decidir (`dotnet list package --vulnerable` também sem nenhuma) | 2026-11-08 |
| CodeQL | [#1](https://github.com/LucasFrenhan/Order-Management-System/security/code-scanning/1) e [#2](https://github.com/LucasFrenhan/Order-Management-System/security/code-scanning/2) (o mesmo ponto) | `cs/log-forging` | média (segurança), erro | corrigido: `ManipuladorExcecoes` logava o método e o caminho da requisição com quebras de linha possíveis (o `PathString` decodifica `%0A`); agora passam por `TextoDeLog.SemQuebrasDeLinha` | 2026-11-08 |
| CodeQL | workflows (`actions`) | — | — | nenhum alerta, nem para o `pull_request_target` de `prs-de-fork.yml` | 2026-11-08 |
