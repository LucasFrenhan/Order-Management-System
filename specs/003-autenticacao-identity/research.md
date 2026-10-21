# Research: Autenticação e autorização de usuários (REQ-03)

**Data**: 2026-10-21 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Divisão entre camadas

- **Decisão**:
  - **Application**: `ServicoAutenticacao` implementa `IServicoAutenticacao` e concentra as
    regras do requisito: aparo do nome, tradução para 409 e para o 401 genérico, registro
    dos eventos de segurança com e-mail mascarado (`MascaraEmail`). Depende de duas
    abstrações novas:
    - `IGestorIdentidade`: cria a conta com o perfil Cliente e verifica credenciais,
      devolvendo `ResultadoCredenciais` (`Valida`, `Invalida`, `Bloqueada`,
      `BloqueadaAgora`).
    - `IEmissorToken`: emite o token para um usuário e seus perfis.
  - **Infrastructure**: `GestorIdentidade` implementa `IGestorIdentidade` com o
    `UserManager` do Identity sobre o EF Core; também guarda o `DbContext`, as migrations e
    a preparação da inicialização.
  - **API**: `EmissorTokenJwt` implementa `IEmissorToken` ao lado da validação que já
    existe em `Seguranca/ConfiguracaoJwt.cs`, usando as mesmas `OpcoesJwt`, os mesmos
    nomes de claim (`sub`, `role`) e o algoritmo HS256. Emissão e validação ficam no mesmo
    lugar e não podem divergir.
- **Justificativa**: a constituição põe regras em serviços de aplicação, e o Princípio V
  exige testes unitários para elas. Com as abstrações, as regras são testadas sem banco. O
  Identity é detalhe de infraestrutura. Emitir na API evita um pacote novo de JWT na
  Infrastructure, já que a API tem o `JwtBearer` (e com ele o `JsonWebTokenHandler`).
- **Alternativas**: serviço inteiro na Infrastructure, usando o `UserManager` direto.
  Rejeitada, porque as regras do requisito só seriam testáveis com banco.

## R-02. Identity sem cookies

- **Decisão**: `AddIdentityCore<Usuario>().AddRoles<Perfil>().AddEntityFrameworkStores<ContextoBancoDados>()`.
  Não usar `AddIdentity`, que registra autenticação por cookie e trocaria o esquema
  padrão, hoje JWT Bearer (REQ-01). Não há `SignInManager`: o login usa o `UserManager`
  (`FindByEmailAsync`, `IsLockedOutAsync`, `CheckPasswordAsync`, `AccessFailedAsync`,
  `ResetAccessFailedCountAsync`).
- **Opções do Identity**:
  - `User.RequireUniqueEmail = true`; `UserName` recebe o próprio e-mail, e
    `User.AllowedUserNameCharacters = ""` libera qualquer caractere que o contrato aceite
    no e-mail.
  - `Lockout`: `MaxFailedAccessAttempts = 5`, `DefaultLockoutTimeSpan = 15 min`,
    `AllowedForNewUsers = true` (FR-009).
  - `Password`: só `RequiredLength = 8`; `RequireDigit`, `RequireLowercase`,
    `RequireUppercase` e `RequireNonAlphanumeric` desligados (ver R-04).
- **Alternativas**: `SignInManager.CheckPasswordSignInAsync`. Rejeitada, porque depende
  de `HttpContext` e do esquema de cookie.

## R-03. Unicidade de e-mail e registro concorrente

- **Decisão**:
  - O e-mail vira `UserName` e `Email`. O Identity normaliza os dois em maiúsculas
    (`NormalizedUserName`, `NormalizedEmail`), então a comparação não diferencia
    maiúsculas de minúsculas (FR-003).
  - O índice `UserNameIndex` já é único; o `EmailIndex` passa a ser único no mapeamento.
    Dois registros simultâneos passam pela checagem do Identity, mas o segundo esbarra no
    índice; o `GestorIdentidade` captura a violação de chave única (`SqlException` 2601 ou
    2627 dentro da `DbUpdateException`) e devolve conflito.
  - Criação da conta e atribuição do perfil Cliente rodam numa transação: não existe conta
    sem perfil.
  - `Email` guarda o valor como informado (FR-004).
- **Alternativas**: trava de aplicação (lock) por e-mail. Rejeitada, porque não funciona
  com várias instâncias.

## R-04. Regras de senha

- **Decisão**: a regra de senha tem uma única fonte, a validação de
  `RegistrarUsuarioRequestDTO` do REQ-01 (8 a 128 caracteres; `char.IsUpper`,
  `char.IsLower`, `char.IsDigit`). O Identity só exige o tamanho mínimo.
- **Justificativa**: o `PasswordValidator` do Identity considera só `A–Z` e `a–z` como
  letras. Uma senha como `ÇÃOabc12`, aceita pelo contrato, seria recusada por ele,
  violando o FR-005. Desligar as regras de caractere do Identity mantém exatamente o
  contrato.
