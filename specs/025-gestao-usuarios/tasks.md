---

description: "Lista de tarefas do REQ-25 — Gestão de usuários e perfis pelo Admin"
---

# Tasks: Gestão de usuários e perfis pelo Admin

**Input**: Design documents from `/specs/025-gestao-usuarios/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/gestao-usuarios.md](./contracts/gestao-usuarios.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras e integração com sucesso, 401 e 403, e a spec
pede o mesmo no FR-024. Os testes da fundação vêm antes da implementação; nas fases das histórias, descrevem o
comportamento esperado, e os que falharem orientam o ajuste.

**Organization**: uma fundação com o estado da conta, as regras, o lock e a política; depois uma fase por história.
**O MVP são as três histórias P1**: US1 (consulta), US2 (criação com convite) e US3 (perfis). A US4 (desativação) é P2 e
depende só da fundação, mas fecha um critério de aceite da issue: o PR só sai com as quatro.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência de tarefa incompleta)
- **[Story]**: história da spec (US1, US2, US3, US4)

## Convenções válidas para todas as tarefas

- Nomes e mensagens em português; as mensagens são as do [contrato](./contracts/gestao-usuarios.md).
- As regras ficam na Application; a API só traduz HTTP; o acesso a dados fica na Infrastructure.
- Logs de auditoria só com ações, ids e perfil, nunca com nome, e-mail ou endereço de origem.
- Nada de novo pacote NuGet.
- Um commit por fase: `tipo(usuarios): descrição [REQ-25]`.
- Antes de seguir de fase, `dotnet build` e os testes da fase verdes.

---

## Phase 1: Setup

**Purpose**: registrar o ponto de partida.

- [X] T001 Registrar a linha de base com `dotnet build` e `dotnet test` em `PedidoFulfillment.sln`, anotar o total de testes para comparar na T056, e tirar o retrato do Docker (contêineres, volumes e imagens) para conferir na T057

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: estado da conta, regras, serialização e política, usados por todas as histórias.

**⚠️ CRITICAL**: nenhuma história começa antes do fim desta fase.

- [X] T002 Acrescentar a `src/PedidoFulfillment.Infrastructure/Identidade/Usuario.cs` as propriedades `DesativadoEm` e `ReativadoEm`, ambas `DateTimeOffset?`: "Não nula = conta desativada"; `ReativadoEm` é "preenchida na reativação e mantida depois" (data-model)
- [X] T003 Declarar o índice `IX_Usuarios_Nome_Id` (`Nome`, `Id`) em `src/PedidoFulfillment.Infrastructure/Persistencia/ContextoBancoDados.cs` e gerar a migration `GestaoUsuarios` em `src/PedidoFulfillment.Infrastructure/Persistencia/Migracoes/`, só com acréscimos: contas existentes ficam ativas, com as duas datas nulas
- [X] T004 [P] Acrescentar `ConviteUsuario` a `FinalidadeTokenUsoUnico` em `src/PedidoFulfillment.Application/Autenticacao/IArmazenamentoSessoes.cs` e a `TipoEmail` em `src/PedidoFulfillment.Application/Emails/TipoEmail.cs`. Os dois são gravados como texto em `varchar(30)`, sem mudar a coluna
- [X] T005 [P] Acrescentar `USUARIO_ULTIMO_ADMIN`, `USUARIO_PROPRIA_CONTA`, `USUARIO_ULTIMO_PERFIL` e `USUARIO_PERFIS_INCOMPATIVEIS` a `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs`
- [X] T006 [P] Criar os DTOs em `src/PedidoFulfillment.Application/Contratos/Usuarios/`: `CriarUsuarioInternoRequestDTO` (obrigatórios `nome`, `email` e `perfil`, este só `Admin` ou `OperadorLogistica`, com as regras e as mensagens de nome e e-mail do `RegistrarUsuarioRequestDTO`; sem campo de senha), `UsuarioGestaoResponseDTO` (obrigatórios `id`, `nome`, `email`, `perfis`, `situacao`, `emailConfirmado` e `convitePendente`) e `PaginaUsuariosResponseDTO` (`itens`, `tamanho`, `proximo` e `anterior`)
- [X] T007 [P] Escrever `tests/PedidoFulfillment.Testes.Unitarios/Usuarios/RegrasGestaoUsuariosTestes.cs` cobrindo a ordem de R-04: quem chama não é Admin ativo; repetição sem efeito; único Admin tirando o próprio perfil ou se desativando recebe `USUARIO_ULTIMO_ADMIN`, mesmo com um único perfil; com outro Admin ativo, `USUARIO_PROPRIA_CONTA`; retirar o próprio perfil `OperadorLogistica` é permitido; retirar o único perfil recebe `USUARIO_ULTIMO_PERFIL`; Cliente com perfil interno e interna com Cliente recebem `USUARIO_PERFIS_INCOMPATIVEIS`; conta sem perfil aceita qualquer grupo; conta com grupos misturados aceita retirada e recusa atribuição
- [X] T008 Implementar `src/PedidoFulfillment.Application/Usuarios/RegrasGestaoUsuarios.cs`, classe pura que recebe o estado já lido e devolve a falha ou nada, na ordem de [R-04](./research.md#r-04-ordem-das-regras-e-onde-elas-ficam-fr-003-fr-011-fr-013), com os `title` e `detail` do contrato
- [X] T009 Definir `src/PedidoFulfillment.Application/Usuarios/IGestorUsuarios.cs` com `EhAdminAtivoAsync`, as leituras e as alterações usadas pelas histórias, e `src/PedidoFulfillment.Application/Usuarios/EventosGestaoUsuarios.cs` com os logs 3020 a 3025 de [R-10](./research.md#r-10-auditoria-fr-021)
- [X] T010 Implementar a base de `src/PedidoFulfillment.Infrastructure/Identidade/GestorUsuarios.cs` e registrá-lo em `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs`: `EhAdminAtivoAsync` e a contagem de Admins ativos ("perfil Admin, `DesativadoEm` nula e `PasswordHash` não nulo"; o bloqueio não entra), e uma alteração em transação que começa com `sp_getapplock` em `oms-gestao-usuarios`, `@LockOwner = 'Transaction'`, `@LockTimeout = 10000`, falhando sem alterar nada se o lock não vier (R-03)
- [X] T011 Criar `src/PedidoFulfillment.API/Seguranca/RequisitoAdminAtivo.cs` (requisito e manipulador, que lê o `sub` e delega ao gestor) e acrescentar `PoliticaGestaoUsuarios` a `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs`, sem mudar `PoliticaAdministracao` nem `PoliticaCatalogo` (R-02)
- [X] T012 Criar `src/PedidoFulfillment.API/Endpoints/EndpointsUsuarios.cs` com o grupo `/api/v1/admin/usuarios`, tag própria e `PoliticaGestaoUsuarios`, e mapeá-lo em `src/PedidoFulfillment.API/Program.cs`, fora do limite de requisições (FR-023)
- [X] T013 Criar `src/PedidoFulfillment.Application/Usuarios/IServicoGestaoUsuarios.cs` e a base de `ServicoGestaoUsuarios.cs` na mesma pasta, e registrá-los junto dos demais serviços da Application
- [X] T014 [P] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestorUsuariosTestes.cs`: a contagem de Admins ativos ignora desativado e sem senha e inclui o bloqueado; `EhAdminAtivoAsync` é falso para os mesmos casos; alterações simultâneas ficam em sequência; uma exceção no meio não deixa nada gravado

