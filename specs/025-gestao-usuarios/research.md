# Research: gestão de usuários e perfis (REQ-25)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

Todas as decisões abaixo partem do código da `main` em 2026-11-06. Nenhuma medição foi feita nesta etapa; o único número ainda não comprovado está em R-07 e é conferido no [quickstart](./quickstart.md).

## R-01: como guardar a desativação (FR-015, FR-019)

**Decisão**: duas colunas novas em `Usuarios`, `DesativadoEm` e `ReativadoEm`, ambas `datetimeoffset` e opcionais. A conta está desativada quando `DesativadoEm` não é nulo. Gravadas por `ExecuteUpdate`, sem passar pelo `UserManager`.

**Por quê**:

- A redefinição de senha faz `LockoutEnd = null` (`GestorIdentidade.cs:111`). Se a desativação usasse o bloqueio do Identity, a recuperação de senha reativaria a conta, o que o FR-019 proíbe.
- `ReativadoEm` é o que permite a regra dos webhooks sem contar nada à parte (R-06).
- O `ExecuteUpdate` não confere o `ConcurrencyStamp`. Um login simultâneo que grava `AccessFailedCount` não faz a desativação falhar, e o EF só grava no login as colunas que ele mudou, então não apaga a desativação.

**Alternativas descartadas**: `LockoutEnd` em uma data distante, pelo motivo acima; uma tabela de histórico, porque a auditoria fica só nos logs (issue).

## R-02: quem confere o Admin ativo a cada chamada (FR-001, FR-002)

**Decisão**: política nova `PoliticaGestaoUsuarios`, com `RequireRole(Admin)` e um requisito próprio, `RequisitoAdminAtivo`. O manipulador lê o `sub` do token e pergunta a `IGestorUsuarios.EhAdminAtivoAsync`. Falhou, a resposta é o 403 de sempre.

**Por quê**: a constituição pede validação por políticas (Princípio III), e o endpoint não pode acessar o banco. A resposta 403 sai pelo mesmo caminho das demais rotas, e todas as rotas de gestão ficam cobertas por estarem no mesmo grupo.

**Efeito**: uma consulta por chamada, só nessas rotas, com busca por chave primária e pelo índice de `UsuariosPerfis`. A `PoliticaAdministracao` (DLQ) e a `PoliticaCatalogo` não mudam: é a limitação aceita na clarificação, a registrar no fluxo 1.

**Alternativa descartada**: filtro de endpoint, que responderia 403 fora do pipeline de autorização e deixaria a regra fora da política.

## R-03: serialização das alterações (FR-013, FR-014)

**Decisão**: toda alteração de usuário roda em uma transação que começa com `sp_getapplock` no recurso fixo `oms-gestao-usuarios`, com `@LockOwner = 'Transaction'` e `@LockTimeout = 10000`. Dentro dela, confere de novo quem chama, carrega a conta e aplica as regras. Sem o lock em 10 s, a operação falha com 500, e nada muda.

**Por quê**:

