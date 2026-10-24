# Implementation Plan: Publicação garantida de eventos e reconciliação de pedidos parados

**Branch**: `feature/req-12-publicacao-garantida` | **Date**: 2026-10-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/012-publicacao-garantida/spec.md`

**Requisito**: REQ-12 (GitHub issue #26)

## Summary

- **Topologia antes de publicar**: cada consumidor registrado no Worker ganha um descritor
  (`IDescritorConsumidor`). A cada canal novo, o despachante do outbox declara as filas de
  todos os consumidores conhecidos com a mesma função que os consumidores usam (R-01, R-02).
- **Rota obrigatória**: os tipos com consumidor conhecido saem com `mandatory: true`. Uma
  devolução do broker (`PublishReturnException`) vira falha de publicação `SemRota`: o
  evento continua pendente, com a espera crescente do REQ-06. Ao recriar o canal, o
  despachante recria também a fila. Os tipos sem consumidor seguem como hoje (R-03).
- **Reconciliação**: `ReconciliadorPedidos`, serviço em segundo plano no Worker. O primeiro
  ciclo roda na partida, depois um a cada 5 min, com uma instância por ciclo (`sp_getapplock`).
  Cada ciclo lista até 100 pedidos parados há mais de 10 min, primeiro os sem falha anterior,
  e aplica a regra do consumidor da etapa pela interface nova `IEtapaFluxoPedido`, que os
  três consumidores passam a implementar (R-04 a R-09).
- **Esgotamento**: a contagem de falhas fica em memória. Com 3 ciclos com falha e mais de
  24 h parado:
  - estoque: o pedido vai para `Cancelado`;
  - pagamento: `Pedido.RegistrarPagamentoNaoProcessado`, com a situação nova
    `NaoProcessado`, o estoque devolvido e o pedido `Cancelado`;
  - entrega: nunca se esgota (R-06, R-07, R-12).
- **Configuração**: seção nova `Reconciliacao`, validada na partida. `Habilitada` fica
  desligada na `FabricaWorker` dos testes (R-10).
- **Persistência**: a migration `PagamentoNaoProcessado` só recria a check constraint (R-12).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: `RabbitMQ.Client` 7.2.2 (`PublishReturnException` com confirmações
  rastreadas) e EF Core SqlServer 10.0.13;
- nos testes: `JsonSchema.Net` 9.4.0, já em uso;
- nenhum pacote novo.

**Storage**: SQL Server; tabelas `Pedidos`, `Pagamentos`, `OrdensEntrega`, `Produtos` e
`EventosSaida` já existentes. Uma migration, `PagamentoNaoProcessado`, que recria
`CK_Pagamentos_SituacaoValida`. Lock de aplicação `sp_getapplock`, sem tabela.

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (Worker) e Testcontainers
MsSql e RabbitMq

**Target Platform**: Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- pedido parado de volta ao fluxo em até 15 min com os padrões (SC-002);
- o fluxo normal continua em até 10 s (SC-008): a declaração da topologia só acontece em
  canal novo, e `mandatory` não acrescenta ida e volta, porque a confirmação já é esperada.

**Constraints**:
- contratos de eventos v1 e contrato HTTP v1 inalterados;
- regras dos consumidores sem duplicação (FR-017);
- nada gravado no pedido por falha da reconciliação;
- concorrência otimista, sem bloqueio pessimista em pedido;
- uma instância da reconciliação por ciclo.

**Scale/Scope**:
- 1 interface e 3 consumidores adaptados;
- 1 serviço de reconciliação e 1 serviço em segundo plano;
- 1 porta de consulta, 1 seção de opções e 1 migration;
- mudança localizada no despachante e no registro de consumidores.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify e o clarify (3 perguntas: reconciliação automática, falha repetida, esgotamento), commits `59dc839`, `15516c4` e `d4dfac9` `[REQ-12]` |
| II. Contratos versionados | ✅ | Nenhum contrato de evento muda; OpenAPI v1 intocado. A situação `NaoProcessado` é interna, sem aparecer na API nem nos eventos (R-12) |
| III. Segurança por padrão | ✅ | Nenhuma rota nova. Logs só com identificadores, etapas e contagens (R-11). Nenhum segredo novo |
| IV. Desacoplamento assíncrono | ✅ | Os eventos continuam saindo só pelo outbox, e agora não se perdem por falta de fila. A reconciliação roda no Worker e grava os eventos de saída no outbox, na transação do efeito |
| V. Qualidade verificável | ✅ | Unitários do domínio, do serviço de reconciliação, do registro de falhas, das opções e da topologia. Integração com banco e broker reais: publicação num broker sem filas (FR-018), as três etapas, o esgotamento e a concorrência entre instâncias |
| RabbitMQ.Client nativo | ✅ | `mandatory` e `PublishReturnException` do cliente nativo, sem MassTransit |
| Endpoint só roteia | ✅ | Nenhum endpoint muda |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `ReconciliadorPedidos`, `ServicoReconciliacao`, `IEtapaFluxoPedido`, `TopologiaConhecida`, `NaoProcessado` |
| Branch dedicada | ✅ | `feature/req-12-publicacao-garantida`, a partir da `main` sincronizada depois do merge do PR #25 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/012-publicacao-garantida/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/publicacao-e-reconciliacao.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   ├── Pagamentos/SituacaoPagamento.cs              # NaoProcessado
│   ├── Pagamentos/Pagamento.cs                      # RegistrarResultado aceita NaoProcessado
│   └── Pedidos/Pedido.cs                            # RegistrarPagamentoNaoProcessado
├── PedidoFulfillment.Application/
│   ├── Estoque/ReservaEstoque.cs                    # IEtapaFluxoPedido (AplicarAsync)
│   ├── Pagamentos/ProcessamentoPagamento.cs         # IEtapaFluxoPedido
│   ├── Entregas/CriacaoOrdemEntrega.cs              # IEtapaFluxoPedido
│   └── Reconciliacao/                               # NOVO
│       ├── EtapaFluxoPedido.cs, IEtapaFluxoPedido.cs
│       ├── PedidoParado.cs, IConsultaPedidosParados.cs
│       ├── RegistroFalhasReconciliacao.cs
│       ├── ServicoReconciliacao.cs                  # decide por pedido; motivos de esgotamento
│       └── EventosReconciliacao.cs                  # logs 9001-9007
├── PedidoFulfillment.Infrastructure/
│   ├── Configuracao/OpcoesReconciliacao.cs          # NOVO, com validador
│   ├── Mensageria/
│   │   ├── DescritorConsumidor.cs                   # NOVO: IDescritorConsumidor
│   │   ├── TopologiaConhecida.cs                    # NOVO
│   │   ├── DespachanteEventosSaida.cs               # declara a topologia; mandatory; SemRota
│   │   └── ExtensoesMensageria.cs                   # descritor e IEtapaFluxoPedido no AdicionarConsumidor
│   ├── Reconciliacao/                               # NOVO
│   │   ├── ConsultaPedidosParados.cs
│   │   ├── ReconciliadorPedidos.cs                  # BackgroundService, sp_getapplock
│   │   └── ExtensoesReconciliacao.cs                # AdicionarReconciliacao
│   └── Persistencia/Migracoes/*_PagamentoNaoProcessado.cs
└── PedidoFulfillment.Worker/
    ├── ProgramaWorker.cs                            # AdicionarReconciliacao
    └── appsettings.json                             # seção Reconciliacao

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoPagamentoNaoProcessadoTestes.cs           # NOVO
│   ├── Reconciliacao/ServicoReconciliacaoTestes.cs             # NOVO, com regras falsas
│   ├── Reconciliacao/RegistroFalhasReconciliacaoTestes.cs      # NOVO
│   ├── Configuracao/OpcoesReconciliacaoTestes.cs               # NOVO
│   └── Mensageria/TopologiaConhecidaTestes.cs                  # NOVO
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaWorker.cs                         # Reconciliacao:Habilitada=false por padrão
    ├── Mensageria/PublicacaoSemFilaTestes.cs                   # NOVO (FR-018)
    └── Reconciliacao/                                          # NOVO
        ├── CenarioReconciliacao.cs
        ├── ReconciliacaoEtapasTestes.cs                        # três etapas, recente intocado, predicados = domínio
        ├── EsgotamentoTestes.cs                                # gateway que sempre falha
        ├── ReconciliacaoConcorrenteTestes.cs                   # dois Workers, um efeito; lock ocupado
        └── LogsReconciliacaoTestes.cs                          # logs 9001-9007 (US3)
```