- **Usuários iniciais**: a senha configurada é validada com a mesma regra, construindo um
  `RegistrarUsuarioRequestDTO` e chamando `Validator.TryValidateObject` (R-08).

## R-05. Login, bloqueio e resposta genérica

- **Decisão**: fluxo do `GestorIdentidade.VerificarCredenciaisAsync`:
  1. `FindByEmailAsync`. Sem conta: verifica a senha contra um hash fixo, para gastar o
     mesmo tempo, e devolve `Invalida`.
  2. `IsLockedOutAsync` verdadeiro: devolve `Bloqueada`, sem verificar a senha.
  3. `CheckPasswordAsync` falso: `AccessFailedAsync`; se a conta acabou de ser bloqueada,
     devolve `BloqueadaAgora`; senão, `Invalida`.
  4. Senha correta: `ResetAccessFailedCountAsync` e `Valida`, com id, nome e perfis.
  - O `ServicoAutenticacao` responde o mesmo 401 (título "Credenciais inválidas.") para
    `Invalida`, `Bloqueada` e `BloqueadaAgora` (FR-008, FR-009).
- **Justificativa**: o hash fixo evita que o tempo de resposta revele se o e-mail existe,
  complementando o FR-008. Custa uma verificação de hash, dentro do SC-005.

## R-06. Token de acesso

- **Decisão**: `EmissorTokenJwt` usa `JsonWebTokenHandler` com `SecurityTokenDescriptor`:
  - claims `sub` (id do usuário), `jti` (Guid novo) e um `role` por perfil;
  - `iss` e `aud` de `OpcoesJwt`; `IssuedAt` e `Expires` = agora + `DuracaoTokenMinutos`
    (60 por padrão), com o relógio vindo de `TimeProvider`;
  - assinatura HMAC-SHA256 com `Jwt:ChaveAssinatura`.
  - `LoginResponseDTO.ExpiraEm` usa o mesmo instante de `Expires`.
- **Justificativa**: reaproveita a configuração e os nomes de claim já usados pela
  validação (`ConfiguracaoJwt.ClaimUsuario` e `ClaimPerfil`). O `TimeProvider` permite
  testar `expiraEm` sem pacote novo, com uma subclasse simples nos testes.
- **Alternativas**: `JwtSecurityTokenHandler` (System.IdentityModel.Tokens.Jwt). Rejeitada:
  é a API legada e exigiria um pacote a mais.

## R-07. Modelo de dados e migrations

- **Decisão**:
  - `ContextoBancoDados : IdentityDbContext<Usuario, Perfil, Guid>` na Infrastructure
    (`Persistencia/`). O REQ-04 acrescenta os `DbSet` de negócio nele.
  - `Usuario : IdentityUser<Guid>` com `Nome` (100); `Perfil : IdentityRole<Guid>`.
    Ids gerados com `Guid.CreateVersion7()`.
  - Tabelas renomeadas para português: `Usuarios`, `Perfis`, `UsuariosPerfis`,
    `UsuariosClaims`, `PerfisClaims`, `UsuariosLogins`, `UsuariosTokens`.
  - Primeira migration `InicialIdentidade`, em `Persistencia/Migracoes`. Projeto de
    inicialização para a ferramenta: a API.
  - Ferramenta `dotnet-ef` 10.0.13 como ferramenta local (`.config/dotnet-tools.json`),
    para que o comando de migration fora de Development seja reproduzível:
    `dotnet tool restore` e
    `dotnet ef database update --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API`.
- **Alternativas**: `EnsureCreated`. Rejeitada: não convive com migrations, e o REQ-04
  precisa delas.

## R-08. Preparação da inicialização

- **Decisão**: um `IHostedService` na Infrastructure, `PreparacaoBancoDados`, registrado por
  `AdicionarInfraestrutura`. Os hosted services iniciam antes do servidor web e depois da
  validação de opções (`ValidateOnStart`); uma exceção no `StartAsync` impede a API de
  subir. Passos:
  1. Se `Banco:PrepararNaInicializacao` for `false`, não faz nada (FR-018). Padrão: `true`.
  2. Em Development ou `Testes`: `Database.MigrateAsync()`, que cria o banco se preciso.
     Nos demais: `GetPendingMigrationsAsync()`; se houver alguma, falha com "O banco de
     dados tem migrations pendentes. Aplique-as com o comando documentado antes de iniciar
     a API." (FR-017).
  3. Cria os perfis que faltam, um por valor de `PerfilUsuario`. Se outra instância criar o
     mesmo perfil ao mesmo tempo, a violação do índice único é ignorada e o perfil é lido de
     novo (FR-011).
  4. Cria os usuários iniciais configurados (R-09).
  - Falha de conexão (`SqlException` ou tempo esgotado) em qualquer passo vira "Não foi
    possível acessar o banco de dados (ConnectionStrings:SqlServer) para preparar a
    inicialização.", sem a exceção interna, que pode conter o servidor (FR-018).
