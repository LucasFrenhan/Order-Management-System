# Specification Quality Checklist: Cancelamento depois do pagamento, com estorno e cancelamento da ordem de entrega

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

- Validação em 2026-10-24, duas iterações, 16/16. Termos como "gateway", "reconciliação",
  "concorrência otimista" e "outbox" seguem o vocabulário das specs do REQ-06 ao REQ-12 e
  descrevem comportamento observável.
- As duas decisões que a issue #28 deixava para o clarify foram respondidas no specify
  (seção Clarifications):
  - Q1 = B: cancela na hora com o estorno pendente; o processo de workers estorna e tenta de
    novo sem limite, com erro no log depois de 24 h e 3 tentativas;
  - Q2 = A: chave de idempotência por pedido na cobrança, e também no estorno.
- Decisões tomadas como padrão:
  - quem pode cancelar não muda (o Cliente só em `Pendente`);
  - estorno sempre do valor total;
  - rodada de estornos a cada 1 minuto;
  - cobrança aprovada para pedido cancelado no meio do caminho é registrada com estorno
    pendente (FR-006);
  - a ordem cancelada ganha só o instante do cancelamento, exposto como campo opcional;
  - motivo do esgotamento da entrega: `Não foi possível criar a ordem de entrega.`;
  - nenhum evento novo (#29).
