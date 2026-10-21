---

description: "Lista de tarefas do REQ-01 — Contratos da API v1"
---

# Tasks: Contratos da API v1 (OpenAPI e DTOs)

**Input**: Design documents from `/specs/001-contratos-api/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/openapi-v1.yaml](./contracts/openapi-v1.yaml),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A spec exige conformidade automatizada (FR-024) e a constituição
(Princípio V) exige testes unitários de validação, integração 401/403 + sucesso por endpoint
protegido e testes de serialização. Os testes de cada história são escritos antes da
implementação e devem falhar primeiro.

**Organization**: por história de usuário. Cada história fecha um conjunto de operações do
contrato que pode ser testado sozinho.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Raiz da solução: `PedidoFulfillment.sln` na raiz do repositório; código em `src/`, testes em `tests/`.
- Nomes de classes, arquivos, namespaces e mensagens de erro em português (constituição).
  Os DTOs mantêm o sufixo `DTO` em maiúsculas, conforme a issue.
- Endpoints Minimal API só roteiam: recebem, delegam ao serviço da Application e traduzem
  `Resultado` em `TypedResults`. Nenhuma regra de negócio nem acesso a dados no endpoint.
- Os DTOs são `sealed record` com propriedades `init` e DataAnnotations; nullable habilitado.
- Os testes de integração usam `FabricaApi` (T023) no ambiente `Testes`, com fakes de
  `IServicoAutenticacao`/`IServicoPedidos` para o caminho feliz.
- Commits: `tipo(escopo): descrição [REQ-01]`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: criar a solução e os projetos do plano.

- [ ] T001 Criar `global.json` fixando o SDK `10.0.402` com `rollForward: latestFeature`, e `.gitignore` padrão do `dotnet new gitignore` na raiz do repositório
- [ ] T002 Criar `Directory.Build.props` na raiz com `TargetFramework net10.0`, `Nullable enable`, `ImplicitUsings enable`, `TreatWarningsAsErrors true`, `LangVersion latest`, e `.editorconfig` com as regras padrão do .NET
- [ ] T003 Criar `Directory.Packages.props` na raiz com `ManagePackageVersionsCentrally=true` e as versões estáveis mais recentes compatíveis com .NET 10 de: `Microsoft.AspNetCore.OpenApi`, `Scalar.AspNetCore`, `Microsoft.AspNetCore.Authentication.JwtBearer`, `xunit.v3`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `FluentAssertions` **fixado em `[7.2.0,8.0.0)`** (licença, R-12), `Microsoft.AspNetCore.Mvc.Testing`, `Microsoft.OpenApi.YamlReader` (mesma versão maior do `Microsoft.OpenApi` usado por `Microsoft.AspNetCore.OpenApi`). Nenhum outro pacote
- [ ] T004 Criar a solução `PedidoFulfillment.sln` e os projetos: `src/PedidoFulfillment.Domain` (classlib), `src/PedidoFulfillment.Application` (classlib, referencia Domain), `src/PedidoFulfillment.API` (web vazio, referencia Application e Domain), `tests/PedidoFulfillment.Testes.Unitarios` (xUnit v3, referencia Application) e `tests/PedidoFulfillment.Testes.Integracao` (xUnit v3, referencia API). Remover arquivos de exemplo gerados pelos templates
- [ ] T005 Adicionar as referências de pacote (sem versão, vindas de T003) a cada `.csproj`, conforme a tabela de justificativas do plan.md; em `src/PedidoFulfillment.API/PedidoFulfillment.API.csproj` **e** em `tests/PedidoFulfillment.Testes.Integracao/PedidoFulfillment.Testes.Integracao.csproj` incluir `<None Include="..\..\specs\001-contratos-api\contracts\openapi-v1.yaml" Link="Contrato\openapi-v1.yaml" CopyToOutputDirectory="PreserveNewest" CopyToPublishDirectory="PreserveNewest" />` (fonte única da verdade, R-03)
- [ ] T006 Rodar `dotnet build PedidoFulfillment.sln` e `dotnet test PedidoFulfillment.sln` e garantir que compilam sem avisos

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: tipos de domínio, formato de erro, segurança, OpenAPI e infraestrutura de testes usados por todas as histórias.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

- [ ] T007 [P] Criar enum `StatusPedido` com os valores exatos `Pendente, Processando, ProntoParaEnvio, Despachado, Cancelado, PagamentoRejeitado` em `src/PedidoFulfillment.Domain/Pedidos/StatusPedido.cs`
- [ ] T008 [P] Criar enum `PerfilUsuario` com `Cliente, Admin, OperadorLogistica` em `src/PedidoFulfillment.Domain/Usuarios/PerfilUsuario.cs`
- [ ] T009 [P] Criar `CodigosErro` (constantes `string`) com `USUARIO_EMAIL_JA_CADASTRADO`, `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`, `IDEMPOTENCIA_EM_PROCESSAMENTO`, `PRODUTO_INEXISTENTE`, `PRODUTO_INDISPONIVEL`, `PEDIDO_TRANSICAO_INVALIDA` em `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs`
- [ ] T010 [P] Criar `Resultado<T>` (sucesso com valor | falha com `TipoFalha` {Validacao, NaoAutenticado, SemPermissao, NaoEncontrado, Conflito, RegraNegocio, NaoImplementado}, `Codigo` opcional, `Titulo`, `Detalhe`) e `TipoFalha` em `src/PedidoFulfillment.Application/Comum/Resultado.cs`
- [ ] T011 Configurar ProblemDetails em `src/PedidoFulfillment.API/Erros/ConfiguracaoProblemDetails.cs`: `AddProblemDetails` com `CustomizeProblemDetails` que sempre preenche `instance` (= `HttpContext.Request.Path`) e a extensão `traceId` (`Activity.Current?.Id ?? HttpContext.TraceIdentifier`); `UseExceptionHandler` que transforma `BadHttpRequestException` em 400 (título "Requisição inválida.", e quando o erro for de cabeçalho `X-Idempotency-Key` ausente/inválido, devolver `ValidacaoProblemDetails` com `erros["X-Idempotency-Key"] = ["O cabeçalho X-Idempotency-Key é obrigatório e deve ser um UUID."]`) e qualquer outra exceção em 500 com título "Erro interno." sem `detail`, sem stack trace; `UseStatusCodePages` para 4xx/5xx sem corpo. Todas as respostas com `application/problem+json`
- [ ] T012 Criar `ExtensoesResultado.ParaResposta()` em `src/PedidoFulfillment.API/Erros/ExtensoesResultado.cs`, que converte `Resultado<T>` em `IResult`: falhas viram ProblemDetails com o status de cada `TipoFalha` (400, 401, 403, 404, 409, 422, 501) e, para 409/422, a extensão `codigo`
- [ ] T013 Criar `FiltroValidacao<T>` (`IEndpointFilter`) em `src/PedidoFulfillment.API/Erros/FiltroValidacao.cs`, que valida o argumento `T` com `Validator.TryValidateObject(validateAllProperties: true)`, valida recursivamente coleções de objetos (chaves no formato `itens[0].quantidade`), executa `IValidatableObject` e devolve 400 `ValidacaoProblemDetails` (`title` "Um ou mais campos são inválidos.") com o dicionário `erros` (chaves em camelCase). Corpo nulo → `erros[""] = ["O corpo da requisição é obrigatório."]`
- [ ] T014 Criar `PoliticasAutorizacao` em `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs` com as constantes e o registro das policies: `PoliticaCliente` (perfil Cliente), `PoliticaConsultaPedido` e `PoliticaAtualizacaoStatusPedido` (Cliente, Admin ou OperadorLogistica), e `FallbackPolicy` = usuário autenticado
- [ ] T015 Criar `ConfiguracaoJwt` em `src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs`: opções `Jwt:Emissor`, `Jwt:Audiencia`, `Jwt:ChaveAssinatura` (HMAC-SHA256, falhar na inicialização se a chave tiver menos de 32 bytes); validação de emissor, audiência, tempo de vida (`ClockSkew` 30s) e assinatura; `RoleClaimType = ClaimTypes.Role`, `MapInboundClaims = false`; eventos `OnChallenge` (chamar `HandleResponse`, 401 ProblemDetails "Não autenticado.") e `OnForbidden` (403 ProblemDetails "Acesso negado.") escritos via `IProblemDetailsService`
- [ ] T016 Adicionar a seção `Jwt` com uma chave de desenvolvimento de 32+ bytes **marcada como somente desenvolvimento** em `src/PedidoFulfillment.API/appsettings.Development.json`; em `src/PedidoFulfillment.API/appsettings.json` deixar `Emissor`/`Audiencia` (`PedidoFulfillment`) e sem chave
- [ ] T017 Criar `TransformadorSegurancaOpenApi` (`IOpenApiDocumentTransformer` + `IOpenApiOperationTransformer`) em `src/PedidoFulfillment.API/OpenApi/TransformadorSegurancaOpenApi.cs`: registra o esquema `bearerAuth` (http, bearer, JWT); em operações com `[AllowAnonymous]` define `security: []`; nas demais adiciona `bearerAuth` e as respostas 401/403 (`ProblemDetails`, `application/problem+json`)
- [ ] T018 Criar `TransformadorSchemasErroOpenApi` em `src/PedidoFulfillment.API/OpenApi/TransformadorSchemasErroOpenApi.cs`, que registra os schemas `ProblemDetails`, `ValidacaoProblemDetails` e `NegocioProblemDetails` exatamente como em `contracts/openapi-v1.yaml` e adiciona 500 `ProblemDetails` a todas as operações. Configurar `info.title` = "Order Management System - API de Pedidos" e `info.version` = "v1", e as tags `Autenticacao` e `Pedidos`
- [ ] T019 Criar as interfaces vazias `IServicoAutenticacao` em `src/PedidoFulfillment.Application/Autenticacao/IServicoAutenticacao.cs` e `IServicoPedidos` em `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs`
- [ ] T020 Criar `ServicoNaoImplementado` (implementa `IServicoAutenticacao` e `IServicoPedidos`, todos os métodos devolvem `Resultado` com `TipoFalha.NaoImplementado`, título "Operação ainda não implementada.") em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`, com comentário `// Provisório (research R-05): remover no REQ-03/REQ-05`. As interfaces começam vazias e ganham métodos em cada história
- [ ] T021 Montar `src/PedidoFulfillment.API/Program.cs`: `ConfigureHttpJsonOptions` (camelCase, `JsonStringEnumConverter`, ignorar campos desconhecidos), `Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true)` **em todos os ambientes** (R-08; sem isso, fora de Development, falhas de binding viram 400 sem corpo), ProblemDetails (T011), autenticação/autorização (T014/T015), `AddOpenApi("v1")` com os transformers T017/T018, registro de `ServicoNaoImplementado` para as duas interfaces; pipeline `UseExceptionHandler` → `UseStatusCodePages` → `UseAuthentication` → `UseAuthorization`; `MapOpenApi()` e `MapearEndpointsDocumentacao()` (T029) com `AllowAnonymous` **somente** quando o ambiente for `Development` ou `Testes` (constituição v1.1.0, Princípio III); deixar `public partial class Program;` para os testes
- [ ] T022 [P] Criar `GeradorTokenTeste` em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/GeradorTokenTeste.cs`, que gera JWT com a chave de teste para um `usuarioId` e perfis informados, com opções para token expirado, assinatura inválida e audiência errada
- [ ] T023 [P] Criar `FabricaApi : WebApplicationFactory<Program>` em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaApi.cs`, com ambiente `Testes`, configuração `Jwt` de teste em memória e a possibilidade de substituir `IServicoAutenticacao`/`IServicoPedidos` por fakes; e `ClienteHttpExtensoes` (`ComToken(perfis...)`)
- [ ] T024 [P] Criar `LeitorContrato` em `tests/PedidoFulfillment.Testes.Integracao/Contrato/LeitorContrato.cs`, que carrega `Contrato/openapi-v1.yaml` com `Microsoft.OpenApi.YamlReader` e o JSON de `/openapi/v1.json` com o leitor JSON do `Microsoft.OpenApi`, e `ComparadorContrato`, que compara para uma operação (`método + caminho`): parâmetros (nome, local, obrigatório, formato), corpo (schema referenciado), códigos de resposta, segurança e, para cada schema referenciado, propriedades, `type`/`format`, `required`, `enum`, `minLength`/`maxLength`/`minimum`/`maximum`/`minItems`/`maxItems`/`pattern`. Ignora descrições e exemplos. `number` com formato `double` ou `decimal` é equivalente (R-11)
- [ ] T025 Criar o teste de formato de erro global em `tests/PedidoFulfillment.Testes.Integracao/Erros/FormatoErroTestes.cs`: rota inexistente → 404 `application/problem+json` com `type`, `title`, `status`, `instance`, `traceId`; exceção não tratada (endpoint de teste registrado só pela `FabricaApi`) → 500 sem `detail`, sem stack trace e sem nome de tipo .NET

