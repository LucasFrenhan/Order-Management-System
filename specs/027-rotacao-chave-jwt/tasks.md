---

description: "Lista de tarefas do REQ-27 — Rotação da chave de assinatura do JWT"
---

# Tasks: Rotação da chave de assinatura do JWT

**Input**: Design documents from `/specs/027-rotacao-chave-jwt/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/configuracao.md](./contracts/configuracao.md),
[contracts/token-acesso.md](./contracts/token-acesso.md), [quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A spec lista os testes no FR-013, e a constituição exige os 401 dos endpoints protegidos.

**Organization**: o formato antigo deixa de ser aceito (FR-007), então a fundação já troca a configuração, a emissão e a
validação, para a suíte inteira voltar a ficar verde no fim dela. As histórias acrescentam o que cada uma prova: US1 a
transição sem 401 e o log da partida; US2 a recusa e o log 3027; US3 a partida barrada e a chave do cursor; US4 os
documentos. **O MVP são a fundação e as duas histórias P1 (US1 e US2).**

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US4)

## Convenções válidas para todas as tarefas

- Nomes em português; tudo na API (`src/PedidoFulfillment.API/Seguranca/`); o Worker não muda.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Nenhum pacote novo.** Sem migration. Contrato v1 (OpenAPI) inalterado.
- **Nenhum valor de chave** em mensagem de validação, exceção, log ou teste que imprima a configuração (FR-006). O
  identificador pode aparecer; o identificador fora do formato aparece só pela posição na lista (R-04).
- Logs só com os IDs 3026 e 3027 ([contrato](./contracts/configuracao.md#logs)). O 3027 nunca registra o token, o
  `sub` nem o endereço de origem.
- Identificador: "de 1 a 64 caracteres em `[A-Za-z0-9._-]`; único na lista (comparação ordinal)". Valor: "no mínimo 32
  bytes em UTF-8". Conjunto: "de 1 a 3 itens" (data-model).
- Os testes existentes passam sem afrouxar asserções. O que muda neles é só a forma de configurar e de gerar a chave
  de teste (T002, T003) e a asserção nova do `kid` (T011).
- Artefatos do Docker criados pelos testes não ficam na máquina: retrato antes (`docker ps -a`, `docker volume ls`) e
  conferência no fim (T031).

---

## Phase 1: Setup (migração da configuração de desenvolvimento e de testes)

**Purpose**: trocar o formato antigo pelo novo, com o mesmo valor de hoje, onde a configuração é versionada.

**Atenção**: a API só volta a subir com estes valores depois da fase 2 (o validador antigo ainda exige
`Jwt:ChaveAssinatura`). Faça as fases 1 e 2 juntas antes de rodar a suíte.

- [ ] T001 [P] Em `src/PedidoFulfillment.API/appsettings.Development.json`, trocar `"ChaveAssinatura"` por `"Chaves": [ { "Identificador": "desenvolvimento-local", "Valor": "SOMENTE-DESENVOLVIMENTO-chave-local-de-32-bytes-ou-mais" } ]` e `"ChaveAtiva": "desenvolvimento-local"`, com o mesmo valor de hoje (mantém os cursores, R-06) e um comentário `// SOMENTE DESENVOLVIMENTO (REQ-27): rotação em docs/rotacao-chave-jwt.md`.
- [ ] T002 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/FabricaApi.cs`, criar `public const string IdentificadorChaveTeste = "teste-a";` e trocar `builder.UseSetting("Jwt:ChaveAssinatura", ChaveTeste)` por `Jwt:Chaves:0:Identificador = IdentificadorChaveTeste`, `Jwt:Chaves:0:Valor = ChaveTeste` e `Jwt:ChaveAtiva = IdentificadorChaveTeste`, antes da aplicação de `Configuracoes` (que continua podendo sobrescrevê-los).
- [ ] T003 [P] Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/GeradorTokenTeste.cs`, acrescentar os parâmetros opcionais `string? chave = null` (padrão `FabricaApi.ChaveTeste`) e `string? identificadorChave = FabricaApi.IdentificadorChaveTeste` (com `null`, o token sai sem `kid`), preenchendo `SymmetricSecurityKey.KeyId`. O `assinaturaInvalida: true` continua assinando com outra chave e o mesmo `kid`.

---

## Phase 2: Foundational (conjunto de chaves, validação, emissão e verificação por `kid`)

**Purpose**: a API sobe com o formato novo, emite com a chave ativa e valida pelo `kid`. Bloqueia todas as histórias.

