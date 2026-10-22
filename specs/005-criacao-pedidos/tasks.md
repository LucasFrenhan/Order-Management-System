---

description: "Lista de tarefas do REQ-05 — Criação, consulta e status de pedidos"
---

# Tasks: Criação, consulta e status de pedidos

**Input**: Design documents from `/specs/005-criacao-pedidos/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/idempotencia.md](./contracts/idempotencia.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. O Princípio V da constituição exige:
- testes unitários para as regras;
- testes de integração com sucesso, 401 e 403 nas rotas protegidas.

A spec pede comprovação de idempotência concorrente (SC-002), de números únicos (SC-003),
da tabela de transições inteira (SC-004) e de desempenho (SC-005). Os testes de cada fase
são escritos antes da implementação e devem falhar primeiro.

**Organization**: por história de usuário:
- **Fundação**: tabela de transições, portas, registro de idempotência, armazenamento,
  gerador de número e migration. Todas as histórias dependem disso.
- **US1**: cria o `ServicoPedidos` e troca o provisório.
- **US2, US3 e US4**: completam os métodos.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português (constituição). `TreatWarningsAsErrors` ligado, e
  `dotnet format --verify-no-changes` precisa continuar passando.
- Nenhum pacote NuGet novo (R-13).
- Contrato v1 intocado (FR-021): nenhuma rota, DTO, status ou código de erro novo. O
  `EndpointsPedidos.cs` não muda.
- Instantes vêm do `TimeProvider`. Logs nunca contêm e-mail, token nem corpo da requisição
  (FR-020).
- Testes com banco real usam a `ColecaoBancoDados`, com um banco novo por teste, e são
  ignorados sem Docker.
- Commits: `tipo(escopo): descrição [REQ-05]`.

---

## Phase 1: Setup (Shared Infrastructure)

- [ ] T001 Rodar `dotnet tool restore`, `dotnet build PedidoFulfillment.sln` e `dotnet test PedidoFulfillment.sln` na branch `feature/req-05-criacao-pedidos`. Registrar a contagem de testes, que é a linha de base. Esperado: zero avisos e 287 testes verdes, herdados do REQ-04.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: regra de transições, portas do Application, persistência da idempotência,
número do pedido e utilitários de teste.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [ ] T002 [P] Testes de `TransicoesStatusPedido` em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/TransicoesStatusPedidoTestes.cs` (SC-004):
  - Usar `[Theory]` com `MemberData` cobrindo as 36 combinações (de, para) dos 6 status.
  - `PermitidaParaOperacao` é verdadeira exatamente para:
    - Pendente→Processando;
    - Processando→ProntoParaEnvio;
    - ProntoParaEnvio→Despachado;
    - Pendente→Cancelado, Processando→Cancelado e ProntoParaEnvio→Cancelado.
  - `PermitidaParaCliente` é verdadeira só para Pendente→Cancelado.
  - Todas as demais combinações são falsas, inclusive `de == para` e as saídas de
    Despachado, Cancelado e PagamentoRejeitado.
