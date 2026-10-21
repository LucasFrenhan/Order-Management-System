# Constituição do projeto Order Management System (OMS)

## Core Principles
- **Specification-First (SDD):** Nenhuma linha de código de funcionalidade é escrita sem uma especificação/requisito formal prévio e rastreável. O código é a materialização da especificação.
- **Contratos Imutáveis:** Mensagens assíncronas e contratos de API são contratos de negócio e devem ser tratados com versionamento rigoroso e compatibilidade retroativa.
- **Segurança por Padrão:** Toda rota da API (exceto endpoints públicos de autenticação e saúde) exige autenticação obrigatória via token e escopos/claims validados.
- **Desacoplamento Assíncrono:** Efeitos colaterais e integrações entre domínios (ex: notificação de pedido criado) devem trafegar via mensageria, garantindo resiliência.

## Stack
- **Plataforma:** .NET 10, C#, Minimal APIs
- **Persistência:** SQL Server via Entity Framework Core
- **Autenticação & Autorização:** ASP.NET Core Identity / JWT Bearer (com validação de Claims/Policies)
- **Mensageria:** RabbitMQ (`RabbitMQ.Client` nativo, sem frameworks de abstração como MassTransit ou Wolverine)
- **Testes:** xUnit, FluentAssertions e Testcontainers (quando aplicável)

## Arquitetura
- O endpoint Minimal API atua estritamente como camada de roteamento/transporte; não acessa o banco de dados diretamente e não contém regras de negócio.
- Toda regra de negócio reside em serviços de aplicação ou no domínio.
- A publicação de mensagens no RabbitMQ deve ser tratada com cuidado para mitigar perdas em caso de falha de transação do banco relacional (ex: garantia de consistência ou registro de outbox simplificado).
- Nenhuma dependência nova (pacote NuGet) entra sem justificativa escrita no plano de especificação correspondente.

## Qualidade
- Toda regra de negócio e validação complexa precisa ter cobertura de teste unitário.
- Todo endpoint protegido deve possuir testes de integração cobrindo cenários de sucesso (caminho feliz) e falha de autenticação/autorização (401/403).
- Falhas de validação de dados devem retornar sempre o código HTTP `400` estruturado no padrão `ProblemDetails` (RFC 7807).
- Os contratos de mensageria (payloads JSON publicados/consumidos via RabbitMQ) devem possuir testes de serialização e conformidade de schema.

## Convenções
- Nomes de classes, arquivos, namespaces e mensagens de erro descritivas em **Português**.
- Nomes de exchanges, filas e eventos no padrão adequado (ex: exchange de pedidos, eventos no passado como `pedido.criado`).
- Formato de Commits: `tipo(escopo): descrição [ID-Requisito]` (ex: `feat(pedidos): implementa criacao de pedido [REQ-01]`).

## Governança
- **Isolamento de Especificação (Spec-Branch Workflow):** Toda nova spec (requisito) e sua respectiva implementação de código devem ser desenvolvidas em uma **branch dedicada** (ex: `feature/req-01-criacao-pedido`). Todos os commits relacionados a essa especificação devem ocorrer exclusivamente nessa branch até o fechamento/merge.
- Se o plano de implementação contradiz a constituição, a constituição prevalece e o plano deve ser ajustado.
- Se a constituição precisar evoluir ou ser alterada, a mudança deve ocorrer em um **commit separado**, documentando o motivo da mudança conceitual do projeto.