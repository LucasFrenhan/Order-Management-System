# Research: Criação, consulta e status de pedidos (REQ-05)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

## R-01. Divisão entre camadas

- **Decisão**:
  - **Application**: `ServicoPedidos` implementa `IServicoPedidos` e concentra as regras
    do requisito:
    - validação de produtos (422);
    - orquestração da idempotência;
    - visibilidade da consulta;
    - aplicação das transições;
    - reavaliação em conflito;
    - mapeamento para os DTOs;
    - eventos de log.

    Ele depende de duas abstrações novas e do `TimeProvider`:
    - `IArmazenamentoPedidos`: leitura de produtos, pedidos e registros de idempotência,
      inclusão e gravação atômica (R-02);
    - `IGeradorNumeroPedido`: o próximo número `PED-AAAAMMDD-NNNNNN` (R-05).
  - **Domain**: `TransicoesStatusPedido` guarda a tabela de transições do contrato v1
    (R-06). A regra é pura e independe de quem a chama.
  - **Infrastructure**: `ArmazenamentoPedidos` e `GeradorNumeroPedido` sobre o
    `ContextoBancoDados`, o mapeamento do registro de idempotência e a migration
    `ChavesIdempotencia`.
  - **API**: os endpoints não mudam. O `Program.cs` registra o `ServicoPedidos` no lugar
    do provisório.
- **Justificativa**:
  - A constituição põe as regras em serviços de aplicação e no domínio, e o Princípio V
    exige testes unitários para elas. Com as abstrações, o serviço é testado sem banco,
    como o `ServicoAutenticacao` do REQ-03.
  - O REQ-04 deixou para este requisito a escolha entre repositório e unidade de
    trabalho.
- **Alternativas**:
  - Serviço na Infrastructure, usando o `ContextoBancoDados` direto. Rejeitada: as regras
    só seriam testáveis com banco.
  - Um repositório por entidade, mais uma unidade de trabalho separada. Rejeitada: mais
    peças sem ganho. As operações daqui sempre gravam pedido e registro juntos.

## R-02. `IArmazenamentoPedidos`: porta única de persistência

- **Decisão**: interface no Application (`Pedidos/IArmazenamentoPedidos.cs`):
  - `ObterProdutosAsync(IReadOnlyCollection<Guid> ids)`: devolve os produtos encontrados,
    só para leitura.
  - `ObterPedidoAsync(Guid id, bool paraAlterar)`: devolve o pedido com os itens. Com
    `paraAlterar`, o pedido volta rastreado, para a gravação.
  - `ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave)`.
  - `Adicionar(Pedido pedido, RegistroIdempotencia registro)`.
  - `SalvarAsync()`: gravação atômica (uma chamada ao `SaveChangesAsync`). Em caso de
    conflito, lança `ChaveIdempotenciaRegistradaException` (R-04) ou
    `ConflitoConcorrenciaException` (REQ-04).
  - `DescartarAlteracoes()`: limpa o rastreamento depois de um conflito, para reler.
- **Justificativa**:
  - Uma porta por caso de uso mantém o serviço legível, e a gravação atômica cobre FR-006
    e FR-018.
  - Exceções do Application permitem tratar o conflito sem conhecer o EF Core (REQ-04,
    `contracts/persistencia.md`).
- **Alternativas**: devolver `bool` em `SalvarAsync`. Rejeitada: há dois tipos de conflito
  com tratamentos diferentes.

## R-03. Armazenamento da chave de idempotência

