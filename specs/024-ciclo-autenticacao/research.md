# Research: ciclo de vida da autenticação (REQ-24)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md) | **Data**: 2026-11-04

## R-01: forma e guarda dos tokens (FR-003, FR-014, FR-018)

**Decisão**: tokens opacos de 32 bytes aleatórios (`RandomNumberGenerator`), em Base64 URL, guardados só como SHA-256, em `binary(32)`. Vale para renovação, recuperação e confirmação.

- **Hash simples, sem sal nem chave**: um token de 256 bits aleatórios não pode ser adivinhado por força bruta, então um hash lento ou com sal não acrescenta nada, e a busca fica por igualdade no índice.
- **Sem o e-mail na chamada** (clarify): o hash é a chave de busca e identifica a conta.

**Alternativas descartadas**:

- **Tokens do Identity** (`AddDefaultTokenProviders`): exigem o usuário na chamada, o que contraria o clarify, e dependem de chaves do Data Protection compartilhadas entre as instâncias.
- **JWT como token de renovação**: não há como revogar sem guardar estado, e o estado seria necessário de qualquer forma.

## R-02: sessão e rotação (FR-002, FR-004, FR-005)

**Decisão**: duas tabelas, `SessoesAutenticacao` e `TokensRenovacao`.

A renovação faz, em uma transação:

1. `UPDATE TokensRenovacao SET UsadoEm = @agora WHERE HashToken = @h AND UsadoEm IS NULL AND ExpiraEm > @agora`, pelo `ExecuteUpdate` do EF Core.
2. **Uma linha afetada**: confere se a sessão está ativa e dentro dos 30 dias, grava o token novo e devolve o par.
3. **Nenhuma linha**: se o hash existe e já foi usado, é reutilização, e a sessão é encerrada, com o log 3010. Em qualquer caso, 401.

O `UPDATE` condicional decide a disputa: em duas apresentações simultâneas, só uma afeta a linha (SC-002). A outra vira reutilização e encerra a sessão, como o usuário decidiu.

**Os tokens usados ficam até a sessão acabar**, porque são eles que detectam a reutilização. Uma sessão ativa, renovando a cada 15 minutos por 30 dias, acumula até cerca de 2.900 linhas de 60 bytes. É aceitável, e a limpeza (R-07) as remove junto com a sessão.

**Conta bloqueada** (FR-007): a renovação não consulta o bloqueio. **Perfis**: lidos da conta a cada renovação.

## R-03: envio de e-mail: quem envia e como o token fica em sigilo (FR-003, FR-013, FR-021)

**Decisão**: a API grava só uma **solicitação**, sem token, na tabela `SolicitacoesEmail`, e o **Worker gera o token no momento de enviar**.

```text
API: grava a solicitação (tipo e destino)          -> 202, igual com ou sem conta
Worker: acha a conta -> sem conta: apaga, sem enviar
                     -> gera o token, grava só o hash e confirma a transação
                     -> envia por SMTP -> apaga a solicitação
                     -> se falhar: mais tarde, com um token novo
```

- **O token em claro nunca é gravado**: existe só na memória do Worker e no e-mail. Atende ao FR-003 sem cifra e sem mais uma chave para operar. Esta é uma revisão do que o clarify supunha, que era cifrar a mensagem pendente.
- **A resposta é igual com e sem conta**: a API faz exatamente a mesma gravação nos dois casos e nem consulta a conta, o que também resolve o tempo (FR-013, SC-005).
- **Nova tentativa, token novo**: o token anterior é invalidado. Uma queda entre o envio e a remoção da solicitação gera um segundo e-mail, e só o do último vale, o que não duplica o efeito (FR-021).
- **O destino é o endereço da conta**: o `Destino` digitado só serve para achar a conta. O e-mail vai ao endereço guardado nela, para um e-mail apenas equivalente na comparação não desviar o token.
- **Validade conta do envio**: um e-mail atrasado pela queda do SMTP ainda chega com os 30 minutos completos.
- **Tipos**: `RecuperacaoSenha`, `ConfirmacaoEmail` e `AvisoSenhaRedefinida`.
- **Na transação do que a originou**: no registro, a solicitação grava junto com a conta, e na redefinição, junto com a senha nova.

**Molde**: o mesmo do `ProcessadorNotificacoes` (REQ-18), uma tabela e um `BackgroundService` no Worker, com lock de aplicação (`oms-emails`), sem passar pelo RabbitMQ. É uma exceção ao Princípio IV da constituição, aprovada pelo usuário e registrada no plano: não põe no broker endereços digitados por qualquer pessoa.

**Novas tentativas**: a cada 10 s, até 50 solicitações por rodada. Depois de uma falha, a espera dobra de 30 s até no máximo 2 min, o que cumpre o SC-007 (até 5 min depois da volta). Depois de 24 h, a solicitação é descartada com o log 3018. Uma recusa permanente do servidor (código 5xx para o destinatário) também descarta, sem insistir.

