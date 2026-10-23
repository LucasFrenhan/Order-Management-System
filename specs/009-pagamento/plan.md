# Implementation Plan: Pagamento assíncrono simulado com compensação do estoque

**Branch**: `feature/req-09-pagamento` | **Date**: 2026-10-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/009-pagamento/spec.md`

**Requisito**: REQ-09 (GitHub issue #9)

## Summary

- **Regra no domínio**: `Pedido.AprovarPagamento` e `Pedido.RecusarPagamento`, com a
  condição `AptoParaPagamento`. A recusa reaproveita `DevolverEstoque` do REQ-08 na mesma
  operação (R-03, clarify).
- **Gateway simulado**: porta `IGatewayPagamento` no Application e
  `GatewayPagamentoSimulado` na Infrastructure, com `GatewayPagamento:TaxaAprovacaoPercentual`
  (padrão 90, de 0 a 100, validada na partida) (R-02).
- **Consumidor `pagamento`**: `ProcessamentoPagamento`, no Application, implementa
  `IManipuladorEvento<EstoqueReservado>` sobre a base do REQ-06 e é registrado no Worker
  (R-01). Ele:
  - ignora pedidos não aptos (status final, sem reserva ou já pagos) (FR-002);
  - chama o gateway uma vez por processamento (R-05);
  - grava pagamento, status, devolução (na recusa) e `pagamento.aprovado` ou
    `pagamento.recusado` no outbox, na mesma transação.
- **Concorrência otimista**: "ler, decidir, gravar" até 3 vezes, com o savepoint do EF Core
  dentro da transação da base; esgotado, as novas tentativas e a DLQ do REQ-06 (R-05, R-07).
- **Persistência**: nenhuma migration; `ObterPedidoAsync` passa a incluir o `Pagamento`
  (R-06, R-11).
- **Testes do REQ-08**: continuam isolados, sem o consumidor de pagamento (R-09).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: `RabbitMQ.Client` 7.2.2 e EF Core SqlServer 10.0.13;
- nos testes: `JsonSchema.Net` 9.4.0, já em uso;
- nenhum pacote novo.

**Storage**: SQL Server; tabelas `Pagamentos`, `Pedidos`, `Produtos` e
`HistoricoStatusPedido` já existentes, sem migration

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker) e
Testcontainers MsSql e RabbitMq

**Target Platform**: API e Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**: 95% dos pedidos reservados com o resultado do pagamento gravado e
publicado em até 5 s (SC-001)

**Constraints**:
- concorrência otimista, sem bloqueio pessimista;
- contrato HTTP v1 e contratos de eventos v1 inalterados;
- pagamento, status, devolução, evento e registro de processado na mesma transação;
- gateway chamado no máximo uma vez por processamento.

**Scale/Scope**:
- 1 consumidor, 1 porta com 1 implementação simulada e 1 seção de configuração;
- 2 operações de domínio e 1 condição;
- mudança pequena em `ArmazenamentoPedidos` (`Include`) e no `ProgramaWorker`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify e o clarify (3 perguntas), commit `a7226a3` `[REQ-09]` |
| II. Contratos versionados | ✅ | `estoque.reservado`, `pagamento.aprovado` e `pagamento.recusado` v1 usados sem mudança, com os schemas do REQ-06; OpenAPI v1 intocado |
| III. Segurança por padrão | ✅ | Nenhuma rota nova; autorização do PATCH sem mudança. Logs só com identificadores e valores (R-08). Nenhum segredo novo: a taxa de aprovação não é sensível |
| IV. Desacoplamento assíncrono | ✅ | O pagamento roda no Worker, disparado por `estoque.reservado`; os resultados saem pelo outbox na transação do efeito |
| V. Qualidade verificável | ✅ | Testes unitários das regras de aprovação e recusa, da decisão do consumidor e da proporção do gateway (semente fixa). Integração com banco e broker reais, com a taxa fixada em 100% ou 0%, e validação das mensagens pelos schemas |
| RabbitMQ.Client nativo | ✅ | Usa a base do REQ-06, sem MassTransit, apesar da issue |
| Endpoint só roteia | ✅ | Nenhum endpoint muda |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português; eventos no passado | ✅ | `ProcessamentoPagamento`, `IGatewayPagamento`, `AprovarPagamento`, `pagamento.aprovado`, `pagamento.recusado` |
| Branch dedicada | ✅ | `feature/req-09-pagamento`, a partir da `main` sincronizada depois do merge do PR #22 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/009-pagamento/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/consumidor-pagamento.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   └── Pedidos/Pedido.cs                       # AptoParaPagamento, AprovarPagamento, RecusarPagamento
├── PedidoFulfillment.Application/
│   └── Pagamentos/                             # NOVO
│       ├── IGatewayPagamento.cs                # porta + ResultadoCobranca
│       ├── ProcessamentoPagamento.cs           # IManipuladorEvento<EstoqueReservado>
│       └── EventosPagamento.cs                 # logs 7001-7004
├── PedidoFulfillment.Infrastructure/
│   ├── Pagamentos/                             # NOVO
│   │   ├── GatewayPagamentoSimulado.cs
│   │   └── ExtensoesPagamento.cs               # AdicionarGatewayPagamentoSimulado
│   ├── Configuracao/OpcoesGatewayPagamento.cs  # NOVO, com o validador
│   └── Persistencia/ArmazenamentoPedidos.cs    # Include(Pagamento)
└── PedidoFulfillment.Worker/
    ├── ProgramaWorker.cs                       # gateway + AdicionarConsumidor<ProcessamentoPagamento, EstoqueReservado>
    └── appsettings.json                        # GatewayPagamento:TaxaAprovacaoPercentual = 90

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoPagamentoTestes.cs                 # NOVO
│   ├── Pagamentos/ProcessamentoPagamentoTestes.cs       # NOVO, com gateway falso
│   ├── Pagamentos/GatewayPagamentoSimuladoTestes.cs     # NOVO, proporção com semente (SC-002)
│   ├── Configuracao/OpcoesGatewayPagamentoTestes.cs     # NOVO, validador
│   └── Pedidos/Falsos.cs                                # pagamento no pedido falso, se preciso
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaWorker.cs                  # taxa 100 por padrão
    ├── Estoque/CenarioEstoque.cs                        # Worker sem o consumidor de pagamento (R-09)
    ├── Pagamentos/                                      # NOVO
    │   ├── CenarioPagamento.cs
    │   ├── PagamentoAprovadoTestes.cs
    │   ├── PagamentoRecusadoTestes.cs
    │   └── PedidoIgnoradoNoPagamentoTestes.cs           # cancelado, já pago, corrida com a API
    └── Mensageria/WorkerTestes.cs                       # taxa inválida impede a partida
```