- **Decisão**:
  - **Onde**: uma tabela `ChavesIdempotencia` no SQL Server, como o contrato v1
    recomendou.
  - **Classe**: `RegistroIdempotencia` no Application (`Pedidos/RegistroIdempotencia.cs`),
    mapeada na Infrastructure. Não é entidade do domínio: é um detalhe do caso de uso de
    criação.
  - **Colunas**:
    - `ClienteId` e `Chave`, que formam a chave primária composta (FR-007);
    - `Impressao`: hash SHA-256 em hexadecimal, com 64 caracteres (R-04);
    - `PedidoId`, chave estrangeira para `Pedidos`, com `Cascade`;
    - `Resposta`: o JSON do `PedidoResponseDTO` enviado (FR-009, clarify);
    - `CriadoEm`.
  - **Gravação**: o registro entra na mesma gravação do pedido (FR-006). Só uma criação
    bem-sucedida o grava (FR-013, clarify).
  - **Retenção**: a chave nunca é apagada por este requisito, o que cumpre o mínimo de 24
    horas (FR-009). O índice `IX_ChavesIdempotencia_CriadoEm` deixa pronta uma limpeza
    futura, que só pode apagar registros com mais de 24 horas.
- **Justificativa**:
  - Banco em vez de cache: a constituição já tem o SQL Server, e a gravação junto com o
    pedido só é atômica no mesmo banco.
  - A chave primária composta é o que impede dois pedidos para o mesmo par (FR-012), mesmo
    com requisições simultâneas.
- **Alternativas**:
  - Cache distribuído (Redis). Rejeitada: não faz parte da constituição, e não haveria
    atomicidade com o pedido.
  - Colunas de idempotência na tabela `Pedidos`. Rejeitada: a resposta guardada e a
    impressão não são dados do pedido.
  - Job de limpeza agora. Adiada: nenhum requisito pede, e o volume esperado é pequeno.

## R-04. Impressão do conteúdo e corrida de chaves

- **Decisão**:
  - **Impressão (FR-008)**: SHA-256 do texto canônico `produtoId:quantidade`, com os itens
    ordenados por `produtoId`, separados por `;` e com o Guid no formato `D`. A classe
    `ImpressaoConteudo` (Application) faz esse cálculo.
  - **Fluxo da criação**:
    1. Ler o registro de (cliente, chave). Se existir, mesma impressão devolve a
       `Resposta` guardada (FR-010), e impressão diferente devolve 409
       `IDEMPOTENCIA_CONTEUDO_DIVERGENTE` (FR-011).
    2. Validar os produtos (R-07).
    3. Gerar o número (R-05), criar o `Pedido`, montar o DTO e o registro, e gravar tudo
       junto.
    4. Se a gravação violar a chave primária de `ChavesIdempotencia`, outra requisição com
       a mesma chave venceu a corrida. Descartar as alterações e reler o registro:
       - mesma impressão: devolve a resposta dela;
       - impressão diferente: 409 divergente;
       - registro ainda não visível: 409 `IDEMPOTENCIA_EM_PROCESSAMENTO` (FR-012).
  - **Tradução do erro**: o `ArmazenamentoPedidos` reconhece a violação pelo nome
    `PK_ChavesIdempotencia` na mensagem do SQL Server. Para isso, `ErrosBancoDados` ganha
    uma sobrecarga com o nome da restrição. A violação vira
    `ChaveIdempotenciaRegistradaException` (Application).
- **Justificativa**:
  - No isolamento padrão do SQL Server (`READ COMMITTED`, sem snapshot), a segunda
    inserção espera a primeira transação terminar e então falha. Por isso, depois da
    violação, o registro vencedor já está visível quase sempre.
  - O 409 "em processamento" fica para o caso raro em que o vencedor ainda não aparece.
    O contrato v1 permite as duas respostas.
- **Alternativas**:
  - Trava de aplicação (`sp_getapplock`) por chave. Rejeitada: mais complexa, e a chave
    primária já garante a unicidade.
  - Gravar um registro "em processamento" antes de criar o pedido. Rejeitada: exige uma
    gravação a mais e limpeza de registros órfãos quando a criação falha.

## R-05. Número do pedido

- **Decisão**:
  - Uma `SEQUENCE` do SQL Server, `SequenciaNumeroPedido` (`bigint`, começa em 1),
    criada pela migration (`HasSequence`).
  - O `GeradorNumeroPedido` lê `NEXT VALUE FOR` e monta
    `PED-{AAAAMMDD da data UTC}-{valor % 1_000_000:D6}`.
  - O índice único `IX_Pedidos_Numero` (REQ-04) continua sendo a garantia final.
