# Research: Listagem e paginação de pedidos por perfil (REQ-21)

**Spec**: [spec.md](./spec.md) | **Plan**: [plan.md](./plan.md) | **Data**: 2026-11-03

Decisões de implementação que a spec deixou para o plan. Cada uma traz a decisão, o motivo e
as alternativas descartadas.

## R-01 — Ordem da listagem e desempate

**Decision**: ordenar por `CriadoEm` decrescente e, no empate, por `Numero` decrescente.

**Rationale**: o `Numero` (`PED-yyyyMMdd-NNNNNN`) é único (índice único já existe) e vem de uma
sequência do banco, então dá uma ordem total e determinística, e a comparação de `string` é
traduzida pelo EF Core para SQL (`string.Compare(a, b) < 0`). O desempate só é usado entre
pedidos criados no mesmo instante (FR-016).

**Alternatives considered**:
- Desempate por `Id` (UUID v7): o SQL Server ordena `uniqueidentifier` por uma regra própria de
  bytes, diferente da ordem do UUID v7, e o EF Core não traduz `<`/`>` entre `Guid` de forma
  portável. Daria uma ordem estável, mas difícil de explicar e de testar.
- Só `CriadoEm`: não é único; dois pedidos no mesmo instante poderiam se repetir ou se perder
  na troca de página.

## R-02 — Paginação por chave (keyset), nos dois sentidos

**Decision**: o cursor guarda a posição `(CriadoEm, Numero)` do último item da página (para a
próxima) ou do primeiro (para a anterior). A próxima página busca os pedidos estritamente
"depois" dessa posição na ordem decrescente; a anterior busca os estritamente "antes", em
ordem crescente, e inverte o resultado. Busca-se `tamanho + 1` itens para saber se há mais
uma página naquele sentido, sem contar o total.

- Página vinda de um cursor da **próxima**: tem cursor da anterior (a partir do primeiro item)
  e tem cursor da próxima se sobrou o item extra.
- Página vinda de um cursor da **anterior**: tem cursor da próxima (a partir do último item) e
  tem cursor da anterior se sobrou o item extra.
- Primeira página (sem cursor): sem cursor da anterior; cursor da próxima se sobrou o extra.
- Página vazia vinda de um cursor (os pedidos à frente saíram do filtro): 200 com a lista
  vazia; mantém o cursor do sentido oposto, construído a partir da posição do cursor recebido,
  para o cliente poder voltar.

**Rationale**: atende FR-013 a FR-016 e SC-002/SC-004: a consulta é sempre um `TOP (n+1)` num
índice, com o mesmo custo em qualquer profundidade, e um pedido criado no meio da navegação
nunca desloca a posição já visitada.

**Alternatives considered**: `OFFSET/FETCH` com o número da página escondido no cursor — repete
ou pula pedidos com inserções e fica mais lento nas páginas fundas, o que a clarify descartou.

## R-03 — Formato e integridade do cursor

**Decision**: o cursor é `base64url(carga) + "." + base64url(mac)`:

- `carga`: JSON compacto `{"v":1,"s":"p"|"a","c":"<CriadoEm ISO 8601 com fuso>","n":"<Numero>"}`
  (`s` = sentido: próxima ou anterior);
- `mac`: os primeiros 16 bytes de `HMAC-SHA256(chave, carga ‖ usuarioId ‖ filtros normalizados)`.

Os filtros normalizados são os status ordenados e sem repetição, os quatro limites de período em
UTC e o `clienteId`, num texto canônico. O tamanho da página fica de fora, para que o cliente
possa trocá-lo no meio da navegação (Edge Cases da spec). Na leitura, um cursor com formato
inválido, versão desconhecida ou `mac` diferente (comparado com `CryptographicOperations.FixedTimeEquals`)
dá 400 `"O cursor é inválido para esta consulta; recomece da primeira página."`, sem dizer o
motivo (FR-017).

**Rationale**: o `mac` amarra o cursor ao usuário e aos filtros e torna a adulteração
detectável, como a spec exige. A segurança do escopo **não** depende dele: o filtro do Cliente
pelo `ClienteId` do token é sempre aplicado no servidor (FR-004), então um cursor forjado nunca
mostraria pedido de outro Cliente. A carga não tem dado pessoal, só uma data e o número de um
pedido que o usuário já viu.

