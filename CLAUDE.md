# Instruções do projeto para o Claude Code

O projeto segue Specification-Driven Development com o Spec Kit. A constituição fica em
`.specify/memory/constitution.md`, e cada requisito (REQ) nasce de uma issue do GitHub e
percorre `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-analyze` → `/speckit-implement` → PR.

## Regra: só começar uma nova spec com tudo mergeado e sincronizado

Antes de iniciar um novo REQ (o primeiro passo é o `/speckit-specify`), é obrigatório:

1. **Nenhum PR aberto.** `gh pr list --state open` precisa vir vazio. Se houver PR aberto,
   não comece a spec: avise quais PRs faltam e espere o merge.
2. **Sincronizar o git.**
   - `git fetch origin --prune`
   - `git switch main` e `git pull --ff-only`; a `main` local precisa ficar igual à
     `origin/main`.
   - Enviar o que estiver pendente: nenhuma branch de trabalho pode ter commits que não
     estão no remoto (`git status -sb` sem "ahead"). Se houver, faça `git push`, ou
     pergunte quando a branch não for do REQ atual.
   - Nenhuma alteração local sem commit (`git status --short` vazio).
3. **Branch nova a partir da `main` atualizada:**
   `git switch -c feature/req-NN-<nome> main`.

Se qualquer item falhar, pare e informe o que impede o início, em vez de contornar (por
exemplo, empilhar a branch sobre outra ainda não mergeada).

## Convenções já em uso

- Commits: `tipo(escopo): descrição [REQ-NN]`, um por etapa do Spec Kit e um por fase da
  implementação.
- PR em português (Resumo, Pontos de atenção, Validação), com `Closes #N` da issue.
- O número da issue no GitHub é diferente do número no título dela (o REQ-04 é a issue
  #5). O número do REQ segue a ordem do README, e a pasta em `specs/` acompanha o número
  do REQ.
- Mensageria com RabbitMQ.Client nativo: ignore as menções a MassTransit nas issues
  (constituição).
- Antes de abrir o PR, rode `dotnet build`, `dotnet test` e
  `dotnet format --verify-no-changes`. O workflow `build-testes` precisa ficar verde.
