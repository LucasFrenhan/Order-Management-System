---

description: "Lista de tarefas do REQ-02 — Estrutura da solução e ambiente local"
---

# Tasks: Estrutura da solução e ambiente local

**Input**: Design documents from `/specs/002-setup-infraestrutura/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/saude.md](./contracts/saude.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A spec exige teste de arquitetura (FR-003) e a constituição
(Princípio V) exige testes verificáveis; o plano lista testes de configuração, de saúde
(503 sem Docker, 200 com Testcontainers) e a varredura de autorização. Os testes de cada
história são escritos antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário. A criação do projeto Infrastructure e do ponto
de registro é fundacional, porque a US2 e a US3 dependem dela.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US3)

## Convenções válidas para todas as tarefas

- Raiz da solução: `PedidoFulfillment.sln` na raiz do repositório; código em `src/`, testes em `tests/`.
- Nomes de classes, arquivos, namespaces e mensagens em português (constituição).
- `Directory.Build.props` já impõe `TreatWarningsAsErrors`: nenhuma tarefa pode deixar aviso.
- Pacotes só com versão central em `Directory.Packages.props`; nenhum pacote além dos quatro
  justificados no plan.md. Health checks usam só o ASP.NET Core nativo (sem Xabaril).
- Mensagens de validação e respostas de saúde **nunca** mostram valores de configuração
  (strings de conexão, hosts, usuários, senhas) nem mensagens de exceção.
- Endpoints só roteiam: a lógica das verificações fica na Infrastructure; a API só mapeia e
  escreve a resposta.
- Os testes de integração usam `FabricaApi` no ambiente `Testes`; a suíte inteira roda sem
  Docker (SC-005).
- Commits: `tipo(escopo): descrição [REQ-02]`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: pacotes e o projeto novo da solução.

- [X] T001 Em `Directory.Packages.props`: acrescentar o `ItemGroup Label="Infrastructure"` com `Microsoft.Data.SqlClient` `7.1.1` e `RabbitMQ.Client` `7.2.2`; no `ItemGroup Label="Testes"` acrescentar `Testcontainers.MsSql` `4.15.0` e `Testcontainers.RabbitMq` `4.15.0` (não 4.16.0, ver plan.md); atualizar o comentário do topo para citar também `specs/002-setup-infraestrutura/plan.md`
- [X] T002 Criar o classlib `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj` (sem `TargetFramework` próprio, herdado de `Directory.Build.props`), com `<FrameworkReference Include="Microsoft.AspNetCore.App" />` (health checks e options nativos), `PackageReference` sem versão para `Microsoft.Data.SqlClient` e `RabbitMQ.Client`, e `ProjectReference` para `PedidoFulfillment.Application` e `PedidoFulfillment.Domain`; remover o `Class1.cs` do template; adicionar o projeto a `PedidoFulfillment.sln` na mesma pasta de solução dos outros projetos de `src`
- [X] T003 Acrescentar `ProjectReference` para `..\PedidoFulfillment.Infrastructure\PedidoFulfillment.Infrastructure.csproj` em `src/PedidoFulfillment.API/PedidoFulfillment.API.csproj` e para `..\..\src\PedidoFulfillment.Infrastructure\PedidoFulfillment.Infrastructure.csproj` em `tests/PedidoFulfillment.Testes.Unitarios/PedidoFulfillment.Testes.Unitarios.csproj`; acrescentar `PackageReference` de `Testcontainers.MsSql` e `Testcontainers.RabbitMq` em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj`
- [X] T004 Rodar `dotnet build PedidoFulfillment.sln` e garantir zero erros e zero avisos

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: ponto único de registro da Infrastructure e valores de teste na `FabricaApi`, para que a validação de configuração da US2 não quebre a suíte do REQ-01.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

