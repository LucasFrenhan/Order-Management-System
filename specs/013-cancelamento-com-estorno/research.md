# Research: cancelamento depois do pagamento, com estorno (REQ-13)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

Não há `NEEDS CLARIFICATION` no contexto técnico. As decisões abaixo resolvem o "como" das
escolhas que a spec, o specify e o clarify deixaram para o plano.

## R-01 Um cancelamento só, no domínio

**Decisão**: método novo `Pedido.Cancelar(Guid? autorId, string motivo,
IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)`, que faz, nesta ordem
e sem gravar nada pela metade (as validações vêm antes de qualquer mudança):

1. confere a transição para `Cancelado` (status não final; `Despachado` nunca);
2. devolve o estoque, se reservado (`DevolverEstoque`, REQ-08);
3. pede o estorno, se o pagamento está `Aprovado` (`Pagamento.SolicitarEstorno`, R-02);
4. cancela a ordem de entrega, se existe e não foi despachada (`OrdemEntrega.Cancelar`, R-05);
5. muda o status para `Cancelado`, com o autor (ou sem autor) e o motivo no histórico.

Devolve um `ResultadoCancelamento` com o que aconteceu (estoque devolvido, estorno pedido,
ordem cancelada), para os logs.

`ServicoPedidos.AtualizarStatusAsync` passa a chamar `Cancelar` quando o status pedido é
`Cancelado`, no lugar de `DevolverEstoque` + `MudarStatus`. Os produtos são lidos para
alteração só quando há estoque reservado, como hoje. As permissões e as respostas não mudam
(clarify Q3): `AvaliarTransicao` continua decidindo 403 e 409 antes.

O esgotamento da etapa de entrega na reconciliação usa o mesmo método, sem autor (R-06).

**Por quê**: FR-001 e FR-008 exigem tudo na mesma gravação. Um único método garante que a
API e a reconciliação cancelam do mesmo jeito, e que um cancelamento futuro (por exemplo, de
outra rota) não esqueça o estorno.

**Alternativas rejeitadas**:
- manter a lógica no `ServicoPedidos` e duplicá-la na reconciliação: as duas versões
  divergiriam;
- estorno como reação a um evento de cancelamento: não existe evento de cancelamento, e
  criá-lo é a issue #29.

## R-02 Estorno assíncrono: situações e campos do pagamento

**Decisão**: `SituacaoPagamento` ganha `EstornoPendente` e `Estornado`. O `Pagamento` ganha:

| Campo | Uso |
|-------|-----|
| `EstornoSolicitadoEm` | Quando o cancelamento pediu o estorno; ordena a fila e mede o prazo do alerta |
| `IdentificadorEstorno` | Referência do estorno no gateway, na confirmação |
| `EstornadoEm` | Instante da confirmação |
| `TentativasEstorno` | Quantas tentativas falharam |
| `UltimaFalhaEstornoEm` | Quando foi a última falha |

Transições, todas pelo `Pedido` (raiz do agregado), que também atualiza `AtualizadoEm` para
a versão (`rowversion`) do pedido mudar e a concorrência otimista valer:

```text
Aprovado ──SolicitarEstorno──▶ EstornoPendente ──ConfirmarEstorno──▶ Estornado
                                   │  ▲
                                   └──┘ RegistrarFalhaEstorno (tentativas + 1)
```

A contagem de tentativas fica **gravada**, ao contrário da contagem da reconciliação do
REQ-12, que fica em memória: o estorno nunca desiste, e o alerta de 24 h e 3 tentativas
(FR-003) precisa sobreviver a reinícios do Worker.

**Por quê**: a decisão do specify (Q1 = B) é cancelar na hora e estornar depois, sem que a
API dependa do gateway (princípio IV da constituição).

**Alternativas rejeitadas**:
- tabela própria de estornos: o estorno é no máximo um por pagamento (FR-005), e a tabela
  `Pagamentos` já é 1:1 com o pedido;
- contagem em memória: o alerta seria adiado a cada reinício, e o estorno pendente não tem
  outro sinal para a operação.

## R-03 Chave de idempotência no gateway

**Decisão**: a porta `IGatewayPagamento` ganha o estorno, e o pedido passa a ser,
explicitamente, a chave de idempotência das duas operações:

```text
Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken)
Task<ResultadoEstorno>  EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken)
```

