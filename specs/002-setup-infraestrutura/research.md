# Research: Estrutura da solução e ambiente local (REQ-02)

**Data**: 2026-10-21 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Projeto Infrastructure e ponto de registro

- **Decisão**: criar o projeto `PedidoFulfillment.Infrastructure` (classlib, referencia
  Application e Domain). Ele expõe `ExtensoesInfraestrutura.AdicionarInfraestrutura(
  IServiceCollection, IConfiguration)`, que registra opções e verificações de saúde, e é
  chamado uma vez no `Program.cs` (FR-004). O REQ-04 e o REQ-06 acrescentam DbContext,
  outbox e publicadores dentro desse método.
- **Alternativas**: registrar tudo no `Program.cs`. Rejeitada, porque espalha detalhes de
  infraestrutura pela API.

## R-02. Imagens e definição do ambiente

- **Decisão**: `docker-compose.yml` na raiz, com o projeto `oms` e dois serviços:

  | Serviço | Imagem | Portas (padrão) | Volume |
  |---------|--------|-----------------|--------|
  | `sqlserver` | `mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04` (edição Developer) | `${OMS_SQL_PORTA:-1433}:1433` | `oms-sqlserver:/var/opt/mssql` |
  | `rabbitmq` | `rabbitmq:4.3.6-management` | `${OMS_RABBITMQ_PORTA:-5672}:5672`, `${OMS_RABBITMQ_PORTA_PAINEL:-15672}:15672` | `oms-rabbitmq:/var/lib/rabbitmq` |

  As versões são fixas (FR-008). Não usei a CU27 nem a 4.4.0, que são recentes demais. A
  atualização é manual e deliberada.
- **Saúde**: `sqlserver` usa `sqlcmd -C ... -Q "SELECT 1" -b` (interval 10s, retries 12,
  start_period 30s); `rabbitmq` usa `rabbitmq-diagnostics -q check_port_connectivity`.
  O comando de subida é `docker compose up -d --wait`, que só retorna com os dois saudáveis
  (FR-006).
- **Comandos**: subir `docker compose up -d --wait`; parar `docker compose down`; apagar
  dados `docker compose down -v` (FR-007).
- **Alternativas**: PostgreSQL. Rejeitada, porque a constituição define SQL Server. Também
  considerei a imagem do Azure SQL Edge, mas ela foi descontinuada.

## R-03. Credenciais de desenvolvimento e sobrescrita

- **Decisão**: o compose usa variáveis com valor padrão de desenvolvimento
  (`${OMS_SQL_SENHA:-OmsDev_2026!Local}`, `${OMS_RABBITMQ_USUARIO:-oms}`,
  `${OMS_RABBITMQ_SENHA:-oms-dev-local}`). Um `.env.example` versionado documenta as
  variáveis, e o `.env` real fica no `.gitignore` (FR-009). A senha padrão atende à
  política do SQL Server (8+ caracteres, maiúscula, minúscula, dígito e símbolo).
- **Justificativa**: as credenciais só valem para contêineres locais (spec, Assumptions) e
  podem ser trocadas sem editar arquivos versionados.
- **Alternativas**: exigir `.env` antes da primeira subida. Rejeitada, porque piora o
  SC-001.

## R-04. Configuração da API

- **Decisão**:
  - `ConnectionStrings:SqlServer` e a seção `RabbitMQ` (`Servidor`, `Porta`, `Usuario`,
    `Senha`, `VirtualHost`) ficam em `appsettings.Development.json`, com comentário de que
    são só de desenvolvimento.
  - A string aponta para o banco `PedidoFulfillment`, que só passa a existir no REQ-04.
  - `OpcoesRabbitMQ` e `OpcoesSqlServer` usam `ValidateOnStart` com mensagens que nomeiam
    a chave ausente, sem valores (FR-012). A validação vale em todos os ambientes; em
    Development os valores já vêm do arquivo.
  - Variáveis de ambiente (`ConnectionStrings__SqlServer`, `RabbitMQ__Senha`...) prevalecem
    por padrão no ASP.NET Core (FR-013).
- **Testes**: a `FabricaApi` passa valores de teste via `UseSetting`, para que os testes do
  REQ-01 continuem rodando sem Docker (SC-005).

## R-05. Verificações de saúde

