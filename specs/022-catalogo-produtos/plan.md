# Implementation Plan: Cadastro e gestão do catálogo de produtos

**Branch**: `feature/req-22-catalogo-produtos` | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/022-catalogo-produtos/spec.md`

**Requisito**: REQ-22 (GitHub issue #53)

## Summary

- **Sete rotas em `/api/v1/produtos`**: listagem e consulta para os três perfis; cadastro,
  alteração, ajuste de estoque, desativação e ativação só para o Admin
  ([contrato](./contracts/api-produtos.md), research R-06 e R-09).
- **Visões por perfil**: o Cliente vê só os ativos, sem quantidade (inativo = 404); a operação
  (Admin e OperadorLogistica) vê tudo, com quantidade e datas.
- **Busca textual em português pelo Full-Text Search do SQL Server** (decisão do usuário depois
  de um teste prático): coluna `TextoBusca`, índice full-text `LANGUAGE 1046` sem acentos e sem
  stoplist, condição `FORMSOF(INFLECTIONAL) OR prefixo` por palavra, parametrizada (R-01, R-03,
  R-04).
- **Imagem própria do SQL Server** (`docker/sqlserver/Dockerfile`, CU26 + `mssql-server-fts` na
  mesma versão) no compose, na demonstração e nos três pontos dos testes (R-02).
- **Listagem por cursor como a de pedidos**, ordenada por `Nome, Sku`; a chave do cursor passa
  a `IChaveCursor` e serve aos dois (R-05).
- **Ajuste de estoque por variação, com concorrência otimista e até 3 reavaliações**, sem perder
  unidades com a reserva do Worker (R-07). SKU único com 409 também na disputa (R-08).
- Migration `CatalogoProdutos`; logs 12001–12006; quatro códigos de erro novos; nenhum pacote
  novo; sem eventos novos.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**: as que já existem — ASP.NET Core 10 (Minimal APIs, OpenAPI), EF Core
SqlServer 10.0.13 (`EF.Functions.Contains`), Identity/JWT, Testcontainers (inclui o
`ImageFromDockerfileBuilder`). **Nenhum pacote novo.**

**Storage**: SQL Server 2022 CU26 **com Full-Text Search** (imagem própria); migration
`CatalogoProdutos` ([data-model](./data-model.md)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory, Testcontainers MsSql com a
imagem própria; conformidade do contrato e oasdiff.

**Target Platform**: API e Worker .NET 10; SQL Server e RabbitMQ em contêiner

**Project Type**: web-service

**Performance Goals**: qualquer página em até 1 s com 10.000 produtos, com ou sem busca
(SC-005); busca refletindo gravações em até 10 s (medido: 2,6 a 4,5 s).

**Constraints**:
- contrato v1 só com acréscimos;
- endpoint só roteia; regras no `ServicoCatalogo`; consultas no `ArmazenamentoProdutos`;
- a migration exige o FTS e falha com mensagem clara sem ele;
- o CI passa a montar a imagem do SQL Server (cerca de 2 min a mais por execução).

**Scale/Scope**: 7 rotas, ~8 DTOs, 1 serviço, 1 armazenamento, 1 codificador de cursor,
1 Dockerfile, 1 migration, ~60 testes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Avaliação | Situação |
|-------------------|-----------|----------|
| I. Specification-First | Spec, clarify e plano antes do código; issue #53 | ✅ |
| II. Contratos imutáveis | Só acréscimos (rotas, schemas, códigos); `ComparadorContrato` e oasdiff | ✅ |
| III. Segurança por padrão | Todas as rotas autenticadas; gestão só para o Admin por política; visão do Cliente sem estoque; condição do FTS montada só com letras e dígitos e passada como parâmetro; erros sem detalhe interno | ✅ |
| IV. Desacoplamento assíncrono | Sem efeito colateral entre domínios (sem eventos novos, decisão da clarify); a reserva do Worker continua pelo fluxo atual | ✅ |
| V. Qualidade verificável | Unitários das regras, integração com sucesso, 401 e 403 em todas as rotas, concorrência e conformidade do contrato | ✅ |
| Endpoint só roteia | Rotas delegam a `IServicoCatalogo` | ✅ |
| 400 em ProblemDetails | `FiltroValidacao` com todos os erros | ✅ |
| Nova dependência NuGet | Nenhuma | ✅ |
| Restrição de plataforma (SQL Server via EF Core) | Mantida; o FTS é um recurso do próprio SQL Server, e a imagem continua sendo a oficial com um pacote oficial a mais | ✅ |

**Re-check pós-design**: sem violações.

## Project Structure

### Documentation (this feature)

```text
specs/022-catalogo-produtos/
├── plan.md
├── research.md          # R-01 a R-12, com o teste prático do FTS
├── data-model.md
├── quickstart.md
├── contracts/api-produtos.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
docker/sqlserver/Dockerfile                          # novo: CU26 + mssql-server-fts (R-02)
docker-compose.yml                                   # sqlserver: build da imagem própria

