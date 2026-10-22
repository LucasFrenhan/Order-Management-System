# Order Management System (OMS)

[![build-testes](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml/badge.svg?branch=main)](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml)

API de gestão de pedidos em .NET 10, desenvolvida com **Specification-Driven Development**
(Spec Kit). Toda funcionalidade nasce de uma especificação aprovada em `specs/`, e a
constituição do projeto fica em [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

## Estado atual

| Requisito | Situação |
|-----------|----------|
| REQ-01 — Contratos da API v1 ([spec](specs/001-contratos-api/spec.md)) | Contrato publicado; rotas mapeadas com validação, autorização e erros RFC 7807 |
| REQ-02 — Estrutura da solução e ambiente local ([spec](specs/002-setup-infraestrutura/spec.md)) | Infrastructure, ambiente local (SQL Server + RabbitMQ) e saúde |
| REQ-03 — Autenticação e autorização ([spec](specs/003-autenticacao-identity/spec.md)) | Registro, login com JWT, bloqueio após 5 erros, perfis e usuários iniciais |
| REQ-04 — Domínio de pedidos e persistência ([spec](specs/004-dominio-persistencia/spec.md)) | Produtos, pedidos, itens, histórico de status, pagamentos e ordens de entrega; concorrência otimista em produto e pedido; catálogo inicial em Development |
| REQ-05 — Criação, consulta e status de pedidos ([spec](specs/005-criacao-pedidos/spec.md)) | Criação com idempotência por `X-Idempotency-Key`, consulta e mudança de status de pedidos, conforme o contrato v1 |
| REQ-07 — CI e ambiente de demonstração ([spec](specs/007-ci-demo/spec.md)) | Verificação `build-testes` em todo PR (build, testes com Docker obrigatório, cobertura, demonstração) e perfil `demo` que sobe a API com um comando |
| REQ-06 e demais | Pendentes; ver [Próximos passos](#próximos-passos) |

## Demonstração rápida

Só com Docker (sem o SDK do .NET), um comando sobe banco, broker e API:

```powershell
docker compose --profile demo up -d --wait --build   # primeira vez constrói a imagem da API
docker compose --profile demo down                   # para; os dados continuam nos volumes
```

| O quê | Endereço |
|-------|----------|
| API | <http://localhost:5230> |
| Prontidão | <http://localhost:5230/health/pronto> |
| Contrato navegável | <http://localhost:5230/scalar/v1> |
| Painel do RabbitMQ | <http://localhost:15672> (`oms` / `oms-dev-local`) |

Usuários **somente de desenvolvimento**: `admin@oms.local` / `AdminDev2026` (Admin) e
`operador@oms.local` / `OperadorDev2026` (OperadorLogistica). Clientes se registram em
`POST /api/v1/auth/register`. A API roda em Development: aplica as migrations e cria o
catálogo inicial (`CAT-001` a `CAT-010`).

- **Porta 5230**: é a mesma da API rodando pelo SDK; não rode as duas juntas, ou troque a
  da demonstração com `OMS_API_PORTA` no `.env` (veja `.env.example`).
- **Dados**: a demonstração usa os mesmos volumes do [ambiente local](#ambiente-local).
  `docker compose --profile demo down -v` remove os três serviços **e apaga o banco local
  de desenvolvimento**.

## Próximos passos

Issues abertas que formam o escopo restante, na ordem sugerida de implementação. Cada uma
segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
`/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma branch
`feature/req-XX-...`. A numeração de requisito a partir do REQ-08 é definida quando cada
especificação começa.

| Ordem | Requisito | Issue | Escopo | Depende de |
|-------|-----------|-------|--------|------------|
| 1 | REQ-06 | [#7](https://github.com/LucasFrenhan/Order-Management-System/issues/7) — Mensageria, publicação de eventos e DLQ | Eventos `PedidoCriado`, `EstoqueReservado`, `PagamentoAprovado` e `PagamentoRecusado`; publicação após gravar o pedido (outbox, Princípio IV); retry com intervalo exponencial e DLQ | REQ-05 |
| 2 | a numerar | [#8](https://github.com/LucasFrenhan/Order-Management-System/issues/8) — Worker de estoque e concorrência otimista | Consome `PedidoCriado`, reserva estoque tratando `ConflitoConcorrenciaException`; sem estoque, cancela o pedido e publica `EstoqueInsuficiente` | REQ-06 |
| 3 | a numerar | [#9](https://github.com/LucasFrenhan/Order-Management-System/issues/9) — Worker de pagamento assíncrono | Consome `EstoqueReservado`, simula o gateway (90% aprova); aprovado → `Processando`; recusado → `PagamentoRejeitado` e compensação do estoque | estoque |
| 4 | a numerar | [#10](https://github.com/LucasFrenhan/Order-Management-System/issues/10) — Worker de fulfillment e logística | Consome `PagamentoAprovado`, cria a `OrdemEntrega`, muda o pedido para `ProntoParaEnvio` e publica o evento de despacho | pagamento |
| 5 | a numerar | [#11](https://github.com/LucasFrenhan/Order-Management-System/issues/11) — Rastreio e histórico na consulta | A consulta com dono validado pelo token já existe (REQ-05). Faltam o histórico de status na resposta do `GET /api/v1/pedidos/{id}` e o `PATCH /api/v1/pedidos/{id}/rastreio` (Admin e OperadorLogistica) para registrar o rastreio e despachar | fulfillment |

O que os REQ-04 e REQ-05 já deixaram pronto para esses requisitos:

- **Armazenamento**: produtos, pedidos com itens e histórico de status, pagamentos e ordens
  de entrega, com as restrições no banco.
- **Concorrência otimista** em produto e pedido, com `ConflitoConcorrenciaException` para
  os serviços e workers tratarem sem depender do EF Core
  ([contrato de persistência](specs/004-dominio-persistencia/contracts/persistencia.md)).
- **Catálogo inicial** com casos reservados para testes: `CAT-008` (estoque 1, disputa do
  último item), `CAT-009` (sem estoque) e `CAT-010` (inativo).
- **Serviço de pedidos** (REQ-05): criação idempotente, consulta e a tabela de transições
  de status no domínio (`TransicoesStatusPedido`), que os workers podem reaproveitar. A
  criação ainda não publica o evento `PedidoCriado`; isso entra no REQ-06.

Decisões e pontos já conhecidos:

- **Mensageria com RabbitMQ.Client nativo.** As issues #7 a #10 citam MassTransit, mas a
  constituição proíbe frameworks de abstração de mensageria. Retry, DLQ e consumidores são
  implementados sobre o cliente nativo.
- **Mudanças de contrato (Princípio II).** A rota `PATCH /api/v1/pedidos/{id}/rastreio` e o
  histórico de status na consulta (#11) não existem no contrato v1. Eles exigem evolução
  compatível do contrato ou uma versão nova, decidida na especificação.
- **Status do pedido.** A issue #9 cita "AguardandoEnvio", que não existe no contrato v1;
  o pagamento aprovado leva a `Processando`.

## Pré-requisitos

- SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`)
- Docker com Compose v2, para o [ambiente local](#ambiente-local) (`winget install Docker.DockerDesktop`)

Roteiros de validação: [ambiente](specs/002-setup-infraestrutura/quickstart.md), [autenticação](specs/003-autenticacao-identity/quickstart.md), [domínio e persistência](specs/004-dominio-persistencia/quickstart.md), [pedidos](specs/005-criacao-pedidos/quickstart.md) e [CI e demonstração](specs/007-ci-demo/quickstart.md).

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
| RabbitMQ (AMQP) | `localhost:5672` | usuário `oms`, senha `oms-dev-local` |
| Painel do RabbitMQ | <http://localhost:15672> | usuário `oms`, senha `oms-dev-local` |

Essas credenciais valem **só para os contêineres locais** e não são segredos reais.

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
  com o estado de `sqlserver` e `rabbitmq`

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

## Integração contínua

O workflow [`build-testes`](.github/workflows/build-testes.yml) roda em todo PR para a
`main` e em todo push nela, com dois jobs em paralelo:

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
  PedidoFulfillment.Application/     DTOs de contrato, serviços de autenticação e de pedidos
                                     (com as portas de persistência e a idempotência),
                                     Resultado, ConflitoConcorrenciaException
  PedidoFulfillment.Infrastructure/  banco (EF Core + Identity, Persistencia/Configuracoes com o
                                     mapeamento do domínio, armazenamento de pedidos, número do
                                     pedido, migrations, preparação e catálogo inicial),
                                     configuração e saúde de banco e broker; a
                                     mensageria (REQ-06) entra aqui
  PedidoFulfillment.API/             Minimal APIs (só roteamento), segurança (validação e
                                     emissão de JWT), erros, OpenAPI, saúde; Dockerfile da
                                     demonstração
tests/
  PedidoFulfillment.Testes.Unitarios/   DTOs, configurações, regras de autenticação e de pedidos
                                        e entidades do domínio
  PedidoFulfillment.Testes.Integracao/  contrato, segurança, erros, serialização, configuração,
                                        saúde, autenticação, persistência e pedidos com SQL
                                        Server real (Testcontainers) e arquitetura
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```

Direção de dependências, garantida pelo teste de arquitetura: Domain não depende de nenhum
projeto; Application depende de Domain; Infrastructure depende de Application e Domain; API
depende de Application, Infrastructure e Domain.
