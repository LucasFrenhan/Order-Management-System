---

description: "Lista de tarefas do REQ-03 — Autenticação e autorização de usuários"
---

# Tasks: Autenticação e autorização de usuários

**Input**: Design documents from `/specs/003-autenticacao-identity/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/token-acesso.md](./contracts/token-acesso.md),
[contracts/configuracao.md](./contracts/configuracao.md), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição (Princípio V) exige testes unitários para as regras
e testes de integração com sucesso, 401 e 403 para as rotas protegidas; a spec pede a
verificação com tokens emitidos pelo login (FR-013, SC-002) e a ausência de dados sensíveis
nos logs (SC-003). Os testes de cada história são escritos antes da implementação e devem
falhar primeiro.

**Organization**: por história de usuário. O modelo do Identity, a migration, a preparação
da inicialização e as abstrações do Application são fundacionais: o registro (US1) já
precisa do banco e do perfil Cliente.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Raiz da solução: `PedidoFulfillment.sln`; código em `src/`, testes em `tests/`.
- Nomes de classes, arquivos, namespaces, tabelas e mensagens em português (constituição).
- `TreatWarningsAsErrors` está ligado: nenhuma tarefa pode deixar aviso. `dotnet format
  --verify-no-changes` deve continuar passando.
- Pacotes só com versão central em `Directory.Packages.props`; nenhum pacote além dos
  justificados no plan.md.
- Contrato v1 intocado (FR-015): nenhuma rota, DTO, status ou código de erro novo.
- Respostas, mensagens de inicialização e logs **nunca** mostram senha, hash, token, e-mail
  completo nem dados de conexão (FR-002, FR-018, FR-019, SC-003).
- Endpoints só roteiam: `EndpointsAutenticacao.cs` não muda.
- Testes com banco real usam a fixture de Testcontainers da T012 e são ignorados com
  "Docker indisponível" sem Docker; o resto da suíte roda sem Docker, com a preparação da
  inicialização desligada na `FabricaApi`.
- Commits: `tipo(escopo): descrição [REQ-03]`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: pacotes, ferramenta de migrations e referências.

- [X] T001 Em `Directory.Packages.props`: no `ItemGroup Label="Infrastructure"` acrescentar `Microsoft.AspNetCore.Identity.EntityFrameworkCore` e `Microsoft.EntityFrameworkCore.SqlServer`, ambos `10.0.13`; no `ItemGroup Label="API"` acrescentar `Microsoft.EntityFrameworkCore.Design` `10.0.13`; criar `ItemGroup Label="Application"` com `Microsoft.Extensions.Logging.Abstractions` `10.0.13`; atualizar o comentário do topo para citar também `specs/003-autenticacao-identity/plan.md`
- [X] T002 [P] Criar `.config/dotnet-tools.json` com a ferramenta local `dotnet-ef` `10.0.13` (`dotnet new tool-manifest` + `dotnet tool install dotnet-ef --version 10.0.13`) e rodar `dotnet tool restore`
- [X] T003 Acrescentar `PackageReference` sem versão: `Microsoft.AspNetCore.Identity.EntityFrameworkCore` e `Microsoft.EntityFrameworkCore.SqlServer` em `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`; `Microsoft.EntityFrameworkCore.Design` com `PrivateAssets="all"` em `src/PedidoFulfillment.API/PedidoFulfillment.API.csproj`; `Microsoft.Extensions.Logging.Abstractions` em `src/PedidoFulfillment.Application/PedidoFulfillment.Application.csproj`
- [X] T004 Rodar `dotnet build PedidoFulfillment.sln` e garantir zero erros e zero avisos (inclusive NU1902/NU1903 de pacotes transitivos; se aparecer algum, fixar a versão corrigida como no REQ-02 e justificar no plan.md)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: modelo do Identity, primeira migration, preparação da inicialização (migrations, perfis, falhas de banco) e abstrações do Application.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T005 [P] Testes de `MascaraEmail` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/MascaraEmailTestes.cs`: `maria@exemplo.com.br` → `m***@exemplo.com.br`; parte local de 1 caractere → `m***@...`; texto sem `@`, com `@` no início ou no fim → `***`; resultado nunca contém a parte local completa
- [X] T006 [P] Testes de preparação sem Docker em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/PreparacaoInicializacaoTestes.cs`: com `Banco:PrepararNaInicializacao=true` e a string de conexão de porta fechada da `FabricaApi`, a API não inicia e a exceção diz "Não foi possível acessar o banco de dados (ConnectionStrings:SqlServer) para preparar a inicialização." sem conter `Password=`, `127.0.0.1` nem `senha-de-teste` em nenhuma exceção da cadeia (FR-018); com a preparação desligada (padrão da `FabricaApi`), a API inicia e `/health` responde 200
- [X] T007 [P] Testes da preparação com banco real em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/PreparacaoComBancoTestes.cs` (fixture da T012): no ambiente `Testes` com banco vazio, a API cria o banco, aplica `InicialIdentidade` e cria os perfis `Cliente`, `Admin` e `OperadorLogistica` (FR-011, FR-017); reiniciar (nova `FabricaApi` no mesmo banco) não duplica perfis; com a migration já aplicada e a tabela `Perfis` vazia, duas `FabricaApi` iniciando ao mesmo tempo deixam exatamente 3 perfis (a migration é aplicada antes, para o teste disputar só a criação dos perfis, não a do banco — `/speckit-analyze` M1); no ambiente `Production` com um banco sem migrations, a API não inicia e a mensagem fala em migrations pendentes, sem alterar o esquema (a tabela `__EFMigrationsHistory` continua inexistente)