- [X] T005 Criar `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs` (namespace `PedidoFulfillment.Infrastructure`), com a classe estática `ExtensoesInfraestrutura` e o método de extensão `public static IServiceCollection AdicionarInfraestrutura(this IServiceCollection services, IConfiguration configuration)`, por enquanto sem registros e retornando `services`; o comentário XML diz que o REQ-04 (persistência) e o REQ-06 (mensageria) acrescentam seus registros aqui (FR-004, R-01)
- [X] T006 Em `src/PedidoFulfillment.API/Program.cs`, chamar `builder.Services.AdicionarInfraestrutura(builder.Configuration);` logo após `builder.Services.AdicionarPoliticasAutorizacao();`, com o `using PedidoFulfillment.Infrastructure;` em ordem alfabética
- [X] T007 Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaApi.cs`: (a) acrescentar as constantes `StringConexaoTeste = "Server=127.0.0.1,1;Database=PedidoFulfillment;User Id=sa;Password=senha-de-teste;Encrypt=True;TrustServerCertificate=True;Connect Timeout=2"` e `ServidorRabbitMQTeste = "127.0.0.1"`, `PortaRabbitMQTeste = 1` (portas fechadas: o caminho de falha da prontidão roda sem Docker, R-07); (b) acrescentar a propriedade `public bool ComConfiguracaoInfraestrutura { get; init; } = true;` e, quando verdadeira, chamar `UseSetting` para `ConnectionStrings:SqlServer`, `RabbitMQ:Servidor`, `RabbitMQ:Porta`, `RabbitMQ:Usuario` (`teste`), `RabbitMQ:Senha` (`senha-de-teste`) e `RabbitMQ:VirtualHost` (`/`); (c) acrescentar `public IReadOnlyDictionary<string, string?> Configuracoes { get; init; } = new Dictionary<string, string?>();`, aplicado com `UseSetting` depois dos padrões, para sobrescrever valores (usado pela T018 com os contêineres)
- [X] T008 Rodar `dotnet test PedidoFulfillment.sln` e garantir que todos os testes do REQ-01 continuam verdes, sem Docker

**Checkpoint**: Infrastructure registrada pela API; suíte verde. As histórias podem começar.

---

## Phase 3: User Story 1 - Subir as dependências locais com um comando (Priority: P1) 🎯 MVP

**Goal**: `docker compose up -d --wait` sobe SQL Server e RabbitMQ (painel na 15672), saudáveis, com dados persistentes e limpeza documentada.

**Independent Test**: numa máquina só com Docker e SDK, `docker compose up -d --wait` retorna com `sqlserver` e `rabbitmq` `healthy`; `http://localhost:15672` aceita `oms`/`oms-dev-local`; `docker compose down` + `up` mantém os dados; `docker compose down -v` os apaga (quickstart §2).

### Implementation for User Story 1