- A soma "contar os Admins e depois retirar" não é atômica em `READ COMMITTED`: dois Admins removendo um ao outro passariam os dois. O lock fecha essa corrida entre instâncias, e o REQ-23 (issue #75) já mostrou que o `sp_getapplock` resolve disputa melhor que `HOLDLOCK` neste banco.
- Um único recurso basta: são operações raras, feitas por pessoas, e nenhuma rota do cliente espera por esse lock.
- Repetir a conferência de quem chama dentro do lock é o que dá o 403 ao perdedor da disputa (US3, cenário 6). Sem isso, ele seria avaliado com o estado antigo do manipulador da política.

**Alternativas descartadas**: `SERIALIZABLE`, que troca a espera por deadlocks; lock por conta, que não protege a contagem global.

**Definir a senha pelo convite não usa o lock**: isso só aumenta a contagem de Admins ativos.

## R-04: ordem das regras e onde elas ficam (FR-003, FR-011, FR-013)

**Decisão**: uma classe pura na Application, `RegrasGestaoUsuarios`, recebe o estado já lido e devolve a falha ou nada, em testes unitários sem banco. A ordem é:

1. Quem chama deixou de ser Admin ativo: 403.
2. Conta inexistente: 404.
3. A conta já está no estado pedido: sucesso, sem alterar nem auditar.
4. A operação tiraria o único Admin ativo: 409 `USUARIO_ULTIMO_ADMIN`.
5. É o próprio Admin desativando a conta ou retirando o próprio perfil Admin: 409 `USUARIO_PROPRIA_CONTA`.
6. A retirada deixaria a conta sem perfil: 409 `USUARIO_ULTIMO_PERFIL`.
7. A atribuição misturaria os grupos: 409 `USUARIO_PERFIS_INCOMPATIVEIS`.

**Por quê**: a regra 4 antes da 5 é o que a spec pede: o único Admin recebe o código do último Admin. Na prática, com a regra 1, a 4 só dispara quando o único Admin ativo tenta alterar a si mesmo.

## R-05: o convite usa a rota existente de redefinição (FR-008, FR-023)

**Decisão**: finalidade nova `ConviteUsuario` nos tokens de uso único e tipo novo `ConviteUsuario` nos e-mails. A pessoa define a senha em `POST /api/v1/auth/redefinicao-senha`, que passa a aceitar os dois tokens. O corpo e as respostas da rota não mudam.

**Por quê**:

- É a mesma ação (token + senha nova), com as mesmas regras de senha, a mesma resposta `TOKEN_INVALIDO` e o limite já aplicado, o que cumpre o FR-023 sem política nova.
- O contrato ganha só uma frase na descrição da rota, e nenhuma rota pública nova.
- O `redefinicao` já marca o e-mail como confirmado, zera a contagem de erros e encerra sessões. O que muda no convite: não pede o e-mail de aviso de senha redefinida, porque a pessoa acabou de receber o convite, e registra o log próprio.

**Como consumir**: um método novo, ao lado de `ConsumirTokenUsoUnicoAsync`, aceita as duas finalidades e devolve a conta e a finalidade: o mesmo `UPDATE` condicional por `ExecuteUpdate` e, depois, a leitura da linha, como já ocorre hoje. Na mesma transação, apaga os outros tokens de senha da conta, o que cumpre o caso "convite e recuperação ao mesmo tempo".

**Validade**: `Email:ValidadeConviteHoras`, padrão 72, faixa de 1 a 720, validada na partida como as demais. O token é gerado pelo Worker na hora de enviar, como no REQ-24 (R-03 de lá), e por isso a validade conta a partir do envio.

**Alternativa descartada**: rota nova `POST /auth/convite/aceite`, que duplicaria validação, limite e testes sem diferença para quem usa.

## R-06: webhooks da conta desativada (FR-020)

**Decisão**: uma regra só, nos dois pontos do `ProcessadorNotificacoes`. Um destino com dono não recebe o aviso se o dono está desativado, ou se o aviso é anterior a `ReativadoEm`.

- **Na distribuição** (`DistribuirAsync`, linha 107): a lista de destinos passa a trazer `DesativadoEm` e `ReativadoEm` do dono. Sem a entrega criada, nada é guardado.
- **No envio** (`EnviarAsync`, linha 209): uma entrega pendente de destino com dono é descartada pela mesma regra, como já ocorre com o destino inativo.

**Por quê**: cobre as três situações com uma comparação de datas, sem tocar em linhas de entrega na transação da desativação: o aviso gerado durante a desativação; o que estava pendente ao desativar; e o que estava atrasado no Worker quando a conta foi reativada, que não pode sair depois.

**Destinos globais**: têm `ClienteId` nulo e não passam por essa regra. `Ativo` e o contador de falhas do destino não mudam, e a reativação retoma os envios com os mesmos destinos e segredos.

**Custo**: uma consulta a mais por entrega de destino com dono. O envio já faz várias consultas por entrega, e o processador é limitado por lote.

## R-07: listagem e busca (FR-004)

**Decisão**: `GET /api/v1/admin/usuarios?perfil=&situacao=&busca=&tamanho=&cursor=`, ordenada por `Nome` e `Id`, com índice novo `IX_Usuarios_Nome_Id`. A paginação segue REQ-21 e REQ-22: `tamanho` de 1 a 100 (padrão 20), `proximo` e `anterior`, sem total, cursor assinado e ligado a quem chama e aos filtros (rótulo `usuarios`).

A `busca`, de 2 a 100 caracteres sem os espaços das pontas, usa `Contains` do EF Core em `Nome` e `Email`, com `EF.Functions.Collate` em uma collation explícita que ignora maiúsculas e acentos (`Latin1_General_100_CI_AI`). Assim, "joao" acha "João", em qualquer servidor. O custo é baixo, porque essa busca já lê a tabela inteira. O EF escapa `%` e `_`.

**Por quê**: o que se procura aqui é um trecho exato de e-mail ou de nome, para o que o Full-Text do catálogo serve mal: ele não acha o meio de uma palavra, e criar outro catálogo sobre uma tabela do Identity custaria caro para pouco ganho.

**Custo aceito**: o `LIKE '%trecho%'` lê a tabela inteira. Só o Admin usa. A meta de p95 abaixo de 500 ms com 100 mil contas **ainda não foi medida** e está no quickstart; se falhar, a decisão volta para o usuário antes do PR.

**Privacidade**: a `busca` pode trazer um pedaço de e-mail na URL. `Microsoft.AspNetCore` fica em `Warning` nos `appsettings`, então a URL não vai para o log por padrão. Nos spans do OpenTelemetry, isso **ainda não foi conferido**: a implementação confere em um teste que a query não sai no span e, se sair, a remove (SC-006).

**Duplicação do cursor**: `CodificadorCursorProdutos` e o de pedidos têm a mesma estrutura, e um terceiro seria mais uma cópia. A parte comum (MAC e Base64Url) vai para `EnvelopeCursor` em `Application/Comum`, usado pelo codificador novo e pelo de produtos. O formato não muda, e os testes de cursor existentes protegem isso. O de pedidos fica como está, para não crescer o escopo.

## R-08: login e renovação da conta desativada (FR-017, FR-018)

**Login** (`VerificarCredenciaisAsync`): antes do bloqueio, se a conta está desativada ou não tem senha, verifica a senha contra o hash de referência (o mesmo custo de um e-mail inexistente) e responde recusa, sem contar erro. Situação nova `Desativada`, com o motivo `ContaDesativada` no log 3003; a resposta continua o mesmo 401.

Hoje, com `PasswordHash` nulo, `CheckPasswordAsync` devolve falso sem calcular hash, o que revelaria por tempo a conta sem senha, e ainda contaria erro para o bloqueio. A mudança evita os dois.

**Renovação** (`RotacionarAsync`): a sessão é lida com a situação do dono. Conta desativada sai sem confirmar a transação, como já faz para sessão encerrada, com o mesmo 401.

**Corrida entre login e desativação**: um login já avaliado pode criar a sessão logo depois de as sessões serem encerradas. Enquanto a conta está desativada, a renovação recusa; e a reativação encerra de novo todas as sessões da conta, o que garante que nenhuma volte a valer.

**Ordem das gravações**: nas transações que encerram sessões, a conta é gravada por último (tokens de uso único, sessões, conta). O login insere a sessão e só então lê a conta, pela chave estrangeira; na ordem inversa, os dois se bloqueiam em deadlock e um deles responde 500. Vale para a desativação, a reativação, a redefinição e a troca de senha.

**Demais caminhos**:

- `RedefinirSenhaAsync` e `ConfirmarEmailAsync` conferem a situação depois de consumir o token e, desativada, saem sem confirmar. Isso cobre o token gerado pelo Worker no instante da desativação.
- `TrocarSenhaAsync` responde `SenhaAtualIncorreta`, sem verificar a senha nem contar erro.
- `ProcessadorEmails` descarta a solicitação de conta desativada antes de gerar o token, para todos os tipos, e o convite de conta que já tem senha.

## R-09: criação e perfis (FR-007 a FR-012)

**Criação**: `UserManager.CreateAsync(usuario)` sem senha, com o perfil e a solicitação de e-mail na mesma transação, como o registro. E-mail duplicado reaproveita o 409 `USUARIO_EMAIL_JA_CADASTRADO`, inclusive pela violação do índice único.

**Perfis**: gravados direto em `UsuariosPerfis` pelo `ContextoBancoDados`, dentro do lock. `AddToRoleAsync` passa pelo `UpdateAsync`, que confere o `ConcurrencyStamp` e poderia falhar por um login simultâneo; a linha da tabela não depende disso.

**O parâmetro `{perfil}`** chega como texto: um valor desconhecido é 400 na validação, e não um 404 de rota.

## R-10: auditoria (FR-021)

**Decisão**: logs 3019 a 3025, emitidos pela Application depois do commit, com a ação, o id de quem fez, o id da conta e o perfil, quando houver.

| Id | Nome | Nível | Quando |
|----|------|-------|--------|
| 3019 | `ConviteAceito` | Information | A senha foi definida pelo convite |
| 3020 | `UsuarioInternoCriado` | Information | Criação |
| 3021 | `PerfilAtribuido` | Information | Atribuição efetiva |
| 3022 | `PerfilRetirado` | Information | Retirada efetiva |
| 3023 | `UsuarioDesativado` | Information | Desativação efetiva |
| 3024 | `UsuarioReativado` | Information | Reativação efetiva |
| 3025 | `AlteracaoUsuarioRecusada` | Warning | Recusa por último Admin ou por própria conta |

Os ids 3001 a 3018 estão em uso. Nenhuma métrica nova: a issue pede só logs. `oms.emails` ganha o tipo `ConviteUsuario` pelo atributo que já existe.

## Conferências da implementação

- **Linha de base** (T001, 2026-11-06): 1.244 unitários + 846 de integração, sem falhas.
- **`busca` fora da telemetria** (T022): o valor não aparece em logs, em atributos nem em nomes de spans, conferido em `GestaoUsuariosConsultaTestes`. Nenhuma mudança foi preciso.
- **Manipulador da política**: sem o perfil Admin no token, o 403 sai sem consultar o banco. Na primeira versão, um Cliente recebia 500 com o banco fora do ar.
- **Deadlock entre login e encerramento de sessões** (suíte completa da T056): a desativação respondeu 500 com o erro 1205 do SQL Server. Forçando a corrida com 6 logins por rodada, em 400 rodadas, houve 15 desativações e 22 logins com 500. A troca de senha, do REQ-24, tinha o mesmo ciclo, reproduzido em teste. Corrigido pela ordem das gravações do [R-08](#r-08-login-e-renovação-da-conta-desativada-fr-017-fr-018).
