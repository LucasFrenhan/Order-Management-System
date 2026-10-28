# Order Management System (OMS)

[![build-testes](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml/badge.svg?event=pull_request)](https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml)

API de gestão de pedidos em .NET 10, desenvolvida com **Specification-Driven Development**
(Spec Kit). Toda funcionalidade nasce de uma especificação aprovada em `specs/`, e a
constituição do projeto fica em [`.specify/memory/constitution.md`](.specify/memory/constitution.md).

Os fluxos de ponta a ponta (autenticação, pedido, estoque, pagamento, entrega, despacho,
mensageria e reconciliação), com o caminho feliz, os erros tratados e as falhas externas,
estão em [`docs/fluxos/`](docs/fluxos/README.md).

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
| REQ-13 — Cancelamento depois do pagamento, com estorno ([spec](specs/013-cancelamento-com-estorno/spec.md)) | Cancelar um pedido pago deixa o estorno pendente na mesma gravação (com o estoque devolvido e a ordem de entrega sem despacho cancelada, `entrega.canceladaEm` na consulta); o Worker estorna no gateway e tenta de novo sem desistir; a reconciliação passa a esgotar a etapa de entrega; cobrança e estorno idempotentes por pedido |
| REQ-14 — Limpeza de mensageria e reenvio da DLQ ([spec](specs/014-limpeza-e-dlq/spec.md)) | O Worker apaga, em lotes, os eventos enviados, os processados, as chaves de idempotência e os pedidos de reenvio antigos; o Admin pede pela API o reenvio da DLQ de um consumidor, com filtro e limite de 3 reenvios, e o Worker o executa sem perder nem embaralhar mensagens; aviso no log quando a DLQ passa de 10 mensagens; a API passa a usar um usuário próprio no broker, sem permissões |
| REQ-15 — Cancelamentos e estornos visíveis para outros sistemas ([spec](specs/015-visibilidade-cancelamentos/spec.md)) | Todo cancelamento, por qualquer caminho, grava `pedido.cancelado` na mesma gravação, com o motivo em categoria, a etapa esgotada e o que foi desfeito junto; a confirmação do estorno grava `pagamento.estornado`; os dois são acréscimos à v1, com schema, sem consumidor interno e sem dados pessoais; uma guarda na gravação recusa cancelamento ou estorno sem evento |
| REQ-16 — Rastreabilidade de ponta a ponta do pedido ([spec](specs/016-observabilidade-opentelemetry/spec.md)) | OpenTelemetry com exportação OTLP, ligado só com destino: o pedido aparece num único trace da rota `POST` até o consumidor de entrega, pelo outbox (coluna `ContextoRastreio`) e pelo RabbitMQ (`traceparent` do cliente nativo), com tentativas, DLQ e reenvio; rotinas com trace por rodada e link ao pedido; logs com trace e span, sem mudar os IDs; seis métricas (outbox, DLQ, consumo, rotinas e limpeza); painel local Aspire Dashboard no compose |
| REQ-17 — Recuperação dos consumidores depois de o broker reiniciar ([spec](specs/017-recuperacao-consumidores/spec.md)) | Correção da issue #41: a conexão do Worker deixa de usar a recuperação automática do RabbitMQ.Client, que era descartada no meio pelo despachante; cada consumidor tem um supervisor que refaz a inscrição depois de queda do broker, conexão fechada à força, canal fechado ou fila apagada (0, 1, 2, 4, 8 e 10 s; volta em até 1 min), com ack pela sessão que entregou; prontidão `consumidores` no Worker; logs 5016 a 5019; métricas de inscrição e de reinscrições |

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