- **Alternativas**: chamar a preparação no `Program.cs` antes de `app.Run()`. Rejeitada:
  rodaria antes da validação de opções, e a falta da string de conexão daria a mensagem
  errada.

## R-09. Usuários iniciais

- **Decisão**: seção `UsuariosIniciais`, uma lista de `{ Nome, Email, Senha, Perfil }`.
  - `OpcoesUsuariosIniciais` com `ValidadorOpcoesUsuariosIniciais` (`IValidateOptions` e
    `ValidateOnStart`, como no REQ-02): campos obrigatórios, `Perfil` só `Admin` ou
    `OperadorLogistica` (Cliente vem do registro público), e nome, e-mail e senha validados
    pelas regras do `RegistrarUsuarioRequestDTO`. As mensagens citam a chave
    (`UsuariosIniciais:0:Senha`), nunca o valor.
  - Usuário cujo e-mail já existe: não é alterado (FR-012), mesmo que o perfil
    configurado seja outro; só um aviso no log com o e-mail mascarado.
  - Em `appsettings.Development.json`, um Admin e um OperadorLogistica de desenvolvimento,
    sob o comentário `SOMENTE DESENVOLVIMENTO`. Em outros ambientes, a lista vem de
    variáveis de ambiente (`UsuariosIniciais__0__Email`...) ou de cofre de segredos.
- **Justificativa**: o REQ-05 precisa de Admin e OperadorLogistica para exercitar a troca
  de status, sem passo manual.

## R-10. Eventos de segurança em log

- **Decisão**: o `ServicoAutenticacao` registra com `LoggerMessage` (gerador de código), na
  categoria `PedidoFulfillment.Application.Autenticacao.ServicoAutenticacao`:

  | Evento | Id | Nível | Campos |
  |--------|----|-------|--------|
  | `UsuarioRegistrado` | 3001 | Information | `UsuarioId`, `EmailMascarado` |
  | `LoginRealizado` | 3002 | Information | `UsuarioId` |
  | `LoginRecusado` | 3003 | Warning | `EmailMascarado`, `UsuarioId` (quando a conta existe), `Motivo` (`CredenciaisInvalidas` ou `ContaBloqueada`) |
  | `ContaBloqueada` | 3004 | Warning | `UsuarioId`, `EmailMascarado` |

  - `MascaraEmail`: primeira letra da parte local + `***@` + domínio
    (`maria@exemplo.com.br` → `m***@exemplo.com.br`); sem `@` válido, `***`.
  - O motivo real só vai para o log; a resposta continua genérica.
- **Pacote**: `Microsoft.Extensions.Logging.Abstractions` 10.0.13 no Application, que hoje
  não tem nenhuma dependência.
- **Verificação (SC-003)**: os testes de integração capturam os logs com um
  `ILoggerProvider` de teste e verificam que senha, token e e-mail completo não aparecem.

## R-11. Testes

- **Decisão**:
  - **Unitários** (sem banco): `ServicoAutenticacao` com fakes de `IGestorIdentidade`,
    `IEmissorToken` e logger (aparo, 409, 401 genérico nos três casos, eventos e máscara);
    `MascaraEmail`; `ValidadorOpcoesUsuariosIniciais`.
  - **Integração sem Docker**: `EmissorTokenJwt` resolvido da `FabricaApi` (claims,
    `exp` = emissão + 60 min com `TimeProvider` de teste, token aceito por uma rota
    protegida); falha de inicialização com o banco inacessível (porta fechada) e mensagem
    sem dados de conexão; preparação desligada sobe como no REQ-02.
  - **Integração com banco real** (Testcontainers MsSql, ignorados sem Docker, como no
    REQ-02): registro e login de ponta a ponta, e-mail duplicado com outra caixa,
    registros simultâneos, bloqueio após 5 erros e desbloqueio (o teste ajusta o fim do
    bloqueio para o passado com o `UserManager`, em vez de esperar 15 minutos), perfis
    idempotentes, usuários iniciais, persistência após reinício, migration pendente fora
    de Development e a tabela de acesso 401/403 com tokens emitidos pelo login (SC-002).
  - Um contêiner por classe de teste (fixture), com e-mails únicos por teste.
- **`FabricaApi`**: passa `Banco:PrepararNaInicializacao=false` por padrão, para que os
  testes atuais continuem sem banco; os testes com Testcontainers ligam a preparação e
  apontam para o contêiner.

## R-12. Remoção do provisório

- **Decisão**: `IServicoAutenticacao` passa a ser o `ServicoAutenticacao` (escopo por
  requisição, porque usa o `UserManager`). O `ServicoNaoImplementado` deixa de implementar
  `IServicoAutenticacao` e continua só para `IServicoPedidos` até o REQ-05 (FR-016).