- **Decisão**:
  - Usar as verificações nativas do ASP.NET Core (`AddHealthChecks`, `MapHealthChecks`),
    com duas verificações próprias na Infrastructure:
    - **`VerificacaoSqlServer`**: abre conexão com `Microsoft.Data.SqlClient` no
      catálogo `master`, porque o banco da aplicação só existe a partir do REQ-04, e
      executa `SELECT 1`.
    - **`VerificacaoRabbitMQ`**: abre e fecha uma conexão com `RabbitMQ.Client`
      (`CreateConnectionAsync`).
  - Cada uma tem limite de 5 s, por `CancellationTokenSource` e pelo timeout de conexão
    (FR-018), e ambas levam a tag `pronto`.
  - Endpoints:
    - **`/health`**: `Predicate = _ => false`, então não executa verificações (FR-015).
    - **`/health/pronto`**: só as verificações com a tag `pronto` (FR-016), com um escritor
      de resposta próprio que publica só o nome e o estado de cada dependência (ver
      [contracts/saude.md](./contracts/saude.md)). Healthy dá 200; Unhealthy dá 503.
  - Os dois são `AllowAnonymous` e não entram no OpenAPI, porque `MapHealthChecks` não é
    descrito pelo ApiExplorer (FR-017).
- **Pacotes**: `Microsoft.Data.SqlClient` 7.1.1 e `RabbitMQ.Client` 7.2.2. A constituição
  exige o cliente RabbitMQ nativo, e os dois serão usados no REQ-04 e no REQ-06.
- **Alternativas**: `AspNetCore.HealthChecks.SqlServer`/`.Rabbitmq` (Xabaril). Rejeitadas:
  são pacotes de terceiros para poucas linhas de código, e o pacote de RabbitMQ deles
  acompanha versões do cliente que podem conflitar com a do REQ-06.

## R-06. Teste de arquitetura

- **Decisão**: um teste em `PedidoFulfillment.Testes.Integracao`, que já referencia
  indiretamente todos os projetos, junta duas fontes de referências de cada camada e
  compara com a matriz permitida do FR-002. Não precisa de pacote novo.
  - `Assembly.GetReferencedAssemblies()`: o que o código compilado de fato usa.
  - Os `ProjectReference` de cada `.csproj` em `src/`: o compilador omite dos metadados
    referências de projeto não usadas no código, e só com a primeira fonte uma
    referência proibida ainda sem uso passaria despercebida (SC-003).
- **Limite conhecido**: com a ordem linear das camadas, toda referência proibida entre os
  quatro projetos atuais forma um ciclo, que o próprio build já rejeita. O teste cobre as
  violações sem ciclo (por exemplo, depois de alguém remover a referência inversa) e
  projetos futuros; uma função pura de comparação, testada com um mapa sintético, prova a
  detecção sem depender do build.
- **Revisão (2026-10-21, `/speckit-analyze` M1)**: a segunda fonte foi acrescentada
  depois do plano; a decisão original usava só `GetReferencedAssemblies()`.
- **Alternativas**: NetArchTest/ArchUnitNET. Rejeitadas, porque seriam pacote novo para
  uma regra de nível de projeto.

## R-07. Testes que dependem de contêiner

- **Decisão**: o caminho feliz de `/health/pronto` (200 com dependências reais) é testado
  com **Testcontainers** (`Testcontainers.MsSql` e `Testcontainers.RabbitMq`), como pede a
  constituição. Quando o Docker não está disponível, o teste é marcado como ignorado com
  `Assert.Skip` e a mensagem "Docker indisponível", em vez de falhar. Assim a suíte
  continua rodando sem Docker (SC-005).
  - O caminho de falha (503 com dependências fora do ar) não precisa de Docker: a API
    aponta para portas fechadas.
- **Alternativas**: depender do compose rodando durante os testes. Rejeitada, porque é
  frágil e o estado fica compartilhado entre testes.

## R-08. Ambiente desta máquina

- **Constatação (2026-10-21)**: o Docker não está instalado nesta máquina. É pré-requisito
  para o quickstart §2–§4 e para os testes com Testcontainers rodarem de fato.
- **Atualização (2026-10-21, implementação)**: o Docker Desktop 29.9.1 foi instalado, mas o
  daemon não sobe ("Docker Desktop is unable to start"). O `docker compose config` valida o
  arquivo; a subida real e o teste com Testcontainers continuam pendentes.
