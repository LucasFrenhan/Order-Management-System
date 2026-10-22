# Implementation Plan: Criação, consulta e status de pedidos

**Branch**: `feature/req-05-criacao-pedidos` | **Date**: 2026-10-22 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/005-criacao-pedidos/spec.md`

**Requisito**: REQ-05 (GitHub issue #6)

## Summary

As três operações de pedido do contrato v1 deixam de responder 501:

- **Serviço**: um `ServicoPedidos` no Application concentra as regras:
  - validação de produtos (422);
  - idempotência por (cliente, chave);
  - visibilidade da consulta;
  - tabela de transições, com reavaliação em conflito.

  O serviço depende de duas portas, `IArmazenamentoPedidos` e `IGeradorNumeroPedido`,
  implementadas na Infrastructure sobre o `ContextoBancoDados` do REQ-04.
- **Idempotência**:
  - A tabela `ChavesIdempotencia` tem chave primária (cliente, chave) e guarda a
    impressão SHA-256 do conteúdo normalizado e a resposta original.
  - O registro é gravado junto com o pedido.
  - A violação da chave primária resolve a corrida entre requisições simultâneas
    (R-03, R-04).
- **Número**: uma `SEQUENCE` do SQL Server gera o sufixo de `PED-AAAAMMDD-NNNNNN`, sem
  colisão entre criações simultâneas (R-05).
- **Transições**: a tabela do contrato v1 fica no domínio (`TransicoesStatusPedido`). O
  serviço escolhe o papel (operação ou Cliente) e reavalia até 3 vezes quando a
  concorrência otimista detecta conflito (R-06, R-09).
- **Remoção**: o `ServicoNaoImplementado` e o `TipoFalha.NaoImplementado` deixam de
  existir (R-10).

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.402)

**Primary Dependencies**:
- EF Core SqlServer 10.0.13 (já no projeto);
- SHA-256 e `System.Text.Json` da biblioteca base.

Nenhum pacote novo (R-13).

**Storage**:
- SQL Server 2022, banco `PedidoFulfillment`;
- migration nova `ChavesIdempotencia`, com a tabela e a sequência `SequenciaNumeroPedido`.

**Testing**:
- xUnit v3, FluentAssertions 7.x e WebApplicationFactory;
- Testcontainers.MsSql 4.15.0, com testes ignorados sem Docker.

**Target Platform**: API ASP.NET Core rodando pelo SDK; banco em contêiner

**Project Type**: web-service

**Performance Goals**: 95% das criações com até 10 itens em menos de 500 ms no ambiente
local (SC-005)

**Constraints**:
- O contrato v1 não muda (FR-021).
- O comportamento de autorização do REQ-03 continua igual (FR-023).
- Os endpoints só roteiam.
- A suíte continua rodando sem Docker.

**Scale/Scope**:
- 3 operações implementadas e 1 serviço;
- 2 portas e 2 implementações;
- 1 tabela, 1 sequência e 1 migration;
- 3 eventos de log.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação | Evidência |
|-------------------|----------|-----------|
| I. Specification-First | ✅ | Spec aprovada após o specify (escopo com o usuário) e o clarify (3 perguntas); commit `[REQ-05]` |
| II. Contratos versionados | ✅ | OpenAPI v1 intocado (FR-021). O comportamento de idempotência está detalhado em `contracts/idempotencia.md`, sem status nem código novo |
| III. Segurança por padrão | ✅ | O dono vem só do token. Pedido de outra pessoa responde o mesmo 404 de inexistente (FR-015). Os logs não têm e-mail, token nem corpo (FR-020). As políticas de acesso continuam iguais |
| IV. Desacoplamento assíncrono | ➖ N/A | Nenhuma integração entre domínios neste requisito; o evento de pedido criado e o outbox são do REQ-06 (issue gh #7) |
| V. Qualidade verificável | ✅ | Testes unitários do serviço, da impressão e da tabela de transições. Integração com banco real para idempotência concorrente, números, consulta, status e as respostas 401/403 que já existem |
| Stack | ✅ | .NET 10, Minimal APIs, SQL Server via EF Core |
| Endpoint só roteia | ✅ | `EndpointsPedidos` não muda |
| Regras em serviços ou domínio | ✅ | `ServicoPedidos` (Application) e `TransicoesStatusPedido` (Domain) |
| Dependências NuGet justificadas | ✅ | Nenhuma nova |
| Nomes em português | ✅ | `ServicoPedidos`, `ArmazenamentoPedidos`, `RegistroIdempotencia`, `ChavesIdempotencia`, `SequenciaNumeroPedido` |
| Branch dedicada | ✅ | `feature/req-05-criacao-pedidos`, a partir de `main` com o REQ-04 mergeado |

**Resultado**: aprovado, sem violações. Reavaliado após a Fase 1 e após o
`/speckit-analyze`, sem mudanças.

## Project Structure

### Documentation (this feature)

```text
specs/005-criacao-pedidos/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/idempotencia.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.Domain/Pedidos/
│   └── TransicoesStatusPedido.cs                 # NOVO (R-06)
├── PedidoFulfillment.Application/
│   ├── Comum/Resultado.cs                        # − TipoFalha.NaoImplementado
│   └── Pedidos/
│       ├── IServicoPedidos.cs                    # comentário: implementado no REQ-05
│       ├── ServicoPedidos.cs                     # NOVO (R-01, R-04, R-07 a R-09)
│       ├── IArmazenamentoPedidos.cs              # NOVO (R-02)
│       ├── IGeradorNumeroPedido.cs               # NOVO (R-05)
│       ├── RegistroIdempotencia.cs               # NOVO (R-03)
│       ├── ChaveIdempotenciaRegistradaException.cs  # NOVO
│       ├── ImpressaoConteudo.cs                  # NOVO (R-04)
│       ├── MapeamentoPedido.cs                   # NOVO: Pedido → PedidoResponseDTO
│       └── EventosPedidos.cs                     # NOVO (R-11)
├── PedidoFulfillment.Infrastructure/
│   ├── ExtensoesInfraestrutura.cs                # + armazenamento e gerador
│   └── Persistencia/
│       ├── ArmazenamentoPedidos.cs               # NOVO
│       ├── GeradorNumeroPedido.cs                # NOVO
│       ├── ErrosBancoDados.cs                    # + violação de unicidade por nome de restrição
│       ├── ContextoBancoDados.cs                 # + DbSet ChavesIdempotencia, sequência
│       ├── Configuracoes/ConfiguracaoRegistroIdempotencia.cs  # NOVO
│       └── Migracoes/                            # + ChavesIdempotencia
└── PedidoFulfillment.API/
    ├── Program.cs                                # ServicoPedidos no lugar do provisório
    ├── Erros/ExtensoesResultado.cs               # − 501
    ├── Erros/ConfiguracaoProblemDetails.cs       # − título 501
    └── Provisorio/                               # REMOVIDO

