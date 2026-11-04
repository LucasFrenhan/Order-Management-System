# Implementation Plan: Limite de requisições compartilhado no login e no registro

**Branch**: `feature/req-23-rate-limiting` | **Date**: 2026-11-04 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/023-rate-limiting/spec.md`

**Requisito**: REQ-23 (GitHub issue #58)

## Summary

- **Estado único no SQL Server** (tabela `ContagensLimite`), atrás da porta `IArmazenamentoContagens`: medido
  contra o Redis, atende ao SC-008 no uso real e não traz nada novo para operar
  ([R-01](./research.md#r-01-armazenamento-compartilhado-sql-server-ou-redis), a confirmar pelo usuário).
- **Decisão atômica dos dois limites em um só lote T-SQL**, com o relógio do banco: quem é barrado por um limite
  não consome o outro (R-03).
- **Middleware próprio em vez do limitador nativo**, que é síncrono na chave e guarda o estado no processo (R-02).
- **Sem o armazenamento, a requisição segue sem o limite**: 250 ms no máximo, pausa de 5 s depois de uma falha
  e pool de conexões próprio (R-04). Um **bloqueio local** das recusas alivia o banco em uma inundação (R-05).
- **Cliente real por intermediários confiáveis**, com o `UseForwardedHeaders` nativo; IPv6 pelo `/64` (R-06).
- **Identidade por HMAC com chave própria** (`LimiteRequisicoes:ChaveContagem`), sem dados legíveis (R-08).
- **Limpeza a cada 60 s no Worker** (R-07). Migration `LimiteRequisicoes`; logs 3007 a 3009; duas métricas; um
  código de erro novo; nenhum pacote novo.

## Technical Context

**Language/Version**: C# 14 / .NET 10 (SDK 10.0.403)

**Primary Dependencies**: as que já existem: ASP.NET Core 10 (`UseForwardedHeaders`, `IMemoryCache`),
EF Core SqlServer 10.0.13 (migration, lote e limpeza) e OpenTelemetry. **Nenhum pacote novo.**

**Storage**: SQL Server 2022 CU26; tabela `ContagensLimite` ([data-model](./data-model.md))

**Testing**: xUnit v3, FluentAssertions, WebApplicationFactory, Testcontainers MsSql; conformidade do contrato e
oasdiff. Duas `FabricaApi` sobre o mesmo banco simulam duas instâncias.

**Target Platform**: API e Worker .NET 10; SQL Server em contêiner

**Project Type**: web-service

**Performance Goals**: p95 da verificação até 50 ms (SC-008). Medido: 5,69 ms em chaves novas e 2,09 ms na recusa, com 16 simultâneas.

**Constraints**:
- contrato v1 só com acréscimos;
- regras na Application; a API só traduz HTTP; o SQL fica na Infrastructure;
- `Application` não depende de `Options` nem da `TelemetriaOms`, que é interna: portas e configuração são passadas a ela;
- nenhuma mudança no bloqueio de conta;
- as respostas dentro do limite continuam idênticas.

**Scale/Scope**: 2 rotas, 3 políticas, 1 serviço, 1 middleware, 1 armazenamento, 1 rotina no Worker, 1 migration, ~40 testes.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Avaliação | Situação |
|-------------------|-----------|----------|
| I. Specification-First | Spec, clarify e plano antes do código; issue #58 | ✅ |
| II. Contratos imutáveis | Só acréscimos: 429 nas duas rotas e um código novo; `ComparadorContrato` e oasdiff. O 429 do SSE não muda | ✅ |
| III. Segurança por padrão | As rotas continuam públicas, como a constituição permite; sem detalhes na resposta; identidades por HMAC; cabeçalho de origem só de intermediários confiáveis; sem opção de desligar | ✅ |
| IV. Desacoplamento assíncrono | Sem efeito entre domínios e sem eventos: a decisão é síncrona por natureza | ✅ |
| V. Qualidade verificável | Unitários das regras; integração com 429, duas instâncias, concorrência, volta depois da espera e armazenamento fora | ✅ |
| Endpoint só roteia | As rotas só ganham metadados; a decisão fica no `ServicoLimiteRequisicoes` | ✅ |
| Persistência em SQL Server via EF Core | Tabela e migration pelo EF Core. O lote atômico não cabe em LINQ e usa `Database.SqlQueryRaw`, como `FromSqlRaw` e `Database.Execute…` já usados no projeto; a limpeza usa `Database.ExecuteSql`. Nada grava por `SqlClient` direto (analyze C1) | ✅ |
| 400 em ProblemDetails | Não muda: a validação segue depois do limite | ✅ |
| Nova dependência NuGet | Nenhuma | ✅ |

**Re-check pós-design**: sem violações.

## Project Structure

### Documentation (this feature)

```text
specs/023-rate-limiting/
├── plan.md
├── research.md          # R-01 a R-10, com a medição SQL Server × Redis
├── data-model.md
├── quickstart.md
├── contracts/limite-requisicoes.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/
├── PedidoFulfillment.API/
│   ├── LimiteRequisicoes/
│   │   ├── MiddlewareLimiteRequisicoes.cs       # novo: lê o e-mail, chama o serviço, responde 429
│   │   ├── LeitorEmailLogin.cs                  # novo: até 16 KB, sem consumir o corpo (R-09)
│   │   ├── MetadadosLimiteRequisicoes.cs        # novo: liga a rota à política
│   │   └── ConfiguracaoIntermediarios.cs        # novo: UseForwardedHeaders (R-06)
│   ├── Endpoints/EndpointsAutenticacao.cs       # + metadados e 429
│   ├── OpenApi/TransformadorSchemasErroOpenApi.cs, TransformadorCabecalhosOpenApi.cs  # + 429 e Retry-After
│   ├── appsettings.json, appsettings.Development.json  # + limites; chave de desenvolvimento
│   └── Program.cs                               # registros e ordem do pipeline
├── PedidoFulfillment.Application/
│   ├── Contratos/Erros/CodigosErro.cs           # + LimiteRequisicoesExcedido
│   └── LimiteRequisicoes/                       # novo
│       ├── PoliticaLimite.cs, ConfiguracaoLimites.cs
│       ├── OrigemCliente.cs                     # IPv4, IPv4 mapeado, /64 (R-06)
│       ├── GeradorChaveContagem.cs              # HMAC (R-08)
│       ├── IServicoLimiteRequisicoes.cs, ServicoLimiteRequisicoes.cs  # decisão, pausa, bloqueio local
│       ├── IArmazenamentoContagens.cs, IBloqueiosLocais.cs, IMetricasLimite.cs
│       └── EventosLimiteRequisicoes.cs          # logs 3007 e 3008
├── PedidoFulfillment.Infrastructure/
│   ├── Configuracao/OpcoesLimiteRequisicoes.cs  # novo, com validador
│   ├── Configuracao/OpcoesLimpeza.cs            # + chaves das contagens
│   ├── LimiteRequisicoes/                       # novo
│   │   ├── ArmazenamentoContagensSql.cs         # lote de R-03 pelo EF Core, pool próprio
│   │   ├── AquecimentoLimiteRequisicoes.cs      # abre o pool na partida (R-04)
│   │   ├── BloqueiosLocaisMemoria.cs, MetricasLimite.cs
│   │   ├── LimpezaContagensLimite.cs            # rotina do Worker (R-07)
│   │   └── ExtensoesLimiteRequisicoes.cs
│   ├── Observabilidade/TelemetriaOms.cs         # + 2 métricas
│   ├── Mensageria/ExtensoesLimpezaReenvio.cs    # + LimpezaContagensLimite
│   └── Persistencia/
│       ├── ContagemLimite.cs, Configuracoes/ConfiguracaoContagemLimite.cs
│       ├── ContextoBancoDados.cs
│       └── Migracoes/<data>_LimiteRequisicoes.cs  # nova

