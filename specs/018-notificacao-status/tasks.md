---

description: "Lista de tarefas do REQ-18 — Notificação de mudança de status do pedido (webhooks e SSE)"
---

# Tasks: Notificação de mudança de status do pedido (webhooks e SSE)

**Input**: Design documents from `/specs/018-notificacao-status/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/api-notificacoes.md](./contracts/api-notificacoes.md),
[contracts/webhooks.md](./contracts/webhooks.md), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras de negócio,
integração com sucesso, 401 e 403 de toda rota protegida, e conformidade de schema dos
contratos; o FR-030 lista os cenários de integração. Os testes de cada fase vêm antes da
implementação e devem falhar primeiro.

**Organization**: por história de usuário, depois de uma fundação com os contratos, as
entidades, a migration e a leitura por cursor. A US1 (aviso único por mudança) é o MVP e a base
das outras. A US2 (webhooks) e a US3 (SSE) dependem só da US1 e podem correr em paralelo. A US4
(gestão e acompanhamento) depende da US2. A US5 (`ETag`) é independente de todas.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Nomes em português; endpoints só roteiam; regras no Application; persistência, rotinas, HTTP
  de saída e SSE no Infrastructure (plan, Structure Decision). O domínio não muda.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo** (plan). Uma única migration, `NotificacaoStatus`.
- Contratos v1 só com acréscimos: os tipos de evento, os schemas e as rotas existentes não
  mudam. Cada rota nova entra no `specs/001-contratos-api/contracts/openapi-v1.yaml` na mesma
  tarefa que a cria, para o `ConformidadeOpenApiTestes` continuar verde.
- Avisos e eventos sem dados pessoais, valores, itens, dados de pagamento, transportadora,
  código de rastreio, `clienteId` ou texto livre do motivo (FR-002). O segredo do destino nunca
  aparece em log, métrica, consulta ou exceção; a URL nos logs só com esquema e host (FR-028).
- A API **não** fala com o broker (FR-024, REQ-14).
- Logs com `LoggerMessage` na faixa 10001–10014 (research R-19); IDs existentes não mudam.
- Os testes existentes do REQ-01 ao REQ-17 passam sem afrouxar asserções.
- Testes com banco e broker reais usam Testcontainers e respeitam `OMS_EXIGIR_DOCKER`
  (REQ-07). Nenhuma espera fixa em teste: espere por condição, com prazo. Nos testes de
  integração, `Notificacoes:Envio:Habilitado` = `false` por padrão (como os estornos), ligado
  só nos testes de webhook, e `Notificacoes:PermitirRedeLocal` = `true`.
- Commits: `tipo(escopo): descrição [REQ-18]`, um por fase.

---

## Phase 1: Setup

- [X] T001 Rodar `dotnet build` e `OMS_EXIGIR_DOCKER=1 dotnet test` na branch `feature/req-18-notificacao-status` e registrar em Notes a contagem de testes como linha de base (unitários, integração e ignorados; zero ignorados com o Docker no ar).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: contratos, entidades, migration, configuração e a leitura dos avisos por cursor.
Nenhum aviso é gerado ainda.

### Testes da fundação

- [X] T002 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/SerializacaoEventosTestes.cs`: ida e volta de `PedidoStatusAlterado` em camelCase, com `statusAnterior`, `statusNovo` e `origem` como texto; o tipo do envelope é `pedido.status-alterado`; `TiposEvento.TipoDados` passa a ter 9 tipos.
- [X] T003 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/CorpoNotificacaoTestes.cs`: a serialização do corpo `notificacao.v1` dos dois tipos produz exatamente os campos do schema (`idNotificacao`, `tipo`, `versao` = 1, `ocorridoEm`, `dados`); `pedido.estorno-confirmado` sem `statusAnterior`, `statusNovo` e `sequencia`; `link` = `/api/v1/pedidos/{pedidoId}`.
- [X] T004 [P] Em `tests/PedidoFulfillment.Testes.Unitarios/Configuracao/OpcoesNotificacoesTestes.cs`: o validador acumula as falhas com a mensagem "Notificacoes:Chave deve estar entre X e Y." para cada faixa do research R-18; recusa `ChaveCifra` ausente, que não é Base64 ou que não tem 32 bytes, sem mostrar o valor; aceita `PermitirRedeLocal` = `true` só nos ambientes `Development` e `Testes` (o das fábricas de teste) e recusa nos demais, inclusive `Production` (analyze A1).
- [X] T005 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/LeituraAvisosTestes.cs` (research R-13), com banco real e avisos semeados direto pelo contexto:
  - lê em ordem de `Posicao` só o que está depois do cursor, até o tamanho do lote;
  - **transação lenta**: abre uma transação que insere o aviso A e não confirma; outra conexão insere e confirma o aviso B; a leitura **não** devolve B enquanto A está pendente (B está acima de `MIN_ACTIVE_ROWVERSION()`); depois do commit de A, devolve A e B, nessa ordem, sem pular nenhum;
  - as leituras filtradas por Cliente e por pedido (para o reenvio do SSE) devolvem só os avisos desse Cliente ou desse pedido;
  - `PosicaoDe(idNotificacao)` devolve a posição de um aviso existente e `null` para um inexistente; `UltimaConfirmada()` devolve a do último aviso confirmado, ou nada com a tabela vazia.

### Implementação da fundação

