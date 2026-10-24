# Contrato: cancelamento com estorno e cancelamento da ordem de entrega (REQ-13)

**Spec**: [spec.md](../spec.md) | **Research**: [research.md](../research.md)

## HTTP: `PATCH /api/v1/pedidos/{id}/status` com `Cancelado`

Requisição, permissões e respostas **inalteradas** (clarify Q3):

| Quem | De | Resposta |
|------|----|----------|
| Admin, OperadorLogistica | `Pendente`, `Processando`, `ProntoParaEnvio` | 200 com o pedido `Cancelado` |
| Cliente dono | `Pendente` | 200 |
| Cliente dono | outro status | 403 "O cliente só pode cancelar o próprio pedido enquanto ele está Pendente." |
| Qualquer um | `Despachado`, `Cancelado`, `PagamentoRejeitado` | 409 `PEDIDO_TRANSICAO_INVALIDA` |
| Conflitos persistentes | — | 409 "O pedido foi alterado por outra operação; tente novamente." |

O que muda é o efeito, na mesma gravação: estoque devolvido (como hoje), pagamento aprovado
passa a estorno pendente (interno) e ordem de entrega sem despacho cancelada. O gateway
**não** é chamado na requisição.

## HTTP: `GET /api/v1/pedidos/{id}`

`entrega` ganha `canceladaEm`, opcional e anulável. Mudança compatível no `openapi-v1.yaml`
(oasdiff: propriedade nova e opcional numa resposta).

```yaml
EntregaPedidoResponseDTO:
  type: object
  required: [criadoEm]
  properties:
    # transportadora, codigoRastreio, despachadoEm, criadoEm: como hoje
    canceladaEm:
      type: ['null', string]
      format: date-time
      description: Instante do cancelamento da ordem, junto com o pedido; null se não cancelada.
```

Exemplo de pedido cancelado em `ProntoParaEnvio`:

```json
{ "status": "Cancelado",
  "entrega": { "transportadora": null, "codigoRastreio": null, "despachadoEm": null,
               "criadoEm": "2026-10-24T15:00:00+00:00", "canceladaEm": "2026-10-24T15:20:00+00:00" } }
```

A situação do pagamento e do estorno **não** aparece (clarify Q2).

## Eventos

Nenhum evento novo e nenhum contrato v1 alterado (FR-017). O cancelamento e o estorno não
publicam nada; isso é a issue #29.

## Porta do gateway (`IGatewayPagamento`, Application)

```text
Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken)
Task<ResultadoEstorno>  EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken)

ResultadoEstorno(bool Confirmado, string? IdentificadorEstorno, string? Motivo)
  Confirmado(identificadorEstorno) | Recusado(motivo)
```

- `pedidoId` é a **chave de idempotência** das duas operações: repetir a chamada com o
  mesmo pedido devolve o mesmo resultado, sem novo efeito (FR-013, FR-014).
- Exceção (tempo esgotado, indisponível) é falha técnica: o chamador tenta de novo.
- Simulado: guarda os resultados em memória por pedido; o estorno sempre confirma, com
  identificador `SIM-EST-<guid>`.

## Processamento dos estornos (Worker)

| Item | Valor |
|------|-------|
| Serviço | `ProcessadorEstornos`, primeira rodada na partida |
| Exclusão entre instâncias | `sp_getapplock 'oms-estornos'`; quem não consegue pula a rodada (log 7011) |
| Seleção | Pagamentos `EstornoPendente`; sem falha antes, depois os que já falharam; mais antigos primeiro; até `TamanhoLote` |
| Confirmação | Pagamento `Estornado`, com identificador e instante (log 7006) |
| Falha ou recusa | `TentativasEstorno + 1` (log 7007); com mais de `LimiteAlertaSegundos` desde a solicitação e pelo menos `TentativasAlerta` tentativas, log 7008 (Error). Nunca desiste |

## Reconciliação (alteração do REQ-12)

| Etapa | Esgotamento |
|-------|-------------|
| Estoque | Como no REQ-12 |
| Pagamento | Como no REQ-12 |
| Entrega | **Novo**: 3 ciclos com falha e mais de 24 h parado → `Cancelado`, sem autor, `Não foi possível criar a ordem de entrega.`, estoque devolvido, estorno pendente, sem evento (log 9002 e 7005) |

## Configuração

Seção `Estornos`, validada na partida; fora da faixa, o Worker não sobe e a mensagem cita a
chave (por exemplo, "Estornos:TamanhoLote deve estar entre 1 e 1000.").

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Habilitado` | `true` | — |
| `IntervaloSegundos` | 60 | 1 a 3 600 |
| `TamanhoLote` | 100 | 1 a 1 000 |
| `LimiteAlertaSegundos` | 86 400 | 1 a 2 592 000 |
| `TentativasAlerta` | 3 | 1 a 100 |

## Logs

| Id | Nome | Nível |
|----|------|-------|
| 7005 | `EstornoSolicitado` | Information |
| 7006 | `EstornoConfirmado` | Information |
| 7007 | `FalhaEstorno` | Warning |
| 7008 | `EstornoPendenteAlemDoLimite` | Error |
| 7009 | `CobrancaAprovadaParaPedidoCancelado` | Warning |
| 7010 | `FalhaRodadaEstornos` | Warning |
| 7011 | `RodadaEstornosIgnorada` | Debug |
| 7012 | `ResumoEstornos` | Information ou Debug |
| 8004 | `OrdemEntregaCancelada` | Information |

Os campos de cada log estão no [research R-11](../research.md#r-11-logs). Nenhum traz
identificador de transação ou de estorno, nem dado pessoal.
