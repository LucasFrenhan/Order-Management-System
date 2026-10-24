# Implementation Plan: Cancelamento depois do pagamento, com estorno e cancelamento da ordem de entrega

**Branch**: `feature/req-13-cancelamento-com-estorno` | **Date**: 2026-10-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/013-cancelamento-com-estorno/spec.md`

**Requisito**: REQ-13 (GitHub issue #28)

## Summary

- **Cancelamento único no domínio**: `Pedido.Cancelar` devolve o estoque, passa o pagamento
  aprovado para `EstornoPendente`, cancela a ordem de entrega sem despacho e muda o status,
  numa gravação. A API e a reconciliação usam o mesmo método; permissões e respostas HTTP
  não mudam (R-01).
- **Estorno assíncrono**: o gateway não é chamado na requisição. O `ProcessadorEstornos`,
  serviço novo no Worker, roda a cada 1 min, com uma instância por rodada
  (`sp_getapplock 'oms-estornos'`). Ele pede o estorno e grava `Estornado`; numa falha, conta
  a tentativa no pagamento e tenta de novo sem limite, com log de erro depois de 24 h e 3
  tentativas (R-02, R-04).
- **Idempotência no gateway**: `IGatewayPagamento` ganha `EstornarAsync`; o pedido é a chave da
  cobrança e do estorno. O simulado guarda os resultados por pedido (R-03).
- **Ordem de entrega**: `CanceladaEm`, exposto como `entrega.canceladaEm` na consulta (campo
  opcional, mudança compatível do contrato v1) (R-05).
- **Reconciliação**: a etapa de entrega passa a se esgotar (24 h e 3 ciclos), cancelando com
  `Não foi possível criar a ordem de entrega.` e estorno pendente (R-06).
- **Cobrança aprovada para pedido cancelado**: o consumidor de pagamento registra o pagamento
  com estorno pendente em vez de descartar (R-07).
- **Persistência**: migration `EstornoPagamento`, com 5 colunas em `Pagamentos`, 1 em
  `OrdensEntrega`, a check constraint recriada e um índice filtrado (R-08).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- as que já existem: EF Core SqlServer 10.0.13, `Microsoft.Data.SqlClient` (lock de
  aplicação, como no REQ-12) e `RabbitMQ.Client` 7.2.2 (sem mudança);
- nenhum pacote novo.

**Storage**: SQL Server; tabelas `Pedidos`, `Pagamentos`, `OrdensEntrega` e `Produtos`
existentes. Uma migration, `EstornoPagamento` (R-08). Lock de aplicação `sp_getapplock`, sem
tabela.

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API e Worker), Testcontainers
MsSql e RabbitMq

**Target Platform**: API e Worker em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em contêiner

**Project Type**: web-service, com o processo de workers do REQ-06

**Performance Goals**:
- cancelamento pela API no mesmo tempo de hoje, sem chamada externa (SC-006);
- estorno confirmado em até 1 min com os padrões (SC-002);
- fluxo normal em até 10 s (SC-007): nada muda no caminho do pedido criado ao
  `entrega.criada`.

**Constraints**:
- contratos de eventos v1 inalterados; contrato HTTP v1 só com o campo opcional
  `canceladaEm`;
- a API não depende do gateway;
- estorno no máximo uma vez por pedido (FR-005): lock por rodada, concorrência otimista no
  pedido e gateway idempotente;
- o estorno pendente nunca desiste;
- concorrência otimista, sem bloqueio pessimista em pedido.

**Scale/Scope**:
- 4 métodos novos no agregado `Pedido`, 3 no `Pagamento`, 1 na `OrdemEntrega`;
- 1 operação nova na porta do gateway e o simulado com memória;
- 1 serviço de aplicação (`ServicoEstornos`), 1 serviço em segundo plano, 1 porta de
  consulta e 1 seção de opções;
- mudanças localizadas no `ServicoPedidos`, no `ProcessamentoPagamento` e no
  `ServicoReconciliacao`;
- 1 migration e 1 campo no `openapi-v1.yaml`.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | spec após o specify (2 decisões: estorno assíncrono e chave de idempotência) e o clarify (3 perguntas), commits `1b9e3c6` e `75e6d71` `[REQ-13]` |
| II. Contratos versionados | ✅ | Nenhum contrato de evento muda. No HTTP v1, só a propriedade opcional `canceladaEm` na resposta, compatível para o oasdiff (workflow `contrato-compatibilidade`). As situações de estorno são internas |
| III. Segurança por padrão | ✅ | Nenhuma rota nova; as permissões do cancelamento não mudam (clarify Q3). Logs sem identificador de transação, de estorno ou dado pessoal (R-11). Nenhum segredo novo |
| IV. Desacoplamento assíncrono | ✅ | O efeito externo (estorno no gateway) sai da requisição e vai para o Worker, a partir do estado gravado na mesma transação do cancelamento, como um outbox de estado (R-04) |
| V. Qualidade verificável | ✅ | Unitários do domínio, do `ServicoEstornos`, do gateway simulado, das opções e da reconciliação. Integração com banco e broker reais: cancelamento pela API, rodada de estornos, corridas, esgotamento da entrega e conformidade do OpenAPI (R-12) |
| RabbitMQ.Client nativo | ✅ | Mensageria intocada |
| Endpoint só roteia | ✅ | O endpoint de status não muda; a regra fica no `Pedido` e no `ServicoPedidos` |
| Dependências justificadas | ✅ | Nenhuma dependência nova |
| Nomes em português | ✅ | `Cancelar`, `EstornoPendente`, `ProcessadorEstornos`, `ServicoEstornos`, `ResultadoEstorno`, `CanceladaEm` |
| Branch dedicada | ✅ | `feature/req-13-cancelamento-com-estorno`, a partir da `main` sincronizada depois do merge do PR #33 (regra do `CLAUDE.md`) |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/013-cancelamento-com-estorno/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/estorno-e-cancelamento.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/
│   ├── Pagamentos/SituacaoPagamento.cs              # EstornoPendente, Estornado
│   ├── Pagamentos/Pagamento.cs                      # SolicitarEstorno, ConfirmarEstorno, RegistrarFalhaEstorno; campos do estorno
│   ├── Entregas/OrdemEntrega.cs                     # CanceladaEm, Cancelar
│   └── Pedidos/
│       ├── Pedido.cs                                # Cancelar, RegistrarCobrancaAposCancelamento, ConfirmarEstorno, RegistrarFalhaEstorno
│       └── ResultadoCancelamento.cs                 # NOVO
├── PedidoFulfillment.Application/
│   ├── Contratos/Pedidos/EntregaPedidoResponseDTO.cs # CanceladaEm
│   ├── Pedidos/MapeamentoPedido.cs                  # CanceladaEm
│   ├── Pedidos/ServicoPedidos.cs                    # cancelamento por Pedido.Cancelar; logs 7005 e 8004
│   ├── Pagamentos/
│   │   ├── IGatewayPagamento.cs                     # EstornarAsync, ResultadoEstorno; pedido como chave
│   │   ├── ProcessamentoPagamento.cs                # cobrança aprovada para pedido cancelado (R-07)
│   │   ├── EventosPagamento.cs                      # logs 7005-7012
│   │   ├── ServicoEstornos.cs                       # NOVO: decide um estorno pendente
│   │   ├── IConsultaEstornosPendentes.cs            # NOVO
│   │   └── OrigemEstorno.cs                         # NOVO
│   ├── Entregas/EventosEntrega.cs                   # log 8004
│   └── Reconciliacao/ServicoReconciliacao.cs        # entrega esgotável; MotivoOrdemEntregaNaoCriada
├── PedidoFulfillment.Infrastructure/
│   ├── Configuracao/OpcoesEstornos.cs               # NOVO, com validador
│   ├── Pagamentos/
│   │   ├── GatewayPagamentoSimulado.cs              # memória por pedido; EstornarAsync
│   │   ├── ConsultaEstornosPendentes.cs             # NOVO
│   │   ├── ProcessadorEstornos.cs                   # NOVO: BackgroundService, sp_getapplock
│   │   └── ExtensoesPagamento.cs                    # AdicionarProcessamentoEstornos
│   └── Persistencia/
│       ├── Configuracoes/ConfiguracaoPagamento.cs   # colunas, check e índice filtrado
│       ├── Configuracoes/ConfiguracaoOrdemEntrega.cs
│       └── Migracoes/*_EstornoPagamento.cs
├── PedidoFulfillment.API/                           # nada muda além do contrato publicado
└── PedidoFulfillment.Worker/
    ├── ProgramaWorker.cs                            # AdicionarProcessamentoEstornos
    └── appsettings.json                             # seção Estornos

specs/001-contratos-api/contracts/openapi-v1.yaml    # EntregaPedidoResponseDTO.canceladaEm

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/PedidoCancelamentoTestes.cs                     # NOVO
│   ├── Dominio/PagamentoEstornoTestes.cs                       # NOVO
│   ├── Dominio/PedidoOrdemEntregaTestes.cs                     # cancelamento da ordem
│   ├── Pagamentos/ServicoEstornosTestes.cs                     # NOVO, com gateway falso
│   ├── Pagamentos/GatewayPagamentoSimuladoTestes.cs            # idempotência
│   ├── Pagamentos/ProcessamentoPagamentoTestes.cs              # R-07
│   ├── Pedidos/ServicoPedidosStatusTestes.cs                   # cancelamento de pedido pago
│   ├── Reconciliacao/ServicoReconciliacaoTestes.cs             # entrega esgotável
│   └── Configuracao/OpcoesEstornosTestes.cs                    # NOVO
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaWorker.cs                         # Estornos:Habilitado=false por padrão
    ├── Estornos/                                               # NOVO
    │   ├── CenarioEstorno.cs
    │   ├── CancelamentoPedidoPagoTestes.cs                     # API: pago, com ordem, Cliente 403, despachado 409
    │   ├── ProcessamentoEstornosTestes.cs                      # confirmação, falha, alerta, dois Workers
    │   └── DisputaCancelamentoTestes.cs                        # corridas das Edge Cases, R-07
    ├── Reconciliacao/EsgotamentoTestes.cs                      # entrega esgota com estorno
    ├── Persistencia/MigracaoBancoTestes.cs                     # migration nova
    └── Contrato/ConformidadeOpenApiTestes.cs                   # canceladaEm
```