tests/
├── PedidoFulfillment.Testes.Unitarios/
│   ├── Dominio/TransicoesStatusPedidoTestes.cs   # NOVO
│   └── Pedidos/                                  # NOVO: ServicoPedidosTestes, ImpressaoConteudoTestes, falsos
└── PedidoFulfillment.Testes.Integracao/
    ├── Pedidos/                                  # NOVO: criação, idempotência concorrente, número, consulta, status, desempenho
    ├── Persistencia/ModeloSincronizadoTestes.cs  # + terceira migration
    └── Autenticacao/{AutorizacaoComTokensReais,EmissorToken,LoginComBanco}Testes.cs  # sem 501 (R-10)
```

**Structure Decision**: as quatro camadas continuam. As regras de pedido ficam no
Application, com as portas implementadas na Infrastructure. A tabela de transições fica no
Domain, porque também servirá aos workers.

## Complexity Tracking

| Risco | Mitigação |
|-------|-----------|
| Corrida de chaves com isolamento diferente de `READ COMMITTED` (por exemplo, snapshot ligado no banco) | O fluxo trata o "registro ainda não visível" com 409 `IDEMPOTENCIA_EM_PROCESSAMENTO`, aceito pelo contrato. O teste SC-002 afirma "1 pedido e nenhum 500", não a resposta exata (R-04) |
| Identificar a violação pelo nome `PK_ChavesIdempotencia` na mensagem do SQL Server | Teste de integração da corrida. O nome é fixado no mapeamento (R-04) |
| Testes do REQ-03 dependiam do 501 | Ajuste explícito nos 3 testes (R-10) |
| Tabela `ChavesIdempotencia` crescer sem limite | Aceito no volume esperado. O índice por `CriadoEm` prepara uma limpeza futura, que respeita as 24 h (R-03) |
| Estoque verificado na criação ficar velho logo depois | Esperado: a criação não reserva, e o worker de estoque (gh #8) é a garantia (spec, Assumptions) |
| Princípio IV sem evento de pedido criado | Adiado para o REQ-06, que acrescenta a publicação e o outbox sobre este fluxo |
