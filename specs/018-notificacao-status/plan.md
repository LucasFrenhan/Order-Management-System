# Implementation Plan: Notificação de mudança de status do pedido (webhooks e SSE)

**Branch**: `feature/req-18-notificacao-status` | **Date**: 2026-11-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/018-notificacao-status/spec.md`

**Requisito**: REQ-18 (GitHub issue #43)

## Summary

- **Um ponto gera tudo**: o `ArmazenamentoPedidos.SalvarAsync`, ao lado da guarda do REQ-15,
  transforma cada entrada nova do histórico de status em um `pedido.status-alterado` no outbox e
  um `AvisoPedido` com o corpo da notificação pronto; o estorno confirmado gera um aviso
  `pedido.estorno-confirmado` (research R-02, R-14).
- **Uma fonte para os dois canais**: a tabela `AvisosPedido`, lida por cursor de `rowversion`
  abaixo de `MIN_ACTIVE_ROWVERSION()`, sem pular aviso de transação lenta (R-13).
- **Webhooks pelo Worker**: o `ProcessadorNotificacoes` distribui os avisos em
  `EntregasNotificacao` (uma por destino) e envia com assinatura Standard Webhooks, novas
  tentativas por 24 h, desativação e proteção contra SSRF (R-05 revista, R-06, R-08).
- **SSE na API, sem broker**: o `LeitorAvisos` de cada instância lê o banco a cada 10 s e o
  `DistribuidorAvisos` entrega às conexões, com `conectado`, reenvio por `Last-Event-ID` (até
  500) e `sincronizacao-necessaria` (R-11 revista, R-16). A API continua sem permissão no broker.
- **Destinos** em `/api/v1/notificacoes/destinos`, de Cliente ou globais da operação, com
  segredo cifrado por AES-GCM (R-04, R-15); **`ETag`/304** no `GET` do pedido (R-17).
- Nenhum pacote novo; uma migration com quatro tabelas; logs 10001–10014 e métricas
  `oms.notificacoes.*` (R-18, R-19).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: ASP.NET Core 10 (Minimal APIs, `TypedResults.ServerSentEvents`,
  `IHttpClientFactory`), EF Core SqlServer 10.0.13, Identity, `RabbitMQ.Client` 7.2.2 (só o
  evento novo no outbox), OpenTelemetry 1.19;
- da biblioteca padrão: `System.Net.ServerSentEvents` (`SseItem<T>`),
  `System.Security.Cryptography` (`AesGcm`, `HMACSHA256`, `RandomNumberGenerator`),
  `SocketsHttpHandler.ConnectCallback`, `System.Threading.Channels`;
- **nenhum pacote novo**.

**Storage**: SQL Server; migration `NotificacaoStatus` com `AvisosPedido`,
`DestinosNotificacao`, `EntregasNotificacao` e `CursorNotificacoes` ([data-model](./data-model.md)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker), Testcontainers
MsSql e RabbitMq, `JsonSchema.Net`; receptor HTTP de teste com Kestrel em porta local.

**Target Platform**: API e Worker em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- mudança de status: dois `INSERT`s a mais na mesma transação (evento e aviso) e, só nos
  caminhos da API, uma consulta aos perfis do autor (SC-007);
- webhook em até 10 s (rodada de 5 s) e SSE em até 12 s (leitura de 10 s) (SC-002);
- leitura do SSE: uma consulta indexada por instância a cada 10 s, independente do número de
  conexões.

**Constraints**:
- contratos v1 só com acréscimos (evento novo, rotas novas, `ETag`/304); nada muda nas rotas e
  nos eventos existentes;
- aviso e evento na mesma transação da mudança (Princípio IV);
- a API não fala com o broker (REQ-14 mantido);
- sem dados pessoais, valores, rastreio, token ou dados de pagamento nos avisos (FR-002);
- nenhum envio HTTP para endereço interno fora de `PermitirRedeLocal` (FR-016).

**Scale/Scope**:
- 4 tabelas, 3 entidades de aplicação e 1 cursor; 1 record de evento, 1 corpo de notificação,
  2 schemas;
- Application: geração dos avisos (fábrica), `ServicoDestinosNotificacao`, portas de leitura;
- Infrastructure: `SalvarAsync` estendido, `ProcessadorNotificacoes`, `EnviadorWebhook`,
  `EnderecosProibidos`, `CifraSegredos`, `LeitorAvisos`, `DistribuidorAvisos`, opções, limpeza;
- API: `EndpointsNotificacoes` (5 rotas de destinos + SSE), `ETag` no `GET`, política nova.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Estudo aprovado (`40c1f30`), specify (`e6fb5e0`) e clarify (`57c3011`) `[REQ-18]`. As divergências do plano em relação ao estudo (R-05 e R-11 revistas) estão registradas no `research.md` e refletidas na spec, para aprovação junto com o plano |
| II. Contratos versionados | ✅ | Evento `pedido.status-alterado` como acréscimo à v1, com schema; corpo `notificacao.v1` com schema próprio; rotas novas e `ETag`/304 como acréscimos ao `openapi-v1.yaml`, conferidos pelo `oasdiff` e pelo teste de conformidade do contrato |
| III. Segurança por padrão | ✅ | Todas as rotas novas exigem token e política (Cliente/Admin para destinos; consulta para o SSE). Validação com `400` em `ProblemDetails`. Segredo mostrado uma vez e guardado cifrado; HMAC no corpo; SSRF no cadastro e no `ConnectCallback`; redirecionamento não seguido; nenhum dado pessoal no aviso; URL nos logs só com esquema e host |
| IV. Desacoplamento assíncrono | ✅ | Aviso e evento gravados na transação da mudança; envio HTTP e SSE fora da rota e do consumo; receptor lento ou fora do ar não afeta API nem Worker |
| V. Qualidade verificável | ✅ | Unitários das regras novas; integração com 401/403/404 das rotas, receptor real, SSE real, transação lenta e broker parado; schemas com conformidade (R-20) |
| RabbitMQ.Client nativo | ✅ | Só o evento novo passa pelo despachante existente; nenhum consumidor novo, nenhuma permissão nova no broker |
| Endpoint só roteia | ✅ | `EndpointsNotificacoes` delega ao `ServicoDestinosNotificacao` e ao `DistribuidorAvisos`; o `ETag` usa a versão devolvida pelo serviço |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `AvisoPedido`, `DestinoNotificacao`, `EntregaNotificacao`, `ProcessadorNotificacoes`, `DistribuidorAvisos`; evento no passado (`pedido.status-alterado`) |
| Branch dedicada | ✅ | `feature/req-18-notificacao-status`, criada da `main` sincronizada depois do merge do PR #44 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/018-notificacao-status/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── api-notificacoes.md                          # rotas de destinos, SSE e ETag
│   ├── webhooks.md                                  # requisição assinada e respostas
│   ├── eventos/pedido.status-alterado.v1.schema.json
│   └── notificacoes/notificacao.v1.schema.json
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Application/
│   ├── Contratos/
│   │   ├── Eventos/Eventos.cs, TiposEvento.cs              # PedidoStatusAlterado, OrigemMudancaStatus
│   │   ├── Notificacoes/                                    # NOVO: corpo notificacao.v1, DTOs de destino e entrega
│   │   └── Erros/CodigosErro.cs                             # 3 códigos novos
│   ├── Notificacoes/                                        # NOVO
│   │   ├── AvisoPedido.cs, DestinoNotificacao.cs, EntregaNotificacao.cs
│   │   ├── FabricaAvisos.cs                                 # evento + aviso a partir do histórico e do estorno
│   │   ├── ServicoDestinosNotificacao.cs                    # cadastro, lista, remoção, reativação, entregas
│   │   ├── EsperaEntregas.cs                                # 30 s … 2 h, limite de 24 h
│   │   └── IArmazenamentoNotificacoes.cs, IVerificadorEndereco.cs, ICifraSegredos.cs
│   └── Pedidos/ServicoPedidos.cs                            # ObterAsync devolve a versão (ETag)
├── PedidoFulfillment.Infrastructure/
│   ├── Persistencia/
│   │   ├── ArmazenamentoPedidos.cs                          # gera avisos e eventos; origem pelos perfis
│   │   ├── ArmazenamentoNotificacoes.cs                     # NOVO
│   │   ├── Configuracoes/Configuracao{AvisoPedido,DestinoNotificacao,EntregaNotificacao,CursorNotificacoes}.cs
│   │   └── Migracoes/…_NotificacaoStatus.cs
│   ├── Mensageria/ConsumidorEventos.cs                      # recusa histórico novo fora do SalvarAsync (R-14)
│   ├── Mensageria/LimpezaMensageria.cs                      # avisos e entregas (R-18)
│   ├── Notificacoes/                                        # NOVO
│   │   ├── LeituraAvisos.cs                                 # consulta por cursor com MIN_ACTIVE_ROWVERSION()
│   │   ├── ProcessadorNotificacoes.cs                       # distribuição + envio (Worker)
│   │   ├── EnviadorWebhook.cs, AssinaturaWebhook.cs
│   │   ├── EnderecosProibidos.cs, VerificadorEndereco.cs    # SSRF
│   │   ├── CifraSegredos.cs                                 # AES-GCM
│   │   ├── LeitorAvisos.cs, DistribuidorAvisos.cs           # SSE (API): leitura e distribuição
│   │   ├── FluxoEventosTempoReal.cs                         # SSE: conexão, reenvio, sinal; o endpoint só roteia
│   │   ├── EventosNotificacao.cs                            # logs 10001–10014
│   │   └── ExtensoesNotificacoes.cs
│   ├── Configuracao/OpcoesNotificacoes.cs, OpcoesLimpeza.cs
│   └── Observabilidade/TelemetriaOms.cs                     # métricas e span novos
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsNotificacoes.cs                   # NOVO
│   ├── Endpoints/EndpointsPedidos.cs                        # ETag/304
│   ├── Seguranca/PoliticasAutorizacao.cs                    # PoliticaDestinosNotificacao
│   └── Program.cs, appsettings*.json
└── PedidoFulfillment.Worker/ProgramaWorker.cs               # AdicionarProcessamentoNotificacoes

specs/001-contratos-api/contracts/openapi-v1.yaml            # rotas novas, ETag e 304
docker-compose.yml                                           # Notificacoes__ChaveCifra na API e no Worker

tests/
├── PedidoFulfillment.Testes.Unitarios/Notificacoes/         # NOVO: fábrica, assinatura, SSRF, cifra, espera, distribuidor, opções
└── PedidoFulfillment.Testes.Integracao/
    ├── Notificacoes/                                         # NOVO: avisos por caminho, transação lenta, webhooks, SSE, destinos
    ├── Infraestrutura/ReceptorWebhookTeste.cs               # NOVO: Kestrel local
    ├── Mensageria/SchemaEventosTestes.cs                     # pedido.status-alterado
    ├── Pedidos/EtagPedidoTestes.cs                           # NOVO
    └── PedidoFulfillment.Testes.Integracao.csproj           # copia os schemas de specs/018

docs/fluxos/                                                 # 13 novo; 03, 07, 10, 11, 12 e README (Polish)
```

