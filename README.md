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
| REQ-06 — Mensageria, publicação de eventos e DLQ ([spec](specs/006-mensageria/spec.md)) | Contratos de eventos v1 com schema, `pedido.criado` publicado por outbox, processo `PedidoFulfillment.Worker` e base de consumo com novas tentativas, DLQ e descarte de duplicados |
| REQ-07 — CI e ambiente de demonstração ([spec](specs/007-ci-demo/spec.md)) | Verificação `build-testes` em todo PR (build, testes com Docker obrigatório, cobertura, demonstração) e perfil `demo` que sobe a API com um comando |
| REQ-08 — Reserva de estoque assíncrona ([spec](specs/008-estoque/spec.md)) | Consumidor `estoque` no Worker: reserva tudo ou nada com concorrência otimista e publica `estoque.reservado`; sem estoque, cancela o pedido e publica `estoque.insuficiente`. O cancelamento pela API devolve o estoque reservado |
| REQ-09 — Pagamento assíncrono simulado ([spec](specs/009-pagamento/spec.md)) | Consumidor `pagamento` no Worker: cobra o pedido com estoque reservado num gateway simulado (90% aprova, configurável); aprovado → `Processando` e `pagamento.aprovado`; recusado → `PagamentoRejeitado`, estoque devolvido na mesma gravação e `pagamento.recusado` |
| Issues #10 e #11 | Pendentes; ver [Próximos passos](#próximos-passos) |

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

