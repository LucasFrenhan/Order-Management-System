# Implementation Plan: Rastreio, despacho e histórico na consulta de pedidos

**Branch**: `feature/req-11-rastreio` | **Date**: 2026-10-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/011-rastreio/spec.md`

**Requisito**: REQ-11 (GitHub issue #11)

## Summary

- **Contrato v1, evolução compatível**: `PedidoResponseDTO` ganha `historico` e `entrega`, e
  entra a operação `PATCH /api/v1/pedidos/{id}/rastreio`, com o código
  `PEDIDO_RASTREIO_DUPLICADO`, no `openapi-v1.yaml` do REQ-01 (R-01, R-02).
- **Regra no domínio**: `Pedido.RegistrarRastreio` despacha um pedido `ProntoParaEnvio`
  (criando a ordem, se faltar) ou corrige o rastreio de um `Despachado`, sem mexer no
  status; dados iguais não mudam nada. `OrdemEntrega.CorrigirRastreio` mantém a data de
  despacho (R-05).
- **Serviço**: `ServicoPedidos.RegistrarRastreioAsync`, com política só para Admin e
  OperadorLogistica, conferência de duplicidade e o laço de reavaliação do REQ-05 (R-06 a
  R-08).
- **Persistência**: a leitura do pedido inclui o histórico (`AsSplitQuery`); uma migration
  põe collation sem diferença de caixa nas colunas de rastreio; a violação do índice único
  vira `RastreioDuplicadoException` (R-04, R-06).
- **Consulta**: o mapeamento devolve o histórico em ordem, sem o autor, e a entrega (R-02).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: ASP.NET Core (minimal APIs, OpenAPI), EF Core SqlServer 10.0.13;
- nenhum pacote novo.

**Storage**: SQL Server; tabelas `Pedidos`, `HistoricosStatusPedido` e `OrdensEntrega` já
existentes; uma migration de collation em `OrdensEntrega` (R-06)

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory e Testcontainers MsSql e
RabbitMq; `ConformidadeOpenApiTestes` para o contrato

**Target Platform**: API e Worker em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em contêiner

**Project Type**: web-service

**Performance Goals**: 95% das consultas e dos rastreios em menos de 500 ms no ambiente
local, com 10 itens e 10 entradas de histórico (SC-004)

**Constraints**:
- contrato HTTP v1 só com acréscimos (oasdiff no PR);
- concorrência otimista, sem bloqueio pessimista;
- endpoint só roteia; regra no domínio e no serviço;
- histórico sem o autor na resposta.

**Scale/Scope**:
- 1 rota nova, 3 schemas novos e 2 campos novos na resposta do pedido;
- 1 operação de domínio, 1 método novo na ordem, 1 política, 1 código de erro;
- 1 migration de collation.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec, specify e clarify (2 perguntas), commits `6aac55b` e `c6c9c2c` `[REQ-11]` |
| II. Contratos versionados | ✅ | Evolução compatível da v1 (só acréscimos), contrato do REQ-01 atualizado junto com o código, oasdiff no PR e teste de conformidade (R-01) |
| III. Segurança por padrão | ✅ | Rota nova só para Admin e OperadorLogistica (política própria); Cliente continua vendo só os próprios pedidos; histórico sem autor; logs só com identificadores (R-08, R-10) |
| IV. Desacoplamento assíncrono | ✅ | Nada novo na mensageria; o despacho é síncrono pela API, como a mudança de status do REQ-05. Evento "pedido despachado" fora do escopo (Assumptions) |
| V. Qualidade verificável | ✅ | Testes unitários da regra e do serviço; integração com banco real; conformidade OpenAPI; teste de desempenho |
| Endpoint só roteia | ✅ | `EndpointsPedidos` só valida, delega a `IServicoPedidos` e traduz o resultado |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `RegistrarRastreio`, `PoliticaRastreioPedido`, `PEDIDO_RASTREIO_DUPLICADO` |
| Branch dedicada | ✅ | `feature/req-11-rastreio`, da `main` sincronizada depois do merge do PR #24 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/011-rastreio/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/rastreio-http.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
specs/001-contratos-api/contracts/openapi-v1.yaml          # rota, schemas e campos novos (R-01)

src/
├── PedidoFulfillment.Domain/
│   ├── Pedidos/Pedido.cs                                  # RegistrarRastreio, PodeRegistrarRastreio
│   ├── Pedidos/ResultadoRastreio.cs                       # NOVO
│   └── Entregas/OrdemEntrega.cs                           # CorrigirRastreio
├── PedidoFulfillment.Application/
│   ├── Contratos/Pedidos/                                 # PedidoResponseDTO (+2), 3 DTOs novos
│   ├── Contratos/Erros/CodigosErro.cs                     # PEDIDO_RASTREIO_DUPLICADO
│   └── Pedidos/                                           # IServicoPedidos, ServicoPedidos, MapeamentoPedido,
│                                                          # IArmazenamentoPedidos, RastreioDuplicadoException, EventosPedidos
├── PedidoFulfillment.Infrastructure/
│   ├── Persistencia/ArmazenamentoPedidos.cs               # Include(Historico), RastreioEmUsoAsync, índice → exceção
│   ├── Persistencia/Configuracoes/ConfiguracaoOrdemEntrega.cs  # collation
│   └── Persistencia/Migracoes/*_RastreioSemCaixa.cs       # NOVO
└── PedidoFulfillment.API/
    ├── Endpoints/EndpointsPedidos.cs                      # PATCH /{id}/rastreio
    └── Seguranca/PoliticasAutorizacao.cs                  # PoliticaRastreioPedido

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoRastreioTestes.cs                    # NOVO
│   ├── Pedidos/ServicoPedidosRastreioTestes.cs            # NOVO
│   ├── Pedidos/ServicoPedidosConsultaTestes.cs            # histórico e entrega
│   └── Pedidos/Falsos.cs                                  # RastreioEmUsoAsync, duplicado na gravação
└── PedidoFulfillment.Testes.Integracao/
    ├── Contrato/ConformidadeOpenApiTestes.cs              # nova operação e schemas
    └── Pedidos/
        ├── ConsultaPedidoComBancoTestes.cs                # histórico e entrega
        ├── RastreioPedidoComBancoTestes.cs                # NOVO
        └── DesempenhoRastreioTestes.cs                    # NOVO (SC-004)
```

**Structure Decision**: nenhum projeto novo. A regra fica no Domain, a orquestração no
`ServicoPedidos` (Application), a persistência no Infrastructure e o endpoint só roteia.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Resposta idempotente gravada antes do deploy não tem `historico` | `Historico` com padrão `[]` e não `required` em C# (R-03) |
| Produto cartesiano ao ler itens e histórico juntos | `AsSplitQuery()` na leitura do pedido (R-04) |
| Correção sem mudança de status não detecta conflito | `Pedido.AtualizadoEm` muda na correção; o `UPDATE` leva a `rowversion` (R-07) |
| Dois despachos com o mesmo rastreio em pedidos diferentes, ao mesmo tempo | Índice único com collation sem caixa; violação → `RastreioDuplicadoException` → 409 (R-06) |
| Gerador de OpenAPI produz `null` de forma diferente do contrato escrito | O contrato segue a forma gerada, conferida pelo teste de conformidade (contracts/rastreio-http.md) |
| Testes existentes que comparam a resposta do pedido campo a campo | Revisar `ConsultaPedidoComBancoTestes`, `StatusPedidoComBancoTestes` e os de criação ao mudar o DTO |
