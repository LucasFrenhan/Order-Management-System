# Research: CI de build e testes e ambiente de demonstração (REQ-07)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Estrutura do workflow

- **Decisão**: um arquivo, `.github/workflows/build-testes.yml`, com `name: build-testes`:
  - **Gatilhos**: `pull_request` (para `main`) e `push` (em `main`), sem filtro de
    caminhos.
  - **`permissions: contents: read`** no topo (FR-010).
  - **`concurrency`**: `group: build-testes-${{ github.ref }}`, com
    `cancel-in-progress: true` (FR-005).
  - **Jobs paralelos** (clarify), ambos `ubuntu-latest`, sem `needs` entre eles:
    - `testes` (nome exibido "Build e testes");
    - `demo` (nome exibido "Demonstração").
  - **`timeout-minutes: 20`** em cada job. A meta do SC-006 é de 15 minutos; o limite
    evita execuções presas.
- **Justificativa**: dois jobs em paralelo deixam o tempo total próximo do mais lento. A
  proteção da branch pode exigir cada um pelo nome (FR-020).
- **Alternativas**: um workflow por parte. Rejeitada: o selo e a proteção ficam mais
  simples com um só.

## R-02. Ações e versões fixadas por SHA

- **Decisão** (FR-011): as ações oficiais, nas últimas versões estáveis, todas publicadas
  há mais de duas semanas:

  | Ação | Versão | SHA do commit |
  |------|--------|---------------|
  | `actions/checkout` | v7.0.1 | `3d3c42e5aac5ba805825da76410c181273ba90b1` |
  | `actions/setup-dotnet` | v6.0.0 | `a98b56852c35b8e3190ac28c8c2271da59106c68` |
  | `actions/cache` | v6.1.0 | `55cc8345863c7cc4c66a329aec7e433d2d1c52a9` |
  | `actions/upload-artifact` | v7.0.2 | `cf430e030ddbb5b0abf93d22962f4752f3646cd9` |

  O comentário `# vX.Y.Z` fica ao lado, como no workflow de contrato.
- **Justificativa**: só ações da própria organização `actions`. Resumo de testes e de
  cobertura vêm de um script do repositório (R-05), sem ação de terceiros.
- **Alternativas**: `dorny/test-reporter`. Rejeitada: exige `checks: write`, o que fere o
  FR-010, e é de terceiros.

## R-03. Build e cache

- **Decisão**:
  - **SDK**: `setup-dotnet` com `global-json-file: global.json` (FR-003).
  - **Cache**: `actions/cache` de `~/.nuget/packages`, com chave
    `nuget-${{ runner.os }}-${{ hashFiles('global.json', 'Directory.Packages.props', '**/*.csproj') }}`
    e `restore-keys: nuget-${{ runner.os }}-` (FR-004).
  - **Passos**:
    1. `dotnet restore PedidoFulfillment.sln`;
    2. `dotnet build PedidoFulfillment.sln -c Release --no-restore`. Os avisos já são
       erros pelo `Directory.Build.props` (FR-002);
    3. `dotnet test PedidoFulfillment.sln -c Release --no-build`, com os registradores e a
       cobertura da R-04.
- **Justificativa**: `setup-dotnet` com `cache: true` exige `packages.lock.json`, que o
  projeto não usa. A chave pelos arquivos de dependência cumpre o FR-004.

## R-04. Resultados e cobertura sem pacote novo

- **Decisão**:
  - **Resultados**: `--logger "trx;LogFilePrefix=resultado"`, com
    `--results-directory resultados`.
  - **Cobertura**: `--collect "Code Coverage;Format=cobertura"`, com
    `--settings coverage.runsettings`.
    - O coletor `Microsoft.CodeCoverage` já vem como dependência do
      `Microsoft.NET.Test.Sdk`, que os dois projetos referenciam. Nenhum pacote NuGet novo.
    - O `coverage.runsettings`, na raiz, inclui só os módulos `PedidoFulfillment.*`, exceto
      os de teste, e exclui pelo caminho os fontes de `Persistencia/Migracoes/` (clarify).
- **Justificativa**: constituição, que pede justificativa para pacote novo. O formato
  Cobertura é texto e fácil de resumir.
- **Alternativas**: `coverlet.collector`. Rejeitada: pacote novo sem ganho.

## R-05. Resumo no job e falha com testes ignorados

