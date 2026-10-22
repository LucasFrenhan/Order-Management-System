# Contrato: catálogo inicial de produtos (Development e testes)

**Requisito**: FR-020 | **Research**: [R-08](../research.md#r-08-catálogo-inicial)

Este catálogo é criado pela preparação da inicialização só nos ambientes `Development` e
`Testes`, com `Banco:PrepararNaInicializacao` ligado. Os identificadores e SKUs são fixos e
podem ser citados por testes e roteiros de requisitos seguintes (REQ-05 em diante). Um
produto já existente (mesmo SKU) nunca é alterado pela preparação.

| Id | Sku | Nome | Preço (BRL) | Estoque | Ativo |
|----|-----|------|-------------|---------|-------|
| `0192b6a0-0000-7000-8000-000000000001` | `CAT-001` | Teclado mecânico | 349,90 | 50 | sim |
| `0192b6a0-0000-7000-8000-000000000002` | `CAT-002` | Mouse sem fio | 129,90 | 100 | sim |
| `0192b6a0-0000-7000-8000-000000000003` | `CAT-003` | Monitor 27 polegadas | 1.899,00 | 20 | sim |
| `0192b6a0-0000-7000-8000-000000000004` | `CAT-004` | Headset com microfone | 259,90 | 40 | sim |
| `0192b6a0-0000-7000-8000-000000000005` | `CAT-005` | Webcam Full HD | 219,90 | 30 | sim |
| `0192b6a0-0000-7000-8000-000000000006` | `CAT-006` | Cabo HDMI 2 m | 39,90 | 500 | sim |
| `0192b6a0-0000-7000-8000-000000000007` | `CAT-007` | Suporte de notebook | 89,90 | 60 | sim |
| `0192b6a0-0000-7000-8000-000000000008` | `CAT-008` | Hub USB-C | 199,90 | **1** | sim |
| `0192b6a0-0000-7000-8000-000000000009` | `CAT-009` | Cadeira ergonômica | 1.299,00 | **0** | sim |
| `0192b6a0-0000-7000-8000-000000000010` | `CAT-010` | Mousepad estendido | 59,90 | 80 | **não** |

Casos reservados para testes:

- `CAT-008`: estoque 1. Serve para disputa do último item (concorrência, issue gh #7).
- `CAT-009`: estoque zero (`PRODUTO_INDISPONIVEL`, REQ-05).
- `CAT-010`: inativo (`PRODUTO_INDISPONIVEL`, REQ-05).

Descrição de todos os produtos: nula. Datas de criação e atualização: o instante da
preparação.