### Implementação da fundação

- [X] T008 [P] Criar as abstrações em `src/PedidoFulfillment.Application/Autenticacao/`: `IGestorIdentidade.cs` (`CriarAsync(nome, email, senha, ct)` → `Resultado<UsuarioResponseDTO>` com conflito `USUARIO_EMAIL_JA_CADASTRADO`; `VerificarCredenciaisAsync(email, senha, ct)` → `ResultadoCredenciais`), `ResultadoCredenciais.cs` (situações `Valida` com id, nome e perfis, `Invalida` com id opcional, `Bloqueada` e `BloqueadaAgora` com id — data-model.md), `IEmissorToken.cs` (`Emitir(usuarioId, perfis)` → `LoginResponseDTO`) e `MascaraEmail.cs` (regra da R-10)
- [X] T009 [P] Criar `src/PedidoFulfillment.Infrastructure/Identidade/Usuario.cs` (`IdentityUser<Guid>` + `Nome`) e `src/PedidoFulfillment.Infrastructure/Identidade/Perfil.cs` (`IdentityRole<Guid>`), com construtores que geram `Id` por `Guid.CreateVersion7()`
- [X] T010 Criar `src/PedidoFulfillment.Infrastructure/Persistencia/ContextoBancoDados.cs` (`IdentityDbContext<Usuario, Perfil, Guid>`): tabelas `Usuarios`, `Perfis`, `UsuariosPerfis`, `UsuariosClaims`, `PerfisClaims`, `UsuariosLogins`, `UsuariosTokens`; `Nome` obrigatório com 100 caracteres; índice `EmailIndex` em `NormalizedEmail` **único** (R-03); comentário de que o REQ-04 acrescenta os `DbSet` de negócio aqui
- [X] T011 Criar `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesBanco.cs` (seção `Banco`, `PrepararNaInicializacao` padrão `true`) e, em `ExtensoesInfraestrutura.AdicionarInfraestrutura`, registrar `AddDbContext<ContextoBancoDados>` com `UseSqlServer` a partir de `ConnectionStrings:SqlServer`, `AddIdentityCore<Usuario>` com `AddRoles<Perfil>()` e `AddEntityFrameworkStores<ContextoBancoDados>()` e as opções da R-02 (e-mail único, `AllowedUserNameCharacters = ""`, só `RequiredLength = 8` na senha, bloqueio 5 erros / 15 min / `AllowedForNewUsers`), as `OpcoesBanco` e o hosted service da T014
- [X] T012 [P] Criar a fixture `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/BancoDadosTeste.cs` (`IAsyncLifetime`, Testcontainers.MsSql com a mesma imagem do `docker-compose.yml`, `Assert.SkipUnless("Docker indisponível")` quando o Docker não responde, como em `SaudeComDependenciasTestes`) com um método que devolve uma string de conexão para um banco novo de nome único por teste; e em `FabricaApi.cs`: `PrepararBanco` (padrão `false`) mapeado para `Banco:PrepararNaInicializacao`, além de uma propriedade `StringConexao` que sobrescreve a de porta fechada
- [X] T013 Gerar a migration `InicialIdentidade` em `src/PedidoFulfillment.Infrastructure/Persistencia/Migracoes/` com `dotnet ef migrations add InicialIdentidade --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`; conferir no arquivo gerado os nomes das tabelas em português e o índice único `EmailIndex`; excluir `Persistencia/Migracoes` do `dotnet format` se o código gerado for acusado (via `.editorconfig` `generated_code = true`)
- [X] T014 Criar `src/PedidoFulfillment.Infrastructure/Persistencia/PreparacaoBancoDados.cs` (`IHostedService`, R-08): desligado → nada; Development ou `Testes` → `Database.MigrateAsync()`; outros ambientes → `GetPendingMigrationsAsync()` e falha "O banco de dados tem migrations pendentes. Aplique-as com o comando documentado antes de iniciar a API." se houver; cria os perfis faltantes de `PerfilUsuario` com o `RoleManager`, ignorando a violação de índice único de outra instância e relendo o perfil; `SqlException`, `TimeoutException` e falhas de conexão do EF viram `InvalidOperationException` com a mensagem do FR-018, **sem** `innerException`
- [X] T015 Rodar `dotnet test PedidoFulfillment.sln`: T005–T007 verdes e toda a suíte do REQ-01/REQ-02 continua verde (preparação desligada por padrão)