- [ ] T004 [P] Testes unitários das regras do validador (sem subir a API nem fazer I/O; ficam no projeto de integração porque o de unidade não referencia a API, R-07; dizer isso no comentário da classe), em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/ValidadorOpcoesJwtTestes.cs`: configuração válida com 1 e com 3 chaves; `ChaveAssinatura` preenchida (sozinha e junto com o formato novo); nenhuma chave; 4 chaves; identificador vazio, com 65 caracteres, com espaço, com acento e com `:`; identificador repetido; identificadores que diferem só em maiúsculas (aceitos, comparação ordinal); valor com 31 bytes UTF-8 (e um com 31 caracteres mas mais de 32 bytes por acento, aceito); `ChaveAtiva` vazia; `ChaveAtiva` fora da lista; emissor ou audiência vazios. Cada caso confere a mensagem exata do [contrato](./contracts/configuracao.md#mensagens-de-falha-na-partida), que todas as falhas saem juntas e que nenhuma mensagem contém o valor de uma chave.
- [ ] T005 Em `src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs`, reescrever `OpcoesJwt`: `Emissor`, `Audiencia`, `DuracaoTokenMinutos` (como hoje), `List<ChaveJwtConfigurada> Chaves`, `string ChaveAtiva` e `string ChaveAssinatura` (só para detectar a configuração antiga), com `ChaveJwtConfigurada { string Identificador; string Valor }` no mesmo arquivo (data-model).
- [ ] T006 Criar `src/PedidoFulfillment.API/Seguranca/ValidadorOpcoesJwt.cs` (`IValidateOptions<OpcoesJwt>`) com as regras do FR-005 e do FR-007 e as mensagens exatas do contrato, juntando todas as falhas num só `ValidateOptionsResult.Fail`. Formato do identificador por regex `^[A-Za-z0-9._-]{1,64}$`; tamanho do valor por `Encoding.UTF8.GetByteCount`. Até T004 passar.
- [ ] T007 Criar `src/PedidoFulfillment.API/Seguranca/ConjuntoChavesJwt.cs` (singleton, a partir de `IOptions<OpcoesJwt>` já validadas): `Ativa` (`SymmetricSecurityKey` com `KeyId`), `Todas` (as chaves aceitas, a ativa inclusive, cada uma com `KeyId`), `ValorAtiva` (bytes UTF-8 da ativa) e `Contem(string identificador)` com comparação ordinal (R-03).
- [ ] T008 Em `ConfiguracaoJwt.AdicionarAutenticacaoJwt` (`src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs`), trocar os `.Validate(...)` em linha pelo `ValidadorOpcoesJwt` com `ValidateOnStart`, registrar o `ConjuntoChavesJwt` e montar os `TokenValidationParameters` com `IssuerSigningKeys = conjunto.Todas`, `TryAllIssuerSigningKeys = false` e um `IssuerSigningKeyResolver` que devolve só a chave cujo `KeyId` é igual ao `kid` em comparação ordinal (nenhuma sem `kid` ou com `kid` desconhecido), mantendo `ValidAlgorithms = [HmacSha256]`, emissor, audiência, validade, `ClockSkew` de 30 s e os eventos de 401/403 de hoje (R-02).
- [ ] T009 Em `src/PedidoFulfillment.API/Seguranca/EmissorTokenJwt.cs`, assinar com `ConjuntoChavesJwt.Ativa` (com `KeyId`, que põe o `kid` no cabeçalho) no lugar da `SymmetricSecurityKey` de `ChaveAssinatura`; atualizar o comentário da classe (R-03).
- [ ] T010 Em `src/PedidoFulfillment.API/Seguranca/ChaveCursorDerivadaDoJwt.cs`, derivar do `ConjuntoChavesJwt.ValorAtiva` em vez de `OpcoesJwt.ChaveAssinatura` (mesmo HKDF e mesmo contexto), só para manter a build; a classe é substituída na US3 (T021).
- [ ] T011 Rodar `dotnet build` e `dotnet test` e corrigir o que o formato novo quebrar, sem afrouxar asserções; em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/EmissorTokenTestes.cs`, acrescentar ao teste do contrato do token a asserção `token.Kid.Should().Be(FabricaApi.IdentificadorChaveTeste)` (FR-002).

**Checkpoint**: suíte inteira verde com o formato novo; tokens saem com `kid`.

---

## Phase 3: User Story 1 - Rotacionar a chave sem derrubar as sessões (Priority: P1) 🎯 MVP

**Goal**: as três etapas da rotação (acrescentar, ativar, retirar) não recusam nenhum token válido, e cada instância diz na
partida qual chave usa.