- [X] T009 [P] [US1] Criar `docker-compose.yml` na raiz com `name: oms` e dois serviços (R-02, R-03): **`sqlserver`** com `image: mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`, `environment` `ACCEPT_EULA: "Y"`, `MSSQL_PID: Developer`, `MSSQL_SA_PASSWORD: ${OMS_SQL_SENHA:-OmsDev_2026!Local}`, `ports: ["${OMS_SQL_PORTA:-1433}:1433"]`, volume `oms-sqlserver:/var/opt/mssql` e `healthcheck` com `test: ["CMD-SHELL", "/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P \"$$MSSQL_SA_PASSWORD\" -Q \"SELECT 1\" -b"]`, `interval: 10s`, `retries: 12`, `start_period: 30s`; **`rabbitmq`** com `image: rabbitmq:4.3.6-management`, `RABBITMQ_DEFAULT_USER: ${OMS_RABBITMQ_USUARIO:-oms}`, `RABBITMQ_DEFAULT_PASS: ${OMS_RABBITMQ_SENHA:-oms-dev-local}`, `ports: ["${OMS_RABBITMQ_PORTA:-5672}:5672", "${OMS_RABBITMQ_PORTA_PAINEL:-15672}:15672"]`, volume `oms-rabbitmq:/var/lib/rabbitmq` e `healthcheck` com `test: ["CMD", "rabbitmq-diagnostics", "-q", "check_port_connectivity"]`, `interval: 10s`, `retries: 12`, `start_period: 20s`; declarar os volumes nomeados `oms-sqlserver` e `oms-rabbitmq`; comentário no topo dizendo que as credenciais são **somente de desenvolvimento** e que versões são fixas (sem `latest`, FR-008)
- [X] T010 [P] [US1] Criar `.env.example` na raiz documentando, com comentário por linha e os mesmos padrões do compose, as variáveis `OMS_SQL_PORTA=1433`, `OMS_SQL_SENHA=OmsDev_2026!Local`, `OMS_RABBITMQ_PORTA=5672`, `OMS_RABBITMQ_PORTA_PAINEL=15672`, `OMS_RABBITMQ_USUARIO=oms`, `OMS_RABBITMQ_SENHA=oms-dev-local`; avisar que a senha do SQL Server precisa de 8+ caracteres com maiúscula, minúscula, dígito e símbolo, e que trocar porta ou credencial exige sobrescrever também `ConnectionStrings__SqlServer`/`RabbitMQ__*` ao rodar a API (FR-009)
- [X] T011 [US1] Confirmar com `git check-ignore -v .env` que o `.env` já é ignorado pelo `.gitignore` (regra `# dotenv files`) e com `git check-ignore .env.example` que o `.env.example` **não** é; só alterar `.gitignore` se alguma das duas verificações falhar
- [X] T012 [US1] Acrescentar a seção `## Ambiente local` ao `README.md` (depois de `## Pré-requisitos`): pré-requisito Docker com Compose v2 (`docker compose version`; `winget install Docker.DockerDesktop`), subir com `docker compose up -d --wait` (retorna só quando os dois estão saudáveis), conferir com `docker compose ps`, painel em `http://localhost:15672` com `oms`/`oms-dev-local`, parar com `docker compose down`, apagar os dados com `docker compose down -v`, trocar portas e credenciais copiando `.env.example` para `.env`, e o aviso de que as credenciais valem só para os contêineres locais (FR-014)
- [ ] T013 [US1] Validar: `docker compose config --quiet` sem erros e o roteiro do quickstart §2 (saudável em < 2 min com imagens baixadas, painel, persistência após `down`/`up`, limpeza com `down -v`). Se o Docker não estiver instalado (R-08), registrar a validação como pendente no commit e no relatório, sem marcar esta tarefa como concluída

**Checkpoint**: dependências locais sobem com um comando e o README explica tudo.

---

## Phase 4: User Story 2 - API lê a configuração do ambiente local (Priority: P1)

**Goal**: a API valida `ConnectionStrings:SqlServer` e a seção `RabbitMQ` na inicialização, carrega os valores de desenvolvimento de `appsettings.Development.json`, deixa variáveis de ambiente prevalecerem e expõe `/health` e `/health/pronto`.

**Independent Test**: em Development a API inicia com os valores do arquivo; em Production sem configuração ela falha nomeando as chaves sem mostrar valores; `/health` responde 200 sem token; `/health/pronto` responde 503 com as dependências fora do ar e 200 com elas no ar (quickstart §1, §3, §4).

### Tests for User Story 2 ⚠️

> **NOTE: escrever estes testes PRIMEIRO e confirmar que falham antes da implementação**

