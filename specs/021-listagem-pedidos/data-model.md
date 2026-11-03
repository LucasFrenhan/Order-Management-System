# Data Model: Listagem e paginação de pedidos por perfil (REQ-21)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

O requisito não cria tabela nem coluna: tudo é lido do `Pedido` que já existe. Muda só o
conjunto de índices de `Pedidos` e surgem tipos de leitura na aplicação.

## Persistência

### Tabela `Pedidos` (existente) — índices

| Índice | Colunas | Situação | Uso |
|--------|---------|----------|-----|
| `IX_Pedidos_ClienteId_CriadoEm` | `ClienteId, CriadoEm` | **Removido** | Substituído pelo seguinte, do qual era prefixo |
| `IX_Pedidos_ClienteId_CriadoEm_Numero` | `ClienteId, CriadoEm DESC, Numero DESC`, inclui `Status, AtualizadoEm, ValorTotal, Moeda` | **Novo** | Listagem do Cliente |
| `IX_Pedidos_CriadoEm_Numero` | `CriadoEm DESC, Numero DESC`, inclui `ClienteId, Status, AtualizadoEm, ValorTotal, Moeda` | **Novo** | Listagem da operação |
| `IX_Pedidos_Status` | `Status` | Mantido | Filtro por status (resíduo) |
| `IX_Pedidos_Numero` (único) | `Numero` | Mantido | Desempate da ordem (R-01) |

Migration: `ListagemPedidos` (só índices; sem perda de dados).

## Tipos de aplicação

### `FiltroListagemPedidos` (novo, valor)

O filtro já convertido e com o escopo aplicado; é também a base do texto canônico que entra no
`mac` do cursor (R-03).

| Campo | Tipo | Regra |
|-------|------|-------|
| `Status` | conjunto de `StatusPedido` | Vazio = todos; sem repetição; ordenado no texto canônico |
| `CriadoDe`, `CriadoAte` | `DateTimeOffset?` (UTC) | Inclusivos; `CriadoDe ≤ CriadoAte` |
| `AtualizadoDe`, `AtualizadoAte` | `DateTimeOffset?` (UTC) | Inclusivos; `AtualizadoDe ≤ AtualizadoAte` |
| `ClienteId` | `Guid?` | Para o Cliente, **sempre** o do token (FR-004); para a operação, o do parâmetro ou nenhum |

### `PosicaoCursor` (novo, valor)

| Campo | Tipo | Significado |
|-------|------|-------------|
| `Sentido` | `Proxima` \| `Anterior` | Para que lado navegar a partir da posição |
| `CriadoEm` | `DateTimeOffset` (UTC) | Chave 1 da ordem (R-01) |
| `Numero` | `string` | Chave 2 da ordem, desempate |

Codificação, `mac` e validação em [research R-03](./research.md#r-03--formato-e-integridade-do-cursor).
O cursor não é guardado em lugar nenhum: tudo o que é preciso para validá-lo vem nele e na
requisição.

### `CodificadorCursorPedidos` (novo, serviço)

- `Codificar(PosicaoCursor, Guid usuarioId, FiltroListagemPedidos) → string`
- `TentarDecodificar(string, Guid usuarioId, FiltroListagemPedidos, out PosicaoCursor) → bool`

Chave derivada por HKDF da `Jwt:ChaveAssinatura` (R-04). Sem estado; singleton.

## Contratos da API (DTOs)

### `ListarPedidosRequestDTO` (novo, parâmetros de consulta)

Todos opcionais. Campos como texto para validar tudo de uma vez (R-05). Regras e mensagens em
[research R-05](./research.md#r-05--parâmetros-de-consulta-e-validação-com-todos-os-erros).

| Parâmetro | Forma no contrato |
|-----------|-------------------|
| `status` | lista de `StatusPedido`, repetindo o parâmetro (`?status=Pendente&status=Processando`) |
| `criadoDe`, `criadoAte`, `atualizadoDe`, `atualizadoAte` | `date-time` com fuso |
| `clienteId` | `uuid` (só a operação) |
| `tamanho` | `int32`, 1–100, padrão 20 |
| `cursor` | texto opaco devolvido em `proximo` ou `anterior` |

### `ResumoPedidoResponseDTO` (novo)

| Campo | Tipo | Origem |
|-------|------|--------|
| `id` | `uuid` | `Pedido.Id` |
| `numero` | `string` (`PED-yyyyMMdd-NNNNNN`) | `Pedido.Numero` |
| `clienteId` | `uuid` | `Pedido.ClienteId` |
| `status` | `StatusPedido` | `Pedido.Status` |
| `valorTotal` | `decimal` (2 casas) | `Pedido.ValorTotal` |
| `moeda` | `string` (`BRL`) | `Pedido.Moeda` |
| `quantidadeItens` | `int32` | Número de linhas de item (R-08) |
| `criadoEm` | `date-time` | `Pedido.CriadoEm` |
| `atualizadoEm` | `date-time` | `Pedido.AtualizadoEm` |

Os campos coincidem em nome, tipo e valor com os do `PedidoResponseDTO` (FR-020).

### `PaginaPedidosResponseDTO` (novo)

| Campo | Tipo | Regra |
|-------|------|-------|
| `itens` | lista de `ResumoPedidoResponseDTO` | Na ordem da listagem (R-01), também quando a página veio do cursor da anterior |
| `tamanho` | `int32` | O tamanho usado |
| `proximo` | `string?` | `null` na última página (R-02) |
| `anterior` | `string?` | `null` na primeira página (R-02) |

## Regras de estado

A listagem é só leitura (FR-023): não muda status, não grava histórico, outbox nem avisos.
