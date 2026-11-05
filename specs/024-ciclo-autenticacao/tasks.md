---

description: "Lista de tarefas do REQ-24 — Ciclo de vida da autenticação"
---

# Tasks: Ciclo de vida da autenticação

**Input**: Design documents from `/specs/024-ciclo-autenticacao/`

**Prerequisites**: [plan.md](./plan.md), [spec.md](./spec.md), [research.md](./research.md),
[data-model.md](./data-model.md), [contracts/autenticacao.md](./contracts/autenticacao.md),
[quickstart.md](./quickstart.md)

**Tests**: OBRIGATÓRIOS. A constituição exige testes unitários das regras e integração com sucesso, 401 e 403, e a spec
pede testes de sucesso e de falha em todas as rotas novas (FR-029). Os testes da fundação vêm antes da implementação; nas
fases das histórias, descrevem o comportamento esperado, e os que falharem orientam o ajuste.

**Organization**: uma fundação com as tabelas, os limites e o envio de e-mail; depois uma fase por história. **O MVP são
as três histórias P1**: US1 (renovação), US2 (logout) e US3 (recuperação). A US4 reutiliza o `ServicoSenhas` da US3, e a
US5 depende só da fundação. O envio fica na fundação porque a US3 e a US5 o usam, e nenhuma das duas pode depender da outra.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: pode rodar em paralelo (arquivos diferentes, sem dependência pendente)
- **[Story]**: história da spec (US1–US5)

## Convenções válidas para todas as tarefas