**Alternatives considered**:
- Cursor sem `mac` (só base64): simples, mas a adulteração passaria (FR-017).
- ASP.NET Core Data Protection: as chaves ficam por máquina por padrão; com mais de uma
  instância da API, um cursor gerado numa seria recusado na outra, e compartilhar o anel de
  chaves exigiria um armazenamento novo.
- Cifrar a carga (AES-GCM): não há segredo na carga; só a integridade importa.

## R-04 — Chave do HMAC do cursor

**Decision**: derivar a chave com HKDF-SHA256 da `Jwt:ChaveAssinatura`, com o `info`
`"oms/listagem-pedidos/cursor/v1"`, uma vez na inicialização (`HKDF.DeriveKey`, da biblioteca
padrão).

**Rationale**: a `Jwt:ChaveAssinatura` já é obrigatória, tem pelo menos 32 bytes (validado na
inicialização) e é a mesma em todas as instâncias da API; o HKDF dá uma chave independente, sem
reaproveitar a chave do JWT diretamente e sem configuração nova. A troca da chave do JWT (issue
#60) só invalida os cursores em uso: a resposta é 400 e o cliente recomeça da primeira página,
o que fica registrado em Limitações no fluxo 3.

**Alternatives considered**: uma configuração própria (`Pedidos:Listagem:ChaveCursor`) — mais
um segredo obrigatório para gerar, distribuir e validar em todos os ambientes, sem ganho de
segurança para um cursor que não protege escopo.

## R-05 — Parâmetros de consulta e validação com todos os erros

**Decision**: a rota recebe um `ListarPedidosRequestDTO` com `[AsParameters]`, com os campos
como texto (`string[]? Status`, `string? CriadoDe`, `CriadoAte`, `AtualizadoDe`, `AtualizadoAte`,
`ClienteId`, `Tamanho`, `Cursor`) e `IValidatableObject`. O `FiltroValidacao<T>` existente valida
e devolve 400 com todos os erros em `erros` (FR-021, FR-022). A conversão para tipos fica num
método do próprio DTO, usado depois da validação.

Regras:

| Parâmetro | Regra | Mensagem (400) |
|-----------|-------|----------------|
| `status` | Cada valor é um nome de `StatusPedido` (sem diferenciar maiúsculas); repetidos contam uma vez | `O status '{valor}' não existe.` |
| `criadoDe`, `criadoAte`, `atualizadoDe`, `atualizadoAte` | ISO 8601 com fuso (`Z` ou `±hh:mm`) | `O parâmetro {nome} deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00.` |
| Período | Início ≤ fim, quando os dois existem | `O início do período de criação deve ser anterior ou igual ao fim.` (e o equivalente para última alteração) |
| `clienteId` | UUID | `O parâmetro clienteId deve ser um UUID.` |
| `tamanho` | Inteiro de 1 a 100; padrão 20 | `O tamanho deve ser um número entre 1 e 100.` |
| `cursor` | Ver R-03 (validado no serviço, porque depende do usuário) | `O cursor é inválido para esta consulta; recomece da primeira página.` |

**Rationale**: com tipos (`DateTimeOffset?`, `int?`) o Minimal API recusaria o primeiro valor
inválido antes do filtro, com uma mensagem genérica e um erro só; e a exigência de fuso só
pode ser conferida no texto original. O padrão de validação por DTO e filtro já é o da casa.
O documento OpenAPI gerado precisa mostrar os tipos certos (`date-time`, `uuid`, `int32`); isso
é feito com atributos ou com o `TransformadorFormatosOpenApi`, e o teste de conformidade
(`ComparadorContrato`) confere.

**Alternatives considered**: parâmetros tipados e um `IProblemDetailsService` para o erro de
binding — dá um erro por vez e não confere o fuso.

## R-06 — Escopo por perfil e o 403 do filtro por Cliente

**Decision**: reaproveitar a `PoliticaConsultaPedido` (os três perfis; sem perfil → 403,
FR-003). No serviço, `EhOperacao(perfis)` decide o escopo (FR-005, FR-007); se não for operação,
o `ClienteId` do token entra sempre no filtro (FR-004) e o parâmetro `clienteId`, se informado,
dá `TipoFalha.SemPermissao` com `"Operação não permitida."` / `"O filtro por cliente é exclusivo
da operação."` (FR-006), antes de qualquer consulta ao banco.

**Rationale**: é a mesma regra da consulta por id; o 403 vem antes da validação do cursor para
que a resposta não dependa do cursor.

## R-07 — Consulta, projeção e índices

**Decision**:
- Um método novo em `IArmazenamentoPedidos`, `ListarResumosAsync(FiltroListagemPedidos, PosicaoCursor?, int quantidade, ct)`,
  com `AsNoTracking` e projeção direta para o resumo (incluindo `Itens.Count()` como
  subconsulta), sem carregar itens, histórico, pagamento nem entrega (FR-019).
- Dois índices novos na migration `ListagemPedidos`:
  - `IX_Pedidos_CriadoEm_Numero` (`CriadoEm DESC, Numero DESC`, incluindo `Status`,
    `AtualizadoEm`, `ValorTotal`, `Moeda`, `ClienteId`) para a operação;
  - `IX_Pedidos_ClienteId_CriadoEm_Numero` (`ClienteId, CriadoEm DESC, Numero DESC`, com as
    mesmas colunas incluídas), que substitui o `IX_Pedidos_ClienteId_CriadoEm` atual (prefixo
    dele, sem outro uso).
- Os filtros de status e de última alteração são resíduos sobre o índice; o índice de `Status`
  já existe.

**Rationale**: atende SC-004 (até 1 s com 10.000 pedidos, sem piorar com a profundidade): a
consulta lê `n+1` linhas em ordem de índice. Com filtros muito seletivos sobre muitos pedidos o
SQL Server pode varrer mais linhas, o que não afeta a meta no volume da spec.

**Alternatives considered**: `Include` dos itens e projeção em memória — carrega o que o resumo
não mostra.

## R-08 — Resumo do pedido

**Decision**: `ResumoPedidoResponseDTO` com `id`, `numero`, `clienteId`, `status`, `valorTotal`,
`moeda`, `quantidadeItens`, `criadoEm` e `atualizadoEm`. `quantidadeItens` é o número de linhas
de item do pedido (produtos distintos), não a soma das quantidades, como no log `PedidoCriado`.
Os nomes e os formatos são os mesmos do `PedidoResponseDTO` (FR-018, FR-020).

**Rationale**: o consumidor reaproveita o que já conhece da consulta por id; `moeda` entra
porque `valorTotal` sem moeda seria ambíguo e o campo já existe.

## R-09 — Resposta da página

**Decision**: `PaginaPedidosResponseDTO` com `itens` (lista de resumos), `tamanho` (o usado),
`proximo` e `anterior` (texto do cursor ou `null`). Não há `total` (clarify).

## R-10 — Log

**Decision**: evento `4006 PedidosListados`, nível `Debug`, com o perfil (`Cliente` ou
`Operacao`), os nomes dos filtros usados (não os valores: o `clienteId` é dado pessoal), o
sentido (`Inicio`, `Proxima`, `Anterior`) e a quantidade de itens (FR-024). O 403 do filtro por
Cliente e o cursor inválido não ganham log próprio: já aparecem no log de requisições.

## R-11 — Testes

**Decision**:
- **Unitários**: validação do DTO (cada regra e erros múltiplos); codificação e validação do
  cursor (ida e volta, adulteração da carga e do `mac`, outro usuário, outros filtros, tamanho
  diferente aceito); regra de escopo do serviço com armazenamento falso.
- **Integração** (SQL Server em Testcontainers): 200 para os três perfis, 401, 403 sem perfil e
  403 do Cliente com `clienteId`; escopo do Cliente; cada filtro e a combinação; navegação de
  45 pedidos nos dois sentidos; pedido criado no meio da navegação; empate de `CriadoEm`;
  cursor de outro usuário; desempenho com 10.000 pedidos (SC-004), com inserção em lote.
- **Contrato**: o `ComparadorContrato` confere a rota nova contra o `openapi-v1.yaml`, e o
  workflow `Compatibilidade do contrato` (oasdiff) roda porque o YAML muda.

## R-12 — Dependências

**Decision**: nenhum pacote novo. `HMACSHA256`, `HKDF` e `CryptographicOperations` são da
biblioteca padrão.