- [ ] T003 [P] Testes de `ImpressaoConteudo` em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ImpressaoConteudoTestes.cs` (FR-008, contracts/idempotencia.md):
  - o resultado tem 64 caracteres hexadecimais;
  - os mesmos itens em ordens diferentes dão a mesma impressão;
  - outra quantidade ou outro produto dá impressão diferente;
  - o valor bate com o SHA-256 calculado à mão para o texto canônico
    `"<guid1>:2;<guid2>:1"`, com os Guids em minúsculas e ordenados.
- [ ] T004 Atualizar `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ModeloSincronizadoTestes.cs`: a lista de migrations passa a ser `InicialIdentidade`, `EntidadesNegocio` e `ChavesIdempotencia`.

### Implementação da fundação

- [ ] T005 [P] Criar `src/PedidoFulfillment.Domain/Pedidos/TransicoesStatusPedido.cs`:
  - classe `public static` com `PermitidaParaOperacao(StatusPedido de, StatusPedido para)`
    e `PermitidaParaCliente(StatusPedido de, StatusPedido para)`, conforme o data-model;
  - comentário com a tabela do contrato v1 e a nota de que o perfil é decidido pelo
    serviço (R-06).

  A T002 deve passar.
- [ ] T006 [P] Criar as portas e tipos do Application em `src/PedidoFulfillment.Application/Pedidos/` (R-02 a R-04, data-model):
  - **`IArmazenamentoPedidos.cs`**:
    - `Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, CancellationToken)`;
    - `Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken)`, com
      os itens;
    - `Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken)`;
    - `void Adicionar(Pedido pedido, RegistroIdempotencia registro)`;
    - `Task SalvarAsync(CancellationToken)`, documentando que lança
      `ChaveIdempotenciaRegistradaException` e `ConflitoConcorrenciaException`;
    - `void DescartarAlteracoes()`.
  - **`IGeradorNumeroPedido.cs`**: `Task<string> ProximoAsync(DateTimeOffset instante, CancellationToken)`.
  - **`RegistroIdempotencia.cs`**:
    - classe `sealed` com `ClienteId`, `Chave`, `Impressao`, `PedidoId`, `Resposta`
      (string) e `CriadoEm`;
    - construtor público com todos os campos, setters privados e construtor privado sem
      parâmetros para o EF.
  - **`ChaveIdempotenciaRegistradaException.cs`**: `sealed`, com a exceção interna.
  - **`ImpressaoConteudo.cs`**: `public static string Calcular(CriarPedidoRequestDTO pedido)`.
    Texto canônico conforme o contrato; `SHA256.HashData` sobre o UTF-8 e
    `Convert.ToHexString`, em maiúsculas.

  A T003 deve passar.
- [ ] T007 [P] Criar `src/PedidoFulfillment.Application/Pedidos/MapeamentoPedido.cs`:
  - `internal static PedidoResponseDTO ParaResposta(this Pedido pedido)`;
  - os itens em ordem de `ProdutoId`, para que a resposta e a repetição sejam estáveis;
  - copia número, cliente, status, valores, moeda e datas.
- [ ] T008 [P] Criar `src/PedidoFulfillment.Application/Pedidos/EventosPedidos.cs` (`internal static partial`, `LoggerMessage`, R-11):
  - 4001 `PedidoCriado` (Information): `PedidoId`, `Numero`, `QuantidadeItens` e
    `ValorTotal`;
  - 4002 `PedidoRepetido` (Information): `PedidoId`;
  - 4003 `StatusPedidoAlterado` (Information): `PedidoId`, `StatusAnterior`, `StatusNovo`
    e `PapelAutor`.
- [ ] T009 Infrastructure, idempotência e sequência (R-03, R-05):
  - criar `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/ConfiguracaoRegistroIdempotencia.cs`:
    - tabela `ChavesIdempotencia`;
    - `HasKey(r => new { r.ClienteId, r.Chave }).HasName("PK_ChavesIdempotencia")`;
    - `Impressao` `HasMaxLength(64).IsFixedLength()`;
    - `Resposta` sem limite;
    - `HasOne<Pedido>().WithMany().HasForeignKey(r => r.PedidoId).OnDelete(Cascade)`;
    - índice `IX_ChavesIdempotencia_CriadoEm`.
  - em `ContextoBancoDados.cs`:
    - `DbSet<RegistroIdempotencia> ChavesIdempotencia`;
    - em `OnModelCreating`, `builder.HasSequence<long>("SequenciaNumeroPedido").StartsAt(1)`;
    - constante pública `NomeChavePrimariaIdempotencia = "PK_ChavesIdempotencia"`.
  - em `ErrosBancoDados.cs`, a sobrecarga
    `EhViolacaoUnicidade(Exception excecao, string restricao)`, que confere o número e se
    `SqlException.Message` contém `restricao`.
- [ ] T010 Criar `src/PedidoFulfillment.Infrastructure/Persistencia/ArmazenamentoPedidos.cs` (`internal sealed`, sobre o `ContextoBancoDados`):
  - **Leituras**:
    - produtos com `AsNoTracking` e `Where(p => ids.Contains(p.Id))`;
    - o pedido com `Include(p => p.Itens)`, `AsNoTracking` quando não for `paraAlterar`;
    - o registro de idempotência pela chave composta, com `AsNoTracking`.
  - **`Adicionar`**: inclui o pedido e o registro.
  - **`SalvarAsync`**: chama `SaveChangesAsync` e traduz `DbUpdateException` com
    `EhViolacaoUnicidade(e, NomeChavePrimariaIdempotencia)` para
    `ChaveIdempotenciaRegistradaException`. O `ConflitoConcorrenciaException` já vem
    traduzido pelo contexto. Uma exceção com `SqlException` 1205 (vítima de deadlock)
    também vira `ChaveIdempotenciaRegistradaException` (R-04, achado M1). Para isso,
    acrescentar `EhDeadlock(Exception)` em `ErrosBancoDados.cs`.
  - **`DescartarAlteracoes`**: `ChangeTracker.Clear()`.
- [ ] T011 Criar `src/PedidoFulfillment.Infrastructure/Persistencia/GeradorNumeroPedido.cs` (`internal sealed`):
  - `SELECT NEXT VALUE FOR SequenciaNumeroPedido` via
    `contexto.Database.SqlQueryRaw<long>(...)`, ou `DbCommand` na conexão do contexto;
  - formato `$"PED-{instante.UtcDateTime:yyyyMMdd}-{valor % 1_000_000:D6}"`, com
    `CultureInfo.InvariantCulture`.
- [ ] T012 Registrar `IArmazenamentoPedidos` → `ArmazenamentoPedidos` e `IGeradorNumeroPedido` → `GeradorNumeroPedido` (`scoped`) em `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs`.
- [ ] T013 Gerar a migration com `dotnet ef migrations add ChavesIdempotencia --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`. Revisar:
  - só `CreateSequence` e `CreateTable`/`CreateIndex` de `ChavesIdempotencia`;
  - a chave primária com o nome `PK_ChavesIdempotencia`;
  - a FK para `Pedidos` com `Cascade`.

  `dotnet ef migrations has-pending-model-changes` não acusa nada, e a T004 passa.
- [ ] T014 [P] Criar os falsos unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/Falsos.cs`:
  - **`ArmazenamentoPedidosFalso`**:
    - listas em memória de produtos, pedidos e registros;
    - `Salvar` move os adicionados para as listas;
    - opções para simular, na próxima gravação, `ChaveIdempotenciaRegistradaException`
      (com um registro "vencedor" opcional, que aparece depois do descarte) e N
      `ConflitoConcorrenciaException` seguidos (com uma ação que altera o pedido guardado,
      para simular a outra operação);
    - contadores de chamadas.
  - **`GeradorNumeroPedidoFalso`**: números sequenciais no formato do contrato.
  - **Relógio**: `TimeProvider` fixo (`FakeTimeProvider` não está disponível sem pacote
    novo; criar `RelogioFixo : TimeProvider`).
  - **Logs**: reaproveitar o `LoggerFalso<T>` de
    `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/Falsos.cs` para afirmar os
    eventos 4001 a 4003 (achado L2).
