<!--
Sync Impact Report
- Version change: não versionada -> 1.0.0
- Modified principles: princípios existentes foram formalizados e reorganizados em cinco
  princípios declarativos.
- Added sections: Restrições Técnicas e Arquiteturais; Qualidade e Conformidade;
  Convenções de Desenvolvimento.
- Removed sections: nenhuma informação normativa removida.
- Follow-up TODOs: definir a data original de ratificação da constituição.
-->

# Constituição do projeto Order Management System (OMS)

## Core Principles

### I. Specification-First (SDD)
Nenhuma linha de código de funcionalidade pode ser escrita sem uma especificação ou
requisito formal, rastreável e aprovado previamente. A implementação deve materializar
essa especificação, e qualquer divergência deve ser registrada e aprovada antes da
alteração. Isso garante rastreabilidade entre necessidade de negócio, projeto e código.

### II. Contratos Imutáveis e Versionados
Mensagens assíncronas e contratos de API são contratos de negócio. Toda alteração deve
preservar compatibilidade retroativa ou introduzir uma nova versão explícita, com
documentação e testes de conformidade. Consumidores existentes não podem ser quebrados
silenciosamente, pois contratos compartilhados exigem evolução coordenada.

### III. Segurança por Padrão
Toda rota da API, exceto endpoints públicos de autenticação e saúde, deve exigir
autenticação por token e validar os escopos ou claims necessários por meio de políticas.
Dados de entrada devem ser validados e falhas devem ser expostas sem revelar
informações sensíveis. A segurança é obrigatória desde o desenho, não uma etapa
posterior de implantação.

### IV. Desacoplamento Assíncrono
Efeitos colaterais e integrações entre domínios, como notificações de pedido criado,
devem trafegar por mensageria quando não exigirem resposta síncrona. Publicações devem
preservar a consistência com a transação relacional, usando outbox ou mecanismo
equivalente quando necessário. Isso reduz acoplamento e aumenta a resiliência.

### V. Qualidade Verificável
Toda regra de negócio e validação complexa deve ter testes unitários. Todo endpoint
protegido deve ter testes de integração para sucesso e falhas de autenticação e
autorização (401/403). Contratos de mensageria devem ter testes de serialização e
conformidade de schema. A qualidade deve ser demonstrada por verificações automatizadas,
não presumida pela revisão manual.

## Restrições Técnicas e Arquiteturais

- A plataforma é .NET 10 com C# e Minimal APIs.
- A persistência usa SQL Server por meio do Entity Framework Core.
- A autenticação e autorização usam ASP.NET Core Identity e JWT Bearer, com validação
  de claims e policies.
- A mensageria usa RabbitMQ.Client nativo; frameworks de abstração como MassTransit e
  Wolverine não são permitidos.
- O endpoint Minimal API atua exclusivamente como camada de roteamento e transporte.
  Ele não acessa o banco diretamente nem contém regras de negócio.
- Regras de negócio residem em serviços de aplicação ou no domínio.
- Uma nova dependência NuGet exige justificativa escrita no plano da especificação
  correspondente antes de ser adicionada.

## Qualidade e Conformidade

- Falhas de validação de dados devem retornar HTTP 400 em `ProblemDetails`, conforme
  RFC 7807.
- Testes de integração de endpoints protegidos devem cobrir o caminho feliz e as
  respostas 401 e 403.
- Payloads JSON publicados ou consumidos via RabbitMQ devem ser validados por testes de
  serialização e conformidade de schema.
- A suíte de testes usa xUnit, FluentAssertions e Testcontainers quando aplicável.
- Cada revisão deve verificar rastreabilidade para a especificação, segurança,
  compatibilidade de contratos, cobertura de testes e ausência de dependências não
  justificadas.

## Convenções de Desenvolvimento

- Classes, arquivos, namespaces e mensagens de erro devem usar nomes descritivos em
  português.
- Exchanges, filas e eventos devem seguir nomes consistentes com o domínio; eventos
  devem usar o passado, como `pedido.criado`.
- Commits devem seguir o formato
  `tipo(escopo): descrição [ID-Requisito]`, por exemplo:
  `feat(pedidos): implementa criacao de pedido [REQ-01]`.

## Governance

A constituição prevalece sobre práticas, planos e decisões conflitantes. Toda nova
especificação e sua implementação devem ser desenvolvidas em uma branch dedicada, como
`feature/req-01-criacao-pedido`, e seus commits devem permanecer nessa branch até o
fechamento ou merge.

Alterações nesta constituição exigem uma proposta explícita, a descrição do impacto
conceitual e a atualização deste arquivo. A alteração da constituição deve ser feita em
um commit separado dos commits de implementação. O versionamento segue Semantic
Versioning: MAJOR para remoção ou redefinição incompatível de princípios; MINOR para
novos princípios ou seções e expansões normativas relevantes; PATCH para esclarecimentos,
correções de redação e refinamentos semânticos não incompatíveis.

Cada pull request ou revisão deve avaliar conformidade com esta constituição. Violações
devem ser corrigidas antes do merge ou justificadas explicitamente com uma decisão
aprovada e, quando necessário, um plano de migração. A constituição deve ser revisada
quando a arquitetura, os contratos, os requisitos de segurança ou a plataforma mudarem
materialmente.

**Version**: 1.0.0 | **Ratified**: TODO(RATIFICATION_DATE): informar a data original de adoção | **Last Amended**: 2026-10-21
