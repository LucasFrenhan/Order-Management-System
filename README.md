# Order Management System (OMS)

[![build-testes](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml/badge.svg?event=pull_request)](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml)

API de gestão de pedidos em .NET 10, desenvolvida com **Specification-Driven Development**
(Spec Kit). Toda funcionalidade nasce de uma especificação aprovada em `specs/`, e a
constituição do projeto fica em [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

Os fluxos de ponta a ponta (autenticação, pedido, estoque, pagamento, entrega, despacho,
mensageria e reconciliação), com o caminho feliz, os erros tratados e as falhas externas,
estão em [`docs/fluxos/`](docs/fluxos/README.md).

## Estado atual

Os REQ-01 a REQ-17 estão entregues: o pedido vai da criação ao despacho com rastreio, com
estoque, pagamento e entrega assíncronos, publicação garantida, estorno, limpeza e reenvio da
DLQ, rastreabilidade de ponta a ponta e recuperação dos consumidores. O que cada REQ entregou,
com o link para a spec, está em [Estado atual](docs/estado-atual.md).

## Demonstração rápida

Só com Docker (sem o SDK do .NET), um comando sobe banco, broker, API e Worker:

```powershell
docker compose --profile demo up -d --wait --build   # primeira vez constrói as imagens
docker compose --profile demo down                   # para; os dados continuam nos volumes
```

| O quê | Endereço |
|-------|----------|
| API | <http://localhost:5230> |
| Prontidão | <http://localhost:5230/health/pronto> |
| Contrato navegável | <http://localhost:5230/scalar/v1> |
| Prontidão do Worker | <http://localhost:5231/health/pronto> |
| Painel do RabbitMQ | <http://localhost:15672> (`oms` / `oms-dev-local`) |
| Painel de telemetria (traces, métricas e logs) | <http://localhost:18888> (sem login; só local) |

Usuários **somente de desenvolvimento**: `admin@oms.local` / `AdminDev2026` (Admin) e
`operador@oms.local` / `OperadorDev2026` (OperadorLogistica). Clientes se registram em
`POST /api/v1/auth/register`. A API roda em Development: aplica as migrations e cria o
catálogo inicial (`CAT-001` a `CAT-010`).

- **Portas 5230 e 5231**: são as mesmas da API e do Worker rodando pelo SDK; não rode os
  dois modos juntos, ou troque as da demonstração com `OMS_API_PORTA` e `OMS_WORKER_PORTA`
  no `.env` (veja `.env.example`).
- **Dados**: a demonstração usa os mesmos volumes do [ambiente local](#ambiente-local).
  `docker compose --profile demo down -v` remove os serviços **e apaga o banco local de
  desenvolvimento**.

## Próximos passos

As issues de negócio do roteiro original (#1 a #11) já foram entregues, e o próximo trabalho
começa no REQ-18. A fila de requisitos, o que os REQs anteriores deixaram pronto para
reaproveitar e as decisões já tomadas estão em [Próximos passos](docs/proximos-passos.md).

## Pré-requisitos

- SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`)
- Docker com Compose v2, para o [ambiente local](#ambiente-local) (`winget install Docker.DockerDesktop`)

Roteiros de validação: [ambiente](specs/002-setup-infraestrutura/quickstart.md), [autenticação](specs/003-autenticacao-identity/quickstart.md), [domínio e persistência](specs/004-dominio-persistencia/quickstart.md), [pedidos](specs/005-criacao-pedidos/quickstart.md), [mensageria](specs/006-mensageria/quickstart.md), [CI e demonstração](specs/007-ci-demo/quickstart.md), [estoque](specs/008-estoque/quickstart.md), [pagamento](specs/009-pagamento/quickstart.md), [entrega](specs/010-fulfillment/quickstart.md), [rastreio](specs/011-rastreio/quickstart.md), [publicação garantida e reconciliação](specs/012-publicacao-garantida/quickstart.md), [cancelamento com estorno](specs/013-cancelamento-com-estorno/quickstart.md), [limpeza e reenvio da DLQ](specs/014-limpeza-e-dlq/quickstart.md), [cancelamentos e estornos para outros sistemas](specs/015-visibilidade-cancelamentos/quickstart.md), [rastreabilidade e métricas](specs/016-observabilidade-opentelemetry/quickstart.md) e [recuperação dos consumidores](specs/017-recuperacao-consumidores/quickstart.md).

## Ambiente local

O banco (SQL Server 2022) e o broker (RabbitMQ) rodam em contêineres; a API roda pelo SDK.
Pré-requisito: Docker com Compose v2 (`docker compose version`; no Windows,
`winget install Docker.DockerDesktop`).

```powershell
docker compose up -d --wait   # retorna só quando os dois serviços estão saudáveis
docker compose ps             # sqlserver e rabbitmq aparecem como "healthy"
docker compose down           # para; os dados continuam nos volumes
docker compose down -v        # para e apaga os dados locais (recomeçar do zero)
```

| Dependência | Endereço local | Credenciais de desenvolvimento |
|-------------|----------------|--------------------------------|
| SQL Server | `localhost,1433` | usuário `sa`, senha `OmsDev_2026!Local` |
| RabbitMQ (AMQP), Worker | `localhost:5672` | usuário `oms`, senha `oms-dev-local` |
| RabbitMQ (AMQP), API | `localhost:5672` | usuário `oms-api`, senha `oms-api-dev-local`; só conecta para a prontidão (REQ-14) |
| Painel do RabbitMQ | <http://localhost:15672> | usuário `oms`, senha `oms-dev-local` |

Essas credenciais valem **só para os contêineres locais** e não são segredos reais. O broker
sobe pelo script `docker/rabbitmq/iniciar.sh`, que cria o usuário `oms-api` com permissões
`^$` (não configura, não lê e não publica em nada) a cada subida; um ambiente local antigo
ganha o usuário sem apagar o volume. Para conferir:
`docker compose exec rabbitmq rabbitmqctl list_permissions -p /`.

**Porta ocupada ou outra credencial**: copie `.env.example` para `.env` (ignorado pelo Git)
e troque as variáveis `OMS_*`, sem editar arquivos versionados. Ao rodar a API, sobrescreva
também a configuração correspondente, por exemplo
`$env:ConnectionStrings__SqlServer = "Server=localhost,14330;..."`.

## Compilar, testar e executar

```powershell
dotnet build PedidoFulfillment.sln
dotnet test PedidoFulfillment.sln

$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API
```

A API lê `ConnectionStrings:SqlServer` e a seção `RabbitMQ` (`Servidor`, `Porta`, `Usuario`,
`Senha`, `VirtualHost`) e as valida na inicialização: sem elas, a API não inicia e a
mensagem nomeia a chave ausente, sem mostrar valores. Em Development os valores vêm de
`appsettings.Development.json` e apontam para o [ambiente local](#ambiente-local). Fora
dele, vêm de variáveis de ambiente ou de um cofre de segredos. As variáveis (por exemplo
`ConnectionStrings__SqlServer`, `RabbitMQ__Senha`) sempre prevalecem sobre os arquivos.

Endereços de saúde, públicos (sem token), em todos os ambientes e fora do contrato v1:

- `/health`: vida; responde 200 enquanto o processo está de pé, sem consultar dependências
- `/health/pronto`: prontidão; verifica banco e broker (até 5 s cada) e responde 200 ou 503,
  com o estado de `sqlserver` e `rabbitmq`; no Worker, também `consumidores`, pronta só com
  todos os consumidores inscritos (REQ-17)

Em Development (e no ambiente `Testes`):

- `/scalar/v1`: interface navegável do contrato
- `/openapi/v1.yaml`: contrato aprovado ([`openapi-v1.yaml`](specs/001-contratos-api/contracts/openapi-v1.yaml))
- `/openapi/v1.json`: documento gerado pelo código (usado no teste de conformidade)

Em produção esses endereços não existem.

### Banco de dados e usuários

Ao iniciar, a API prepara o banco: em Development (e no ambiente `Testes`) cria o banco
`PedidoFulfillment` e aplica as migrations pendentes; depois cria os perfis `Cliente`,
`Admin` e `OperadorLogistica` e os usuários iniciais configurados. Sem o banco no ar, a API
não inicia. Fora de Development ela não altera o esquema e se recusa a iniciar com migration
pendente; aplique as migrations antes, com as mesmas variáveis de ambiente da API:

```powershell
dotnet tool restore
dotnet ef database update --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API
```

`Banco__PrepararNaInicializacao=false` desliga essa preparação (a API sobe sem consultar o
banco). Detalhes em [`contracts/configuracao.md`](specs/003-autenticacao-identity/contracts/configuracao.md).

O banco tem três migrations: `InicialIdentidade` (usuários e perfis), `EntidadesNegocio`
(produtos, pedidos, itens, histórico de status, pagamentos e ordens de entrega) e
`ChavesIdempotencia` (as chaves de idempotência da criação de pedido, guardadas por no
mínimo 24 horas, e a sequência que gera o número `PED-AAAAMMDD-NNNNNN`; ver
[idempotência](specs/005-criacao-pedidos/contracts/idempotencia.md)). Em
Development e `Testes` a preparação também cria um
[catálogo inicial](specs/004-dominio-persistencia/contracts/catalogo-inicial.md) de 10
produtos (`CAT-001` a `CAT-010`), sem alterar produtos que já existem; nos demais ambientes
nenhum produto é criado. Depois de mudar o modelo, gere uma migration e confira que nada
ficou pendente:

```powershell
dotnet ef migrations has-pending-model-changes --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API
```

Para obter um token, registre-se em `POST /api/v1/auth/register` (perfil Cliente) e faça
login em `POST /api/v1/auth/login`. Em Development também existem, **só para
desenvolvimento**, `admin@oms.local` / `AdminDev2026` (Admin) e `operador@oms.local` /
`OperadorDev2026` (OperadorLogistica). Cinco senhas erradas seguidas bloqueiam a conta por
15 minutos.

Roteiro completo de validação: [`quickstart.md`](specs/001-contratos-api/quickstart.md).

## Mensageria

Os eventos de domínio trafegam pelo RabbitMQ, publicados por outbox e consumidos pelo
processo `PedidoFulfillment.Worker` (estoque, pagamento, entrega, reconciliação, estorno,
limpeza e reenvio da DLQ). A topologia, as regras de cada consumidor, as seções de
configuração e os logs estão em [Mensageria](docs/mensageria.md).

Para rodar o Worker localmente, com o [ambiente local](#ambiente-local) no ar e a API já
iniciada (é ela que aplica as migrations):

```powershell
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # http://localhost:5231/health/pronto
```

## Observabilidade

Traces, métricas e logs saem por OTLP (REQ-16, [fluxo 12](docs/fluxos/12-rastreabilidade-e-metricas.md),
[contrato](specs/016-observabilidade-opentelemetry/contracts/telemetria.md)). Sem destino, a
telemetria fica desligada e nada muda.

- **Demonstração**: o perfil `demo` sobe o painel (Aspire Dashboard, em contêiner) em
  <http://localhost:18888>, e a API e o Worker já exportam para ele. Os dados ficam em memória.
- **Pelo SDK**: `docker compose --profile observabilidade up -d` sobe só o painel; rode a API e o
  Worker com `$env:OTEL_EXPORTER_OTLP_ENDPOINT = "http://localhost:4317"`.
- **Outros destinos**, só por configuração: Jaeger (OTLP direto), Grafana Tempo,
  Prometheus/Mimir e Loki (por um OpenTelemetry Collector ou Grafana Alloy) ou Azure Monitor
  (Collector com o exportador `azuremonitor`). Variáveis padrão: `OTEL_EXPORTER_OTLP_ENDPOINT`,
  `OTEL_EXPORTER_OTLP_PROTOCOL`, `OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME`,
  `OTEL_RESOURCE_ATTRIBUTES` e `OTEL_METRIC_EXPORT_INTERVAL`.
- **Seção `Observabilidade`**: `TaxaAmostragem` (1,0) e, no Worker,
  `IntervaloAmostragemOutboxSegundos` (30).
- **Várias instâncias do Worker**: os gauges (`oms.outbox.*` e `oms.mensageria.dlq.mensagens`)
  saem iguais de cada instância; agregue com `max`. Os contadores se somam.

## Integração contínua

O workflow [`build-testes`](.github/workflows/build-testes.yml) roda em todo PR para a
`main`, e não de novo no merge, porque o código que entra na `main` é o mesmo que o PR já
verificou. São dois jobs em paralelo:

- **Build e testes**: compila em Release, com avisos como erros, e roda os testes unitários
  e de integração com SQL Server e RabbitMQ reais (Testcontainers). O resumo da execução
  mostra as contagens por projeto, os testes que falharam e a cobertura de linhas, que é
  só informativa e não conta as migrations nem o código gerado. Os resultados (TRX e
  Cobertura) ficam 14 dias no artefato `resultados-testes`.
- **Demonstração**: sobe o perfil `demo` e confere a prontidão e o login do administrador
  de desenvolvimento.

O check fica vermelho com:
- erro ou aviso de compilação;
- teste falhando ou **ignorado**;
- falta de Docker;
- demonstração que não fica pronta.

No CI, `OMS_EXIGIR_DOCKER=true` faz os testes com banco e broker reais falharem sem Docker,
em vez de serem ignorados. Para reproduzir localmente:

```powershell
$env:OMS_EXIGIR_DOCKER = "true"
dotnet test PedidoFulfillment.sln -c Release --logger "trx;LogFilePrefix=resultado" --results-directory resultados --collect "Code Coverage;Format=cobertura" --settings coverage.runsettings
bash .github/scripts/resumo-testes.sh resultados
```

**Proteção da `main`** (configuração manual, feita por quem administra o repositório):

1. Abra Settings → Branches → Add branch ruleset (ou "Add rule") para `main`.
2. Marque "Require status checks to pass".
3. Adicione os checks **Build e testes** e **Demonstração**. Eles só aparecem na busca
   depois que o workflow roda pela primeira vez.
4. Se quiser, marque também "Require branches to be up to date before merging" e salve.

## Estrutura

```text
docker-compose.yml                   ambiente local: SQL Server + RabbitMQ; perfil demo com a API
.env.example                         variáveis OMS_* para trocar portas e credenciais locais
.config/dotnet-tools.json            ferramenta local dotnet-ef (migrations)
.github/workflows/                   build-testes (CI) e compatibilidade do contrato
.github/scripts/resumo-testes.sh     resumo de testes e cobertura do CI
coverage.runsettings                 o que entra na cobertura
.dockerignore                        contexto da imagem da API
src/
  PedidoFulfillment.Domain/          entidades e enums: Produtos/, Pedidos/ (pedido, itens,
                                     histórico de status, tabela de transições), Pagamentos/,
                                     Entregas/
  PedidoFulfillment.Application/     DTOs de contrato, contratos de eventos, serviços de
                                     autenticação e de pedidos (portas de persistência,
                                     idempotência, outbox), consumidores de estoque (Estoque/) e
                                     de pagamento (Pagamentos/, com a porta do gateway),
                                     IManipuladorEvento, Resultado,
                                     ConflitoConcorrenciaException
  PedidoFulfillment.Infrastructure/  banco (EF Core + Identity, Persistencia/Configuracoes com o
                                     mapeamento do domínio, armazenamento de pedidos, número do
                                     pedido, migrations, preparação e catálogo inicial),
                                     mensageria (Mensageria/: conexão, topologia, despachante
                                     do outbox, base de consumo), gateway de pagamento
                                     simulado (Pagamentos/), configuração e saúde
  PedidoFulfillment.API/             Minimal APIs (só roteamento), segurança (validação e
                                     emissão de JWT), erros, OpenAPI; Dockerfile da demonstração
  PedidoFulfillment.Worker/          processo dos consumidores (estoque e pagamento) e do despachante do outbox
                                     (só /health e /health/pronto); Dockerfile
tests/
  PedidoFulfillment.Testes.Unitarios/   DTOs, configurações, regras de autenticação, de pedidos,
                                        de estoque, de pagamento e de mensageria, e entidades do domínio
  PedidoFulfillment.Testes.Integracao/  contrato, segurança, erros, serialização, configuração,
                                        saúde, autenticação, persistência, pedidos, mensageria, estoque e pagamento
                                        com SQL Server e RabbitMQ reais (Testcontainers),
                                        schemas de eventos e arquitetura
docs/                                estado atual, próximos passos, mensageria e fluxos/ (fluxos de ponta a ponta)
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```

Direção de dependências, garantida pelo teste de arquitetura: Domain não depende de nenhum
projeto; Application depende de Domain; Infrastructure depende de Application e Domain; API
e Worker dependem de Application, Infrastructure e Domain, e não se referenciam.
