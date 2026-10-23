# Implementation Plan: Ordem de entrega assíncrona para pedidos pagos

**Branch**: `feature/req-10-fulfillment` | **Date**: 2026-10-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/010-fulfillment/spec.md`

**Requisito**: REQ-10 (GitHub issue #10)

## Summary

- **Regra no domínio**: `Pedido.CriarOrdemEntrega`, com a condição `AptoParaOrdemEntrega`
  (reserva, pagamento aprovado, sem ordem, em `Processando` ou `ProntoParaEnvio`). Em
  `Processando`, muda para `ProntoParaEnvio` com `Ordem de entrega criada.` e sem autor (R-02, R-03).
- **Contrato novo `entrega.criada` v1**: record `EntregaCriada`, entrada em `TiposEvento` e
  schema em `specs/010-fulfillment/contracts/eventos/`, com testes de serialização, ida e
  volta e recusa fora do schema (R-07).
- **Consumidor `entrega`**: `CriacaoOrdemEntrega`, no Application, implementa
  `IManipuladorEvento<PagamentoAprovado>` sobre a base do REQ-06 e é registrado no Worker
  (R-01). Ele:
  - ignora pedidos não aptos, com log do motivo (FR-002);
  - grava a ordem, o status e `entrega.criada` no outbox, na mesma transação.
- **Concorrência otimista**: "ler, decidir, gravar" até 3 vezes; esgotado, as novas
  tentativas e a DLQ do REQ-06. Uma ordem por pedido garantida pelo registro de processado,
  pela versão do pedido e pelo índice único (R-04).
- **Persistência**: nenhuma migration; `ObterPedidoAsync` passa a incluir a `OrdemEntrega`
  (R-05, R-10).
- **Testes do REQ-08 e do REQ-09**: continuam isolados, sem o consumidor de entrega (R-09).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: `RabbitMQ.Client` 7.2.2 e EF Core SqlServer 10.0.13;
- nos testes: `JsonSchema.Net` 9.4.0, já em uso;
- nenhum pacote novo.

**Storage**: SQL Server; tabelas `OrdensEntrega`, `Pedidos`, `Pagamentos` e
`HistoricosStatusPedido` já existentes, sem migration

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker) e
Testcontainers MsSql e RabbitMq

**Target Platform**: API e Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**: 95% dos pedidos pagos com a ordem, o status e `entrega.criada` em até
5 s (SC-001); fluxo completo do pedido criado ao `entrega.criada` em até 10 s (SC-005)

**Constraints**:
- concorrência otimista, sem bloqueio pessimista;
- contrato HTTP v1 e contratos de eventos existentes inalterados;
- ordem, status, evento e registro de processado na mesma transação;
- estoque e pagamento intocados pelo fulfillment.

**Scale/Scope**:
- 1 consumidor e 1 contrato de evento novo, com schema;
- 1 operação de domínio e 1 condição;
- mudança pequena em `ArmazenamentoPedidos` (`Include`), no `ProgramaWorker` e no projeto
  de testes de integração (cópia dos schemas).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify e o clarify (1 pergunta: pedidos pagos antes da fila, fora do escopo), commits `e773406` e `d2ff5a0` `[REQ-10]` |
| II. Contratos versionados | ✅ | `entrega.criada` nasce na v1, com schema e testes; `pagamento.aprovado` v1 usado sem mudança; OpenAPI v1 intocado |
| III. Segurança por padrão | ✅ | Nenhuma rota nova. Evento e logs só com identificadores, sem dados pessoais (R-07, R-08). Nenhum segredo novo |
| IV. Desacoplamento assíncrono | ✅ | O fulfillment roda no Worker, disparado por `pagamento.aprovado`; `entrega.criada` sai pelo outbox na transação do efeito |
| V. Qualidade verificável | ✅ | Testes unitários da regra de domínio e da decisão do consumidor; testes de serialização e schema do contrato novo; integração com banco e broker reais e validação das mensagens publicadas pelo schema |
| RabbitMQ.Client nativo | ✅ | Usa a base do REQ-06, sem MassTransit, apesar da issue |
| Endpoint só roteia | ✅ | Nenhum endpoint muda |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português; eventos no passado | ✅ | `CriacaoOrdemEntrega`, `CriarOrdemEntrega`, fila `oms.entrega`, evento `entrega.criada` |
| Branch dedicada | ✅ | `feature/req-10-fulfillment`, a partir da `main` sincronizada depois do merge do PR #23 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/010-fulfillment/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── consumidor-entrega.md
│   └── eventos/entrega.criada.v1.schema.json
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   └── Pedidos/Pedido.cs                         # AptoParaOrdemEntrega, CriarOrdemEntrega
├── PedidoFulfillment.Application/
│   ├── Contratos/Eventos/Eventos.cs              # EntregaCriada, ItemEntregaCriada
│   ├── Contratos/Eventos/TiposEvento.cs          # entrega.criada
│   └── Entregas/                                 # NOVO
│       ├── CriacaoOrdemEntrega.cs                # IManipuladorEvento<PagamentoAprovado>
│       └── EventosEntrega.cs                     # logs 8001-8003
├── PedidoFulfillment.Infrastructure/
│   └── Persistencia/ArmazenamentoPedidos.cs      # Include(OrdemEntrega)
└── PedidoFulfillment.Worker/
    └── ProgramaWorker.cs                         # AdicionarConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoOrdemEntregaTestes.cs                 # NOVO
│   ├── Entregas/CriacaoOrdemEntregaTestes.cs               # NOVO, com armazenamento falso
│   └── Mensageria/SerializacaoEventosTestes.cs             # entrega.criada
└── PedidoFulfillment.Testes.Integracao/
    ├── PedidoFulfillment.Testes.Integracao.csproj          # copia os schemas do 010
    ├── Mensageria/SchemaEventosTestes.cs                   # entrega.criada válido e recusas
    ├── Estoque/CenarioEstoque.cs                           # Worker sem o consumidor de entrega
    ├── Pagamentos/CenarioPagamento.cs                      # Worker sem o consumidor de entrega (R-09)
    └── Entregas/                                           # NOVO
        ├── CenarioEntrega.cs
        ├── OrdemEntregaCriadaTestes.cs                     # caminho feliz, reentrega, ProntoParaEnvio
        └── PedidoIgnoradoNaEntregaTestes.cs                # cancelado, despachado, já com ordem, inexistente, corrida com a API
```

**Structure Decision**: nenhum projeto novo. A regra fica no Domain, o consumidor e o
contrato no Application, e o Worker só registra.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Testes do REQ-09 mudam de resultado (`ProntoParaEnvio` em vez de `Processando`) com a entrega no mesmo Worker | `CenarioPagamento` e `CenarioEstoque` removem o consumidor de entrega no teste (R-09) |
| `pagamento.aprovado` de outros testes acumulado em `oms.entrega` | `LimparFilasConsumidorAsync("entrega")` no início dos testes de entrega, como no REQ-08 e no REQ-09 |
| Conflito com a API não detectado quando o status não muda | `CriarOrdemEntrega` sempre altera `AtualizadoEm`; o `UPDATE` do pedido leva a `rowversion` (R-04) |
| Ordem inserida pela navegação sem ser detectada | Teste de integração confere a linha em `OrdensEntrega`; o índice único em `PedidoId` barra uma segunda |
| Pedido cancelado com `entrega.criada` publicado | Releitura e conferência do status a cada reavaliação; teste da corrida com as duas ordens (US3 cenário 5) |
| Quickstart do REQ-09 (pedido aprovado em `Processando`) deixa de valer com o Worker completo | Nota em `specs/009-pagamento/quickstart.md` (R-11) |
