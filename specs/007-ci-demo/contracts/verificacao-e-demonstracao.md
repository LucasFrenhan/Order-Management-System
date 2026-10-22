# Contrato: verificação `build-testes` e perfil `demo`

**Requisitos**: FR-001 a FR-021 | **Research**: [research.md](../research.md)

Este documento fixa os nomes e comportamentos que outras pessoas e configurações usam: a
proteção da branch, o README e quem roda a demonstração.

## Verificação `build-testes`

| Item | Valor |
|------|-------|
| Workflow | `.github/workflows/build-testes.yml`, `name: build-testes` |
| Gatilhos | `pull_request` para `main`; `push` em `main`; sem filtro de caminhos |
| Jobs exigíveis na proteção da branch | `Build e testes` e `Demonstração` |
| Permissões | `contents: read` |
| Concorrência | uma execução por referência; a mais nova cancela a anterior |
| Segredos | nenhum |
| Artefato | `resultados-testes` (TRX e Cobertura), 14 dias |
| Reprova quando | erro ou aviso de compilação; teste falho; **qualquer teste ignorado**; Docker indisponível; demonstração não fica pronta, ou o login do administrador falha |
| Não reprova por | percentual de cobertura (informativo) |

Selo: `https://github.com/LucasFrenhan/Order-Management-System/actions/workflows/build-testes.yml/badge.svg?branch=main`.

## `OMS_EXIGIR_DOCKER`

| Valor | Sem Docker, os testes com banco ou broker real |
|-------|-----------------------------------------------|
| `true` (qualquer caixa) | falham com "Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true)." |
| ausente ou outro valor | são ignorados com "Docker indisponível" (comportamento local de sempre) |

## Perfil `demo`

| Comando | Resultado |
|---------|-----------|
| `docker compose up -d --wait` | só `sqlserver` e `rabbitmq` (inalterado) |
| `docker compose --profile demo up -d --wait --build` | `sqlserver`, `rabbitmq` e `api`, todos saudáveis |
| `docker compose --profile demo down -v` | remove os três serviços e os volumes |

| Endereço (padrões) | O que é |
|--------------------|---------|
| `http://localhost:5230` (`OMS_API_PORTA`) | API, ambiente Development |
| `http://localhost:5230/health/pronto` | prontidão (200 com banco e broker prontos) |
| `http://localhost:5230/scalar/v1` | contrato navegável |
| `http://localhost:15672` | painel do RabbitMQ (`oms` / `oms-dev-local`) |

Usuários de desenvolvimento: `admin@oms.local` / `AdminDev2026` (Admin) e
`operador@oms.local` / `OperadorDev2026` (OperadorLogistica). São **somente de
desenvolvimento**, já versionados no REQ-03.
