# Research: ações da operação sobre estornos pendentes (REQ-28)

[← Plano](./plan.md)

Decisões técnicas do plano. Cada uma cita os requisitos da [spec](./spec.md) que atende.

## R-01: identificador do estorno (FR-001)

- **Decisão**: o identificador do estorno é o `Id` do `Pagamento` (Guid v7, já existente e único).
  As rotas e os eventos o chamam de `estornoId`; ninguém de fora sabe que coincide com o pagamento.
- **Por quê**: hoje há no máximo um estorno por pagamento, e todos os campos do estorno já vivem no
  `Pagamento` (REQ-13). Criar uma entidade `Estorno` agora seria a mudança de modelo da #56, sem
  ganho para este requisito.
- **Para a #56**: quando o estorno parcial criar a entidade `Estorno`, a migração dá ao primeiro
  estorno de cada pagamento o mesmo id de hoje, e as rotas e os eventos deste REQ não mudam.
- **Alternativas**: entidade `Estorno` própria agora (antecipa a #56 e amplia muito o REQ); usar o
  id do pedido (contraria a recomendação da issue e quebraria com vários estornos).

## R-02: situação da baixa e campos novos (FR-005, FR-007, FR-011, FR-015)

- **Decisão**: nova situação `EstornoBaixado` em `SituacaoPagamento`, final como `Estornado`, e sete
  colunas novas em `Pagamentos`, todas anuláveis (detalhe no [data-model](./data-model.md)):
  `UltimoErroEstorno`, `TentativaEstornoForcadaEm`, `EstornoAtrasadoEm`, `EstornoBaixadoEm`,
  `EstornoBaixadoPorId`, `MotivoBaixaEstorno` e `ReferenciaExternaBaixaEstorno`.
- **Por quê**: uma situação própria deixa a conciliação distinguir a devolução pelo gateway da
  devolução por fora (FR-007) e mantém o `ExigirSituacao(EstornoPendente)` dos métodos atuais como
  barreira contra nova tentativa (FR-008). A restrição `CK_Pagamentos_SituacaoValida` é recriada
  pela migração com o valor novo.
- **Alternativas**: reaproveitar `Estornado` com uma marca de origem (mistura os dois casos nas
  consultas e na guarda do `pagamento.estornado`); tabela de auditoria separada (mais uma entidade
  para um registro que acontece uma vez por estorno).

## R-03: a baixa espera a rodada (FR-009)

- **Decisão**: a baixa roda numa transação do EF Core e, antes de reler o estorno, pede o mesmo lock
  da rodada, `sp_getapplock 'oms-estornos'`, com `@LockOwner = 'Transaction'` e
  `@LockTimeout = 5000`. Com o lock: relê o pedido, aplica a baixa, grava (estorno, evento e aviso) e
  confirma a transação, que libera o lock. Sem o lock em 5 s: desfaz a transação e responde 409
  `ESTORNO_RODADA_EM_ANDAMENTO`, com o log 7023.
- **Efeito na rodada**: o Worker continua pedindo o lock com `@LockTimeout = 0`. Se a baixa o tem, o
  Worker pula a rodada (log 7011, Debug) e tenta no próximo intervalo; a baixa dura milissegundos.
- **Porta**: `IBloqueioRodadaEstornos.ExecutarAsync(espera, acao)` na Application, implementada no
  Infrastructure sobre a conexão do `ContextoBancoDados`. O projeto não usa
  `EnableRetryOnFailure`, então a transação explícita não conflita com uma estratégia de execução
  (como em `ArmazenamentoSessoes` e `GestorIdentidade`).
- **Por quê**: a rodada já segura o lock durante toda a conversa com o gateway, e é exatamente esse
  intervalo (pediu, não gravou) que a baixa não pode atravessar. O lock por transação é liberado
  sozinho num erro ou numa conexão perdida.
- **Alternativas**: marca de tentativa em andamento por estorno (precisa de limpeza de marca órfã se
  o Worker cair; rejeitada no clarify); 409 imediato (rejeitado no clarify).

## R-04: tentativa forçada (FR-011 a FR-014)

- **Decisão**: a rota grava `TentativaEstornoForcadaEm` (instante do primeiro pedido; um segundo
  pedido não muda nada e responde 202, FR-014). A consulta da rodada passa a ler em duas etapas:
  primeiro os pendentes com tentativa forçada, do pedido mais antigo para o mais novo, por um índice
  filtrado novo `IX_Pagamentos_EstornoForcado`; depois completa o lote com a fila de hoje
  (`IX_Pagamentos_EstornoPendente`), sem repetir os já escolhidos.
- **Retirada da marca**: `ConfirmarEstorno` e `RegistrarFalhaEstorno` limpam a marca na mesma
  gravação. No adiamento pelo circuito, nada é gravado (`DescartarAlteracoes`, como hoje), e a marca
  continua (FR-013).
- **Concorrência**: a marca muda o pedido (versão de linha). Se a rodada gravar a falha ao mesmo
  tempo, o conflito segue a regra atual: a tentativa não é contada, e a rodada seguinte trata o
  pedido de novo, ainda marcado.
- **Por quê**: segue o padrão da API que registra e do Worker que executa (REQ-14); a API não fala
  com o gateway (constituição, princípio IV, e o desenho do REQ-19).
- **Alternativas**: chamar o gateway pela API (traz para a API o cliente HTTP, o circuito e o lock);
  acordar o Worker na hora (não há canal da API para o Worker; o intervalo de 60 s basta).

## R-05: último erro sem dado sensível (FR-005)

- **Decisão**: `RegistrarFalhaEstorno` recebe um texto curto, montado pela Application a partir de
  categorias fechadas, gravado em `UltimoErroEstorno` (até 200 caracteres):

  | Origem | Texto gravado |
  |--------|---------------|
  | Recusa do gateway | `Recusado: <motivo>`, com o motivo do gateway cortado em 180 caracteres |
  | `FalhaGatewayPagamentoException` | `Falha passageira do gateway.` ou `Falha permanente do gateway.`, pelo `TipoFalhaGateway` |
  | Outra exceção | `Erro inesperado: <nome do tipo da exceção>.` |

- **Por quê**: a mensagem da exceção pode trazer URL, corpo de resposta ou identificadores; o tipo e
  a categoria bastam para a operação decidir. O log 7007/7008 continua como hoje.
- **Alternativa**: gravar a mensagem da exceção (risco de vazar dado do gateway).

## R-06: evento de estorno travado uma única vez (FR-015 a FR-018)

- **Decisão**: em `RegistrarFalhaAsync`, quando a falha leva o estorno além do limite (a mesma
  condição do 7008) e `EstornoAtrasadoEm` ainda é nulo, o domínio marca `EstornoAtrasadoEm` e a
  Application acrescenta o `pagamento.estorno-atrasado` v1 ao outbox, na mesma gravação da falha.
  O log 7024 (Warning) registra a publicação.
- **Uma vez só**: a coluna só passa de nulo para preenchido; a guarda do `ArmazenamentoPedidos`
  passa a exigir o evento nessa transição (como já faz com o `pagamento.estornado`). Uma gravação com
  conflito descarta tudo, e a próxima falha tenta de novo. Mudar a configuração do limite não limpa a
  coluna.
- **Estornos antigos** (US3, cenário 5): a coluna nasce nula, e a próxima falha depois da implantação
  emite o evento.
- **Sem aviso ao Cliente**: o evento não passa pela `FabricaAvisos`.

## R-07: evento da baixa e aviso ao Cliente (FR-010, FR-010a)

- **Decisão**: a baixa grava o `pagamento.estorno-baixado` v1 no outbox. A guarda
  `ExigirEventosDeCancelamentoEEstorno` passa a exigir esse evento na transição
  `EstornoPendente → EstornoBaixado`. O `AdicionarAvisosAsync` trata essa transição como a da
  confirmação: cria o aviso `pedido.estorno-confirmado` com o id do evento da baixa como id da
  notificação (o mesmo vínculo que hoje usa o id do `pagamento.estornado`).
- **Por quê**: reaproveita a garantia existente (evento e aviso juntos, na mesma transação).

## R-08: listagem (FR-002 a FR-004)

- **Decisão**: `GET /api/v1/estornos/pendentes`, paginada por cursor com `tamanho` de 1 a 100
  (padrão 20), como pedidos, produtos e usuários. Ordem: `EstornoSolicitadoEm` e, no empate, `Id`. O
  cursor guarda os dois, assinado pela chave dos cursores (`IChaveCursor`, REQ-27).
- **Mudança na spec**: a primeira versão pedia a ordem da fila da rodada. Essa ordem muda a cada
  falha (a última falha vai para o fim), e um cursor sobre ela pularia ou repetiria estornos entre as
  páginas. A spec (FR-003 e US1, cenário 1) foi ajustada no plan.
- **Índice**: um índice filtrado novo, `IX_Pagamentos_EstornoPendenteListagem`
  (`EstornoSolicitadoEm`, `Id`) com o mesmo filtro.

## R-09: alerta na listagem (FR-002, US1 cenário 3)

- **Decisão**: o campo `alemDoLimiteAlerta` é calculado na consulta com a mesma regra do 7008:
  `agora − EstornoSolicitadoEm > LimiteAlertaSegundos` e `TentativasEstorno ≥ TentativasAlerta`. A
  API passa a ler as duas chaves da seção `Estornos` (as mesmas do Worker, com os mesmos padrões e a
  mesma validação). O campo `atrasadoEm` mostra quando o evento de atraso saiu (nulo se não saiu).
- **Por quê**: um estorno que passou do limite antes da implantação ainda não tem `EstornoAtrasadoEm`
  e precisa aparecer em alerta.
- **Cuidado**: a API e o Worker devem usar a mesma configuração de alerta. O compose e os testes usam
  os padrões; o fluxo 10 registra a regra.

## R-10: autorização (FR-019, FR-020)

- **Decisão**: duas políticas novas em `PoliticasAutorizacao`:
  - `PoliticaEstornos`: Admin ou OperadorLogistica, para a listagem, a consulta e a tentativa forçada.
  - `PoliticaBaixaEstorno`: Admin e o `RequisitoAdminAtivo` do REQ-25.
- **Rotas fora de `/admin`**: o prefixo `/api/v1/admin` hoje é só do Admin (DLQ e usuários). As rotas
  ficam em `/api/v1/estornos`, como os pedidos, que a operação também usa.

## R-11: métrica (FR-022a)

- **Decisão**: `ObservableGauge<long>` `oms.estornos.pendentes` em `TelemetriaOms`, com o atributo
  `oms.alerta` (`true` ou `false`), no molde do `oms.outbox.pendentes`. A rodada que pega o lock
  conta os pendentes e os além do limite (regra do R-09) numa consulta só, no começo, e guarda os
  dois números para o medidor. A rodada ignorada (sem lock) não atualiza.

## R-12: logs (FR-022)

| ID | Nome | Nível | Onde | Campos |
|----|------|-------|------|--------|
| 7021 | `TentativaEstornoForcada` | Information | API | estorno, pedido, autor |
| 7022 | `EstornoBaixado` | Warning | API | estorno, pedido, valor, autor (sem motivo e sem referência) |
| 7023 | `BaixaEstornoRecusadaRodadaEmAndamento` | Warning | API | estorno, autor |
| 7024 | `EstornoAtrasadoPublicado` | Warning | Worker | estorno, pedido, tentativas, horas pendente |

A baixa é Warning porque encerra uma obrigação financeira sem o gateway e deve chamar atenção numa
auditoria dos logs.

## R-13: erros (FR-009, FR-014, contratos)

| Código | Status | `title` / `detail` |
|--------|--------|--------------------|
| (sem `codigo`, como os demais 404) | 404 | "Estorno não encontrado." / "Nenhum estorno com o identificador informado." |
| `ESTORNO_NAO_PENDENTE` | 409 | "Estorno não está pendente." / "O estorno já foi confirmado pelo gateway ou baixado." |
| `ESTORNO_RODADA_EM_ANDAMENTO` | 409 | "Rodada de estornos em andamento." / "Rodada de estornos em andamento; tente de novo em instantes." |

Validação (400, `ProblemDetails` com `erros`): `motivo` obrigatório, 10 a 500 caracteres sem os
espaços das pontas; `referenciaExterna` opcional, até 100; `tamanho` e `cursor` como nas outras
listagens; identificador inválido na rota é 400 (sem a restrição `{id:guid}`, R-10 do REQ-05).

## R-14: contratos e testes de contrato (FR-021)

- `specs/001-contratos-api/contracts/openapi-v1.yaml` ganha as quatro rotas e os schemas novos, só
  por acréscimo; o workflow `contrato-compatibilidade` (oasdiff) confere.
- Os schemas dos dois eventos novos ficam em [contracts/eventos](./contracts/eventos/) e entram no
  `SchemaEventosTestes`, como os do REQ-15.
- O enum `SituacaoPagamento` não aparece em nenhum contrato (REQ-13, clarify), então a situação nova
  não muda os existentes.

## Conferências da implementação

- Confirmar que alterar só o `Pagamento` atualiza a versão de linha do `Pedido` (a rodada de hoje já
  depende disso para o `ConflitoConcorrenciaException`).
- Confirmar que `sp_getapplock` com `LockOwner = 'Transaction'` na conexão do EF Core funciona dentro
  de `BeginTransactionAsync` (teste de integração com a rodada segurando o lock).