- [X] T006 Contrato do evento (research R-02, R-14) em `src/PedidoFulfillment.Application/Contratos/Eventos/Eventos.cs`: `public enum OrigemMudancaStatus { Worker = 1, Cliente, Operacao }` (XML doc: acrescentar valor muda o contrato) e `sealed record PedidoStatusAlterado` com `required` `PedidoId` (`Guid`), `Numero` (`string`), `StatusAnterior` e `StatusNovo` (`StatusPedido`), `Sequencia` (`int`, "a criação é 1"), `Origem` (`OrigemMudancaStatus`) e `AlteradoEm` (`DateTimeOffset`). Em `TiposEvento.cs`, `PedidoStatusAlterado = "pedido.status-alterado"` no catálogo e em `TipoDados`. No `.csproj` dos testes de integração, copiar `..\..\specs\018-notificacao-status\contracts\eventos\*.schema.json` com `LinkBase="Eventos"`, e em `Mensageria/SchemaEventosTestes.cs` um exemplo válido do tipo novo. T002 deve passar.
- [X] T007 [P] Corpo da notificação em `src/PedidoFulfillment.Application/Contratos/Notificacoes/CorpoNotificacao.cs` (`sealed record` com `IdNotificacao`, `Tipo`, `Versao`, `OcorridoEm`, `Dados`) e os dados dos dois tipos (`DadosStatusAlterado`, `DadosEstornoConfirmado`), com `TiposNotificacao` (`pedido.status-alterado`, `pedido.estorno-confirmado`) e a serialização com as `SerializacaoEventos.Opcoes`. No `.csproj` dos testes de integração, copiar `specs\018-notificacao-status\contracts\notificacoes\*.schema.json` com `LinkBase="Notificacoes"`. T003 deve passar.
- [X] T008 Entidades do Application em `src/PedidoFulfillment.Application/Notificacoes/` (data-model, campos e regras como estão lá):
  - `AvisoPedido.cs`: `Id` (`long`), `IdNotificacao`, `Tipo` ("`varchar(50)`"), `PedidoId`, `ClienteId`, `Conteudo` (corpo serializado, "enviado byte a byte"), `CriadoEm`; só de inclusão, com fábrica estática;
  - `DestinoNotificacao.cs`: `Id` (v7), `ClienteId?` ("nulo = global"), `CriadoPor`, `Url` ("até 2048"), `SegredoCifrado` ("`varbinary(60)`"), `Ativo`, `MotivoDesativacao?` ("até 200"), `DesativadoEm?`, `FalhasSeguidas`, `CriadoEm`, `AtualizadoEm`; métodos `Desativar(motivo, instante)`, `Reativar(instante)` (zera as falhas; idempotente se ativo), `RegistrarFalha()` e `RegistrarSucesso()`;
  - `EntregaNotificacao.cs` e `SituacaoEntrega` (`Pendente`, `Entregue`, `Esgotada`, `Descartada`): `DestinoId?`, `AvisoId`, `Tentativas`, `ProximaTentativaEm`, `PrimeiraTentativaEm?`, `UltimoCodigoHttp?`, `UltimoErro?` ("até 200, sem URL completa nem corpo da resposta"), `CriadaEm`, `FinalizadaEm?`, `ContextoRastreio`; métodos `MarcarEntregue`, `RegistrarFalha(codigo, erro, proxima)`, `Esgotar`, `Descartar`, que preenchem `FinalizadaEm` ao sair de `Pendente`;
  - portas usadas pelas US2 a US4 (analyze U3): `IArmazenamentoNotificacoes` (destinos por escopo, contagem para o limite, gravação, entregas de um destino), `IVerificadorEndereco` (`Task<bool> EnderecoPublicoAsync(Uri, CancellationToken)`) e `ICifraSegredos` (`byte[] Cifrar(byte[])`, `byte[] Decifrar(byte[])`).
- [X] T009 Persistência e migration (data-model) em `src/PedidoFulfillment.Infrastructure/Persistencia/`:
  - `Configuracoes/ConfiguracaoAvisoPedido.cs` (tabela `AvisosPedido`; `Posicao` `rowversion` de sombra com índice único; `IdNotificacao` único; índices `(ClienteId, Posicao)`, `(PedidoId, Posicao)` e `CriadoEm`; `ContextoRastreio` de sombra como em `EventosSaida`; FK `Pedidos`);
  - `ConfiguracaoDestinoNotificacao.cs` (`Versao` `rowversion` de sombra; índice `ClienteId` filtrado `[ClienteId] IS NOT NULL`; índice `Ativo`);
  - `ConfiguracaoEntregaNotificacao.cs` (índice único `(DestinoId, AvisoId)`; `(Situacao, ProximaTentativaEm)` filtrado `[Situacao] = 'Pendente'`; `FinalizadaEm` filtrado não nulo; FK do destino com `ON DELETE SET NULL`; FK do aviso sem cascata; `Situacao` como texto com `CHECK` por `RestricoesEnum`);
  - `ConfiguracaoCursorNotificacoes.cs` (uma linha, `Id` = 1, `Posicao` `binary(8)`, semeada com `0x0` pela migration);
  - `DbSet`s no `ContextoBancoDados` e o interceptor do REQ-16 preenchendo o `ContextoRastreio` de `AvisoPedido`;
  - `dotnet ef migrations add NotificacaoStatus` (pasta `Migracoes`), conferindo que nenhuma tabela existente muda.
