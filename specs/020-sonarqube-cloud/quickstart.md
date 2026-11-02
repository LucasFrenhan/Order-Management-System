# Quickstart: análise estática com SonarQube Cloud e Quality Gate no CI

**Requisito**: REQ-20 | **Contrato**: [análise no CI](./contracts/analise-no-ci.md)

Roteiro de validação. Painel do projeto:
<https://sonarcloud.io/project/overview?id=LucasFrenhan_Order-Management-System>.

## 1. Análise no PR (US1)

Abrir o PR de integração. Esperado: o job `Build e testes` roda o `sonarscanner begin` e o
`end` sem mostrar o token; o PR recebe a verificação `SonarCloud Code Analysis` e o comentário
com o Quality Gate, problemas novos, cobertura e duplicação; o tempo do job cresce no máximo
5 min em relação ao último PR (SC-007).

## 2. Análise da `main` e situação de partida (US3, US4)

Depois do merge (rebase), o push na `main` roda só o `Build e testes`, com a análise. No
painel: cobertura diferente de 0% e a até 2 pontos da cobertura do resumo do job (SC-003);
nenhum arquivo de `Persistencia/Migracoes`, `specs/` ou `.specify/` entre os arquivos
analisados; os projetos `tests/` listados como teste.

## 3. Quality Gate bloqueando o merge (US2)

Num PR descartável:

1. Introduzir uma possível referência nula num serviço do Application e abrir o PR. Esperado:
   Quality Gate reprovado, merge bloqueado.
2. Noutro PR descartável, acrescentar um método público sem testes. Esperado: cobertura do
   código novo abaixo de 80%, Quality Gate reprovado, merge bloqueado.
3. Fechar os dois PRs sem merge.

## 4. Push direto e fork

```bash
git push origin HEAD:main     # recusado pelo ruleset
```

Um PR de fork é comentado e fechado pelo `prs-de-fork`; se reaberto, o `Build e testes` roda
sem a análise (sem acesso ao token) e o merge continua bloqueado.

## 5. Pontos de segurança (US4)

No painel, Security Hotspots: nenhum "To review"; cada credencial de desenvolvimento marcada
"Safe" com a justificativa registrada no research R-10.

## 6. Suíte

```bash
dotnet build
dotnet test
dotnet format --verify-no-changes
```
