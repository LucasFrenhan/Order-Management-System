# Contrato: análise do CodeQL e exigência no ruleset

**Requisitos**: FR-009, FR-010 | **Research**: R-06, R-07

## Workflow `.github/workflows/codeql.yml`

| Item | Valor |
|---|---|
| `name` | `codeql` |
| Gatilhos | `pull_request` (branches `main`), `push` (branches `main`), `schedule` semanal (segunda, 09:00 UTC) |
| `permissions` (workflow) | `contents: read` |
| `permissions` (job) | `contents: read`, `security-events: write`, `actions: read` |
| Job | `analyze`, nome `Analyze (${{ matrix.language }})`, `runs-on: ubuntu-latest`, `timeout-minutes: 20` |
| Matriz | `language: [actions, csharp]`, `build-mode: none`, `fail-fast: false` |
| Passos | `actions/checkout` (mesmo SHA do `build-testes`) → `github/codeql-action/init` → `github/codeql-action/analyze` com `category: /language:${{ matrix.language }}` |
| Action | `github/codeql-action@9b3254085494b55a0f7c4c2b3d2d52cc712ce370 # v4.39.1` |
| Regras | conjunto padrão (sem `queries:`) |
| Concorrência | `codeql-${{ github.ref }}`, cancela a anterior só em PR |

Plano B para o C# (R-06): se a análise sem compilação falhar ou extrair mal, `build-mode: manual`
com `actions/setup-dotnet` (global.json) e `dotnet build PedidoFulfillment.sln -c Release`.

O default setup do CodeQL continua `not-configured`.

## Ruleset "Proteção da main" (PR 2)

Acréscimo em `.github/rulesets/protecao-main.json`, aplicado ao ruleset 26400051 depois da
primeira análise da `main`:

```json
{
  "type": "code_scanning",
  "parameters": {
    "code_scanning_tools": [
      { "tool": "CodeQL", "security_alerts_threshold": "high_or_higher", "alerts_threshold": "errors" }
    ]
  }
}
```

Efeito: o merge fica bloqueado enquanto a análise do CodeQL do PR não chegar, ou se ela trouxer
alerta novo de segurança alta/crítica ou de severidade erro. Sai pela correção ou pela dispensa do
alerta com justificativa. As verificações exigidas atuais (`Build e testes`, `Demonstração`,
`SonarCloud Code Analysis`) não mudam.

Queda do serviço: a regra `code_scanning` é retirada temporariamente, com registro em
`docs/seguranca-cadeia-build.md`, e volta quando o serviço voltar (mesmo procedimento do REQ-20).