- **Justificativa**:
  - A sequência é atômica e não bloqueia: criações simultâneas nunca recebem o mesmo
    valor (FR-005, SC-003).
  - Uma colisão só aconteceria com um milhão de pedidos no mesmo dia, o que está fora da
    escala do projeto. Mesmo assim, o índice único a impediria.
- **Alternativas**:
  - Contador diário numa tabela, com trava. Rejeitada: serializa as criações.
  - Número aleatório com nova tentativa. Rejeitada: a colisão é provável com volume, e a
    nova tentativa complica o fluxo.

## R-06. Tabela de transições no domínio

- **Decisão**: `Domain/Pedidos/TransicoesStatusPedido` (estática) com dois métodos:
  - `PermitidaParaOperacao(de, para)`: as cinco linhas do contrato para Admin e
    OperadorLogistica;
  - `PermitidaParaCliente(de, para)`: só `Pendente → Cancelado`.

  O serviço decide o papel do usuário:
  - Com Admin ou OperadorLogistica entre os perfis, vale a operação.
  - Só com Cliente:
    - pedido de outra pessoa recebe 404;
    - transição diferente de `Pendente → Cancelado` recebe 403, mesmo que também fosse
      inválida para a operação (contrato v1).
  - Para a operação, uma transição fora da tabela recebe 409
    `PEDIDO_TRANSICAO_INVALIDA`, com `detail` "O pedido está em {atual} e não pode passar
    para {solicitado}.".
- **Justificativa**: a regra fica testável de forma unitária, linha a linha (SC-004), e os
  workers dos próximos requisitos podem reutilizá-la.
- **Alternativas**: validar a transição em `Pedido.MudarStatus`. Rejeitada: o REQ-04
  decidiu que o método não valida, e o perfil não é conceito do pedido.

## R-07. Validação de produtos na criação

- **Decisão**:
  - Uma consulta lê todos os produtos do pedido.
  - **Inexistente**: produto ausente na consulta.
  - **Indisponível**: `Ativo == false` ou `QuantidadeEstoque < quantidade` (clarify).
  - **Resposta**: um único 422, com `PRODUTO_INEXISTENTE` se houver algum produto
    inexistente e `PRODUTO_INDISPONIVEL` nos demais casos. O `detail` lista os
    identificadores com problema, em ordem (FR-003).
  - A leitura é sem rastreamento, e o estoque não é alterado (FR-004).
- **Justificativa**: uma ida ao banco por criação, e uma mensagem que permite corrigir
  tudo de uma vez.

## R-08. Consulta e visibilidade

- **Decisão**:
  - `ObterAsync` lê o pedido sem rastreamento.
  - Pedido inexistente, ou Cliente sem Admin/OperadorLogistica que não é o dono, recebem
    a mesma `Falha` de `TipoFalha.NaoEncontrado`, com título "Pedido não encontrado."
    (FR-015).
  - A resposta é o estado atual do pedido (FR-014).
- **Justificativa**: o mesmo objeto de falha garante respostas idênticas nos dois casos.

## R-09. Mudança de status com reavaliação

- **Decisão**: um laço de até 3 tentativas. Em cada uma:
  1. Ler o pedido com `paraAlterar`.
  2. Aplicar as regras de R-06. Uma recusa encerra o laço.
  3. Chamar `pedido.MudarStatus(novo, usuarioId, motivo?.Trim(), agora)` e gravar.
  4. Em `ConflitoConcorrenciaException`, chamar `DescartarAlteracoes()` e tentar de novo.

  Depois de 3 conflitos seguidos, a resposta é 409 `PEDIDO_TRANSICAO_INVALIDA`, com
  `detail` "O pedido foi alterado por outra operação; tente novamente." (FR-019).
