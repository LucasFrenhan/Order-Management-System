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
| REQ-10 — Ordem de entrega assíncrona ([spec](specs/010-fulfillment/spec.md)) | Consumidor `entrega` no Worker: para o pedido pago, cria a ordem de entrega (sem transportadora, rastreio nem data de despacho), muda o pedido para `ProntoParaEnvio` e publica `entrega.criada`; pedido cancelado, despachado ou já com ordem é ignorado |
| REQ-11 — Rastreio, despacho e histórico na consulta ([spec](specs/011-rastreio/spec.md)) | A resposta do pedido traz o histórico de status (sem o autor) e a entrega; `PATCH /api/v1/pedidos/{id}/rastreio` (Admin e OperadorLogistica) despacha o pedido pronto com transportadora e código de rastreio, ou corrige o rastreio de um despachado; evolução compatível do contrato v1 |
| REQ-12 — Publicação garantida e reconciliação ([spec](specs/012-publicacao-garantida/spec.md)) | O despachante declara as filas de todos os consumidores antes de publicar e exige rota para os tipos com consumidor (devolvido sem fila, o evento fica pendente); a reconciliação periódica no Worker aplica a regra da etapa aos pedidos parados por evento perdido, inclusive os pagos sem ordem do REQ-10, e cancela os de estoque e pagamento que falham por mais de 24 h |

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

