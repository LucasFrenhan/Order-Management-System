# Contrato: `GET /api/v1/pedidos` (REQ-21)

**Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md)

Acréscimo ao contrato v1 em `specs/001-contratos-api/contracts/openapi-v1.yaml`: uma operação
`get` no caminho `/api/v1/pedidos`, que hoje só tem o `post`, e três schemas novos. Nada do que
existe muda (Princípio II); o workflow `Compatibilidade do contrato` (oasdiff) precisa ficar
verde.

## Trecho a acrescentar ao OpenAPI

```yaml
  /api/v1/pedidos:
    get:
      tags: [Pedidos]
      operationId: ListarPedidos
      summary: Lista os pedidos do escopo do usuário, dos mais recentes para os mais antigos.
      description: |
        Perfis autorizados: **Cliente** (somente pedidos próprios), **Admin**, **OperadorLogistica**.
        O filtro `clienteId` é exclusivo da operação: o Cliente que o informa recebe 403.

        Paginação por cursor: a resposta traz `proximo` e `anterior` (ou `null`); para navegar,
        repita a consulta com os mesmos filtros e `cursor` igual a um deles. O cursor é opaco e
        vale só para o mesmo usuário e os mesmos filtros; fora disso, a resposta é 400 e a
        navegação recomeça da primeira página. Não há total nem número de página.
      security:
        - bearerAuth: []
      parameters:
        - name: status
          in: query
          description: Um ou mais status; repita o parâmetro para mais de um.
          style: form
          explode: true
          schema:
            type: array
            items:
              $ref: '#/components/schemas/StatusPedido'
        - name: criadoDe
          in: query
          description: Início (inclusivo) do período de criação, com fuso.
          schema: { type: string, format: date-time }
        - name: criadoAte
          in: query
          description: Fim (inclusivo) do período de criação, com fuso.
          schema: { type: string, format: date-time }
        - name: atualizadoDe
          in: query
          description: Início (inclusivo) do período de última alteração, com fuso.
          schema: { type: string, format: date-time }
        - name: atualizadoAte
          in: query
          description: Fim (inclusivo) do período de última alteração, com fuso.
          schema: { type: string, format: date-time }
        - name: clienteId
          in: query
          description: Somente Admin e OperadorLogistica.
          schema: { type: string, format: uuid }
        - name: tamanho
          in: query
          description: Pedidos por página, de 1 a 100.
          schema: { type: integer, format: int32, default: 20, minimum: 1, maximum: 100 }
        - name: cursor
          in: query
          description: O `proximo` ou o `anterior` de uma resposta anterior.
          schema: { type: string }
      responses:
        '200':
          description: Página de pedidos.
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PaginaPedidosResponseDTO'
        '400':
          $ref: '#/components/responses/ErroValidacao'
        '401':
          $ref: '#/components/responses/ErroNaoAutenticado'
        '403':
          $ref: '#/components/responses/ErroSemPermissao'
        '500':
          $ref: '#/components/responses/ErroInterno'

# components/schemas
    ResumoPedidoResponseDTO:
      type: object
      required: [id, numero, clienteId, status, valorTotal, moeda, quantidadeItens, criadoEm, atualizadoEm]
      properties:
        id: { type: string, format: uuid }
        numero: { type: string, pattern: '^PED-\d{8}-\d{6}$' }
        clienteId: { type: string, format: uuid }
        status: { $ref: '#/components/schemas/StatusPedido' }
        valorTotal: { type: number, format: decimal, description: Duas casas decimais. }
        moeda: { type: string, description: Sempre "BRL". }
        quantidadeItens: { type: integer, format: int32, description: Linhas de item (produtos distintos). }
        criadoEm: { type: string, format: date-time }
        atualizadoEm: { type: string, format: date-time }
    PaginaPedidosResponseDTO:
      type: object
      required: [itens, tamanho, proximo, anterior]
      properties:
        itens:
          type: array
          items: { $ref: '#/components/schemas/ResumoPedidoResponseDTO' }
        tamanho: { type: integer, format: int32 }
        proximo: { type: ['string', 'null'], description: Cursor da próxima página; null na última. }
        anterior: { type: ['string', 'null'], description: Cursor da página anterior; null na primeira. }
```

## Exemplos

### Primeira página do Cliente

```http
GET /api/v1/pedidos?tamanho=2
Authorization: Bearer <token do Cliente>
```

```http
HTTP/1.1 200 OK
Content-Type: application/json

{ "itens": [
    { "id": "0199f1a2-…-0003", "numero": "PED-20261103-000003", "clienteId": "0199f0…", "status": "Pendente",
      "valorTotal": 49.90, "moeda": "BRL", "quantidadeItens": 1,
      "criadoEm": "2026-11-03T13:10:00+00:00", "atualizadoEm": "2026-11-03T13:10:00+00:00" },
    { "id": "0199f1a2-…-0002", "numero": "PED-20261103-000002", "clienteId": "0199f0…", "status": "Processando",
      "valorTotal": 149.70, "moeda": "BRL", "quantidadeItens": 2,
      "criadoEm": "2026-11-03T12:00:00+00:00", "atualizadoEm": "2026-11-03T12:00:05+00:00" } ],
  "tamanho": 2,
  "proximo": "eyJ2IjoxLCJzIjoicCIs….Qm9h…",
  "anterior": null }
```

### Pedidos parados em `ProntoParaEnvio` (operação)

```http
GET /api/v1/pedidos?status=ProntoParaEnvio&atualizadoAte=2026-11-02T23:59:59-03:00
Authorization: Bearer <token do OperadorLogistica>
```

## Respostas

| Situação | Resposta | Corpo |
|----------|----------|-------|
| Cliente, Admin ou OperadorLogistica | 200 | `PaginaPedidosResponseDTO`; lista vazia quando nada atende |
| Sem token ou token inválido | 401 | `ProblemDetails` |
| Autenticado sem nenhum dos três perfis | 403 | `ProblemDetails` |
| Cliente com `clienteId` (qualquer valor) | 403 | `"Operação não permitida."` / `"O filtro por cliente é exclusivo da operação."` |
| Parâmetro inválido (um ou vários) | 400 | `ValidacaoProblemDetails` com todos os erros em `erros` ([R-05](../research.md#r-05--parâmetros-de-consulta-e-validação-com-todos-os-erros)) |
| Cursor malformado, adulterado, de outro usuário ou de outros filtros | 400 | `erros.cursor`: `"O cursor é inválido para esta consulta; recomece da primeira página."` |

Ordem de avaliação: autenticação (401) → perfil (403) → validação dos parâmetros (400) →
filtro por Cliente do Cliente (403) → cursor (400) → consulta.

> Observação: a validação dos parâmetros acontece no filtro da rota, antes do serviço; por isso
> um Cliente que mande `clienteId` malformado recebe 400, e não 403. O 403 do Cliente vale para
> qualquer `clienteId` bem formado.