- [X] T014 [P] [US2] Criar `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesSqlServerTestes.cs` testando `ValidadorOpcoesSqlServer` (T020): string vazia, só espaços ou nula → falha com exatamente "A configuração ConnectionStrings:SqlServer é obrigatória."; string preenchida → sucesso; a mensagem de falha nunca contém o valor recebido
- [X] T015 [P] [US2] Criar `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesRabbitMQTestes.cs` testando `ValidadorOpcoesRabbitMQ` (T021): opções válidas (`localhost`, `5672`, `oms`, `oms-dev-local`, `/`) → sucesso; para cada campo `Servidor`, `Usuario`, `Senha`, `VirtualHost` vazio → falha com "A configuração RabbitMQ:<Campo> é obrigatória."; `Porta` 0, -1 e 65536 → "RabbitMQ:Porta deve estar entre 1 e 65535."; com vários campos inválidos, todas as mensagens aparecem; nenhuma mensagem contém o valor da senha ou do usuário
- [X] T016 [P] [US2] Criar `tests/PedidoFulfillment.Testes.Integracao/Configuracao/ConfiguracaoAusenteTestes.cs`, numa `[Collection]` com `DisableParallelization = true` (mexe em variável de ambiente do processo): (a) `FabricaApi { Ambiente = "Production", ComConfiguracaoInfraestrutura = false }` → criar o cliente lança exceção na inicialização. Com as duas opções inválidas, a validação na inicialização lança um `AggregateException` com uma `OptionsValidationException` por opção (com uma só inválida, lança a própria `OptionsValidationException`). Por isso, coletar as `OptionsValidationException` (a própria exceção ou as `InnerExceptions` do `AggregateException`) e verificar **separadamente**: uma com `OptionsType == typeof(OpcoesSqlServer)` cujas `Failures` contêm "A configuração ConnectionStrings:SqlServer é obrigatória.", e outra com `OptionsType == typeof(OpcoesRabbitMQ)` cujas `Failures` contêm "A configuração RabbitMQ:Servidor é obrigatória."; em nenhuma mensagem aparecem `Password=` ou `oms-dev-local` (FR-012, cenário 2); (b) `Ambiente = "Development"`, `ComConfiguracaoInfraestrutura = false` → `IOptions<OpcoesRabbitMQ>` traz `localhost`/`5672`/`oms` e `IOptions<OpcoesSqlServer>` traz a string de `appsettings.Development.json` (cenário 1); (c) igual a (b) com as variáveis de ambiente `RabbitMQ__Servidor=servidor-da-variavel` e `ConnectionStrings__SqlServer=Server=servidor-da-variavel;Database=PedidoFulfillment` definidas antes de criar a fábrica e removidas no `finally` → os dois valores lidos são os das variáveis (FR-013, cenário 3)
- [X] T017 [P] [US2] Criar `tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeTestes.cs` com `FabricaApi` padrão (dependências em portas fechadas, T007): `GET /health` sem token → 200, `application/json`, corpo exatamente `{"status":"Healthy"}` (FR-015); `GET /health/pronto` sem token → 503, `status` `Unhealthy`, `dependencias.sqlserver` e `dependencias.rabbitmq` `Unhealthy`, e o corpo não contém `127.0.0.1`, `Server=`, `senha-de-teste`, `teste`, `Exception` nem `at ` (FR-016, cenário 6); **dependência travada** (FR-018): um `TcpListener` em `127.0.0.1` com porta livre (porta 0) aceita conexões e nunca responde; com `FabricaApi { Configuracoes = ... }` apontando `ConnectionStrings:SqlServer` e `RabbitMQ:Servidor`/`RabbitMQ:Porta` para ele, `GET /health/pronto` responde 503 com as duas dependências `Unhealthy` em no máximo 7 s (as verificações rodam em paralelo, cada uma limitada a 5 s), e o listener é parado ao fim do teste; `GET /openapi/v1.json` não tem nenhum caminho que comece com `/health` (FR-017)
- [X] T018 [P] [US2] Criar `tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs` com uma fixture `IAsyncLifetime` que sobe `MsSqlContainer` (imagem `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04`) e `RabbitMqContainer` (imagem `rabbitmq:4.3.6-management`) do Testcontainers; se a subida lançar porque o Docker não está disponível, a fixture guarda isso e o teste chama `Assert.Skip("Docker indisponível")` em vez de falhar (R-07, SC-005); com os contêineres no ar, `FabricaApi { Configuracoes = ... }` aponta `ConnectionStrings:SqlServer` e `RabbitMQ:*` para eles e `GET /health/pronto` sem token → 200 com `status`, `sqlserver` e `rabbitmq` `Healthy` (cenário 5)
- [X] T019 [P] [US2] (antes de T025) Em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/AutorizacaoEndpointsTestes.cs`, acrescentar `"/health"` e `"/health/pronto"` a `RotasAnonimasPermitidas` e atualizar o comentário XML da classe para citar os endereços de saúde, que o Princípio III permite públicos (FR-017)

### Implementation for User Story 2

- [X] T020 [P] [US2] Criar `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesSqlServer.cs` com `public sealed class OpcoesSqlServer { public const string NomeConexao = "SqlServer"; public string StringConexao { get; set; } = ""; }` e, no mesmo arquivo, `public sealed class ValidadorOpcoesSqlServer : IValidateOptions<OpcoesSqlServer>` que falha com "A configuração ConnectionStrings:SqlServer é obrigatória." quando `StringConexao` é nula ou em branco (data-model.md)
- [X] T021 [P] [US2] Criar `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesRabbitMQ.cs` com `public sealed class OpcoesRabbitMQ { public const string Secao = "RabbitMQ"; }` e as propriedades `Servidor`, `Usuario`, `Senha`, `VirtualHost` (`string`, padrão `""`) e `Porta` (`int`); no mesmo arquivo, `ValidadorOpcoesRabbitMQ : IValidateOptions<OpcoesRabbitMQ>` que acumula todas as falhas: "A configuração RabbitMQ:<Campo> é obrigatória." para cada string em branco e "RabbitMQ:Porta deve estar entre 1 e 65535." para porta fora de 1–65535, sem incluir valores (data-model.md)
- [X] T022 [P] [US2] Criar `src/PedidoFulfillment.Infrastructure/Saude/VerificacaoSqlServer.cs`: `internal sealed class VerificacaoSqlServer(IOptions<OpcoesSqlServer> opcoes) : IHealthCheck`, que monta um `SqlConnectionStringBuilder` a partir da string configurada com `InitialCatalog = "master"` (o banco da aplicação nasce no REQ-04) e `ConnectTimeout = 5`, abre a conexão e executa `SELECT 1` com `CommandTimeout = 5`, usando um `CancellationTokenSource` ligado ao token recebido com `CancelAfter(TimeSpan.FromSeconds(5))` (FR-018); retorna `HealthCheckResult.Healthy()` ou, em qualquer exceção, `HealthCheckResult.Unhealthy()` **sem** descrição nem exceção anexada (nada de dado de conexão pode vazar)
- [X] T023 [P] [US2] Criar `src/PedidoFulfillment.Infrastructure/Saude/VerificacaoRabbitMQ.cs`: `internal sealed class VerificacaoRabbitMQ(IOptions<OpcoesRabbitMQ> opcoes) : IHealthCheck`, que cria uma `ConnectionFactory` (`HostName`, `Port`, `UserName`, `Password`, `VirtualHost`, `RequestedConnectionTimeout = 5 s`, `AutomaticRecoveryEnabled = false`), abre a conexão com `await CreateConnectionAsync(token)` (token com `CancelAfter` de 5 s), fecha com `CloseAsync` e descarta; retorna `Healthy()` ou, em qualquer exceção, `Unhealthy()` sem descrição nem exceção
- [X] T024 [US2] Completar `AdicionarInfraestrutura` em `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs`: `AddOptions<OpcoesSqlServer>().Configure(o => o.StringConexao = configuration.GetConnectionString(OpcoesSqlServer.NomeConexao) ?? "").ValidateOnStart()`; `AddOptions<OpcoesRabbitMQ>().BindConfiguration(OpcoesRabbitMQ.Secao).ValidateOnStart()`; registrar os dois validadores como `IValidateOptions<>` singleton; `AddHealthChecks().AddCheck<VerificacaoSqlServer>("sqlserver", tags: [TagPronto], timeout: TimeSpan.FromSeconds(5)).AddCheck<VerificacaoRabbitMQ>("rabbitmq", tags: [TagPronto], timeout: TimeSpan.FromSeconds(5))`; expor `public const string TagPronto = "pronto";` na classe (depende de T020–T023)
- [X] T025 [US2] Criar `src/PedidoFulfillment.API/Saude/EndpointsSaude.cs` com `public static class EndpointsSaude` e `public static IEndpointRouteBuilder MapearEndpointsSaude(this IEndpointRouteBuilder app)`: `MapHealthChecks("/health", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = EscreverVida })` e `MapHealthChecks("/health/pronto", new HealthCheckOptions { Predicate = r => r.Tags.Contains(ExtensoesInfraestrutura.TagPronto), ResultStatusCodes = { [Healthy] = 200, [Degraded] = 503, [Unhealthy] = 503 }, ResponseWriter = EscreverProntidao })`, ambos com `.AllowAnonymous()` e `.ExcludeFromDescription()`; os escritores gravam `application/json` com `JsonSerializer` no formato de [contracts/saude.md](./contracts/saude.md): `{"status":"Healthy"}` para vida e `{"status":"<estado>","dependencias":{"sqlserver":"<estado>","rabbitmq":"<estado>"}}` para prontidão, usando só `report.Status` e `entry.Status` (nunca `Description`, `Exception` nem `Data`)
- [X] T026 [US2] Em `src/PedidoFulfillment.API/Program.cs`, chamar `app.MapearEndpointsSaude();` depois de `app.MapearEndpointsPedidos();`, fora do `if` de ambiente (saúde existe em todos os ambientes), com o `using PedidoFulfillment.API.Saude;`
- [X] T027 [US2] Em `src/PedidoFulfillment.API/appsettings.Development.json`, acrescentar um comentário `// SOMENTE DESENVOLVIMENTO: valores dos contêineres locais do docker-compose.yml; outros ambientes usam variáveis de ambiente ou cofre de segredos.`, `"ConnectionStrings": { "SqlServer": "Server=localhost,1433;Database=PedidoFulfillment;User Id=sa;Password=OmsDev_2026!Local;Encrypt=True;TrustServerCertificate=True" }` e `"RabbitMQ": { "Servidor": "localhost", "Porta": 5672, "Usuario": "oms", "Senha": "oms-dev-local", "VirtualHost": "/" }` (FR-010, FR-011). Não acrescentar nada disso a `appsettings.json`
- [X] T028 [US2] Acrescentar ao `README.md`, na seção `## Compilar, testar e executar`: a API lê `ConnectionStrings:SqlServer` e `RabbitMQ:*` (em Development vêm de `appsettings.Development.json`; fora dele, de variáveis como `ConnectionStrings__SqlServer` e `RabbitMQ__Senha`, que sempre prevalecem; sem elas a API não inicia), e os endereços `/health` (vida) e `/health/pronto` (prontidão de banco e broker, 200/503), públicos e fora do contrato v1
- [X] T029 [US2] Rodar `dotnet test PedidoFulfillment.sln`: todos os testes verdes sem Docker, com `SaudeComDependenciasTestes` ignorado ("Docker indisponível") quando o Docker não estiver rodando; confirmar que `ConformidadeOpenApiTestes` continua verde (5 operações, nenhum `/health`)

