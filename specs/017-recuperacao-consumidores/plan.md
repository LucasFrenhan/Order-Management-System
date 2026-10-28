# Implementation Plan: Recuperação dos consumidores depois de o broker reiniciar

**Branch**: `feature/req-17-recuperacao-consumidores` | **Date**: 2026-10-28 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/017-recuperacao-consumidores/spec.md`

**Requisito**: REQ-17 (GitHub issue #41)

## Summary

- **Causa** (FR-001, R-01): com a recuperação automática do RabbitMQ.Client ligada, a conexão
  compartilhada fica com `IsOpen = false` durante a recuperação; o despachante (a cada 1 s), o
  `MonitorDlq` ou o reenvio pedem a conexão, e `ConexaoRabbitMQ.ObterAsync` a descarta e cria
  outra. Os canais de consumo eram da conexão descartada, e o `ConsumidorEventos` só se
  inscreve na partida. A confirmação com o broker real é a primeira tarefa da implementação
  (o Docker não estava disponível no plano).
- **Correção** (FR-002 a FR-007, R-02 a R-04): o processo passa a ser dono da recuperação.
  `AutomaticRecoveryEnabled = false`; cada consumidor roda um `SupervisorInscricao` que abre
  uma `SessaoConsumo` (canais, topologia, `basic.consume`), espera o sinal de perda (canal
  fechado ou inscrição cancelada pelo broker), fecha a sessão e abre outra, com intervalos de
  0, 1, 2, 4, 8 e 10 s. O descarte de conexão fechada em `ObterAsync` passa a ser correto, e o
  despachante, o `MonitorDlq` e o reenvio não mudam.
- **Garantias** (FR-008 a FR-010, R-05): ack e republicação usam os canais da sessão que
  entregou; ack em canal fechado não relança, e a reentrega é descartada como duplicada.
- **Prontidão** (FR-013, FR-014, R-06): `EstadoConsumidores` (singleton) e a verificação
  `consumidores`, só no Worker, não pronta na hora em que algum consumidor perde a inscrição.
- **Logs e métricas** (FR-015 a FR-016a, R-07, R-08): logs 5016 a 5019;
  `oms.mensageria.consumidor.inscrito` e `oms.mensageria.consumidor.reinscricoes`.
- **Testes** (FR-017, FR-018, R-09, R-10): o `MensageriaTeste` liga o AMQP a uma porta fixa do
  host para reiniciar o contêiner sem trocar a porta; testes de reinício, broker sem dados,
  conexão fechada à força, fila apagada, encerramento e componentes depois da queda; testes
  unitários do supervisor e da verificação.
- **Documentação** (FR-019): fluxos 08 e 12, e o README.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.403)

**Primary Dependencies**: as que já existem: `RabbitMQ.Client` 7.2.2 (`ChannelShutdownAsync`,
`AsyncEventingBasicConsumer.UnregisteredAsync`, `ConnectionShutdownAsync`),
`Microsoft.Extensions.Diagnostics.HealthChecks`, `System.Diagnostics.Metrics`. **Nenhuma
dependência nova** (R-10: a espera injetada dispensa `Microsoft.Extensions.TimeProvider.Testing`).

**Storage**: SQL Server, sem mudança de esquema (nenhuma migration).

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory (`FabricaWorker`), Testcontainers
MsSql e RabbitMq (com porta fixa no host), `ColetorTelemetria` do REQ-16 para logs e métricas.

**Target Platform**: Worker em .NET 10; RabbitMQ 4.3.6 e SQL Server 2022 em contêiner.

**Project Type**: web-service, com o processo de workers do REQ-06.

**Performance Goals**:
- consumo de volta em até 1 min depois de o broker aceitar conexões (FR-003, SC-001); no pior
  caso, 10 s de espera mais 5 s de prazo de conexão;
- sem custo no caminho feliz: o supervisor só age no sinal de perda; a prontidão lê memória
  (SC-006).

**Constraints**:
- envelope v1, schemas, trocas, filas e argumentos de fila inalterados (Princípio II);
- RabbitMQ.Client nativo, sem MassTransit (constituição);
- IDs de log existentes inalterados (o 5008 continua na partida);
- prontidão sem dados de conexão (REQ-02);
- no máximo um consumidor por fila por processo (FR-006);
- a suíte atual passa, com um ajuste: `Worker_fica_pronto_e_declara_a_troca_de_eventos` espera a
  prontidão em vez de ler uma vez (R-06).

**Scale/Scope**:
- 4 tipos novos na Infrastructure (`SupervisorInscricao`, `SessaoConsumo`, `EstadoConsumidores`,
  `VerificacaoConsumidores`);
- ajustes em `ConexaoRabbitMQ`, `ConsumidorEventos`, `ExtensoesMensageria`, `TelemetriaOms` e
  `EstadoMetricas`;
- `MensageriaTeste` (porta fixa e três métodos de queda), 1 classe de testes de integração nova e
  acréscimos em `MetricasTestes`; 2 classes de testes unitários;
- fluxos 08 e 12 e README.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / seção | Como o plano atende | Situação |
|-------------------|---------------------|----------|
| I. Specification-First | Spec com clarify (3 perguntas); a causa é confirmada antes da correção (T001 esperado); divergência entre análise e observação volta ao plano | ✅ |
| II. Contratos imutáveis | Nenhum evento, schema, fila, troca ou rota muda; métricas e logs são acréscimos; a prontidão ganha uma entrada (endereço fora do contrato v1, REQ-02) | ✅ |
| III. Segurança por padrão | Prontidão e logs sem credenciais nem cadeia de conexão (FR-013, FR-016); nenhuma rota nova | ✅ |
| IV. Desacoplamento assíncrono | O consumo resiste à queda do broker sem intervenção; outbox e "pelo menos uma vez" preservados | ✅ |
| V. Qualidade verificável | Testes de integração com broker real que reinicia o contêiner (não só pausa), unitários do supervisor e da verificação | ✅ |
| Restrições técnicas | RabbitMQ.Client nativo; nenhuma dependência NuGet nova | ✅ |
| Convenções | Nomes em português; commits `tipo(escopo): descrição [REQ-17]` | ✅ |

**Re-check pós-design**: sem violações. A desativação da recuperação automática substitui uma
decisão de implementação do REQ-06 (R-09), não um princípio; está registrada em R-02.

## Project Structure

### Documentation (this feature)

```text
specs/017-recuperacao-consumidores/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── recuperacao-consumo.md
├── checklists/
│   └── requirements.md
└── tasks.md              # /speckit-tasks
```

### Source Code (repository root)

```text
src/PedidoFulfillment.Infrastructure/
├── Mensageria/
│   ├── ConexaoRabbitMQ.cs          # recuperação automática desligada; 5019 no fechamento não pedido
│   ├── ConsumidorEventos.cs        # usa o supervisor; ack e republicação pela sessão que entregou
│   ├── SupervisorInscricao.cs      # novo: laço abrir → esperar perda → fechar → intervalo (5016–5018)
│   ├── SessaoConsumo.cs            # novo: canais, inscrição e a Task de perda
│   ├── EstadoConsumidores.cs       # novo: inscrito/sem inscrição por consumidor
│   └── ExtensoesMensageria.cs      # registra o estado e a verificação "consumidores"
├── Saude/
│   └── VerificacaoConsumidores.cs  # novo: Healthy só com todos inscritos
└── Observabilidade/
    ├── TelemetriaOms.cs            # métricas 7 e 8
    └── EstadoMetricas.cs           # espelho das inscrições para o gauge