Issues abertas que formam o escopo restante, na ordem sugerida de implementação. Cada uma
segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
`/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma branch
`feature/req-XX-...`. A numeração de requisito a partir do REQ-10 é definida quando cada
especificação começa.

| Ordem | Requisito | Issue | Escopo | Depende de |
|-------|-----------|-------|--------|------------|
| 1 | a numerar | [#10](https://github.com/LucasFrenhan/Order-Management-System/issues/10) — Worker de fulfillment e logística | Consome `pagamento.aprovado` (o pedido já chega em `Processando`, REQ-09), cria a `OrdemEntrega`, muda o pedido para `ProntoParaEnvio` e publica o evento de despacho | pagamento |
| 2 | a numerar | [#11](https://github.com/LucasFrenhan/Order-Management-System/issues/11) — Rastreio e histórico na consulta | A consulta com dono validado pelo token já existe (REQ-05). Faltam o histórico de status na resposta do `GET /api/v1/pedidos/{id}` e o `PATCH /api/v1/pedidos/{id}/rastreio` (Admin e OperadorLogistica) para registrar o rastreio e despachar | fulfillment |

O que os REQ-04 a REQ-09 já deixaram pronto para esses requisitos:

- **Armazenamento**: produtos, pedidos com itens e histórico de status, pagamentos e ordens
  de entrega, com as restrições no banco.
- **Concorrência otimista** em produto e pedido, com `ConflitoConcorrenciaException` para
  os serviços e workers tratarem sem depender do EF Core
  ([contrato de persistência](specs/004-dominio-persistencia/contracts/persistencia.md)).
- **Catálogo inicial** com casos reservados para testes: `CAT-008` (estoque 1, disputa do
  último item), `CAT-009` (sem estoque) e `CAT-010` (inativo).
- **Serviço de pedidos** (REQ-05): criação idempotente, consulta e a tabela de transições
  de status no domínio (`TransicoesStatusPedido`), que os workers podem reaproveitar.
- **Mensageria** (REQ-06): cada worker implementa `IManipuladorEvento<T>` no Application e
  se registra no Worker com `AdicionarConsumidor`; fila, novas tentativas, DLQ e
  duplicados ficam com a base. Para publicar, grava um `EventoSaida` (outbox). Guia em
  [topologia e consumo](specs/006-mensageria/contracts/topologia-e-consumo.md).
- **Estoque** (REQ-08): `Pedido.ReservarEstoque` e `Pedido.DevolverEstoque`, com a marca
  `EstoqueReservado`; o consumidor `ReservaEstoque` é o exemplo de worker de negócio
  ([consumidor de estoque](specs/008-estoque/contracts/consumidor-estoque.md)).
- **Pagamento** (REQ-09): `Pedido.AprovarPagamento` e `Pedido.RecusarPagamento`, com a
  condição `AptoParaPagamento`; a porta `IGatewayPagamento` e o gateway simulado
  ([consumidor de pagamento](specs/009-pagamento/contracts/consumidor-pagamento.md)).

Decisões e pontos já conhecidos:

- **Mensageria com RabbitMQ.Client nativo.** As issues #7 a #10 citam MassTransit, mas a
  constituição proíbe frameworks de abstração de mensageria. Retry, DLQ e consumidores são
  implementados sobre o cliente nativo.
- **Pendências futuras de mensageria**: limpeza do outbox, dos eventos processados e das
  chaves de idempotência, reprocessamento da DLQ (hoje manual, pelo painel) e o evento
  descartado quando é publicado antes de a fila do consumidor existir num broker novo
  (encontrado no REQ-08). Estão
  anotadas em [pendências futuras](specs/006-mensageria/spec.md#pendências-futuras), para
  virar requisito próprio.
- **Mudanças de contrato (Princípio II).** A rota `PATCH /api/v1/pedidos/{id}/rastreio` e o
  histórico de status na consulta (#11) não existem no contrato v1. Eles exigem evolução
  compatível do contrato ou uma versão nova, decidida na especificação.
- **Status do pedido.** A issue #9 cita "AguardandoEnvio", que não existe no contrato v1;
  o pagamento aprovado leva a `Processando` (REQ-09).
- **Estorno.** Cancelar pela API um pedido com pagamento aprovado devolve o estoque, mas o
  pagamento continua `Aprovado`; o estorno fica para um requisito futuro (clarify do REQ-09).

## Pré-requisitos

- SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`)
- Docker com Compose v2, para o [ambiente local](#ambiente-local) (`winget install Docker.DockerDesktop`)

Roteiros de validação: [ambiente](specs/002-setup-infraestrutura/quickstart.md), [autenticação](specs/003-autenticacao-identity/quickstart.md), [domínio e persistência](specs/004-dominio-persistencia/quickstart.md), [pedidos](specs/005-criacao-pedidos/quickstart.md), [mensageria](specs/006-mensageria/quickstart.md), [CI e demonstração](specs/007-ci-demo/quickstart.md), [estoque](specs/008-estoque/quickstart.md) e [pagamento](specs/009-pagamento/quickstart.md).

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

## Mensageria

Os eventos de domínio trafegam pelo RabbitMQ ([spec](specs/006-mensageria/spec.md),
[topologia e consumo](specs/006-mensageria/contracts/topologia-e-consumo.md),
[schemas](specs/006-mensageria/contracts/eventos/)):

- **Publicação por outbox**: a API grava o evento em `EventosSaida` na mesma operação do
  pedido e não fala com o broker. O processo `PedidoFulfillment.Worker` publica os pendentes
  em ordem, com confirmação do broker, na troca `oms.eventos` (`topic`), com o tipo como
  chave (`pedido.criado`). Com o broker fora, os pedidos continuam sendo criados, e os
  eventos saem quando ele volta. A entrega é **pelo menos uma vez**.
- **Consumo**: por consumidor `C`, a fila `oms.C`, as filas de espera
  `oms.C.espera.{1000,2000,4000}` e a DLQ `oms.C.dlq`. Uma falha é tentada de novo até 3
  vezes, com espera crescente e sem bloquear a fila, e depois vai para a DLQ com o motivo
  nos cabeçalhos. Mensagem fora do contrato vai direto para a DLQ. Duplicados são
  descartados por consumidor.
- **Estoque** (REQ-08, [consumidor de estoque](specs/008-estoque/contracts/consumidor-estoque.md)):
  o consumidor `estoque` (fila `oms.estoque`) recebe `pedido.criado` e reserva o estoque
  de todos os itens de uma vez, publicando `estoque.reservado`; se algum item falta, nada é
  abatido, o pedido é cancelado e sai `estoque.insuficiente`
  ([schema](specs/008-estoque/contracts/eventos/)). Conflitos de concorrência no mesmo
  produto são reavaliados com o estoque atual. Cancelar pela API um pedido com estoque
  reservado devolve o estoque na mesma gravação.
- **Pagamento** (REQ-09, [consumidor de pagamento](specs/009-pagamento/contracts/consumidor-pagamento.md)):
  o consumidor `pagamento` (fila `oms.pagamento`) recebe `estoque.reservado` e cobra o valor
  total num gateway simulado, que aprova com a chance de
  `GatewayPagamento:TaxaAprovacaoPercentual` (padrão 90, de 0 a 100; fora disso o Worker não
  sobe). Aprovado, o pedido vai de `Pendente` para `Processando` e sai `pagamento.aprovado`.
  Recusado, o pedido vai para `PagamentoRejeitado` (final), o estoque volta na mesma
  gravação e sai `pagamento.recusado`. Pedido cancelado, em status final ou já pago é
  ignorado.

Para rodar localmente, com o [ambiente local](#ambiente-local) no ar e a API já iniciada
(é ela que aplica as migrations):

```powershell
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # http://localhost:5231/health/pronto
```

No [painel do RabbitMQ](http://localhost:15672), a troca `oms.eventos` e as filas aparecem
assim que o Worker sobe. A DLQ é inspecionada e reenviada à mão pelo painel; a limpeza e o
reprocessamento automáticos estão em
[pendências futuras](specs/006-mensageria/spec.md#pendências-futuras).

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
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```

Direção de dependências, garantida pelo teste de arquitetura: Domain não depende de nenhum
projeto; Application depende de Domain; Infrastructure depende de Application e Domain; API
e Worker dependem de Application, Infrastructure e Domain, e não se referenciam.