**Checkpoint**: `dotnet test` verde; a API sobe (com o 400 de binding em ProblemDetails também fora de Development) e `/openapi/v1.json` contendo `bearerAuth` e os schemas de erro.

---

## Phase 3: User Story 1 - Consultar o contrato da API v1 (Priority: P1) 🎯 MVP

**Goal**: documento OpenAPI e UI navegável publicados, nos ambientes certos, com teste de conformidade em vigor.

**Independent Test**: em `Development`, `/openapi/v1.json` e `/scalar/v1` respondem 200; em `Production`, 404; o teste de conformidade roda contra o contrato.

### Tests for User Story 1 ⚠️

- [ ] T026 [P] [US1] Teste em `tests/PedidoFulfillment.Testes.Integracao/Contrato/PublicacaoContratoTestes.cs`: em `Testes`, `GET /openapi/v1.yaml`, `GET /openapi/v1.json` e `GET /scalar/v1` → 200 sem token; o corpo de `/openapi/v1.yaml` é byte a byte igual a `Contrato/openapi-v1.yaml` e tem content-type `application/yaml`; o JSON gerado declara OpenAPI 3.1, `info.version` = `v1` e `securitySchemes.bearerAuth` (http/bearer/JWT); o HTML do Scalar referencia `/openapi/v1.yaml`; em `Production` (fábrica com ambiente trocado) os três → 404
- [ ] T027 [P] [US1] Teste `ConformidadeOpenApiTestes` em `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs`: (a) schemas `ProblemDetails`, `ValidacaoProblemDetails`, `NegocioProblemDetails`, `StatusPedido`, `PerfilUsuario` do gerado são equivalentes aos do contrato; (b) `[Theory]` `OperacaoConformeAoContrato(metodo, caminho)` usando `ComparadorContrato`, **criado sem linhas**: cada história acrescenta a linha da sua operação junto com o endpoint (T039, T049, T053, T058), assim a suíte fica verde ao fim de cada fase; (c) nenhuma resposta 501 aparece no documento gerado