tests/PedidoFulfillment.Testes.Unitarios/
├── Mensageria/SupervisorInscricaoTestes.cs
└── Mensageria/VerificacaoConsumidoresTestes.cs

tests/PedidoFulfillment.Testes.Integracao/
├── Infraestrutura/MensageriaTeste.cs        # porta fixa; ReiniciarBrokerAsync, RecriarBrokerAsync, FecharConexoesAsync
├── Mensageria/RecuperacaoConsumoTestes.cs   # novo: reinício, sem dados, conexão forçada, fila apagada, encerramento, efeito único, despachante/monitor/reenvio
├── Mensageria/WorkerTestes.cs               # espera a prontidão
└── Observabilidade/MetricasTestes.cs        # métricas 7 e 8

docs/fluxos/08-publicacao-e-consumo-de-eventos.md   # falhas externas, prontidão, logs, limitação resolvida (#41)
docs/fluxos/12-rastreabilidade-e-metricas.md         # métricas 7 e 8
README.md                                            # linha do REQ-17
```

**Structure Decision**: mantém a divisão atual. Tudo fica na Infrastructure (mensageria, saúde e
observabilidade), que o Worker já usa; o Application e o Domain não mudam, e a API não muda.

## Ordem sugerida da implementação

1. **Confirmação da causa** (FR-001): fixture com porta fixa e `ReiniciarBrokerAsync`; teste de
   reinício escrito antes da correção, que falha na `main`; logs temporários dos eventos da
   conexão; registrar o observado em `research.md` (R-01, "Observado").
2. **Estado e supervisor** (US1, US2): `EstadoConsumidores`, `SessaoConsumo`,
   `SupervisorInscricao` com testes unitários; `ConsumidorEventos` passa a usá-los;
   `ConexaoRabbitMQ` sem recuperação automática.
3. **Casos de perda sem queda** (US2): fila apagada, conexão fechada à força, broker sem dados,
   encerramento.
4. **Prontidão, logs e métricas** (US3).
5. **Polish**: fluxos 08 e 12, README, quickstart, `dotnet format`, CI verde.

## Complexity Tracking

Sem violações da constituição a justificar.