- **Decisão**: o script `.github/scripts/resumo-testes.sh` (bash; só `grep`, `sed` e `awk`
  do runner) roda com `if: always()`:
  - **TRX**: lê os `resultados/**/*.trx` e, por arquivo, os atributos `total`, `passed`,
    `failed` e `notExecuted` de `<Counters>`, mais os `testName` dos `UnitTestResult` com
    `outcome="Failed"`.
  - **Cobertura**: lê os `resultados/**/*.cobertura.xml`. Soma `lines-covered` e
    `lines-valid` da raiz de cada arquivo e calcula o percentual.
  - **Resumo**: escreve em `$GITHUB_STEP_SUMMARY` uma tabela por projeto, os testes que
    falharam e a cobertura (FR-008).
  - **Saída**: código 1 se algum `notExecuted > 0` (FR-007), com a mensagem
    "Há testes ignorados no CI".

  Em seguida, `upload-artifact` (`if: always()`, `retention-days: 14`) publica a pasta
  `resultados` (FR-009).
- **Justificativa**: o skip é verificado duas vezes. A fixture falha sem Docker (R-06), e
  o resumo recusa qualquer teste ignorado, por qualquer motivo.
- **Alternativas**: `dotnet tool` ReportGenerator. Rejeitada: ferramenta nova só para
  somar dois atributos.

## R-06. `OMS_EXIGIR_DOCKER` nas fixtures

- **Decisão**:
  - **Regra**: `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/AmbienteDocker.cs`,
    com:
    - `public static bool Obrigatorio(string? valor) => string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase)`;
    - `public static bool Obrigatorio() => Obrigatorio(Environment.GetEnvironmentVariable("OMS_EXIGIR_DOCKER"))`;
    - `public const string MensagemObrigatorio = "Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true)."`.
  - **Fixtures**: nas duas que hoje capturam `DockerUnavailableException`
    (`BancoDadosTeste` e os contêineres de `SaudeComDependenciasTestes`), quando
    `Obrigatorio()` for verdadeiro, lançar
    `new InvalidOperationException(MensagemObrigatorio, excecao)` em vez de marcar
    `DockerDisponivel = false`. A falha na fixture reprova todos os testes da coleção, com
    a mensagem (FR-006).
  - **Teste**: um teste unitário de `Obrigatorio(string?)` (`"true"`, `"TRUE"`, `null`,
    `"false"`, `"1"`).
- **Justificativa**: uma regra só, nas duas fixtures. Localmente, sem a variável, nada
  muda (US2, cenário 3).
- **Alternativas**: trocar `Assert.SkipUnless` em cada um dos 15 arquivos. Rejeitada: mais
  pontos de mudança.

## R-07. Imagem da API

- **Decisão**: `src/PedidoFulfillment.API/Dockerfile`, com contexto na raiz do
  repositório:
  - **Compilação**: `mcr.microsoft.com/dotnet/sdk:10.0.402-noble`.
    1. Copiar `global.json`, `Directory.Build.props`, `Directory.Packages.props` e os
       quatro `.csproj` de `src/`, e rodar `dotnet restore` do projeto da API, numa camada
       que o cache reaproveita.
    2. Copiar `src/` e rodar
       `dotnet publish src/PedidoFulfillment.API -c Release --no-restore -o /app`.
  - **Execução**: `mcr.microsoft.com/dotnet/aspnet:10.0.13-noble`, com
    `ENV ASPNETCORE_HTTP_PORTS=8080`, `EXPOSE 8080`, `USER $APP_UID` (o usuário `app`, sem
    privilégios, que a imagem já traz) e `ENTRYPOINT ["dotnet", "PedidoFulfillment.API.dll"]`
    (FR-013).
  - **Exclusões**: `.dockerignore` na raiz, com `**/bin/`, `**/obj/`, `.git/`,
    `.github/`, `.vs/`, `.vscode/`, `.idea/`, `tests/`, `specs/`, `**/*.user`, `.env` e
    `TestResults/` (FR-014).
  - **Versões**: SDK 10.0.402, a mesma do `global.json`, e runtime 10.0.13, a mesma série
    dos pacotes ASP.NET. As duas tags foram conferidas em `mcr.microsoft.com`.
- **Justificativa**: a imagem de execução fica pequena, sem o SDK, e roda sem
  privilégios.
- **Alternativas**: imagem `chiseled`. Rejeitada: não tem shell, e o healthcheck da R-08
  usa bash.

