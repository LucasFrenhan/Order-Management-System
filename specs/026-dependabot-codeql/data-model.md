# Data Model: Dependências vulneráveis e CodeQL na cadeia de build (REQ-26)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Nada muda no banco, nas mensagens ou na API. Os "dados" deste requisito são arquivos versionados
no repositório e o estado guardado no GitHub.

## Exceção de vulnerabilidade — `.github/vulnerabilidades-aceitas.json`

```json
{
  "excecoes": [
    {
      "pacote": "Exemplo.Pacote",
      "aviso": "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx",
      "motivo": "Sem versão corrigida; o código vulnerável não é usado (ver issue #NN).",
      "registradaEm": "2026-11-08",
      "revisarAte": "2026-12-08"
    }
  ]
}
```

| Campo | Regra |
|---|---|
| `pacote` | Obrigatório; id do pacote NuGet, comparado sem diferenciar maiúsculas |
| `aviso` | Obrigatório; URL do aviso exatamente como o `dotnet list package` informa (`advisoryurl`) |
| `motivo` | Obrigatório, não vazio |
| `registradaEm` | Obrigatório, `AAAA-MM-DD` |
| `revisarAte` | Obrigatório, `AAAA-MM-DD`, no máximo 90 dias depois de `registradaEm` |

- Chave: `pacote` + `aviso` (sem repetição).
- Estados, calculados a cada execução: **vigente** (`revisarAte` ≥ hoje, em UTC) → aceita a
  vulnerabilidade; **vencida** → não aceita, e o CI reprova; **sem uso** (nenhuma vulnerabilidade
  atual casa) → aviso no resumo.
- Arquivo ausente equivale a lista vazia; arquivo inválido (JSON ou campos) reprova.

## Vulnerabilidade encontrada (saída da verificação)

Lida de `dotnet list package --vulnerable --include-transitive --format json`, sem persistência.

| Atributo | Origem |
|---|---|
| Projeto | `projects[].path` |
| Pacote, versão | `id`, `resolvedVersion` |
| Direta ou transitiva | `topLevelPackages` ou `transitivePackages` |
| Severidade | `vulnerabilities[].severity`: `Low`, `Moderate`, `High`, `Critical` |
| Aviso | `vulnerabilities[].advisoryurl` |
| Situação | `reprova` (High/Critical sem exceção vigente), `exceção` (com exceção vigente), `aviso` (Low/Moderate) |

A mesma vulnerabilidade em vários projetos aparece uma vez no resumo, com a lista de projetos.

## Fonte das versões de imagem — `docker-compose.yml`

| Serviço | Imagem (hoje) | Lida pelos testes |
|---|---|---|
| `rabbitmq` | `rabbitmq:4.3.6-management` | `MensageriaTeste`, `SaudeComDependenciasTestes` |
| `mailpit` | `axllent/mailpit:v1.33.0` | `MailpitTeste` |
| `sqlserver` | `oms-sqlserver:fts` (montada de `docker/sqlserver`) | não: os testes montam do `Dockerfile`, com tag pelo hash da pasta |
| `aspire-dashboard` | `mcr.microsoft.com/dotnet/aspire-dashboard:13.6.0` | não |

Regras da leitura (`ImagensDoAmbiente`): serviço sob `services:`, chave `image:` no bloco dele;
a imagem precisa ter tag e não pode ser `latest`; serviço ou imagem ausente lança
`InvalidOperationException` com o nome do serviço e o caminho do arquivo.

## Registro de triagem — `docs/seguranca-cadeia-build.md`

| Coluna | Conteúdo |
|---|---|
| Origem | Dependabot ou CodeQL |
| Alerta | número e link |
| Regra / aviso | id da regra do CodeQL ou GHSA |
| Severidade | como o GitHub informa |
| Decisão | corrigido (commit/PR), dispensado (motivo do GitHub: falso positivo, usado em teste, não será corrigido) ou issue |
| Data | AAAA-MM-DD |

## Estado no GitHub (fora de arquivo, registrado em `docs/`, FR-018)

| Configuração | Valor alvo | Como conferir |
|---|---|---|
| Alertas do Dependabot | ligados | `gh api repos/{repo}/vulnerability-alerts` → 204 |
| Security updates | ligados | `gh api repos/{repo}/automated-security-fixes` → `enabled: true` |
| CodeQL default setup | desligado (`not-configured`) | `gh api repos/{repo}/code-scanning/default-setup` |
| Segredo `SONAR_TOKEN` de Dependabot | cadastrado | `gh secret list --app dependabot` |
| Ruleset "Proteção da main" | com a regra `code_scanning` (PR 2) | `.github/rulesets/protecao-main.json` e `gh api repos/{repo}/rulesets/26400051` |
