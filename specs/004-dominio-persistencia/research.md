# Research: Domínio de pedidos e persistência (REQ-04)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Entidades no Domain e mapeamento na Infrastructure

- **Decisão**:
  - As entidades ficam no `PedidoFulfillment.Domain`, como classes C# simples, sem
    nenhuma referência a EF Core:
    - `Produtos/Produto`;
    - `Pedidos/Pedido`, `Pedidos/ItemPedido` e `Pedidos/HistoricoStatusPedido`;
    - `Pagamentos/Pagamento` e `Pagamentos/SituacaoPagamento`;
    - `Entregas/OrdemEntrega`.
  - O mapeamento fica na Infrastructure, em `Persistencia/Configuracoes/`, com uma classe
    `IEntityTypeConfiguration<T>` por entidade, aplicadas por
    `ApplyConfigurationsFromAssembly`.
  - O `ContextoBancoDados` ganha os `DbSet` de negócio.
- **Justificativa**:
  - O teste de arquitetura do REQ-02 exige que o Domain não referencie nada (`Domain = []`).
  - A issue pede Fluent API, que deixa o domínio livre de atributos.
  - Uma classe por entidade mantém o `OnModelCreating` curto.
- **Alternativas**:
  - Entidades na Infrastructure, como o `Usuario` do Identity. Rejeitada: o REQ-05 e os
    workers precisam das regras no domínio (constituição).
  - Data annotations nas entidades. Rejeitada: puxaria `System.ComponentModel.Annotations`
    para o domínio e espalharia o mapeamento.

## R-02. Quanto comportamento as entidades têm

- **Decisão**: as entidades protegem só a própria consistência, e não as regras de
  negócio dos requisitos seguintes:
  - **Proteção do estado**: setters privados e construtores que exigem os dados
    obrigatórios. As coleções são expostas como `IReadOnlyCollection`, com campo privado
    por trás. Cada entidade tem um construtor privado sem parâmetros, usado pelo EF.
  - **`ItemPedido`**: é criado a partir do `Produto` e copia o nome e o preço dele (FR-008).
    O subtotal é `preço × quantidade`.
  - **`Pedido`**:
    - recebe o dono, o número, os itens e o instante;
    - calcula o `ValorTotal` como a soma dos subtotais;
    - nasce com status `Pendente` e com a entrada de criação no histórico (status anterior
      vazio).
  - **`Pedido.MudarStatus(novo, autorId?, motivo?, instante)`**: muda o status, atualiza
    `AtualizadoEm` e acrescenta a entrada no histórico. Não valida a transição: a tabela
    de transições é do REQ-05.
  - **`Produto`**: `AjustarEstoque(variacao, instante)` recusa resultado negativo.
    `AlterarDados(nome, descricao, preco, instante)`, `Ativar` e `Desativar` completam as
    operações.
  - **`Pagamento`**: `RegistrarResultado(situacao, transacaoExterna, instante)`.
  - **`OrdemEntrega`**: `RegistrarDespacho(transportadora, codigoRastreio, instante)`.
  - **Erros de argumento**: guardas de argumento (`ArgumentException`,
    `ArgumentOutOfRangeException`) nos construtores e métodos. Não são erros de negócio
    do contrato. Esses erros (422, 409) são do REQ-05.
  - **Datas**: o instante sempre vem de fora, porque os serviços usam `TimeProvider`. O
    domínio não lê o relógio.
- **Justificativa**:
  - Os testes deste requisito precisam criar e alterar dados de forma válida.
  - Os métodos acima são o mínimo para isso e para os cenários da spec: snapshot do preço,
    histórico na mesma gravação (FR-023) e estoque nunca negativo.
  - Regras de transição e de disponibilidade ficam para quem as especifica.
- **Alternativas**:
  - Entidades anêmicas com setters públicos. Rejeitada: o histórico e o snapshot ficariam
    a cargo de cada chamador.
  - Já colocar a tabela de transições no `Pedido`. Rejeitada: é escopo do REQ-05 (FR-020
    do REQ-01).

## R-03. Relação com o `Usuario` do Identity

- **Decisão**: o domínio guarda só os identificadores:
  - `Pedido.ClienteId`;
  - `HistoricoStatusPedido.AutorId`, opcional.

  As chaves estrangeiras para `Usuarios` ficam no mapeamento, com
  `HasOne<Usuario>().WithMany().HasForeignKey(...)` e `OnDelete(DeleteBehavior.Restrict)`.
  Um usuário com pedidos, ou citado no histórico, não pode ser apagado (FR-007).