- [ ] T015 [P] Criar `tests/PedidoFulfillment.Testes.Integracao/Pedidos/CenarioPedidos.cs` para os testes de integração:
  - `ApiAsync(BancoDadosTeste banco)`: uma `FabricaApi` com `PrepararBanco = true`, banco
    novo e `Configuracoes = UsuariosIniciaisComBancoTestes.Configuracao()`, que já traz o
    Admin e o OperadorLogistica de teste (achado L1).
  - Tokens: `TokenClienteAsync` (registro e login de um Cliente com e-mail único),
    `TokenAdminAsync` e `TokenOperadorAsync`.
  - `CriarAsync(HttpClient, Guid chave, params (Guid produto, int quantidade)[] itens)`.
  - Os IDs do catálogo inicial: `Cat001` … `Cat010`.
- [ ] T016 Rodar `dotnet build` e `dotnet test`: T002–T004 verdes, o resto da suíte verde e zero avisos.

**Checkpoint**: portas, persistência da idempotência e número prontos.

---

## Phase 3: User Story 1 — Criar um pedido (Priority: P1) 🎯 MVP

**Goal**: o Cliente cria o pedido com preços do catálogo, dono do token e status
`Pendente`. Produto inexistente ou indisponível responde 422.

**Independent Test**: criar com `CAT-001` × 2 e `CAT-002` × 1 e conferir 201, `Location`,
valores e número. Criar com produto inexistente, inativo, sem estoque e com estoque menor
que a quantidade, e conferir os 422.

