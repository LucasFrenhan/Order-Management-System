# Implementation Plan: Ações da operação sobre estornos pendentes e evento de estorno travado

**Branch**: `feature/req-28-acoes-estornos-pendentes` | **Date**: 2026-11-10 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/028-acoes-estornos-pendentes/spec.md`

## Summary

O estorno continua dentro do `Pagamento`, cujo `Id` passa a ser exposto como `estornoId` (research
R-01). Quatro rotas novas em `/api/v1/estornos`: listagem paginada dos pendentes e consulta (Admin e
OperadorLogistica), tentativa forçada (mesmos perfis; a API marca e o Worker executa, primeiro da
fila, R-04) e baixa manual (só o Admin ativo no banco, R-10). A baixa pede o mesmo lock da rodada,
`sp_getapplock 'oms-estornos'`, dentro da transação, esperando até 5 s; sem ele, 409 (R-03). A baixa
leva o pagamento à situação nova `EstornoBaixado` e grava o `pagamento.estorno-baixado` v1 e o aviso
`pedido.estorno-confirmado` (R-07). A falha que passa do limite de alerta grava, uma única vez, o
`pagamento.estorno-atrasado` v1 (R-06). O último erro passa a ser guardado só por categorias sem dado
sensível (R-05). Logs 7021 a 7024 e o medidor `oms.estornos.pendentes` completam a visibilidade
(R-11, R-12).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: as já em uso (ASP.NET Core Minimal APIs, EF Core SQL Server,
RabbitMQ.Client, OpenTelemetry). Nenhuma dependência nova.

**Storage**: SQL Server: sete colunas anuláveis, uma restrição nova, uma recriada e dois índices
filtrados em `Pagamentos` (migração `AcoesEstornos`, [data-model](./data-model.md))

**Testing**: xUnit v3, FluentAssertions, `FabricaApi` e `FabricaWorker` com Testcontainers (SQL
Server e RabbitMQ), gateway falso do `CenarioEstorno`, `SchemaEventosTestes` e a conformidade do
`openapi-v1.yaml`

**Target Platform**: API e Worker em contêiner Linux (e local em Windows)

**Project Type**: web-service (Minimal API) com Worker separado

**Performance Goals**: sem regressão na rodada: a primeira etapa da fila (forçados) usa índice
filtrado e costuma vir vazia; a contagem do medidor é uma consulta por rodada sobre o índice filtrado

**Constraints**: contratos v1 só por acréscimo; nenhum identificador do gateway, motivo da baixa ou
dado pessoal em log ou evento (a referência externa vai só no evento da baixa); a baixa espera no
máximo 5 s; a API continua sem falar com o gateway

**Scale/Scope**: ~20 arquivos de produção (Domain, Application, Infrastructure, API), uma migração,
~6 arquivos de teste novos e 6 documentos

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação |
|-------------------|----------|
| I. Specification-First | ✅ Spec e clarify aprovados (REQ-28, #57). O FR-003 foi ajustado no plan (ordem da listagem, R-08) e registrado na spec |
| II. Contratos imutáveis | ✅ Rotas, schemas e eventos novos, só acréscimos; `pagamento.estornado` e `pedido.estorno-confirmado` inalterados; `SituacaoPagamento` continua fora dos contratos ([contrato](./contracts/estornos.md)) |
| III. Segurança por padrão | ✅ Políticas novas (R-10); baixa só para Admin ativo no banco; 400 em `ProblemDetails`; último erro sanitizado (R-05); motivo fora de logs e eventos |
| IV. Desacoplamento assíncrono | ✅ Eventos e aviso pelo outbox, na mesma transação; a tentativa forçada é registrada pela API e executada pelo Worker |
| V. Qualidade verificável | ✅ Unitários do domínio e do `ServicoEstornos`; integração com 401/403 de todas as rotas, concorrência com a rodada e schemas dos eventos |
| Endpoint só roteia | ✅ Endpoints chamam o `ServicoAcoesEstornos`; regras no domínio e no serviço |
| Dependência NuGet nova | ✅ Nenhuma |
| Nomes em português | ✅ `ServicoAcoesEstornos`, `IBloqueioRodadaEstornos`, `EstornoAtrasadoEm`, eventos no passado (`estorno-atrasado`, `estorno-baixado`) |
| Commits `tipo(escopo): ... [REQ-28]` | ✅ Um por etapa e por fase |

**Pós-design**: sem violações; a tabela de Complexity Tracking não se aplica.

## Project Structure

### Documentation (this feature)

```text
specs/028-acoes-estornos-pendentes/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── estornos.md
│   └── eventos/
│       ├── pagamento.estorno-atrasado.v1.schema.json
│       └── pagamento.estorno-baixado.v1.schema.json
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/PedidoFulfillment.Domain/
├── Pagamentos/SituacaoPagamento.cs           # EstornoBaixado
├── Pagamentos/Pagamento.cs                   # campos novos; PedirTentativaEstorno, MarcarEstornoAtrasado, DarBaixaEstorno; falha com erro
└── Pedidos/Pedido.cs                         # repasses dos métodos novos