### Implementation for User Story 1

- [ ] T028 [US1] Garantir que os schemas de enum `StatusPedido` e `PerfilUsuario` saem no documento como `type: string` com `enum` (ajustar no `TransformadorSchemasErroOpenApi` se o gerador nativo não produzir isso) em `src/PedidoFulfillment.API/OpenApi/TransformadorSchemasErroOpenApi.cs`
- [ ] T029 [US1] Criar `EndpointsDocumentacao.MapearEndpointsDocumentacao()` em `src/PedidoFulfillment.API/OpenApi/EndpointsDocumentacao.cs`: `GET /openapi/v1.yaml` servindo o arquivo `Contrato/openapi-v1.yaml` da pasta da aplicação com `application/yaml` (excluído do documento gerado com `ExcludeFromDescription`), e `MapScalarApiReference` com título "OMS - API v1", autenticação Bearer pré-selecionada e documento `v1` apontando para `/openapi/v1.yaml`

**Checkpoint**: T026 e T027 verdes; quickstart §2 itens 1–3 e §3 conferem.

---

## Phase 4: User Story 2 - Contratos de autenticação (Priority: P1)

**Goal**: `POST /api/v1/auth/register` e `POST /api/v1/auth/login` mapeados, públicos, com validação e respostas do contrato.