### Testes da US1

- [ ] T017 [P] [US1] Unitários de criação em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosCriacaoTestes.cs`, com os falsos da T014:
  - **Sucesso**: um pedido com dois produtos ativos e com estoque gera:
    - `Pendente`, `ClienteId` do parâmetro e moeda `BRL`;
    - itens com nome e preço do produto, subtotal e total corretos;
    - número vindo do gerador;
    - `CriadoEm` igual ao relógio;
    - uma única gravação com o pedido e o registro (impressão da `ImpressaoConteudo`,
      `Resposta` com o JSON do DTO devolvido);
    - estoque inalterado (FR-004).
  - **422** (FR-003):
    - produto inexistente: `PRODUTO_INEXISTENTE`;
    - produto inativo: `PRODUTO_INDISPONIVEL`;
    - estoque 1 com quantidade 2: `PRODUTO_INDISPONIVEL`;
    - estoque igual à quantidade: sucesso;
    - um inexistente e um inativo: um só 422 `PRODUTO_INEXISTENTE`, com os dois IDs no
      `Detalhe`;
    - em todos os casos de erro, nenhuma gravação.
  - **Log**: o evento 4001 `PedidoCriado` é emitido, sem o corpo da requisição.
- [ ] T018 [P] [US1] Integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/CriacaoPedidoComBancoTestes.cs` (cenários 1 a 7 da US1):
  - **Criação** com `CAT-001` × 2 e `CAT-002` × 1:
    - 201 e `Location` = `/api/v1/pedidos/{id}`;
    - corpo com `status` `Pendente`, `valorTotal` 829.70, `moeda` `BRL`, `clienteId` igual
      ao `sub` do token e `numero` que casa com `^PED-\d{8}-\d{6}$`, com a data UTC de
      hoje;
    - no banco: o pedido, os 2 itens, a entrada de criação no histórico e 1 linha em
      `ChavesIdempotencia`;
    - o estoque de `CAT-001` continua 50.
  - **422**:
    - `CAT-009` (estoque zero), `CAT-010` (inativo) e `CAT-008` × 2 (estoque 1):
      `PRODUTO_INDISPONIVEL`;
    - um produto inexistente junto com `CAT-009`: `PRODUTO_INEXISTENTE`, com os dois IDs
      no `detail`;
    - em todos os casos, `ChavesIdempotencia` e `Pedidos` continuam vazias.
  - **403** com o token de Admin.
  - **400** sem o cabeçalho de idempotência.
  - **Usuário apagado** (Edge Cases, achado M2): um token emitido pelo `IEmissorToken` para
    um usuário inexistente, com perfil Cliente, recebe 500 em ProblemDetails sem detalhes
    internos; `Pedidos` e `ChavesIdempotencia` continuam vazias.

### Implementação da US1

- [ ] T019 [US1] Criar `src/PedidoFulfillment.Application/Pedidos/ServicoPedidos.cs`:
  - classe `sealed`, com construtor primário recebendo `IArmazenamentoPedidos`,
    `IGeradorNumeroPedido`, `TimeProvider` e `ILogger<ServicoPedidos>`.
  - **`CriarAsync`**, nesta história sem consultar a chave antes e sem tratar a corrida
    (US2):
    1. calcular a impressão;
    2. ler os produtos e validar conforme a R-07;
    3. gerar o número e criar o `Pedido`;
    4. `var resposta = pedido.ParaResposta()`;
    5. serializar a `resposta` com
       `JsonSerializer.Serialize(resposta, OpcoesJsonRegistro)`, usando
       `JsonSerializerDefaults.Web` e `JsonStringEnumConverter`;
    6. `Adicionar` o pedido e o registro, `SalvarAsync`, logar 4001 e devolver a
       `resposta`.

    As falhas da R-07 são `Falha` estáticas, com `CodigosErro`. O `Detalhe` lista os IDs
    em ordem.
  - **`ObterAsync` e `AtualizarStatusAsync`**: continuam devolvendo
    `Falha(TipoFalha.NaoImplementado, ...)` até a US3 e a US4, com um comentário que diz
    isso.