src/PedidoFulfillment.Application/
├── Pagamentos/ServicoEstornos.cs             # último erro por categoria, evento de atraso, marca limpa
├── Pagamentos/ServicoAcoesEstornos.cs        # novo: listar, obter, forçar, dar baixa (com o bloqueio)
├── Pagamentos/IBloqueioRodadaEstornos.cs     # novo: porta do lock com espera
├── Pagamentos/IConsultaEstornos.cs           # novo: listagem paginada e leitura para a API
├── Pagamentos/CodificadorCursorEstornos.cs   # novo: cursor (EstornoSolicitadoEm, Id)
├── Pagamentos/RegrasAlertaEstorno.cs         # novo: regra única do alerta (7008, listagem, medidor)
├── Pagamentos/DescricaoFalhaEstorno.cs       # novo: categorias do R-05
├── Pagamentos/EventosPagamento.cs            # 7021 a 7024
├── Contratos/Eventos/Eventos.cs, TiposEvento.cs   # PagamentoEstornoAtrasado, PagamentoEstornoBaixado
├── Contratos/Estornos/                       # novo: DTOs do contrato
└── Contratos/Erros/                          # códigos ESTORNO_*

src/PedidoFulfillment.Infrastructure/
├── Pagamentos/ConsultaEstornosPendentes.cs   # fila em duas etapas (forçados primeiro)
├── Pagamentos/ConsultaEstornos.cs            # novo: listagem e leitura da API
├── Pagamentos/BloqueioRodadaEstornos.cs      # novo: sp_getapplock por transação, 5 s
├── Pagamentos/ProcessadorEstornos.cs         # contagem para o medidor no começo da rodada
├── Configuracao/OpcoesEstornos.cs            # parâmetros de alerta também na API
├── Observabilidade/TelemetriaOms.cs          # oms.estornos.pendentes
├── Persistencia/ArmazenamentoPedidos.cs      # guarda e aviso das transições novas
├── Persistencia/Configuracoes/ConfiguracaoPagamento.cs  # colunas, restrições e índices
└── Persistencia/Migracoes/<data>_AcoesEstornos.cs       # nova

src/PedidoFulfillment.API/
├── Endpoints/EndpointsEstornos.cs            # novo: quatro rotas
├── Endpoints/ParametrosListarEstornos.cs     # novo
├── Seguranca/PoliticasAutorizacao.cs         # PoliticaEstornos, PoliticaBaixaEstorno
└── Program.cs                                # registros

specs/001-contratos-api/contracts/openapi-v1.yaml   # rotas e schemas novos

tests/PedidoFulfillment.Testes.Unitarios/
├── Dominio/PagamentoAcoesEstornoTestes.cs    # transições, idempotência da marca, atraso uma vez
└── Pagamentos/ServicoEstornosTestes.cs       # categorias do erro, evento de atraso, marca limpa