**Independent Test**: as linhas de register e login acrescentadas ao T027(b) ficam verdes; validação devolve 400 com `erros`; com fake, sucesso devolve 201/200 com os DTOs corretos.

### Tests for User Story 2 ⚠️

- [ ] T030 [P] [US2] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Contratos/RegistrarUsuarioRequestDTOTestes.cs`: `nome` obrigatório, "2 a 100 caracteres"; `email` obrigatório, "formato válido, até 256 caracteres"; `senha` obrigatória, "8–128 caracteres, ≥1 maiúscula, ≥1 minúscula, ≥1 dígito"; mensagens em português; caso válido sem erros
- [ ] T031 [P] [US2] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Contratos/LoginRequestDTOTestes.cs`: `email` obrigatório e em formato válido; `senha` obrigatória e não vazia
- [ ] T032 [P] [US2] Testes de serialização em `tests/PedidoFulfillment.Testes.Integracao/Serializacao/SerializacaoAutenticacaoTestes.cs`: `UsuarioResponseDTO` e `LoginResponseDTO` serializam em camelCase, `perfis` como texto, `expiraEm` ISO 8601 UTC, nenhuma propriedade `senha`/`hash`; `tipoToken` = `Bearer`
- [ ] T033 [P] [US2] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/EndpointsAutenticacaoTestes.cs`: register e login **sem token** não devolvem 401; corpo `{}` → 400 com `erros.nome`/`erros.email`/`erros.senha` (register) e `erros.email`/`erros.senha` (login); JSON mal formado → 400 ProblemDetails; com fake: register → 201 `UsuarioResponseDTO`, e-mail duplicado → 409 `codigo` `USUARIO_EMAIL_JA_CADASTRADO`, login → 200 `LoginResponseDTO`, credenciais inválidas → 401 ProblemDetails com `detail` "E-mail ou senha incorretos."; sem fake (provisório) → 501

### Implementation for User Story 2

- [ ] T034 [P] [US2] Criar `RegistrarUsuarioRequestDTO` (`nome` `[Required]` + `IValidatableObject` validando "2–100 caracteres contados após `nome.Trim()`" (o DTO não altera o valor; o aparo definitivo é do serviço no REQ-03); `email` `[Required][EmailAddress][MaxLength(256)]`; `senha` `[Required][StringLength(128, MinimumLength = 8)]` + regra "≥1 maiúscula, ≥1 minúscula, ≥1 dígito" via `IValidatableObject`) em `src/PedidoFulfillment.Application/Contratos/Autenticacao/RegistrarUsuarioRequestDTO.cs`
- [ ] T035 [P] [US2] Criar `UsuarioResponseDTO` (`Guid Id`, `string Nome`, `string Email`, `IReadOnlyList<PerfilUsuario> Perfis`) em `src/PedidoFulfillment.Application/Contratos/Autenticacao/UsuarioResponseDTO.cs`
- [ ] T036 [P] [US2] Criar `LoginRequestDTO` (`email` `[Required][EmailAddress]`, `senha` `[Required]`) em `src/PedidoFulfillment.Application/Contratos/Autenticacao/LoginRequestDTO.cs`
- [ ] T037 [P] [US2] Criar `LoginResponseDTO` (`string TokenAcesso`, `string TipoToken` = "Bearer", `DateTimeOffset ExpiraEm`, `IReadOnlyList<PerfilUsuario> Perfis`) em `src/PedidoFulfillment.Application/Contratos/Autenticacao/LoginResponseDTO.cs`
- [ ] T038 [US2] Adicionar a `IServicoAutenticacao` os métodos `Task<Resultado<UsuarioResponseDTO>> RegistrarAsync(RegistrarUsuarioRequestDTO, CancellationToken)` e `Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO, CancellationToken)` em `src/PedidoFulfillment.Application/Autenticacao/IServicoAutenticacao.cs`, e as implementações 501 em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`
- [ ] T039 [US2] Acrescentar as linhas `POST /api/v1/auth/register` e `POST /api/v1/auth/login` ao `[Theory]` do T027(b) e criar `EndpointsAutenticacao.MapearEndpointsAutenticacao()` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`: grupo `/api/v1/auth` com tag `Autenticacao` e `AllowAnonymous`; `POST /register` (`operationId` `RegistrarUsuario`, `FiltroValidacao<RegistrarUsuarioRequestDTO>`, `Produces<UsuarioResponseDTO>(201)`, `ProducesValidationProblem`→`ValidacaoProblemDetails` 400, `NegocioProblemDetails` 409) e `POST /login` (`operationId` `AutenticarUsuario`, `Produces<LoginResponseDTO>(200)`, 400, 401 `ProblemDetails`). Handler só chama o serviço e `ParaResposta()`; register com sucesso devolve `TypedResults.Created` sem `Location`. Registrar em `Program.cs`

**Checkpoint**: T030–T033 verdes; linhas register/login do T027(b) verdes.

---

## Phase 5: User Story 3 - Contrato de criação de pedido com idempotência (Priority: P1)

**Goal**: `POST /api/v1/pedidos` mapeado, restrito a Cliente, com `X-Idempotency-Key` obrigatório e respostas do contrato.

**Independent Test**: linha `POST /api/v1/pedidos` do T027(b) verde; 401/403/400 corretos; com fake, 201 com `Location` e `PedidoResponseDTO`.

### Tests for User Story 3 ⚠️

- [ ] T040 [P] [US3] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Contratos/CriarPedidoRequestDTOTestes.cs`: `itens` obrigatório, "1–50 itens"; "`produtoId` único na lista"; `ItemPedidoDTO.produtoId` obrigatório e "diferente do UUID vazio"; `quantidade` "1–999"; chaves de erro no formato `itens[0].quantidade`
- [ ] T041 [P] [US3] Testes de serialização em `tests/PedidoFulfillment.Testes.Integracao/Serializacao/SerializacaoPedidoTestes.cs`: `PedidoResponseDTO` em camelCase, `status` como texto, `criadoEm`/`atualizadoEm` ISO 8601 UTC, `valorTotal`/`precoUnitario`/`subtotal` como número com 2 casas, `moeda` = `BRL`; desserializar `CriarPedidoRequestDTO` com campo desconhecido não gera erro
- [ ] T042 [P] [US3] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/CriarPedidoTestes.cs`: sem token, token expirado, assinatura inválida, audiência errada → 401 ProblemDetails; perfis Admin e OperadorLogistica → 403 ProblemDetails; Cliente sem `X-Idempotency-Key`, com valor vazio ou não UUID → 400 com `erros["X-Idempotency-Key"]`; corpo `{"itens":[]}` → 400; com fake: 201 com `Location: /api/v1/pedidos/{id}` e corpo `PedidoResponseDTO`; fake devolvendo `Conflito`/`IDEMPOTENCIA_CONTEUDO_DIVERGENTE` → 409 com `codigo`; `RegraNegocio`/`PRODUTO_INEXISTENTE` → 422 com `codigo`; o fake recebe o `clienteId` vindo do claim `sub` do token e a chave de idempotência

### Implementation for User Story 3

- [ ] T043 [P] [US3] Criar `ItemPedidoDTO` (`Guid ProdutoId` obrigatório e diferente de `Guid.Empty`; `int Quantidade` `[Range(1, 999)]`) em `src/PedidoFulfillment.Application/Contratos/Pedidos/ItemPedidoDTO.cs`
- [ ] T044 [P] [US3] Criar `CriarPedidoRequestDTO` (`IReadOnlyList<ItemPedidoDTO> Itens` `[Required][MinLength(1)][MaxLength(50)]`, `IValidatableObject` para "cada produtoId pode aparecer no máximo uma vez") em `src/PedidoFulfillment.Application/Contratos/Pedidos/CriarPedidoRequestDTO.cs`
- [ ] T045 [P] [US3] Criar `ItemPedidoResponseDTO` (`ProdutoId`, `NomeProduto`, `Quantidade`, `decimal PrecoUnitario`, `decimal Subtotal`) em `src/PedidoFulfillment.Application/Contratos/Pedidos/ItemPedidoResponseDTO.cs`
- [ ] T046 [P] [US3] Criar `PedidoResponseDTO` (`Guid Id`, `string Numero` no padrão `PED-AAAAMMDD-NNNNNN`, `Guid ClienteId`, `StatusPedido Status`, `IReadOnlyList<ItemPedidoResponseDTO> Itens`, `decimal ValorTotal`, `string Moeda` = "BRL", `DateTimeOffset CriadoEm`, `DateTimeOffset AtualizadoEm`) em `src/PedidoFulfillment.Application/Contratos/Pedidos/PedidoResponseDTO.cs`
- [ ] T047 [US3] Adicionar a `IServicoPedidos` o método `Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken)` em `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs` e a implementação 501 em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`
- [ ] T048 [US3] Criar `ExtensoesUsuario.ObterUsuarioId(ClaimsPrincipal)` (lê o claim `sub` como `Guid`) e `ObterPerfis(ClaimsPrincipal)` em `src/PedidoFulfillment.API/Seguranca/ExtensoesUsuario.cs`
- [ ] T049 [US3] Acrescentar a linha `POST /api/v1/pedidos` ao `[Theory]` do T027(b) e criar `EndpointsPedidos.MapearEndpointsPedidos()` em `src/PedidoFulfillment.API/Endpoints/EndpointsPedidos.cs` com grupo `/api/v1/pedidos` (tag `Pedidos`) e `POST /` (`operationId` `CriarPedido`, `RequireAuthorization(PoliticaCliente)`, `[FromHeader(Name = "X-Idempotency-Key")] Guid`, `FiltroValidacao<CriarPedidoRequestDTO>`, `Produces<PedidoResponseDTO>(201)` com cabeçalho `Location`, 400, 409 e 422 `NegocioProblemDetails`); o handler chama `CriarAsync(User.ObterUsuarioId(), chave, dto)` e devolve `TypedResults.Created($"/api/v1/pedidos/{id}", dto)`. Registrar em `Program.cs`
- [ ] T050 [US3] Adicionar ao documento o parâmetro `X-Idempotency-Key` como `string`/`uuid` obrigatório, a descrição de idempotência do contrato e o cabeçalho de resposta `Location` (transformer de operação em `src/PedidoFulfillment.API/OpenApi/TransformadorPedidosOpenApi.cs`), se o gerador nativo não produzir isso

