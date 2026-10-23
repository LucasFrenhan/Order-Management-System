# Research: Rastreio, despacho e histórico na consulta (REQ-11)

**Data**: 2026-10-23 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Evolução do contrato HTTP

- **Decisão**: evolução compatível da v1, no próprio
  `specs/001-contratos-api/contracts/openapi-v1.yaml` (a fonte da verdade do REQ-01):
  - `PedidoResponseDTO` ganha `historico` (lista, sempre presente) e `entrega` (objeto ou
    `null`). Os campos atuais não mudam;
  - schemas novos: `HistoricoStatusPedidoResponseDTO`, `EntregaPedidoResponseDTO` e
    `RegistrarRastreioRequestDTO`;
  - operação nova `PATCH /api/v1/pedidos/{id}/rastreio` (`RegistrarRastreioPedido`);
  - código de erro novo `PEDIDO_RASTREIO_DUPLICADO` no catálogo;
  - `info.version` continua `v1`; a descrição do documento ganha a linha do REQ-11.
- **Justificativa**: o `PedidoResponseDTO` não fecha campos extras
  (`additionalProperties` não é `false`), então acrescentar propriedades à resposta é
  compatível. O workflow `contrato-compatibilidade` (oasdiff, `fail-on: ERR`) verifica
  isso no PR (FR-014, SC-005). O teste `ConformidadeOpenApiTestes` exige que o documento
  gerado seja igual ao contrato, então contrato e código mudam juntos.
- **Alternativas**:
  - v2 com rotas novas. Rejeitada: nada quebra a v1, e uma versão nova duplicaria rotas e
    testes sem necessidade;
  - histórico numa rota separada (`GET /pedidos/{id}/historico`). Rejeitada: a issue pede
    o histórico na consulta.

## R-02. Forma da resposta

- **Decisão**:
  - `historico[]`: `statusAnterior` (`StatusPedido` ou `null` na criação), `statusNovo`,
    `ocorridoEm` e `motivo` (`string` ou `null`), em ordem de `ocorridoEm` e, no empate,
    de `Id` (identidade crescente). Sem o autor (FR-003);
  - `entrega`: `transportadora`, `codigoRastreio` e `despachadoEm` (cada um pode ser
    `null` antes do despacho) e `criadoEm` (instante da ordem). Sem ordem, `entrega: null`
    (FR-004);
  - a mesma forma vale para todas as operações que devolvem o pedido (criação, consulta,
    mudança de status e rastreio).
- **Justificativa**: um só `PedidoResponseDTO`, como hoje. O identificador da ordem não é
  exposto: o cliente da API não tem o que fazer com ele.
- **Alternativas**: omitir `entrega` quando não há ordem. Rejeitada: `null` explícito é
  mais simples de documentar e de testar.

## R-03. Resposta idempotente gravada antes do REQ-11

- **Decisão**: no DTO, `Historico` não é `required` em C#: tem `[Required]` e o valor
  padrão `[]`, como `Moeda` (REQ-05); `Entrega` é anulável. Uma repetição idempotente de um
  pedido criado antes do deploy devolve a resposta gravada, com `historico: []` e
  `entrega: null`.
- **Justificativa**: o `RegistroIdempotencia` guarda o JSON da primeira resposta (REQ-05,
  FR-010), e um membro `required` faria a desserialização falhar para registros antigos. A
  janela é de 24 h e só afeta repetições; a consulta sempre devolve o estado atual.
- **Alternativas**: reler o pedido na repetição. Rejeitada: mudaria a regra do REQ-05 de
  devolver a resposta original.

## R-04. Leitura do pedido com o histórico

- **Decisão**: `ArmazenamentoPedidos.ObterPedidoAsync` passa a incluir `Historico`, além de
  `Itens`, `Pagamento` e `OrdemEntrega`, com `AsSplitQuery()` para não multiplicar itens
  por entradas de histórico. A ordenação fica no mapeamento.
- **Justificativa**: consulta, mudança de status e rastreio devolvem o pedido completo a
  partir da mesma leitura. Os workers também leem o histórico; o custo é uma consulta a
  mais por mensagem, aceitável no volume do projeto.
- **Alternativas**:
  - um parâmetro `comHistorico`. Rejeitada: mais um caminho para testar, sem ganho
    medido;
  - consulta única com `Include` de duas coleções. Rejeitada: produto cartesiano de itens
    por histórico.

## R-05. Regra do despacho no domínio

- **Decisão**: `Pedido.RegistrarRastreio(transportadora, codigoRastreio, autorId, instante)`
  devolve `ResultadoRastreio` (`Despachado`, `Corrigido` ou `SemMudanca`):
  - `ProntoParaEnvio`: cria a `OrdemEntrega` se não houver; registra transportadora,
    código e `DespachadoEm = instante`; `MudarStatus(Despachado, autorId, "Pedido despachado.")`
    → `Despachado` (FR-008);
  - `Despachado`: cria a ordem se não houver; mesmos dados (comparação sem diferenciar
    maiúsculas) → `SemMudanca`, nada muda; dados diferentes → atualiza transportadora e
    código, mantém `DespachadoEm` (ou usa o instante, se não houver) e não mexe no status
    nem no histórico → `Corrigido` (FR-009);
  - outros status: `InvalidOperationException`; o serviço testa antes com
    `PodeRegistrarRastreio` e responde 409 `PEDIDO_TRANSICAO_INVALIDA` (FR-009a).
  - `OrdemEntrega` ganha `CorrigirRastreio(transportadora, codigo, instante)`, que não
    mexe em `DespachadoEm`; `RegistrarDespacho` (REQ-04) continua para o primeiro registro.