**Independent Test**: emitir com A; configurar A e B com B ativa e confirmar o token de A aceito e os novos com `kid` B;
retirar A e confirmar os tokens de B aceitos.

- [ ] T012 [P] [US1] Testes em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/RotacaoChaveJwtTestes.cs`, com `FabricaApi.Configuracoes` para cada etapa e `GET /api/v1/pedidos/{id}` com o `ServicoPedidosFalso`, como em `Autenticacao/EmissorTokenTestes.cs` (sem banco): (1) A ativa e B aceita: o `IEmissorToken` emite com `kid` A, e tokens de A e de B são aceitos; (2) B ativa e A aceita: emite com `kid` B, e um token de A ainda válido recebe 200 (cenário 2 da US1); (3) só B: token de B aceito; (4) as três etapas em sequência, com o mesmo token de A até a etapa 2 e o de B até a 3, sem nenhum 401 (SC-001).
- [ ] T013 [US1] Criar `src/PedidoFulfillment.API/Seguranca/EventosChavesJwt.cs` com o `[LoggerMessage(EventId = 3026, EventName = "ChavesJwtCarregadas", Level = LogLevel.Information, Message = "Chaves do JWT: ativa {ChaveAtiva}; aceitas {ChavesAceitas}.")]`, com `-` quando não há aceitas (FR-015).
- [ ] T014 [US1] Criar `src/PedidoFulfillment.API/Seguranca/RegistroChavesJwtNaPartida.cs` (`IHostedService` que, no `StartAsync`, lê o `ConjuntoChavesJwt` e emite o 3026, sem valores) e registrá-lo em `AdicionarAutenticacaoJwt` (`src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs`).
- [ ] T015 [US1] Em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/RotacaoChaveJwtTestes.cs`, testar com `CapturaLogs` o 3026 com uma chave (`aceitas -`) e com três (ativa e as duas aceitas, na ordem da configuração), e que nenhum registro contém o valor de uma chave.

**Checkpoint**: US1 verificável sozinha (quickstart 3.1 a 3.3).

---

## Phase 4: User Story 2 - Recusar o token de uma chave retirada (Priority: P1) 🎯 MVP

**Goal**: o token de chave retirada, sem `kid` ou com `kid` desconhecido recebe o 401 de hoje, com o log 3027; o cliente
renova a sessão e volta a funcionar sem senha.

**Independent Test**: emitir com A, retirar A, confirmar 401 e 3027; renovar e usar o token novo.