**Checkpoint**: T040–T042 verdes; linha `POST /api/v1/pedidos` do T027(b) verde.

---

## Phase 6: User Story 4 - Contrato de consulta de pedido (Priority: P2)

**Goal**: `GET /api/v1/pedidos/{id}` mapeado para os três perfis, com visibilidade delegada ao serviço.

**Independent Test**: linha `GET` do T027(b) verde; 401 sem token; 400 para id inválido; com fake, 200 e 404.

### Tests for User Story 4 ⚠️

- [ ] T051 [P] [US4] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/ObterPedidoTestes.cs`: sem token/token inválido → 401; token sem nenhum dos perfis → 403; `id` = `abc` → 400 (não 404); com fake: Cliente dono → 200 `PedidoResponseDTO`; fake devolvendo `NaoEncontrado` (Cliente não dono ou id inexistente) → 404 ProblemDetails; Admin e OperadorLogistica → 200; o fake recebe `usuarioId` e perfis do token

### Implementation for User Story 4

- [ ] T052 [US4] Adicionar a `IServicoPedidos` o método `Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken)` em `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs`, com comentário XML documentando "Cliente que não é dono recebe NaoEncontrado", e a implementação 501 em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`
- [ ] T053 [US4] Acrescentar a linha `GET /api/v1/pedidos/{id}` ao `[Theory]` do T027(b) e adicionar `GET /{id}` (sem restrição `:guid` na rota, R-10; `operationId` `ObterPedido`; `RequireAuthorization(PoliticaConsultaPedido)`; `Produces<PedidoResponseDTO>(200)`, 400, 404) em `src/PedidoFulfillment.API/Endpoints/EndpointsPedidos.cs`