- **Justificativa**: a regra fica no domínio, testável sem banco, como
  `CriarOrdemEntrega` (REQ-10). O motivo fixo identifica a entrada no histórico.
- **Alternativas**: reaproveitar `MudarStatus` pelo `ServicoPedidos.AtualizarStatusAsync`.
  Rejeitada: o rastreio não é uma mudança de status genérica (cria ordem, corrige sem
  transição).

## R-06. Rastreio duplicado sem diferenciar maiúsculas

- **Decisão**:
  - migration `RastreioSemCaixa`: `Transportadora` e `CodigoRastreio` passam a usar a
    collation `SQL_Latin1_General_CP1_CI_AS`, explícita, como o SKU (REQ-04, R-06); o
    índice único filtrado é recriado sobre as colunas;
  - o serviço confere antes, com `IArmazenamentoPedidos.RastreioEmUsoAsync(transportadora,
    codigo, pedidoId)` (outro pedido com o par), e responde 409
    `PEDIDO_RASTREIO_DUPLICADO` sem gravar;
  - a corrida entre dois despachos com o mesmo par em pedidos diferentes é barrada pelo
    índice: `SalvarAsync` traduz a violação de `IX_OrdensEntrega_Transportadora_CodigoRastreio`
    em `RastreioDuplicadoException` (Application), e o serviço responde o mesmo 409.
- **Justificativa**: a collation explícita garante o comportamento em qualquer servidor;
  a conferência prévia dá a resposta certa no caso comum, e o índice garante o caso raro
  (FR-010).
- **Alternativas**: normalizar para maiúsculas. Rejeitada no clarify (o texto é gravado
  como informado).

## R-07. Concorrência

- **Decisão**: `RegistrarRastreioAsync` no `ServicoPedidos` segue o mesmo laço de
  `AtualizarStatusAsync`: ler para alterar, decidir, gravar; em
  `ConflitoConcorrenciaException`, descartar e reler, até 3 vezes; esgotado, 409
  `PEDIDO_TRANSICAO_INVALIDA` ("alterado por outra operação"). A cada volta a decisão é
  refeita sobre o estado atual (FR-013):
  - cancelamento venceu → o pedido está `Cancelado` → 409;
  - outro despacho venceu → o pedido está `Despachado` → vira correção ou `SemMudanca`.
- **Versão do pedido**: a correção não muda o status, mas o serviço atualiza
  `Pedido.AtualizadoEm`, para o `UPDATE` do pedido levar a `rowversion` e serializar duas
  correções simultâneas.
- **Justificativa**: o mesmo padrão do REQ-05 e do REQ-08, sem bloqueio pessimista.

## R-08. Autorização

- **Decisão**: política nova `PoliticaRastreioPedido` (`Admin` ou `OperadorLogistica`). Um
  Cliente recebe 403 antes de qualquer leitura, mesmo no próprio pedido (FR-006, SC-003).
  A consulta mantém `PoliticaConsultaPedido` e a regra de dono do `ObterAsync` (FR-001).
- **Justificativa**: o rastreio não tem caso de uso para o Cliente; barrar na política
  evita revelar se o pedido existe.

## R-09. Validação da entrada

- **Decisão**: `RegistrarRastreioRequestDTO` com `transportadora` (1 a 100) e
  `codigoRastreio` (1 a 50), validados pelo `FiltroValidacao` (400 com o campo, como o
  REQ-05). Os espaços nas pontas são removidos antes de validar o tamanho e de gravar; só
  espaços conta como vazio (FR-007).

## R-10. Logs

- **Decisão**: em `EventosPedidos`, `4004 RastreioRegistrado` (Information: pedido,
  usuário, resultado `Despachado`, `Corrigido` ou `SemMudanca`) e `4005 RastreioDuplicado`
  (Information: pedido, usuário). Sem transportadora nem código no log (FR-016).

## R-11. Desempenho

- **Decisão**: teste de integração com um pedido de 10 itens e 10 entradas de histórico,
  40 consultas e 40 rastreios (correções alternadas), percentil 95 abaixo de 500 ms
  (SC-004), como `DesempenhoCriacaoTestes` do REQ-05.

## R-12. Documentação afetada

- README: linha do REQ-11, roteiro sem a #11, o despacho com rastreio e o fluxo completo do
  pedido.
- `specs/005-criacao-pedidos/quickstart.md`: nota de que a resposta do pedido passou a
  trazer `historico` e `entrega`.