**Structure Decision**: nenhum projeto novo. A decisão por pedido fica no Application
(`ServicoReconciliacao`, testável com falsos). O que depende do banco, do lock e do host
fica no Infrastructure. O Worker só registra.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Predicado SQL de "parado" se afasta do domínio (`AptoParaPagamento` e outros) | A regra relê o pedido e decide pelo domínio. Um teste de integração compara a seleção SQL com as propriedades do domínio para cada combinação de estado (R-05) |
| Testes existentes semeiam pedidos com datas fixas, e a reconciliação os altera | `FabricaWorker` com `Reconciliacao:Habilitada=false` por padrão; só os testes da reconciliação a ligam (R-10) |
| Fila de consumidor removido nos testes passa a ser declarada e acumula eventos | Os testes já limpam as filas que leem; o descritor fica, como em produção (R-01) |
| `PRECONDITION_FAILED` se a fila declarada pelo despachante diferir da do consumidor | Mesma função `DeclararConsumidorAsync` e mesmas opções; teste de integração com o Worker completo |
| Despachante recria a fila apagada, mas o consumidor perdeu a inscrição | A mensagem fica durável na fila até o consumidor voltar a ler; o quickstart orienta reiniciar o Worker. A reinscrição automática fica fora do escopo |
| Lote tomado por pedidos que falham sempre | Os pedidos com falha anterior vão para o fim do lote (R-05); teste do SC-007 |
| Gateway chamado duas vezes por instâncias concorrentes | `sp_getapplock` por ciclo (R-08). A disputa com o consumidor continua possível, mas é protegida pela concorrência otimista; a idempotência da cobrança num gateway real segue pendente (spec, Assumptions) |
| Volume de pedidos não finais cresce, e a consulta fica lenta | Usa `IX_Pedidos_Status`; lote limitado. Índice composto só se a medição pedir |
| Esgotamento cancela um pedido que acabou de avançar | `DescartarAlteracoes`, releitura e conferência da etapa antes de cancelar, com concorrência otimista e reavaliações (R-07) |
