# Research: Contratos da API v1 (REQ-01)

**Data**: 2026-10-21 | **Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md)

Cada decisão segue o formato Decisão / Justificativa / Alternativas.

## R-01. Fonte da verdade do contrato

- **Decisão**: o arquivo `contracts/openapi-v1.yaml`, escrito à mão e aprovado nesta spec,
  é a fonte da verdade. A API gera o próprio documento OpenAPI em tempo de execução, e
  um teste de conformidade compara os dois (FR-024).
- **Justificativa**: o Princípio I exige que o contrato exista e seja aprovado antes do
  código. Um YAML revisável em PR é legível por pessoas e por máquinas. Gerar só pelo
  código faria o contrato "nascer" da implementação, invertendo o SDD.
- **Alternativas**: (a) só code-first: rejeitada, porque não há artefato aprovado antes
  do código; (b) gerar o código a partir do YAML (NSwag/OpenAPI Generator): rejeitada,
  porque adiciona ferramenta de build e gera código fora das convenções em português.

## R-02. Geração do documento e versão do OpenAPI

- **Decisão**: `Microsoft.AspNetCore.OpenApi` (geração nativa do .NET 10) publicando
  `/openapi/v1.json` no formato **OpenAPI 3.1**, que é o padrão do .NET 10.
- **Justificativa**: é o pacote oficial e mantido pela Microsoft, sem dependências de
  terceiros. O 3.1 é compatível com JSON Schema, que a ferramenta de UI suporta. A issue
  cita "OpenAPI 3.0" de forma genérica; caso algum consumidor exija 3.0, basta configurar
  `OpenApiVersion`, sem mudar o contrato.
- **Alternativas**: Swashbuckle: rejeitada, porque saiu do template oficial a partir do
  .NET 9 e duplica o que o pacote nativo faz.

## R-03. Interface de navegação do contrato

- **Decisão**: `Scalar.AspNetCore`, em `/scalar/v1`, apontando para **`/openapi/v1.yaml`**,
  que é o contrato aprovado (`contracts/openapi-v1.yaml`) servido como arquivo estático.
  O documento gerado pelo código (`/openapi/v1.json`) continua publicado, mas serve só
  para o teste de conformidade (R-11). Assim os exemplos, os perfis por operação, a
  descrição de idempotência e a tabela de transições (FR-004/FR-006) aparecem na UI sem
  duplicar texto no código. A conformidade garante que o código não diverge do que a UI
  mostra.
- **Justificativa**: é a UI recomendada na documentação do .NET 9/10 para o documento
  nativo, tem suporte a OpenAPI 3.1 e a autenticação Bearer para testar operações.
- **Alternativas**: `Swashbuckle.AspNetCore.SwaggerUI` apontando para o documento nativo:
  viável, mas o suporte a 3.1 é mais recente. Sem UI: rejeitada, porque a User Story 1
  pede o contrato navegável por pessoas.

## R-04. Exposição por ambiente

- **Decisão**: `/openapi/v1.yaml`, `/openapi/v1.json` e `/scalar/v1` são mapeados, sem
  autenticação, apenas nos ambientes `Development` e `Testes`. Em produção não existem: sem token respondem 401 e com token válido respondem 404, porque a `FallbackPolicy` exige autenticação em qualquer endereço (FR-007a).
- **Justificativa**: atende a premissa da spec e o Princípio III da constituição v1.1.0,
  que permite documentação pública somente fora de produção.
- **Alternativas**: expor em produção com autenticação: adiado até haver demanda.

## R-05. Endpoints antes das regras de negócio

- **Decisão**: as 5 rotas são mapeadas em REQ-01 com todos os metadados (DTOs, códigos de
  resposta, autorização). Os handlers só delegam a interfaces da camada Application
  (`IServicoAutenticacao`, `IServicoPedidos`). Neste REQ elas têm uma implementação
  provisória (`ServicoNaoImplementado`) que devolve **501** em `ProblemDetails`.
- **Justificativa**: o documento gerado pelo código precisa das rotas para o teste de
  conformidade. O endpoint continua só roteando, como exige a constituição. O 501 é
  transitório: não faz parte do contrato v1 e some à medida que REQ-03 e REQ-05 entregam
  as implementações.
- **Alternativas**: não mapear rotas e servir só o YAML estático: rejeitada, porque não
  haveria conformidade nem testes de 401/403 até o REQ-05.

## R-06. Autenticação e autorização no REQ-01

- **Decisão**: configurar já em REQ-01 a **validação** de JWT Bearer
  (`Microsoft.AspNetCore.Authentication.JwtBearer`) e as policies. A **emissão** de
  tokens e o Identity ficam no REQ-03. Os testes geram tokens com uma chave de teste.
- **Policies**:

  | Policy | Perfis | Uso |
  |--------|--------|-----|
  | `PoliticaCliente` | Cliente | `POST /pedidos` |
  | `PoliticaConsultaPedido` | Cliente, Admin, OperadorLogistica | `GET /pedidos/{id}` |
  | `PoliticaAtualizacaoStatusPedido` | Cliente, Admin, OperadorLogistica | `PATCH /pedidos/{id}/status` |

  A regra de dono do pedido e a matriz de transições por perfil (FR-020) são regras de
  negócio. Por isso ficam no serviço de aplicação, e não na policy.
- **Justificativa**: o Princípio V exige testes de 401 e 403 em todo endpoint protegido.
  Sem a validação de token, as rotas mapeadas estariam abertas.
- **Alternativas**: liberar as rotas até o REQ-03: rejeitada, porque viola o Princípio III.

## R-07. Formato padrão de erro (RFC 7807)

