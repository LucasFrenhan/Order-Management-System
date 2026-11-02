# Contrato: cliente do gateway de pagamento no OMS

**Requisito**: REQ-19 | **Onde**: Worker (consumidor `pagamento`, reconciliação, `ProcessadorEstornos`) | **Decisões**: [research](../research.md) R-06 a R-12

Como o OMS chama a [API de pagamento simulada](./api-pagamentos-simulada.md), classifica cada
resposta e reage. Os contratos HTTP v1 da API do OMS e os eventos v1 não mudam (FR-023).

## Porta (`Application/Pagamentos/IGatewayPagamento`)

```csharp
Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken ct);
Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken ct);
Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken ct);   // NOVO (FR-015)
```

- Devolve só **resultados** (aprovação, recusa, confirmação, "não encontrada").
- Lança `FalhaGatewayPagamentoException` (`Tipo`: `Passageira` ou `Permanente`) quando não há
  resultado, e `GatewayPagamentoIndisponivelException` (`DisponivelEm`) com o circuito aberto.
  As duas são falha técnica para os chamadores de hoje: nada é gravado.

## Requisição

| Operação | Requisição | Chave |
|----------|------------|-------|
| Cobrança | `POST {UrlBase}/v1/cobrancas`, `{ "valor": <valorTotal> }` | `Idempotency-Key: <pedidoId>` |
| Estorno | `POST {UrlBase}/v1/estornos`, `{ "identificadorCobranca": <transacao>, "valor": <valor> }` | `Idempotency-Key: <pedidoId>` |
| Consulta | `GET {UrlBase}/v1/cobrancas/<pedidoId>` | — |

Todas com `X-Api-Key` e `traceparent`. A mesma chave em todas as tentativas, em todas as
entregas da mensagem, na reconciliação e depois de reiniciar o Worker (FR-017).

## Classificação e reação

| Resposta | Classe | Repetir na chamada | Circuito | Exceção/resultado | Quem chamou faz |
|----------|--------|--------------------|----------|-------------------|-----------------|
| `2xx` `aprovada` | resultado | não | sucesso | `ResultadoCobranca.Aprovada(identificador)` | grava como hoje |
| `2xx` `recusada` | resultado | não | sucesso | `ResultadoCobranca.Recusada(motivo)` | `PagamentoRejeitado`, como hoje |
| `2xx` `confirmado` / `recusado` (estorno) | resultado | não | sucesso | `ResultadoEstorno` | como hoje (REQ-13) |
| `GET` `200` / `404` | resultado | não | sucesso | `ConsultaCobranca` | FR-015 |
| `503`, `500`, `502`, `504` | passageira | sim | falha | — | — |
| `429` | passageira | sim, ≥ `Retry-After` | falha | — | — |
| `409 operacao-em-andamento` | passageira | sim, ≥ `Retry-After` | não conta | — | — |
| tempo da tentativa esgotado | incerto | sim, mesma chave | falha | — | — |
| falha de conexão | passageira (incerto se já enviada) | sim | falha | — | — |
| tentativas ou tempo total esgotados | passageira | — | — | `FalhaGatewayPagamentoException(Passageira)` | consumidor: novas tentativas da base e DLQ; reconciliação: falha do ciclo; estorno: falha da rodada (tentativa + 1) |
| `400`, `401`, `403`, `409 chave-reutilizada`, corpo fora do contrato | permanente | não | não conta | `FalhaGatewayPagamentoException(Permanente)` | o mesmo da linha acima, com log 7015 (Error) |
| circuito aberto | — | não chama | — | `GatewayPagamentoIndisponivelException` | consumidor: **pausa** (abaixo); estorno: `Adiado`, sem contar tentativa, fim da rodada; reconciliação: falha do ciclo |

Em nenhum caso sem resultado o OMS grava algo no pedido (FR-013, FR-014).

### Quando a mensagem de pagamento vai para a DLQ

A pausa (abaixo) cobre só o circuito aberto. Continuam seguindo as novas tentativas da base e,
esgotadas, a DLQ:

- **erro permanente** (`400`, `401`/`403`, `409 chave-reutilizada`, corpo fora do contrato): não
  conta para o circuito e **não pausa**. Decisão do usuário: credencial errada vai para a DLQ, e
  não para a pausa. Pausada, ela ficaria escondida com a prontidão em `200`; na DLQ, o monitor do
  REQ-16 alerta, o log 7015 sai como Error, e o reenvio resolve depois da correção (a chave é o
  pedido, sem cobrança dupla);