**Checkpoint**: T051 verde; linha `GET /api/v1/pedidos/{id}` do T027(b) verde.

---

## Phase 7: User Story 5 - Contrato de atualização de status do pedido (Priority: P2)

**Goal**: `PATCH /api/v1/pedidos/{id}/status` mapeado, com validação do corpo e matriz de transições delegada ao serviço.

**Independent Test**: linha `PATCH` do T027(b) verde; 400 para `PagamentoRejeitado` e para Cancelado sem motivo; com fake, 200/403/404/409.

### Tests for User Story 5 ⚠️

- [ ] T054 [P] [US5] Testes unitários em `tests/PedidoFulfillment.Testes.Unitarios/Contratos/AtualizarStatusPedidoRequestDTOTestes.cs`: `status` obrigatório; `status` = `PagamentoRejeitado` → erro "O status PagamentoRejeitado não pode ser definido por esta operação."; `motivo` "até 500 caracteres"; `motivo` "obrigatório se status = Cancelado"; casos válidos sem erros
- [ ] T055 [P] [US5] Testes de integração em `tests/PedidoFulfillment.Testes.Integracao/Endpoints/AtualizarStatusPedidoTestes.cs`: sem token → 401; token sem perfil → 403; `{"status":"Inexistente"}` → 400; `{"status":"Cancelado"}` sem motivo → 400 com `erros.motivo`; com fake: 200 `PedidoResponseDTO`; fake devolvendo `Conflito`/`PEDIDO_TRANSICAO_INVALIDA` → 409 com `codigo`; `SemPermissao` → 403; `NaoEncontrado` → 404; o fake recebe `usuarioId`, perfis e o DTO

