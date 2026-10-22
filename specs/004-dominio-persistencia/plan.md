# Implementation Plan: Domínio de pedidos e persistência

**Branch**: `feature/req-04-dominio-persistencia` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-dominio-persistencia/spec.md`

**Requisito**: REQ-04 (GitHub issue #5)

## Summary

Este REQ cria o armazenamento de negócio sobre o banco que o REQ-03 iniciou:

- **Domínio**: seis entidades no `PedidoFulfillment.Domain`, sem dependências:
  - `Produto`;
  - `Pedido`, `ItemPedido` e `HistoricoStatusPedido`;
  - `Pagamento`;
  - `OrdemEntrega`.

  Elas protegem só a própria consistência: o snapshot do produto no item, o total do
  pedido, o histórico gravado junto com a mudança de status e o estoque nunca negativo. As
  regras de transição ficam para o REQ-05 (R-02).
- **Mapeamento**: Fluent API na Infrastructure, com uma configuração por entidade e nomes
  em português. As restrições do banco (check, únicos, chaves estrangeiras com
  `Restrict`/`Cascade`) cobrem cada regra estrutural da spec (R-05). Os índices cobrem as
  consultas do REQ-05 (R-07).
- **Concorrência**: `rowversion` de sombra em `Produto` e `Pedido`. O contexto traduz o
  conflito do EF Core para `ConflitoConcorrenciaException`, do Application (R-04).
- **Migration**: a `EntidadesNegocio` vem depois da `InicialIdentidade` e não toca nas
  tabelas do Identity. Um teste garante que o modelo não tem alteração sem migration
  (R-09).
- **Catálogo inicial**: 10 produtos fixos, criados pela preparação da inicialização do
  REQ-03, só em Development e testes (R-08).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- `Microsoft.EntityFrameworkCore.SqlServer` 10.0.13 e
  `Microsoft.AspNetCore.Identity.EntityFrameworkCore` 10.0.13 (já no projeto);
- ferramenta local `dotnet-ef` 10.0.13.

Nenhum pacote novo (R-11).

**Storage**: SQL Server 2022, no contêiner do REQ-02. É o banco `PedidoFulfillment`, com a
nova migration `EntidadesNegocio`.

**Testing**:
- xUnit v3, FluentAssertions 7.x e WebApplicationFactory;
- Testcontainers.MsSql 4.15.0 para os cenários com banco real, ignorados sem Docker.

**Target Platform**: API ASP.NET Core rodando pelo SDK; banco em contêiner

**Project Type**: web-service

**Performance Goals**: busca por cliente e por número em menos de 100 ms (mediana), com
10.000 pedidos no banco (SC-005)

**Constraints**:
- O contrato v1 não muda, e as rotas de pedido continuam respondendo 501 (FR-018).
- O comportamento do REQ-03 continua igual (FR-019).
- O Domain continua sem referências (teste de arquitetura do REQ-02).
- A suíte continua rodando sem Docker.

**Scale/Scope**:
- 6 entidades e 1 enum novos;
- 6 tabelas, 10 check constraints, 5 índices únicos e 4 índices de consulta;
- 1 migration;
- 1 passo novo na preparação da inicialização.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec aprovada após o specify e o clarify (3 perguntas); commit `[REQ-04]` |
| II. Contratos versionados | ✅ | OpenAPI v1 intocado (FR-018). Os contratos internos ficam documentados em `contracts/persistencia.md` e `contracts/catalogo-inicial.md` |
| III. Segurança por padrão | ✅ | Nenhuma rota nova. O catálogo só é criado em Development e testes. Nenhum segredo novo. A mensagem de falha da preparação continua sem dados de conexão (REQ-03) |
| IV. Desacoplamento assíncrono | ➖ N/A | Nenhum efeito colateral entre domínios. O outbox é do REQ-06, e a issue gh #7 usa RabbitMQ.Client nativo, não MassTransit |
| V. Qualidade verificável | ✅ | Testes unitários das entidades. Testes com banco real para cada restrição (SC-004), concorrência (SC-002), migration (SC-003) e desempenho (SC-005) |
| Stack (SQL Server via EF Core) | ✅ | exatamente o exigido |
| Endpoint só roteia | ✅ | Nenhum endpoint muda |
| Regras no domínio ou em serviços | ✅ | Os invariantes ficam nas entidades do Domain. O banco só repete as regras estruturais (FR-002) |
| Dependências NuGet justificadas | ✅ | Nenhuma nova |
| Nomes em português | ✅ | Entidades, tabelas, colunas e constraints (`Produtos`, `ItensPedido`, `CK_Produtos_EstoqueNaoNegativo`, `ConflitoConcorrenciaException`) |
| Branch dedicada | ✅ | `feature/req-04-dominio-persistencia`, a partir de `main` com o REQ-03 mergeado |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/004-dominio-persistencia/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── persistencia.md
│   └── catalogo-inicial.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   ├── Produtos/Produto.cs                   # NOVO
│   ├── Pedidos/
│   │   ├── StatusPedido.cs                   # já existe (REQ-01)
│   │   ├── Pedido.cs                         # NOVO: criação, total, MudarStatus + histórico
│   │   ├── ItemPedido.cs                     # NOVO: snapshot de nome e preço
│   │   └── HistoricoStatusPedido.cs          # NOVO
│   ├── Pagamentos/
│   │   ├── Pagamento.cs                      # NOVO
│   │   └── SituacaoPagamento.cs              # NOVO
│   └── Entregas/OrdemEntrega.cs              # NOVO
├── PedidoFulfillment.Application/
│   └── Comum/ConflitoConcorrenciaException.cs  # NOVO (R-04)
└── PedidoFulfillment.Infrastructure/
    └── Persistencia/
        ├── ContextoBancoDados.cs             # + DbSet, ApplyConfigurationsFromAssembly, tradução do conflito
        ├── ErrosBancoDados.cs                # + EhViolacaoRestricao (547)
        ├── PreparacaoBancoDados.cs           # + CriarCatalogoInicialAsync (Development e Testes)
        ├── CatalogoInicial.cs                # NOVO: 10 produtos fixos
        ├── Configuracoes/                    # NOVO: uma IEntityTypeConfiguration por entidade
        │   ├── ConfiguracaoProduto.cs
        │   ├── ConfiguracaoPedido.cs
        │   ├── ConfiguracaoItemPedido.cs
        │   ├── ConfiguracaoHistoricoStatusPedido.cs
        │   ├── ConfiguracaoPagamento.cs
        │   └── ConfiguracaoOrdemEntrega.cs
        └── Migracoes/                        # + EntidadesNegocio

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   └── Dominio/                              # NOVO: ProdutoTestes, PedidoTestes, PagamentoTestes, OrdemEntregaTestes
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/BancoDadosTeste.cs     # + helpers: contexto para um banco, migrar até uma migration
    └── Persistencia/                         # NOVO
        ├── ModeloSincronizadoTestes.cs       # sem Docker (FR-016)
        ├── PersistenciaPedidoTestes.cs       # US1
        ├── RestricoesIntegridadeTestes.cs    # SC-004
        ├── ConcorrenciaTestes.cs             # US2, SC-002
        ├── PagamentoEntregaTestes.cs         # US3
        ├── MigracaoBancoTestes.cs            # US4 1–4, SC-003
        ├── CatalogoInicialTestes.cs          # US4 5–7
        └── DesempenhoConsultaTestes.cs       # SC-005
```