- **Justificativa**: o `Usuario` herda de `IdentityUser` e vive na Infrastructure. O Domain
  não pode referenciá-lo (R-01).
- **Alternativas**:
  - `SetNull` no autor do histórico. Rejeitada: apagaria a autoria de um registro que é só
    de inclusão. Além disso, nenhuma operação do sistema apaga usuários.

## R-04. Concorrência otimista com `rowversion`

- **Decisão**:
  - **Coluna de versão**: `Produto` e `Pedido` têm uma propriedade de sombra (shadow
    property) `Versao`, do tipo `rowversion`, configurada com `IsRowVersion()`. Ela só
    existe no mapeamento, não na classe. O SQL Server muda a versão a cada `UPDATE`, e o EF
    inclui a versão lida no `WHERE`. Se nenhuma linha for afetada, o EF lança
    `DbUpdateConcurrencyException`.
  - **Tradução do erro**: o `ContextoBancoDados` sobrescreve `SaveChangesAsync` e
    `SaveChanges`. Ele traduz `DbUpdateConcurrencyException` para
    `ConflitoConcorrenciaException`, definida em `Application/Comum`, com o nome da entidade
    e o identificador. O Application e os workers tratam o conflito sem referenciar o EF
    Core (FR-003, FR-021).
  - **Escopo**: os itens, o histórico, o pagamento e a ordem de entrega não têm versão
    (Clarifications).
- **Justificativa**:
  - `rowversion` é a forma nativa do SQL Server. Toda alteração na linha muda a versão,
    inclusive a de nome (Edge Cases).
  - Uma propriedade de sombra não expõe um `byte[]` de persistência no domínio.
  - Mudar o status grava uma linha de histórico e altera a linha do pedido, então o
    conflito aparece também quando só o status muda.
- **Alternativas**:
  - `[ConcurrencyCheck]` na quantidade em estoque. Rejeitada: não detecta mudança de nome
    nem de preço.
  - `Versao` como propriedade do domínio. Adiada: se o REQ-05 precisar dela (por exemplo,
    em ETag), basta expor a sombra, sem migration.

## R-05. Restrições de integridade no banco

- **Decisão**: cada regra estrutural da spec vira uma restrição do banco, com nome em
  português:
  - **Check constraints** (`ToTable(t => t.HasCheckConstraint(...))`):
    - `CK_Produtos_PrecoNaoNegativo`;
    - `CK_Produtos_EstoqueNaoNegativo`;
    - `CK_ItensPedido_QuantidadePositiva`;
    - `CK_ItensPedido_PrecoNaoNegativo`;
    - `CK_ItensPedido_SubtotalNaoNegativo`;
    - `CK_Pedidos_ValorTotalNaoNegativo`;
    - `CK_Pedidos_StatusValido`;
    - `CK_HistoricosStatusPedido_StatusValido`;
    - `CK_Pagamentos_ValorNaoNegativo`;
    - `CK_Pagamentos_SituacaoValida`.
  - **Valores de enum**: as listas das constraints de status e de situação são geradas de
    `Enum.GetNames`. Se um valor for incluído e a migration esquecida, o teste de modelo
    sincronizado (R-09) falha.
  - **Índices únicos** (`IX_<Tabela>_<Colunas>`):
    - `Produtos(Sku)`;
    - `Pedidos(Numero)`;
    - `Pagamentos(PedidoId)`;
    - `OrdensEntrega(PedidoId)`;
    - `OrdensEntrega(Transportadora, CodigoRastreio)`, filtrado por
      `CodigoRastreio IS NOT NULL`.
  - **Chave primária composta** `(PedidoId, ProdutoId)` em `ItensPedido`. Ela impede o
    mesmo produto duas vezes no pedido (FR-008), sem índice extra.
  - **Exclusão** (FR-004, FR-007, FR-011):
    - `Pedido` → itens, histórico, pagamento e ordem de entrega: `Cascade`.
    - `Produto` → itens: `Restrict`.
    - `Usuario` → pedidos e histórico: `Restrict`.
  - **Erros**: o `ErrosBancoDados` ganha `EhViolacaoRestricao` (547: check ou FK) ao lado
    de `EhViolacaoUnicidade`. Os testes usam os dois para afirmar o motivo da recusa.