**Checkpoint**: US1 e US2 funcionam; o ambiente local é confirmado com uma consulta a `/health/pronto` (SC-006).

---

## Phase 5: User Story 3 - Estrutura de camadas completa e protegida (Priority: P2)

**Goal**: as quatro camadas existem e um teste falha quando alguma referência sai da direção do FR-002.

**Independent Test**: `dotnet build` sem avisos e `dotnet test --filter FullyQualifiedName~Arquitetura` verde; acrescentar à mão uma referência Domain → Infrastructure faz o teste falhar nomeando a referência.

### Tests for User Story 3 ⚠️

- [X] T030 [US3] Criar `tests/PedidoFulfillment.Testes.Integracao/Arquitetura/DependenciasCamadasTestes.cs` com: (a) a matriz permitida do data-model.md (`Domain` → nenhum; `Application` → `Domain`; `Infrastructure` → `Application`, `Domain`; `API` → `Application`, `Infrastructure`, `Domain`) com nomes `PedidoFulfillment.*`; (b) uma função pura `static IReadOnlyList<string> Violacoes(IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencias)` que devolve mensagens no formato `"PedidoFulfillment.Domain → PedidoFulfillment.Infrastructure não é permitida"` para toda referência a projeto da solução fora da matriz; (c) um teste que monta as referências reais e espera zero violações. As referências reais vêm de **duas fontes unidas**: `Assembly.GetReferencedAssemblies()` de cada camada (obtida por um tipo dela, por exemplo `typeof(StatusPedido).Assembly`, `typeof(IServicoPedidos).Assembly`, `typeof(ExtensoesInfraestrutura).Assembly`, `typeof(Program).Assembly`) **e** os `ProjectReference` de cada `src/<Projeto>/<Projeto>.csproj`, lidos com `XDocument` a partir da raiz encontrada subindo diretórios até achar `PedidoFulfillment.sln`, porque o compilador omite dos metadados referências de projeto não usadas e a violação passaria despercebida (SC-003); (d) um teste que confirma que existem exatamente os quatro projetos em `src/`; (e) um teste que passa à função `Violacoes` um mapa com `Domain → Infrastructure` e espera a mensagem correspondente (cenário 2)
- [X] T031 [US3] Validar à mão o cenário 2 **sem ciclo** (toda referência proibida entre as quatro camadas forma ciclo e o build a rejeita antes do teste, ver R-06): (1) remover temporariamente os `ProjectReference` de Domain e Application de `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`; (2) acrescentar temporariamente `ProjectReference` de Infrastructure em `src/PedidoFulfillment.Domain/PedidoFulfillment.Domain.csproj`; (3) rodar `dotnet test --filter FullyQualifiedName~Arquitetura` e ver a falha nomear `PedidoFulfillment.Domain → PedidoFulfillment.Infrastructure` (se a Infrastructure não compilar sem as referências, usar um `.csproj` vazio de rascunho só para essa verificação); (4) **restaurar** os `ProjectReference` de Domain e Application na Infrastructure e remover a referência de Domain para Infrastructure; (5) confirmar com `git diff --exit-code -- src/` que os dois `.csproj` voltaram ao estado original e que `dotnet build` passa