- [ ] T016 [P] [US2] Testes em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/RotacaoChaveJwtTestes.cs` (mesma rota e mesmo `ServicoPedidosFalso` da T012): token de chave retirada ainda dentro da validade → 401 com o mesmo corpo `ProblemDetails` ("Não autenticado." / "Informe um token de acesso válido.") e `WWW-Authenticate: Bearer` de um token inválido (cenário 1 da US2); token forjado com o valor de A depois da retirada → 401; token sem `kid` assinado com a chave ativa → 401 (FR-014); `kid` desconhecido → 401; `kid` que difere da chave só em maiúsculas → 401; `kid` configurado com assinatura errada (`assinaturaInvalida: true`) → 401.
- [ ] T017 [US2] Em `src/PedidoFulfillment.API/Seguranca/EventosChavesJwt.cs`, acrescentar o `[LoggerMessage(EventId = 3027, EventName = "TokenChaveDesconhecida", Level = LogLevel.Warning, Message = "Token recusado: chave {IdentificadorChave} fora da configuração.")]` (FR-016).
- [ ] T018 [US2] Em `ConfiguracaoJwt.AdicionarAutenticacaoJwt` (`src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs`), acrescentar `OnAuthenticationFailed`: ler o token do cabeçalho `Authorization` (`Bearer ...`), lê-lo sem validar com `JsonWebTokenHandler.ReadJsonWebToken` (ignorando token ilegível) e, quando o `kid` estiver ausente ou fora do `ConjuntoChavesJwt`, emitir o 3027 com o `kid` se ele casar com `^[A-Za-z0-9._-]{1,64}$`, `(ausente)` sem `kid` e `(inválido)` nos demais casos (R-05). Sem log quando o `kid` é conhecido e a assinatura não confere. Não alterar a resposta (FR-004).
- [ ] T019 [US2] Em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/RotacaoChaveJwtTestes.cs`, testar com `CapturaLogs`: 3027 com o `kid` da chave retirada, com `(ausente)`, e com `(inválido)` para um `kid` com quebra de linha e outro com 65 caracteres; nenhum 3027 para assinatura errada com `kid` conhecido nem para token aceito; nenhum registro capturado, inclusive os das categorias `Microsoft.*` (mensagens `IDX` da biblioteca, que escondem dados sensíveis por padrão), com o token, o `sub` ou o valor de uma chave (FR-006).
- [ ] T020 [US2] Teste com banco em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/RotacaoChaveSessoesTestes.cs`, no mesmo molde de `Autenticacao/SessoesTestes.cs` (Testcontainers): login com A ativa; nova `FabricaApi` sobre o mesmo banco só com a chave C (retirada de emergência); o token de acesso de A recebe 401; `POST /api/v1/auth/renovacao` com o token de renovação do login → 200 e um token com `kid` C, aceito na rota protegida (cenário 4 da US2, FR-009, SC-003).

**Checkpoint**: MVP completo (US1 e US2); quickstart 3.4 e 3.5.

---

## Phase 5: User Story 3 - Configuração errada barrada na partida (Priority: P2)

**Goal**: a API não sobe com configuração inválida ou antiga, com mensagem sem valores; os cursores ganham a chave própria
opcional.

**Independent Test**: subir com cada configuração inválida e confirmar a falha e a mensagem sem o valor.

- [ ] T021 [US3] Criar `src/PedidoFulfillment.API/Seguranca/OpcoesListagens.cs` (seção `Listagens`, `string ChaveCursor`, opcional) com validação na partida: preenchida, "no mínimo 32 bytes em UTF-8", mensagem `Listagens:ChaveCursor deve ter ao menos 32 bytes.` (FR-008). Criar `src/PedidoFulfillment.API/Seguranca/ChaveCursorListagens.cs` (`IChaveCursor`): HKDF-SHA256 com o contexto `oms/listagem-pedidos/cursor/v1` sobre `Listagens:ChaveCursor` quando preenchida, ou sobre `ConjuntoChavesJwt.ValorAtiva` quando vazia (R-06); apagar `ChaveCursorDerivadaDoJwt.cs`; em `src/PedidoFulfillment.API/Program.cs`, registrar `OpcoesListagens` e `ChaveCursorListagens` e atualizar o comentário da linha do registro; atualizar o comentário de `src/PedidoFulfillment.Application/Comum/IChaveCursor.cs` (pedidos, produtos e usuários; chave própria opcional).
- [ ] T022 [P] [US3] Testes em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/ChaveCursorListagensTestes.cs`, resolvendo `IChaveCursor` de duas `FabricaApi`: com `Listagens:ChaveCursor` igual e chaves ativas diferentes, a chave do cursor é igual (SC-006); sem ela, muda quando a ativa muda; sem ela e com a ativa de valor igual ao `ChaveTeste`, é igual ao HKDF do `ChaveTeste` com o mesmo contexto (continuidade dos cursores na implantação); e um `proximo` emitido por `GET /api/v1/pedidos` (fake, como em `Endpoints/ListarPedidosTestes.cs`) numa fábrica continua aceito na outra com a mesma `Listagens:ChaveCursor` e outra chave ativa.
- [ ] T023 [P] [US3] Testes em `tests/PedidoFulfillment.Testes.Integracao/Seguranca/ChavesJwtPartidaTestes.cs`, no molde de `Configuracao/ConfiguracaoAusenteTestes.cs` (usando `FalhaPartida` e as `OptionsValidationException`): a API não sobe com `Jwt:ChaveAssinatura` (com e sem o formato novo), com 4 chaves, com identificador repetido, com valor curto, com `ChaveAtiva` fora da lista e com `Listagens:ChaveCursor` curta; cada mensagem é a do contrato, e nenhum texto da exceção contém o valor configurado (SC-004).

**Checkpoint**: US3 verificável sozinha (quickstart 3.6 e 3.7).

---

## Phase 6: User Story 4 - Procedimento e estudo documentados (Priority: P2)

**Goal**: o operador encontra o procedimento, a emergência, a migração e o estudo; os fluxos refletem o comportamento novo.

**Independent Test**: seguir só o `docs/rotacao-chave-jwt.md` numa instalação local e chegar ao resultado da US1.

