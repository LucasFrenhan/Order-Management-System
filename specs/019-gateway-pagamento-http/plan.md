# Implementation Plan: Gateway de pagamento por HTTP com API simulada

**Branch**: `feature/req-19-gateway-pagamento-http` | **Date**: 2026-11-02 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/019-gateway-pagamento-http/spec.md`

**Requisito**: REQ-19 (GitHub issue #46)

## Summary

- **API simulada própria**: projeto novo `PedidoFulfillment.PagamentosSimulados`, sem referência
  ao OMS, no compose como `pagamentos` (porta 5232). Cobra, estorna e consulta pela chave do
  pedido, com idempotência em memória, latência sorteada com cauda lenta, falhas sorteadas
  (`503`, `429` + `Retry-After`, `500`, sem resposta, processar sem responder) e rota de
  controle com chave própria para mudar as chances, forçar um modo ou enfileirar um roteiro
  (research R-01 a R-05).
- **Cliente HTTP com resiliência**: `GatewayPagamentoHttp` sobre `IHttpClientFactory` com
  `Microsoft.Extensions.Http.Resilience`: tempo total 10 s → até 3 tentativas com espera
  exponencial e jitter, respeitando `Retry-After` → circuito (50% em 30 s, mínimo 6, pausa
  30 s) → 3 s por tentativa (R-06).
- **Classificação** em resultado, passageira, incerto e permanente; nada é gravado sem
  resultado, e a repetição com a mesma chave resolve o resultado incerto (R-07). Pedido
  cancelado sem pagamento consulta a cobrança pela chave antes de ignorar (R-08).
- **Circuito aberto pausa o consumo de pagamento**: `DependenciaIndisponivelException` faz a
  base de consumo devolver a mensagem sem contar tentativa e o supervisor do REQ-17 pausar a
  inscrição até a retomada; estornos ficam `Adiado`; reconciliação conta falha (R-09).
- **Chamada dentro da transação**, limitada pelo tempo total (R-10). Gateway em memória só nos
  testes (R-11). Span de cliente HTTP nativo, métricas `oms.pagamentos.*`, logs 7013–7020 e
  5020–5021 (R-12).
- Um pacote novo (`Microsoft.Extensions.Http.Resilience`), nenhuma migration, nenhum contrato
  v1 alterado.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK do `global.json`)

**Primary Dependencies**:
- as que já existem: ASP.NET Core 10 (Minimal APIs, `IHttpClientFactory`), EF Core SqlServer
  10.0.13, `RabbitMQ.Client` 7.2.2, OpenTelemetry 1.19;
- da biblioteca padrão: fonte `System.Net.Http` (spans e métricas nativos de cliente HTTP),
  `System.Security.Cryptography.CryptographicOperations`, `System.Collections.Concurrent`;
- **novo**: `Microsoft.Extensions.Http.Resilience` 10.x (versão fixada na implementação, com
  pelo menos duas semanas de publicada), justificado em R-06.

**Storage**: SQL Server sem mudanças (nenhuma migration). A API simulada guarda as operações em
memória (clarify; [data-model](./data-model.md)).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (API, Worker e, novo, API
simulada com `UseKestrel` em porta local), Testcontainers MsSql e RabbitMq.

**Target Platform**: API, Worker e API simulada em .NET 10; SQL Server 2022 e RabbitMQ 4.3.6 em
contêiner; API simulada em contêiner no compose.

**Project Type**: web-service, com o processo de workers do REQ-06 e um serviço simulado novo

**Performance Goals**:
- com as chances padrão, 95% dos pedidos do criado ao "entrega criada" em até 10 s (SC-006):
  latência típica 40–250 ms, e a cauda de 5% (1–5 s) passa do tempo da tentativa (3 s) só em
  parte, custando no máximo uma tentativa a mais;
- uma falha passageira isolada custa a espera base (≈ 300 ms com jitter) e não chega à base
  de consumo (SC-003);
- com o circuito aberto, zero chamadas à API até a pausa terminar (SC-004).

**Constraints**:
- nunca cobrar ou estornar duas vezes: a mesma chave em toda tentativa, reentrega,
  reconciliação e reinício (FR-017);
- nada gravado sem resultado (FR-013, FR-014);
- conexão do banco ocupada por no máximo o tempo total da chamada (FR-019, R-10);
- chaves fora de logs e traces (FR-002, FR-021);
- contratos v1 (HTTP e eventos) inalterados (FR-023).

**Scale/Scope**:
- 1 projeto novo (API simulada: ~10 rotas, armazenamento, comportamento, opções, Dockerfile);
- Application: 3 exceções, `ConsultaCobranca`, porta estendida, `ProcessamentoPagamento`
  (FR-015) e `ServicoEstornos` (`Adiado`);
- Infrastructure: `GatewayPagamentoHttp`, pipeline de resiliência, classificação,
  `EstadoGatewayPagamento`, opções, logs, telemetria, pausa na base de consumo e no supervisor;
- testes: unitários novos, integração com a API simulada real, migração do gateway em memória
  para os testes;
- compose, CI (demo), README, documentos de fluxo (14 novo; 5, 8, 9, 10, 12).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Issue #46, specify (`202fe5a`) e clarify (`96eb314`) `[REQ-19]`; o plano não diverge da spec |
| II. Contratos versionados | ✅ | `openapi-v1.yaml` e eventos v1 inalterados; o identificador de transação muda de formato (`SIM-…` → `PAG-…`), mas o contrato o define como texto opaco. O contrato da API simulada é novo e documentado ([contracts/api-pagamentos-simulada.md](./contracts/api-pagamentos-simulada.md)) |
| III. Segurança por padrão | ✅ | A API do OMS não ganha rotas. A API simulada exige chave em todas as rotas, menos `/health`, com chave de controle separada, comparação em tempo constante e `401` uniforme; chaves validadas na partida e fora de logs e traces; `url.full` com query ocultada |
| IV. Desacoplamento assíncrono | ✅ | A chamada continua no consumidor e nas rotinas do Worker, nunca na rota da API; o resultado sai pelo outbox como hoje; com a API simulada fora do ar, a API do OMS e os demais consumidores seguem funcionando (pausa só do consumo de pagamento) |
| V. Qualidade verificável | ✅ | Unitários da classificação, das opções, do comportamento simulado, do armazenamento idempotente e da pausa; integração com a API simulada real, banco e broker, conferindo `efetivacoes ≤ 1` (FR-024) |
| RabbitMQ.Client nativo | ✅ | A pausa usa `nack` com `requeue` e o supervisor do REQ-17; nenhum framework |
| Endpoint só roteia | ✅ | Sem endpoints novos no OMS. Na API simulada, as rotas delegam ao `ServicoPagamentosSimulados` e ao `ControleSimulacao` |
| Dependências justificadas | ✅ | `Microsoft.Extensions.Http.Resilience`, justificado em R-06 com as alternativas |
| Nomes em português | ✅ | `PagamentosSimulados`, `GatewayPagamentoHttp`, `FalhaGatewayPagamentoException`, `DependenciaIndisponivelException`, `ConsultaCobranca`; rotas da API simulada em português (`/v1/cobrancas`, `/v1/estornos`, `/controle`) |
| Branch dedicada | ✅ | `feature/req-19-gateway-pagamento-http`, criada da `main` sincronizada depois do merge do PR #45 |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/019-gateway-pagamento-http/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/
│   ├── api-pagamentos-simulada.md        # rotas, autenticação, controle, configuração
│   └── cliente-gateway-pagamento.md      # classificação, tentativas, circuito, pausa, logs
├── checklists/requirements.md
└── tasks.md                              # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.PagamentosSimulados/              # NOVO, sem referência ao OMS
│   ├── ProgramaPagamentos.cs                           # Main explícito; rotas /v1, /controle, /health
│   ├── Seguranca/AutenticacaoChaves.cs                 # X-Api-Key e X-Chave-Controle, tempo constante
│   ├── Operacoes/OperacaoPagamento.cs, ArmazenamentoOperacoes.cs   # memória idempotente
│   ├── Operacoes/ServicoPagamentosSimulados.cs         # cobrança, estorno, consulta
│   ├── Simulacao/Comportamento.cs, DecisorComportamento.cs          # roteiro → modo → sorteio
│   ├── Simulacao/ControleSimulacao.cs, OpcoesSimulacao.cs, OpcoesSeguranca.cs
│   ├── Logs.cs                                          # 11001–11006
│   ├── appsettings*.json
│   └── Dockerfile
├── PedidoFulfillment.Application/
│   ├── Mensageria/DependenciaIndisponivelException.cs   # NOVO
│   └── Pagamentos/
│       ├── IGatewayPagamento.cs                         # + ConsultarCobrancaAsync, ConsultaCobranca
│       ├── FalhaGatewayPagamentoException.cs, GatewayPagamentoIndisponivelException.cs   # NOVOS
│       ├── ProcessamentoPagamento.cs                    # FR-015
│       └── ServicoEstornos.cs                           # Adiado
├── PedidoFulfillment.Infrastructure/
│   ├── Pagamentos/
│   │   ├── GatewayPagamentoHttp.cs                      # NOVO: requisições, leitura, classificação
│   │   ├── ClassificacaoRespostaGateway.cs              # NOVO: tabela do R-07
│   │   ├── EstadoGatewayPagamento.cs                    # NOVO: circuito para métrica e exceção
│   │   ├── MarcadorTentativaHandler.cs                  # NOVO: oms.pagamento.tentativa
│   │   ├── EventosGatewayPagamento.cs                   # NOVO: 7013–7020
│   │   ├── ExtensoesPagamento.cs                        # AdicionarGatewayPagamento + resiliência
│   │   ├── ProcessadorEstornos.cs                       # encerra a rodada no Adiado
│   │   └── GatewayPagamentoSimulado.cs                  # REMOVIDO (vai para os testes)
│   ├── Configuracao/OpcoesGatewayPagamento.cs           # nova seção, validador
│   ├── Mensageria/ConsumidorEventos.cs, SupervisorInscricao.cs, SessaoConsumo.cs, EstadoConsumidores.cs   # pausa
│   ├── Saude/VerificacaoConsumidores.cs                 # pausado = saudável
│   └── Observabilidade/ExtensoesObservabilidade.cs, TelemetriaOms.cs   # System.Net.Http, métricas
└── PedidoFulfillment.Worker/ProgramaWorker.cs, appsettings*.json      # AdicionarGatewayPagamento

Directory.Packages.props                                # Microsoft.Extensions.Http.Resilience
PedidoFulfillment.sln                                   # projeto novo
docker-compose.yml, .env.example                        # serviço pagamentos; Worker com UrlBase e ChaveApi
.github/workflows/build-testes.yml                      # demo confere a prontidão da API simulada

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Pagamentos/ClassificacaoRespostaGatewayTestes.cs, OpcoesGatewayPagamentoTestes.cs
│   ├── PagamentosSimulados/                             # NOVO: decisor, armazenamento, opções, autenticação
│   └── Mensageria/SupervisorInscricaoTestes.cs          # pausa
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaPagamentos.cs              # NOVO: API simulada em Kestrel + cliente de controle
    ├── Infraestrutura/GatewayPagamentoMemoria.cs        # NOVO: o antigo simulado, só para testes
    ├── Infraestrutura/FabricaWorker.cs                  # gateway em memória por padrão; ApiPagamentos opcional
    ├── PagamentosHttp/                                   # NOVO: cenários do FR-024
    └── PedidoFulfillment.Testes.Integracao.csproj       # referência ao projeto da API simulada

docs/fluxos/                                            # 14 novo; 05, 08, 09, 10, 12 e README (Polish)
README.md, docs/proximos-passos.md                      # ambiente local e fila
```