- **Justificativa**:
  - FR-002 exige a recusa "mesmo que a regra também exista no serviço".
  - Só o banco garante isso contra gravações concorrentes ou feitas fora da aplicação.
  - A ordem das cascatas não cria múltiplos caminhos de cascata (`Usuarios` não tem
    cascata). Esses caminhos o SQL Server recusa.
- **Alternativas**:
  - Validar só no domínio. Rejeitada: o FR-002 pede as duas camadas.
  - `ItemPedido` com Id próprio e índice único. Rejeitada: mais uma coluna sem uso.

## R-06. Tipos e tamanhos das colunas

- **Decisão**:
  - **Identificadores**: `Guid` gerado pelo domínio com `Guid.CreateVersion7()`, como o
    `Usuario` do REQ-03. A exceção é `HistoricoStatusPedido`, que usa `long` com identity:
    a ordem de inclusão é a ordem do histórico (FR-022). O SQL Server não ordena
    `uniqueidentifier` cronologicamente.
  - **Dinheiro**: `decimal(18,2)`, com `HasPrecision(18, 2)`. Cobre o limite da spec
    (999.999.999,99) com folga. O domínio arredonda com `MidpointRounding.ToEven` antes de
    atribuir.
  - **Datas**: `DateTimeOffset`, mapeado para `datetimeoffset`. O domínio só aceita
    instantes com deslocamento zero (UTC), como no contrato v1.
  - **Textos** (`nvarchar`):

    | Campo | Tamanho |
    |-------|---------|
    | `Sku` | 50 |
    | `Produto.Nome`, `ItemPedido.NomeProduto` | 200 |
    | `Descricao` | 1000 |
    | `Numero` | 19 (`PED-AAAAMMDD-NNNNNN`) |
    | `Moeda` | 3, padrão `BRL` |
    | `Transportadora` | 100 |
    | `CodigoRastreio` | 50 |
    | `IdentificadorTransacao` | 100 |
    | `Motivo` | 500 (FR-022) |

  - **Enums**: `StatusPedido` e `SituacaoPagamento` são gravados como texto
    (`HasConversion<string>()`, `nvarchar(30)`), conforme o FR-006.
- **Justificativa**:
  - Os tamanhos seguem o contrato v1, quando ele define. Nos demais casos, seguem limites
    usuais.
  - SKU sem diferenciar maiúsculas de minúsculas (FR-001): a collation padrão do contêiner
    (`SQL_Latin1_General_CP1_CI_AS`) já faz o índice único ignorar a diferença. Um teste
    comprova isso.
- **Alternativas**:
  - `DateTime` com conversor para UTC. Rejeitada: `datetimeoffset` é explícito e não
    depende de `Kind`.
  - Normalizar o SKU em maiúsculas numa coluna extra. Rejeitada: duplicaria o dado, e a
    collation já resolve.

## R-07. Índices de consulta

- **Decisão**: os índices não únicos (FR-010, FR-022, SC-005) são:
  - `IX_Pedidos_ClienteId_CriadoEm`: pedidos de um cliente, mais recentes primeiro.
  - `IX_Pedidos_Status`: listagem por status, para os operadores.
  - `IX_HistoricosStatusPedido_PedidoId_Id`: histórico de um pedido, em ordem.
  - `IX_ItensPedido_ProdutoId`: FK; é o que torna barata a recusa de apagar um produto.
- **Justificativa**:
  - Cada consulta citada na spec tem um índice que começa pela coluna filtrada.
  - A busca por número usa o índice único de R-05.
- **Alternativas**: chave clusterizada em `CriadoEm`. Rejeitada: complica o mapeamento sem
  ganho medido.

## R-08. Catálogo inicial

- **Decisão**:
  - **Onde está**: `Persistencia/CatalogoInicial.cs` é uma lista fixa de 10 produtos com
    identificadores fixos, publicados em
    [contracts/catalogo-inicial.md](./contracts/catalogo-inicial.md). Ela inclui um produto
    inativo e um com estoque zero (FR-020).
  - **Quando roda**: o `PreparacaoBancoDados` ganha o passo `CriarCatalogoInicialAsync`.
    Ele roda depois de perfis e usuários, só em Development e no ambiente `Testes`, como as
    migrations. Também respeita `Banco:PrepararNaInicializacao`.
  - **Idempotência**: o passo insere só os SKUs que não existem. Um produto existente não é
    alterado. Uma violação de unicidade, quando outra instância inseriu ao mesmo tempo, é
    ignorada, como nos perfis do REQ-03.