### Implementation for User Story 3

- [X] T032 [US3] Atualizar a seção `## Estrutura` do `README.md` com `PedidoFulfillment.Infrastructure/` (configuração, verificações de saúde; persistência no REQ-04 e mensageria no REQ-06), a direção de dependências do FR-002, `docker-compose.yml`/`.env.example` na raiz e os novos grupos de testes (configuração, saúde, arquitetura)
- [X] T033 [US3] Rodar `dotnet build PedidoFulfillment.sln` (zero avisos) e `dotnet test PedidoFulfillment.sln` (tudo verde, sem Docker)

**Checkpoint**: todas as histórias funcionam de forma independente.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: documentação de estado, segredos, formatação e validação final.

- [ ] T034 Atualizar `README.md`: em `## Estado atual`, linha do REQ-02 com link para `specs/002-setup-infraestrutura/spec.md` e situação "Infrastructure, ambiente local (SQL Server + RabbitMQ) e saúde", e a linha de pendentes para "REQ-03 a REQ-06"; em `## Pré-requisitos`, acrescentar o Docker com Compose v2 e apontar para o roteiro `specs/002-setup-infraestrutura/quickstart.md`
- [ ] T035 Varredura de segredos (SC-004): `git grep -n -i -E "password|senha|pwd"` nos arquivos versionados e confirmar que só aparecem os valores de desenvolvimento (`OmsDev_2026!Local`, `oms-dev-local`), os valores de teste da `FabricaApi` e nomes de chave; nenhum valor em `appsettings.json`
- [ ] T036 Rodar `dotnet format PedidoFulfillment.sln --verify-no-changes` e corrigir o que acusar
- [ ] T037 Executar o quickstart: §1 sempre; §2 a §4 quando o Docker estiver disponível (se não estiver, registrar como pendente, como na T013). Em §4, confirmar que a mensagem de falha nomeia `ConnectionStrings:SqlServer` e `RabbitMQ:*` sem mostrar valores

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende de Setup e bloqueia todas as histórias.
- **US1 (Phase 3)**: depende só da Foundational na prática; não toca código .NET.
- **US2 (Phase 4)**: depende da Foundational (T005–T007).
- **US3 (Phase 5)**: depende da Foundational; o teste de arquitetura enxerga mais com a US2 pronta, mas não depende dela.
- **Polish (Phase 6)**: depois das histórias desejadas.

