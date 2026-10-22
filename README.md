# Order Management System (OMS)

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
| REQ-05 em diante | Pendentes; ver [Próximos passos](#próximos-passos) |

> As operações de pedidos ainda respondem **501 (não implementada)** depois de passar pela
> autorização. As regras chegam com o REQ-05.

## Próximos passos

Issues abertas que formam o escopo restante, na ordem sugerida de implementação. Cada uma
segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
`/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma branch
`feature/req-XX-...`. A numeração de requisito a partir do REQ-07 é definida quando cada
especificação começa.

| Ordem | Requisito | Issue | Escopo | Depende de |
|-------|-----------|-------|--------|------------|
| 1 | REQ-05 | [#6](https://github.com/LucasFrenhan/Order-Management-System/issues/6) — API de criação de pedidos e idempotência | `POST /api/v1/pedidos` restrito ao Cliente, com `X-Idempotency-Key`; valida produto e preço, grava o pedido `Pendente` com o `UserId` do token; tira o 501 das rotas de pedido | REQ-04 |
| 2 | REQ-06 | [#7](https://github.com/LucasFrenhan/Order-Management-System/issues/7) — Mensageria, publicação de eventos e DLQ | Eventos `PedidoCriado`, `EstoqueReservado`, `PagamentoAprovado` e `PagamentoRecusado`; publicação após gravar o pedido (outbox, Princípio IV); retry com intervalo exponencial e DLQ | REQ-05 |
| 3 | a numerar | [#8](https://github.com/LucasFrenhan/Order-Management-System/issues/8) — Worker de estoque e concorrência otimista | Consome `PedidoCriado`, reserva estoque tratando `ConflitoConcorrenciaException`; sem estoque, cancela o pedido e publica `EstoqueInsuficiente` | REQ-06 |
| 4 | a numerar | [#9](https://github.com/LucasFrenhan/Order-Management-System/issues/9) — Worker de pagamento assíncrono | Consome `EstoqueReservado`, simula o gateway (90% aprova); aprovado → `Processando`; recusado → `PagamentoRejeitado` e compensação do estoque | estoque |
| 5 | a numerar | [#10](https://github.com/LucasFrenhan/Order-Management-System/issues/10) — Worker de fulfillment e logística | Consome `PagamentoAprovado`, cria a `OrdemEntrega`, muda o pedido para `ProntoParaEnvio` e publica o evento de despacho | pagamento |
| 6 | a numerar | [#11](https://github.com/LucasFrenhan/Order-Management-System/issues/11) — Consulta, rastreio e logística | `GET /api/v1/pedidos/{id}` com dono validado pelo token e histórico de status; `PATCH /api/v1/pedidos/{id}/rastreio` (Admin e OperadorLogistica) para registrar o rastreio e despachar | fulfillment |
| — | a numerar | [#15](https://github.com/LucasFrenhan/Order-Management-System/issues/15) — CI de build e testes e ambiente de demonstração | Workflow `build-testes` em todo PR (com Testcontainers e sem skip silencioso), cobertura, `Dockerfile` da API e profile `demo` no Docker Compose | independente; pode entrar a qualquer momento |

O que o REQ-04 já deixou pronto para esses requisitos:

- **Armazenamento**: produtos, pedidos com itens e histórico de status, pagamentos e ordens
  de entrega, com as restrições no banco.
- **Concorrência otimista** em produto e pedido, com `ConflitoConcorrenciaException` para
  os serviços e workers tratarem sem depender do EF Core
  ([contrato de persistência](specs/004-dominio-persistencia/contracts/persistencia.md)).
- **Catálogo inicial** com casos reservados para testes: `CAT-008` (estoque 1, disputa do
  último item), `CAT-009` (sem estoque) e `CAT-010` (inativo).

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

Roteiros de validação: [ambiente](specs/002-setup-infraestrutura/quickstart.md), [autenticação](specs/003-autenticacao-identity/quickstart.md) e [domínio e persistência](specs/004-dominio-persistencia/quickstart.md).

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

O banco tem duas migrations: `InicialIdentidade` (usuários e perfis) e `EntidadesNegocio`
(produtos, pedidos, itens, histórico de status, pagamentos e ordens de entrega). Em
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

## Estrutura

```text
docker-compose.yml                   ambiente local: SQL Server + RabbitMQ
.env.example                         variáveis OMS_* para trocar portas e credenciais locais
.config/dotnet-tools.json            ferramenta local dotnet-ef (migrations)
src/
  PedidoFulfillment.Domain/          entidades e enums: Produtos/, Pedidos/ (pedido, itens,
                                     histórico de status), Pagamentos/, Entregas/
  PedidoFulfillment.Application/     DTOs de contrato, interfaces e serviço de autenticação,
                                     Resultado, ConflitoConcorrenciaException
  PedidoFulfillment.Infrastructure/  banco (EF Core + Identity, Persistencia/Configuracoes com o
                                     mapeamento do domínio, migrations, preparação e catálogo
                                     inicial), configuração e saúde de banco e broker; a
                                     mensageria (REQ-06) entra aqui
  PedidoFulfillment.API/             Minimal APIs (só roteamento), segurança (validação e
                                     emissão de JWT), erros, OpenAPI, saúde
tests/
  PedidoFulfillment.Testes.Unitarios/   DTOs, configurações, regras de autenticação e entidades
                                        do domínio
  PedidoFulfillment.Testes.Integracao/  contrato, segurança, erros, serialização, configuração,
                                        saúde, autenticação e persistência com SQL Server real
                                        (Testcontainers) e arquitetura
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```

Direção de dependências, garantida pelo teste de arquitetura: Domain não depende de nenhum
projeto; Application depende de Domain; Infrastructure depende de Application e Domain; API
depende de Application, Infrastructure e Domain.