## R-08. Healthcheck sem `curl`

- **Decisão**: `healthcheck` do serviço `api` com bash e `/dev/tcp`, sem pacote extra:

  ```text
  bash -c 'exec 3<>/dev/tcp/127.0.0.1/8080 && printf "GET /health/pronto HTTP/1.0\r\nHost: localhost\r\n\r\n" >&3 && head -1 <&3 | grep -q " 200 "'
  ```

  Parâmetros: `interval: 10s`, `timeout: 5s`, `retries: 12`, `start_period: 30s`.
- **Justificativa**: a imagem `aspnet` (Ubuntu Noble) tem bash. A issue pediu para definir
  essa alternativa no plano.
- **Alternativas**:
  - Instalar `curl` na imagem. Rejeitada: pacote e superfície a mais.
  - Um verificador em .NET. Rejeitada: código novo para um teste de TCP.

## R-09. Serviço `api` no Compose

- **Decisão**: em `docker-compose.yml`:

  ```text
  api:
    profiles: [demo]
    build: { context: ., dockerfile: src/PedidoFulfillment.API/Dockerfile }
    depends_on: { sqlserver: { condition: service_healthy }, rabbitmq: { condition: service_healthy } }
    environment:
      ASPNETCORE_ENVIRONMENT: Development
      ConnectionStrings__SqlServer: Server=sqlserver,1433;Database=PedidoFulfillment;User Id=sa;Password=${OMS_SQL_SENHA:-OmsDev_2026!Local};Encrypt=True;TrustServerCertificate=True
      RabbitMQ__Servidor: rabbitmq
      RabbitMQ__Porta: "5672"
      RabbitMQ__Usuario: ${OMS_RABBITMQ_USUARIO:-oms}
      RabbitMQ__Senha: ${OMS_RABBITMQ_SENHA:-oms-dev-local}
    ports: ["${OMS_API_PORTA:-5230}:8080"]
    healthcheck: (R-08)
  ```

  A JWT, os usuários iniciais e o restante vêm do `appsettings.Development.json`. Nenhum
  segredo novo (FR-016, FR-017).
- **Portas**: dentro da rede do Compose, os serviços usam as portas internas (1433 e
  5672). As variáveis `OMS_*_PORTA` só mudam as portas publicadas no host.
- **Senha com `$`**: se `OMS_SQL_SENHA` tiver `$`, o Compose a interpola. O
  `.env.example` avisa.
- **Justificativa**: a API sobe só depois que as dependências estão saudáveis e aplica as
  migrations em Development, como no desenvolvimento local.

## R-10. Job de demonstração

- **Decisão**: passos do job `demo`:
  1. `actions/checkout`.
  2. `docker compose --profile demo up -d --wait --wait-timeout 300 --build`.
  3. `curl -fsS http://localhost:5230/health/pronto`.
  4. Login de `admin@oms.local` com `curl` e conferência de que a resposta contém
     `tokenAcesso`.
  5. Se algo falhar (`if: failure()`), `docker compose --profile demo logs api`.
  6. Sempre (`if: always()`), `docker compose --profile demo down -v`.

  O tempo de subida é registrado no resumo (SC-005, como referência).
- **Justificativa**: cobre a US5 e o FR-019. O `--wait` usa o healthcheck da R-08.

## R-11. README

- **Decisão**:
  - **Selo**: no topo,
    `[![build-testes](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml/badge.svg?branch=main)](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml)`.
  - **"Demonstração rápida"**: depois do "Estado atual", com:
    - os comandos;
    - os endereços: API em `http://localhost:5230`, contrato em `/scalar/v1` e painel do
      broker em `http://localhost:15672`;
    - os usuários de desenvolvimento;
    - o aviso de porta compartilhada com a API rodando pelo SDK;
    - o comando de encerramento.
  - **"Integração contínua"**: o que roda, `OMS_EXIGIR_DOCKER` e o passo a passo da
    proteção da `main` (Settings → Branches → regra para `main` → exigir "Build e testes"
    e "Demonstração").
  - **`.env.example`**: ganha `OMS_API_PORTA=5230`.

## R-12. Pacotes e ferramentas

- **Decisão**: nenhum pacote NuGet, ferramenta `dotnet` ou ação de terceiros novos.
  Imagens base novas: `dotnet/sdk:10.0.402-noble` e `dotnet/aspnet:10.0.13-noble`,
  oficiais da Microsoft.