### User Story Dependencies

- **US1 (P1)**: independente das outras. A US2 só é validada de ponta a ponta (cenário 5 à mão) com a US1 no ar.
- **US2 (P1)**: independente da US1 para os testes automatizados (portas fechadas e Testcontainers).
- **US3 (P2)**: independente.

### Within Each User Story

- Testes antes da implementação, e falhando primeiro.
- Na US2: T020/T021 (opções) → T022/T023 (verificações) → T024 (registro) → T025/T026 (endpoints) → T027 (valores de desenvolvimento).
- Tarefas que editam `README.md` (T012, T028, T032, T034) e `Program.cs` (T006, T026) são sequenciais entre si.

### Parallel Opportunities

- T009 e T010 (arquivos de raiz diferentes).
- T014–T019 (arquivos de teste diferentes); a T019 precisa estar concluída antes da T025, senão a varredura de autorização rejeita as rotas de saúde.
- T020–T023 (quatro arquivos novos na Infrastructure).
- US1 inteira pode andar em paralelo com a US2 ou a US3.

---

## Parallel Example: User Story 2

```text
# Testes da US2 juntos:
Task: "Testes do ValidadorOpcoesSqlServer em tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesSqlServerTestes.cs"
Task: "Testes do ValidadorOpcoesRabbitMQ em tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesRabbitMQTestes.cs"
Task: "Configuração ausente e precedência em tests/PedidoFulfillment.Testes.Integracao/Configuracao/ConfiguracaoAusenteTestes.cs"
Task: "Vida, 503 e OpenAPI em tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeTestes.cs"
Task: "Prontidão com Testcontainers em tests/PedidoFulfillment.Testes.Integracao/Saude/SaudeComDependenciasTestes.cs"

# Opções e verificações juntas:
Task: "OpcoesSqlServer em src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesSqlServer.cs"
Task: "OpcoesRabbitMQ em src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesRabbitMQ.cs"
Task: "VerificacaoSqlServer em src/PedidoFulfillment.Infrastructure/Saude/VerificacaoSqlServer.cs"
Task: "VerificacaoRabbitMQ em src/PedidoFulfillment.Infrastructure/Saude/VerificacaoRabbitMQ.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (Setup) e Phase 2 (Foundational).
2. Phase 3 (US1): o ambiente local sobe com um comando.
3. **PARAR e VALIDAR** com o quickstart §2 (exige Docker).

### Incremental Delivery

1. Setup + Foundational → solução com Infrastructure registrada, suíte verde.
2. US1 → ambiente local (commit `feat(ambiente): ... [REQ-02]`).
3. US2 → configuração validada e saúde (commit `feat(saude): ... [REQ-02]`).
4. US3 → teste de arquitetura (commit `test(arquitetura): ... [REQ-02]`).
5. Polish → README, varredura de segredos, quickstart (commit `chore(setup): ... [REQ-02]`).

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- O Docker não está instalado nesta máquina (R-08): T013 e T037 §2–§4 ficam pendentes até ele existir; nada na suíte automatizada depende dele.
- R-06 revisada: o teste de arquitetura também lê os `ProjectReference` dos `.csproj`, porque `GetReferencedAssemblies()` sozinho não vê referências de projeto não usadas no código.
- No radar (T031): a validação manual remove referências da Infrastructure temporariamente; elas **precisam** ser restauradas antes de qualquer commit.
- Fazer commit ao fim de cada fase ou grupo lógico.