**Structure Decision**: nenhum projeto novo. As regras ficam no domínio (`Pedido`,
`Pagamento`, `OrdemEntrega`) e a decisão por estorno no Application (`ServicoEstornos`,
testável com falsos). O que depende do banco, do lock e do host fica no Infrastructure,
junto do gateway simulado. O Worker só registra.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Estorno feito duas vezes (dois Workers, conflito, reinício entre a confirmação e a gravação) | Lock por rodada; concorrência otimista no pedido (`AtualizadoEm` muda em toda transição); gateway idempotente por pedido, que devolve o mesmo estorno (R-03, R-04). Teste com dois Workers |
| Estorno pendente para sempre sem ninguém ver | Log 7008 (Error) a cada falha depois de 24 h e 3 tentativas; tentativas gravadas no banco, não zeradas por reinício (R-02) |
| Memória do simulado perdida no reinício | Aceito só no simulado (spec, Assumptions); um gateway real guarda as chaves. Documentado no fluxo 5 |
| Mudar `ServicoPedidos` quebra os testes de status do REQ-05 e do REQ-08 | `Pedido.Cancelar` cobre os casos de hoje (sem pagamento, estoque reservado ou não); as respostas não mudam, e os testes existentes continuam como estão |
| Conformidade do OpenAPI com a resposta real | Campo no `openapi-v1.yaml` e no DTO no mesmo commit; `ConformidadeOpenApiTestes` e oasdiff no CI |
| Pedidos antigos cancelados com pagamento aprovado | Fora do escopo, sem conversão automática; consulta no quickstart para a operação decidir (R-10) |
| Reentrega de cobrança aprovada sem resultado em mãos, para pedido cancelado depois | Limitação aceita com o simulado; anotada no fluxo 5 para quando houver gateway real (R-07) |
| Testes existentes semeiam pedidos cancelados com pagamento e o processador os toca | `FabricaWorker` com `Estornos:Habilitado=false` por padrão; só os testes do estorno o ligam (R-09) |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxos 3, 5, 6, 9 e o índice (`docs/fluxos/README.md`), conforme a regra do `CLAUDE.md` |