**Structure Decision**: nenhum projeto novo. As entidades e regras das notificações ficam no
Application (como `EstornoPendente` e `PedidoReenvioDlq`), porque não são do domínio do pedido;
o domínio não muda. Persistência, rotinas, HTTP de saída e SSE ficam no Infrastructure, que a
API e o Worker já registram; a API registra só o `LeitorAvisos` e o `DistribuidorAvisos`, e o
Worker só o `ProcessadorNotificacoes`.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Um caminho de mudança de status não gera aviso | Geração no `SalvarAsync`, por onde todos gravam; a base de consumo recusa histórico novo gravado sem ele (R-14); teste de integração por caminho do R-01 |
| Leitura por cursor pula aviso de transação lenta | `rowversion` abaixo de `MIN_ACTIVE_ROWVERSION()` (R-13); teste com transação aberta segurando um aviso |
| Origem errada para quem tem dois perfis | Consulta aos perfis do autor, com a mesma regra do REQ-15; teste com Admin que também é Cliente |
| Receptor lento segura os demais | No máximo uma entrega por destino por rodada, até 8 em paralelo, limite de 10 s por chamada |
| SSRF por troca de DNS depois do cadastro | Conferência do IP no `ConnectCallback` a cada conexão, com a mesma lista do cadastro (`EnderecosProibidos`) |
| Vazamento do segredo | Cifrado em repouso, mostrado só no `201`, fora de logs e métricas; chave obrigatória na partida |
| Memória da API com cliente SSE lento | `Channel` limitado por conexão; ao encher, `sincronizacao-necessaria` e fim da conexão |
| Lacuna ou repetição entre reenvio e avisos ao vivo | Registro da conexão antes do reenvio e descarte por `Posicao` (R-16); teste de reconexão |
| Várias instâncias da API ou do Worker | API: cada instância lê por conta própria, sem estado compartilhado. Worker: lock de aplicação por rodada e índice único `(DestinoId, AvisoId)` |
| Tabela de avisos crescendo | Limpeza do REQ-14 com retenção de 7 dias, sem apagar aviso com entrega pendente |
| Testes existentes que semeiam pedidos pelo contexto | A geração fica no `ArmazenamentoPedidos`, não no contexto; a semeadura direta não gera avisos, como na guarda do REQ-15 |
| `rowversion` em comparação pelo EF | Consultas de cursor em SQL parametrizado (`SqlQuery`), com o valor como `binary(8)`; teste da leitura |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxo 13 novo e fluxos 3, 7, 10, 11, 12 e o índice (R-20), conforme o `CLAUDE.md` |