- [X] T010 [P] Configuração em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesNotificacoes.cs`: seção `Notificacoes` com as chaves, padrões e faixas do research R-18, o validador com `FaixasOpcoes` e as regras da `ChaveCifra` e do `PermitirRedeLocal` (só `Development` e `Testes`); registrado com validação na partida da API e do Worker. `ChaveCifra` em `appsettings.Development.json` da API e do Worker, nas fábricas de teste e no `docker-compose.yml` (`Notificacoes__ChaveCifra`, mesmo valor para API e Worker, como a chave do JWT). T004 deve passar.
- [X] T011 Leitura por cursor (research R-13) em `src/PedidoFulfillment.Infrastructure/Notificacoes/LeituraAvisos.cs`, com SQL parametrizado (`Database.SqlQuery`), `Posicao` tratada como `binary(8)`/`ulong`: `LerAsync(cursor, lote)`, `LerDoClienteAsync(clienteId, cursor, lote)`, `LerDoPedidoAsync(pedidoId, cursor, lote)`, `PosicaoDeAsync(idNotificacao)` e `UltimaConfirmadaAsync()`, sempre com `Posicao < MIN_ACTIVE_ROWVERSION()`. T005 deve passar.
- [X] T012 [P] Observabilidade básica: `src/PedidoFulfillment.Infrastructure/Notificacoes/EventosNotificacao.cs` com os logs 10001 a 10014 do research R-19 (assinaturas só; a URL entra já reduzida a esquema e host por um helper `HostDestino(url)`), e em `Observabilidade/TelemetriaOms.cs` as métricas `oms.notificacoes.entregas`, `oms.notificacoes.latencia`, `oms.notificacoes.pendentes`, `oms.notificacoes.destinos_desativados`, `oms.notificacoes.sse.conexoes`, a rotina `notificacoes` e o span `notificacao enviar`.

**Checkpoint**: build verde, migration aplicada nos testes, T002 a T005 verdes.

---

## Phase 3: User Story 1 - Toda mudança de status vira um aviso único (Priority: P1) 🎯 MVP

**Goal**: toda mudança de status grava, na mesma transação, um `pedido.status-alterado` e um
`AvisoPedido`; o estorno confirmado grava um aviso `pedido.estorno-confirmado`.

**Independent Test**: mudar o status por cada caminho do research R-01 e conferir um evento e um
aviso por mudança, com sequência e origem certas, válidos pelos schemas; nenhum na criação nem
na correção do rastreio.

### Testes da US1

- [X] T013 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/FabricaAvisosTestes.cs`: a fábrica monta o evento e o aviso de uma mudança com o mesmo identificador (`IdEvento` = `IdNotificacao`), a sequência e a origem recebidas e o corpo `notificacao.v1`; monta o aviso do estorno com o identificador do `pagamento.estornado`; nenhum campo proibido (FR-002) aparece no evento nem no corpo, mesmo com o histórico tendo um motivo com texto livre.
- [X] T014 [P] [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/AvisosPorCaminhoTestes.cs`, com banco e broker reais, um caso por caminho (spec US1, cenários 1 a 7), conferindo outbox e `AvisosPedido` do pedido (reaproveitar `Infraestrutura/EventosOutbox.cs`):
  - pagamento aprovado (`Pendente → Processando`, origem `Worker`, sequência 2) e recusado (`→ PagamentoRejeitado`);
  - ordem de entrega criada (`Processando → ProntoParaEnvio`, `Worker`);
  - despacho com rastreio pela operação (`→ Despachado`, `Operacao`) e a correção do rastreio depois, **sem** aviso;
  - `PATCH /status` pela operação nas três transições;
  - cancelamento pelo Cliente (`Cliente`, junto do `pedido.cancelado` do REQ-15), pela operação e pela reconciliação;
  - um Admin que também é Cliente mudando o próprio pedido sai com origem `Operacao`;
  - criação do pedido **sem** aviso;
  - estorno confirmado: aviso `pedido.estorno-confirmado` com o identificador do `pagamento.estornado`;
  - gravação recusada (conflito persistente) sem evento nem aviso;
  - os eventos novos válidos pelo schema `pedido.status-alterado.v1` e os corpos pelo `notificacao.v1`;
  - numa fila de teste ligada a `oms.eventos` com a chave `pedido.status-alterado`, o evento chega publicado pelo despachante (Independent Test da US1; analyze I2).
- [X] T015 [P] [US1] Em `tests/PedidoFulfillment.Testes.Unitarios/Mensageria/ConsumidorEventosTestes.cs` (ou no teste da base existente): um manipulador que muda o status de um pedido sem passar pelo `SalvarAsync` faz a base recusar a gravação (nada é confirmado; a mensagem segue para novas tentativas), e o histórico gravado pelo `SalvarAsync` passa normalmente (research R-14).

### Implementação da US1

- [X] T016 [US1] `src/PedidoFulfillment.Application/Notificacoes/FabricaAvisos.cs`: a partir de pedido, entrada do histórico, sequência, origem e instante, monta o `EventoSaida` `pedido.status-alterado` e o `AvisoPedido`; a partir do pagamento estornado e do `IdEvento` do `pagamento.estornado`, monta o aviso `pedido.estorno-confirmado`. T013 deve passar.
- [X] T017 [US1] Em `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs`, depois da guarda do REQ-15 e antes do `SaveChangesAsync` (research R-14):
  - para cada `HistoricoStatusPedido` `Added` com `StatusAnterior` não nulo, exigir o histórico do pedido carregado (`Navigation(...).IsLoaded`, senão `InvalidOperationException` citando o REQ-18), calcular a sequência (posição na coleção do pedido) e a origem, e adicionar o evento e o aviso da `FabricaAvisos`;
  - origem: `AutorId` nulo → `Worker`; senão, uma consulta a `UserRoles`/`Roles` para os autores da gravação → `Operacao` se tiver Admin ou OperadorLogistica, `Cliente` nos demais (mesma regra do `EhOperacao` do `ServicoPedidos`);
  - para cada `Pagamento` de `EstornoPendente` a `Estornado`, o aviso do estorno com o `IdEvento` do `pagamento.estornado` já adicionado;
  - num conflito, o `DescartarAlteracoes` leva evento e aviso junto (sem mudança: eles estão no mesmo `ChangeTracker`).
  Conferir os caminhos do R-01 que carregam o pedido sem `Include(Historico)` (reconciliação, consumidores, estornos) e incluir o histórico onde faltar.
- [X] T018 [US1] Em `src/PedidoFulfillment.Infrastructure/Mensageria/ConsumidorEventos.cs`, antes do `SaveChangesAsync` do `EventoProcessado`: se houver `HistoricoStatusPedido` `Added` no `ChangeTracker`, lançar `InvalidOperationException` ("mudança de status gravada fora do SalvarAsync, REQ-18"). T014 e T015 devem passar, e a suíte inteira continuar verde.

**Checkpoint**: MVP. Todo caminho de mudança gera evento e aviso; os outros sistemas já recebem
`pedido.status-alterado` pelo broker.

---

## Phase 4: User Story 2 - Sistema de fora recebe o aviso por webhook (Priority: P1)

**Goal**: cadastro de destino com segredo, distribuição dos avisos em entregas e envio assinado
com novas tentativas, esgotamento, desativação e proteção contra SSRF.

**Independent Test**: cadastrar um destino apontando para o receptor de teste, mudar o status
de um pedido e conferir o `POST` com assinatura válida; derrubar o receptor e conferir as novas
tentativas e o esgotamento.

### Testes da US2

- [X] T019 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/EnderecosProibidosTestes.cs`: recusa loopback (`127.0.0.0/8`, `::1`), `10/8`, `172.16/12`, `192.168/16`, `169.254/16` (inclusive `169.254.169.254`), `100.64/10`, `0.0.0.0/8`, multicast, `fc00::/7`, `fe80::/10` e IPv4 mapeado em IPv6 (`::ffff:10.0.0.1`); aceita endereços públicos (`8.8.8.8`, `2606:4700::1111`); com `PermitirRedeLocal`, aceita loopback.
- [X] T020 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/CifraSegredosTestes.cs`: ida e volta; nonce diferente a cada cifra; resultado com 60 bytes; tag ou texto adulterado lança `CryptographicException`; chave diferente não decifra.
- [X] T021 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/AssinaturaWebhookTestes.cs`: com o vetor de teste publicado pelo Standard Webhooks (segredo, `webhook-id`, timestamp e corpo conhecidos), a assinatura `v1,…` bate com a esperada; o segredo é gerado como `whsec_` + Base64 de 32 bytes.
- [X] T022 [P] [US2] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/EsperaEntregasTestes.cs` e `EntregaNotificacaoTestes.cs`: espera de 30 s, 2 min, 10 min, 30 min, 1 h e depois 2 h; falha com 24 h desde a primeira tentativa esgota; `410` esgota e desativa o destino; sucesso zera as falhas seguidas; `Descartar` só a partir de `Pendente`; `FinalizadaEm` preenchido ao sair de `Pendente`.
- [X] T023 [US2] Receptor de teste em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/ReceptorWebhookTeste.cs`: Kestrel em `127.0.0.1` com porta livre, que grava cabeçalhos e corpo de cada requisição e responde conforme um roteiro configurável por teste (200, 500, 410, 302, atraso acima de 10 s); `IAsyncDisposable`.
- [X] T024 [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/DestinosCadastroTestes.cs` (FR-007 a FR-010, FR-026), API real:
  - Cliente e Admin cadastram (`201`, `Location`, `segredo` com `whsec_`); o `GET` em seguida não traz o segredo; o destino do Admin sai `global: true`;
  - sem token `401`; OperadorLogistica `403`;
  - URL relativa, `http` sem `PermitirRedeLocal`, com usuário e senha ou acima de 2048 caracteres: `400`;
  - host que resolve para endereço interno, com `PermitirRedeLocal` = `false` numa fábrica própria: `400` `NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO`;
  - sexto destino do Cliente e sexto global: `409` `NOTIFICACAO_DESTINO_LIMITE`; dois cadastros simultâneos com 4 existentes deixam exatamente 5;
  - o segredo fica cifrado no banco (a coluna não contém os bytes do segredo).
- [X] T025 [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/EntregaWebhookTestes.cs` (US2, cenários 2 a 8; FR-012 a FR-017), API e Worker reais, envio ligado com intervalo curto e o receptor da T023:
  - pedido do Cliente dono chega ao destino dele com os três cabeçalhos e assinatura válida (conferida com a `AssinaturaWebhook` e o segredo do cadastro); o corpo bate byte a byte com o `Conteudo` do aviso; a entrega fica `Entregue`;
  - pedido de outro Cliente não chega; o destino global recebe os dois;
  - `500` e depois `200`: duas tentativas, mesmo `webhook-id`, `Entregue` no fim;
  - atraso acima do tempo limite e `302` contam como falha;
  - `410`: destino desativado na hora, com o motivo, e entregas pendentes dele `Descartada`;
  - esgotamento: com o relógio (`TimeProvider` de teste) avançado além de 24 h, a entrega fica `Esgotada` e o destino, desativado;
  - um destino fora do ar não atrasa o outro (o destino no ar recebe na primeira rodada);
  - destino cadastrado depois do aviso não o recebe;
  - rodada repetida (dois Workers ou a rodada chamada duas vezes) não duplica entregas;
  - com o broker parado, o aviso de uma mudança pela API chega mesmo assim;
  - o receptor nunca recebe `traceparent`;
  - segredo gravado com outra chave: a entrega falha com "segredo ilegível" e o receptor não recebe nada.
- [X] T026 [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/SsrfEnvioTestes.cs`: com `PermitirRedeLocal` = `false`, um destino gravado direto no banco com host que resolve para `127.0.0.1` não recebe chamada; a entrega registra falha "endereço recusado" e o log 10009 sai.

### Implementação da US2

- [X] T027 [P] [US2] `src/PedidoFulfillment.Infrastructure/Notificacoes/EnderecosProibidos.cs` (faixas da T019) e `VerificadorEndereco.cs` (resolve o host com `Dns.GetHostAddressesAsync` e recusa se **qualquer** endereço for proibido; implementa `IVerificadorEndereco` do Application). T019 deve passar.
- [X] T028 [P] [US2] `src/PedidoFulfillment.Infrastructure/Notificacoes/CifraSegredos.cs` (`AesGcm`, nonce 12 + cifrado 32 + tag 16; implementa `ICifraSegredos`) e `AssinaturaWebhook.cs` (geração do segredo e HMAC-SHA256 de `{id}.{timestamp}.{corpo}`). T020 e T021 devem passar.
- [X] T029 [P] [US2] `src/PedidoFulfillment.Application/Notificacoes/EsperaEntregas.cs` com a tabela de espera e o limite configurável. T022 deve passar.
- [X] T030 [US2] `src/PedidoFulfillment.Application/Notificacoes/ServicoDestinosNotificacao.cs` (cadastro e lista) com `IArmazenamentoNotificacoes`, DTOs em `Contratos/Notificacoes/` (`CadastrarDestinoRequestDTO`, `DestinoNotificacaoResponseDTO`, `DestinoCadastradoResponseDTO` com `segredo`), validador do DTO e códigos novos em `CodigosErro`. Escopo: Admin (perfil de operação) cadastra global; Cliente, o próprio; quem tem os dois perfis cadastra como Admin, global (research R-15; analyze U5). Limite conferido sob lock de aplicação por dono (`oms-destinos:{dono}` ou `oms-destinos:global`) na transação do cadastro (research R-15). Log 10001.
- [X] T031 [US2] `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoNotificacoes.cs`: destinos por escopo, contagem para o limite, gravação, e o que o processador precisa (destinos ativos para um Cliente e globais, entregas vencidas com no máximo uma por destino via `ROW_NUMBER() OVER (PARTITION BY DestinoId ORDER BY ProximaTentativaEm)`, descarte das pendentes de um destino).
- [X] T032 [US2] `src/PedidoFulfillment.API/Endpoints/EndpointsNotificacoes.cs` com `POST` e `GET /api/v1/notificacoes/destinos` (contrato), `PoliticaDestinosNotificacao` em `Seguranca/PoliticasAutorizacao.cs`, registro em `Program.cs`, e as duas rotas no `openapi-v1.yaml`. T024 deve passar.
- [X] T033 [US2] `src/PedidoFulfillment.Infrastructure/Notificacoes/EnviadorWebhook.cs`: cliente nomeado do `IHttpClientFactory` com `SocketsHttpHandler` (`AllowAutoRedirect = false`, `ConnectCallback` que resolve o host, confere cada IP com `EnderecosProibidos` e conecta só a um permitido), tempo limite de `Envio:TempoLimiteSegundos`, `User-Agent: OMS-Webhooks/1`, os três cabeçalhos e o corpo do aviso como bytes; devolve o resultado (código, tipo do erro) sem ler o corpo da resposta. Sem `traceparent`: desligar a propagação no cliente (`DistributedContextPropagator` nulo para ele, ou remover o cabeçalho num `DelegatingHandler`).
- [X] T034 [US2] `src/PedidoFulfillment.Infrastructure/Notificacoes/ProcessadorNotificacoes.cs` (research R-05 revista), no molde do `ProcessadorEstornos`: lock `oms-notificacoes`; rodada a cada `Envio:IntervaloSegundos`; **distribuição** (lê avisos depois do `CursorNotificacoes` com a `LeituraAvisos`, cria entregas para os destinos ativos do Cliente dono e globais com `CriadoEm` do destino ≤ `CriadoEm` do aviso, avança o cursor, tudo numa transação; log 10005); **envio** (entregas vencidas, `Parallel.ForEachAsync` com `Envio:Paralelismo`, um escopo por entrega, decifra o segredo (se não decifrar, por chave trocada, a entrega falha com "segredo ilegível" sem enviar nada e segue o caminho normal até esgotar e desativar o destino, com teste em T025; analyze U2), envia, aplica `EsperaEntregas`, desativa o destino e descarta as pendentes dele em `410` ou esgotamento; logs 10006 a 10010; span `notificacao enviar` com link para o `ContextoRastreio`; métricas de entregas e latência). Registro em `ExtensoesNotificacoes.AdicionarProcessamentoNotificacoes()` e em `ProgramaWorker.cs`; `FabricaWorker` desliga por padrão. T025 e T026 devem passar.

**Checkpoint**: um sistema de fora integra por webhook, com assinatura, novas tentativas e
desativação.

---

## Phase 5: User Story 3 - Aplicação conectada recebe o aviso na hora por SSE (Priority: P2)

**Goal**: rota `text/event-stream` autenticada, alimentada pela leitura periódica do banco em cada
instância, com `conectado`, reenvio por `Last-Event-ID`, `sincronizacao-necessaria`, sinal de
vida, limite e fim no `exp` do token.

**Independent Test**: abrir a conexão como Cliente, mudar um pedido dele e um de outro Cliente e
conferir que só o primeiro chega, em até 12 s.

### Testes da US3

- [X] T035 [P] [US3] Em `tests/PedidoFulfillment.Testes.Unitarios/Notificacoes/DistribuidorAvisosTestes.cs`: o filtro entrega ao Cliente só os avisos dele, à operação todos, e à conexão de um pedido só os desse pedido; o limite por usuário recusa a quarta conexão e libera a vaga quando uma fecha; fila cheia encerra a conexão com `ClienteLento`; aviso com `Posicao` menor ou igual à última enviada é descartado (sem repetição entre reenvio e ao vivo).
- [X] T036 [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/SseTestes.cs` (US3, cenários 1 a 14; FR-019 a FR-024), API real com `Sse:IntervaloLeituraSegundos` = 1 e `SinalDeVidaSegundos` = 5, lendo o stream com `HttpClient` (`HttpCompletionOption.ResponseHeadersRead`) e um leitor SSE de teste:
  - sem token `401`; token sem perfil `403` (padrão do `ObterPedidoTestes`); `pedidoId` inválido `400`; de outro Cliente `404`;
  - primeiro evento `conectado` com `id`; com a tabela vazia, o GUID zero;
  - Cliente recebe o aviso do pedido dele, não o de outro Cliente; Admin e OperadorLogistica recebem os dois; a conexão de um pedido só recebe esse pedido;
  - mudança feita entre o `conectado` e o `GET` chega pela conexão;
  - reconexão com o `id` de um aviso anterior recebe os perdidos, em ordem, antes dos novos e sem repetição;
  - reconexão de uma conexão de um pedido só conta e reenvia os avisos desse pedido: com mais avisos de outros pedidos do Cliente que o limite, não recebe `sincronizacao-necessaria`;
  - `Last-Event-ID` desconhecido, não GUID ou com mais avisos que `Sse:MaximoReenvio` (configurado baixo no teste): `sincronizacao-necessaria` e só os novos;
  - `sinal` depois do intervalo sem aviso;
  - quarta conexão do mesmo usuário: `429` `NOTIFICACAO_SSE_LIMITE_CONEXOES`;
  - token com `exp` curto: a API encerra a conexão;
  - com o broker parado, a mudança pela API chega pelo SSE;
  - duas instâncias da API (duas fábricas) no mesmo banco: a conexão de cada uma recebe o aviso.

### Implementação da US3

- [X] T037 [US3] `src/PedidoFulfillment.Infrastructure/Notificacoes/DistribuidorAvisos.cs` (singleton): registro de conexões com filtro (usuário, perfis de operação, pedido opcional), `Channel` limitado a 100 por conexão, limite `Sse:ConexoesPorUsuario` por usuário, descarte por `Posicao`, gauge `oms.notificacoes.sse.conexoes`, logs 10011 a 10013. T035 deve passar.
- [X] T038 [US3] `src/PedidoFulfillment.Infrastructure/Notificacoes/LeitorAvisos.cs` (`BackgroundService` registrado só na API): cursor em memória iniciado pela `UltimaConfirmadaAsync`, leitura a cada `Sse:IntervaloLeituraSegundos` com a `LeituraAvisos`, publicação no distribuidor; falha da leitura vira log 10014 e nova tentativa no ciclo seguinte, sem derrubar a API.
- [X] T039 [US3] SSE em duas partes (constituição: o endpoint só roteia; analyze C1):
  - `src/PedidoFulfillment.Infrastructure/Notificacoes/FluxoEventosTempoReal.cs`: recebe usuário, perfis, `pedidoId` opcional, `Last-Event-ID` e o instante do `exp`; confere o limite (resultado `LimiteAtingido`, sem abrir nada) e devolve um `IAsyncEnumerable<SseItem<string>>` que registra a conexão no distribuidor, envia `conectado` ou o reenvio (`LerDoPedidoAsync` com `pedidoId`, `LerDoClienteAsync` para Cliente, `LerAsync` para operação, até `Sse:MaximoReenvio`) e depois a fila ao vivo, intercalando `sinal` a cada `Sse:SinalDeVidaSegundos`, e termina no `exp`, no cancelamento ou com a fila cheia;
  - rota `GET /api/v1/notificacoes/eventos` em `EndpointsNotificacoes.cs`: política de consulta; para `pedidoId`, conferência de existência e dono pelo `IServicoPedidos` (`404` como no `GET`); `429` com `ProblemDetails` quando o fluxo devolve `LimiteAtingido`; senão, `TypedResults.ServerSentEvents` do enumerável; declarar `.Produces(200, contentType: "text/event-stream")` e conferir o documento gerado antes de editar o `openapi-v1.yaml` (analyze U4).
  Os nomes e o `data` dos eventos seguem o [contrato](./contracts/api-notificacoes.md). T036 deve passar.

**Checkpoint**: uma aplicação acompanha os pedidos na hora, sem broker na API.

---

## Phase 6: User Story 4 - Dono e operação gerenciam os destinos e acompanham as entregas (Priority: P2)

**Goal**: remover, reativar e consultar entregas, com escopo de Cliente e de operação; limpeza
das entregas e dos avisos; métricas de saúde.

**Independent Test**: cadastrar, desativar por esgotamento, reativar, consultar entregas e remover
um destino; outro usuário recebe `404`; outro Admin gerencia o global.

### Testes da US4

- [X] T040 [P] [US4] Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/DestinosGestaoTestes.cs` (US4, cenários 1 a 7; FR-011):
  - Cliente lista só os dele; Admin lista só os globais, inclusive os de outro Admin, e não vê os de Clientes;
  - `DELETE` `204`, pendentes viram `Descartada`, nada mais é enviado;
  - reativação de destino inativo: `200`, `ativo: true`, falhas zeradas, entregas esgotadas não reenviadas; de destino ativo: `200` sem mudança;
  - `GET /entregas` com `limite` padrão 50, máximo 200 (`201` → `400`), da mais nova para a mais antiga, sem URL completa nem corpo de resposta;
  - outro Cliente ou um Cliente num destino global: `404` nas três rotas; outro Admin num destino global: sucesso;
  - sem token `401`; OperadorLogistica `403`.
- [X] T041 [P] [US4] Em `tests/PedidoFulfillment.Testes.Integracao/Mensageria/LimpezaMensageriaTestes.cs` (ou o teste da limpeza existente): avisos acima de `RetencaoAvisosHoras` sem entrega pendente são apagados, e com entrega pendente ficam; entregas finalizadas acima de `RetencaoEntregasHoras` são apagadas, e pendentes nunca.

### Implementação da US4

- [X] T042 [US4] `ServicoDestinosNotificacao`: remover (descarta pendentes, apaga o destino; log 10002), reativar (log 10003) e listar entregas (DTO `EntregaNotificacaoResponseDTO` do contrato); rotas `DELETE /destinos/{id}`, `POST /destinos/{id}/reativacao` e `GET /destinos/{id}/entregas` em `EndpointsNotificacoes.cs` e no `openapi-v1.yaml`. T040 deve passar.
- [X] T043 [US4] Limpeza (research R-18) em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimpeza.cs` (`RetencaoAvisosHoras` e `RetencaoEntregasHoras`, padrão 168, faixa 24–8760) e `Mensageria/LimpezaMensageria.cs` (dois alvos novos em lotes, com `oms.limpeza.alvo`). T041 deve passar.
- [X] T044 [US4] Gauges `oms.notificacoes.pendentes` e `oms.notificacoes.destinos_desativados` no Worker, lidos como os do outbox (amostragem periódica só com a telemetria ligada), com teste em `tests/PedidoFulfillment.Testes.Integracao/Observabilidade/` no padrão dos testes de métricas do REQ-16.

**Checkpoint**: o dono recupera um destino desativado sozinho, e a operação enxerga a saúde das
entregas.

---

## Phase 7: User Story 5 - Consulta mais barata para quem continua no polling (Priority: P3)

**Goal**: `ETag` e `304` no `GET /api/v1/pedidos/{id}`.

**Independent Test**: consultar, repetir com o `ETag` e receber `304`; mudar o pedido e receber
`200` com outro `ETag`.

- [X] T045 [P] [US5] Em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/EtagPedidoTestes.cs` (US5, cenários 1 a 3; FR-025): `200` com `ETag` forte; `If-None-Match` igual → `304` sem corpo e com o mesmo `ETag`; depois de mudança de status, de rastreio e de estorno, `200` com outro `ETag`; `If-None-Match` malformado → `200`; pedido de outro Cliente → `404` com e sem `If-None-Match`.
- [X] T046 [US5] `IServicoPedidos.ObterAsync` devolve também a versão (`rowversion` lido pelo armazenamento), sem mudar o `PedidoResponseDTO`; em `EndpointsPedidos.cs`, o `GET` escreve `ETag: "<Base64Url>"` e responde `304` com `If-None-Match` igual (research R-17); `304` e o cabeçalho no `openapi-v1.yaml`. T045 deve passar, e o `ConformidadeOpenApiTestes` continuar verde.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T047 [P] Documentos de fluxo (regra do `CLAUDE.md`, research R-20), conferindo mensagens, códigos, limites, IDs de log e links relativos no código:
  - `docs/fluxos/13-notificacao-de-status.md` novo, no formato dos demais: caminho feliz (aviso, webhook e SSE), erros tratados com resposta e exemplo, concorrência (transação lenta, várias instâncias), falhas externas (receptor, broker, banco), logs 10001–10014, métricas e limitações;
  - fluxo 3 (`ETag`/304 e aviso nas mudanças pela API), 7 (aviso no despacho; a pendência do `pedido.despachado` resolvida pelo `pedido.status-alterado`), 10 (aviso do estorno), 11 (limpeza de avisos e entregas), 12 (logs, métricas e span novos);
  - linha do fluxo 13 no índice `docs/fluxos/README.md`; o diagrama de estados não muda.
- [X] T048 [P] Documentação do projeto: `README.md` e `docs/` (estado atual, próximos passos com a #18, mensageria com o evento novo) e a seção de eventos do contrato apontando o schema novo.
- [X] T049 Varredura de segredos (nenhuma chave real no diff, só a de desenvolvimento), `dotnet build`, `dotnet format --verify-no-changes` e a suíte local inteira com Docker (zero ignorados); registrar a contagem em Notes.
- [ ] T050 Executar o [quickstart](./quickstart.md) (§2 a §6) com API e Worker locais e registrar em Notes o que chegou ao receptor e ao `curl`, corrigindo o quickstart onde divergir.
- [ ] T051 Push e PR em português (Resumo, Pontos de atenção, Validação) com `Closes #43`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, a revisão do R-05 (webhooks sem consumidor) e a decisão de a API ler o banco. Acompanhar `build-testes` e `contrato-compatibilidade` até ficarem verdes, **inclusive depois de cada push posterior**, e registrar em Notes.

---

## Dependencies & Execution Order

- **Setup** (T001): antes de tudo.
- **Foundational** (T002 a T012): bloqueia todas as histórias. T008 antes de T009; T009 antes de T011; T006 e T007 antes de T013.
- **US1** (T013 a T018): depende da fundação. T016 antes de T017; T018 por último.
- **US2** (T019 a T034): depende da US1 (os avisos precisam existir). T023 antes de T025 e T026; T027 a T031 antes de T032 a T034.
- **US3** (T035 a T039): depende da US1; pode correr junto da US2. T037 e T038 antes de T039.
- **US4** (T040 a T044): depende da US2 (destinos e entregas).
- **US5** (T045 e T046): depende só da fundação (nem disso, na prática); pode correr a qualquer momento.
- **Polish** (T047 a T051): depois de tudo. A T051 exige push.

### Parallel Opportunities

- T002 a T005; T007, T010 e T012.
- T013 a T015.
- T019 a T022; T027 a T029.
- US2 e US3 em paralelo depois da US1; US5 em paralelo com qualquer fase.
- T040 e T041; T047 e T048.

### Exemplo: US2

```text
Juntos: T019 (EnderecosProibidos), T020 (CifraSegredos), T021 (AssinaturaWebhook), T022 (EsperaEntregas)
Depois: T027, T028 e T029 → T030 → T031 → T032 → T033 → T034
Testes de integração: T023 → T024, T025 e T026
```

---

## Implementation Strategy

### MVP

Setup, fundação e US1: toda mudança de status passa a gerar `pedido.status-alterado` no broker e
um aviso pronto no banco. Já fecha a lacuna do despacho e das mudanças pela API para quem ouve
o broker.

### Incremental Delivery

1. Setup + fundação → `feat(notificacoes): contratos, tabelas e leitura dos avisos [REQ-18]`.
2. US1 → `feat(pedidos): gera pedido.status-alterado e avisos em toda mudança de status [REQ-18]`.
3. US2 → `feat(notificacoes): entrega de webhooks assinados com novas tentativas [REQ-18]`.
4. US3 → `feat(notificacoes): eventos em tempo real por SSE [REQ-18]`.
5. US4 → `feat(notificacoes): gestao dos destinos, entregas e limpeza [REQ-18]`.
6. US5 → `feat(pedidos): ETag e 304 na consulta do pedido [REQ-18]`.
7. Polish → `docs(fluxos): ... [REQ-18]`, push e PR.

---

## Notes

- Pela regra do `CLAUDE.md`, nenhum REQ novo começa antes de o PR deste ser mergeado.
- Fora do escopo (spec, Assumptions): SignalR, push móvel e de navegador, e-mail e SMS,
  notificação da correção do rastreio, reenvio manual de entrega esgotada, troca do segredo sem
  recadastro e avisos retroativos.
- A US1 muda o volume do outbox: um `pedido.status-alterado` a mais por mudança. Sem consumidor
  interno, ele sai sem rota obrigatória, como os tipos do REQ-15.
- Lição do REQ-10: depois de cada push, acompanhar o CI de novo; um teste instável só aparece
  em outra rodada.
- **SC-007** decorre de a mudança de status ganhar só dois `INSERT`s na mesma transação (e uma
  consulta de perfis nos caminhos da API), e de o SSE ser uma consulta por instância. Não há
  tarefa de desempenho própria; a suíte de desempenho existente continua rodando no CI.
- **Analyze (2026-11-02)**: aplicadas C1 (o SSE sai do endpoint para o
  `FluxoEventosTempoReal`, T039), U1 (reenvio por pedido: índice e leitura `LerDoPedidoAsync`,
  T005, T009, T011, T036), U2 (segredo ilegível com chave trocada, T025, T034 e spec), A1
  (`PermitirRedeLocal` só em `Development` e `Testes`, T004, T010), G1 (`403` do SSE, T036),
  U3 (portas no Application, T008), I1 (premissa da spec), I2 (fila de teste na T014), G2
  (SC-007 acima), U4 (`text/event-stream` no OpenAPI, T039) e U5 (dois perfis cadastram como
  Admin, T030 e contrato).
- **T001 (linha de base, 2026-11-02)**: 1.129 testes, 696 unitários e 433 de integração, zero
  ignorados, com o Docker no ar.
- **Fundação (T002 a T012)**: suíte inteira verde com 1.158 testes (720 + 438). Achados: as
  guardas do Domain são internas, então o Application ganhou `GuardasNotificacoes` com as mesmas
  regras; o banco não usa snapshot, e a leitura por cursor usa `READPAST` para não esperar pelo
  lock de um `INSERT` aberto (toda linha travada está em `MIN_ACTIVE_ROWVERSION()` ou acima); os
  testes do modelo e da migração listam as migrations e ganharam a `NotificacaoStatus`; a chave
  de cifra de desenvolvimento fica no `appsettings.Development.json` da API e do Worker (o
  compose usa Development, como a chave do JWT), sem mudança no `docker-compose.yml`. Os logs
  10001 a 10003 ficam no `ServicoDestinosNotificacao` (Application), e não no
  `EventosNotificacao` (Infrastructure, interno).
- **US1 (T013 a T018)**: a T015 virou teste de integração (`Persistencia/GuardaMudancaStatusTestes`),
  porque a guarda olha o `ChangeTracker` do EF; ele também cobre a gravação repetida sem descarte,
  que não duplica o aviso (o `ArmazenamentoPedidos` guarda o que já avisou). Três testes do
  REQ-12/13/15 contavam os eventos do outbox no cancelamento e passaram a esperar também o
  `pedido.status-alterado` (`CancelamentoPedidoPagoTestes`, dois casos de `EsgotamentoTestes`); o
  helper do esgotamento passou a conferir a origem `Worker` do aviso da reconciliação. O caso do
  Admin que também é Cliente usa um usuário com os dois perfis e sai com origem `Operacao`.
- **US2 a US5 (T019 a T046)**: o endpoint, o registro de serviços e o `openapi-v1.yaml` são
  comuns às quatro histórias, então o código saiu num commit só (`014373e`), e não em um por
  fase. Achados:
  - o Worker dos testes de webhook roda só o `ProcessadorNotificacoes`: com os consumidores, o
    pedido seguia o fluxo e gerava avisos que o teste não esperava;
  - os testes esperam a entrega com `ISNULL(MAX(Tentativas), 0)`: antes da distribuição a linha
    não existe;
  - a resposta 200 do SSE fica sem `content` no documento gerado, mesmo com
    `.Produces(200, contentType: "text/event-stream")`; o contrato segue o gerado e descreve o
    tipo no texto (analyze U4);
  - o `ParaProblema` ganhou um status opcional, para o 429 do limite de conexões;
  - o `ConsultarAsync` (versão + pedido) e o `ObterVersaoAsync` têm implementação padrão na
    interface, e os falsos dos testes continuam como estavam; a versão é lida antes do pedido;
  - a limpeza (T043) mudou o texto do log 5013 (duas contagens a mais), e o teste de trace da
    limpeza passou a esperar os alvos `entregas_notificacao` e `avisos_pedido`; o contrato de
    telemetria do REQ-16 registra os valores e as métricas novas;
  - a US5 não testa o `ETag` depois do estorno: a confirmação muda `AtualizadoEm` do pedido,
    como o rastreio, que é testado.
- **T047 e T048**: fluxo 13 novo; fluxos 3, 7, 10, 11 e 12, o índice (com o 429 e o parágrafo
  das notificações) e o diagrama de estados sem mudança. README, `docs/estado-atual.md`,
  `docs/mensageria.md` e `docs/proximos-passos.md` (fila a partir do REQ-19, só a #18). Links
  relativos conferidos por script.
- **T049 (2026-11-02)**: nenhum segredo no diff (só o vetor público do Standard Webhooks num
  exemplo e as chaves marcadas como de desenvolvimento e de teste); `dotnet build` sem avisos;
  `dotnet format --verify-no-changes` limpo; suíte local inteira com Docker: 769 unitários e 511
  de integração, zero ignorados (1.280 no total; 1.129 na linha de base). Numa rodada sob carga,
  o `Destino_fora_do_ar_nao_atrasa_o_outro` falhou por medir um prazo absoluto que incluía a
  partida do Worker; passou a comparar o instante de chegada dos dois receptores. Outra rodada
  foi descartada: a máquina dormiu no meio, e o banco de teste ficou inacessível por horas.
