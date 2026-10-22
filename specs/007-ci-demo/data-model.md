# Data Model: CI de build e testes e ambiente de demonstração (REQ-07)

**Data**: 2026-10-22 | **Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Este requisito não cria entidades de dados nem muda o banco. O "modelo" é o conjunto de
artefatos de entrega e de variáveis de configuração abaixo.

## Variáveis de ambiente

| Variável | Onde | Padrão | Efeito |
|----------|------|--------|--------|
| `OMS_EXIGIR_DOCKER` | testes de integração | ausente | `true` (sem diferenciar maiúsculas) transforma "Docker indisponível" em falha (FR-006). O CI define `true` |
| `OMS_API_PORTA` | `docker-compose.yml`, `.env.example` | `5230` | porta da API publicada no host pelo perfil `demo` (FR-016) |
| `OMS_SQL_SENHA`, `OMS_RABBITMQ_USUARIO`, `OMS_RABBITMQ_SENHA` | já existiam (REQ-02) | valores de desenvolvimento | passam a montar também a configuração da API da demonstração |
| `OMS_SQL_PORTA`, `OMS_RABBITMQ_PORTA`, `OMS_RABBITMQ_PORTA_PAINEL` | já existiam | `1433`, `5672`, `15672` | só portas publicadas no host; a API da demonstração usa as portas internas da rede do Compose |

## Artefatos

| Artefato | Caminho | Conteúdo |
|----------|---------|----------|
| Workflow | `.github/workflows/build-testes.yml` | jobs `testes` ("Build e testes") e `demo` ("Demonstração") |
| Script de resumo | `.github/scripts/resumo-testes.sh` | contagens do TRX, testes falhos e cobertura em `$GITHUB_STEP_SUMMARY`; sai com erro se houver teste ignorado |
| Configuração de cobertura | `coverage.runsettings` | só módulos `PedidoFulfillment.*` de produção, sem `Persistencia/Migracoes` |
| Imagem da API | `src/PedidoFulfillment.API/Dockerfile` | duas etapas, `sdk:10.0.402-noble` → `aspnet:10.0.13-noble`, usuário `app`, porta 8080 |
| Exclusões da imagem | `.dockerignore` | `bin`, `obj`, `.git`, `.github`, `tests`, `specs`, IDEs, `.env` |
| Serviço `api` | `docker-compose.yml` | perfil `demo`, `depends_on` saudável, variáveis, porta, healthcheck |
| Exigência de Docker | `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/AmbienteDocker.cs` | `Obrigatorio(...)` e a mensagem do FR-006 |

## Arquivos da execução (FR-009)

Artefato `resultados-testes`, mantido por 14 dias:

- `resultado*.trx`: um por projeto de teste;
- `**/*.cobertura.xml`: cobertura de cada projeto.