### Implementation for User Story 5

- [ ] T056 [P] [US5] Criar `AtualizarStatusPedidoRequestDTO` (`StatusPedido Status` **não anulável**, para o schema gerado bater com o contrato, I3; campo ausente é detectado com `[JsonRequired]`, que gera falha de binding → 400 via T011; `string? Motivo` `[MaxLength(500)]`; `IValidatableObject` para "não pode ser `PagamentoRejeitado`" e "obrigatório se `status = Cancelado`") em `src/PedidoFulfillment.Application/Contratos/Pedidos/AtualizarStatusPedidoRequestDTO.cs`
- [ ] T057 [US5] Adicionar a `IServicoPedidos` o método `Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO dto, CancellationToken)` em `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs`, com comentário XML reproduzindo a tabela de transições do FR-020 (De/Para/Perfis) e as regras 403/404/409, e a implementação 501 em `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs`
- [ ] T058 [US5] Acrescentar a linha `PATCH /api/v1/pedidos/{id}/status` ao `[Theory]` do T027(b) e adicionar `PATCH /{id}/status` (`operationId` `AtualizarStatusPedido`; `RequireAuthorization(PoliticaAtualizacaoStatusPedido)`; `FiltroValidacao<AtualizarStatusPedidoRequestDTO>`; `Produces<PedidoResponseDTO>(200)`, 400, 404, 409 `NegocioProblemDetails`) em `src/PedidoFulfillment.API/Endpoints/EndpointsPedidos.cs`