- Nomes em português; rotas só delegam aos serviços; regras na `Application`; dados e SMTP na `Infrastructure`.
- `TreatWarningsAsErrors` ligado e `dotnet format --verify-no-changes` passando.
- **Um pacote novo: MailKit 4.18.2**, justificado em [R-04](./research.md#r-04-pacote-e-configuração-do-envio-fr-022-fr-023).
  Uma única migration, `CicloAutenticacao`. Sem eventos novos no RabbitMQ.
- Todo token é opaco, de 32 bytes, e só o SHA-256 é gravado. O token em claro existe só na resposta da API ou na memória do
  Worker e no e-mail (R-01, R-03).
- Logs só com os IDs 3010 a 3018. Nenhum token, senha, e-mail completo ou endereço de origem em logs, métricas ou spans
  (SC-008). E-mail só por `MascaraEmail`. MailKit sem `ProtocolLogger`, que gravaria o corpo com o token (R-10).
- O bloqueio de conta do REQ-03 e o limite do REQ-23 não mudam nas rotas existentes. Os testes existentes passam sem
  afrouxar asserções; a única exceção são os que conferem os 60 minutos (SC-009, T035).
- Contrato v1 só por acréscimo; o 409 do registro continua (FR-028). Cada história atualiza o
  `specs/001-contratos-api/contracts/openapi-v1.yaml` junto com a rota, para o `ConformidadeOpenApiTestes` continuar verde.
- Tempo pelo `TimeProvider` injetado, para os testes controlarem a validade.
- `README.md` **não recebe acréscimos** sem permissão do usuário.
- Commit por fase: `tipo(escopo): descrição [REQ-24]`.

---

## Phase 1: Setup

- [X] T001 Registrar a linha de base com `dotnet build` e `dotnet test` em `PedidoFulfillment.sln` e anotar o total de testes para comparar na T066
- [X] T002 Acrescentar `MailKit` 4.18.2 em `Directory.Packages.props` e a referência em `src/PedidoFulfillment.Infrastructure/PedidoFulfillment.Infrastructure.csproj`
- [X] T003 Acrescentar o serviço `mailpit` (`axllent/mailpit:v1.33.0`, sem perfil, com healthcheck e as portas nas variáveis `OMS_MAILPIT_SMTP_PORTA` e `OMS_MAILPIT_TELA_PORTA`, padrões 1025 e 8025) em `docker-compose.yml`. Subir o contêiner e **confirmar as portas de verdade** (R-05 as deixou por verificar); se forem outras, corrigir o `research.md` e o `quickstart.md`
- [X] T004 [P] Acrescentar `TokenInvalido = "TOKEN_INVALIDO"` em `src/PedidoFulfillment.Application/Contratos/Erros/CodigosErro.cs`

**Checkpoint**: linha de base conhecida, e o Mailpit responde.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: tabelas, gerador de tokens, envio de e-mail e limites, sem nenhuma rota nova ainda.

**⚠️ CRITICAL**: nenhuma história começa antes desta fase.

### Testes da fundação

- [X] T005 [P] Escrever testes de `GeradorTokenOpaco` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/GeradorTokenOpacoTestes.cs`: 32 bytes em Base64 URL, sem `+`, `/` nem `=`; dois tokens diferentes; hash de 32 bytes, determinístico, igual ao SHA-256 do texto; texto vazio não falha (R-01)
- [X] T006 [P] Escrever testes de `RegrasSenha` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/RegrasSenhaTestes.cs`: sem maiúscula, sem minúscula ou sem dígito falham com a mensagem atual do registro ("A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito."); senha válida passa
- [X] T007 [P] Escrever testes de `ValidadorOpcoesEmail` em `tests/PedidoFulfillment.Testes.Unitarios/Emails/ValidadorOpcoesEmailTestes.cs`: desligado (`Habilitado=false`) não exige nada; ligado exige `Servidor` e `Remetente` válido; cada faixa de [R-04](./research.md#r-04-pacote-e-configuração-do-envio-fr-022-fr-023) recusada nas pontas; `Seguranca=Nenhuma` só aceita em Development e em Testes; todas as falhas juntas; a mensagem cita a chave, nunca o valor, inclusive da senha (FR-023)
- [X] T008 [P] Escrever testes de `ValidadorOpcoesSessoes` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ValidadorOpcoesSessoesTestes.cs`: padrões válidos (14 e 30); `ValidadeTokenRenovacaoDias` de 1 a 90 e `DuracaoMaximaDias` de 1 a 365, recusados nas pontas; duração menor que a validade recusada ("não menor que a validade do token")
- [X] T009 [P] Ampliar `tests/PedidoFulfillment.Testes.Unitarios/LimiteRequisicoes/ValidadorOpcoesLimiteRequisicoesTestes.cs` e `ServicoLimiteRequisicoesTestes.cs`: padrões e faixas das 8 políticas novas; recuperação e reenvio com e-mail avaliam duas políticas e, sem e-mail utilizável, só a de origem; redefinição, confirmação, renovação e logout avaliam uma só; contagens de rotas diferentes não se misturam, porque o nome da política entra na chave (R-08, FR-024)
- [X] T010 [P] Escrever testes de `ModelosEmail` em `tests/PedidoFulfillment.Testes.Unitarios/Emails/ModelosEmailTestes.cs`: textos em português, por tipo; com `UrlBaseAplicacao`, traz o link com o token; sem ela, traz só o token; o token é codificado na URL; `AvisoSenhaRedefinida` não traz token

### Application

- [X] T011 [P] Implementar `GeradorTokenOpaco` em `src/PedidoFulfillment.Application/Autenticacao/GeradorTokenOpaco.cs` (`RandomNumberGenerator`, Base64 URL, `SHA256.HashData`) até a T005 passar
- [X] T012 [P] Extrair a regra hoje escrita dentro de `RegistrarUsuarioRequestDTO.Validate` para `src/PedidoFulfillment.Application/Autenticacao/RegrasSenha.cs`, até a T006 passar, e fazer o `src/PedidoFulfillment.Application/Contratos/Autenticacao/RegistrarUsuarioRequestDTO.cs` usá-la, sem mudar nenhuma mensagem. (R-09): a regra são um `StringLength(128, MinimumLength = 8)` e esse trecho do `Validate`
- [X] T013 [P] Criar em `src/PedidoFulfillment.Application/Emails/`: `TipoEmail.cs` (`RecuperacaoSenha`, `ConfirmacaoEmail`, `AvisoSenhaRedefinida`), `MensagemEmail.cs`, `IFilaEmails.cs` (grava a solicitação no contexto da requisição, para entrar na transação de quem a originou, R-03), `IEnviadorEmail.cs` (distingue falha temporária de recusa permanente do destinatário) e `EventosEmails.cs` com os logs 3016 `EmailEnviado`, 3017 `FalhaEnvioEmail` e 3018 `EmailDescartado`, só com o tipo, o id da conta e o e-mail mascarado
- [X] T014 [P] Implementar `ModelosEmail` em `src/PedidoFulfillment.Application/Emails/ModelosEmail.cs` até a T010 passar
- [X] T015 Criar em `src/PedidoFulfillment.Application/Autenticacao/`: `IArmazenamentoSessoes.cs` (sessões, rotação, encerramento e tokens de uso único), `ConfiguracaoSessoes.cs` (sem depender de `Options`) e `IMetricasAutenticacao.cs`; acrescentar a `EventosAutenticacao.cs` os logs 3010 `RenovacaoReutilizada`, 3011 `SessaoEncerrada`, 3012 `SenhaTrocada`, 3013 `TrocaSenhaRecusada`, 3014 `SenhaRedefinida` e 3015 `EmailConfirmado` (R-10)
- [X] T016 Generalizar o limite em `src/PedidoFulfillment.Application/LimiteRequisicoes/`: `RotaLimitada` ganha `Renovacao`, `Logout`, `RecuperacaoSenha`, `ReenvioConfirmacao`, `RedefinicaoSenha` e `ConfirmacaoEmail`; `PoliticaLimite` ganha as constantes `recuperacao_origem_email`, `recuperacao_origem`, `reenvio_origem_email`, `reenvio_origem`, `redefinicao`, `confirmacao`, `renovacao` e `logout`; `ConfiguracaoLimites` ganha as 8 políticas; `ServicoLimiteRequisicoes.Montar` passa a cobrir todas as rotas, até a parte unitária da T009 passar

### Infrastructure

- [X] T017 Criar em `src/PedidoFulfillment.Infrastructure/Persistencia/` as entidades `SessaoAutenticacao`, `TokenRenovacao`, `TokenUsoUnico` e `SolicitacaoEmail`, com as configurações em `Configuracoes/`, e registrá-las em `ContextoBancoDados.cs`. Regras do [data-model](./data-model.md): `SessoesAutenticacao` (`Id` `uniqueidentifier` PK; `UsuarioId` FK para `Usuarios`, em cascata, com índice; `IniciadaEm`; `ExpiraEm`, com índice; `EncerradaEm` nulo); `TokensRenovacao` (`HashToken` `binary(32)` PK; `SessaoId` FK em cascata; `ExpiraEm`; `UsadoEm` nulo); `TokensUsoUnico` (`HashToken` `binary(32)` PK; `UsuarioId` FK, em cascata, índice com `Finalidade`; `Finalidade` `varchar(30)`; `ExpiraEm`, com índice; `UsadoEm` nulo); `SolicitacoesEmail` (`Id` `uniqueidentifier` PK; `Tipo` `varchar(30)`; `Destino` `nvarchar(256)`; `CriadaEm`; `ProximaTentativaEm`, com índice; `Tentativas` `int`); datas em `datetimeoffset`. Gerar a migration `CicloAutenticacao` em `Persistencia/Migracoes/`
- [X] T018 [P] Implementar `OpcoesEmail` e `ValidadorOpcoesEmail` em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesEmail.cs`, com a seção `Email` e os padrões de R-04, recebendo o ambiente para a regra de `Nenhuma`, até a T007 passar
- [X] T019 [P] Implementar `OpcoesSessoes` e `ValidadorOpcoesSessoes` em `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesSessoes.cs`, com a seção `Sessoes`, até a T008 passar
- [X] T020 Acrescentar as 8 políticas a `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimiteRequisicoes.cs` (`ParaConfiguracao` e validador) com os padrões do [data-model](./data-model.md#limites-configuração-sem-tabela): recuperação e reenvio, 3 por 3.600 s por origem + e-mail e 20 por 3.600 s por origem; redefinição e confirmação, 10 por 60 s; renovação e logout, 120 por 60 s; até a T009 passar
- [X] T021 Implementar `ArmazenamentoSessoes` em `src/PedidoFulfillment.Infrastructure/Autenticacao/ArmazenamentoSessoes.cs`, com `ExecuteUpdate` e na mesma instância do `ContextoBancoDados` da requisição, **recebendo as validades por parâmetro, sem depender de `ConfiguracaoSessoes`**, porque o Worker também o usa e essa configuração só existe na API: criar sessão com o primeiro token; rotação de [R-02](./research.md#r-02-sessão-e-rotação-fr-002-fr-004-fr-005) (`UPDATE` com `UsadoEm IS NULL AND ExpiraEm > @agora`; uma linha afetada confere a sessão, ativa e dentro do limite, e grava o token novo, com `ExpiraEm` limitado ao da sessão; nenhuma linha distingue reutilização de token desconhecido ou vencido, e só a reutilização encerra a sessão); encerrar uma sessão; encerrar todas as da conta; criar token de uso único apagando os anteriores da mesma conta e finalidade; consumir token de uso único de forma atômica. Registrar em `src/PedidoFulfillment.Infrastructure/ExtensoesInfraestrutura.cs`
- [X] T022 Escrever testes com banco real em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/ArmazenamentoSessoesTestes.cs`: 50 rotações simultâneas com o mesmo hash afetam uma linha (SC-002); token novo nunca passa do limite da sessão; só a reutilização encerra a sessão; 20 consumos simultâneos do mesmo token de uso único têm um só vencedor; token novo invalida os anteriores só da mesma conta e finalidade; apagar a sessão apaga os tokens em cascata
- [X] T023 [P] Implementar `FilaEmails` e `EnviadorEmailSmtp` em `src/PedidoFulfillment.Infrastructure/Emails/`: a fila apara o destino e grava a linha no contexto da requisição; o enviador usa MailKit por SMTP com `Seguranca`, credenciais opcionais e `TempoLimiteSegundos`, sem `ProtocolLogger`, e separa falha de conexão, outra falha temporária e recusa `5xx` do destinatário (R-03, R-04, R-10)
- [X] T024 Implementar `ProcessadorEmails` em `src/PedidoFulfillment.Infrastructure/Emails/ProcessadorEmails.cs`, no molde de `src/PedidoFulfillment.Infrastructure/Notificacoes/ProcessadorNotificacoes.cs`, com lock de aplicação `oms-emails`, intervalo e lote de `OpcoesEmail`. Para cada solicitação: acha a conta; sem conta, apaga, sem enviar; reenvio para conta já confirmada, apaga, sem enviar; gera o token e **confirma a transação com o hash antes de enviar**; **envia ao endereço guardado na conta, nunca ao `Destino` digitado**, para um e-mail equivalente na comparação não desviar o token; sucesso ou recusa permanente apaga; falha temporária reagenda com espera dobrando de 30 s até 2 min, e a tentativa seguinte gera outro token; **falha de conexão também termina a rodada**, para não gerar um token por linha nem esgotar o tempo limite 50 vezes; depois de 24 h, descarta com o log 3018 (R-03). Acrescentar `oms.emails` (`oms.email.tipo`, `oms.resultado`: `enviado`, `falha`, `descartado`, `sem_conta`) em `src/PedidoFulfillment.Infrastructure/Observabilidade/TelemetriaOms.cs`
- [X] T025 Criar `ExtensoesEmails` em `src/PedidoFulfillment.Infrastructure/Emails/ExtensoesEmails.cs`: `AdicionarProcessamentoEmails` (só o Worker, com a seção `Email` validada na partida), chamada em `src/PedidoFulfillment.Worker/ProgramaWorker.cs`. A `IFilaEmails` fica em `AdicionarInfraestrutura`, junto do gestor que a usa. Em `docker-compose.yml`, acrescentar `Email__*` e a dependência do `mailpit` ao `worker`. Em `src/PedidoFulfillment.Worker/appsettings.Development.json`, apontar para `localhost:1025` com `Seguranca=Nenhuma`
- [X] T026 Escrever testes com banco real e `IEnviadorEmail` falso em `tests/PedidoFulfillment.Testes.Integracao/Emails/ProcessadorEmailsTestes.cs`: os três tipos; sem conta, apaga sem enviar e soma `sem_conta`; reenvio para conta confirmada não envia; falha temporária reagenda com espera crescente, e só o último token vale (FR-021); falha de conexão termina a rodada sem processar as linhas seguintes; recusa permanente apaga, sem insistir; mais de 24 h descarta; queda entre o envio e a remoção não duplica o efeito; endereço guardado na conta recebe a mensagem, mesmo com o `Destino` em outra caixa; o lock impede duas instâncias na mesma rodada. Acrescentar o processador a `tests/PedidoFulfillment.Testes.Unitarios/Rotinas/RotinasSemDependenciasTestes.cs`

### API e testes

- [X] T027 Renomear `LeitorEmailLogin` para `LeitorEmailCorpo` em `src/PedidoFulfillment.API/LimiteRequisicoes/` e `LeitorEmailLoginTestes` para `LeitorEmailCorpoTestes` em `tests/PedidoFulfillment.Testes.Integracao/LimiteRequisicoes/`. Em `MiddlewareLimiteRequisicoes.cs`, ler o e-mail em `Login`, `RecuperacaoSenha` e `ReenvioConfirmacao`, sem mudar o restante
- [X] T028 Acrescentar as seções `Sessoes` e as 8 políticas a `src/PedidoFulfillment.API/appsettings.json`. Em `tests/PedidoFulfillment.Testes.Integracao/Infraestrutura/`: `FabricaApi.cs` com limites de 1.000.000 nas políticas novas (sem banco, a fábrica já troca o `IServicoAutenticacao` por um falso; só criar um dublê das portas novas se a suíte mostrar que é preciso); `FabricaWorker.cs` com `Email:Habilitado=false`, salvo nos testes dele; `MailpitTeste.cs` novo, com o contêiner pelo Testcontainers e a leitura por `GET /api/v1/messages`. Rodar toda a suíte: nada muda

**Checkpoint**: tudo compila, a suíte segue verde, o Worker envia e-mails de solicitações gravadas à mão, e nenhuma rota nova existe.

---

## Phase 3: User Story 1 — Continuar conectado sem digitar a senha de novo (Priority: P1) 🎯 MVP

**Goal**: login com token de renovação e `POST /renovacao` com rotação e detecção de reutilização.

**Independent Test**: fazer login, renovar, confirmar que o token de acesso novo funciona e que o token de renovação antigo é recusado.

- [X] T029 [P] [US1] Escrever testes de `ServicoSessoes` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoSessoesTestes.cs`, com portas falsas: par novo com os perfis atuais (FR-007); inválido, expirado, encerrado e reutilizado têm a mesma `Falha` (FR-006); log 3010 e métrica `reutilizada` só na reutilização; conta bloqueada renova, porque o bloqueio não é consultado. Atualizar `ServicoAutenticacaoTestes.cs` e `Falsos.cs` na mesma pasta: o login cria a sessão e devolve os campos novos; login recusado não cria sessão
- [X] T030 [P] [US1] Acrescentar `TokenRenovacao` e `TokenRenovacaoExpiraEm` a `src/PedidoFulfillment.Application/Contratos/Autenticacao/LoginResponseDTO.cs`, com `[Required]` e valor padrão, como `TipoToken`, para os testes que montam o DTO continuarem compilando (R-06), e corrigir o comentário dos 60 minutos. Criar `RenovacaoRequestDTO.cs` na mesma pasta (`tokenRenovacao` obrigatório, com tamanho máximo, para limitar o que é calculado)
- [X] T031 [US1] Acrescentar `ObterPerfisAsync` a `src/PedidoFulfillment.Application/Autenticacao/IGestorIdentidade.cs` e `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, reaproveitando a conversão de perfis de `VerificarCredenciaisAsync`
- [X] T032 [US1] Implementar `IServicoSessoes` e `ServicoSessoes.RenovarAsync` em `src/PedidoFulfillment.Application/Autenticacao/`, com a resposta 401 "Sessão inválida." / "Faça login novamente." do [contrato](./contracts/autenticacao.md#mensagens), até a T029 passar
- [X] T033 [US1] Fazer `src/PedidoFulfillment.Application/Autenticacao/ServicoAutenticacao.cs` criar a sessão depois das credenciais válidas e completar a resposta do emissor com `with`, sem mudar `IEmissorToken`
- [X] T034 [US1] Implementar `MetricasAutenticacao` em `src/PedidoFulfillment.Infrastructure/Autenticacao/MetricasAutenticacao.cs`, com `oms.autenticacao.renovacoes` (`oms.resultado`: `renovada`, `recusada`, `reutilizada`) em `Observabilidade/TelemetriaOms.cs`. Mapear `POST /renovacao` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`, com `FiltroValidacao`, `ComLimiteRequisicoes(RotaLimitada.Renovacao)` e `ProduzErros` (400, 401, 429). Registrar serviços e `ConfiguracaoSessoes` em `src/PedidoFulfillment.API/Program.cs`. Atualizar `specs/001-contratos-api/contracts/openapi-v1.yaml`: rota nova e dois campos na resposta do login
- [X] T035 [US1] Mudar o padrão de `DuracaoTokenMinutos` de 60 para 15 em `src/PedidoFulfillment.API/Seguranca/ConfiguracaoJwt.cs` e em `src/PedidoFulfillment.API/appsettings.json`; corrigir a descrição de `expiraEm` em `specs/001-contratos-api/contracts/openapi-v1.yaml` (linha 1250: "60 minutos após a emissão" passa a dizer que a validade é configurável, 15 minutos por padrão) e o comentário de `src/PedidoFulfillment.Infrastructure/Notificacoes/FluxoEventosTempoReal.cs`; e atualizar só as asserções dos 60 minutos, em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/EmissorTokenTestes.cs` e `LoginComBancoTestes.cs` (exceção do SC-009). O `GeradorTokenTeste.cs` só cria tokens de teste e não muda
- [X] T036 [US1] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/SessoesTestes.cs`, com banco real, cobrindo os 6 cenários da US1: resposta do login; par novo e token de acesso novo aceito em rota protegida; reutilização encerra a sessão, e o token mais recente também é recusado; expirado, revogado, inexistente e reutilizado têm o mesmo 401, com corpo idêntico; perfil alterado aparece no token novo; conta bloqueada renova; além disso, 50 renovações simultâneas pela API com exatamente um 200 (SC-002), sessão não passa de 30 dias (FR-005), 400 sem o campo e 429 com limite baixo. Em `tests/PedidoFulfillment.Testes.Integracao/Notificacoes/`, provar o efeito no SSE: a conexão termina quando o token expira, e reconectar com um token renovado e `Last-Event-ID` recupera os avisos perdidos

**Checkpoint**: sessão renovável, com a reutilização detectada.

---

## Phase 4: User Story 2 — Encerrar a sessão (Priority: P1) 🎯 MVP

**Goal**: `POST /logout`, público e idempotente.

**Independent Test**: fazer login, fazer logout e confirmar que a renovação é recusada.

- [X] T037 [P] [US2] Ampliar `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoSessoesTestes.cs`: logout de token válido encerra a sessão e gera o log 3011; token desconhecido, expirado ou já revogado termina igual, sem log e sem falha
- [X] T038 [US2] Implementar `ServicoSessoes.EncerrarAsync` em `src/PedidoFulfillment.Application/Autenticacao/ServicoSessoes.cs` até a T037 passar
- [X] T039 [US2] Mapear `POST /logout` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`, com `LogoutRequestDTO` novo, de mesma forma que o `RenovacaoRequestDTO`, `FiltroValidacao`, `ComLimiteRequisicoes(RotaLimitada.Logout)` e `ProduzErros` (400, 429); sem exigir token de acesso (FR-008). Atualizar `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [X] T040 [US2] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/LogoutTestes.cs`, cobrindo os 4 cenários da US2: 204, e a renovação é recusada (SC-003); repetir ou usar token inexistente é o mesmo 204; encerrar uma sessão não afeta a outra da mesma conta; o token de acesso continua aceito depois do logout (FR-009); além de 400 sem o campo e 429 com limite baixo

**Checkpoint**: sessão encerrável, em qualquer dispositivo.

---

## Phase 5: User Story 3 — Recuperar a conta ao esquecer a senha (Priority: P1) 🎯 MVP

**Goal**: pedido de recuperação e redefinição, sem revelar se o e-mail tem conta.

**Independent Test**: no ambiente local, pedir a recuperação, ler o e-mail capturado, definir a senha nova e fazer login com ela.

- [X] T041 [P] [US3] Escrever testes de `ServicoSenhas` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoSenhasTestes.cs`, com portas falsas: o pedido só grava a solicitação, **sem consultar a conta** (FR-013) e sem log; token inválido devolve `Falha` com `TOKEN_INVALIDO` e o texto do contrato; a redefinição concluída gera o log 3014
- [X] T042 [P] [US3] Criar `RecuperacaoSenhaRequestDTO.cs` (`email` com as regras do registro) e `RedefinicaoSenhaRequestDTO.cs` (`token`, com tamanho máximo, e `novaSenha`, com as regras da T012) em `src/PedidoFulfillment.Application/Contratos/Autenticacao/`
- [X] T043 [US3] Implementar `IServicoSenhas` e `ServicoSenhas` (`PedirRecuperacaoAsync`, `RedefinirAsync`) em `src/PedidoFulfillment.Application/Autenticacao/` até a T041 passar
- [X] T044 [US3] Implementar `RedefinirSenhaAsync` em `IGestorIdentidade` e `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`, em **uma transação**: consome o token (finalidade `RecuperacaoSenha`) de forma atômica; troca o hash pelo `PasswordHasher`; atualiza o `SecurityStamp`; zera o bloqueio e a contagem; marca o e-mail como confirmado (FR-015); encerra todas as sessões (FR-011); grava a solicitação `AvisoSenhaRedefinida`. Qualquer falha desfaz tudo, inclusive o consumo do token (R-09)
- [X] T045 [US3] Mapear `POST /recuperacao-senha` (202 sem corpo em qualquer caso; 400, 429) e `POST /redefinicao-senha` (204; 400, 429) em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`, com `ComLimiteRequisicoes` de `RecuperacaoSenha` e `RedefinicaoSenha`. Registrar o serviço em `src/PedidoFulfillment.API/Program.cs`. Atualizar `specs/001-contratos-api/contracts/openapi-v1.yaml`, com o exemplo de `TOKEN_INVALIDO`
- [X] T046 [US3] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/RecuperacaoSenhaTestes.cs`, com banco real e `IEnviadorEmail` falso, cobrindo os 7 cenários da US3: com conta, 202 e e-mail; sem conta, o mesmo 202 com o mesmo corpo e nenhum e-mail; redefinição muda a senha, invalida o token e encerra as sessões; expirado, usado e inexistente têm a mesma resposta, sem mudar a senha; só o token mais recente vale; bloqueio e contagem zerados; e-mail de aviso enviado. Além de senha nova fora das regras, sem consumir o token; 20 redefinições simultâneas com o mesmo token, uma só aceita; 4 pedidos seguidos do mesmo e-mail com um 429 e o `Retry-After`; 429 também em `/redefinicao-senha`, com limite baixo (FR-024)
- [X] T047 [US3] Escrever testes com Mailpit real em `tests/PedidoFulfillment.Testes.Integracao/Emails/EnvioMailpitTestes.cs`: recuperação de ponta a ponta em menos de 2 minutos, do pedido ao login com a senha nova (SC-004); com o servidor parado, as rotas respondem como de costume, e o e-mail chega em até 5 minutos depois da volta (SC-007, FR-021)
- [X] T048 [US3] Escrever o teste de tempo em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/TempoRecuperacaoTestes.cs`, em uma coleção que não roda em paralelo: medianas de 100 pedidos com conta e 100 sem conta, com diferença abaixo de 50 ms, status e corpo idênticos, na recuperação e também em `/confirmacao-email/reenvio` (SC-005)

**Checkpoint**: MVP entregue: renovação, logout e recuperação funcionam de ponta a ponta.

---

## Phase 6: User Story 4 — Trocar a senha estando conectado (Priority: P2)

**Goal**: `POST /troca-senha`, a única rota nova protegida.

**Independent Test**: trocar a senha, fazer login com a nova e confirmar que a antiga e a sessão anterior deixaram de valer.

- [X] T049 [P] [US4] Ampliar `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoSenhasTestes.cs`: senha atual errada e conta bloqueada têm a mesma `Falha` de validação em `senhaAtual` ("A senha atual está incorreta.") e o log 3013; senha nova igual à atual, em `novaSenha` ("A nova senha deve ser diferente da atual."); sucesso gera o log 3012
- [X] T050 [US4] Criar `TrocaSenhaRequestDTO.cs` em `src/PedidoFulfillment.Application/Contratos/Autenticacao/` (`senhaAtual` e `novaSenha`, com as regras da T012). Implementar `ServicoSenhas.TrocarAsync` e `TrocarSenhaAsync` em `IGestorIdentidade` e `GestorIdentidade.cs`, em **uma transação** com o encerramento de todas as sessões. Em conta bloqueada, a senha nem é verificada; senha errada chama `AccessFailedAsync` e o bloqueio continua invisível (FR-012, R-09); até a T049 passar
- [X] T051 [US4] Acrescentar `PoliticaContaAutenticada` (`RequireAuthenticatedUser` e todos os perfis) em `src/PedidoFulfillment.API/Seguranca/PoliticasAutorizacao.cs` e mapear `POST /api/v1/auth/troca-senha` em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`, com `ProduzErros` (400, 401, 403). **Mapear fora do grupo atual**: o grupo tem `.AllowAnonymous()`, que prevalece sobre `RequireAuthorization` e deixaria a rota pública. Atualizar `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [X] T052 [US4] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/TrocaSenhaTestes.cs`, cobrindo os 4 cenários da US4: 204, a senha muda e todas as sessões encerram, inclusive a do dispositivo que trocou; senha atual errada, 400, com repetição até o bloqueio sem revelá-lo; senha fora das regras ou igual, 400, sem mudar nada; **sem token e com token inválido, 401; com token válido sem perfil, 403** (FR-029, constituição); o token de acesso continua aceito até expirar (FR-009)

**Checkpoint**: troca de senha entregue, e a rota não é pública.

---

## Phase 7: User Story 5 — Confirmar o e-mail (Priority: P3)

**Goal**: e-mail de confirmação no registro, confirmação e reenvio.

**Independent Test**: registrar uma conta, ler o e-mail capturado, confirmar e ver a conta marcada como confirmada.

- [X] T053 [P] [US5] Escrever testes de `ServicoConfirmacaoEmail` em `tests/PedidoFulfillment.Testes.Unitarios/Autenticacao/ServicoConfirmacaoEmailTestes.cs`, com portas falsas: confirmação com token válido marca a conta e gera o log 3015; token inválido devolve `TOKEN_INVALIDO` com o texto do contrato; reenvio só grava a solicitação, sem consultar a conta
- [X] T054 [US5] Gravar a solicitação `ConfirmacaoEmail` dentro da transação de `CriarAsync`, em `src/PedidoFulfillment.Infrastructure/Identidade/GestorIdentidade.cs`: registro com e-mail repetido não deixa solicitação, e a resposta da rota não muda (FR-017)
- [X] T055 [US5] Implementar `IServicoConfirmacaoEmail` e `ServicoConfirmacaoEmail` em `src/PedidoFulfillment.Application/Autenticacao/`, e `ConfirmarEmailAsync` em `IGestorIdentidade` e `GestorIdentidade.cs`, consumindo o token de finalidade `ConfirmacaoEmail` na mesma transação, até a T053 passar. Criar `ConfirmacaoEmailRequestDTO.cs` e `ReenvioConfirmacaoRequestDTO.cs` em `src/PedidoFulfillment.Application/Contratos/Autenticacao/`
- [X] T056 [US5] Mapear `POST /confirmacao-email` (204; 400, 429) e `POST /confirmacao-email/reenvio` (202 sem corpo em qualquer caso; 400, 429) em `src/PedidoFulfillment.API/Endpoints/EndpointsAutenticacao.cs`, com `ComLimiteRequisicoes` de `ConfirmacaoEmail` e `ReenvioConfirmacao`. Registrar o serviço em `src/PedidoFulfillment.API/Program.cs`. Atualizar `specs/001-contratos-api/contracts/openapi-v1.yaml`
- [X] T057 [US5] Marcar os usuários iniciais como confirmados **ao serem criados**, em `src/PedidoFulfillment.Infrastructure/Persistencia/PreparacaoBancoDados.cs`, e ampliar `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/UsuariosIniciaisComBancoTestes.cs`. Os que já existem não mudam, como as demais contas anteriores a este requisito, e nada depende da confirmação (FR-020)
- [X] T058 [US5] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/ConfirmacaoEmailTestes.cs`, com banco real e `IEnviadorEmail` falso, cobrindo os 6 cenários da US5: registro gera o e-mail, com a mesma resposta; token válido confirma; inválido, expirado e usado têm a mesma resposta; reenvio, o mesmo 202 em todos os casos, e só a conta não confirmada recebe; recuperação concluída confirma o e-mail; login sem confirmação tem resposta igual à de uma conta confirmada. Além de conta antiga, sem sessão nem confirmação, continuar entrando; e 429 em `/confirmacao-email` e `/confirmacao-email/reenvio`, com limite baixo (FR-024)

**Checkpoint**: todas as histórias entregues.

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T059 Acrescentar `RetencaoSessoesHoras` e `RetencaoTokensUsoUnicoHoras` (padrão 24, de 1 a 8.760) a `src/PedidoFulfillment.Infrastructure/Configuracao/OpcoesLimpeza.cs` e ao validador, e os alvos `sessoes_autenticacao` e `tokens_uso_unico` a `src/PedidoFulfillment.Infrastructure/Mensageria/LimpezaMensageria.cs`, em lotes, como os demais (R-07, FR-027)
- [X] T060 Ampliar `tests/PedidoFulfillment.Testes.Integracao/Administracao/LimpezaMensageriaTestes.cs`: apaga sessões encerradas ou vencidas além da retenção, com os tokens em cascata; mantém a sessão ativa e os tokens usados dela, necessários para detectar a reutilização; apaga tokens de uso único usados ou vencidos; respeita a retenção
- [X] T061 [P] Escrever testes em `tests/PedidoFulfillment.Testes.Integracao/Autenticacao/SigiloTokensTestes.cs`: depois de renovação, recuperação e confirmação, o token capturado não aparece em nenhuma coluna das quatro tabelas, e um hash do banco não serve como token (SC-006); com `CapturaLogs` e `ColetorTelemetria`, logs, métricas e atributos de spans dessas rotas não contêm token, senha, e-mail completo nem endereço de origem (SC-008, FR-026)
- [X] T062 [P] Conferir o contrato: `tests/PedidoFulfillment.Testes.Integracao/Contrato/ConformidadeOpenApiTestes.cs` verde com as 7 rotas e os 2 campos, a política de autorização documentada em `/troca-senha` e o comparador de contrato sem mudança incompatível (FR-028, SC-009)
- [X] T063 [P] Atualizar `docs/fluxos/01-autenticacao.md` (rotas, tabela de erros com as mensagens do contrato, limites, logs 3010 a 3015; limitações: token de acesso vale até 15 minutos depois do logout; duas renovações simultâneas encerram a sessão; o 409 do registro revela e-mails cadastrados; e trocar os "60 minutos" da linha 60), `docs/fluxos/11-limpeza-e-reenvio-da-dlq.md` (2 alvos), `docs/fluxos/12-rastreabilidade-e-metricas.md` (2 métricas) e `docs/fluxos/13-notificacao-de-status.md` (a conexão SSE cai a cada 15 minutos por padrão; renovar e reconectar com `Last-Event-ID`). Criar `docs/fluxos/16-envio-de-email.md`, no formato dos demais, e acrescentar a linha em `docs/fluxos/README.md`. Conferir mensagens, códigos, limites e IDs no código e os links relativos
- [X] T064 [P] Atualizar `specs/003-autenticacao-identity/contracts/token-acesso.md` (15 minutos), `specs/016-observabilidade-opentelemetry/contracts/telemetria.md` (2 métricas e 2 alvos) e `specs/023-rate-limiting/contracts/limite-requisicoes.md` (8 políticas), e acrescentar a `specs/001-contratos-api/spec.md`, junto do requisito dos 60 minutos, uma nota de que o REQ-24 mudou o padrão para 15
- [X] T065 [P] Atualizar `docs/proximos-passos.md`: acrescentar o REQ-24 ao resumo, numeração a partir do REQ-25, remover a #55 da fila e registrar o provedor real de e-mail em "Para análise futura". **Não acrescentar nada ao `README.md`**: se algo parecer necessário, como a porta 8025 do Mailpit, perguntar antes
- [X] T066 Rodar `dotnet build`, `dotnet test` e `dotnet format --verify-no-changes`, comparando o total de testes com a T001
- [X] T067 Executar o [quickstart](./quickstart.md) inteiro com o compose, inclusive a queda do Mailpit (3.6) e a busca do token no banco (3.7), e registrar em `specs/024-ciclo-autenticacao/research.md` as portas confirmadas e qualquer divergência; se um critério não atender, avisar o usuário antes de abrir o PR
- [ ] T068 Abrir o PR em português (Resumo, Pontos de atenção, Validação) com `Closes #55`, citando em Validação os documentos de fluxo atualizados e, em Pontos de atenção, a seção `Email` obrigatória no Worker fora de Development, a mudança de comportamento da validade padrão de 60 para 15 minutos (o contrato prometia 60), a queda do SSE a cada 15 minutos, a exceção ao Princípio IV e o pacote novo; depois, acompanhar os workflows, inclusive o `build-testes` e o Quality Gate, e usar `gh pr merge --rebase --auto`, sem apagar a branch. Marcar esta tarefa em `specs/024-ciclo-autenticacao/tasks.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (1)** → **Foundational (2)** → **US1, US2, US3, US4 e US5 (3 a 7)** → **Polish (8)**.

### User Story Dependencies

- **US1**: só da fundação. É a base das demais em valor, porque cria as sessões.
- **US2**: `ServicoSessoes` e a sessão criada no login, da US1.
- **US3**: só da fundação para as rotas. O teste "encerra as sessões" usa o login da US1.
- **US4**: reutiliza `ServicoSenhas`, da US3; o teste "encerra as sessões" usa o login da US1.
- **US5**: só da fundação. O cenário "recuperação confirma o e-mail" usa a US3.
- Todas as histórias alteram `EndpointsAutenticacao.cs` e `openapi-v1.yaml`, então as rotas entram uma de cada vez.

### Within Foundational

- T005 → T011; T006 → T012; T007 → T018; T008 → T019; T009 → T016 → T020; T010 → T014.
- T013 e T015 → T017 → T021 → T022; T013 → T023; T021 e T023 → T024 → T025 → T026.
- T016 e T020 → T027; T019, T020, T021 e T025 → T028.

### Parallel Opportunities

```text
Setup:              T004
Testes da fundação: T005, T006, T007, T008, T009, T010
Application:        T011, T012, T013, T014
Infrastructure:     T018, T019, T023
US1:                T029, T030
US3:                T041, T042
Polish:             T061, T062, T063, T064, T065
```

## Implementation Strategy

1. **MVP**: fases 1 e 2 e as três histórias P1 (US1, US2 e US3). Resolvem a sessão que expira, a sessão que não se encerra e a conta perdida.
2. **Incremental**: US4, depois US5, com um commit por fase.
3. **Antes do PR**: quickstart completo com o compose, porque só ele passa pelo SMTP e pelo Worker reais juntos.

## Notes

- `IArmazenamentoSessoes`, `IFilaEmails` e `IGestorIdentidade` precisam da mesma instância com escopo do `ContextoBancoDados`, ou as gravações ficam fora da transação da senha ou do registro.
- O `Validator` não chama `Validate` quando um atributo falha: regras que precisam aparecer juntas ficam todas em atributos ou todas no `Validate`.
- O processador só apaga a solicitação depois de enviar; a queda nesse intervalo gera um segundo e-mail, e só o token do último vale, como o R-03 aceita.
- Correção de code smells neste REQ usa `Refs #61`, nunca `Closes`, e atualiza a tabela da issue.
