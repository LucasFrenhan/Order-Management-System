---

description: "Lista de tarefas do REQ-04 — Domínio de pedidos e persistência"
---

# Tasks: Domínio de pedidos e persistência

**Input**: Design documents from `/specs/004-dominio-persistencia/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/persistencia.md](./contracts/persistencia.md),
[contracts/catalogo-inicial.md](./contracts/catalogo-inicial.md), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. O Princípio V da constituição exige testes unitários para as
regras do domínio. A spec exige teste automatizado contra banco real para cada restrição
(SC-004), para a concorrência (SC-002), para a atualização do banco (SC-003) e para o
modelo sem migration pendente (FR-016). Os testes de cada fase são escritos antes da
implementação e devem falhar primeiro.

**Organization**: por história de usuário. As entidades, o mapeamento e a migration
`EntidadesNegocio` ficam na fase Foundational: a spec pede uma migration só (FR-014), e
todas as histórias precisam do esquema completo para os testes com banco real. As histórias
validam esse esquema contra o SQL Server e acrescentam o que é só delas: o catálogo inicial
(US4) e as correções que os testes revelarem.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Raiz da solução: `PedidoFulfillment.sln`; código em `src/`, testes em `tests/`.
- Nomes de classes, arquivos, namespaces, tabelas, colunas, constraints e mensagens em
  português (constituição, FR-017).
- `TreatWarningsAsErrors` está ligado: nenhuma tarefa pode deixar aviso.
  `dotnet format --verify-no-changes` deve continuar passando.
- Nenhum pacote NuGet novo (R-11).
- O `PedidoFulfillment.Domain` não referencia nenhum projeto nem pacote. O teste
  `DependenciasCamadasTestes` precisa continuar verde.
- Contrato v1 intocado (FR-018): nenhuma rota, DTO, status ou código de erro novo. As rotas
  de pedido continuam respondendo 501.
- Instantes sempre UTC (deslocamento zero), vindos de fora. O domínio não lê o relógio
  (R-02).
- Valores monetários: `decimal(18,2)`, arredondados no domínio com
  `Math.Round(valor, 2, MidpointRounding.ToEven)` (R-06).
- Testes com banco real usam a fixture `BancoDadosTeste` (coleção `ColecaoBancoDados`), com
  um banco novo por teste. Sem Docker, eles são ignorados com "Docker indisponível". O resto
  da suíte roda sem Docker.
- Commits: `tipo(escopo): descrição [REQ-04]`.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: confirmar a linha de base. Não há pacote nem projeto novo.

- [X] T001 Rodar `dotnet tool restore`, `dotnet build PedidoFulfillment.sln` e `dotnet test PedidoFulfillment.sln` na branch `feature/req-04-dominio-persistencia` e registrar a contagem de testes atual. Esperado: zero avisos e tudo verde, que é a linha de base do FR-019.
- [X] T002 [P] Criar as pastas novas com um arquivo cada, conforme a estrutura do plan.md:
  - `src/PedidoFulfillment.Domain/Produtos/`, `src/PedidoFulfillment.Domain/Pagamentos/` e `src/PedidoFulfillment.Domain/Entregas/`;
  - `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/`;
  - `tests/PedidoFulfillment.Testes.Unitarios/Dominio/` e `tests/PedidoFulfillment.Testes.Integracao/Persistencia/`.

  As pastas nascem com os arquivos das tarefas seguintes. Esta tarefa só confere que nenhum
  `.csproj` exclui essas pastas.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: entidades do domínio, exceção de conflito, mapeamento, migration
`EntidadesNegocio` e utilitários de teste.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T003 [P] Testes de `Produto` em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/ProdutoTestes.cs`:
  - **Construção**: a partir de sku, nome, descrição, preço, estoque, ativo e instante, o
    produto gera um `Id` v7, apara `Sku` e `Nome` e preenche `CriadoEm` = `AtualizadoEm` =
    instante.
  - **Construção recusada**:
    - sku ou nome vazio ou só com espaços;
    - `Sku` > 50, `Nome` > 200 ou `Descricao` > 1000 caracteres;
    - preço < 0;
    - estoque < 0;
    - instante com deslocamento diferente de zero.
  - **Preço**: arredondado para 2 casas (`10.005m` → `10.00m`; `10.015m` → `10.02m`).
  - **`AjustarEstoque(+5)` e `AjustarEstoque(-3)`**: alteram a quantidade e o
    `AtualizadoEm`.
  - **`AjustarEstoque` que deixaria o estoque negativo**: lança `InvalidOperationException`
    e não altera nada.
  - **`AlterarDados`, `Ativar` e `Desativar`**: alteram os campos e o `AtualizadoEm`.