**Structure Decision**: nenhum projeto novo. A regra fica no Domain, o consumidor e a porta
no Application, o gateway simulado e a configuração na Infrastructure, e o Worker só
registra.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Testes do REQ-08 mudam de resultado com o pagamento no mesmo Worker | `CenarioEstoque` remove o consumidor de pagamento no teste; os testes de pagamento fixam a taxa (R-09) |
| Resultado aleatório deixa testes instáveis | Taxa 100 ou 0 na integração; gateway falso nos unitários; semente fixa no teste da proporção (R-02) |
| Reavaliação sorteando resultado diferente | Gateway chamado uma vez por processamento; o resultado é reaproveitado (R-05) |
| Devolução duplicada (recusa e cancelamento ao mesmo tempo) | Versão do pedido; os dois lados releem e conferem `EstoqueReservado` e o status (R-07). Teste com as duas ordens |
| Pagamento inserido pela navegação sem ser detectado | Teste de integração confere a linha em `Pagamentos`; o índice único em `PedidoId` barra um segundo pagamento |
| Fila `oms.pagamento` sem consumidor em testes do REQ-08 acumulando mensagens | `LimparFilasConsumidorAsync("pagamento")` no início dos testes de pagamento, como no REQ-08 |
| Quickstart do REQ-08 (pedido continua `Pendente`, Cliente cancela) deixa de valer com o pagamento ligado | Nota no `specs/008-estoque/quickstart.md` indicando a taxa 0/100 ou o efeito do pagamento |