**Checkpoint**: a migration aplica, as regras têm testes, e o grupo de rotas existe com a política.

---

## Phase 3: User Story 1 — Consultar os usuários (Priority: P1) 🎯 MVP

**Goal**: o Admin lista, filtra e consulta contas, sem nenhum dado de credencial.

**Independent Test**: com contas de perfis diferentes, listar com e sem filtros, percorrer as páginas e consultar pelo id.

- [X] T015 [P] [US1] Escrever `tests/PedidoFulfillment.Testes.Unitarios/Usuarios/CodificadorCursorUsuariosTestes.cs` e `FiltroListagemUsuariosTestes.cs`: cursor adulterado, de outro usuário, de outros filtros ou de produtos é recusado; `busca` de 2 a 100 caracteres sem os espaços das pontas; `tamanho` de 1 a 100, padrão 20; valores desconhecidos de `perfil` e `situacao`
- [X] T016 [P] [US1] Extrair a parte comum dos cursores (MAC e Base64Url) para `src/PedidoFulfillment.Application/Comum/EnvelopeCursor.cs` e usá-la em `src/PedidoFulfillment.Application/Produtos/CodificadorCursorProdutos.cs`, sem mudar o formato: `CodificadorCursorProdutosTestes` continua verde sem alteração (R-07)
- [X] T017 [US1] Criar `CodificadorCursorUsuarios.cs` (rótulo `usuarios`), `PosicaoCursorUsuarios.cs` (nome e id) e `FiltroListagemUsuarios.cs` em `src/PedidoFulfillment.Application/Usuarios/`
- [X] T018 [US1] Implementar em `src/PedidoFulfillment.Infrastructure/Identidade/GestorUsuarios.cs` a leitura por id e a listagem por cursor, ordenada por `Nome` e `Id`, com filtros combinados por perfil, situação e `busca` por `Contains` em `Nome` e `Email`, com `EF.Functions.Collate` em `Latin1_General_100_CI_AI` (R-07); `situacao` e `convitePendente` vêm do [data-model](./data-model.md#usuarios-tabela-existente)
- [X] T019 [US1] Implementar a consulta e a listagem em `src/PedidoFulfillment.Application/Usuarios/ServicoGestaoUsuarios.cs` e as rotas `GET` em `src/PedidoFulfillment.API/Endpoints/EndpointsUsuarios.cs`, com os parâmetros documentados em `src/PedidoFulfillment.API/OpenApi/`; id mal formado é 400, sem a restrição `{id:guid}`
- [X] T020 [US1] Acrescentar as rotas `GET` e os schemas a `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [X] T021 [P] [US1] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosConsultaTestes.cs`: sucesso, 401 e 403 (Cliente, OperadorLogistica) nas duas rotas; **403 para o token válido de quem perdeu o Admin ou foi desativado** (US1, cenário 6; a conta precisa ter também `OperadorLogistica` para poder perder o Admin); filtros combinados; páginas para frente e para trás sem repetir nem pular com nomes iguais; `busca` com `%` e `_` tratados como texto; "joao" e "JOAO" acham "João"; 404 e 400; nenhuma resposta contém senha, hash, carimbo ou token (FR-006)
- [X] T022 [US1] Acrescentar ao teste da T021 a conferência com `ColetorTelemetria` e `CapturaLogs` de que o valor de `busca` não aparece em spans nem em logs (R-07); se aparecer, removê-lo da telemetria em `src/PedidoFulfillment.API/` e registrar em `specs/025-gestao-usuarios/research.md`

**Checkpoint**: a consulta funciona sozinha.

---

## Phase 4: User Story 2 — Criar um usuário interno (Priority: P1) 🎯 MVP

**Goal**: o Admin cria uma conta interna sem senha, e a pessoa define a senha pelo convite.

**Independent Test**: no ambiente local, criar um OperadorLogistica, ler o e-mail capturado, definir a senha e fazer login.

- [X] T023 [US2] Implementar a criação em `src/PedidoFulfillment.Infrastructure/Identidade/GestorUsuarios.cs` e em `src/PedidoFulfillment.Application/Usuarios/ServicoGestaoUsuarios.cs`, pela alteração com lock da T010, conferindo de novo quem chama (403 se deixou de ser Admin ativo): conta sem senha, perfil e solicitação de e-mail `ConviteUsuario` na mesma transação; e-mail duplicado, inclusive pelo índice único, recebe `USUARIO_EMAIL_JA_CADASTRADO`; log 3020 depois do commit (R-09)
- [X] T024 [US2] Acrescentar a rota `POST` a `src/PedidoFulfillment.API/Endpoints/EndpointsUsuarios.cs`, com `Location` e `FiltroValidacao`, e a `specs/001-contratos-api/contracts/openapi-v1.yaml`, junto com a frase sobre o convite na descrição de `redefinicao-senha`
- [X] T025 [P] [US2] Acrescentar `ValidadeConviteHoras` ("padrão 72, faixa de 1 a 720") a `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesEmail.cs`, com a mensagem citando a chave, e o modelo do convite a `src/PedidoFulfillment.Application/Emails/ModelosEmail.cs`, em português, com a validade e o aviso de que a recuperação cobre o prazo vencido, sem a senha nem o nome de quem criou
- [X] T026 [US2] Em `src/PedidoFulfillment.Infrastructure/Emails/ProcessadorEmails.cs`, gerar o token `ConviteUsuario` na hora de enviar e descartar o convite de conta que já tem senha
- [X] T027 [US2] Em `src/PedidoFulfillment.Application/Autenticacao/IArmazenamentoSessoes.cs` e `src/PedidoFulfillment.Infrastructure/Autenticacao/ArmazenamentoSessoes.cs`, criar o consumo que aceita `RecuperacaoSenha` e `ConviteUsuario`, devolve a conta e a finalidade e apaga os outros tokens de senha da conta (R-05)
- [X] T028 [US2] Em `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, `src/PedidoFulfillment.Application/Autenticacao/IGestorIdentidade.cs` e `ServicoSenhas.cs`, fazer a redefinição usar esse consumo e devolver a finalidade: pelo convite, sem o e-mail de aviso e com o log 3019 em `EventosAutenticacao.cs`; pela recuperação, como hoje
- [X] T029 [US2] Em `VerificarCredenciaisAsync` de `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, tratar a conta sem senha antes do bloqueio: verifica contra o hash de referência, recusa como credenciais inválidas e não conta erro (R-08)
- [X] T030 [P] [US2] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosCriacaoTestes.cs`: 201 e `Location`; 401 e 403; Cliente ou perfil desconhecido é 400; nome e e-mail inválidos; 409 sequencial e simultâneo, com um único 201; log 3020 só com ids e perfil
- [X] T031 [P] [US2] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/ConviteTestes.cs`, com o Mailpit: o e-mail chega com o token; definir a senha dá 204, login funciona, e-mail confirmado, `convitePendente` falso, sem e-mail de aviso; token vencido, usado ou inexistente recebe o mesmo `TOKEN_INVALIDO`; senha fora das regras não gasta o token; login sem senha é o 401 de sempre e não conta para o bloqueio; convite vencido é resolvido pela recuperação; convite e recuperação ao mesmo tempo, o primeiro invalida o outro; a definição conta para a política `redefinicao`; com `ValidadeConviteHoras`, a validade conta a partir do envio; e a solicitação com mais de 24 horas sem enviar é descartada, a conta fica com o convite pendente e a recuperação resolve
- [X] T032 [P] [US2] Acrescentar a `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/SigiloTokensTestes.cs` a conferência de que o token de convite não aparece em nenhuma coluna nem em logs, e a `tests/PedidoFulfillment.Testes.Unitarios/` os casos de `ValidadeConviteHoras` fora da faixa

**Checkpoint**: um usuário interno ganha acesso sem reiniciar o sistema (SC-001).

---

## Phase 5: User Story 3 — Atribuir e retirar perfis (Priority: P1) 🎯 MVP

**Goal**: promover e rebaixar contas internas, sem misturar grupos e sem ficar sem Admin.

**Independent Test**: com dois Admins, retirar o perfil de um, confirmar que o outro não pode perder o dele e conferir os perfis novos depois de renovar a sessão.

- [ ] T033 [US3] Implementar a atribuição e a retirada em `src/PedidoFulfillment.Infrastructure/Identidade/GestorUsuarios.cs`, gravando em `UsuariosPerfis` dentro do lock, sem o `AddToRoleAsync`, e conferindo de novo quem chama (R-03, R-09)
- [ ] T034 [US3] Implementar em `src/PedidoFulfillment.Application/Usuarios/ServicoGestaoUsuarios.cs` o uso de `RegrasGestaoUsuarios`, com os logs 3021, 3022 e, nas recusas de último Admin e de própria conta, 3025, sempre depois de a transação terminar; repetição sem efeito não gera log
- [ ] T035 [US3] Acrescentar `PUT` e `DELETE` `/{id}/perfis/{perfil}` a `src/PedidoFulfillment.API/Endpoints/EndpointsUsuarios.cs`, com `{perfil}` como texto e 400 para valor desconhecido, e a `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [ ] T036 [P] [US3] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosPerfisTestes.cs`: sucesso, 401 e 403 nas duas rotas; os quatro códigos de 409; idempotência sem log; 404 e 400; `PUT` de `Admin` em conta de Cliente; rebaixar um Admin de perfil único recebe `USUARIO_ULTIMO_PERFIL`, e atribuir `OperadorLogistica` antes resolve; a renovação traz os perfis novos, e o token antigo mantém os antigos nas rotas de pedido até expirar
- [ ] T037 [US3] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosConcorrenciaTestes.cs`: em 50 rodadas, dois Admins que também têm `OperadorLogistica` retiram o perfil Admin um do outro ao mesmo tempo; sempre uma aceita, a outra 403, e sobra um Admin ativo (SC-003, US3, cenário 6), inclusive com duas instâncias da API sobre o mesmo banco

**Checkpoint**: o MVP está completo.

---

## Phase 6: User Story 4 — Desativar e reativar uma conta (Priority: P2)

**Goal**: tirar o acesso sem apagar o histórico, sem revelar a desativação, e poder devolvê-lo.

**Independent Test**: desativar uma conta com sessão aberta e pedidos, confirmar que nada devolve o acesso e que os pedidos seguem consultáveis; reativar e fazer login com a mesma senha.

- [ ] T038 [US4] Implementar em `src/PedidoFulfillment.Infrastructure/Identidade/GestorUsuarios.cs` a desativação (grava `DesativadoEm` por `ExecuteUpdate`, encerra as sessões e apaga os tokens de uso único) e a reativação (limpa `DesativadoEm`, grava `ReativadoEm` e encerra de novo as sessões), em transação e dentro do lock (R-01, R-08)
- [ ] T039 [US4] Implementar as duas operações em `src/PedidoFulfillment.Application/Usuarios/ServicoGestaoUsuarios.cs`, com `RegrasGestaoUsuarios` e os logs 3023, 3024 e 3025, e as rotas `POST /{id}/desativacao` e `POST /{id}/reativacao` em `src/PedidoFulfillment.API/Endpoints/EndpointsUsuarios.cs` e em `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [ ] T040 [US4] Em `VerificarCredenciaisAsync` de `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, `src/PedidoFulfillment.Application/Autenticacao/ResultadoCredenciais.cs` e `ServicoAutenticacao.cs`, tratar a conta desativada antes do bloqueio: verifica contra o hash de referência, sem contar erro; situação `Desativada`; motivo `ContaDesativada` no log 3003; o mesmo 401 (R-08)
- [ ] T041 [US4] Em `RotacionarAsync` de `src/PedidoFulfillment.Infrastructure/Autenticacao/ArmazenamentoSessoes.cs`, ler a situação do dono junto com a sessão e, desativada, sair sem confirmar a transação, com o mesmo 401
- [ ] T042 [US4] Em `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, fazer `RedefinirSenhaAsync` e `ConfirmarEmailAsync` conferirem a situação depois de consumir o token e saírem sem confirmar a transação, e `TrocarSenhaAsync` responder `SenhaAtualIncorreta` sem verificar a senha nem contar erro
- [ ] T043 [US4] Em `src/PedidoFulfillment.Infrastructure/Emails/ProcessadorEmails.cs`, descartar a solicitação de conta desativada antes de gerar o token, para todos os tipos
- [ ] T044 [US4] Em `src/PedidoFulfillment.Infrastructure/Notificacoes/ProcessadorNotificacoes.cs`, aplicar a regra de R-06 em `DistribuirAsync` e em `EnviarAsync`, em um método único e testável: destino com dono não recebe se o dono está desativado ou se o aviso é anterior a `ReativadoEm`; destinos globais, `Ativo` e o contador de falhas não mudam
- [ ] T045 [P] [US4] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosDesativacaoTestes.cs`: sucesso, 401 e 403 nas duas rotas; 409 `USUARIO_ULTIMO_ADMIN` e `USUARIO_PROPRIA_CONTA`; idempotência sem log; 404 e 400; desativar encerra as sessões; reativar devolve o login com a mesma senha e os mesmos perfis, e as sessões antigas continuam encerradas; pedidos iguais depois de desativar e reativar (SC-005), e um pedido em andamento segue o fluxo
- [ ] T046 [P] [US4] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/ContaDesativadaTestes.cs`: login com senha certa e errada dá o mesmo 401, sem contar erro; renovação dá o mesmo 401; recuperação e reenvio dão 202 sem e-mail; token de recuperação, confirmação ou convite anterior dá `TOKEN_INVALIDO`, e a conta segue desativada; troca de senha com token válido dá 400 de senha atual; registro com o mesmo e-mail dá 409; redefinição não reativa, e reativação não mexe no bloqueio (FR-019); o token de acesso segue valendo nas rotas de pedido até expirar; e a conta da carga inicial continua desativada depois de rodar a preparação de novo
- [ ] T047 [P] [US4] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/ContaDesativadaTempoTestes.cs` no molde de `Autenticacao/TempoRecuperacaoTestes.cs`: em 100 tentativas de cada caso, status e corpo idênticos aos de senha errada, e medianas a menos de 50 ms (SC-004), inclusive para a conta sem senha. A base de "senha errada" não pode bloquear, porque a conta bloqueada não calcula o hash: use contas diferentes ou zere a contagem de erros fora da medição. Configure limites altos no teste, porque o padrão do login é 10 por minuto
- [ ] T048 [P] [US4] Escrever `tests/PedidoFulfillment.Testes.Integracao/Usuarios/ContaDesativadaWebhooksTestes.cs`, com `ReceptorWebhookTeste`: nada chega durante a desativação; entrega pendente ao desativar é descartada; aviso atrasado no Worker não sai depois de reativar; aviso novo depois de reativar chega, com o mesmo segredo; destino global continua recebendo; uma falha antiga não desativa o destino
- [ ] T049 [US4] Ampliar `tests/PedidoFulfillment.Testes.Integracao/Usuarios/GestaoUsuariosConcorrenciaTestes.cs`: em 50 rodadas, desativações simultâneas cruzadas e combinadas com retiradas de perfil sempre deixam um Admin ativo (SC-003); e uma desativação simultânea a um login não deixa sessão que volte a valer depois de reativar

**Checkpoint**: todas as histórias funcionam, e os critérios de aceite da issue estão cobertos.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T050 [P] Conferir o contrato: `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs` verde com as 7 rotas, os schemas e os 4 códigos, a política documentada e o comparador sem mudança incompatível (FR-022, SC-008)
- [ ] T051 [P] Conferir com `CapturaLogs` e `ColetorTelemetria`, em `tests/PedidoFulfillment.Testes.Integracao/Usuarios/AuditoriaGestaoUsuariosTestes.cs`, que cada alteração efetiva gera exatamente um log, com id de quem fez e da conta, e que nenhum traz nome, e-mail ou endereço de origem (FR-021, SC-006, SC-007)
- [ ] T052 [P] Atualizar `docs/fluxos/01-autenticacao.md`: tabela de perfis, seção nova da gestão, convite, conta desativada em login, renovação e recuperação, erros com as mensagens do contrato e logs 3019 a 3025. Nas limitações, retirar "Não há rota de gestão de usuários ou de perfis", citando a #54 (FR-025), e acrescentar: um Admin removido ainda usa o reenvio da DLQ e o catálogo até o token expirar. Conferir mensagens, códigos e IDs no código e os links relativos
- [ ] T053 [P] Atualizar `docs/fluxos/13-notificacao-de-status.md` (destinos de conta desativada) e `docs/fluxos/16-envio-de-email.md` (tipo `ConviteUsuario`, validade, descarte de conta desativada e convite descartado depois de 24 horas) e `specs/016-observabilidade-opentelemetry/contracts/telemetria.md` (valor `ConviteUsuario` de `oms.email.tipo`). No fluxo 13, registrar também que a reconexão com `Last-Event-ID` pode trazer avisos do período desativado. Conferir se o `docs/fluxos/README.md` precisa de ajuste
- [ ] T054 [P] Atualizar `docs/proximos-passos.md`: acrescentar o REQ-25 ao resumo, numeração a partir do REQ-26 e remover a #54 da fila. Corrigir a linha 60 do `README.md`, sem fazê-lo crescer: numeração a partir do REQ-26 e fila sem a #54 e sem a #75, já concluída. **Não acrescentar nada além disso ao `README.md`**: se algo parecer necessário, perguntar antes
- [ ] T055 Medir a busca do [quickstart](./quickstart.md#4-medição-da-busca) com 100 mil contas e registrar o p95 em `specs/025-gestao-usuarios/research.md`. **Se passar de 500 ms, avisar o usuário antes de abrir o PR**, com o número medido
- [ ] T056 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`, comparando o total de testes com a T001
- [ ] T057 Executar o [quickstart](./quickstart.md) inteiro com o compose e registrar em `specs/025-gestao-usuarios/research.md` qualquer divergência; se um critério não atender, avisar o usuário antes de abrir o PR. No fim, comparar o Docker com o retrato da T001, sem deixar artefatos dos testes
- [ ] T058 Abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #54`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, a consulta ao banco por chamada nas rotas de gestão, a limitação do ex-Admin nas rotas da DLQ e do catálogo, a mudança em `CodificadorCursorProdutos` e a redefinição aceitando o token de convite; depois, acompanhar os workflows, inclusive o `build-testes` e o Quality Gate, e usar `gh pr merge --rebase --auto`, sem apagar a branch. Marcar esta tarefa em `specs/025-gestao-usuarios/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende do Setup e bloqueia todas as histórias.
- **Histórias (Phases 3 a 6)**: dependem da fundação.
- **Polish (Phase 7)**: depende de todas as histórias.

### User Story Dependencies

- **US1 (consulta)**: só a fundação.
- **US2 (criação)**: só a fundação.
- **US3 (perfis)**: só a fundação; os testes criam Admins pela carga inicial ou por `CenarioIdentidade`.
- **US4 (desativação)**: só a fundação; o caso do convite anterior à desativação na T046 usa a criação da US2, então a T046 vem depois da T023 a T028.

### Dentro de cada história

- A T016 vem antes da T017.
- A T027 vem antes da T028.
- A T033 vem antes da T034; a T038 vem antes da T039.
- A T049 amplia o arquivo da T037.

### Parallel Opportunities

```text
Fundação:  T004, T005, T006, T007, depois T014
US1:       T015, T016, T021
US2:       T025, T030, T031, T032
US3:       T036
US4:       T045, T046, T047, T048
Polish:    T050, T051, T052, T053, T054
```

---

## Implementation Strategy

### MVP First

1. Setup e fundação.
2. US1, depois US2, depois US3. Validar cada uma pelo teste independente.
3. **Parar e conferir**: com o MVP, o Admin já cria usuários e gerencia perfis.

### Entrega

O PR só sai com a US4, porque "um usuário desativado não consegue fazer login" é critério de aceite da issue.

## Notes

- `[P]` = arquivos diferentes, sem dependência de tarefa incompleta.
- Commit ao fim de cada fase, com os testes da fase verdes.
- Uma falha não relacionada ao REQ não é ignorada: informe o usuário.
- Toda alteração em `README.md` que o faça crescer exige permissão.