- [X] T004 [P] Testes de `Pedido`, `ItemPedido` e `HistoricoStatusPedido` em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoTestes.cs`:
  - **Snapshot do item**: o item copia `Nome` → `NomeProduto` e `Preco` → `PrecoUnitario`
    do produto. `Subtotal` = `PrecoUnitario × Quantidade`. Alterar o produto depois não
    muda o item.
  - **Quantidade do item**: ≤ 0 é recusada.
  - **Criação do pedido**:
    - status `Pendente`;
    - `Moeda` = `"BRL"`;
    - `ValorTotal` = soma dos subtotais;
    - `CriadoEm` = `AtualizadoEm` = instante;
    - exatamente uma entrada no histórico, com `StatusAnterior` nulo, `StatusNovo`
      `Pendente`, `OcorridoEm` = instante, `AutorId` nulo e `Motivo` nulo.
  - **Criação recusada**:
    - lista de itens vazia;
    - mesmo produto duas vezes;
    - `ClienteId` vazio;
    - número vazio ou com tamanho diferente de 19.
  - **`MudarStatus(Processando, autorId, null, t1)` seguido de
    `MudarStatus(Cancelado, autorId, "Desistência", t2)`**:
    - o status fica `Cancelado`;
    - `AtualizadoEm` = t2;
    - o histórico tem 3 entradas na ordem, com os status anterior e novo encadeados, e a
      última tem o motivo.
  - **Motivo**: > 500 caracteres é recusado.
  - **`MudarStatus` sem validar transição**: aceita `Despachado → Pendente`. A regra de
    transição é do REQ-05 (R-02); o teste documenta isso.
- [X] T005 [P] Testes de `Pagamento` e `OrdemEntrega` em `tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEntregaTestes.cs`:
  - **Pagamento criado**: situação `Pendente`, valor arredondado; valor negativo é
    recusado.
  - **`RegistrarResultado(Aprovado, "TX-1", t)`**: preenche a situação, o
    `IdentificadorTransacao`, `ProcessadoEm` e `AtualizadoEm`.
  - **`IdentificadorTransacao`**: > 100 caracteres é recusado.
  - **Ordem de entrega criada**: sem transportadora, código ou `DespachadoEm`.
  - **`RegistrarDespacho("Transportadora X", "BR123", t)`**: preenche os três e o
    `AtualizadoEm`.
  - **Despacho recusado**: transportadora ou código vazio, transportadora > 100 ou
    código > 50.
- [X] T006 [P] Teste sem Docker em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ModeloSincronizadoTestes.cs` (FR-016):
  - Montar um `ContextoBancoDados` com `UseSqlServer(FabricaApi.StringConexaoTeste)`, sem
    abrir conexão.
  - Afirmar `contexto.Database.HasPendingModelChanges()` == `false`.
  - Afirmar que `contexto.Database.GetMigrations()` termina com `InicialIdentidade` seguida
    de `EntidadesNegocio`.

### Implementação da fundação

- [X] T007 [P] Criar `src/PedidoFulfillment.Domain/Pagamentos/SituacaoPagamento.cs`: enum `Pendente = 1`, `Aprovado` e `Recusado`, com o mesmo comentário do `StatusPedido` sobre começar em 1.
- [X] T008 [P] Criar `src/PedidoFulfillment.Application/Comum/ConflitoConcorrenciaException.cs`:
  - classe `sealed`, derivada de `Exception`;
  - propriedades `string Entidade` e `string Identificador`;
  - mensagem "O registro {Entidade} {Identificador} foi alterado por outra operação desde que foi lido.";
  - construtor que recebe também a exceção interna;
  - comentário citando FR-003, FR-021 e `contracts/persistencia.md`.
- [X] T009 [P] Criar `src/PedidoFulfillment.Domain/Produtos/Produto.cs` conforme o data-model:
  - **Propriedades**, todas com setter privado:
    - `Id` (`Guid.CreateVersion7()`);
    - `Sku` ("obrigatório", 50);
    - `Nome` ("obrigatório, aparado", 200);
    - `Descricao` (opcional, 1000);
    - `Preco` ("≥ 0", 2 casas);
    - `QuantidadeEstoque` ("≥ 0");
    - `Ativo`;
    - `CriadoEm` e `AtualizadoEm` (`DateTimeOffset`, UTC).
  - **Construtores**: um construtor público com guardas que fazem T003 passar e um
    construtor privado sem parâmetros, para o EF.
  - **Métodos**:
    - `AjustarEstoque(int variacao, DateTimeOffset instante)`;
    - `AlterarDados(string nome, string? descricao, decimal preco, DateTimeOffset instante)`;
    - `Ativar(DateTimeOffset instante)` e `Desativar(DateTimeOffset instante)`.
  - **Helpers internos**: a guarda de instante UTC e o arredondamento ficam num helper
    `internal static` do Domain, em `src/PedidoFulfillment.Domain/Comum/Guardas.cs`, usado
    por todas as entidades.
