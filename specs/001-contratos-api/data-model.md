# Data Model: Contratos da API v1 (REQ-01)

**Data**: 2026-10-21 | **Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/openapi-v1.yaml](./contracts/openapi-v1.yaml)

Este REQ define os **modelos de contrato** (DTOs). As entidades persistidas (Usuario,
Produto, Pedido, ItemPedido, Pagamento, OrdemEntrega) são modeladas no REQ-04. Os tipos
abaixo ficam na camada Application, no namespace `PedidoFulfillment.Application.Contratos`,
exceto `StatusPedido` e `PerfilUsuario`, que pertencem ao Domain.

Os campos JSON usam camelCase. Os tipos seguem o JSON Schema do contrato.

## Enumerações (Domain)

### StatusPedido

`Pendente`, `Processando`, `ProntoParaEnvio`, `Despachado`, `Cancelado`, `PagamentoRejeitado`.
Trafega como texto (FR-021).

### PerfilUsuario

`Cliente`, `Admin`, `OperadorLogistica`.

## Autenticação

### RegistrarUsuarioRequestDTO

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| nome | string | sim | 2–100 caracteres contados após remover espaços das pontas (`nome.Trim()`); o aparo definitivo é do serviço (REQ-03) |
| email | string (email) | sim | formato válido, até 256 caracteres; comparado sem diferenciar maiúsculas |
| senha | string (password) | sim | 8–128 caracteres, ≥1 maiúscula, ≥1 minúscula, ≥1 dígito |

### UsuarioResponseDTO

| Campo | Tipo | Obrigatório |
|-------|------|-------------|
| id | uuid | sim |
| nome | string | sim |
| email | string (email) | sim |
| perfis | PerfilUsuario[] | sim (≥1) |

### LoginRequestDTO

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| email | string (email) | sim | formato válido |
| senha | string (password) | sim | não vazio |

### LoginResponseDTO

| Campo | Tipo | Obrigatório | Observação |
|-------|------|-------------|------------|
| tokenAcesso | string | sim | JWT assinado |
| tipoToken | string | sim | sempre `Bearer` |
| expiraEm | date-time (UTC) | sim | instante de expiração |
| perfis | PerfilUsuario[] | sim | perfis presentes no token |

## Pedidos

### CriarPedidoRequestDTO

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| itens | ItemPedidoDTO[] | sim | 1–50 itens; `produtoId` único na lista |

Cabeçalho obrigatório: `X-Idempotency-Key` (UUID). O cliente **não** envia preço, total,
status nem identificador do usuário (FR-016).

### ItemPedidoDTO (entrada)

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| produtoId | uuid | sim | diferente do UUID vazio |
| quantidade | int32 | sim | 1–999 |

### ItemPedidoResponseDTO (saída)

| Campo | Tipo | Obrigatório | Observação |
|-------|------|-------------|------------|
| produtoId | uuid | sim | |
| nomeProduto | string | sim | nome no momento da criação |
| quantidade | int32 | sim | |
| precoUnitario | decimal (2 casas; `number`/`decimal` no contrato) | sim | preço do cadastro no momento da criação |
| subtotal | decimal (2 casas) | sim | `precoUnitario × quantidade` |

### PedidoResponseDTO

| Campo | Tipo | Obrigatório | Observação |
|-------|------|-------------|------------|
| id | uuid | sim | |
| numero | string | sim | padrão `PED-AAAAMMDD-NNNNNN` |
| clienteId | uuid | sim | usuário dono, vindo do token |
| status | StatusPedido | sim | `Pendente` na criação |
| itens | ItemPedidoResponseDTO[] | sim | ≥1 |
| valorTotal | decimal (2 casas) | sim | soma dos subtotais |
| moeda | string | sim | sempre `BRL` |
| criadoEm | date-time (UTC) | sim | |
| atualizadoEm | date-time (UTC) | sim | |

### AtualizarStatusPedidoRequestDTO

| Campo | Tipo | Obrigatório | Regras |
|-------|------|-------------|--------|
| status | StatusPedido | sim | não pode ser `PagamentoRejeitado` (400) |
| motivo | string | condicional | até 500 caracteres; obrigatório se `status = Cancelado` |

## Ciclo de vida do pedido (FR-020)

```text
Pendente ──► Processando ──► ProntoParaEnvio ──► Despachado
   │              │                 │
   └──────────────┴─────────────────┴──► Cancelado

Pendente ──► PagamentoRejeitado   (somente fluxo de pagamento; nunca via PATCH)
```

| De | Para | Perfis |
|----|------|--------|
| Pendente | Processando | Admin, OperadorLogistica |
| Processando | ProntoParaEnvio | Admin, OperadorLogistica |
| ProntoParaEnvio | Despachado | Admin, OperadorLogistica |
| Pendente, Processando, ProntoParaEnvio | Cancelado | Admin, OperadorLogistica |
| Pendente | Cancelado | Cliente dono do pedido |

Estados finais: `Despachado`, `Cancelado`, `PagamentoRejeitado`. Qualquer outra transição
(inclusive para o mesmo status) → 409 `PEDIDO_TRANSICAO_INVALIDA`. Um Cliente que tenta
transição diferente de Pendente→Cancelado no próprio pedido → 403. Um Cliente que tenta
alterar pedido de outro → 404.

## Erros (RFC 7807)

### ProblemDetails (base para todos os 4xx/5xx)

| Campo | Tipo | Obrigatório |
|-------|------|-------------|
| type | string (uri-reference) | sim |
| title | string | sim |
| status | int32 | sim |
| detail | string | não |
| instance | string | sim (caminho da requisição) |
| traceId | string | sim |

### ValidacaoProblemDetails (400)

ProblemDetails + `erros`: objeto `{ "<campo>": ["mensagem", ...] }`. As chaves usam o caminho
do campo em camelCase (ex.: `itens[0].quantidade`) ou o nome do cabeçalho
(`X-Idempotency-Key`).

### NegocioProblemDetails (409, 422)

ProblemDetails + `codigo` (string estável).

### Catálogo de códigos de erro

| codigo | Status | Operação | Situação |
|--------|--------|----------|----------|
| `USUARIO_EMAIL_JA_CADASTRADO` | 409 | register | e-mail já existe |
| `IDEMPOTENCIA_CONTEUDO_DIVERGENTE` | 409 | criar pedido | mesma chave com corpo diferente |
| `IDEMPOTENCIA_EM_PROCESSAMENTO` | 409 | criar pedido | requisição com a mesma chave ainda em andamento |
| `PRODUTO_INEXISTENTE` | 422 | criar pedido | `produtoId` não encontrado |
| `PRODUTO_INDISPONIVEL` | 422 | criar pedido | produto inativo ou sem estoque |
| `PEDIDO_TRANSICAO_INVALIDA` | 409 | atualizar status | transição fora da tabela |

Novos códigos podem ser adicionados (mudança compatível). Códigos existentes não podem
ser renomeados nem reaproveitados (Princípio II).

## Chave de idempotência (regra de contrato)

- Escopo: par (usuário, chave).
- Retenção mínima: 24 horas a partir da primeira requisição.
- A impressão do conteúdo é calculada sobre o corpo normalizado (itens ordenados por
  `produtoId`). Isso permite distinguir "mesmo conteúdo" de "conteúdo divergente".
- O armazenamento é definido no REQ-05 (recomendação: tabela no SQL Server).
