# Instruções do projeto para o Claude Code

O projeto segue Specification-Driven Development com o Spec Kit. A constituição fica em
`.specify/memory/constitution.md`, e cada requisito (REQ) nasce de uma issue do GitHub e
percorre `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` → `/speckit-tasks` →
`/speckit-analyze` → `/speckit-implement` → PR.

## Regra: só começar uma nova spec com tudo mergeado e sincronizado

Antes de iniciar um novo REQ (o primeiro passo é o `/speckit-specify`), é obrigatório:

1. **Nenhum PR aberto.** `gh pr list --state open --search "-author:app/dependabot"` precisa
   vir vazio. Se houver PR aberto, não comece a spec: avise quais PRs faltam e espere o merge.
   Os PRs do Dependabot (REQ-26) não contam, mas liste-os (`gh pr list --author app/dependabot`)
   e avise o dono se algum for uma atualização de segurança (o corpo do PR cita o aviso GHSA).
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
4. **Issue em andamento no GitHub**: veja a regra abaixo.

Se qualquer item falhar, pare e informe o que impede o início, em vez de contornar (por
exemplo, empilhar a branch sobre outra ainda não mergeada).

## Regra: issue iniciada passa de "Todo" para "In progress", com o dono como responsável

Toda vez que começar o trabalho de uma task ou issue registrada no GitHub (um REQ no
`/speckit-specify`, um estudo, uma investigação ou uma correção), mude o campo "Status" do
item dela no GitHub Project de **Todo** para **In progress** e atribua a issue ao dono do
repositório (`LucasFrenhan`), junto com o sync acima e antes de qualquer outro passo.

- O quadro é o projeto **OMS** (número 4, do usuário `LucasFrenhan`). O campo "Status" tem as
  opções `Todo`, `In progress` e `Done`. Ids (conferidos em 2026-10-27; se o comando falhar,
  confira de novo com `gh project field-list 4 --owner LucasFrenhan`):

  | O quê | Id |
  |-------|----|
  | Projeto OMS | `PVT_kwHOBbApDs4BoGZe` |
  | Campo Status | `PVTSSF_lAHOBbApDs4BoGZezhmoA40` |
  | Opção `In progress` | `47fc9ee4` |

- Comandos, com `N` = número da issue:

  ```bash
  item=$(gh project item-list 4 --owner LucasFrenhan --limit 200 --format json \
    -q '.items[] | select(.content.number == N) | .id')
  gh project item-edit --id "$item" --project-id PVT_kwHOBbApDs4BoGZe \
    --field-id PVTSSF_lAHOBbApDs4BoGZezhmoA40 --single-select-option-id 47fc9ee4
  gh issue edit N --add-assignee LucasFrenhan
  ```

  Para conferir antes e depois:
  `gh issue view N --json projectItems,assignees -q '{status: .projectItems, responsaveis: [.assignees[].login]}'`.
- Se a issue já tiver outro responsável, mantenha-o e acrescente o dono (`--add-assignee` não
  remove ninguém).
- O token precisa do escopo `project`. Sem ele, peça ao usuário para rodar
  `! gh auth refresh -h github.com -s project`.
- Se a issue não estiver em nenhum projeto, ou o status não for "Todo", avise e pergunte antes
  de mudar.
- Confirme a mudança no relatório do sync (issue, projeto, status novo e responsável).

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

## Regra: o README só cresce com permissão

O `README.md` é a porta de entrada do projeto e deve ficar curto. O detalhe vai para `docs/`
(como `docs/mensageria.md`, `docs/integracao-continua.md` e `docs/analise-estatica.md`), com o
README apontando para lá.

- **Antes de acrescentar qualquer coisa ao README** (seção, parágrafo, linha de tabela,
  comando ou item de lista), pergunte ao usuário e espere a permissão. Diga o que entraria e
  proponha a alternativa em `docs/`. Vale também para as tarefas de documentação dos REQs (por
  exemplo, a "linha do REQ" no README ou um roteiro novo).
- Sem pedir: corrigir um texto que ficou errado, trocar um link ou encurtar, desde que o README
  não cresça.
- Um documento novo em `docs/` ganha o link `[← Voltar ao README](../README.md)` no topo e
  entra na linha `docs/` da seção Estrutura do README (isso não precisa de permissão).

## Regra: manter os documentos de fluxo em dia

`docs/fluxos/` descreve cada fluxo de ponta a ponta: caminho feliz, erros tratados (com
resposta, exemplo e motivo), concorrência, falhas externas, logs e limitações. As specs
continuam sendo a fonte da verdade, e os documentos de fluxo as consolidam.

- Todo REQ que muda o comportamento de um fluxo (rota, resposta, mensagem ou código de
  erro, transição de status, evento, regra de consumidor, configuração, log ou limitação)
  MUST atualizar o documento desse fluxo **no mesmo PR**, na fase de Polish. Um fluxo novo
  ganha um documento novo, no mesmo formato, e uma linha no índice
  (`docs/fluxos/README.md`).
- Mudanças no ciclo de vida do pedido também atualizam o diagrama de estados do índice.
- As mensagens, os códigos, os limites e os IDs de log citados devem ser conferidos no
  código, e os links relativos não podem ficar quebrados.
- O `tasks.md` de cada REQ inclui uma tarefa de Polish para isso, e o PR cita os documentos
  atualizados em Validação. Se o REQ não muda nenhum fluxo, o PR diz isso.
- Uma limitação resolvida sai da seção "Limitações" do documento, e a issue que a
  resolveu fica no lugar da pendência.