**Structure Decision**: um projeto novo, separado, porque a API simulada representa um serviço
externo e não pode depender do OMS (R-01). No OMS, as exceções e a porta ficam no Application
(os chamadores reagem a elas), e o HTTP, a resiliência e a pausa ficam no Infrastructure, onde
já estão a base de consumo e o supervisor. O domínio não muda.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Cobrança duplicada por repetição | Mesma `Idempotency-Key` em toda tentativa; API com `TryAdd` atômico e `409 operacao-em-andamento` na concorrência; testes conferem `efetivacoes ≤ 1` em todos os cenários (SC-002) |
| Tentativas da chamada × novas tentativas da base × reconciliação multiplicando chamadas | Tempo total de 10 s por chamada; a base de consumo continua com 3 novas tentativas (1, 2, 4 s); pior caso por mensagem ≈ 4 × 10 s + 7 s antes da DLQ, documentado no fluxo 14; circuito corta as chamadas numa queda |
| Conexão do banco presa durante a chamada | Chamada limitada pelo tempo total (R-10); só leituras antes da chamada; prefetch do consumidor limita as conexões |
| Pausa confundida com perda de inscrição (prontidão, métrica de reinscrição do REQ-17) | Motivo `pausa` separado no supervisor; `EstadoConsumidores` com a situação pausado, saudável na prontidão; a reinscrição depois da pausa não conta em `oms.mensageria.consumidor.reinscricoes` |
| Circuito por instância com dois Workers | Aceito e documentado: cada instância pausa por conta própria; nenhuma regra depende de um circuito global |
| Retomada sem chamada de teste (consumo pausado = nenhuma chamada) | A retomada acontece em `DisponivelEm`, e a primeira mensagem é a chamada de teste do meio-aberto |
| Testes lentos com latência e tempos reais | API simulada determinística nos testes, roteiro por chamada e tempos curtos (R-13) |
| Testes existentes dependentes do gateway em memória e da chave `TaxaAprovacaoPercentual` | `GatewayPagamentoMemoria` nos testes, registrado por padrão na `FabricaWorker`, com a chave renomeada para o escopo de teste (R-11) |
| Span de saída também para os webhooks do REQ-18 | Acréscimo aceito: `url.full` com query ocultada pelo .NET; sem cabeçalhos nem corpo; registrado no fluxo 12 |
| `sem-resposta` segurando conexões da API simulada | `SemRespostaMs` limitado (padrão 15 s); o OMS abandona em 3 s; a API cancela a espera quando o cliente desconecta |
| API simulada reiniciada perde as chaves | Limitação aceita no clarify; o OMS não cobra de novo pedido já pago (idempotência pelo estado); registrada no fluxo 14 |
| Documentos de fluxo desatualizados | Tarefa de Polish: fluxo 14 novo e fluxos 5, 8, 9, 10, 12 e o índice (R-14), conforme o `CLAUDE.md` |