- **falha passageira que não abre o circuito** (intermitente abaixo de `Circuito:ProporcaoFalhas`
  ou sem `Circuito:MinimoChamadas` na janela), com azar em todas as entregas;
- **falhas fora do gateway**, como hoje: mensagem malformada ou fora do schema, banco
  indisponível, conflito persistente, erro inesperado.

## Pausa do consumo com o circuito aberto (FR-016a)

1. O consumidor de pagamento recebe `GatewayPagamentoIndisponivelException` (herda de
   `DependenciaIndisponivelException`).
2. A base de consumo devolve a mensagem com `nack` e `requeue` (sem `x-tentativas` novo, sem
   DLQ) e encerra a sessão com o motivo `pausa`. Log 5020 `ConsumoPausado`.
3. O supervisor marca o consumidor como pausado (a prontidão continua `200`), espera até
   `DisponivelEm` e abre a sessão de novo. Log 5021 `ConsumoRetomado`.
4. A primeira mensagem depois da pausa é a chamada de teste do circuito: sucesso fecha o
   circuito; falha abre de novo, e o passo 2 se repete.

O circuito é **por instância do Worker** (estado em memória do pipeline de resiliência): com
duas instâncias, cada uma decide a própria pausa.

## Configuração (`GatewayPagamento`)

| Chave | Padrão | Faixa | Nos testes de integração |
|-------|--------|-------|--------------------------|
| `UrlBase` | — (obrigatória) | URL absoluta `http`/`https` | a da API simulada em Kestrel |
| `ChaveApi` | — (obrigatória) | ≥ 16 caracteres | a do teste |
| `TempoTentativaMs` | 3000 | 100–30000 | 300 |
| `Tentativas` | 3 | 1–6 | 3 |
| `EsperaBaseMs` | 300 | 10–10000 | 20 |
| `TempoTotalMs` | 10000 | > `TempoTentativaMs`, ≤ 60000 | 1500 |
| `Circuito:ProporcaoFalhas` | 0.5 | 0.1–1.0 | 0.5 |
| `Circuito:MinimoChamadas` | 6 | 2–100 | 4 |
| `Circuito:JanelaSegundos` | 30 | 1–600 | 5 |
| `Circuito:PausaSegundos` | 30 | 1–600 | 1 |

Validadas na partida (`ValidateOnStart`); um valor inválido derruba a partida, nomeando a
chave sem o valor (FR-018). A `ChaveApi` nunca é logada. A chave
`GatewayPagamento:TaxaAprovacaoPercentual` deixa de existir no Worker (a taxa agora é da API
simulada).

## Logs

| Id | Nome | Nível | Campos |
|----|------|-------|--------|
| 7013 | `FalhaPassageiraGateway` | Warning | pedido, operação, tipo (`indisponivel`, `limite-taxa`, `erro-interno`, `em-andamento`, `conexao`), tentativa |
| 7014 | `ResultadoIncertoGateway` | Warning | pedido, operação, tentativa |
| 7015 | `ErroPermanenteGateway` | Error | pedido, operação, status HTTP, código |
| 7016 | `CircuitoAberto` | Error | pausa (s), disponível em |
| 7017 | `CircuitoMeioAberto` | Information | — |
| 7018 | `CircuitoFechado` | Information | — |
| 7019 | `ConsultaCobranca` | Information | pedido, resultado |
| 7020 | `EstornosAdiados` | Warning | quantidade, disponível em |
| 5020 | `ConsumoPausado` | Warning | consumidor, até quando |
| 5021 | `ConsumoRetomado` | Information | consumidor, duração da pausa (ms) |

Sem chave da API, identificador de transação, corpo ou dados pessoais (FR-021).

## Telemetria

- Span interno `cobrar pagamento` / `estornar pagamento` / `consultar cobranca` (`TelemetriaOms`),
  com `oms.pedido.id`, `oms.pagamento.operacao`, `oms.resultado` e `oms.pagamento.tentativas`.
- Um span de cliente HTTP por tentativa (fonte nativa `System.Net.Http`), filho do span acima,
  com `oms.pagamento.tentativa`; `traceparent` propagado para a API simulada.
- Métricas `oms.pagamentos.chamadas`, `oms.pagamentos.duracao` e `oms.pagamentos.circuito`
  (research R-12), mais o medidor nativo `System.Net.Http`.