- [X] T010 [P] Criar as entidades de `src/PedidoFulfillment.Domain/Pedidos/`, conforme o data-model:
  - **`ItemPedido.cs`**:
    - propriedades `PedidoId`, `ProdutoId`, `NomeProduto` (200), `Quantidade` ("> 0"),
      `PrecoUnitario` e `Subtotal`;
    - construtor `internal ItemPedido(Guid pedidoId, Produto produto, int quantidade)`, que
      faz o snapshot.
  - **`HistoricoStatusPedido.cs`**:
    - propriedades `long Id`, `PedidoId`, `StatusPedido? StatusAnterior`,
      `StatusPedido StatusNovo`, `OcorridoEm`, `Guid? AutorId` e `Motivo` ("opcional, até
      500 caracteres");
    - construtor `internal`;
    - sem métodos de alteração.
  - **`Pedido.cs`**:
    - propriedades `Id`, `Numero` (19), `ClienteId`, `Status`, `ValorTotal`, `Moeda`
      (constante `"BRL"`), `CriadoEm` e `AtualizadoEm`;
    - coleções `Itens` e `Historico` como `IReadOnlyCollection`, com campos privados
      `itens` e `historico`;
    - navegações `Pagamento?` e `OrdemEntrega?`, com setter privado;
    - construtor público `Pedido(Guid clienteId, string numero, IEnumerable<(Produto Produto, int Quantidade)> itens, DateTimeOffset instante)`;
    - `MudarStatus(StatusPedido novo, Guid? autorId, string? motivo, DateTimeOffset instante)`;
    - construtor privado sem parâmetros.

  Os testes da T004 devem passar.
- [X] T011 [P] Criar as entidades de pagamento e entrega, conforme o data-model:
  - **`src/PedidoFulfillment.Domain/Pagamentos/Pagamento.cs`**:
    - propriedades `Id`, `PedidoId`, `Valor`, `Situacao`, `IdentificadorTransacao` (100),
      `ProcessadoEm?`, `CriadoEm` e `AtualizadoEm`;
    - construtor `Pagamento(Guid pedidoId, decimal valor, DateTimeOffset instante)`;
    - `RegistrarResultado(...)`.
  - **`src/PedidoFulfillment.Domain/Entregas/OrdemEntrega.cs`**:
    - propriedades `Id`, `PedidoId`, `Transportadora` (100), `CodigoRastreio` (50),
      `DespachadoEm?`, `CriadoEm` e `AtualizadoEm`;
    - construtor `OrdemEntrega(Guid pedidoId, DateTimeOffset instante)`;
    - `RegistrarDespacho(...)`.

  Os testes da T005 devem passar.
- [X] T012 [P] Em `src/PedidoFulfillment.Infrastructure/Persistencia/ErrosBancoDados.cs`, acrescentar `EhViolacaoRestricao(Exception)`: `SqlException.Number == 547`, que cobre check e chave estrangeira, procurando em toda a cadeia de exceções, como os métodos existentes. Acrescentar `<InternalsVisibleTo Include="PedidoFulfillment.Testes.Integracao" />` em `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`, para que os testes classifiquem as violações sem duplicar os códigos do SQL Server. Hoje nenhum projeto usa `InternalsVisibleTo`.
- [X] T013 Criar as configurações em `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/`, uma `internal sealed class` `IEntityTypeConfiguration<T>` por entidade. Os nomes seguem exatamente o data-model e a R-05.
  - **`ConfiguracaoProduto.cs`**:
    - tabela `Produtos`;
    - `Sku` `HasMaxLength(50)`, obrigatório, `UseCollation("SQL_Latin1_General_CP1_CI_AS")`
      (research R-06), índice único `IX_Produtos_Sku`;
    - `Nome` 200, obrigatório;
    - `Descricao` 1000;
    - `Preco` `HasPrecision(18, 2)`;
    - propriedade de sombra `Versao` (`byte[]`) com `IsRowVersion()`;
    - checks `CK_Produtos_PrecoNaoNegativo` (`[Preco] >= 0`) e
      `CK_Produtos_EstoqueNaoNegativo` (`[QuantidadeEstoque] >= 0`).
  - **`ConfiguracaoPedido.cs`**:
    - tabela `Pedidos`;
    - `Numero` 19, obrigatório, índice único `IX_Pedidos_Numero`;
    - `Status` `HasConversion<string>()` com `HasMaxLength(30)`;
    - `Moeda` 3, obrigatória;
    - `ValorTotal` (18,2);
    - sombra `Versao` `IsRowVersion()`;
    - `HasOne<Usuario>().WithMany().HasForeignKey(p => p.ClienteId).OnDelete(DeleteBehavior.Restrict)`;
    - índices `IX_Pedidos_ClienteId_CriadoEm` (`ClienteId`, `CriadoEm`) e
      `IX_Pedidos_Status`;
    - checks `CK_Pedidos_ValorTotalNaoNegativo` e `CK_Pedidos_StatusValido`
      (`[Status] IN (N'Pendente', ...)`, com a lista gerada de
      `Enum.GetNames<StatusPedido>()`);
    - navegações `Itens` e `Historico` com `UsePropertyAccessMode(PropertyAccessMode.Field)`
      e `OnDelete(Cascade)`;
    - `Pagamento` e `OrdemEntrega` um-para-um por `PedidoId`, com `OnDelete(Cascade)`.
  - **`ConfiguracaoItemPedido.cs`**:
    - tabela `ItensPedido`;
    - chave `(PedidoId, ProdutoId)`;
    - `NomeProduto` 200;
    - `PrecoUnitario` e `Subtotal` (18,2);
    - `HasOne<Produto>().WithMany().HasForeignKey(i => i.ProdutoId).OnDelete(DeleteBehavior.Restrict)`;
    - índice `IX_ItensPedido_ProdutoId`;
    - checks `CK_ItensPedido_QuantidadePositiva` (`[Quantidade] > 0`),
      `CK_ItensPedido_PrecoNaoNegativo` e `CK_ItensPedido_SubtotalNaoNegativo`.
  - **`ConfiguracaoHistoricoStatusPedido.cs`**:
    - tabela `HistoricosStatusPedido`;
    - `Id` `bigint` identity (`ValueGeneratedOnAdd`);
    - `StatusAnterior` e `StatusNovo` como texto 30;
    - `Motivo` 500;
    - `HasOne<Usuario>().WithMany().HasForeignKey(h => h.AutorId).OnDelete(DeleteBehavior.Restrict)`;
    - índice `IX_HistoricosStatusPedido_PedidoId_Id`;
    - check `CK_HistoricosStatusPedido_StatusValido`
      (`([StatusAnterior] IS NULL OR [StatusAnterior] IN (...)) AND [StatusNovo] IN (...)`).
  - **`ConfiguracaoPagamento.cs`**:
    - tabela `Pagamentos`;
    - índice único `IX_Pagamentos_PedidoId`;
    - `Valor` (18,2);
    - `Situacao` como texto 30;
    - `IdentificadorTransacao` 100;
    - checks `CK_Pagamentos_ValorNaoNegativo` e `CK_Pagamentos_SituacaoValida` (lista de
      `Enum.GetNames<SituacaoPagamento>()`).
  - **`ConfiguracaoOrdemEntrega.cs`**:
    - tabela `OrdensEntrega`;
    - índice único `IX_OrdensEntrega_PedidoId`;
    - `Transportadora` 100;
    - `CodigoRastreio` 50;
    - índice único `IX_OrdensEntrega_Transportadora_CodigoRastreio`, com
      `HasFilter("[CodigoRastreio] IS NOT NULL")`.

  Um helper `internal static` em
  `src/PedidoFulfillment.Infrastructure/Persistencia/Configuracoes/RestricoesEnum.cs` monta o
  SQL `IN (N'...', ...)` a partir de um enum.
- [X] T014 Atualizar `src/PedidoFulfillment.Infrastructure/Persistencia/ContextoBancoDados.cs`:
  - acrescentar os `DbSet` `Produtos`, `Pedidos`, `ItensPedido`, `HistoricosStatusPedido`,
    `Pagamentos` e `OrdensEntrega`;
  - em `OnModelCreating`, depois do mapeamento do Identity, chamar
    `builder.ApplyConfigurationsFromAssembly(typeof(ContextoBancoDados).Assembly)`;
  - sobrescrever `SaveChangesAsync(bool, CancellationToken)` e `SaveChanges(bool)` para
    capturar `DbUpdateConcurrencyException` e lançar `ConflitoConcorrenciaException`, com
    a exceção original como interna:
    - `Entidade` = `entry.Metadata.ClrType.Name` da primeira entrada;
    - `Identificador` = os valores de `entry.Metadata.FindPrimaryKey()` unidos por `/`;
    - **só quando** o `ClrType` da primeira entrada pertence ao assembly do Domain
      (`typeof(Pedido).Assembly`). Nos demais casos (entidades do Identity), usar `throw;`
      para relançar a exceção original, que o `UserStore` converte em
      `IdentityResult.Failed(ConcurrencyFailure)` (research R-04, FR-019). Um filtro
      `catch (...) when (...)` deixa isso explícito;
  - atualizar o comentário da classe: "o REQ-03 criou o Identity; o REQ-04 acrescentou as
    entidades de negócio (`Persistencia/Configuracoes`)".
- [X] T015 Gerar a migration com `dotnet ef migrations add EntidadesNegocio --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`. Se o snapshot sair fora da pasta, movê-lo como no REQ-03. Revisar o arquivo gerado (R-09):
  - só `CreateTable`, `CreateIndex` e `AddCheckConstraint` das 6 tabelas novas;
  - nenhum `AlterColumn`, `DropTable`, `DropIndex` nem `RenameTable` nas tabelas do
    Identity;
  - as FKs `Pedidos → Usuarios`, `HistoricosStatusPedido → Usuarios` e
    `ItensPedido → Produtos` com `ReferentialAction.Restrict`, e as demais com `Cascade`;
  - `Versao` como `rowversion`.

  Rodar
  `dotnet ef migrations has-pending-model-changes --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API`;
  ele precisa responder que não há mudanças. A T006 deve passar.
- [X] T016 [P] Acrescentar em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/BancoDadosTeste.cs`:
  - `public static ContextoBancoDados NovoContexto(string stringConexao)`;
  - `public async Task<string> NovoBancoMigradoAsync()`: cria o banco e aplica todas as
    migrations;
  - `public static async Task MigrarAteAsync(string stringConexao, string migration)`:
    usa `IMigrator` (`contexto.GetService<IMigrator>().MigrateAsync(migration)`);
  - `public static async Task<int> ExecutarAsync(string stringConexao, string sql)`.
- [X] T017 [P] Criar `tests/PedidoFulfillment.Testes.Integracao/Persistencia/DadosPersistencia.cs`, com dados válidos para os testes:
  - `Instante`: um `DateTimeOffset` UTC fixo;
  - `NovoUsuarioAsync(ContextoBancoDados)`: grava um `Usuario` com e-mail único e devolve o
    `Id`;
  - `NovoProduto(string? sku = null, decimal preco = 10m, int estoque = 10)`;
  - `NovoPedido(Guid clienteId, params Produto[] produtos)`: quantidade 1 por item e número
    único no padrão `PED-AAAAMMDD-NNNNNN`.
- [X] T018 Rodar `dotnet build` e `dotnet test`:
  - T003–T006 verdes;
  - `DependenciasCamadasTestes` verde;
  - toda a suíte do REQ-03 verde, sem alteração (FR-019);
  - zero avisos.

**Checkpoint**: esquema completo e migration criada. As histórias podem começar.

---

## Phase 3: User Story 1 — Guardar um pedido completo com seus itens (Priority: P1) 🎯 MVP

**Goal**: o REQ-05 consegue gravar e ler um pedido com todos os dados do `PedidoResponseDTO`.
O snapshot de preço vale, e o banco recusa pedidos inválidos.

**Independent Test**: gravar um pedido com dois itens, reler com outro contexto e conferir
todos os campos. Alterar o preço do produto e verificar que o item não muda.

### Testes da US1

- [X] T019 [P] [US1] Ida e volta em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/PersistenciaPedidoTestes.cs` (coleção `ColecaoBancoDados`, banco de `NovoBancoMigradoAsync`).
  - **Cenário 1**: gravar um pedido com dois produtos e reler num contexto novo, com
    `Include` de itens e histórico. Conferir:
    - `Id`, `Numero`, `ClienteId` e `Status` `Pendente`;
    - nos itens: `ProdutoId`, `NomeProduto`, `Quantidade`, `PrecoUnitario` e `Subtotal`;
    - `ValorTotal`, `Moeda` `BRL`, `CriadoEm` e `AtualizadoEm`, com o mesmo instante e
      deslocamento zero;
    - o histórico com a entrada de criação (SC-001).
  - **Cenário 2**: alterar nome e preço do produto, gravar, reler o pedido e verificar que
    o item mantém o nome e o preço originais.
  - **Cenário 3**: reler com um `ContextoBancoDados` novo, que simula o reinício.
  - **Cenário 8**: duas chamadas a `MudarStatus` (Processando e Cancelado com motivo), cada
    uma seguida de `SaveChangesAsync`. Reler e verificar o histórico com 3 entradas,
    ordenadas por `Id`, com o motivo na última.
  - **Status como texto**: `ConsultarAsync<string>("SELECT Status FROM Pedidos ...")`
    devolve `"Cancelado"`. Isso mostra que o status é gravado como texto (FR-006).
  - **Valores grandes**: um item com preço `999999999.99m` e quantidade 1 é relido sem
    perda de precisão.
- [X] T020 [P] [US1] Restrições do pedido em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/RestricoesPedidoTestes.cs`. Cada caso afirma `DbUpdateException` e, quando indicado, `ErrosBancoDados.EhViolacaoUnicidade` ou `EhViolacaoRestricao`. Esses métodos são `internal`; o acesso vem da T012.
  - **Cenário 4**:
    - pedido com `ClienteId` inexistente → violação de restrição;
    - item com `ProdutoId` inexistente, inserido por SQL direto → violação de restrição.
  - **Cenário 5**: dois pedidos com o mesmo `Numero` → violação de unicidade.
  - **Cenário 6**: o mesmo produto duas vezes no mesmo pedido, por SQL direto em
    `ItensPedido`, porque o domínio impede → violação de unicidade (chave primária).
  - **Por SQL direto**:
    - `Quantidade = 0` → `CK_ItensPedido_QuantidadePositiva`;
    - `Status = N'Desconhecido'` em `Pedidos` → `CK_Pedidos_StatusValido`;
    - `Status = NULL` em `Pedidos` → recusado (coluna obrigatória, Edge Cases);
    - `StatusNovo = N'Desconhecido'` em `HistoricosStatusPedido` →
      `CK_HistoricosStatusPedido_StatusValido`;
    - `ValorTotal = -1` → `CK_Pedidos_ValorTotalNaoNegativo`.
  - **Exclusões**:
    - apagar um usuário com pedidos → violação de restrição (FR-007);
    - apagar um pedido apaga itens, histórico, pagamento e ordem de entrega: as contagens
      ficam em zero (FR-011).
  - **Atomicidade**: um pedido com um item inválido, cujo produto foi removido do banco
    entre a leitura e a gravação, não grava nada. A tabela `Pedidos` continua vazia
    (FR-009).
- [X] T021 [P] [US1] Concorrência do pedido em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ConcorrenciaPedidoTestes.cs` (cenário 9, FR-021):
  - Dois contextos leem o mesmo pedido.
  - O primeiro faz `MudarStatus(Processando)` e grava.
  - O segundo faz `MudarStatus(Cancelado)` e recebe `ConflitoConcorrenciaException` com
    `Entidade == "Pedido"` e `Identificador == pedido.Id.ToString()`.
  - Relendo: status `Processando` e histórico com 2 entradas. A entrada do segundo não foi
    gravada.
  - Repetir 50 vezes, num laço dentro de um único teste, no mesmo banco, com um pedido novo
    a cada repetição: 100% com uma gravação aceita e uma recusada (SC-002).
  - **Identity sem tradução** (research R-04, FR-019):
    - dois `UserManager<Usuario>`, de escopos diferentes da `FabricaApi` com
      `StringConexao`, leem o mesmo usuário;
    - o primeiro chama `UpdateAsync` depois de mudar o `Nome`;
    - o segundo, com o `ConcurrencyStamp` antigo, chama `UpdateAsync`, que devolve
      `IdentityResult` com `Succeeded == false` e um erro de código `ConcurrencyFailure`,
      sem lançar exceção.

### Implementação da US1

- [X] T022 [US1] Rodar T019–T021 com o Docker no ar e corrigir o mapeamento (T013) ou o domínio (T009–T011) até ficarem verdes. Se a correção mudar o modelo, recriar a `EntidadesNegocio`, que ainda não foi publicada (`dotnet ef migrations remove` e T015 de novo), em vez de acrescentar outra migration. Registrar cada desvio em Notes.

**Checkpoint**: pedido completo gravado e lido, com restrições e concorrência comprovadas.

---

## Phase 4: User Story 2 — Proteger o estoque contra alterações simultâneas (Priority: P1)

**Goal**: uma alteração simultânea de produto é recusada, sem perda e sem estoque negativo.

**Independent Test**: dois contextos alteram o mesmo produto; um grava, e o outro recebe o
conflito. Estoque negativo é recusado.

### Testes da US2

- [X] T023 [P] [US2] Concorrência do produto em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/ConcorrenciaProdutoTestes.cs`.
  - **Cenários 1 e 2**:
    - Dois contextos leem o mesmo produto, com estoque 10.
    - O primeiro faz `AjustarEstoque(-3)` e grava.
    - O segundo faz `AjustarEstoque(-5)` e recebe `ConflitoConcorrenciaException` com
      `Entidade == "Produto"`.
    - O estoque relido é 7.
    - A `Versao` lida por SQL (`SELECT Versao FROM Produtos`) mudou depois da primeira
      gravação.
  - **Cenário 3**: depois do conflito, o segundo contexto chama `ChangeTracker.Clear()`,
    relê, ajusta e grava com sucesso. O estoque final é 2.
  - **Mudança de nome**: `AlterarDados` (só o nome) no primeiro contexto e
    `AjustarEstoque` no segundo também geram conflito (Edge Cases).
  - **Disputa repetida (SC-002)**: 50 repetições com duas tarefas paralelas (`Task.WhenAll`)
    disputando o último item de um produto com estoque 1. As leituras são sincronizadas:
    - cada tarefa usa o próprio contexto e lê o produto;
    - cada tarefa sinaliza a leitura e espera a outra (`Barrier` de 2 participantes ou dois
      `TaskCompletionSource`), e só então chama `AjustarEstoque(-1)` e grava.

    Sem a barreira, a segunda tarefa poderia ler o estoque 0 e falhar no domínio
    (`InvalidOperationException`), sem exercitar a concorrência (achado U2 do
    `/speckit-analyze`). Em cada repetição:
    - exatamente uma grava e a outra recebe `ConflitoConcorrenciaException`;
    - nenhuma recebe outra exceção;
    - o estoque final é 0, nunca negativo.
- [X] T024 [P] [US2] Restrições do produto em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/RestricoesProdutoTestes.cs`.
  - **Cenário 4**, por SQL direto `UPDATE`:
    - `QuantidadeEstoque = -1` → `CK_Produtos_EstoqueNaoNegativo`;
    - `Preco = -0.01` → `CK_Produtos_PrecoNaoNegativo`.
  - **Cenário 5**: apagar um produto citado num item de pedido → violação de restrição
    (FR-004). Apagar um produto sem pedidos funciona.
  - **SKU** (R-06): dois produtos com SKU `abc-1` e `ABC-1` → violação de unicidade.

### Implementação da US2

- [X] T025 [US2] Rodar T023–T024 com o Docker no ar e corrigir até ficarem verdes, com a mesma regra da T022 para mudanças de modelo.

**Checkpoint**: estoque protegido contra concorrência e valores inválidos.

---

## Phase 5: User Story 4 — Atualizar o banco existente e ter catálogo em Development (Priority: P1)

**Goal**:
- Um banco do REQ-03, com usuários, é atualizado sem perder contas.
- Um banco vazio recebe tudo.
- Em Development e testes existe o catálogo inicial.

**Independent Test**:
- Migrar até `InicialIdentidade`, registrar um usuário e aplicar o resto.
- Conferir as tabelas e o login.
- Subir a API com a preparação ligada e conferir o catálogo.

### Testes da US4

- [ ] T026 [P] [US4] Migrations em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/MigracaoBancoTestes.cs`:
  - **Cenário 1 (SC-003)**:
    - `MigrarAteAsync(banco, "InicialIdentidade")`;
    - registrar 3 usuários pela API (`FabricaApi` com `StringConexao`, preparação
      desligada), via `POST /api/v1/auth/register`;
    - `NovoContexto(banco).Database.MigrateAsync()`.

    Depois, conferir:
    - as 6 tabelas novas existem (`INFORMATION_SCHEMA.TABLES`);
    - `SELECT COUNT(*) FROM Usuarios` = 3;
    - os 3 fazem login com 200.
  - **Cenário 2**: com um banco vazio, a `FabricaApi` (`PrepararBanco = true`, ambiente
    `Testes`) cria o esquema completo. `__EFMigrationsHistory` tem as duas migrations.
  - **Cenário 3**:
    - com o banco migrado só até `InicialIdentidade`, a `FabricaApi` no ambiente
      `Production` com a preparação ligada não inicia, e a mensagem é
      `PreparacaoBancoDados.MensagemMigrationsPendentes`;
    - a tabela `Produtos` continua inexistente;
    - depois de `MigrateAsync`, a mesma API inicia.
  - **Cenário 4**: já coberto pela T006. Citar no comentário da classe.
- [ ] T027 [P] [US4] Catálogo em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/CatalogoInicialTestes.cs` (contracts/catalogo-inicial.md):
  - **Cenário 5**:
    - banco vazio e `FabricaApi` com `PrepararBanco = true`, ambiente `Testes`;
    - a tabela `Produtos` tem exatamente os 10 SKUs `CAT-001`…`CAT-010`, com os `Id`,
      preços, estoques e situações da tabela do contrato;
    - `CAT-009` tem estoque 0 e `CAT-010` está inativo.
  - **Cenário 6**:
    - alterar por SQL o estoque de `CAT-001` para 7 e apagar `CAT-002`;
    - subir outra `FabricaApi` no mesmo banco;
    - `CAT-001` continua com 7, `CAT-002` é recriado e nada é duplicado (10 linhas).
  - **Concorrência**: duas `FabricaApi` iniciando juntas num banco já migrado e sem
    produtos deixam exatamente 10 produtos.
  - **Cenário 7**:
    - ambiente `Production`, com banco já migrado e preparação ligada;
    - a API inicia e `Produtos` continua vazia.
  - **Preparação desligada**: com `PrepararBanco = false`, nenhum produto é criado.

### Implementação da US4

- [ ] T028 [US4] Criar `src/PedidoFulfillment.Infrastructure/Persistencia/CatalogoInicial.cs`:
  - classe `internal static`;
  - um método `Produtos(DateTimeOffset instante)` que devolve os 10 `Produto` com os `Id`
    fixos da tabela de `contracts/catalogo-inicial.md`;
  - o `Produto` precisa aceitar um `Id` informado: acrescentar um construtor ou parâmetro
    opcional `Guid? id` no `Produto` (T009), coberto por um caso a mais na T003;
  - comentário "SOMENTE Development e Testes (FR-020)".
- [ ] T029 [US4] Em `src/PedidoFulfillment.Infrastructure/Persistencia/PreparacaoBancoDados.cs`, acrescentar `CriarCatalogoInicialAsync(ContextoBancoDados, CancellationToken)`:
  - **Quando roda**: no `StartAsync`, depois de `CriarUsuariosIniciaisAsync`, só quando
    `ambiente.IsDevelopment() || ambiente.IsEnvironment("Testes")`. Extrair essa condição
    para um método privado `PodeAlterarDados()`, reutilizado em `AtualizarEsquemaAsync`.
  - **O que insere**: lê os SKUs existentes (`Produtos.Select(p => p.Sku)`) e insere só os
    ausentes, numa única `SaveChangesAsync`.
  - **Disputa entre instâncias**: em `DbUpdateException` com `EhViolacaoUnicidade`, quando
    outra instância inseriu ao mesmo tempo, chama `ChangeTracker.Clear()` e tenta uma vez
    mais só com os que ainda faltam.
  - **Log**: um evento `LoggerMessage` `CatalogoInicialCriado` (EventId 3006, Information)
    com a quantidade inserida.
  - **Falhas**: a falha de conexão já é tratada pelo `catch` existente.
  - **Comentário**: atualizar o comentário do topo da classe para incluir o catálogo.
- [ ] T030 [US4] Rodar T026–T027 com o Docker no ar e corrigir até ficarem verdes. Os testes de preparação do REQ-03 também precisam continuar verdes, porque agora toda preparação no ambiente `Testes` também cria o catálogo:
  - `PreparacaoInicializacaoTestes`;
  - `PreparacaoComBancoTestes`, em especial o caso das duas `FabricaApi` iniciando ao
    mesmo tempo, que agora também disputa os produtos;
  - `UsuariosIniciaisComBancoTestes`.

  Se algum desses testes afirmar contagens de tabela, ajustar só a afirmação, nunca o
  comportamento (achado C3 do `/speckit-analyze`).

**Checkpoint**: atualização de banco segura e catálogo disponível em Development e testes.

---

## Phase 6: User Story 3 — Registrar pagamento e ordem de entrega (Priority: P2)

**Goal**: pagamento e ordem de entrega pertencem ao pedido, no máximo um de cada.

**Independent Test**: gravar um pagamento e uma ordem de entrega para um pedido e lê-los a
partir dele. A segunda gravação de cada um é recusada.

### Testes da US3

- [ ] T031 [P] [US3] Em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/PagamentoEntregaTestes.cs`:
  - **Cenário 1**: gravar um `Pagamento` com resultado `Aprovado` e reler o pedido com
    `Include(p => p.Pagamento)`. Conferir valor, situação, transação e `ProcessadoEm`. A
    coluna `Situacao` guarda o texto `Aprovado`.
  - **Cenário 2**: um segundo `Pagamento` para o mesmo pedido → violação de unicidade
    (`IX_Pagamentos_PedidoId`).
  - **Cenário 3**: gravar uma `OrdemEntrega` e reler. Os três campos opcionais estão nulos.
  - **Cenário 4**: uma segunda `OrdemEntrega` → violação de unicidade.
  - **Cenário 5**: `RegistrarDespacho("Transportadora X", "BR123", t)`, gravar e reler.
  - **Rastreio** (FR-013):
    - o mesmo código `BR123` na mesma transportadora, em outro pedido → violação de
      unicidade;
    - o mesmo código em outra transportadora é aceito;
    - duas ordens sem código são aceitas (índice filtrado).
  - **Cenário 6**: `Pagamento` e `OrdemEntrega` com `PedidoId` inexistente → violação de
    restrição.
  - **Por SQL direto**:
    - `Situacao = N'Estornado'` → `CK_Pagamentos_SituacaoValida`;
    - `Valor = -1` → `CK_Pagamentos_ValorNaoNegativo`.

### Implementação da US3

- [ ] T032 [US3] Rodar a T031 com o Docker no ar e corrigir até ficar verde, com a mesma regra da T022 para mudanças de modelo.

**Checkpoint**: todas as histórias validadas contra o SQL Server.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: desempenho, documentação, comentários, segurança e validação final.

- [ ] T033 [P] Desempenho em `tests/PedidoFulfillment.Testes.Integracao/Persistencia/DesempenhoConsultaTestes.cs` (SC-005, R-10):
  - **Carga**:
    - um banco migrado com 100 usuários e 1 produto;
    - 10.000 pedidos inseridos por um único comando SQL em lote (`INSERT ... SELECT` sobre
      uma sequência gerada), distribuídos entre os usuários, com números únicos.
  - **Aquecimento**: 3 execuções de cada consulta.
  - **Medição**: com `Stopwatch`, a mediana de 20 execuções de cada consulta:
    - `Pedidos.Where(p => p.ClienteId == x).OrderByDescending(p => p.CriadoEm).ToListAsync()`;
    - `Pedidos.SingleAsync(p => p.Numero == n)`;
    - `Pedidos.Where(p => p.Status == StatusPedido.Processando).Take(50).ToListAsync()`,
      com cerca de 10% dos pedidos nesse status (FR-010).
  - **Resultado**: cada mediana < 100 ms.
  - **Índices**: consultar `sys.indexes` e conferir que os 4 índices de consulta da R-07
    existem com esses nomes (achado C2 do `/speckit-analyze`).
- [ ] T034 [P] Atualizar os comentários que citam o REQ-04 como futuro:
  - `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs`: "O REQ-04 (persistência) e o REQ-06 ... acrescentam" passa a dizer que a persistência de negócio já está no `ContextoBancoDados` e que o REQ-06 acrescenta a mensageria;
  - `src/PedidoFulfillment.Infrastructure/Saude/VerificacaoSqlServer.cs`: ajustar a menção ao REQ-04.
- [ ] T035 Atualizar `README.md`:
  - **`## Estado atual`**: linha do REQ-04 com link para `specs/004-dominio-persistencia/spec.md` e o texto "Produtos, pedidos, itens, histórico de status, pagamentos e ordens de entrega; concorrência otimista em produto e pedido; catálogo inicial em Development". A linha de pendentes passa a ser "REQ-05 a REQ-06".
  - **Seção do banco**: a migration `EntidadesNegocio`, o catálogo inicial (com link para
    `contracts/catalogo-inicial.md`) e o comando
    `dotnet ef migrations has-pending-model-changes`.
  - **Pré-requisitos**: acrescentar o roteiro de validação do REQ-04 à linha "Roteiros de
    validação".
  - **Árvore de pastas**: acrescentar `Produtos/`, `Pagamentos/` e `Entregas/` no Domain e
    `Persistencia/Configuracoes/` na Infrastructure.
- [ ] T036 Varredura de segredos com `git grep -n -i -E "password|senha|pwd|chave"` nos arquivos versionados. Confirmar que o REQ-04 não acrescentou nenhum valor novo além dos de desenvolvimento e de teste já conhecidos.
- [ ] T037 Rodar `dotnet format PedidoFulfillment.sln --verify-no-changes` e corrigir o que acusar. As migrations geradas já são ignoradas (REQ-03).
- [ ] T038 Executar o quickstart (quickstart.md §1 a §6) com o Docker no ar e registrar o resultado em Notes:
  - a contagem de testes, comparada com a T001;
  - nenhum teste ignorado;
  - o resultado de cada seção.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: sem dependências.
- **Foundational (Phase 2)**: depende da Setup e bloqueia todas as histórias. Precisa da
  migration (T015) antes de qualquer teste com banco.
- **US1 (Phase 3)**: depende da Foundational.
- **US2 (Phase 4)**: depende da Foundational. É independente da US1; só compartilha os
  utilitários da T016 e da T017.
- **US4 (Phase 5)**: depende da Foundational. A T028 altera `Produto.cs`, então ela vem
  depois da T025, se as duas fases correrem juntas.
- **US3 (Phase 6)**: depende da Foundational. É independente das demais.
- **Polish (Phase 7)**: depois das histórias.

A US4 (P1) vem antes da US3 (P2) por prioridade, embora a spec numere a US3 primeiro.

### Within Each Phase

- Testes antes da implementação, e falhando primeiro.
- Fundação: T007/T008 → T009 → T010/T011 → T012/T013 → T014 → T015. A T016 e a T017 podem
  correr em paralelo a partir da T014.
- Arquivos editados em sequência:
  - `Produto.cs`: T009, T028;
  - `ContextoBancoDados.cs`: T014;
  - `PreparacaoBancoDados.cs`: T029;
  - `BancoDadosTeste.cs`: T016.
- Qualquer correção que mude o modelo recria a `EntidadesNegocio`, sem migration nova
  (T022).

### Parallel Opportunities

- T003, T004, T005 e T006 (testes, arquivos diferentes).
- T007, T008, T009 e T012; T010 e T011 depois da T009.
- T016 e T017.
- Nos testes das histórias: T019, T020 e T021; T023 e T024; T026 e T027; T031. Também entre
  histórias, porque todos estão em arquivos diferentes.
- T033 e T034.

---

## Parallel Example: Foundational

```text
Task: "Testes de Produto em tests/PedidoFulfillment.Testes.Unitarios/Dominio/ProdutoTestes.cs"
Task: "Testes de Pedido em tests/PedidoFulfillment.Testes.Unitarios/Dominio/PedidoTestes.cs"
Task: "Testes de Pagamento e OrdemEntrega em tests/PedidoFulfillment.Testes.Unitarios/Dominio/PagamentoEntregaTestes.cs"
Task: "Modelo sincronizado em tests/PedidoFulfillment.Testes.Integracao/Persistencia/ModeloSincronizadoTestes.cs"
Task: "SituacaoPagamento em src/PedidoFulfillment.Domain/Pagamentos/SituacaoPagamento.cs"
Task: "ConflitoConcorrenciaException em src/PedidoFulfillment.Application/Comum/ConflitoConcorrenciaException.cs"
```

## Parallel Example: User Story 1

```text
Task: "Ida e volta em tests/PedidoFulfillment.Testes.Integracao/Persistencia/PersistenciaPedidoTestes.cs"
Task: "Restrições do pedido em tests/PedidoFulfillment.Testes.Integracao/Persistencia/RestricoesPedidoTestes.cs"
Task: "Concorrência do pedido em tests/PedidoFulfillment.Testes.Integracao/Persistencia/ConcorrenciaPedidoTestes.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 (Setup) e Phase 2 (Foundational).
2. Phase 3 (US1): pedido completo contra o banco real.
3. **PARAR e VALIDAR** com o quickstart §1 e §2.

### Incremental Delivery

1. Setup + Foundational → entidades, mapeamento e migration (commit `feat(dominio): cria entidades, mapeamento e migration EntidadesNegocio [REQ-04]`).
2. US1 → pedido validado contra o banco (commit `test(persistencia): valida pedido, restricoes e concorrencia do pedido [REQ-04]`).
3. US2 → estoque protegido (commit `test(persistencia): valida concorrencia e restricoes do produto [REQ-04]`).
4. US4 → atualização do banco e catálogo inicial (commit `feat(persistencia): cria catalogo inicial e valida migracao do banco [REQ-04]`).
5. US3 → pagamento e entrega (commit `test(persistencia): valida pagamento e ordem de entrega [REQ-04]`).
6. Polish → desempenho, README, comentários, segredos, formatação, quickstart (commit `chore(persistencia): ... [REQ-04]`).

---

## Notes

- [P] = arquivos diferentes, sem dependência pendente.
- O `ServicoNaoImplementado` continua respondendo pelas rotas de pedido. Ele sai no REQ-05.
- Repositório e unidade de trabalho não fazem parte deste REQ (plan.md, Structure
  Decision).
- `ConflitoConcorrenciaException.Identificador` é `string`, porque o EF também lança
  conflito ao alterar ou apagar uma linha já apagada, em entidades sem chave `Guid`.
  data-model.md e contracts/persistencia.md foram ajustados na geração das tarefas.
- `/speckit-analyze` (2026-10-22), achados aplicados:
  - **U1**: tradução do conflito só para entidades do Domain, mais o teste do Identity
    (T014, T021, R-04, contracts/persistencia.md).
  - **U2**: barreira na disputa do último item (T023).
  - **I1**, **I2** e **I3**: edge cases da spec alinhados ao domínio (pedido sem itens e
    arredondamento) e ao FR-011 (histórico apagado junto).
  - **C1**: collation explícita no `Sku` (T013, R-06, data-model).
  - **C2**: consulta por status e índices conferidos (T033).
  - **C3**: testes de preparação do REQ-03 citados na T030.
  - **C4**: status nulo (T020).
  - **A1**: estratégia única de repetição (T021).
  - **F1**: spec `Approved`.
  - **F2**: numeração FR-020 a FR-023 mantida fora de ordem, para não quebrar referências
    (como o L5 do REQ-03).
- Fazer commit ao fim de cada fase ou grupo lógico.
