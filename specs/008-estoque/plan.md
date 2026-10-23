# Implementation Plan: Reserva de estoque assíncrona com concorrência otimista

**Branch**: `feature/req-08-estoque` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/008-estoque/spec.md`

**Requisito**: REQ-08 (GitHub issue #8)

## Summary

- **Regra no domínio**: `Pedido.ReservarEstoque` (tudo ou nada, devolve as faltas) e
  `Pedido.DevolverEstoque`, com a marca nova `EstoqueReservado` (R-02).
- **Consumidor `estoque`**: `ReservaEstoque`, no Application, implementa
  `IManipuladorEvento<PedidoCriado>` sobre a base do REQ-06 e é registrado no Worker
  (R-01). Ele:
  - reserva pedidos não finais e ainda sem reserva, sem mudar o status;
  - sem estoque, cancela o pedido;
  - ignora status finais (R-04);
  - grava `estoque.reservado` ou `estoque.insuficiente` no outbox, na mesma transação.
- **Concorrência otimista**: ciclo "ler, decidir, gravar" até 3 vezes, com os savepoints
  do EF Core dentro da transação da base; esgotado, as novas tentativas e a DLQ do REQ-06
  (R-03).
- **Devolução no cancelamento pela API**: o `ServicoPedidos` devolve o estoque na mesma
  gravação do `Cancelado`, e o laço de reavaliação que já existe resolve a corrida com o
  worker (R-05).
- **Contrato novo** `estoque.insuficiente` v1, com schema e testes (R-06).
- **Persistência**: coluna `Pedidos.EstoqueReservado`, na migration `ReservaEstoque` (R-08).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: `RabbitMQ.Client` 7.2.2 e EF Core SqlServer 10.0.13;
- nos testes: `JsonSchema.Net` 9.4.0, já em uso;
- nenhum pacote novo.

**Storage**: SQL Server; coluna nova `Pedidos.EstoqueReservado` (migration `ReservaEstoque`)

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker) e
Testcontainers MsSql e RabbitMq

**Target Platform**: API e Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**: 95% dos pedidos com o resultado do estoque gravado e publicado em
até 5 s (SC-001)

**Constraints**:
- concorrência otimista, sem bloqueio pessimista (issue);
- contrato HTTP v1 inalterado;
- reserva tudo ou nada;
- efeito, evento e registro de processado na mesma transação.

**Scale/Scope**:
- 1 consumidor e 1 contrato novo, com schema;
- 2 operações de domínio, 1 campo e 1 migration;
- mudanças em `IArmazenamentoPedidos`, `ArmazenamentoPedidos` e `ServicoPedidos`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify e o clarify (2 perguntas), commit `e268005` `[REQ-08]` |
| II. Contratos versionados | ✅ | `estoque.insuficiente` v1 com schema publicado; `estoque.reservado` v1 sem mudança; OpenAPI v1 intocado (a devolução não muda requisição nem resposta) |
| III. Segurança por padrão | ✅ | Nenhuma rota nova. O PATCH mantém a autorização do REQ-05. Logs só com identificadores (R-09). Nenhum segredo novo |
| IV. Desacoplamento assíncrono | ✅ | A reserva roda no Worker, disparada por `pedido.criado`; os resultados saem pelo outbox na transação do efeito |
| V. Qualidade verificável | ✅ | Testes unitários das regras de reserva, devolução e decisão do consumidor. Serialização e schema do contrato novo. Integração com banco e broker reais, incluindo a disputa (SC-002) |
| RabbitMQ.Client nativo | ✅ | Usa a base do REQ-06, sem MassTransit, apesar da issue |
| Endpoint só roteia | ✅ | A devolução fica no `ServicoPedidos` e no domínio; o endpoint não muda |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português; eventos no passado | ✅ | `estoque.insuficiente`, `ReservaEstoque`, `EstoqueReservado`, `FaltaEstoque` |
| Branch dedicada | ✅ | `feature/req-08-estoque`, a partir da `main` sincronizada depois do merge do PR #21 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/008-estoque/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── consumidor-estoque.md
│   └── eventos/estoque.insuficiente.v1.schema.json
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   ├── Pedidos/Pedido.cs                  # EstoqueReservado, ReservarEstoque, DevolverEstoque
│   └── Pedidos/FaltaEstoque.cs            # NOVO
├── PedidoFulfillment.Application/
│   ├── Contratos/Eventos/Eventos.cs       # + EstoqueInsuficiente, ItemEstoqueInsuficiente
│   ├── Contratos/Eventos/TiposEvento.cs   # + estoque.insuficiente
│   ├── Estoque/                           # NOVO
│   │   ├── ReservaEstoque.cs              # IManipuladorEvento<PedidoCriado>
│   │   └── EventosEstoque.cs              # logs 6001-6005
│   └── Pedidos/
│       ├── IArmazenamentoPedidos.cs       # ObterProdutosAsync(paraAlterar), Adicionar(EventoSaida)
│       └── ServicoPedidos.cs              # devolução no Cancelado
├── PedidoFulfillment.Infrastructure/
│   └── Persistencia/
│       ├── ArmazenamentoPedidos.cs        # leitura rastreada, evento avulso, deadlock (R-07)
│       ├── Configuracoes/ConfiguracaoPedido.cs   # EstoqueReservado
│       └── Migracoes/                     # + ReservaEstoque
└── PedidoFulfillment.Worker/ProgramaWorker.cs    # AdicionarConsumidor<ReservaEstoque, PedidoCriado>

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoEstoqueTestes.cs              # NOVO
│   ├── Estoque/ReservaEstoqueTestes.cs             # NOVO, com o armazenamento falso
│   ├── Pedidos/{Falsos,ServicoPedidosStatusTestes}.cs   # devolução
│   └── Mensageria/SerializacaoEventosTestes.cs     # + EstoqueInsuficiente
└── PedidoFulfillment.Testes.Integracao/
    ├── PedidoFulfillment.Testes.Integracao.csproj  # copia também specs/008-estoque/contracts/eventos
    ├── Estoque/                                    # NOVO
    │   ├── ReservaEstoqueTestes.cs                 # pelo Worker
    │   ├── DisputaEstoqueTestes.cs                 # SC-002
    │   └── DevolucaoEstoqueTestes.cs               # pela API
    ├── Mensageria/SchemaEventosTestes.cs           # exemplos de estoque.insuficiente
    └── Persistencia/{ModeloSincronizado,MigracaoBanco}Testes.cs   # 5ª migration
```

**Structure Decision**: nenhum projeto novo. A regra fica no Domain, o consumidor no
Application (onde o REQ-06 previu os workers de negócio), a persistência na Infrastructure,
e o Worker só registra o consumidor.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Reavaliar dentro da transação aberta pela base | Savepoint automático do EF Core a cada `SaveChanges`; um teste de integração força conflito e confere que a segunda tentativa grava só o que deve (R-03) |
| Deadlock entre reservas concorrentes | Comandos ordenados por tabela e chave; o resto propaga para as novas tentativas da base, com transação nova (R-03, R-07) |
| Teste de disputa intermitente | Manipulador em paralelo, com escopo e transação próprios, e verificação só do invariante (N reservados, M − N cancelados, estoque 0), que vale para qualquer intercalação |
| Corrida entre cancelamento pela API e reserva | Versão do pedido; os dois lados releem depois do conflito (R-05). Teste com as duas ordens |
| Mudança de assinatura em `IArmazenamentoPedidos` | Só a criação usa `ObterProdutosAsync` hoje; o falso dos testes unitários é atualizado junto |
| Pedidos criados antes deste REQ, sem reserva | `EstoqueReservado = 0` na migration; o cancelamento deles não mexe em estoque, o que está correto |