`ResultadoEstorno(bool Confirmado, string? IdentificadorEstorno, string? Motivo)`, com
`Confirmado(id)` e `Recusado(motivo)`, como `ResultadoCobranca`. Exceção é falha técnica.

O `GatewayPagamentoSimulado` (singleton) guarda os resultados em dois
`ConcurrentDictionary<Guid, ...>`, por pedido: a mesma chave devolve o mesmo resultado (a
aprovação, com a mesma transação, ou a recusa), sem novo sorteio (FR-013 e FR-014). O
estorno simulado sempre confirma, com identificador `SIM-EST-…`. Os testes trocam o gateway
por falsos que falham ou recusam, como o `GatewayQueFalha` do REQ-12 já faz.

A memória do simulado se perde quando o Worker reinicia, o que só é aceitável porque a
simulação não tem efeito externo (spec, Assumptions). Um gateway real guarda as chaves do
lado dele: o adaptador real passa o `pedidoId` como cabeçalho ou campo de idempotência.

**Por quê**: o consumidor de pagamento e a reconciliação já passam o `pedidoId` ao gateway;
torná-lo a chave não muda o fluxo deles (FR-013: "comportamento atual"), só o contrato da
porta e o simulado.

**Alternativas rejeitadas**:
- chave por evento (`IdEvento`): a reconciliação não tem evento, e reentregas com outro
  envelope cobrariam de novo;
- gravar a chave numa tabela antes de chamar o gateway: só faz sentido com gateway real, e
  o pedido já é único.

## R-04 Processamento dos estornos pendentes no Worker

**Decisão**: serviço em segundo plano novo, `ProcessadorEstornos` (Infrastructure), no mesmo
molde do `ReconciliadorPedidos`:

- primeira rodada na partida, depois a cada `Estornos:IntervaloSegundos` (padrão 60);
- uma instância por rodada, com `sp_getapplock 'oms-estornos'` (conexão própria, liberado no
  fim), para o gateway não ser chamado em paralelo para o mesmo pedido;
- lista até `Estornos:TamanhoLote` (padrão 100) pagamentos `EstornoPendente`, pela consulta
  nova `IConsultaEstornosPendentes`, ordenados por `UltimaFalhaEstornoEm` nula primeiro e
  depois por `EstornoSolicitadoEm` (os que nunca falharam vêm antes, como no FR-011 do
  REQ-12);
- cada pedido num escopo próprio, decidido pelo `ServicoEstornos` (Application):
  1. relê o pedido para alteração; se o pagamento não está mais `EstornoPendente`, nada a
     fazer;
  2. chama `EstornarAsync` com a transação e o valor da cobrança;
  3. confirmado: `Pedido.ConfirmarEstorno(id, agora)` e grava;
  4. recusa ou exceção: descarta o que estava rastreado, relê e
     `Pedido.RegistrarFalhaEstorno(agora)`, grava; log de aviso, ou de erro quando
     `agora - EstornoSolicitadoEm > Estornos:LimiteAlertaSegundos` **e**
     `TentativasEstorno >= Estornos:TentativasAlerta` (padrões 86 400 e 3, FR-003);
  5. conflito de concorrência ao gravar a confirmação: relê e tenta gravar de novo até 3
     vezes, sem chamar o gateway outra vez (o resultado vale para as reavaliações, como no
     REQ-09). Esgotadas, a próxima rodada chama o gateway de novo, que devolve o mesmo
     estorno (R-03).
- a falha de um pedido não interrompe os demais; banco fora derruba só a rodada, com log.

**Por quê**: FR-002, FR-003, FR-007 (primeiro pedido de estorno em até um intervalo) e
SC-002 (1 minuto). Separado da reconciliação porque o intervalo, o lote e a regra de
desistência são outros: o estorno nunca desiste.

**Alternativas rejeitadas**:
- dentro do ciclo da reconciliação: o ciclo é de 5 min e o candidato é "pedido parado há mais
  de 10 min", o que atrasaria todo estorno em até 15 min;
