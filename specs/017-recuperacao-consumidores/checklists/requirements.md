# Specification Quality Checklist: Recuperação dos consumidores depois de o broker reiniciar

**Purpose**: Validate specification completeness and quality before proceeding to planning
**Created**: 2026-10-27
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

- Requisito técnico por natureza (resiliência da mensageria): a spec cita o broker, as filas,
  a DLQ, o outbox e a prontidão porque são o domínio do problema e já aparecem nos REQ-02,
  REQ-06, REQ-12 e REQ-14. Classes, eventos do cliente e a alternativa escolhida ficam no
  `research.md` e no `plan.md` (FR-001 e FR-002 pedem que o estudo os registre).
- O SC-005 cita `docker compose restart worker` só para nomear o passo da reprodução da issue
  que deixa de ser necessário.
- Correção da hipótese da issue: a prontidão (`VerificacaoRabbitMQ`) abre uma conexão própria
  e não chama a conexão compartilhada; quem a pede durante a queda são o despachante, o
  monitor da DLQ e o reenvio.
- Sem `[NEEDS CLARIFICATION]`. Candidatos para o `/speckit-clarify`: incluir a fila apagada
  (User Story 2) no escopo; a prontidão do Worker ficar não pronta logo na perda da inscrição
  ou só depois de uma tolerância; uma métrica de consumidores inscritos (hoje fora do exigido).
