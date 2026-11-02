# Data Model: gateway de pagamento por HTTP com API simulada

**Requisito**: REQ-19 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

**Nenhuma migration.** O banco do OMS não muda: o `Pagamento` continua com as mesmas colunas e
situações (REQ-09, REQ-12, REQ-13); só a origem do identificador de transação e do estorno
passa a ser a API simulada (`PAG-…`, `EST-…` em vez de `SIM-…`). Os dados novos são a memória
da API simulada e o estado em memória do Worker.

## API simulada (em memória, R-04)

### OperacaoPagamento

| Campo | Tipo | Regra |
|-------|------|-------|
| `Tipo` | `Cobranca` \| `Estorno` | parte da chave composta |
| `Chave` | `Guid` (o pedido) | única por `Tipo` |
| `Valor` | `decimal` | > 0, até 2 casas; a mesma chave com outro valor é `chave-reutilizada` |
| `IdentificadorCobranca` | `string?` | só no estorno; o `PAG-…` informado |
| `Situacao` | `EmAndamento` \| `Aprovada` \| `Recusada` \| `Confirmado` \| `Recusado` | ver transições |
| `Identificador` | `string?` | `PAG-<guid v7>` ou `EST-<guid v7>`; nulo na recusa |
| `Motivo` | `string?` | só na recusa |
| `ProcessadaEm` | `DateTimeOffset?` | ao concluir |
| `Efetivacoes` | `int` | 0 ou 1; conferido pelos testes (SC-002) |
| `Chamadas` | `int` | todas as chamadas recebidas com a chave, inclusive repetições |

Transições:

```text
(nenhuma) --primeira chamada--> EmAndamento
EmAndamento --aprovar / processar-sem-responder--> Aprovada | Confirmado   (Efetivacoes = 1)
EmAndamento --recusar / regra do estorno-------> Recusada | Recusado        (Efetivacoes = 1)
EmAndamento --503, 429, 500, sem-resposta------> (removida: a chave fica livre)
Aprovada|Recusada|Confirmado|Recusado --nova chamada mesma chave--> (sem mudança; devolve o mesmo, Chamadas + 1)
Aprovada|Recusada|Confirmado|Recusado --falha sorteada/forçada----> (sem mudança; responde a falha, Chamadas + 1)
```

O comportamento é decidido antes da consulta à memória (research R-03). Só a operação criada
na própria chamada, ainda `EmAndamento`, é removida por uma falha; uma operação concluída nunca
é removida nem alterada por falha.

`Efetivacoes` conta a decisão de negócio da operação (inclusive a recusa), não o dinheiro;
para os testes, o que importa é que nunca passa de 1.

### ComportamentoSimulacao

As chances e a latência em vigor (da configuração, trocadas pela rota de controle), o modo
forçado (`ModoForcado?`) e o roteiro (`ConcurrentQueue<Comportamento>`). Volta à configuração
no reinício e em `POST /controle/restaurar`.

`Comportamento`: `Aprovar`, `Recusar`, `Indisponivel`, `LimiteTaxa`, `ErroInterno`,
`SemResposta`, `ProcessarSemResponder`.

## OMS (Application)

### ConsultaCobranca (novo)

| Caso | Dados |
|------|-------|
| `NaoEncontrada` | — |
| `Aprovada` | `IdentificadorTransacao` |
| `Recusada` | `Motivo` |

### Exceções (novas)

| Tipo | Onde | Campos | Uso |
|------|------|--------|-----|
| `DependenciaIndisponivelException` | `Application/Mensageria` | `DisponivelEm` | a base de consumo pausa a inscrição (R-09) |
| `GatewayPagamentoIndisponivelException` : `DependenciaIndisponivelException` | `Application/Pagamentos` | — | circuito aberto |
| `FalhaGatewayPagamentoException` | `Application/Pagamentos` | `Tipo` (`Passageira`, `Permanente`), `Operacao` | sem resultado depois das tentativas |

### ResultadoRodadaEstorno

Ganha `Adiado`: o circuito está aberto; nada é gravado e a tentativa não é contada.

## OMS (Infrastructure, em memória)

| Estado | Onde | Conteúdo |
|--------|------|----------|
| Circuito | pipeline de resiliência do `HttpClient` do gateway | fechado / aberto (até quando) / meio-aberto; por instância do Worker |
| `EstadoGatewayPagamento` | singleton | espelho do circuito para a métrica `oms.pagamentos.circuito` e o `DisponivelEm` da exceção |
| `EstadoConsumidores` | já existe (REQ-17) | ganha a situação **pausado** (com `PausadoAte`), que conta como saudável na prontidão |

## Configuração

- Worker: seção `GatewayPagamento` ([contrato do cliente](./contracts/cliente-gateway-pagamento.md#configuração-gatewaypagamento)).
- API simulada: seções `Seguranca` e `Simulacao` ([contrato da API](./contracts/api-pagamentos-simulada.md#configuração)).
- Testes: `GatewayPagamentoMemoria:TaxaAprovacaoPercentual` no projeto de integração (R-11).