src/
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsProdutos.cs               # novo: 7 rotas
│   ├── Endpoints/ParametrosListarProdutos.cs        # novo
│   ├── Seguranca/PoliticasAutorizacao.cs            # + PoliticaCatalogo
│   ├── Seguranca/ChaveCursorDerivadaDoJwt.cs        # implementa IChaveCursor
│   ├── OpenApi/ParametrosListagemProdutosOpenApi.cs # novo
│   └── Program.cs                                   # registros
├── PedidoFulfillment.Application/
│   ├── Contratos/Produtos/*.cs                      # DTOs novos
│   ├── Contratos/Erros/CodigosErro.cs               # + 4 códigos
│   ├── Pedidos/CodificadorCursorPedidos.cs          # IChaveCursorPedidos → IChaveCursor
│   └── Produtos/                                    # novo
│       ├── IServicoCatalogo.cs / ServicoCatalogo.cs
│       ├── IArmazenamentoProdutos.cs
│       ├── FiltroListagemProdutos.cs
│       ├── PosicaoCursorProdutos.cs
│       ├── CodificadorCursorProdutos.cs
│       └── EventosCatalogo.cs                       # logs 12001–12006
└── PedidoFulfillment.Infrastructure/
    └── Persistencia/
        ├── ArmazenamentoProdutos.cs                 # novo
        ├── ContextoBancoDados.cs                    # TextoBusca ao gravar produtos
        ├── Configuracoes/ConfiguracaoProduto.cs     # coluna e índices
        └── Migracoes/<data>_CatalogoProdutos.cs     # nova

tests/
├── PedidoFulfillment.Testes.Unitarios/Produtos/     # validação, busca, cursor, serviço
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/ImagemSqlServerTeste.cs       # novo: monta a imagem uma vez
    ├── Infraestrutura/BancoDadosTeste.cs, MensageriaTeste.cs, Saude/SaudeComDependenciasTestes.cs
    ├── Endpoints/ProdutosTestes.cs                  # 401/403 por rota
    └── Produtos/                                    # escopo, busca, estoque concorrente, preço, desempenho

specs/001-contratos-api/contracts/openapi-v1.yaml    # + /api/v1/produtos e schemas
docs/fluxos/15-catalogo-de-produtos.md               # novo; índice docs/fluxos/README.md
docs/fluxos/02-criacao-de-pedido.md, 04-reserva-de-estoque.md, 03-…, docs/proximos-passos.md
```

**Structure Decision**: os projetos existentes. O catálogo ganha a pasta `Produtos` na
Application, como `Pedidos` e `Notificacoes`. O Dockerfile fica em `docker/sqlserver/`, fora de
`src/`, porque é infraestrutura de ambiente e serve ao compose e aos testes.

## Complexity Tracking

| Ponto | Por quê | Alternativa mais simples descartada porque |
|-------|---------|-------------------------------------------|
| Imagem própria do SQL Server (não é violação da constituição, mas é custo novo de manutenção e de CI) | O FTS não vem na imagem oficial Linux | A busca pela aplicação cobre pior os plurais irregulares; o usuário escolheu o FTS depois do teste prático |
