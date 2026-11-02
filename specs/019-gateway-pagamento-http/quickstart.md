# Quickstart: gateway de pagamento por HTTP com API simulada

**Requisito**: REQ-19 | **Contratos**: [API simulada](./contracts/api-pagamentos-simulada.md), [cliente no OMS](./contracts/cliente-gateway-pagamento.md)

Roteiro de validação de ponta a ponta. As chaves abaixo são as de desenvolvimento do
`docker-compose.yml` (trocáveis por `OMS_PAGAMENTOS_CHAVE_API` e
`OMS_PAGAMENTOS_CHAVE_CONTROLE`).

```bash
API=http://localhost:5230
PAG=http://localhost:5232
CHAVE_CONTROLE='controle-dev-local-2026'
```

## 1. Subir o ambiente

```bash
docker compose --profile demo up -d --wait --build
docker compose ps        # sqlserver, rabbitmq, pagamentos, api, worker e aspire-dashboard "healthy"
curl -fsS $PAG/health    # 200
```

Esperado: o Worker sobe com `GatewayPagamento:UrlBase` apontando para `http://pagamentos:8080`.
Sem `GatewayPagamento:ChaveApi`, o Worker não sobe e o log nomeia a chave.

## 2. Autenticação da API simulada

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST $PAG/v1/cobrancas \
  -H 'Content-Type: application/json' -H "Idempotency-Key: $(uuidgen)" -d '{"valor":10}'   # 401
curl -s -o /dev/null -w '%{http_code}\n' $PAG/controle/comportamento \
  -H 'X-Api-Key: <chave do OMS>'                                                            # 401
```

## 3. Caminho feliz com latência variável

1. Com o comportamento padrão, criar 20 pedidos pela API do OMS (login do Cliente e
   `POST /api/v1/pedidos`, como no [quickstart do REQ-09](../009-pagamento/quickstart.md)).
2. Esperar até 15 s e consultar: cada pedido está em `Processando` (identificador `PAG-…` no
   evento `pagamento.aprovado`) ou em `PagamentoRejeitado`.
3. No painel (`http://localhost:18888`), abrir o trace de um pedido: há o span `cobrar
   pagamento` com um ou mais spans de cliente HTTP `POST`, de durações diferentes entre os
   pedidos.

## 4. Falha passageira seguida de sucesso

```bash
curl -s -X POST $PAG/controle/roteiro -H "X-Chave-Controle: $CHAVE_CONTROLE" \
  -H 'Content-Type: application/json' -d '{"comportamentos":["indisponivel","aprovar"]}'
```

Criar um pedido. Esperado: `Processando`; no log do Worker, um 7013 `FalhaPassageiraGateway`
(tipo `indisponivel`, tentativa 1); no trace, dois spans HTTP (`503` e `201`);
`GET /controle/operacoes/cobranca/<pedidoId>` mostra `efetivacoes: 1`, `chamadas: 2`.

## 5. Resultado incerto

```bash
curl -s -X POST $PAG/controle/roteiro -H "X-Chave-Controle: $CHAVE_CONTROLE" \
  -H 'Content-Type: application/json' -d '{"comportamentos":["processar-sem-responder"]}'
```

Criar um pedido. Esperado: 7014 `ResultadoIncertoGateway`, a segunda tentativa recebe `200` com
`Idempotent-Replayed: true`, o pedido fica `Processando` com o mesmo `PAG-…` e a operação tem
`efetivacoes: 1`. Repetir com `docker compose restart worker` logo depois de criar o pedido: o
resultado é o mesmo.

## 6. API fora do ar: circuito e pausa

```bash
curl -s -X PUT $PAG/controle/modo -H "X-Chave-Controle: $CHAVE_CONTROLE" \
  -H 'Content-Type: application/json' -d '{"modo":"indisponivel"}'
```

Criar 10 pedidos. Esperado: 7016 `CircuitoAberto` e 5020 `ConsumoPausado` (`pagamento`); no
painel do RabbitMQ (`http://localhost:15672`), as mensagens esperam em `oms.pagamento`, com
`oms.pagamento.dlq` vazia; `curl -fsS http://localhost:5231/health/pronto` continua `200`;
pedidos novos continuam reservando estoque (consumidor `estoque` no ar).

```bash
curl -s -X DELETE $PAG/controle/modo -H "X-Chave-Controle: $CHAVE_CONTROLE"
```

Esperado, em até ~30 s: 5021 `ConsumoRetomado`, 7018 `CircuitoFechado`, e os 10 pedidos
cobrados.

## 7. Pedido cancelado com cobrança sem resposta (FR-015)

Coberto pelo teste de integração `CobrancaAposCancelamentoTestes` (a janela é curta demais para
reproduzir à mão): roteiro `processar-sem-responder` com tentativas esgotadas, cancelamento
pela operação e reentrega. Esperado: 7019 `ConsultaCobranca` com `aprovada`, pagamento com
estorno pendente e, na rodada seguinte, `pagamento.estornado`.

## 8. Estorno com o circuito aberto

Com o modo `indisponivel`, cancelar um pedido pago pela operação. Esperado: 7020
`EstornosAdiados`, o pagamento continua `EstornoPendente` sem tentativa contada; desfeito o
modo, o estorno é confirmado na rodada seguinte.

## 9. Suíte

```bash
dotnet build
dotnet test          # Docker no ar; a API simulada sobe em Kestrel pelos testes
dotnet format --verify-no-changes
```

## 10. Encerrar

```bash
curl -s -X POST $PAG/controle/restaurar -H "X-Chave-Controle: $CHAVE_CONTROLE"
docker compose --profile demo down -v
```