- **Justificativa**:
  - Identificadores fixos permitem que o quickstart e os testes do REQ-05 citem produtos
    concretos.
  - Reaproveitar a preparação dá as mesmas regras de falha e de desligamento, sem um
    mecanismo novo.
- **Alternativas**:
  - `HasData` na migration. Rejeitada no clarify: valeria em produção.
  - Catálogo em configuração. Rejeitada no clarify.

## R-09. Migration e verificação de modelo sincronizado

- **Decisão**:
  - **Geração**: a migration `EntidadesNegocio` é gerada com
    `dotnet ef migrations add EntidadesNegocio --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API --output-dir Persistencia/Migracoes`.
    Ela só cria tabelas, índices e constraints. Não altera as tabelas do Identity.
  - **Revisão**: a migration gerada é revisada. Ela não pode conter `AlterColumn` nem
    `DropTable` nas tabelas `Usuarios` e `Perfis` (FR-014).
  - **Modelo sincronizado (FR-016)**: um teste sem Docker chama
    `contexto.Database.HasPendingModelChanges()`, do EF Core 9 ou superior. O contexto usa
    uma string de conexão qualquer, sem abrir conexão. Além disso, o EF Core 10 já lança
    `PendingModelChangesWarning` como erro no `Migrate`, o que protege a inicialização em
    Development.
  - **Atualização de banco existente (FR-014, SC-003)**: o teste aplica só a
    `InicialIdentidade` pelo `IMigrator`, registra usuários pela API e depois aplica as
    migrations restantes. Em seguida, confere que os usuários continuam fazendo login.
- **Justificativa**: o caminho de atualização é o que existe hoje em qualquer ambiente com o
  REQ-03.
- **Alternativas**: comparar o snapshot por script. Rejeitada: a API do EF já faz isso.

## R-10. Testes

- **Decisão**:
  - **Unitários** (`Testes.Unitarios/Dominio/`):
    - construtores e métodos das entidades;
    - snapshot do produto no item;
    - total do pedido;
    - entrada de criação e entradas de mudança no histórico;
    - estoque nunca negativo;
    - instantes não UTC recusados.
  - **Integração sem Docker**: `ModeloSincronizadoTestes` (FR-016).
  - **Integração com Testcontainers** (fixture `BancoDadosTeste`, um banco novo por teste):
    - `PersistenciaPedidoTestes`: US1, ida e volta de todos os campos, snapshot e
      reinício com um contexto novo.
    - `RestricoesIntegridadeTestes`: SC-004. Uma inserção inválida por restrição, feita por
      SQL direto quando o domínio impediria o dado.
    - `ConcorrenciaTestes`: SC-002. Dois contextos sobre o mesmo produto ou pedido, 50
      repetições.
    - `PagamentoEntregaTestes`: US3.
    - `MigracaoBancoTestes`: US4 1–4.
    - `CatalogoInicialTestes`: US4 5–7, pela `FabricaApi` com a preparação ligada.
    - `DesempenhoConsultaTestes`: SC-005. Insere 10.000 pedidos por SQL em lote e mede a
      mediana de 20 buscas por cliente e por número, depois de aquecer. O limite é 100 ms.
  - **Testes que já existem**: a suíte do REQ-03 continua como está (FR-019). A
    `FabricaApi` não muda o padrão de preparação desligada.
- **Justificativa**:
  - Princípio V: as regras do domínio têm testes unitários.
  - As restrições só são verificáveis contra o SQL Server real.
  - A mediana com aquecimento reduz a flutuação do teste de desempenho.
- **Alternativas**:
  - SQLite em memória. Rejeitada: não tem `rowversion`, os check constraints divergem e a
    constituição pede SQL Server.

## R-11. Pacotes

- **Decisão**: nenhum pacote NuGet novo. EF Core SqlServer, Design, `dotnet-ef` e
  Testcontainers.MsSql já estão no projeto desde o REQ-03.
- **Justificativa**: constituição. Cada dependência nova exige justificativa, e nenhuma é
  necessária.
