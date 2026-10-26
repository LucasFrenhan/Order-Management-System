# Data Model: rastreabilidade de ponta a ponta do pedido (REQ-16)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Só as mudanças sobre o modelo dos REQ-04 a REQ-15.

## Coluna `ContextoRastreio` (migration `ContextoRastreio`)

| Tabela | Coluna | Tipo | Regra |
|--------|--------|------|-------|
| `EventosSaida` | `ContextoRastreio` | `varchar(55) NULL` | Contexto ativo quando o evento foi gravado |
| `Pedidos` | `ContextoRastreio` | `varchar(55) NULL` | Contexto da requisição que criou o pedido |
| `PedidosReenvioDlq` | `ContextoRastreio` | `varchar(55) NULL` | Contexto da requisição do Admin que registrou o pedido de reenvio |

- **Formato**: W3C `traceparent`, `00-<trace-id 32 hex>-<span-id 16 hex>-<flags 2 hex>`,
  exatamente 55 caracteres. Só é gravado quando `Activity.Current` existe e usa o formato W3C.
- **Propriedade de sombra** do EF Core: as entidades (`EventoSaida`, `Pedido`,
  `PedidoReenvioDlq`) não ganham propriedade; o Domain e o Application não mudam.
- **Quem grava**: `InterceptorContextoRastreio` (`SaveChangesInterceptor`), só nas linhas em
  estado `Added`. Linhas alteradas nunca têm o contexto trocado: o contexto é o da criação.
- **Quem lê**:
  - `DespachanteEventosSaida`, pela entrada do evento lido (`Entry(evento).Property(...)`);
  - `ConsultaPedidosParados` e `ConsultaEstornosPendentes`, por `EF.Property<string?>` na
    projeção;
  - `ExecutorReenviosDlq`, pela entrada do pedido de reenvio.
- **Inválido** (tamanho, versão, caracteres): `ActivityContext.TryParse` falha e o valor é
  ignorado (FR-013).
- **Retroativo**: linhas anteriores ficam nulas. Nenhuma migração de dados.
- **Limpeza** (REQ-14): nada muda; os eventos enviados e os pedidos de reenvio continuam
  apagados pela retenção, com a coluna junto.
- **Índices**: nenhum; a coluna nunca é filtro.

## Registros de leitura das rotinas (Application)

| Registro | Antes | Depois |
|----------|-------|--------|
| `PedidoParado` | `(PedidoId, Etapa, AtualizadoEm)` | `+ ContextoRastreio (string?)` |
| `IConsultaEstornosPendentes.ListarAsync` | lista de `Guid` | lista de `EstornoPendente(PedidoId, ContextoRastreio)` |

O texto é opaco para o Application: só é repassado à Infrastructure para criar o link.

## Mensagem no broker

| Cabeçalho | Quem grava | Conteúdo |
|-----------|------------|----------|
| `traceparent` | RabbitMQ.Client, em todo `BasicPublishAsync` com ouvinte ativo | Contexto do span de publicação (W3C) |

- Convive com `x-tentativas`, `x-ultimo-erro`, `x-motivo`, `x-falhou-em`, `x-reenvios`,
  `x-reenviado-em` e `x-pedido-reenvio`, que não mudam.
- Nas republicações (fila de espera, DLQ, reenvio), o cliente sobrescreve o `traceparent`
  copiado com o do span atual; o reenvio lê o original **antes** de publicar (R-04).
- Com a telemetria desligada, nenhum `traceparent` é gravado; um que venha de fora é ignorado
  sem efeito.
- O corpo (envelope v1) não muda.

## Configuração

### `Observabilidade` (nova; API e Worker)

| Chave | Padrão | Faixa | Uso |
|-------|--------|-------|-----|
| `Habilitada` | `false` | — | Liga o SDK sem exportador (testes). Com `OTEL_EXPORTER_OTLP_ENDPOINT` preenchido, liga sempre |
| `TaxaAmostragem` | `1.0` | 0,0 a 1,0 | Razão da amostragem na origem (FR-022) |
| `IntervaloAmostragemOutboxSegundos` | `30` | 5 a 300 | Leitura dos gauges do outbox (Worker) |

Fora das faixas, o processo não sobe (validador, como as demais opções).

### `ReenvioDlq` (REQ-14; Worker)

| Chave | Padrão | Faixa | Uso |
|-------|--------|-------|-----|
| `MetricaIntervaloSegundos` | `60` | 10 a 300 | Leitura da DLQ para o gauge 3, só com a telemetria ligada |

O `AvisoIntervaloSegundos` (300) passa a ser o intervalo mínimo entre dois logs 5011 do mesmo
consumidor, e não mais o da leitura.

### Variáveis padrão do OpenTelemetry (lidas pelo SDK)

`OTEL_EXPORTER_OTLP_ENDPOINT`, `OTEL_EXPORTER_OTLP_PROTOCOL` (padrão `grpc`),
`OTEL_EXPORTER_OTLP_HEADERS`, `OTEL_SERVICE_NAME` (padrão `oms-api` e `oms-worker`),
`OTEL_RESOURCE_ATTRIBUTES`. Ver [contracts/telemetria.md](./contracts/telemetria.md).