tests/
├── PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/  # origem, chave, validador, decisão, pausa, bloqueio local
└── PedidoFulfillment.Testes.Integracao/
    ├── Infraestrutura/FabricaApi.cs             # limites altos, dublê sem banco, origem por teste
    ├── Infraestrutura/ArmazenamentoContagensMemoria.cs  # novo
    └── LimiteRequisicoes/                       # 429, duas instâncias, concorrência, espera, intermediários, falha, limpeza

specs/001-contratos-api/contracts/openapi-v1.yaml  # + 429 nas duas rotas
specs/016-observabilidade-opentelemetry/contracts/telemetria.md  # + métricas e alvo da limpeza
docs/fluxos/01-autenticacao.md, 11-limpeza-e-reenvio-da-dlq.md, 12-rastreabilidade-e-metricas.md, docs/proximos-passos.md
```

**Structure Decision**: os projetos existentes, com uma pasta `LimiteRequisicoes` em cada um, como `Notificacoes`.
As opções ficam na `Infrastructure`, como as demais, e a `Application` recebe um objeto pronto (`ConfiguracaoLimites`).
O `README.md` não muda sem permissão: o detalhe fica no fluxo 1.

## Complexity Tracking

Sem violações da constituição. Dois pontos a mais que a sugestão da issue:

| Ponto | Por quê | Alternativa mais simples descartada porque |
|-------|---------|-------------------------------------------|
| Middleware próprio em vez do `AddRateLimiter` | Chave vinda do corpo, dois limites atômicos e estado no banco (R-02) | O nativo guarda o estado no processo e tem particionador síncrono |
| Bloqueio local das recusas | Impede que uma inundação da mesma origem esgote as conexões e libere as tentativas pelo FR-012 (R-05) | Sem ele, o limite ficaria mais fraco justamente sob ataque |