**Alternativas descartadas**: enviar durante a requisição (o tempo revelaria a conta e o SMTP fora derrubaria a rota); cifrar a mensagem pendente (uma chave a mais, e o token em claro ainda existiria em algum momento); usar o outbox do RabbitMQ (poria no broker o e-mail digitado, com ou sem conta, sem ganho na garantia de envio).

## R-04: pacote e configuração do envio (FR-022, FR-023)

**Decisão**: **MailKit 4.18.2**, escolhido pelo usuário. É a recomendação comum no lugar do `System.Net.Mail.SmtpClient`, que gera o aviso DE0005 (obsoleto). Falar SMTP puro evita prender o código a um provedor, e o provedor real fica fora do escopo. **Justificativa da dependência, exigida pela constituição**: o .NET não tem hoje um cliente SMTP recomendado.

**Isolado atrás da porta `IEnviadorEmail`**, na Infrastructure. Só o Worker envia.

**Seção `Email`, validada na partida do Worker**:

| Chave | Padrão | Regra |
|-------|:------:|-------|
| `Habilitado` | `true` | Os testes o desligam, como as demais rotinas |
| `Servidor` | sem padrão | Obrigatório se habilitado |
| `Porta` | 587 | 1 a 65.535 |
| `Usuario`, `Senha` | vazios | Opcionais; a senha nunca aparece em mensagens |
| `Remetente` | sem padrão | Obrigatório se habilitado; e-mail válido |
| `Seguranca` | `StartTls` | `StartTls` ou `SslOnConnect`; `Nenhuma` só em Development e em testes |
| `UrlBaseAplicacao` | vazio | Opcional; com ela, o e-mail traz um link |
| `ValidadeRecuperacaoMinutos` | 30 | 5 a 1.440 |
| `ValidadeConfirmacaoHoras` | 24 | 1 a 168 |
| `TempoLimiteSegundos` | 10 | 1 a 60; por operação SMTP. Com o servidor fora, a rodada termina na primeira falha de conexão, sem gerar um token por linha |
| `IntervaloSegundos` | 10 | 1 a 300 |
| `TamanhoLote` | 50 | 1 a 1.000 |

**Configuração obrigatória fora de Development**: sem ela, o Worker não sobe. A recuperação de senha que falha em silêncio é pior que uma implantação que para na hora.

## R-05: captura local (FR-022)

**Decisão**: **Mailpit** `axllent/mailpit:v1.33.0`, escolhido pelo usuário, sem perfil no compose, como o banco e o broker, porque o Worker pelo SDK também precisa dele. SMTP na porta 1025 e tela na 8025. Nos testes de integração que passam pelo SMTP real, um contêiner próprio pelo Testcontainers; a API `GET /api/v1/messages` do Mailpit serve para ler o e-mail.

A imagem precisa ser confirmada ao subir o compose, na implementação, porque as portas vêm da documentação do projeto e não foram verificadas na busca.

## R-06: rotas e contrato (FR-028)

**Decisão**: rotas em português, como as de pedidos e produtos. `register` e `login` ficam como estão, pois mudar quebraria o contrato. O contrato está em [contracts/autenticacao.md](./contracts/autenticacao.md).

**`/troca-senha` fora do grupo atual**: o grupo de `/api/v1/auth` tem `.AllowAnonymous()`, que prevalece sobre a autorização da rota e a deixaria pública.

**`LoginResponseDTO`**: dois campos novos, com `[Required]` e valor padrão, como `TipoToken`. O emissor do JWT continua só com o token de acesso, e o serviço completa o restante com `with`. Por isso, os testes que montam o DTO continuam compilando.

**Token de acesso de 15 minutos**: muda o padrão de `DuracaoTokenMinutos` e a descrição de `expiraEm` no contrato, que prometia 60 minutos. Os testes que conferem os 60 minutos mudam, e essa é a exceção prevista no SC-009.

**Efeito no SSE (REQ-18)**: a API encerra a conexão quando o token expira, então ela passa a cair a cada 15 minutos por padrão. O cliente renova e reconecta com `Last-Event-ID`, que recupera os avisos perdidos. Aceito pelo usuário.

## R-07: limpeza (FR-027)

**Decisão**: dois alvos novos na `LimpezaMensageria`, que roda de hora em hora, porque esses dados, ao contrário das contagens do REQ-23, crescem devagar e são limitados pelo limite de requisições:

| Alvo | O que apaga | Retenção padrão |
|------|-------------|-----------------|
| `sessoes_autenticacao` | Sessões encerradas ou vencidas, com os tokens delas, em cascata | 24 h, mínimo 1 h |
| `tokens_uso_unico` | Tokens usados ou vencidos | 24 h, mínimo 1 h |

