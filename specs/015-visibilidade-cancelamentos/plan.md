# Implementation Plan: Visibilidade dos cancelamentos e dos estornos para outros sistemas

**Branch**: `feature/req-15-visibilidade-cancelamentos` | **Date**: 2026-10-26 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/015-visibilidade-cancelamentos/spec.md`

**Requisito**: REQ-15 (GitHub issue #29)

## Summary

- **Dois tipos novos na v1**: `pedido.cancelado` (motivo em categoria, etapa esgotada, o que
  foi desfeito junto) e `pagamento.estornado` (valor e identificadores do gateway), com schema
  em `contracts/eventos/` e testes de conformidade (R-04).
- **Todo cancelamento passa pelo domínio com resultado**: a falta de estoque e a reconciliação
  da etapa de estoque trocam `MudarStatus(Cancelado)` por `Pedido.Cancelar`, e
  `RegistrarPagamentoNaoProcessado` passa a devolver `ResultadoCancelamento` (R-01).
- **Um ponto monta o evento**: `EventosPedido.Cancelado(...)` no Application; `ServicoPedidos`,
  `ReservaEstoque` e `ServicoReconciliacao` o adicionam ao outbox na mesma gravação, e o
  `ServicoEstornos` adiciona o `pagamento.estornado` na confirmação (R-02, R-05).
- **Guarda na gravação**: `ArmazenamentoPedidos.SalvarAsync` recusa um pedido que passa a
  `Cancelado` sem `pedido.cancelado`, ou um pagamento que passa a `Estornado` sem
  `pagamento.estornado` (R-03).
- **Sem mudança** na mensageria, no banco (nenhuma migration), no contrato HTTP e nos logs
  (R-06 a R-08).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: EF Core SqlServer 10.0.13, `RabbitMQ.Client` 7.2.2 e, nos testes,
  `JsonSchema.Net` (conformidade dos schemas);
- nenhum pacote novo.

**Storage**: SQL Server; tabela `EventosSaida` existente. Nenhuma migration (R-07).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker), Testcontainers
MsSql e RabbitMq, `JsonSchema.Net`

**Target Platform**: API e Worker em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- cancelamento pela API no mesmo tempo de hoje: um `INSERT` a mais na mesma transação
  (SC-006);
- eventos novos publicados no mesmo ritmo dos demais pelo despachante (SC-003);
- fluxo normal do pedido sem mudança (SC-006).

**Constraints**:
- contratos v1 só com acréscimos; tipos existentes e contrato HTTP inalterados;
- evento na mesma transação da mudança de estado (Princípio IV, outbox);
- exatamente um `pedido.cancelado` por cancelamento e um `pagamento.estornado` por estorno;
- sem dados pessoais, identificador do Cliente ou texto livre do motivo nos eventos.

**Scale/Scope**:
- 2 records, 2 enums e 2 entradas de catálogo no contrato de eventos; 2 schemas;
- 1 fábrica de eventos no Application;
- mudanças localizadas em `ServicoPedidos`, `ReservaEstoque`, `ServicoReconciliacao`,
  `ServicoEstornos`, `Pedido.RegistrarPagamentoNaoProcessado` e `ArmazenamentoPedidos`;
- 1 linha no `.csproj` dos testes de integração para copiar os schemas novos.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify (decisões da #29 e a opção A para `PagamentoRejeitado`) e o clarify (3 perguntas), commits `04913ac` e `41ca629` `[REQ-15]` |
| II. Contratos versionados | ✅ | Dois tipos novos como acréscimo à v1, com schema publicado em `contracts/eventos/` e testes de serialização e conformidade. Tipos existentes, schemas existentes e contrato HTTP inalterados. Mudança futura em `motivo`/`etapaEsgotada` documentada como incompatível no contrato |
| III. Segurança por padrão | ✅ | Nenhuma rota nova. Eventos sem dados pessoais, sem `clienteId` e sem texto livre (FR-010). Identificadores do gateway só no evento, fora dos logs (clarify Q2) |
| IV. Desacoplamento assíncrono | ✅ | É o objetivo do requisito: outros sistemas sabem do cancelamento e do estorno pela mensageria, com outbox na mesma transação |
| V. Qualidade verificável | ✅ | Unitários da fábrica, do domínio e dos quatro serviços; schema dos dois tipos; integração por caminho, concorrência, falha na gravação e guarda (R-09) |
| RabbitMQ.Client nativo | ✅ | Mensageria intocada; os tipos novos usam o despachante existente |
| Endpoint só roteia | ✅ | Nenhum endpoint muda |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `PedidoCancelado`, `PagamentoEstornado`, `MotivoCancelamento`, `EtapaEsgotada`, `EventosPedido`; eventos no passado (`pedido.cancelado`, `pagamento.estornado`) |
| Branch dedicada | ✅ | `feature/req-15-visibilidade-cancelamentos`, a partir da `main` sincronizada depois do merge do PR #36 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/015-visibilidade-cancelamentos/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── eventos-cancelamento-e-estorno.md
│   └── eventos/
│       ├── pedido.cancelado.v1.schema.json
│       └── pagamento.estornado.v1.schema.json
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   └── Pedidos/
│       ├── Pedido.cs                                # RegistrarPagamentoNaoProcessado devolve ResultadoCancelamento
│       └── ResultadoCancelamento.cs                 # comentário: usado também pelo evento
├── PedidoFulfillment.Application/
│   ├── Contratos/Eventos/
│   │   ├── Eventos.cs                               # PedidoCancelado, PagamentoEstornado, MotivoCancelamento, EtapaEsgotada
│   │   └── TiposEvento.cs                           # pedido.cancelado, pagamento.estornado
│   ├── Pedidos/
│   │   ├── EventosPedido.cs                         # NOVO: fábrica do pedido.cancelado
│   │   └── ServicoPedidos.cs                        # evento no cancelamento; Cliente/Operacao
│   ├── Estoque/ReservaEstoque.cs                    # Pedido.Cancelar + pedido.cancelado
│   ├── Reconciliacao/ServicoReconciliacao.cs        # Pedido.Cancelar na etapa de estoque; evento com a etapa
│   └── Pagamentos/ServicoEstornos.cs                # pagamento.estornado na confirmação
└── PedidoFulfillment.Infrastructure/
    └── Persistencia/ArmazenamentoPedidos.cs         # guarda do R-03 no SalvarAsync

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoCancelamentoTestes.cs                     # cancelar sem reserva (caminho do estoque)
│   ├── Dominio/PedidoPagamentoNaoProcessadoTestes.cs           # resultado devolvido
│   ├── Pedidos/EventosPedidoTestes.cs                          # NOVO: fábrica
│   ├── Pedidos/ServicoPedidosStatusTestes.cs                   # Cliente e Operacao no evento
│   ├── Estoque/ReservaEstoqueTestes.cs                         # evento EstoqueInsuficiente
│   ├── Pagamentos/ServicoEstornosTestes.cs                     # evento na confirmação e nas reavaliações
│   ├── Reconciliacao/ServicoReconciliacaoTestes.cs             # evento com a etapa esgotada
│   └── Mensageria/SerializacaoEventosTestes.cs                 # ida e volta dos tipos novos
└── PedidoFulfillment.Testes.Integracao/
    ├── PedidoFulfillment.Testes.Integracao.csproj              # copia os schemas de specs/015
    ├── Mensageria/SchemaEventosTestes.cs                       # dois tipos novos, válidos e inválidos
    ├── Mensageria/EventosCancelamentoTestes.cs                 # NOVO: fila de teste ligada a oms.eventos; consumidores internos não recebem
    ├── Pedidos/StatusPedidoComBancoTestes.cs                   # Cliente e Operacao: evento no outbox
    ├── Estoque/ReservaEstoqueTestes.cs                         # EstoqueInsuficiente
    ├── Estornos/CancelamentoPedidoPagoTestes.cs                # estornoPendente e ordemEntregaCancelada
    ├── Estornos/ProcessamentoEstornosTestes.cs                 # pagamento.estornado
    ├── Estornos/DisputaCancelamentoTestes.cs                   # um evento só nas corridas
    ├── Reconciliacao/EsgotamentoTestes.cs                      # três etapas com etapaEsgotada
    └── Persistencia/GuardaEventosCancelamentoTestes.cs         # NOVO: guarda do R-03

docs/fluxos/                                         # 03, 04, 08, 09, 10 e README (Polish, R-10)
```

