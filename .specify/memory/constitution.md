# Constituição do projeto Order Management System (OMS)

## Core Principles
- **Specification-First (SDD):** Nenhuma linha de código de funcionalidade é escrita sem uma especificação/requisito formal prévio e rastreável. O código é a materialização da especificação.
- **Contratos Imutáveis:** Mensagens assíncronas e contratos de API são contratos de negócio e devem ser tratados com versionamento rigoroso e compatibilidade retroativa.
- **Segurança por Padrão:** Toda rota da API (exceto endpoints públicos de autenticação e saúde) exige autenticação obrigatória via token e escopos/claims validados.
- **Desacoplamento Assíncrono:** Efeitos colaterais e integrações entre domínios (ex: notificação de pedido criado) devem preferencialmente trafegar via mensageria, garantindo resiliência.

## Stack
- **Plataforma:** .NET 10, C#, Minimal APIs
- **Persistência:** SQL Server via Entity Framework Core
- **Autenticação & Autorização:** ASP.NET Core Identity / JWT Bearer (com validação de Claims/Policies)
- **Mensageria:** RabbitMQ (`RabbitMQ.Client` nativo, sem frameworks de abstração como MassTransit ou Wolverine)
- **Testes:** xUnit, FluentAssertions e Testcontainers (quando aplicável)

## Arquitetura
- O endpoint Minimal API atua estritamente como camada de roteamento/transporte; não acessa o banco de dados diretamente e não contém regras de negócio.
- Toda regra de negócio reside em serviços de aplicação ou no domínio.
- A publicação de eventos de mensageria deve ser acoplada de forma segura ao ciclo de transação do banco relacional (ex: padrão *Outbox* ou garantia de consistência eventual).
- Nenhuma dependência nova (pacote NuGet) entra sem justificativa escrita no plano de especificação correspondente.

## Qualidade
- Toda regra de negócio e validação complexa precisa ter cobertura de teste unitário.
- Todo endpoint protegido deve possuir testes de integração cobrindo cenários de sucesso (caminho feliz) e falha de autenticação/autorização (401/403).
- Falhas de validação de dados devem retornar sempre o código HTTP `400` estruturado no padrão `ProblemDetails` (RFC 7807).
- Os contratos de mensageria devem possuir testes de serialização e conformidade de schema.

## Convenções
- Nomes de classes, arquivos, namespaces e mensagens de erro descritivas em **Português**.
- Nomes de eventos de mensageria no passado (ex: `PedidoCriadoEvent`, `PagamentoAprovadoEvent`).
- Formato de Commits: `tipo(escopo): descrição [ID-Requisito]` (ex: `feat(pedidos): implementa criacao de pedido [REQ-01]`).

## Governança
- Se o plano de implementação contradiz a constituição, a constituição prevalece e o plano deve ser ajustado.
- Se a constituição precisar evoluir ou ser alterada, a mudança deve ocorrer em um **commit separado**, documentando o motivo da mudança conceitual do projeto.