Com o REQ-11, as issues de negócio do roteiro original (#1 a #11) estão entregues: o pedido
vai da criação ao despacho com rastreio. O REQ-12 fechou a perda silenciosa de eventos. Cada
trabalho novo segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
`/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma branch
`feature/req-XX-...`, com a numeração a partir do REQ-13.

| Ordem | Requisito | Origem | Escopo |
|-------|-----------|--------|--------|
| 1 | REQ-13 (a abrir) | Clarify do REQ-09 e do REQ-12 | Cancelamento depois do pagamento: estorno, cancelamento da ordem de entrega e o pedido pago cuja ordem falha sempre na reconciliação (hoje ele segue sendo tentado) |
| 2 | a decidir | Clarify do REQ-12 | Como outros sistemas ficam sabendo dos cancelamentos (não existe evento de cancelamento; o esgotamento da reconciliação cancela sem evento) |
| 3 | REQ-14 (a abrir) | [Pendências futuras de mensageria](specs/006-mensageria/spec.md#pendências-futuras) | Limpeza do outbox, dos eventos processados e das chaves de idempotência; reprocessamento da DLQ |
| 4 | a numerar | [#18](https://github.com/LucasFrenhan/Order-Management-System/issues/18) — SonarQube Cloud | Análise estática de código no CI |

O que os REQ-04 a REQ-12 já deixaram pronto:

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
- **Entrega** (REQ-10): `Pedido.CriarOrdemEntrega`, com a condição `AptoParaOrdemEntrega`;
  a `OrdemEntrega` passa a ser lida com o pedido
  ([consumidor de entrega](specs/010-fulfillment/contracts/consumidor-entrega.md)).
- **Rastreio** (REQ-11): `Pedido.RegistrarRastreio` (despacho, correção ou sem mudança) e
  `OrdemEntrega.CorrigirRastreio`; a política `PoliticaRastreioPedido`; a resposta do pedido
  com `historico` e `entrega` ([contrato do rastreio](specs/011-rastreio/contracts/rastreio-http.md)).
- **Reconciliação** (REQ-12): os consumidores do fluxo implementam `IEtapaFluxoPedido`, a
  regra aplicável sem a mensagem; um consumidor novo entra sozinho na topologia declarada
  pelo despachante ([publicação e reconciliação](specs/012-publicacao-garantida/contracts/publicacao-e-reconciliacao.md)).

Decisões e pontos já conhecidos:

- **Mensageria com RabbitMQ.Client nativo.** As issues #7 a #10 citam MassTransit, mas a
  constituição proíbe frameworks de abstração de mensageria. Retry, DLQ e consumidores são
  implementados sobre o cliente nativo.
- **Publicação garantida** (REQ-12). A cada canal novo, o despachante declara as filas de
  todos os consumidores conhecidos, com a mesma função que eles usam. Os tipos com
  consumidor (`pedido.criado`, `estoque.reservado`, `pagamento.aprovado`) saem com
  `mandatory`: devolvidos sem fila, ficam pendentes com `UltimoErro = "SemRota: ..."` e a
  espera crescente, e o canal seguinte recria a fila. Os demais tipos saem como antes.
- **Reconciliação** (REQ-12). Uma vez por ciclo, só uma instância do Worker (lock de
  aplicação no SQL Server) procura pedidos parados há mais de 10 min nas etapas de estoque,
  pagamento e entrega e aplica a regra do consumidor da etapa, com o mesmo evento de saída.
  Uma falha conta um ciclo, em memória; com 3 ciclos e mais de 24 h parado, o pedido de
  estoque ou de pagamento é cancelado sem evento (o pagamento fica `NaoProcessado`, situação
  interna). O pedido pago cuja ordem falha sempre não é cancelado: espera o REQ-13.
- **Pendências futuras de mensageria**: limpeza do outbox, dos eventos processados e das
  chaves de idempotência e reprocessamento da DLQ (hoje manual, pelo painel), em
  [pendências futuras](specs/006-mensageria/spec.md#pendências-futuras).
- **Mudanças de contrato (Princípio II).** O REQ-11 evoluiu a v1 só com acréscimos: os
  campos `historico` e `entrega` na resposta do pedido e a rota `PATCH .../rastreio`. O
  workflow "Compatibilidade do contrato" (oasdiff) barra mudanças incompatíveis no PR.
- **Status do pedido.** A issue #9 cita "AguardandoEnvio", que não existe no contrato v1;
  o pagamento aprovado leva a `Processando` (REQ-09), e a ordem de entrega leva a
  `ProntoParaEnvio` (REQ-10), onde termina o fluxo automático. O despacho com rastreio leva a
  `Despachado` (REQ-11).
- **Despacho e rastreio** (REQ-11). O despacho cria a ordem de entrega se ela faltar (o que
  também atende aos pedidos pagos sem ordem). Num pedido despachado, a mesma rota corrige
  a transportadora ou o código, sem mudar o status nem a data de despacho. O código é único
  por transportadora, sem diferenciar maiúsculas. A mudança de status do REQ-05 continua
  aceitando `ProntoParaEnvio → Despachado` sem rastreio, que pode ser registrado depois. O
  histórico na consulta mostra o motivo de cada mudança a todos os perfis, inclusive o
  Cliente, e nunca o autor.
- **Ordem de entrega de pedido cancelado.** A operação ainda pode cancelar um pedido
  `ProntoParaEnvio` (o estoque volta); a ordem continua registrada, sem despacho, e o
  cancelamento dela fica para um requisito futuro (REQ-10).
- **Estorno.** Cancelar pela API um pedido com pagamento aprovado devolve o estoque, mas o
  pagamento continua `Aprovado`; o estorno fica para um requisito futuro (clarify do REQ-09).

## Pré-requisitos

- SDK do .NET 10 (`winget install Microsoft.DotNet.SDK.10`)
- Docker com Compose v2, para o [ambiente local](#ambiente-local) (`winget install Docker.DockerDesktop`)

Roteiros de validação: [ambiente](specs/002-setup-infraestrutura/quickstart.md), [autenticação](specs/003-autenticacao-identity/quickstart.md), [domínio e persistência](specs/004-dominio-persistencia/quickstart.md), [pedidos](specs/005-criacao-pedidos/quickstart.md), [mensageria](specs/006-mensageria/quickstart.md), [CI e demonstração](specs/007-ci-demo/quickstart.md), [estoque](specs/008-estoque/quickstart.md), [pagamento](specs/009-pagamento/quickstart.md), [entrega](specs/010-fulfillment/quickstart.md), [rastreio](specs/011-rastreio/quickstart.md) e [publicação garantida e reconciliação](specs/012-publicacao-garantida/quickstart.md).

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
- **Entrega** (REQ-10, [consumidor de entrega](specs/010-fulfillment/contracts/consumidor-entrega.md)):
  o consumidor `entrega` (fila `oms.entrega`) recebe `pagamento.aprovado`, cria a ordem de
  entrega do pedido (sem transportadora, rastreio nem data de despacho), muda o pedido de
  `Processando` para `ProntoParaEnvio` e publica `entrega.criada`
  ([schema](specs/010-fulfillment/contracts/eventos/)), tudo na mesma gravação. Pedido
  cancelado, despachado, sem pagamento aprovado ou já com ordem é ignorado. Pedidos pagos
  antes de `oms.entrega` existir ganham a ordem pela reconciliação do REQ-12.
- **Publicação garantida e reconciliação** (REQ-12, [contrato](specs/012-publicacao-garantida/contracts/publicacao-e-reconciliacao.md)):
  as filas dos consumidores existem antes da primeira publicação, mesmo num broker novo, e
  um evento com consumidor nunca é dado como enviado sem chegar a uma fila. Os pedidos
  parados por evento perdido voltam ao fluxo pela reconciliação, configurada na seção
  `Reconciliacao` do Worker:

  | Chave | Padrão | Faixa |
  |---|---|---|
  | `Habilitada` | `true` | — |
  | `IntervaloCicloSegundos` | 300 | 1 a 86 400 |
  | `LimiteParadoSegundos` | 600 | 1 a 604 800 |
  | `LimiteEsgotamentoSegundos` | 86 400 | maior que `LimiteParadoSegundos`, até 2 592 000 |
  | `CiclosFalhaEsgotamento` | 3 | 1 a 100 |
  | `TamanhoLote` | 100 | 1 a 1 000 |

  Fora das faixas, o Worker não sobe. Os logs 9001 a 9007 mostram cada pedido recuperado
  ou esgotado e o resumo de cada ciclo.

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