tests/PedidoFulfillment.Testes.Integracao/
├── Estornos/ListagemEstornosTestes.cs        # novo: listagem, consulta, cursor, alerta, 401/403/404
├── Estornos/TentativaForcadaTestes.cs        # novo: 202, fila, circuito, 409, 401/403
├── Estornos/BaixaEstornoTestes.cs            # novo: baixa, validação, Admin ativo, eventos e aviso, 409
├── Estornos/BaixaContraRodadaTestes.cs       # novo: lock segurado, espera, 409, rodada sem gateway
├── Estornos/EstornoAtrasadoTestes.cs         # novo: uma vez com 10 falhas e dois Workers
├── Mensageria/SchemaEventosTestes.cs         # dois schemas novos
└── PedidoFulfillment.Testes.Integracao.csproj # copia os schemas do 028

docs/
├── fluxos/10-estorno.md                      # rotas, situação nova, fila, logs, evento, limitações
├── fluxos/08-publicacao-e-consumo-de-eventos.md  # dois eventos
├── fluxos/12-rastreabilidade-e-metricas.md   # medidor
├── fluxos/13-notificacao-de-status.md        # aviso também na baixa
├── fluxos/README.md                          # estados do pagamento, se o índice os mostrar
├── proximos-passos.md                        # #57 sai da fila
└── estado-atual.md                           # linha do REQ-28
```

**Structure Decision**: as regras ficam no domínio (`Pagamento`) e na Application
(`ServicoAcoesEstornos`, `ServicoEstornos`); o lock, as consultas e a métrica no Infrastructure; a API
só roteia. O Worker muda pela consulta da fila, pela contagem do medidor e pelo `ServicoEstornos`.

## Fases de implementação (para o /speckit-tasks)

1. **Fundação**: situação e campos no domínio, migração, configuração do EF, `RegrasAlertaEstorno`,
   `DescricaoFalhaEstorno`, contratos dos eventos e dos DTOs, códigos de erro, políticas e
   `OpcoesEstornos` na API.
2. **US1 (P1), visibilidade**: `IConsultaEstornos`, cursor, rotas de listagem e consulta, último erro
   gravado pela rodada; testes de listagem, consulta, alerta, cursor e 401/403/404.
3. **US2 (P1), baixa**: `IBloqueioRodadaEstornos`, `DarBaixaEstorno`, guarda e aviso no
   `ArmazenamentoPedidos`, rota; testes de baixa, validação, Admin ativo, eventos e aviso, e a
   concorrência com a rodada.
4. **US3 (P2), evento de atraso**: `MarcarEstornoAtrasado` e o evento no `ServicoEstornos`, guarda,
   log 7024; testes de uma vez só, de estorno antigo e do circuito aberto.
5. **US4 (P3), tentativa forçada**: marca, fila em duas etapas, marca limpa, rota; testes da fila, do
   circuito e de 409/401/403.
6. **Polish**: medidor, OpenAPI e conformidade, schemas no `SchemaEventosTestes`, fluxos 8, 10, 12 e
   13, `proximos-passos.md`, `estado-atual.md`, roteiro do quickstart. O README não muda (se algo
   precisar entrar nele, perguntar antes).

## Riscos

- **Baixa x rodada longa**: com o gateway lento, uma rodada pode passar de 5 s, e a baixa responde
  409 várias vezes. Aceito no clarify; o log 7023 mostra a frequência, e a operação tenta de novo.
- **Rodada pulada pela baixa**: enquanto a baixa segura o lock (milissegundos), o Worker pula a rodada
  (7011). O efeito é no máximo um intervalo de atraso.
- **Configuração de alerta divergente** entre API e Worker: a listagem e o evento discordariam. O
  fluxo 10 documenta que as duas chaves devem ser iguais nos dois processos.
- **Devolução dupla fora do sistema**: se o gateway confirmou e a gravação se perdeu, uma baixa
  posterior devolve de novo por fora. Fica nas limitações do fluxo 10, com a orientação de consultar o
  gateway antes da baixa.
- **Versão de linha do pedido**: a rodada e as rotas dependem de a mudança no `Pagamento` atualizar a
  versão do `Pedido`; conferido na implementação (research, Conferências).

## Complexity Tracking

Sem violações da constituição.