- **Decisão**: `AddProblemDetails()` com `CustomizeProblemDetails` para incluir `traceId` e
  `instance` em todos os erros, e `UseExceptionHandler` + `UseStatusCodePages` para que
  qualquer 4xx/5xx sem corpo vire `ProblemDetails`. Para 401 e 403, os eventos
  `OnChallenge` e `OnForbidden` do JwtBearer escrevem `ProblemDetails` via
  `IProblemDetailsService`. Erros 409/422 recebem a extensão `codigo` (FR-011).
- **Justificativa**: usa só recursos nativos. Por padrão o JwtBearer devolve 401/403 sem
  corpo, o que viola o FR-009.
- **Alternativas**: middleware próprio de erros: rejeitada, porque duplica recursos nativos.

## R-08. Validação de entrada e campo `erros`

- **Decisão**: um filtro de endpoint próprio, `FiltroValidacao<T>`, que valida o DTO com
  `System.ComponentModel.DataAnnotations` (atributos + `IValidatableObject`) e devolve 400
  com `ValidacaoProblemDetails`, cujo campo `erros` mapeia campo → mensagens (FR-010).
  JSON mal formado, tipo errado, enum desconhecido e cabeçalho ou rota sem formato de UUID
  geram `BadHttpRequestException`, que vira 400 `ValidacaoProblemDetails` **com `erros`** (FR-010). A chave é o parâmetro (`id`, `X-Idempotency-Key`), o campo do JSON (`status`, extraído do `JsonException.Path`) ou `""` para corpo ausente ou ilegível. Para isso,
  `RouteHandlerOptions.ThrowOnBadRequest = true` é configurado **em todos os ambientes**.
  O padrão do framework só liga essa opção em Development; nos outros ambientes a falha de
  binding devolveria 400 sem corpo. A mensagem da exceção nunca é copiada para a resposta.
- **Justificativa**: a validação nativa do .NET 10 (`AddValidation`) escreve
  `HttpValidationProblemDetails` com o campo `errors`, mas a spec aprovada pede `erros`.
  O filtro usa só recursos do framework, sem pacote novo.
- **Alternativas**: (a) FluentValidation: rejeitada, porque é uma dependência nova
  desnecessária para as regras atuais; (b) `AddValidation` + renomear para `errors`:
  exigiria mudar a spec aprovada.

## R-09. Serialização

- **Decisão**: `System.Text.Json` em camelCase com `JsonStringEnumConverter` global. No
  contrato, os valores monetários usam `type: number, format: decimal`. O comparador do
  teste aceita `double` gerado pelo .NET como equivalente. Assim
  enums viajam como texto (FR-021) e o documento OpenAPI exibe enums com os nomes. Datas
  em `DateTimeOffset` UTC (ISO 8601). Valores monetários em `decimal`, arredondados a 2
  casas no serviço (FR-022). Campos desconhecidos são ignorados (padrão do STJ).
- **Alternativas**: Newtonsoft.Json: rejeitada, porque é uma dependência desnecessária.

## R-10. Identificadores

- **Decisão**: `id`, `clienteId`, `produtoId` e `X-Idempotency-Key` são UUID (`Guid`).
  `numero` do pedido segue o padrão `PED-AAAAMMDD-NNNNNN` (sequencial diário, gerado no
  REQ-05). As rotas **não** usam a restrição `{id:guid}`, para que um formato inválido
  gere 400, e não 404 (spec, User Story 4).
- **Alternativas**: id inteiro sequencial: rejeitada, porque expõe volume de pedidos e
  facilita enumeração.

## R-11. Teste de conformidade do contrato

- **Decisão**: um teste de integração sobe a API com `WebApplicationFactory`, lê
  `/openapi/v1.json` e o compara com `contracts/openapi-v1.yaml`, lido com
  `Microsoft.OpenApi.YamlReader`. Compara: conjunto de caminhos + métodos, códigos de
  resposta por operação, exigência de segurança, nomes dos schemas, propriedades,
  tipos/formatos, obrigatoriedade e valores de enum. Descrições e exemplos não entram na
  comparação.
- **Justificativa**: atende ao FR-024 e ao SC-005. Usa o mesmo modelo de objetos
  (`Microsoft.OpenApi`) que o gerador nativo, sem parser próprio.
- **Alternativas**: comparação textual dos arquivos: rejeitada, porque é frágil a ordem e
  formatação.
- **Compatibilidade entre versões (SC-006/FR-023)**: um workflow do GitHub Actions roda o
  `oasdiff breaking` entre o `contracts/openapi-v1.yaml` da `main` e o do PR, e falha em
  qualquer mudança incompatível. O oasdiff é uma ferramenta de CI, não um pacote NuGet.

## R-12. Bibliotecas de teste

- **Decisão**: xUnit v3, `FluentAssertions` **fixado na série 7.x** (licença Apache 2.0) e
  `Microsoft.AspNetCore.Mvc.Testing`. Testcontainers não é necessário neste REQ, porque
  não há banco nem fila.
- **Justificativa**: a constituição exige FluentAssertions. A partir da versão 8 ele passou
  a ter licença comercial (Xceed), então a série 7.x evita custo e risco legal.
  ⚠️ Se a equipe tiver licença da v8, basta atualizar.
- **Alternativas**: Shouldly/AwesomeAssertions: exigiriam emenda à constituição.

## R-13. Ambiente de desenvolvimento

- **Constatação**: o SDK do .NET não está instalado nesta máquina (`dotnet` não
  encontrado em 2026-10-21). É pré-requisito para a implementação (ver
  [quickstart.md](./quickstart.md)).