**Checkpoint**: banco criado pela migration, perfis garantidos, falhas de inicialização corretas.

---

## Phase 3: User Story 1 - Registrar-se como cliente (Priority: P1) 🎯 MVP

**Goal**: o registro cria a conta com o perfil Cliente, guardada no banco, com 409 para e-mail duplicado.

**Independent Test**: registrar com dados válidos (201, perfil Cliente) e repetir o e-mail com outra caixa (409).

### Tests for User Story 1 ⚠️

- [X] T016 [P] [US1] Criar `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/Falsos.cs` (fakes de `IGestorIdentidade`, `IEmissorToken` e um `ILogger<ServicoAutenticacao>` que guarda eventos com id, nível e propriedades) e os testes de registro em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoAutenticacaoTestes.cs`: nome e e-mail chegam ao gestor sem espaços nas pontas, e o e-mail mantém as maiúsculas e minúsculas informadas (FR-004); sucesso registra `UsuarioRegistrado` (3001) com `UsuarioId` e `EmailMascarado`; conflito do gestor passa adiante com `USUARIO_EMAIL_JA_CADASTRADO` sem registrar evento de sucesso; nenhum evento contém a senha nem o e-mail completo
- [X] T017 [P] [US1] Criar `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/CapturaLogs.cs` (`ILoggerProvider` de teste que acumula as mensagens formatadas e as propriedades) e plugá-lo na `FabricaApi` por uma propriedade opcional
- [X] T018 [US1] Testes de registro com banco real em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/RegistroComBancoTestes.cs` (fixture da T012, preparação ligada): 201 com `id`, `nome` aparado, `email` como informado e `perfis` `["Cliente"]`, sem `senha` no corpo; o mesmo e-mail com outra caixa → 409 `USUARIO_EMAIL_JA_CADASTRADO` e continua existindo uma só conta; o mesmo e-mail com espaços nas pontas (`" maria@exemplo.com.br "`) → 409 (H1); 10 registros simultâneos com o mesmo e-mail → exatamente um 201 e nove 409; corpo inválido → 400 sem conta criada; a senha fica só como hash na tabela `Usuarios` (`PasswordHash` diferente da senha e não vazio); nova `FabricaApi` no mesmo banco encontra a conta (cenário 5); nenhum log capturado contém a senha nem o e-mail completo
- [X] T019 [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/EndpointsAutenticacaoTestes.cs`, substituir `Rotas_publicas_sem_token_chegam_ao_servico_provisorio` (que espera 501) por um teste que, com o serviço falso de `ServicosFalsos.cs`, prova que register e login sem token chegam ao serviço e devolvem a resposta dele (201 e 200); e acrescentar um teste com o serviço real e a string de conexão de porta fechada (preparação desligada): registro válido → 500 em ProblemDetails genérico, sem `Password=`, `127.0.0.1` nem mensagem de exceção no corpo (caso de borda "banco fora do ar durante registro", M3)

### Implementation for User Story 1

- [X] T020 [US1] Implementar `GestorIdentidade.CriarAsync` em `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`: numa transação do `ContextoBancoDados`, `UserManager.CreateAsync(new Usuario { UserName = email, Email = email, Nome = nome }, senha)` e `AddToRoleAsync(Cliente)`; erros `DuplicateUserName`/`DuplicateEmail` e a violação de chave única (`SqlException` 2601/2627 dentro da `DbUpdateException`) viram `Falha(Conflito, ..., CodigosErro.UsuarioEmailJaCadastrado)`; qualquer outro erro do Identity vira exceção (500 genérico); devolve `UsuarioResponseDTO` com `perfis` `[Cliente]`; registrar como `scoped` em `AdicionarInfraestrutura`
- [X] T021 [US1] Criar `src/PedidoFulfillment.Application/Autenticacao/ServicoAutenticacao.cs` com `RegistrarAsync` (apara nome e e-mail, delega ao gestor, registra `UsuarioRegistrado` com `LoggerMessage`) e `AutenticarAsync` provisoriamente devolvendo `TipoFalha.NaoImplementado` até a T028; eventos e ids conforme R-10
- [X] T022 [US1] Em `src/PedidoFulfillment.API/Program.cs`, registrar `IServicoAutenticacao` como `ServicoAutenticacao` (`scoped`) no lugar do `ServicoNaoImplementado`; em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`, remover a implementação de `IServicoAutenticacao` e atualizar o comentário para "remover no REQ-05"
- [X] T023 [US1] Rodar os testes da US1 (T016–T019) e a suíte inteira; todos verdes

**Checkpoint**: registro real funcionando; login ainda 501.

---

## Phase 4: User Story 2 - Entrar e receber um token de acesso (Priority: P1)

**Goal**: login com token de 60 minutos, 401 genérico e bloqueio após 5 erros.

**Independent Test**: registrar, fazer login e usar o token numa rota protegida; comparar as respostas de senha errada e e-mail inexistente.

### Tests for User Story 2 ⚠️

- [X] T024 [P] [US2] Acrescentar a `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoAutenticacaoTestes.cs` os testes de login: o e-mail chega ao gestor sem espaços nas pontas; `Valida` → 200 com o retorno do emissor e evento `LoginRealizado` (3002); `Invalida` (com e sem id), `Bloqueada` e `BloqueadaAgora` → a **mesma** falha `NaoAutenticado` com título "Credenciais inválidas." e o mesmo detalhe; eventos `LoginRecusado` (3003) com `Motivo` `CredenciaisInvalidas` ou `ContaBloqueada` e `EmailMascarado`; `BloqueadaAgora` também registra `ContaBloqueada` (3004); nenhum evento contém senha nem e-mail completo
- [X] T025 [P] [US2] Testes do emissor sem Docker em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/EmissorTokenTestes.cs`: resolver `IEmissorToken` da `FabricaApi` com um `TimeProvider` de teste (subclasse com instante fixo, registrada via `ConfigureTestServices`); o token é HS256, tem `sub`, `jti`, `iss`, `aud`, um `role` por perfil e nenhum claim de nome ou e-mail (contracts/token-acesso.md); `exp` e `expiraEm` = instante + 60 min; `tipoToken` `Bearer`; um token emitido para `Cliente` é aceito por `POST /api/v1/pedidos` (chega ao 501 do serviço provisório) e um emitido para `Admin` recebe 403; um token emitido sem nenhum perfil recebe 403 em `POST /api/v1/pedidos`, `GET /api/v1/pedidos/{id}` e `PATCH /api/v1/pedidos/{id}/status` (caso de borda "conta sem perfil", L1)
- [X] T026 [US2] Testes de login com banco real em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/LoginComBancoTestes.cs` (fixture da T012): uma senha com espaços (`" Senha Forte1 "`) é aceita no registro e o login só funciona com ela idêntica, falhando sem os espaços (L2); registrar e fazer login com o e-mail em outra caixa e com espaços nas pontas → 200 com `perfis` `["Cliente"]` e `expiraEm` ~ agora + 60 min; senha errada e e-mail inexistente → 401 com corpo idêntico (status, `title`, `detail`); 5 senhas erradas seguidas → a 6ª tentativa, mesmo com a senha correta, recebe o mesmo 401; depois de mover `LockoutEnd` para o passado com o `UserManager`, o login correto funciona; 4 erros + 1 acerto zeram a contagem (mais 4 erros não bloqueiam); o token do login é aceito por `POST /api/v1/pedidos`; 20 logins corretos seguidos, depois de um de aquecimento, com 95% abaixo de 1 s (SC-005); logs com `LoginRealizado`, `LoginRecusado` e `ContaBloqueada` e sem senha, token ou e-mail completo

### Implementation for User Story 2

- [X] T027 [P] [US2] Criar `src/PedidoFulfillment.API/Seguranca/EmissorTokenJwt.cs` (`IEmissorToken`, R-06): `JsonWebTokenHandler` + `SecurityTokenDescriptor` com `OpcoesJwt`, claims `sub`/`jti`/`role` usando `ConfiguracaoJwt.ClaimUsuario` e `ClaimPerfil`, `TimeProvider` para `IssuedAt`/`Expires`, HS256; registrar como singleton em `Program.cs` junto de `TryAddSingleton(TimeProvider.System)`; atualizar o resumo de `OpcoesJwt` em `ConfiguracaoJwt.cs` (a emissão agora está no `EmissorTokenJwt`)
- [X] T028 [US2] Implementar `GestorIdentidade.VerificarCredenciaisAsync` em `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs` conforme R-05: sem conta → `VerifyHashedPassword` contra um hash fixo gerado uma vez e `Invalida`; `IsLockedOutAsync` → `Bloqueada`; senha errada → `AccessFailedAsync` e `BloqueadaAgora` se a conta ficou bloqueada, senão `Invalida` com id; senha certa → `ResetAccessFailedCountAsync` e `Valida` com id, nome e perfis (`GetRolesAsync` convertido para `PerfilUsuario`)
- [X] T029 [US2] Implementar `ServicoAutenticacao.AutenticarAsync` em `src/PedidoFulfillment.Application/Autenticacao/ServicoAutenticacao.cs`: apara o e-mail antes de chamar o gestor; `Valida` → `IEmissorToken.Emitir` e `LoginRealizado`; demais situações → falha única `NaoAutenticado` "Credenciais inválidas." e os eventos da R-10
- [X] T030 [US2] Rodar os testes da US2 (T024–T026) e a suíte inteira; todos verdes

**Checkpoint**: registro e login reais; tokens emitidos funcionam nas rotas protegidas.

---

## Phase 5: User Story 3 - Perfis de acesso aplicados de ponta a ponta (Priority: P2)

**Goal**: usuários iniciais Admin e OperadorLogistica por configuração e a tabela de acesso do contrato verificada com tokens do login.

**Independent Test**: com os usuários iniciais configurados, fazer login com cada perfil e conferir 401/403/sucesso em todas as rotas protegidas.

### Tests for User Story 3 ⚠️

- [X] T031 [P] [US3] Testes de `ValidadorOpcoesUsuariosIniciais` em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesUsuariosIniciaisTestes.cs`: lista vazia é válida; campo ausente gera "A configuração UsuariosIniciais:<i>:<Campo> é obrigatória."; `Perfil` `Cliente` ou desconhecido é recusado; senha fora da regra do contrato (sem maiúscula, curta) é recusada citando `UsuariosIniciais:<i>:Senha`; senha `ÇÃOabc12` é aceita (R-04); nenhuma mensagem contém o valor recebido
- [X] T032 [US3] Testes com banco real em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/UsuariosIniciaisComBancoTestes.cs` (fixture da T012): com um Admin e um OperadorLogistica configurados, ambos fazem login e recebem só o seu perfil; reiniciar não duplica e não altera senha nem perfis, mesmo com outra senha na configuração; e-mail configurado que já existe como Cliente continua só Cliente; sem configuração nenhum usuário inicial é criado; senha inválida na configuração impede a API de iniciar com mensagem sem o valor
- [X] T033 [US3] Tabela de acesso com tokens reais em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/AutorizacaoComTokensReaisTestes.cs` (fixture da T012, SC-002): tokens obtidos por login de um Cliente registrado, do Admin e do OperadorLogistica iniciais; para `POST /api/v1/pedidos`, `GET /api/v1/pedidos/{id}` e `PATCH /api/v1/pedidos/{id}/status`, conferir sem token → 401, perfil não autorizado → 403 e perfil autorizado → passa pela autorização (o 501 provisório do REQ-05), conforme a tabela de acesso do contrato v1 (na troca de status a autorização aceita os três perfis; o Cliente só pode cancelar o próprio pedido Pendente, regra do REQ-05)

### Implementation for User Story 3

- [X] T034 [US3] Criar `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesUsuariosIniciais.cs` (seção `UsuariosIniciais`, lista de `{ Nome, Email, Senha, Perfil }`) e `ValidadorOpcoesUsuariosIniciais` (`IValidateOptions`, validando nome, e-mail e senha com um `RegistrarUsuarioRequestDTO` e `Validator.TryValidateObject`, R-09); registrar com `ValidateOnStart` em `AdicionarInfraestrutura`
- [X] T035 [US3] Acrescentar a `PreparacaoBancoDados.cs` a criação dos usuários iniciais depois dos perfis: e-mail inexistente → criar com o perfil configurado; existente → não alterar e registrar aviso com o e-mail mascarado
- [X] T036 [US3] Acrescentar a `src/PedidoFulfillment.API/appsettings.Development.json`, sob o comentário `SOMENTE DESENVOLVIMENTO`, a seção `UsuariosIniciais` com `admin@oms.local`/`AdminDev2026` (Admin) e `operador@oms.local`/`OperadorDev2026` (OperadorLogistica), conforme contracts/configuracao.md
- [X] T037 [US3] Rodar os testes da US3 (T031–T033) e a suíte inteira; todos verdes, inclusive a conformidade do contrato v1 (FR-015)

**Checkpoint**: as três histórias funcionando e testadas.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: comentários obsoletos, documentação, segredos, formatação e validação final.

- [X] T038 [P] Atualizar comentários que citam o REQ-03 como futuro: `src/PedidoFulfillment.Application/Autenticacao/IServicoAutenticacao.cs` ("implementadas no REQ-03") e `src/PedidoFulfillment.Application/Contratos/Autenticacao/RegistrarUsuarioRequestDTO.cs` (aparo "do serviço (REQ-03)" → `ServicoAutenticacao`)
- [X] T039 Atualizar `README.md`: linha do REQ-03 em `## Estado atual` com link para a spec e "Registro, login com JWT, perfis e usuários iniciais"; o aviso de 501 passa a valer só para pedidos (REQ-05); seção sobre o banco: criação automática em Development, `dotnet tool restore` e o comando `dotnet ef database update` para outros ambientes, `Banco:PrepararNaInicializacao`; usuários de desenvolvimento; trocar a instrução de "gerar um JWT à mão" pelo login
- [X] T040 Varredura de segredos: `git grep -n -i -E "password|senha|pwd|chave"` nos arquivos versionados e confirmar que só aparecem valores de desenvolvimento (inclusive `AdminDev2026` e `OperadorDev2026` em `appsettings.Development.json`), valores de teste e nomes de chave
- [X] T041 Rodar `dotnet format PedidoFulfillment.sln --verify-no-changes` e corrigir o que acusar
- [X] T042 Executar o quickstart (quickstart.md §1 a §6) com o Docker no ar e registrar o resultado; em §6, confirmar que as mensagens não mostram a string de conexão

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende de Setup e bloqueia todas as histórias.
- **US1 (Phase 3)**: depende da Foundational.
- **US2 (Phase 4)**: depende da US1 para os testes de ponta a ponta (o login precisa de conta registrada) e porque as duas editam `ServicoAutenticacao.cs` e `GestorIdentidade.cs`.
- **US3 (Phase 5)**: depende da US2 (T033 usa tokens do login).
- **Polish (Phase 6)**: depois das histórias.

### Within Each Phase

- Testes antes da implementação, e falhando primeiro.
- Fundação: T008/T009 → T010 → T011 → T013 (migration precisa do contexto e do registro) → T014.
- `ServicoAutenticacao.cs` (T021, T029), `GestorIdentidade.cs` (T020, T028), `PreparacaoBancoDados.cs` (T014, T035), `ExtensoesInfraestrutura.cs` (T011, T020, T034) e `Program.cs` (T022, T027) são editados em sequência.

### Parallel Opportunities

- T002 em paralelo com T001/T003.
- T005, T006, T007, T008, T009 e T012 (arquivos diferentes).
- T016 e T017; T024 e T025; T027 em paralelo com T028.
- T031 em paralelo com T032/T033 (projetos diferentes).

---

## Parallel Example: Foundational

```text
Task: "Testes de MascaraEmail em tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/MascaraEmailTestes.cs"
Task: "Preparação sem Docker em tests/PedidoFulfillment.Testes.Integracao/Autenticacao/PreparacaoInicializacaoTestes.cs"
Task: "Abstrações em src/PedidoFulfillment.Application/Autenticacao/"
Task: "Usuario e Perfil em src/PedidoFulfillment.Infrastructure/Identidade/"
Task: "Fixture BancoDadosTeste em tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/BancoDadosTeste.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (Setup) e Phase 2 (Foundational).
2. Phase 3 (US1): registro real.
3. **PARAR e VALIDAR** com o quickstart §2 e as três primeiras linhas da §3.

### Incremental Delivery

1. Setup + Foundational → banco com Identity, perfis e preparação (commit `feat(persistencia): ... [REQ-03]`).
2. US1 → registro (commit `feat(autenticacao): implementa registro ... [REQ-03]`).
3. US2 → login, token e bloqueio (commit `feat(autenticacao): implementa login ... [REQ-03]`).
4. US3 → usuários iniciais e tabela de acesso com tokens reais (commit `feat(autorizacao): ... [REQ-03]`).
5. Polish → comentários, README, segredos, formatação, quickstart (commit `chore(autenticacao): ... [REQ-03]`).

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- O `ServicoNaoImplementado` continua só para pedidos; ele some no REQ-05.
- O teste de arquitetura do REQ-02 continua valendo: as novas referências (Application →
  `Logging.Abstractions`, Infrastructure → Identity/EF Core) são de pacote, não de projeto.
- `/speckit-analyze` (2026-10-21): H1 (aparo do e-mail), M1 (T007 sem disputa de criação do
  banco), M3 (T019, banco fora do ar no registro), L1 (T025, token sem perfil) e L2 (T026,
  senha com espaços) aplicados nas tarefas; M2 e L3 registrados na R-11 e no plano; L4 spec
  `Approved`; L5 mantido (numeração estável, como no REQ-02).
- Desvios registrados na implementação (2026-10-21):
  - T002: o .NET 10 cria o manifesto na raiz; ele foi movido para `.config/dotnet-tools.json`, como no plano.
  - T012: um único contêiner de SQL Server compartilhado por uma coleção do xUnit, em vez de um por classe (R-11 revisada).
  - T013: o snapshot do modelo foi gerado fora da pasta das migrations por causa de `--namespace` e foi movido (commit próprio); o código gerado já é ignorado pelo `dotnet format`, sem mudança no `.editorconfig`.
  - T027: o token também leva `nbf` (igual a `iat`); `contracts/token-acesso.md` atualizado.
  - T033: o PATCH de status aceita os três perfis na autorização (o Cliente pode cancelar o próprio pedido Pendente, REQ-01); o cenário 3 da US3 na spec e a §4 do quickstart, que previam 403 para o Cliente, foram corrigidos.
  - `ResultadoCredenciais.Valida` leva id e perfis, sem o nome (o token não usa nome); data-model atualizado.
- T042 (quickstart, 2026-10-21, Docker no ar): §1 201 testes verdes, nenhum ignorado; §2 banco
  criado com `InicialIdentidade`, perfis e usuários de desenvolvimento; §3 201 com nome aparado,
  409 com outra caixa e com espaços, login 200 com `expiraEm` = agora + 60 min (UTC), 401
  idêntico para senha errada e e-mail inexistente, 401 com a senha correta após 5 erros; §4
  Cliente passa em `POST /pedidos` (501) e Admin recebe 403, os dois passam no PATCH (501), sem
  token 401; §5 eventos `UsuarioRegistrado`, `LoginRealizado`, `LoginRecusado` e `ContaBloqueada`
  com `m***@exemplo.com.br`, sem senha, e-mail completo nem token; §6 Production recusa migration
  pendente, sobe depois do `dotnet ef database update`, não sobe com o banco parado e sobe com a
  preparação desligada, sem mostrar a string de conexão.
- Fazer commit ao fim de cada fase ou grupo lógico.