- [ ] T020 [US1] Trocar o serviço:
  - em `src/PedidoFulfillment.API/Program.cs`, registrar
    `builder.Services.AddScoped<IServicoPedidos, ServicoPedidos>()` no lugar do
    `ServicoNaoImplementado`;
  - remover `src/PedidoFulfillment.API/Provisorio/ServicoNaoImplementado.cs` e a pasta;
  - atualizar o comentário de `src/PedidoFulfillment.Application/Pedidos/IServicoPedidos.cs`
    para "Operações de pedidos (REQ-05)".
- [ ] T021 [US1] Ajustar os testes do REQ-03 que usavam o 501 (R-10):
  - **`tests/PedidoFulfillment.Testes.Integracao/Autenticacao/AutorizacaoComTokensReaisTestes.cs`**:
    um perfil autorizado recebe status diferente de 401 e de 403 (comentário: "passou pela
    autorização e chegou ao serviço de pedidos"); um perfil não autorizado recebe 403.
  - **`tests/PedidoFulfillment.Testes.Integracao/Autenticacao/EmissorTokenTestes.cs`**:
    `FabricaApi` com `ServicoPedidos = new ServicoPedidosFalso { ResultadoCriar = Exemplos.Pedido() }`.
    O Cliente recebe 201, e o Admin, 403.
  - **`tests/PedidoFulfillment.Testes.Integracao/Autenticacao/LoginComBancoTestes.cs`**:
    o token do login passa pela autorização, com status diferente de 401 e de 403.
- [ ] T022 [US1] Rodar T017–T018 e a suíte inteira com o Docker no ar e corrigir até ficarem verdes.

**Checkpoint**: criação real de ponta a ponta.

---

## Phase 4: User Story 2 — Repetir a criação sem duplicar (Priority: P1)

**Goal**: a mesma chave com o mesmo conteúdo devolve a resposta original. Conteúdo
diferente recebe 409. A concorrência nunca cria dois pedidos.

**Independent Test**: repetir uma criação e conferir a resposta idêntica e a contagem de
pedidos. Repetir com outro conteúdo e conferir o 409. Disparar 20 requisições iguais em
paralelo e conferir que existe 1 pedido.

### Testes da US2

- [ ] T023 [P] [US2] Unitários de idempotência em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosIdempotenciaTestes.cs`:
  - **Registro existente**:
    - com a mesma impressão, devolve o DTO desserializado da `Resposta`, igual ao
      original, mesmo que o pedido guardado tenha mudado de status; não grava nada e
      loga 4002;
    - com impressão diferente, devolve 409 `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`.
  - **Itens em outra ordem**: tratados como repetição.
  - **Corrida** (`ChaveIdempotenciaRegistradaException` na gravação):
    - o vencedor com a mesma impressão devolve a resposta dele;
    - o vencedor com impressão diferente devolve 409 divergente;
    - sem vencedor visível, devolve 409 `IDEMPOTENCIA_EM_PROCESSAMENTO`;
    - nos três casos, `DescartarAlteracoes` foi chamado.
  - **Depois de um 422**: a mesma chave cria o pedido (FR-013).
- [ ] T024 [P] [US2] Integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/IdempotenciaComBancoTestes.cs` (cenários 1 a 6 da US2):
  - **Repetição**: mesmo status, mesmo `Location` e corpo igual por valor (mesmos campos e
    valores, com decimais comparados numericamente; achado M3); 1 pedido.
  - **Itens em outra ordem**: igual.
  - **Outra quantidade**: 409 `IDEMPOTENCIA_CONTEUDO_DIVERGENTE`.
  - **Mesma chave com outro Cliente**: 201, com outro pedido.
  - **Depois de um 422**: a mesma chave cria o pedido quando o conteúdo é corrigido.
  - **Depois de cancelar o pedido** pelo PATCH (quando a US4 estiver pronta; até lá,
    mudar o status por SQL): a repetição continua com `status` `Pendente`.
- [ ] T025 [P] [US2] Concorrência em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ConcorrenciaIdempotenciaTestes.cs` (SC-002):
  - 10 repetições, cada uma com uma chave nova e 20 `POST` iguais e simultâneos
    (`Task.WhenAll` com clientes HTTP da mesma `FabricaApi`).
  - Em cada repetição:
    - exatamente 1 linha em `Pedidos` para a chave (via `ChavesIdempotencia`);
    - todas as respostas são 201 com o mesmo `id` ou 409
      `IDEMPOTENCIA_EM_PROCESSAMENTO`;
    - nenhuma é 500.

### Implementação da US2

- [ ] T026 [US2] Completar `CriarAsync` em `ServicoPedidos.cs` (R-04):
  - ler o registro antes de validar os produtos;
  - envolver a gravação em `try/catch (ChaveIdempotenciaRegistradaException)`: chamar
    `DescartarAlteracoes()`, reler o registro e decidir entre repetição, divergente ou em
    processamento;
  - extrair um método privado `ResponderRegistro(registro, impressao)`, usado nos dois
    caminhos, que loga 4002 na repetição.
- [ ] T027 [US2] Rodar T023–T025 com o Docker no ar e corrigir até ficarem verdes.

**Checkpoint**: idempotência comprovada, inclusive sob concorrência.

---

## Phase 5: User Story 3 — Consultar um pedido (Priority: P2)

**Goal**: o dono, o Admin e o OperadorLogistica veem o pedido. Outro Cliente recebe o mesmo
404 de um pedido inexistente.

**Independent Test**: criar com o Cliente A e consultar com A, B, Admin, Operador e com um
ID inexistente.

### Testes da US3

- [ ] T028 [P] [US3] Unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosConsultaTestes.cs`:
  - o dono recebe o estado atual, inclusive status e `AtualizadoEm` alterados;
  - Admin e OperadorLogistica recebem o pedido de qualquer pessoa;
  - outro Cliente e um pedido inexistente recebem uma `Falha` igual (mesmo tipo, título e
    detalhe; FR-015);
  - um usuário com Cliente **e** Admin vê qualquer pedido.
- [ ] T029 [P] [US3] Integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/ConsultaPedidoComBancoTestes.cs` (cenários 1 a 4):
  - **200** com o dono, o Admin e o Operador; o corpo é igual ao da criação;
  - **404** com o Cliente B e com um ID inexistente, e os dois corpos têm o mesmo `title`
    e o mesmo `detail`;
  - **400** com um ID mal formado (já existia).

### Implementação da US3

- [ ] T030 [US3] Implementar `ObterAsync` em `ServicoPedidos.cs` (R-08):
  - leitura sem rastreamento;
  - uma `Falha` estática `PedidoNaoEncontrado` (`NaoEncontrado`, "Pedido não
    encontrado.");
  - um método privado `EhOperacao(perfis)`, verdadeiro com Admin ou OperadorLogistica.
- [ ] T031 [US3] Rodar T028–T029 e corrigir até ficarem verdes.

---

## Phase 6: User Story 4 — Mudar o status de um pedido (Priority: P2)

**Goal**: transições do contrato v1, histórico com autor e motivo e reavaliação em
conflito.

**Independent Test**: percorrer o ciclo com o Operador, tentar uma transição inválida (409)
e cancelar com o Cliente um pedido `Pendente` (200) e um `Processando` (403).

### Testes da US4

- [ ] T032 [P] [US4] Unitários em `tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ServicoPedidosStatusTestes.cs`:
  - **Operação**: cada transição permitida responde sucesso, grava o histórico com autor e
    motivo aparado e loga 4003 com `PapelAutor` `Operacao`.
  - **Transição inválida**: 409 `PEDIDO_TRANSICAO_INVALIDA`, com
    "O pedido está em {atual} e não pode passar para {solicitado}." no `Detalhe`, e nada é
    gravado.
  - **Cliente dono**: Pendente→Cancelado responde sucesso. Processando→Cancelado e
    Pendente→Processando respondem 403.
  - **Outro Cliente**: 404. **Pedido inexistente**: 404.
  - **Reavaliação**:
    - 1 conflito, com o pedido guardado alterado para `Processando` pela "outra operação",
      seguido do cancelamento do Cliente: na reavaliação, 403;
    - 1 conflito e o Operador pedindo `Cancelado` sobre o pedido agora `Processando`:
      sucesso;
    - 3 conflitos seguidos: 409 `PEDIDO_TRANSICAO_INVALIDA`, com "O pedido foi alterado
      por outra operação; tente novamente.";
    - `DescartarAlteracoes` é chamado a cada conflito.
- [ ] T033 [P] [US4] Integração em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/StatusPedidoComBancoTestes.cs` (cenários 1 a 8):
  - **Ciclo do Operador**: Pendente→Processando→ProntoParaEnvio→Despachado, sempre 200.
    Depois, o `GET` mostra `Despachado`, e o histórico no banco tem 4 entradas com o
    `AutorId` do operador.
  - **Despachado→Pendente**: 409 `PEDIDO_TRANSICAO_INVALIDA`, com os dois status no
    `detail`.
  - **Admin**: cancela um pedido `ProntoParaEnvio` com motivo.
  - **Cliente dono**: cancela um pedido `Pendente` com motivo (200; histórico com autor e
    motivo) e recebe 403 ao cancelar um `Processando`.
  - **404**: Cliente B e ID inexistente.
  - **400**: `PagamentoRejeitado` e `Cancelado` sem motivo (já existiam).
  - **Corrida** (cenário 8), 10 repetições com um pedido `Pendente` novo:
    - o Cliente cancela e o Operador pede `Processando`, simultaneamente;
    - o status final é `Cancelado` (com o Operador recebendo 409) ou `Processando` (com o
      Cliente recebendo 403);
    - o histórico tem exatamente 2 entradas;
    - nenhuma resposta é 500.

### Implementação da US4

- [ ] T034 [US4] Implementar `AtualizarStatusAsync` em `ServicoPedidos.cs`, conforme as R-06 e R-09:
  - laço de até 3 tentativas: ler com `paraAlterar`, aplicar as regras, chamar
    `MudarStatus` e gravar;
  - em `ConflitoConcorrenciaException`, `DescartarAlteracoes()` e tentar de novo;
  - logar 4003.
- [ ] T035 [US4] Remover `TipoFalha.NaoImplementado` de `src/PedidoFulfillment.Application/Comum/Resultado.cs`, o caso 501 de `src/PedidoFulfillment.API/Erros/ExtensoesResultado.cs` e o título 501 de `src/PedidoFulfillment.API/Erros/ConfiguracaoProblemDetails.cs` (R-10, FR-022).
- [ ] T036 [US4] Rodar T032–T033 e a suíte inteira e corrigir até ficarem verdes. Atualizar a T024: o caso "depois de cancelar" passa a usar o PATCH.

**Checkpoint**: as três operações em funcionamento; nenhum 501 no código.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T037 [P] Números simultâneos em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/NumeroPedidoTestes.cs` (SC-003):
  - 50 `POST` simultâneos com chaves diferentes, por 5 Clientes;
  - 50 respostas 201, com 50 números distintos e no padrão do contrato.
- [ ] T038 [P] Desempenho em `tests/PedidoFulfillment.Testes.Integracao/Pedidos/DesempenhoCriacaoTestes.cs` (SC-005):
  - 5 criações de aquecimento;
  - 40 criações sequenciais com 10 itens (`CAT-001` a `CAT-007` em quantidade 1, mais
    produtos de teste inseridos por SQL até completar 10), cada uma medida com
    `Stopwatch` pelo cliente HTTP;
  - o percentil 95 fica abaixo de 500 ms.
- [ ] T039 [P] Logs: em `CriacaoPedidoComBancoTestes`, com `CapturaLogs`, afirmar que os eventos `PedidoCriado` e `StatusPedidoAlterado` aparecem e que nenhum texto de log contém o e-mail do Cliente, o token ou `"itens"` (FR-020).
- [ ] T040 Atualizar `README.md`:
  - **`## Estado atual`**: linha do REQ-05 com link para a spec e "Criação com idempotência, consulta e mudança de status de pedidos"; a linha de pendentes passa a "REQ-06 em diante".
  - Remover o aviso de 501.
  - **Roteiros de validação**: acrescentar o quickstart do REQ-05.
  - **`## Próximos passos`**: tirar a linha do REQ-05; atualizar a #11, que fica só com o
    rastreio e o histórico na consulta.
  - **Banco**: citar a terceira migration e a tabela `ChavesIdempotencia`.
- [ ] T041 Varredura de segredos (`git grep -n -i -E "password|senha|pwd|chave"` nos arquivos alterados) e `dotnet format PedidoFulfillment.sln --verify-no-changes`.
- [ ] T042 Executar o quickstart (§1 a §7) com o Docker no ar e registrar o resultado em Notes.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup**: sem dependências.
- **Foundational**: depende da Setup e bloqueia todas as histórias. A migration (T013)
  vem antes de qualquer teste com banco.
- **US1**: depende da Foundational. Cria o `ServicoPedidos` e troca o provisório.
- **US2, US3 e US4**: dependem da US1, porque editam o mesmo `ServicoPedidos.cs`, em
  sequência. Os testes delas podem ser escritos em paralelo.
- **Polish**: depois das histórias. Na US4, a T035 (remoção do `NaoImplementado`) só vem
  depois da T030 e da T034, quando nenhum método devolve mais 501.

### Within Each Phase

- Testes antes da implementação, e falhando primeiro.
- Fundação: T005/T006/T007/T008 → T009 → T010/T011 → T012 → T013. A T014 e a T015 podem
  correr em paralelo a partir da T006.
- `ServicoPedidos.cs`: T019 → T026 → T030 → T034, em sequência.
- `ContextoBancoDados.cs` e `ErrosBancoDados.cs`: só na T009.

### Parallel Opportunities

- T002, T003, T005, T006, T007 e T008.
- T014 e T015.
- Os testes de cada história: T017/T018, T023/T024/T025, T028/T029 e T032/T033.
- T037, T038 e T039.

---

## Parallel Example: Foundational

```text
Task: "TransicoesStatusPedidoTestes em tests/PedidoFulfillment.Testes.Unitarios/Dominio/TransicoesStatusPedidoTestes.cs"
Task: "ImpressaoConteudoTestes em tests/PedidoFulfillment.Testes.Unitarios/Pedidos/ImpressaoConteudoTestes.cs"
Task: "TransicoesStatusPedido em src/PedidoFulfillment.Domain/Pedidos/TransicoesStatusPedido.cs"
Task: "Portas e tipos em src/PedidoFulfillment.Application/Pedidos/"
Task: "MapeamentoPedido em src/PedidoFulfillment.Application/Pedidos/MapeamentoPedido.cs"
Task: "EventosPedidos em src/PedidoFulfillment.Application/Pedidos/EventosPedidos.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Setup e Foundational.
2. US1: criação real.
3. **PARAR e VALIDAR** com o quickstart §3.

### Incremental Delivery

1. Fundação → commit `feat(pedidos): cria portas, idempotencia persistida e numero do pedido [REQ-05]`.
2. US1 → commit `feat(pedidos): implementa a criacao de pedidos [REQ-05]`.
3. US2 → commit `feat(pedidos): implementa a idempotencia da criacao [REQ-05]`.
4. US3 → commit `feat(pedidos): implementa a consulta de pedidos [REQ-05]`.
5. US4 → commit `feat(pedidos): implementa a mudanca de status e remove o 501 [REQ-05]`.
6. Polish → commit `chore(pedidos): ... [REQ-05]`.

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- Entre a US1 e a US4, consulta e mudança de status continuam respondendo 501 pelo próprio
  `ServicoPedidos`. O valor `NaoImplementado` só sai na T035.
- Publicação de eventos e outbox: REQ-06.
- Retenção das chaves (FR-009, US2 cenário 7): garantida por nenhuma chave ser apagada
  neste REQ; não há teste de tempo (achado L3).
- `/speckit-analyze` (2026-10-22), achados aplicados:
  - **M1**: deadlock tratado como corrida de chave (T010, R-04).
  - **M2**: caso do usuário apagado (T018).
  - **M3**: comparação por valor na repetição (T024).
  - **M4**: "papel do autor" no FR-020 e na R-11.
  - **L1**: configuração de usuários iniciais reaproveitada (T015).
  - **L2**: `LoggerFalso<T>` reaproveitado (T014).
  - **L3**: nota de retenção.
  - **L4**: Princípio IV como "➖ N/A" no plano.
  - **L5**: spec `Approved`.
- Fazer commit ao fim de cada fase.