- evento interno no outbox, consumido pelo Worker: cria um contrato de mensagem novo (é o
  tema da #29) e ainda precisaria da rodada periódica para as novas tentativas;
- chamar o gateway na própria requisição de cancelamento, com a rodada só para as falhas:
  a API passaria a depender do gateway, o que o specify rejeitou.

## R-05 Cancelamento da ordem de entrega

**Decisão**: `OrdemEntrega` ganha `CanceladaEm` (nulo até o cancelamento) e
`Cancelar(DateTimeOffset)`, que recusa uma ordem já despachada (`DespachadoEm` preenchido).
`Pedido.Cancelar` chama `OrdemEntrega?.Cancelar` (R-01).

A resposta da consulta ganha `canceladaEm` em `EntregaPedidoResponseDTO` e no schema
`EntregaPedidoResponseDTO` do `openapi-v1.yaml`, opcional e anulável. Para o oasdiff, campo
novo e opcional numa resposta não é mudança incompatível.

O rastreio de pedido cancelado já responde 409 pelo status (`PodeRegistrarRastreio`), e o
consumidor de entrega não cria ordem para pedido cancelado (`AptoParaOrdemEntrega`): nada
muda nesses dois caminhos (FR-010).

**Por quê**: FR-008 e FR-009.

**Alternativas rejeitadas**:
- apagar a ordem: perde o registro de que ela existiu, e o índice único de `PedidoId`
  continua valendo;
- situação da ordem como enum: hoje a ordem não tem situação, e um único instante basta.

## R-06 Esgotamento da etapa de entrega na reconciliação

**Decisão**: em `ServicoReconciliacao`:

- `esgotavel` passa a incluir `EtapaFluxoPedido.Entrega` (FR-011);
- `ContinuaNaEtapa(Entrega)` = `pedido.AptoParaOrdemEntrega` (FR-012);
- `CancelarAsync(Entrega)` lê os produtos para alteração e chama
  `pedido.Cancelar(null, MotivoOrdemEntregaNaoCriada, produtos, agora)`, com a constante nova
  `MotivoOrdemEntregaNaoCriada = "Não foi possível criar a ordem de entrega."`;
- o log 9002 (`PedidoEsgotado`) já leva a etapa e o motivo; o estorno pedido gera também o
  log de estorno solicitado com a origem `Reconciliacao`.

O pedido da etapa de entrega não tem ordem (é o que o torna parado), então o passo 4 do R-01
não acontece aqui. O estoque volta, e o estorno fica pendente para o `ProcessadorEstornos`.

**Por quê**: US3; os mesmos limites e a mesma releitura das outras etapas.

## R-07 Cobrança aprovada para pedido cancelado no meio do caminho

**Decisão**: em `ProcessamentoPagamento.AplicarAsync`, quando a cobrança já tem resultado
aprovado (`resultado` preenchido) e a releitura depois de um conflito encontra o pedido
`Cancelado` sem pagamento, o consumidor chama o método novo
`Pedido.RegistrarCobrancaAposCancelamento(identificadorTransacao, agora)`: cria o pagamento
`Aprovado` com a transação e o valor total, e logo pede o estorno (`EstornoPendente`). Grava
sem evento de saída (o pedido está cancelado; `pagamento.aprovado` não sai). Log de aviso
próprio (FR-006, clarify Q1).

Uma recusa nesse caso continua descartada, como hoje (o Cliente não foi cobrado). Os outros
motivos de "não apto" (já pago, `PagamentoRejeitado`) seguem como hoje.

**Limitação aceita**: se o Worker cair depois de o gateway aprovar e antes de gravar, e a
operação cancelar o pedido antes da reentrega, a reentrega encontra o pedido cancelado sem
resultado em mãos e não chama o gateway. Com o simulado não há efeito; com um gateway real,
seria preciso consultar a cobrança pela chave. Fica anotado no fluxo 5.

**Por quê**: o objetivo da issue é o Cliente nunca ficar cobrado por pedido cancelado; o
caso de conflito é o único em que o sistema sabe que cobrou e não registrou.

## R-08 Persistência

**Decisão**: uma migration, `EstornoPagamento`:

- `Pagamentos`: colunas `EstornoSolicitadoEm` (`datetimeoffset` nulo), `IdentificadorEstorno`
  (`nvarchar(100)` nulo), `EstornadoEm` (`datetimeoffset` nulo), `TentativasEstorno` (`int`,
  padrão 0, `>= 0`), `UltimaFalhaEstornoEm` (`datetimeoffset` nulo); recria
  `CK_Pagamentos_SituacaoValida` com as situações novas; índice filtrado
  `IX_Pagamentos_EstornoPendente` em (`UltimaFalhaEstornoEm`, `EstornoSolicitadoEm`) com
  `[Situacao] = 'EstornoPendente'`, para a consulta da rodada;
- `OrdensEntrega`: coluna `CanceladaEm` (`datetimeoffset` nulo).

Dados existentes não mudam: nenhum pagamento antigo fica pendente de estorno. Pedidos pagos
e cancelados antes do REQ-13 continuam com o pagamento `Aprovado` (ver R-10).

## R-09 Configuração

**Decisão**: seção nova `Estornos`, validada na partida como `Reconciliacao`:

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Habilitado` | `true` | — |
| `IntervaloSegundos` | 60 | 1 a 3 600 |
| `TamanhoLote` | 100 | 1 a 1 000 |
| `LimiteAlertaSegundos` | 86 400 | 1 a 2 592 000 |
| `TentativasAlerta` | 3 | 1 a 100 |

A `FabricaWorker` dos testes desliga `Estornos:Habilitado` por padrão, como já faz com a
reconciliação; os testes do estorno chamam a rodada diretamente ou ligam o serviço.

## R-10 Pedidos pagos e cancelados antes do REQ-13

**Decisão**: fora do escopo da migration. Pedidos já `Cancelado` com pagamento `Aprovado`
existem em ambientes que rodaram o REQ-12 ou anteriores. Converter automaticamente para
`EstornoPendente` estornaria pedidos que a operação pode ter resolvido por fora. Fica como
nota no quickstart e no fluxo 3, com a consulta SQL para encontrá-los; a decisão é da
operação.

**Alternativa rejeitada**: a migration marcar todos como `EstornoPendente`. Simples, mas age
sobre dinheiro sem decisão humana.

## R-11 Logs

| Id | Nome | Nível | Quando |
|----|------|-------|--------|
| 7005 | `EstornoSolicitado` | Information | Estorno pedido (id do pedido, valor e origem: `Operacao`, `Reconciliacao` ou `CobrancaAposCancelamento`) |
| 7006 | `EstornoConfirmado` | Information | Gateway confirmou (id do pedido, valor, tentativas anteriores) |
| 7007 | `FalhaEstorno` | Warning | Recusa ou exceção (id do pedido, tentativas, erro) |
| 7008 | `EstornoPendenteAlemDoLimite` | Error | Mesma falha, depois de 24 h e 3 tentativas (id, tentativas, horas pendente) |
| 7009 | `CobrancaAprovadaParaPedidoCancelado` | Warning | R-07 (id do pedido, valor) |
| 7010 | `FalhaRodadaEstornos` | Warning | Rodada inteira falhou (banco fora) |
| 7011 | `RodadaEstornosIgnorada` | Debug | Outra instância tem o lock |
| 7012 | `ResumoEstornos` | Information se tratou algo; Debug se não | Fim da rodada (confirmados e falhas) |
| 8004 | `OrdemEntregaCancelada` | Information | Ordem cancelada junto com o pedido (id do pedido) |

Nenhum log traz identificador de transação, de estorno ou dado pessoal (FR-016). O 4003
(status alterado) continua como está.

## R-12 Testes

- **Unitários** (domínio e aplicação): `Pedido.Cancelar` em cada combinação (sem pagamento,
  pago, pago com ordem, despachado, já cancelado, Cliente); transições do pagamento;
  `OrdemEntrega.Cancelar`; `RegistrarCobrancaAposCancelamento`; `ServicoEstornos` com
  gateway falso (confirmação, recusa, exceção, alerta, conflito); idempotência do simulado;
  validação de `OpcoesEstornos`; `ServicoReconciliacao` esgotando a entrega.
- **Integração** (SQL Server e RabbitMQ reais, Testcontainers):
  - cancelamento pela API de pedido pago, com e sem ordem; resposta igual à de hoje e
    `canceladaEm` na consulta;
  - rodada de estornos: confirmação, falha com nova tentativa, alerta 7008, dois Workers
    (um estorno só no gateway);
  - corridas: cancelamento × cobrança (incluindo o R-07 com gateway que atrasa),
    cancelamento × despacho, cancelamento × criação da ordem, dois cancelamentos;
  - reconciliação esgotando a entrega com o gateway de ordem falhando;
  - contrato: o `openapi-v1.yaml` com `canceladaEm` continua batendo com a resposta real
    (teste de conformidade do REQ-01).