**Structure Decision**: nenhum projeto novo. O contrato fica em `Application/Contratos/Eventos`,
como os demais; a montagem do evento, no Application, junto dos serviços que cancelam; a guarda,
no `ArmazenamentoPedidos` do Infrastructure, que é por onde todos esses serviços gravam. O
domínio só passa a devolver o que o cancelamento fez em todos os caminhos.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Um caminho de cancelamento (atual ou futuro) esquece o evento | Guarda no `SalvarAsync` (R-03), com teste próprio; testes de integração por caminho |
| Trocar `MudarStatus` por `Cancelar` muda o comportamento da falta de estoque ou da reconciliação | Sem reserva, sem pagamento e sem ordem, `Cancelar` faz o mesmo (valida a transição e grava o histórico sem autor); os testes do REQ-08 e do REQ-12 continuam como estão |
| Evento duplicado em reavaliação por conflito | `DescartarAlteracoes` limpa o evento junto; ele é adicionado de novo a cada tentativa, e só a gravação que vence o leva (R-02, R-05) |
| `etapaEsgotada` nulo quebrando consumidores rígidos | Campo sempre presente, com `if/then/else` no schema e exemplos no contrato |
| Consumidor interno recebendo os tipos novos | Troca `topic` com ligação por tipo; teste de integração confere que as filas `oms.estoque`, `oms.pagamento` e `oms.entrega` não recebem |
| Ninguém escutando os tipos novos e o evento "some" | Comportamento documentado no contrato e no fluxo 8 (tipos sem consumidor interno saem sem rota obrigatória) |
| Testes que semeiam pedidos cancelados direto pelo contexto | A guarda fica no `ArmazenamentoPedidos`, não no `ContextoBancoDados`; a semeadura pelo contexto não passa por ela |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxos 3, 4, 8, 9, 10 e o índice, conforme a regra do `CLAUDE.md` (R-10) |
