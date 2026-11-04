---

description: "Lista de tarefas do REQ-23 — Limite de requisições compartilhado no login e no registro"
---

# Tasks: Limite de requisições compartilhado no login e no registro

**Input**: Design documents from `/specs/023-rate-limiting/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/limite-requisicoes.md](./contracts/limite-requisicoes.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras e a spec pede integração com o 429, duas
instâncias, concorrência, espera e armazenamento fora (FR-023). Os testes da fundação vêm antes da implementação; nas
fases das histórias, descrevem o comportamento esperado, e os que falharem orientam o ajuste.

**Organization**: uma fundação com a decisão, o armazenamento e o middleware; depois uma fase por história. A US1 (login
em todas as instâncias) é o MVP. A US2 (registro), a US3 (contrato) e a US4 (operação) dependem só da fundação.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; rotas só com metadados; regras na `Application`; SQL na `Infrastructure`.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo.** Uma única migration, `LimiteRequisicoes`. Sem eventos novos.
- **Armazenamento: SQL Server, confirmado pelo usuário** ([R-01](./research.md#r-01-armazenamento-compartilhado-sql-server-ou-redis)).
- Janela fixa, iniciada na primeira requisição da contagem. Tempo sempre pelo relógio do banco (`SYSUTCDATETIME()`), nunca
  pelo `TimeProvider` da instância, exceto na pausa e no bloqueio local, que são por instância.
- Logs só com os IDs 3007 e 3008; nenhum valor de senha, e-mail completo ou endereço de origem em logs ou métricas.
- O bloqueio de conta do REQ-03 não muda (FR-017), e os testes existentes passam sem afrouxar asserções.
- Sem opção de desligar o limite (FR-018). `README.md` **não recebe acréscimos** sem permissão do usuário.
- Commit por fase: `tipo(escopo): descrição [REQ-23]`.

---

## Phase 1: Setup

- [X] T001 Registrar a linha de base com `dotnet build` e `dotnet test` em `PedidoFulfillment.sln` e anotar o total de testes para comparar na T040
- [X] T002 Acrescentar `LimiteRequisicoesExcedido = "LIMITE_REQUISICOES_EXCEDIDO"` em `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs`

**Checkpoint**: linha de base conhecida.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: decisão, armazenamento e middleware, sem nenhuma rota limitada ainda.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T003 [P] Escrever testes de `OrigemCliente` em `tests/PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/OrigemClienteTestes.cs`: IPv4 exato; IPv4 mapeado em IPv6 igual ao IPv4; dois IPv6 do mesmo `/64` iguais e de `/64` diferentes distintos; sem endereço = `desconhecida` (R-06)
- [X] T004 [P] Escrever testes de `GeradorChaveContagem` em `tests/PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/GeradorChaveContagemTestes.cs`: 32 bytes; determinístico; muda com política, origem, e-mail e chave; e-mail com outra caixa e espaços nas pontas gera a mesma chave; separador impede colisão entre origem e e-mail (R-08, FR-003, FR-010)
- [X] T005 [P] Escrever testes de `ValidadorOpcoesLimiteRequisicoes` em `tests/PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/ValidadorOpcoesLimiteRequisicoesTestes.cs`: padrões do FR-019 válidos; cada faixa do [contrato](./contracts/limite-requisicoes.md#configuração) recusada nas pontas; `ChaveContagem` ausente ou que não é o Base64 de 32 bytes; `IntermediariosConfiaveis` com IP ou CIDR inválido; todas as falhas juntas; a mensagem cita a chave, sem o valor
- [X] T006 Escrever testes de `ServicoLimiteRequisicoes` com portas falsas em `tests/PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/ServicoLimiteRequisicoesTestes.cs`: login com e-mail avalia duas políticas e sem e-mail só `login_origem`; registro avalia só `registro`; `Retry-After` arredondado para cima, mínimo 1 e a maior espera com duas recusas; recusa grava bloqueio local e a seguinte não chama o armazenamento; falha, ou tempo acima de `TempoMaximoEsperaMs`, libera a requisição, gera um log 3008 e abre a pausa; na pausa, não chama o armazenamento, mas o bloqueio local vale; fim da pausa volta a chamar; deadlock (1205) repete uma vez e não abre pausa; cancelamento pelo cliente propaga, sem log nem métrica; log 3007 só com `PrimeiroBloqueio` e e-mail mascarado; métrica em toda recusa, inclusive local (FR-012, FR-015, FR-020, FR-021, R-03 a R-05, R-08)

### Application

- [X] T007 [P] Criar `PoliticaLimite` (nome `login_origem_email`, `login_origem` ou `registro`, limite e janela em segundos) e `ConfiguracaoLimites` (políticas, chave em bytes, tempo máximo, pausa) em `src/PedidoFulfillment.Application/LimiteRequisicoes/PoliticaLimite.cs` e `ConfiguracaoLimites.cs`, sem depender de `Options`
- [X] T008 [P] Implementar `OrigemCliente` em `src/PedidoFulfillment.Application/LimiteRequisicoes/OrigemCliente.cs` até a T003 passar
- [X] T009 [P] Implementar `GeradorChaveContagem` em `src/PedidoFulfillment.Application/LimiteRequisicoes/GeradorChaveContagem.cs` (`HMAC-SHA256(chave, "politica\0origem\0EMAIL")`, e-mail com `Trim` e `ToUpperInvariant`) até a T004 passar
- [X] T010 Criar as portas em `src/PedidoFulfillment.Application/LimiteRequisicoes/`: `IArmazenamentoContagens` (recebe de uma a duas contagens e devolve aceita ou, por contagem recusada, a espera e `PrimeiroBloqueio`), `IBloqueiosLocais` e `IMetricasLimite`; e `EventosLimiteRequisicoes.cs` com o log 3007 `LimiteRequisicoesExcedido` e o 3008 `FalhaArmazenamentoLimite`, ambos Warning (contrato › Observabilidade)
- [X] T011 Implementar `IServicoLimiteRequisicoes` e `ServicoLimiteRequisicoes` em `src/PedidoFulfillment.Application/LimiteRequisicoes/` até a T006 passar, reutilizando `MascaraEmail` e usando `TimeProvider` só na pausa

### Infrastructure

- [X] T012 [P] Implementar `OpcoesLimiteRequisicoes` e `ValidadorOpcoesLimiteRequisicoes` em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimiteRequisicoes.cs`, com `FaixasOpcoes` e seção `LimiteRequisicoes`, e os padrões do contrato, até a T005 passar
- [X] T013 Criar `ContagemLimite` em `src/PedidoFulfillment.Infrastructure/Persistencia/ContagemLimite.cs` e `Configuracoes/ConfiguracaoContagemLimite.cs` com a tabela `ContagensLimite`: `Chave` `binary(32)` PK sem dados legíveis; `Contador` `int`; `FimJanela` `datetime2(3)`; `BloqueioRegistrado` `bit` padrão 0; índice `IX_ContagensLimite_FimJanela`. Registrar em `ContextoBancoDados.cs` e gerar a migration `LimiteRequisicoes` em `Persistencia/Migracoes/`
- [X] T014 Implementar `ArmazenamentoContagensSql` em `src/PedidoFulfillment.Infrastructure/LimiteRequisicoes/ArmazenamentoContagensSql.cs`: lote T-SQL único de [R-03](./research.md#r-03-atomicidade-dos-dois-limites-do-login) (chaves em ordem, `UPDLOCK, HOLDLOCK`, `DEADLOCK_PRIORITY HIGH`, janela vencida como zero, nenhuma escrita na recusa além da primeira, que grava `BloqueioRegistrado`, `SYSUTCDATETIME()`), executado **pelo EF Core** com `Database.SqlQueryRaw` e parâmetros, nunca por `SqlClient` direto (constituição), em um `ContextoBancoDados` com a string de conexão própria: `Application Name=oms-limite-requisicoes`, `Max Pool Size=20`, `Min Pool Size=2` (R-04)
- [X] T015 Escrever testes de `ArmazenamentoContagensSql` com banco real em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/ArmazenamentoContagensSqlTestes.cs`: exatamente o limite aceito com 32 simultâneas; barrada por uma contagem não consome a outra; janela vencida recomeça e zera `BloqueioRegistrado`; `PrimeiroBloqueio` uma só vez por janela; espera coerente; nenhuma linha com dados legíveis; p95 até 50 ms em 200 operações com chaves novas, em uma coleção que não roda em paralelo (FR-002, FR-007, FR-008, FR-010, SC-008)
- [X] T016 [P] Implementar `BloqueiosLocaisMemoria` (`IMemoryCache` com até 100 mil entradas, R-05) e `MetricasLimite` em `src/PedidoFulfillment.Infrastructure/LimiteRequisicoes/`, com as métricas `oms.limite_requisicoes.recusadas` (atributo `oms.limite.politica`) e `oms.limite_requisicoes.falhas_armazenamento` em `src/PedidoFulfillment.Infrastructure/Observabilidade/TelemetriaOms.cs`
- [X] T017 Criar `ExtensoesLimiteRequisicoes` em `src/PedidoFulfillment.Infrastructure/LimiteRequisicoes/ExtensoesLimiteRequisicoes.cs`: opções com `ValidateOnStart`, `ConfiguracaoLimites` pronta e registros do serviço e das portas, só na API (o Worker não usa a chave); e `AquecimentoLimiteRequisicoes` em `src/PedidoFulfillment.Infrastructure/LimiteRequisicoes/AquecimentoLimiteRequisicoes.cs`, serviço em segundo plano que abre o pool ao subir, sem o tempo máximo de 250 ms, sem impedir a partida e, se falhar, só com log Debug (R-04)

### API

- [X] T018 [P] Implementar `LeitorEmailLogin` em `src/PedidoFulfillment.API/LimiteRequisicoes/LeitorEmailLogin.cs` (R-09: `EnableBuffering`, até 16 KB, `email` sem diferenciar maiúsculas, corpo volta ao início) e testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/LeitorEmailLoginTestes.cs`: e-mail válido; corpo maior que 16 KB, JSON inválido, `email` ausente, não texto ou com mais de 256 caracteres devolvem nulo; o corpo continua legível depois
- [X] T019 [P] Implementar `ConfiguracaoIntermediarios` em `src/PedidoFulfillment.API/LimiteRequisicoes/ConfiguracaoIntermediarios.cs`: só `XForwardedFor`, `KnownProxies` e `KnownIPNetworks` limpas antes de receber a lista configurada e `ForwardLimit = null` (R-06)
- [X] T020 Criar `MetadadosLimiteRequisicoes` e `MiddlewareLimiteRequisicoes` em `src/PedidoFulfillment.API/LimiteRequisicoes/`: sem metadados na rota, passa adiante; com metadados, resolve a origem, lê o e-mail no login, chama o serviço e, na recusa, grava `Retry-After` e responde com `Falha` por `ParaProblema(falha, 429)`, com título e detalhe do [contrato](./contracts/limite-requisicoes.md#resposta-429)
- [X] T021 Ligar o pipeline em `src/PedidoFulfillment.API/Program.cs`: registros; `UseForwardedHeaders` antes de tudo; `MiddlewareLimiteRequisicoes` depois de `UsarProblemDetails` e antes de `UseAuthentication`. Acrescentar os limites a `src/PedidoFulfillment.API/appsettings.json` (sem a chave) e `ChaveContagem` de desenvolvimento a `appsettings.Development.json`
- [X] T022 Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaApi.cs`: `ChaveContagem` de teste, limites de 1.000.000, `TempoMaximoEsperaMs=5000` e `PausaAposFalhaSegundos=0` por padrão, sobrescrevíveis por `Configuracoes` (R-10); `ArmazenamentoContagensMemoria` (novo, em `Infraestrutura/`) quando não há `StringConexao`; propriedade para definir o endereço da conexão por um `IStartupFilter`, antes dos cabeçalhos encaminhados (R-10). Rodar toda a suíte: nada muda

**Checkpoint**: tudo compila, a suíte segue verde, e nenhuma rota está limitada.

---

## Phase 3: User Story 1 — O limite do login vale em todas as instâncias (Priority: P1) 🎯 MVP

**Goal**: as duas políticas do login, com o estado compartilhado entre instâncias.

**Independent Test**: duas `FabricaApi` sobre o mesmo banco; esgotar por uma e receber 429 na outra.

- [ ] T023 [US1] Ligar `/login` às políticas `login_origem_email` e `login_origem` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`
- [ ] T024 [US1] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/LimiteLoginTestes.cs`, com banco real e limites baixos: esgotado na instância A, 429 na B, sem avaliar a senha nem contar para o bloqueio de conta; rajada de 100 tentativas simultâneas repartidas entre A e B com no máximo o limite aceito (SC-001, SC-002); um e-mail por tentativa contido pelo limite por origem (SC-009); outra origem e outro e-mail não barrados; e-mail com outra caixa e espaços é o mesmo; e-mail sem conta tem a mesma resposta; insistir em um e-mail barrado não consome o limite por origem; corpo inválido conta por origem e segue respondendo 400; depois do `Retry-After`, aceita em qualquer instância (SC-004); contagem sobrevive ao descarte de uma instância (FR-009); `Retry-After` com até 1 s de diferença entre as instâncias (SC-003); instância B com um `TimeProvider` adiantado em 10 minutos toma a mesma decisão e informa o mesmo `Retry-After` (FR-008)
- [ ] T025 [US1] Provar o bloqueio de conta sem mudanças em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/LimiteLoginTestes.cs`: dentro do limite, 5 erros seguidos continuam bloqueando a conta com o mesmo 401 (FR-017)

**Checkpoint**: MVP entregue: a força bruta é contida em todas as instâncias.

---

## Phase 4: User Story 2 — O limite do registro vale em todas as instâncias (Priority: P1)

**Goal**: a política `registro`.

**Independent Test**: esgotar por uma instância com e-mails diferentes; a outra recusa, sem criar a conta.

- [ ] T026 [US2] Ligar `/register` à política `registro` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`
- [ ] T027 [US2] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/LimiteRegistroTestes.cs`: esgotado na A, 429 na B, e a conta não é criada; outra origem é aceita; a contagem do registro não afeta o login da mesma origem, nem a do login afeta o registro (FR-005, FR-014)

**Checkpoint**: as duas rotas estão protegidas.

---

## Phase 5: User Story 3 — O cliente da API sabe o que aconteceu (Priority: P2)

**Goal**: 429 padronizado e documentado no contrato v1.

**Independent Test**: exceder o limite e conferir a resposta contra o contrato publicado.

- [ ] T028 [P] [US3] Acrescentar o `429` às duas rotas em `specs/001-contratos-api/contracts/openapi-v1.yaml`, com `NegocioProblemDetails`, exemplo com `LIMITE_REQUISICOES_EXCEDIDO` e o cabeçalho `Retry-After` inteiro
- [ ] T029 [US3] Gerar o mesmo contrato em `src/PedidoFulfillment.API/OpenApi/TransformadorSchemasErroOpenApi.cs` e `TransformadorCabecalhosOpenApi.cs`, a partir de `MetadadosLimiteRequisicoes` e de `ProduzErros` em `EndpointsAutenticacao.cs`. O `429` do SSE não muda
- [ ] T030 [US3] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/RespostaLimiteTestes.cs`: `application/problem+json`, `title`, `detail`, `instance`, `traceId`, `codigo` e `Retry-After` inteiro ≥ 1; corpo igual para conta existente, inexistente e bloqueada, sem os valores dos limites (FR-013); `ConformidadeOpenApiTestes` e o comparador de contrato verdes (SC-006)

**Checkpoint**: contrato publicado e sem mudanças incompatíveis.

---

## Phase 6: User Story 4 — A operação ajusta os limites e enxerga os bloqueios (Priority: P2)

**Goal**: configuração validada, intermediários, sinais, falha do armazenamento e limpeza.

**Independent Test**: subir com um limite inválido e ver a partida falhar; provocar um bloqueio e ver o log e a métrica.

- [ ] T031 [P] [US4] Escrever testes de partida em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/ConfiguracaoLimiteTestes.cs`: limite inválido ou sem `ChaveContagem` impede a partida, citando a chave e sem o valor (FR-018), inclusive fora do ambiente `Testes`
- [ ] T032 [P] [US4] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/IntermediariosTestes.cs`: `X-Forwarded-For` de quem não é confiável é ignorado, inclusive de loopback; de intermediário confiável, conta o cliente real; valor forjado à esquerda da cadeia é ignorado; IPv6 do mesmo `/64` divide a contagem (FR-004)
- [ ] T033 [P] [US4] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/ObservabilidadeLimiteTestes.cs`, com `CapturaLogs` e `ColetorTelemetria`: um log 3007 por contagem por janela e a métrica em toda recusa; o conjunto de logs e métricas não contém senha, e-mail completo nem o endereço de origem (FR-020, FR-021, SC-007); os atributos dos spans das duas rotas também não trazem o endereço de origem, ou o achado é registrado para decidir (R-06)
- [ ] T034 [P] [US4] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/ArmazenamentoIndisponivelTestes.cs`: com um dublê que falha e com um que demora, as rotas devolvem a resposta normal, com um log 3008 por pausa e a métrica por requisição, e nada da falha chega ao cliente; depois da pausa, o limite volta; com o banco fora na partida, o aquecimento não impede a API de subir (FR-012, SC-010, R-04)
- [ ] T035 [US4] Acrescentar `IntervaloContagensSegundos` (60, de 10 a 3.600) e `LotesContagensPorRodada` (100, de 1 a 1.000) a `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimpeza.cs` e ao validador
- [ ] T036 [US4] Implementar `LimpezaContagensLimite` em `src/PedidoFulfillment.Infrastructure/LimiteRequisicoes/LimpezaContagensLimite.cs` (R-07: lock de aplicação `oms-limpeza-contagens`, `DELETE TOP` por `FimJanela < SYSUTCDATETIME()`, `DEADLOCK_PRIORITY LOW`, pelo `Database.ExecuteSql`, obedece a `Limpeza:Habilitada` e soma em `oms.limpeza.linhas_apagadas`, alvo `contagens_limite`) e registrar em `src/PedidoFulfillment.Infrastructure/Mensageria/ExtensoesLimpezaReenvio.cs`
- [ ] T037 [US4] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/LimpezaContagensLimiteTestes.cs`: apaga as vencidas, mantém as da janela atual; respeita o lote; outra instância com o lock pula a rodada; desligada por `Limpeza:Habilitada`; com a limpeza rodando e tentativas simultâneas, a contagem continua atômica (FR-011)

**Checkpoint**: todas as histórias entregues e operáveis.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T038 [P] Atualizar `docs/fluxos/01-autenticacao.md` (429 na tabela de erros; os limites, a janela, a origem e o `IntermediariosConfiaveis`; falha do armazenamento; logs 3007 e 3008; limitações: excesso na virada da janela, bloqueio de conta por terceiro, troca de endereço, inundação de muitas origens que deixa a instância sem o limite), `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md` (rotina `LimpezaContagensLimite`), `docs/fluxos/12-rastreabilidade-e-metricas.md` e `specs/016-observabilidade-opentelemetry/contracts/telemetria.md` (duas métricas e o alvo `contagens_limite`), conferindo mensagens, códigos, limites e IDs no código e os links relativos
- [ ] T039 [P] Atualizar `docs/proximos-passos.md`: acrescentar o REQ-23 ao resumo, numeração a partir do REQ-24, remover a #58 da fila e registrar a opção do Redis (R-01) em "Para análise futura". **Não acrescentar nada ao `README.md`**: se algo parecer necessário, perguntar antes
- [ ] T040 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`, comparando o total de testes com a T001
- [ ] T041 Executar o [quickstart](./quickstart.md), inclusive a verificação sob carga (seção 4: p95 da criação de pedidos até 20% acima da linha de base e lote parado no banco), e registrar o resultado em `specs/023-rate-limiting/research.md` na R-01; se não atender, avisar o usuário antes de abrir o PR
- [ ] T042 Abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #58`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, a nova chave obrigatória `LimiteRequisicoes:ChaveContagem`, o `IntermediariosConfiaveis` em produção e o desvio da issue (middleware próprio em vez do limitador nativo, R-02); depois, acompanhar os workflows, inclusive o `build-testes` e o Quality Gate, e usar `gh pr merge --rebase --auto`, sem apagar a branch. Marcar esta tarefa em `specs/023-rate-limiting/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (1)** → **Foundational (2)** → **US1, US2, US3 e US4 (3 a 6)** → **Polish (7)**.

### User Story Dependencies

- **US1 e US2**: só da fundação. Compartilham `EndpointsAutenticacao.cs`, então uma de cada vez.
- **US3**: a T029 usa os metadados da fundação; os testes da T030 usam a US1 ou a US2 já ligadas.
- **US4**: T031, T033 e T034 usam uma rota já ligada (US1). T035 → T036 → T037.

### Within Foundational

- T003 → T008; T004 → T009; T005 → T012; T006 → T011.
- T007 → T008, T009, T010; T010 → T011 e T014; T013 → T014 → T015.
- T011, T012, T014 e T016 → T017; T017, T018, T019 e T020 → T021 → T022.

### Parallel Opportunities

```text
Testes da fundação: T003, T004, T005
Application:        T007, depois T008 e T009
Infra e API:        T012, T016, T018, T019
US4:                T031, T032, T033, T034
Polish:             T038, T039
```

## Implementation Strategy

1. **MVP**: fases 1 e 2 e a US1. Já contém a força bruta entre instâncias.
2. **Incremental**: US2, depois US3, depois US4, com um commit por fase.
3. **Antes do PR**: quickstart e verificação sob carga. É ela que confirma a escolha do SQL Server.

## Notes

- Se a verificação sob carga mostrar impacto nos pedidos, a troca pelo Redis fica restrita a outra implementação de
  `IArmazenamentoContagens`, mas é uma decisão do usuário, não um ajuste automático.
- Lembre-se de que o `TestServer` não informa o endereço da conexão: use a propriedade da T022.
- Correção de code smells neste REQ usa `Refs #61`, nunca `Closes`, e atualiza a tabela da issue.
