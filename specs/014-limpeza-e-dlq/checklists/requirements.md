# Specification Quality Checklist: Limpeza de dados de mensageria e reprocessamento da DLQ

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-24
**Feature**: [spec.md](../spec.md)

## Content Quality

- [x] No implementation details (languages, frameworks, APIs)
- [x] Focused on user value and business needs
- [x] Written for non-technical stakeholders
- [x] All mandatory sections completed

## Requirement Completeness

- [x] No [NEEDS CLARIFICATION] markers remain
- [x] Requirements are testable and unambiguous
- [x] Success criteria are measurable
- [x] Success criteria are technology-agnostic (no implementation details)
- [x] All acceptance scenarios are defined
- [x] Edge cases are identified
- [x] Scope is clearly bounded
- [x] Dependencies and assumptions identified

## Feature Readiness

- [x] All functional requirements have clear acceptance criteria
- [x] User scenarios cover primary flows
- [x] Feature meets measurable outcomes defined in Success Criteria
- [x] No implementation details leak into specification

## Notes

- Validação em 2026-10-24, duas iterações, 16/16. Termos como "outbox", "DLQ", "fila" e
  "reentrega" seguem o vocabulário das specs do REQ-06 ao REQ-13.
- Decidido no specify (Q1 = A): o reenvio é por rota administrativa da API, só Admin, com o
  autor autenticado; a rota entra no contrato HTTP v1 por acréscimo (US2 cenários 7 a 9, FR-008).
- Decisões tomadas como padrão: retenções (7 dias para enviados e processados, 48 h para
  chaves, com mínimos de 1 dia e 24 h), limpeza por hora em lotes de 1 000, limite de 3
  reenvios, até 1 000 mensagens por reenvio, aviso acima de 10 mensagens a cada 5 min, purga
  da DLQ fora do escopo.
- Revisão de 2026-10-24, pedida pelo usuário depois da análise dos impactos de a API falar
  com o broker: o reenvio passou a ser assíncrono (a rota de Admin registra o pedido, o
  processo de workers executa, uma rota consulta o resultado) e a API ganhou um usuário
  próprio no broker, só para a prontidão (US4, FR-017 a FR-019). Revalidado: 16/16.