- **Justificativa**:
  - A concorrência otimista do REQ-04 detecta o conflito.
  - A reavaliação aplica a tabela sobre o estado real: o cancelamento de um pedido que
    acabou de virar `Processando` vira 403 para o Cliente, e não uma sobrescrita.

## R-10. Remoção do provisório e dos 501

- **Decisão**:
  - Remover `API/Provisorio/ServicoNaoImplementado.cs` e a pasta `Provisorio`.
  - Remover `TipoFalha.NaoImplementado`, o 501 de `ExtensoesResultado` e o título 501 de
    `ConfiguracaoProblemDetails`. Nenhuma rota responde mais 501, e o 501 nunca fez parte
    do contrato v1.
  - Registrar `ServicoPedidos`, `ArmazenamentoPedidos` e `GeradorNumeroPedido` com tempo
    de vida `scoped` (o mesmo do `DbContext`).
  - Os três testes do REQ-03 que usavam o 501 como "passou pela autorização" passam a usar
    o `ServicoPedidosFalso`, que devolve sucesso, ou a afirmar "nem 401 nem 403":
    - `AutorizacaoComTokensReaisTestes`;
    - `EmissorTokenTestes`;
    - `LoginComBancoTestes`.

    O teste de conformidade que proíbe 501 no documento continua valendo.
- **Justificativa**: FR-022. Sem o falso, os testes sem banco tentariam conectar numa
  porta fechada e ficariam lentos.

## R-11. Eventos de log

- **Decisão**: `Application/Pedidos/EventosPedidos.cs` (`LoggerMessage`), com os eventos:
  - 4001 `PedidoCriado`: `PedidoId`, `Numero`, `QuantidadeItens` e `ValorTotal`;
  - 4002 `PedidoRepetido`: `PedidoId`, para a repetição idempotente;
  - 4003 `StatusPedidoAlterado`: `PedidoId`, `StatusAnterior`, `StatusNovo` e
    `PapelAutor` (`Operacao` ou `Cliente`).

  Nenhum evento traz e-mail, token nem corpo da requisição (FR-020).
- **Justificativa**: segue o padrão dos eventos de autenticação do REQ-03 (série 3000).

## R-12. Testes

- **Decisão**:
  - **Unitários** (`Testes.Unitarios/Pedidos/`), com um armazenamento em memória e um
    gerador falsos:
    - `ServicoPedidosTestes`: criação, 422, idempotência (repetição, divergente, corrida
      com os três desfechos), consulta (dono, terceiro, operação), status (tabela, 403,
      404, reavaliação, limite de 3);
    - `ImpressaoConteudoTestes`.

    Também `Testes.Unitarios/Dominio/TransicoesStatusPedidoTestes.cs`, com todas as
    combinações de status (6 × 6) para a operação e para o Cliente (SC-004).
  - **Integração com Testcontainers** (`Testes.Integracao/Pedidos/`), por HTTP, com tokens
    do login e o catálogo inicial:
    - `CriacaoPedidoComBancoTestes`: US1 e US2;
    - `ConcorrenciaIdempotenciaTestes`: SC-002, 20 requisições simultâneas × 10;
    - `NumeroPedidoTestes`: SC-003, 50 simultâneas;
    - `ConsultaPedidoComBancoTestes`: US3;
    - `StatusPedidoComBancoTestes`: US4 e a corrida de status;
    - `DesempenhoCriacaoTestes`: SC-005, p95 de 40 criações após aquecimento.
  - **Testes que já existem**: o `ModeloSincronizadoTestes` passa a esperar a terceira
    migration, e os testes do R-10 são ajustados.
- **Justificativa**: Princípio V. Os perfis Admin e OperadorLogistica vêm dos usuários
  iniciais configurados na `FabricaApi`, como no REQ-03.

## R-13. Pacotes

- **Decisão**: nenhum pacote novo. `System.Security.Cryptography` (SHA-256) e
  `System.Text.Json` fazem parte do .NET.
