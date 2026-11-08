# Quickstart: validar o REQ-26

**Spec**: [spec.md](./spec.md) | **Contratos**: [contracts/](./contracts/)

Pré-requisitos: `gh` autenticado com acesso de administração ao repositório, .NET SDK do
`global.json` e PowerShell 7 (`pwsh`).

## 1. Configuração do repositório (antes do merge do PR 1)

```bash
gh api -X PUT repos/LucasFrenhan/Order-Management-System/vulnerability-alerts
gh api -X PUT repos/LucasFrenhan/Order-Management-System/automated-security-fixes
gh secret set SONAR_TOKEN --app dependabot      # valor informado pelo dono
```

Conferir conforme a tabela "Estado no GitHub" do [data-model](./data-model.md).

## 2. A verificação de vulnerabilidades com exemplos fixos

Os exemplos ficam em `.github/scripts/exemplos-vulnerabilidades/`:

| Exemplo | Esperado |
|---|---|
| `sem-vulnerabilidades.json` | saída 0, "Nenhuma vulnerabilidade conhecida…" |
| `so-moderada.json` | saída 0, linha ⚠️ aviso |
| `alta-transitiva.json` | saída 1, linha ❌ reprova, tipo transitiva |
| `alta-transitiva.json` com `excecoes-vigente.json` | saída 0, linha ✅ exceção |
| `alta-transitiva.json` com `excecoes-vencida.json` | saída 1 |
| `alta-transitiva.json` com `excecoes-invalida.json` | saída 2 (motivo vazio, prazo acima de 90 dias) |
| `com-problema.json` | saída 2 |

```powershell
pwsh .github/scripts/exemplos-vulnerabilidades/conferir.ps1     # os sete casos
pwsh .github/scripts/verificar-vulnerabilidades.ps1 -Entrada .github/scripts/exemplos-vulnerabilidades/alta-transitiva.json -Hoje 2026-11-08; "saída $LASTEXITCODE"
```

Contra a solução real (deve dar 0 e a mensagem de nenhuma vulnerabilidade):

```powershell
dotnet restore PedidoFulfillment.sln; pwsh .github/scripts/verificar-vulnerabilidades.ps1
```

## 3. Uma vulnerabilidade alta reprova o CI (SC-001)

Num PR descartável (rascunho), acrescentar ao `Directory.Packages.props` um pino transitivo
vulnerável, por exemplo `<PackageVersion Include="System.Text.Json" Version="8.0.0" />` (High,
GHSA-hh2w-p6rv-4g7w e GHSA-8g4q-xg66-9fp4). Esperado: o passo "Vulnerabilidades" reprova em
cerca de 1 min, e o resumo do job mostra o pacote, a versão, a severidade e os avisos. Fechar o PR
sem merge e apagar a branch.

## 4. CodeQL no PR e na `main` (SC-002)

- No PR 1: as verificações `Analyze (actions)` e `Analyze (csharp)` aparecem e terminam.
- Depois do merge: `gh api repos/LucasFrenhan/Order-Management-System/code-scanning/analyses --jq '.[0:4][] | [.ref, .category, .created_at] | @tsv'`
  mostra as análises de `refs/heads/main` para as duas categorias.

## 5. CodeQL bloqueia o merge (SC-007, depois do PR 2)

Num PR descartável, introduzir um alerta de segurança alta detectável (por exemplo, um caminho de
arquivo montado a partir de um parâmetro da requisição) e conferir que o merge fica bloqueado pela
regra de code scanning. Fechar sem merge.

## 6. Dependabot (SC-003, SC-004)

Depois do merge do PR 1:

- Aba Insights → Dependency graph → Dependabot: cada ecossistema mostra a última verificação sem
  erro (ou disparar "Check for updates").
- `gh pr list --author app/dependabot` lista os PRs; conferir título `build(deps)`/`ci(deps)`,
  agrupamento e que nenhuma versão proposta tem menos de 14 dias.
- Num deles: verificações exigidas todas presentes (incluindo o Quality Gate),
  `gh pr merge <n> --rebase --auto`, merge concluído.

## 7. Imagens numa fonte única (FR-019)

```bash
dotnet test tests/PedidoFulfillment.Testes.Integracao --filter "FullyQualifiedName~ImagensDoAmbiente"   # sem Docker
grep -rn "rabbitmq:\|mailpit:v" tests/ --include=*.cs | grep -v ImagensDoAmbienteTestes   # vazio: só o exemplo do teste tem versão
```

E a suíte de integração completa continua verde (`dotnet test`).

## 8. Triagem (SC-005)

`gh api repos/LucasFrenhan/Order-Management-System/code-scanning/alerts?state=open` e
`.../dependabot/alerts?state=open` só trazem alertas que estão na tabela de
`docs/seguranca-cadeia-build.md` com decisão "issue" (os demais estão corrigidos ou dispensados).