**Structure Decision**: as quatro camadas do REQ-02 continuam. O domínio não depende de
nada, e o mapeamento e o catálogo ficam na Infrastructure. O Application só ganha a
exceção de conflito, para que os serviços do REQ-05 e os workers tratem a concorrência
sem referenciar o EF Core. O repositório ou a unidade de trabalho que esses serviços vão
usar é decisão do REQ-05.

## Complexity Tracking

Sem violações a justificar.

| Risco | Mitigação |
|-------|-----------|
| A migration alterar tabelas do Identity sem querer | Revisão da migration gerada e teste de atualização a partir da `InicialIdentidade`, com usuários (R-09) |
| Constraints de enum ficarem desatualizadas quando um status for incluído | A lista é gerada de `Enum.GetNames`, e o teste de modelo sincronizado falha sem migration nova (R-05, R-09) |
| Cascata múltipla recusada pelo SQL Server | Só `Pedidos` propaga exclusão; `Usuarios` e `Produtos` usam `Restrict` (R-05) |
| Teste de desempenho instável | Mediana de 20 execuções após aquecimento e carga por SQL em lote (R-10) |
| Unicidade do SKU depender da collation | Teste explícito com SKU de outra caixa no contêiner do projeto (R-06) |
| `Guid` v7 fragmentar o índice clusterizado | Aceito no volume esperado, como no `Usuarios` do REQ-03. O histórico usa identity porque depende da ordem (R-06) |
| Catálogo criado em produção por engano | O passo só roda em `Development` e `Testes`, e um teste comprova que outro ambiente não cria produtos (R-08) |