Com o REQ-11, as issues de negócio do roteiro original (#1 a #11) estão entregues: o pedido
vai da criação ao despacho com rastreio. O REQ-12 fechou a perda silenciosa de eventos, e o
REQ-13 garante que o Cliente nunca fica cobrado por um pedido cancelado. O REQ-14 limpou os
dados de mensageria que cresciam sem limite e trocou o painel do broker por um reenvio da DLQ
controlado, e o REQ-15 tornou os cancelamentos e os estornos visíveis para outros sistemas. O
REQ-16 trouxe a rastreabilidade de ponta a ponta do pedido, com métricas da mensageria, e o
REQ-17 fez os consumidores voltarem sozinhos depois de o broker reiniciar (issue #41).
Cada trabalho novo segue o fluxo SDD (`/speckit-specify` → `/speckit-clarify` →
`/speckit-plan` → `/speckit-tasks` → `/speckit-analyze` → `/speckit-implement`), em uma
branch `feature/req-XX-...`, com a numeração a partir do REQ-18.

| Ordem | Requisito | Origem | Escopo |
|-------|-----------|--------|--------|
| 1 | a numerar | [#18](https://github.com/LucasFrenhan/Order-Management-System/issues/18) — SonarQube Cloud | Análise estática de código no CI |

O que os REQ-04 a REQ-17 já deixaram pronto:

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
- **Cancelamento e estorno** (REQ-13): `Pedido.Cancelar` é o único caminho de cancelamento
  (estoque, estorno pendente e ordem juntos); `IGatewayPagamento.EstornarAsync` e o
  `ServicoEstornos` ([estorno e cancelamento](specs/013-cancelamento-com-estorno/contracts/estorno-e-cancelamento.md)).
- **Administração da mensageria** (REQ-14): a política `PoliticaAdministracao` (só Admin), o
  `PedidoReenvioDlq` e a lista `ConsumidoresConhecidos`, que um consumidor novo precisa ganhar
  ([limpeza e reenvio](specs/014-limpeza-e-dlq/contracts/limpeza-e-reenvio.md)).
- **Eventos de cancelamento e estorno** (REQ-15): `EventosPedido.Cancelado` monta o
  `pedido.cancelado` de qualquer caminho, a partir do `ResultadoCancelamento`; um caminho novo
  de cancelamento precisa adicioná-lo, ou a guarda do `ArmazenamentoPedidos` recusa a gravação
  ([contrato](specs/015-visibilidade-cancelamentos/contracts/eventos-cancelamento-e-estorno.md)).
- **Recuperação do consumo** (REQ-17): cada consumidor registrado com `AdicionarConsumidor`
  ganha um `SupervisorInscricao`, que refaz a inscrição depois de qualquer perda, e entra na
  prontidão `consumidores` do Worker; a conexão não usa a recuperação automática do cliente
  ([contrato](specs/017-recuperacao-consumidores/contracts/recuperacao-consumo.md)).

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
  estoque ou de pagamento é cancelado (o pagamento fica `NaoProcessado`, situação interna),
  com o `pedido.cancelado` do REQ-15. Desde o REQ-13, o pedido pago cuja ordem falha sempre também se esgota: é
  cancelado com o estorno pendente.
- **Estorno assíncrono** (REQ-13). Cancelar um pedido pago não chama o gateway: o pagamento
  passa a `EstornoPendente` na mesma gravação, e o `ProcessadorEstornos` do Worker estorna a
  cada 1 min (uma instância por rodada, pelo lock de aplicação), tentando de novo sem
  desistir; depois de 24 h e 3 tentativas, cada falha vira um erro no log (7008). As
  situações de estorno são internas. Pedidos cancelados com pagamento aprovado antes do
  REQ-13 não são estornados sozinhos ([quickstart](specs/013-cancelamento-com-estorno/quickstart.md#5-pedidos-pagos-e-cancelados-antes-do-req-13)).
- **Idempotência no gateway** (REQ-13). O pedido é a chave da cobrança e do estorno: a mesma
  chamada repetida devolve o mesmo resultado. O simulado guarda as chaves em memória; um
  gateway real as guarda do lado dele.
- **Limpeza e reenvio da DLQ** (REQ-14). O Worker apaga, em lotes e com uma instância por
  rodada, os eventos enviados e os processados com mais de 7 dias, as chaves de idempotência
  com mais de 48 h (nunca antes de 24 h) e os pedidos de reenvio finais com mais de 90 dias.
  O reenvio da DLQ é pedido pela API, só pelo Admin, e executado pelo Worker: as mensagens
  selecionadas voltam à fila original com as tentativas zeradas, as outras ficam na posição
  original, e cada mensagem é reenviada no máximo 3 vezes. A API não fala com o broker além da
  prontidão, e com o usuário próprio `oms-api`, sem permissões
  ([fluxo 11](docs/fluxos/11-limpeza-e-reenvio-da-dlq.md)).
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
- **Ordem de entrega de pedido cancelado** (REQ-13). Cancelar um pedido `ProntoParaEnvio`
  cancela também a ordem (`entrega.canceladaEm`), que não pode mais ser despachada.
- **Um evento só de cancelamento** (REQ-15). `pedido.cancelado` sai em todo cancelamento, com o
  motivo em categoria (sem o texto digitado, que pode ter dados pessoais), inclusive junto do
  `estoque.insuficiente`. O pedido em `PagamentoRejeitado` não é cancelamento e continua
  avisado só pelo `pagamento.recusado`. O estorno confirmado sai em `pagamento.estornado`, com
  os identificadores do gateway. Uma guarda na gravação do pedido recusa, com erro, um
  cancelamento ou um estorno sem o evento, para nenhum caminho novo cancelar em silêncio.

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
- **Estorno** (REQ-13, [contrato](specs/013-cancelamento-com-estorno/contracts/estorno-e-cancelamento.md),
  [fluxo](docs/fluxos/10-estorno.md)): o `ProcessadorEstornos` do Worker estorna no gateway os
  pedidos pagos que foram cancelados, configurado na seção `Estornos`:

  | Chave | Padrão | Faixa |
  |---|---|---|
  | `Habilitado` | `true` | — |
  | `IntervaloSegundos` | 60 | 1 a 3 600 |
  | `TamanhoLote` | 100 | 1 a 1 000 |
  | `LimiteAlertaSegundos` | 86 400 | 1 a 2 592 000 |
  | `TentativasAlerta` | 3 | 1 a 100 |

  Fora das faixas, o Worker não sobe. Os logs 7005 a 7012 mostram cada estorno pedido,
  confirmado ou com falha e o resumo de cada rodada.
- **Limpeza e reenvio da DLQ** (REQ-14, [contrato](specs/014-limpeza-e-dlq/contracts/limpeza-e-reenvio.md),
  [fluxo](docs/fluxos/11-limpeza-e-reenvio-da-dlq.md)): seções `Limpeza` (a cada 1 h, em lotes de
  1 000; enviados e processados por 7 dias, chaves por 48 h, pedidos de reenvio por 90 dias)
  e `ReenvioDlq` (pedidos executados a cada 1 min, até 3 reenvios e 1 000 mensagens por
  pedido; aviso acima de 10 mensagens na DLQ, a cada 5 min). As faixas estão no fluxo 11; fora
  delas, o Worker não sobe. Os logs 5009 a 5015 mostram cada limpeza, reenvio e aviso.
- **Cancelamentos e estornos para outros sistemas** (REQ-15, [contrato e schemas](specs/015-visibilidade-cancelamentos/contracts/eventos-cancelamento-e-estorno.md),
  [fluxo](docs/fluxos/08-publicacao-e-consumo-de-eventos.md#sistemas-de-fora-cancelamentos-e-estornos-req-15)):
  todo cancelamento grava `pedido.cancelado`, com o motivo (`Cliente`, `Operacao`,
  `EstoqueInsuficiente` ou `ReconciliacaoEsgotada`, com a etapa) e o que foi desfeito junto, e
  toda confirmação de estorno grava `pagamento.estornado`. Nenhum consumidor interno os recebe:
  um sistema de fora liga a própria fila a `oms.eventos` com essas chaves.

Para rodar localmente, com o [ambiente local](#ambiente-local) no ar e a API já iniciada
(é ela que aplica as migrations):

```powershell
dotnet run --project src/PedidoFulfillment.Worker --launch-profile http   # http://localhost:5231/health/pronto
```

No [painel do RabbitMQ](http://localhost:15672), a troca `oms.eventos` e as filas aparecem
assim que o Worker sobe. A DLQ é reenviada pelo pedido de um Admin
(`POST /api/v1/admin/dlq/{consumidor}/reenvios`, executado pelo Worker) e só a purga continua
pelo painel ([fluxo 11](docs/fluxos/11-limpeza-e-reenvio-da-dlq.md)).

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
specs/                               especificações SDD (spec, plano, tarefas, contrato)
```

Direção de dependências, garantida pelo teste de arquitetura: Domain não depende de nenhum
projeto; Application depende de Domain; Infrastructure depende de Application e Domain; API
e Worker dependem de Application, Infrastructure e Domain, e não se referenciam.