As solicitações de e-mail saem ao serem processadas e não precisam de limpeza.

## R-08: limite de requisições nas rotas novas (FR-024)

**Decisão**: generalizar o `ServicoLimiteRequisicoes` do REQ-23.

- **`RotaLimitada`** ganha `Renovacao`, `Logout`, `RecuperacaoSenha`, `ReenvioConfirmacao`, `RedefinicaoSenha` e `ConfirmacaoEmail`.
- **`LeitorEmailLogin` vira `LeitorEmailCorpo`**: recuperação e reenvio usam o mesmo par de limites do login. A regra "sem e-mail utilizável conta só pela origem" continua igual.
- **Políticas novas** (padrões do clarify): `recuperacao_origem_email`, `recuperacao_origem`, `reenvio_origem_email`, `reenvio_origem`, `redefinicao`, `confirmacao`, `renovacao` e `logout`.
- **O 429 e o `Retry-After`** são os mesmos, pelo mesmo middleware e metadados.

## R-09: troca e redefinição de senha

**Decisão**: pelo `UserManager`, dentro de uma transação do `ContextoBancoDados`, com o encerramento das sessões (FR-011). Só a redefinição grava a solicitação de aviso (FR-015); a troca não envia e-mail, por decisão do usuário.

- **Troca**: `CheckPasswordAsync` da senha atual. Se errar, `AccessFailedAsync` (FR-012), e o bloqueio continua invisível: a resposta é sempre o 400 da senha atual. Em uma conta já bloqueada, a senha nem é verificada, e a resposta é a mesma.
- **Redefinição**: consome o token com um `UPDATE` condicional, como na rotação, e troca o hash da senha pelo `PasswordHasher`. Zera o bloqueio e a contagem, marca o e-mail como confirmado e atualiza o `SecurityStamp`.
- **Regras da senha**: as do contrato v1, com as mesmas mensagens. Hoje são um `StringLength` e um trecho do `Validate` do registro, sem atributo reutilizável; o trecho passa para `RegrasSenha`, usada pelos três DTOs.

## R-10: logs e métricas (FR-026)

- **Logs** na faixa 3010 a 3018: 3010 `RenovacaoReutilizada`, 3011 `SessaoEncerrada`, 3012 `SenhaTrocada`, 3013 `TrocaSenhaRecusada`, 3014 `SenhaRedefinida`, 3015 `EmailConfirmado`, 3016 `EmailEnviado`, 3017 `FalhaEnvioEmail`, 3018 `EmailDescartado`. Com o id da conta e, quando houver, o e-mail mascarado. **O pedido de recuperação não gera log na API**, pois ela nem sabe se a conta existe. O registro desse pedido é o log 3016 do Worker, quando há conta; sem conta, conta só a métrica `sem_conta`.
- **Métricas**: `oms.autenticacao.renovacoes` (`oms.resultado`: `renovada`, `recusada`, `reutilizada`), na API, e `oms.emails` (`oms.email.tipo`, `oms.resultado`: `enviado`, `falha`, `descartado`, `sem_conta`), no Worker.
- **MailKit sem registro de protocolo**: esse registro poria o corpo do e-mail, com o token, no log.

## R-11: testes

- **Sem SMTP**: `IEnviadorEmail` falso, que guarda as mensagens, nos testes do processador e das rotas.
- **Com SMTP real**: um conjunto pequeno com o Mailpit, para o fluxo de ponta a ponta e a queda do servidor (SC-004, SC-007).
- **`FabricaApi`**: limites altos para as políticas novas, como no REQ-23. **`FabricaWorker`**: `Email:Habilitado=false`, salvo nos testes dele.
- **SC-005 (tempo)**: em uma coleção que não roda em paralelo, comparando as medianas de 100 pedidos de cada caso.
- **SC-006 (despejo)**: o token devolvido ou capturado não aparece em nenhuma coluna, e o hash não autentica nada.

## Resultado do quickstart (T067, 2026-11-05)

Executado com o compose, no perfil `demo`, todas as seções de 3.1 a 3.8 e os logs da seção 4, sem falhas.

- **R-05**: as portas do Mailpit são as documentadas: SMTP na 1025 e tela na 8025.
- **SC-004**: recuperação de ponta a ponta em 9 s, com o limite de 2 minutos.
- **SC-007**: com o contêiner parado de verdade, a rota respondeu o mesmo 202, o Worker registrou o log 3017, e o e-mail chegou 33 s depois da volta, com o limite de 5 minutos.
- **SC-006**: as duas colunas `HashToken` são `binary(32)`, e o token capturado não está no banco.
- **Nenhum e-mail completo nem token** nos logs da API e do Worker.
- **O Mailpit guarda os e-mails em memória**: reiniciar o contêiner apaga os anteriores.