- [ ] T024 [US4] Criar `docs/rotacao-chave-jwt.md` com `[← Voltar ao README](../README.md)` no topo: formato da configuração e variáveis de ambiente ([contrato](../specs/027-rotacao-chave-jwt/contracts/configuracao.md)); geração de chave (R-10); as três etapas, cada uma com o efeito, a ordem entre instâncias (a etapa 1 em todas antes da 2) e o tempo mínimo antes da retirada (`Jwt:DuracaoTokenMinutos` + 30 s, 15 min 30 s por padrão); como conferir pelo 3026 e pelo 3027; retirada de emergência (401 e renovação sem senha; limite de 3 chaves); migração de `Jwt:ChaveAssinatura` (mesmo valor em `Jwt:Chaves:0:Valor`; um 401 por cliente na implantação); listas em variável de ambiente sobrescrevendo o arquivo por posição; a chave do cursor (`Listagens:ChaveCursor`, recomendada em produção); e o resumo do estudo com as tabelas e as recomendações de R-08 e R-09 (FR-010, FR-011).
- [ ] T025 [US4] Em `README.md`, acrescentar só `rotacao-chave-jwt.md` à linha `docs/` da seção Estrutura (decisão do usuário; nenhuma outra linha).
- [ ] T026 [US4] Em `docs/fluxos/01-autenticacao.md`: `kid` no token (perto da linha que descreve o JWT `HS256`); a validação por `kid` e a tabela de erros (chave retirada, sem `kid`, `kid` desconhecido: 401 de token inválido, log 3027); a validação da partida com as mensagens; os logs 3026 e 3027 na seção de logs; o cursor da listagem de usuários com a chave do cursor; nas Limitações, o 401 único na implantação, o volume de 3027 sob ataque e a troca da `Listagens:ChaveCursor` invalidando cursores; e o link para `docs/rotacao-chave-jwt.md` (FR-012).
- [ ] T027 [P] [US4] Em `docs/fluxos/03-consulta-e-mudanca-de-status.md`, trocar a limitação "A chave que assina o cursor da listagem é derivada hoje da chave de assinatura do JWT..." pelo comportamento novo (chave própria opcional `Listagens:ChaveCursor`; sem ela, derivada da chave ativa e invalidada na troca da ativa), citando a issue #60 como a que resolveu a pendência (FR-008).
- [ ] T028 [P] [US4] Em `docs/fluxos/15-catalogo-de-produtos.md`, na paginação por cursor, registrar a mesma origem da chave do cursor (FR-008).

---

## Phase 7: Polish & Cross-Cutting Concerns

- [ ] T029 [P] Em `docs/proximos-passos.md`, tirar a #60 da fila (linha 35) e a pendência da chave do cursor (perto da linha 62), apontando para o REQ-27.
- [ ] T030 [P] Em `docs/estado-atual.md`, acrescentar a linha do REQ-27 no mesmo formato das demais, com o link para a spec.
- [ ] T031 Conferir nos documentos alterados (T024 a T030) as mensagens, os IDs de log, os nomes de configuração e o limite de 3 chaves contra o código, e que os links relativos não estão quebrados.
- [ ] T032 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`; seguir o [quickstart](./quickstart.md) nas seções 3.1 a 3.6; e conferir que os contêineres e volumes criados pelos testes não ficaram na máquina (comparar com o retrato do início).

---

## Dependencies & Execution Order

- **Fases 1 e 2** juntas, antes de tudo; a suíte só volta a ficar verde no T011.
- **US1 (fase 3)** e **US2 (fase 4)** dependem só da fundação e podem correr em paralelo, mas os testes delas dividem o
  arquivo `RotacaoChaveJwtTestes.cs` e as duas mexem em `EventosChavesJwt.cs`: na prática, US1 antes da US2.
- **US3 (fase 5)** depende da fundação; T021 substitui a classe adaptada no T010.
- **US4 (fase 6)** depende de US1 a US3 (os documentos citam mensagens e logs implementados).
- **Polish** por último.

### Dentro de cada fase

- Testes primeiro (T004, T012, T016, T022, T023) e falhando; depois a implementação.
- T005 → T006 → T007 → T008 → T009 → T010 → T011.
- T013 → T014 → T015; T017 → T018 → T019; T020 depois de T018.

## Parallel Example

```text
Fase 1: T001, T002 e T003 ao mesmo tempo (arquivos diferentes).
Fase 2: T004 enquanto T005–T007 são escritos.
Fase 5: T022 e T023 em paralelo depois de T021.
Fase 6/7: T027, T028, T029 e T030 em paralelo.
```

## Implementation Strategy

1. **MVP**: fases 1 a 4. A rotação planejada e a recusa da chave retirada funcionam, com os logs; um commit por fase.
2. **Incremento**: fase 5 (partida barrada e chave do cursor).
3. **Documentos e Polish**: fases 6 e 7, no mesmo PR, como exige o CLAUDE.md (fluxos no mesmo PR).
4. PR com `Closes #60`, citando em Validação os documentos de fluxo atualizados (01, 03 e 15) e o novo
   `docs/rotacao-chave-jwt.md`.