**Checkpoint**: T054–T055 verdes; todas as linhas do T027(b) verdes.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [ ] T059 Adicionar ao `ConformidadeOpenApiTestes` em `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs` o teste de igualdade **exata** entre o conjunto de operações (`método + caminho`) do contrato e do documento gerado (nenhuma operação a mais nem a menos, FR-003)
- [ ] T060 [P] Teste de segurança abrangente em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/AutorizacaoEndpointsTestes.cs`: percorre todos os endpoints do `EndpointDataSource` e falha se algum, exceto register, login, `/openapi/*` e `/scalar/*`, não exigir autorização (garante o Princípio III para rotas futuras)
- [ ] T061 [P] Criar `README.md` na raiz com: objetivo do OMS, pré-requisitos (.NET 10 SDK), como compilar/testar/rodar, link para `specs/001-contratos-api/quickstart.md` e para o contrato, e o aviso de que as operações devolvem 501 até REQ-03/REQ-05
- [ ] T062 Executar o [quickstart.md](./quickstart.md) completo (§1–§3) e registrar o resultado no PR
- [ ] T063 [P] Criar `.github/workflows/contrato-compatibilidade.yml`: em `pull_request` que altere `specs/**/contracts/*.yaml`, baixar o arquivo da `main` (`git show origin/main:specs/001-contratos-api/contracts/openapi-v1.yaml`) e rodar `oasdiff breaking <base> <head> --fail-on ERR` (imagem `tufin/oasdiff` com versão fixa); se o arquivo ainda não existir na `main`, o job passa com aviso (SC-006, FR-023)
- [ ] T064 Revisão final contra a constituição: endpoints sem regra nem acesso a dados, nomes em português, só pacotes justificados no `Directory.Packages.props`, todos os erros em ProblemDetails, commits com `[REQ-01]`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** → histórias.
- **US1 (Phase 3)**: depende só da Phase 2. O T027(b) nasce sem linhas; cada história acrescenta a sua, então a suíte fica verde ao fim de cada fase.
- **US2, US3, US4, US5**: dependem da Phase 2 e do T027 (US1), que verifica a conformidade de cada uma. Entre si são independentes, com uma ressalva: US4 e US5 editam `EndpointsPedidos.cs`, criado em US3, e usam `PedidoResponseDTO` (T046). Faça US3 antes de US4/US5.
- **Polish (Phase 8)**: depois de todas as histórias.

### Ordem recomendada

Setup → Foundational → US1 → US2 → US3 → US4 → US5 → Polish

### Within Each User Story

Testes (falhando) → DTOs → método da interface + 501 provisório → endpoint → teste verde.

## Parallel Opportunities

- Phase 2: T007, T008, T009, T010 em paralelo; T022, T023, T024 em paralelo depois de T021.
- US2: T030–T033 em paralelo; depois T034–T037 em paralelo.
- US3: T040–T042 em paralelo; depois T043–T046 em paralelo.
- US4 e US5: os testes (T051, T054, T055) e T056 podem ser escritos em paralelo à US3; a implementação espera T046/T049.

### Exemplo (US3)

```text
Em paralelo: T040 (unitário CriarPedidoRequestDTO), T041 (serialização), T042 (integração)
Em paralelo: T043 ItemPedidoDTO, T044 CriarPedidoRequestDTO, T045 ItemPedidoResponseDTO, T046 PedidoResponseDTO
Sequencial: T047 → T048 → T049 → T050
```

## Implementation Strategy

### MVP (Setup + Foundational + US1)

Contrato publicado e navegável em Development, com formato de erro, JWT e policies ativos
e a conformidade dos schemas comuns verificada. Ponto de demonstração da User Story 1.

### Entrega incremental

Cada história acrescenta e fecha a linha de conformidade de suas operações no T027(b). Commit ao fim
de cada fase, por exemplo `feat(autenticacao): mapeia contratos de registro e login [REQ-01]`.

### Fora do escopo do REQ-01

Emissão de tokens e Identity (REQ-03), persistência (REQ-04), regras de criação e
idempotência (REQ-05), status/consulta reais e eventos (REQ-06). O `ServicoNaoImplementado`
é removido quando essas implementações chegarem.
