# Implementation Plan: Listagem e paginação de pedidos por perfil

**Branch**: `feature/req-21-listagem-pedidos` | **Date**: 2026-11-03 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/021-listagem-pedidos/spec.md`

**Requisito**: REQ-21 (GitHub issue #52)

## Summary

- **Rota nova `GET /api/v1/pedidos`**, acréscimo ao contrato v1, com a política de consulta já
  existente (Cliente, OperadorLogistica e Admin) ([contrato](./contracts/api-listagem-pedidos.md)).
- **Escopo no serviço**: o Cliente sempre filtrado pelo `ClienteId` do token; o filtro
  `clienteId` dá 403 para ele; a operação vê todos (R-06).
- **Paginação por chave (keyset)** sobre `(CriadoEm DESC, Numero DESC)`, nos dois sentidos,
  com `tamanho + 1` para saber se há mais página e sem total (R-01, R-02).
- **Cursor opaco com HMAC** ligado ao usuário e aos filtros, com a chave derivada por HKDF da
  `Jwt:ChaveAssinatura`; adulterado ou de outra consulta → 400 (R-03, R-04). A chave chega por
  `IChaveCursorPedidos`, para poder passar a uma chave própria no futuro trocando só o registro.
- **Filtros**: status (vários), período de criação e período de última alteração, e Cliente
  (operação); validados todos de uma vez pelo `FiltroValidacao` existente (R-05).
- **Resumo** projetado direto do banco, sem carregar itens, histórico nem entrega (R-07, R-08);
  migration `ListagemPedidos` só com índices.
- Nenhum pacote novo; log `4006 PedidosListados` (Debug); fluxo 3 e índice dos fluxos
  atualizados.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: ASP.NET Core 10 (Minimal APIs, `[AsParameters]`, OpenAPI), EF Core
  SqlServer 10.0.13, Identity/JWT Bearer;
- da biblioteca padrão: `System.Security.Cryptography` (`HMACSHA256`, `HKDF`,
  `CryptographicOperations.FixedTimeEquals`), `System.Buffers.Text.Base64Url`, `System.Text.Json`;
- **nenhum pacote novo**.

**Storage**: SQL Server; migration `ListagemPedidos`, só com índices em `Pedidos`
([data-model](./data-model.md#tabela-pedidos-existente--índices)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory, Testcontainers MsSql; teste de
conformidade do contrato (`ComparadorContrato`) e oasdiff no CI.

**Target Platform**: API em .NET 10; SQL Server 2022 em contêiner

**Project Type**: web-service

**Performance Goals**: qualquer página em até 1 s com 10.000 pedidos, sem piorar com a
profundidade (SC-004): uma consulta `TOP (n+1)` por índice por requisição.

**Constraints**:
- contrato v1 só com acréscimos (Princípio II);
- endpoint só roteia; regra no `ServicoPedidos`, consulta no `ArmazenamentoPedidos`
  (restrições arquiteturais da constituição);
- só leitura: nada gravado nem publicado (FR-023);
- mensagens de erro em português, sem revelar o conteúdo do cursor (FR-017, FR-021).

**Scale/Scope**: 1 rota, 3 DTOs, 1 serviço de cursor, 1 método de armazenamento, 1 migration
de índices, ~30 testes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Avaliação | Situação |
|-------------------|-----------|----------|
| I. Specification-First | Spec, clarify e este plano antes do código; issue #52 rastreada | ✅ |
| II. Contratos imutáveis | Só acréscimos no `openapi-v1.yaml` (um `get` e três schemas); oasdiff e `ComparadorContrato` conferem | ✅ |
| III. Segurança por padrão | Rota autenticada com política; escopo do Cliente aplicado no servidor e independente do cursor; 403 para o filtro por Cliente; erros sem detalhe interno; cursor com HMAC e comparação em tempo constante | ✅ |
| IV. Desacoplamento assíncrono | Não se aplica: só leitura, sem efeito colateral | ✅ (N/A) |
| V. Qualidade verificável | Unitários para validação, cursor e escopo; integração com 200, 401 e 403; conformidade do contrato | ✅ |
| Minimal API só roteia | A rota delega ao `IServicoPedidos.ListarAsync`; a validação de formato fica no filtro, como nas outras rotas | ✅ |
| 400 em ProblemDetails | `FiltroValidacao` + `RespostasErro.Validacao`, com todos os erros | ✅ |
| Nova dependência NuGet | Nenhuma | ✅ |
| Nomes em português | `ListarPedidosRequestDTO`, `ResumoPedidoResponseDTO`, `CodificadorCursorPedidos`… | ✅ |

**Re-check pós-design**: sem violações; a seção Complexity Tracking fica vazia.

## Project Structure

### Documentation (this feature)

```text
specs/021-listagem-pedidos/
├── plan.md              # Este arquivo
├── research.md          # Fase 0: R-01 a R-12
├── data-model.md        # Fase 1: índices, tipos e DTOs
├── quickstart.md        # Fase 1: roteiro de validação
├── contracts/
│   └── api-listagem-pedidos.md   # Trecho do OpenAPI, exemplos e respostas
├── checklists/
│   └── requirements.md
└── tasks.md             # Fase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.API/
│   ├── Endpoints/EndpointsPedidos.cs              # + MapGet("") ListarPedidos
│   ├── Seguranca/ChaveCursorDerivadaDoJwt.cs      # novo: IChaveCursorPedidos via HKDF (R-04)
│   └── OpenApi/                                   # ajuste de tipos dos parâmetros, se preciso (R-05)
├── PedidoFulfillment.Application/
│   ├── Contratos/Pedidos/
│   │   ├── ListarPedidosRequestDTO.cs             # novo: parâmetros + validação (R-05)
│   │   ├── ResumoPedidoResponseDTO.cs             # novo
│   │   └── PaginaPedidosResponseDTO.cs            # novo
│   └── Pedidos/
│       ├── FiltroListagemPedidos.cs               # novo
│       ├── PosicaoCursor.cs                       # novo
│       ├── CodificadorCursorPedidos.cs            # novo, com IChaveCursorPedidos (R-03, R-04)
│       ├── IArmazenamentoPedidos.cs               # + ListarResumosAsync
│       ├── IServicoPedidos.cs / ServicoPedidos.cs # + ListarAsync (escopo, cursor, keyset)
│       └── EventosPedidos.cs                      # + 4006 PedidosListados
└── PedidoFulfillment.Infrastructure/
    └── Persistencia/
        ├── ArmazenamentoPedidos.cs                # + ListarResumosAsync (projeção, keyset)
        ├── Configuracoes/ConfiguracaoPedido.cs    # índices novos
        └── Migracoes/<data>_ListagemPedidos.cs     # nova

tests/
├── PedidoFulfillment.Testes.Unitarios/Pedidos/
│   ├── ValidacaoListarPedidosTestes.cs
│   ├── CodificadorCursorPedidosTestes.cs
│   └── ServicoPedidosListagemTestes.cs
└── PedidoFulfillment.Testes.Integracao/Pedidos/
    ├── ListagemPedidosTestes.cs                   # escopo, filtros, navegação, 401/403/400
    └── ListagemPedidosDesempenhoTestes.cs         # SC-004

specs/001-contratos-api/contracts/openapi-v1.yaml  # + get /api/v1/pedidos e schemas
docs/fluxos/03-consulta-e-mudanca-de-status.md      # + seção Listagem
docs/fluxos/README.md                               # índice: rota nova
```

**Structure Decision**: os projetos existentes (API, Application, Infrastructure e os dois de
testes); nenhum projeto novo. A Application define `IChaveCursorPedidos` e só conhece a
interface; a implementação de hoje, `ChaveCursorDerivadaDoJwt`, fica na API, onde estão as
`OpcoesJwt`, e é registrada na inicialização. Assim a Application não depende da configuração da
API, e a troca futura por uma chave própria muda só o registro (R-04).

## Complexity Tracking

Sem violações da constituição.
