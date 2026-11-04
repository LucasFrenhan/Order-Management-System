# Contrato: ciclo de vida da autenticação (REQ-24)

**Spec**: [spec.md](../spec.md) | **Contrato v1**: [openapi-v1.yaml](../../001-contratos-api/contracts/openapi-v1.yaml)

Tudo por acréscimo ao contrato v1 (FR-028). Prefixo das rotas: `/api/v1/auth`.

## Acréscimo ao login

`POST /login`, na resposta 200, além dos campos atuais:

| Campo | Tipo | Conteúdo |
|-------|------|----------|
| `tokenRenovacao` | string | Token opaco, de uso único |
| `tokenRenovacaoExpiraEm` | date-time | Quando ele expira |

O `expiraEm` do token de acesso passa a ser de 15 minutos por padrão, e o contrato já o informa.

## Rotas novas

| Rota | Acesso | Corpo | Sucesso | Erros |
|------|--------|-------|---------|-------|
| `POST /renovacao` | Pública | `tokenRenovacao` | 200, com o mesmo corpo do login | 400, 401, 429, 500 |
| `POST /logout` | Pública | `tokenRenovacao` | 204, em qualquer caso | 400, 429, 500 |
| `POST /troca-senha` | Autenticada, todos os perfis | `senhaAtual`, `novaSenha` | 204 | 400, 401, 403, 500 |
| `POST /recuperacao-senha` | Pública | `email` | 202, em qualquer caso | 400, 429, 500 |
| `POST /redefinicao-senha` | Pública | `token`, `novaSenha` | 204 | 400, 429, 500 |
| `POST /confirmacao-email` | Pública | `token` | 204 | 400, 429, 500 |
| `POST /confirmacao-email/reenvio` | Pública | `email` | 202, em qualquer caso | 400, 429, 500 |

O 429 é o do [REQ-23](../../023-rate-limiting/contracts/limite-requisicoes.md#resposta-429). As rotas com corpo vazio respondem 204 ou 202 sem corpo.

## Mensagens

| Caso | Resposta | Código |
|------|----------|--------|
| Renovação: token inválido, expirado, reutilizado ou de sessão encerrada | 401 "Sessão inválida." / "Faça login novamente." | — |
| Redefinição: token inválido, expirado ou usado | 400 "Token inválido." / "O token é inválido ou expirou. Peça uma nova recuperação." | `TOKEN_INVALIDO` |
| Confirmação: token inválido, expirado ou usado | 400 "Token inválido." / "O token é inválido ou expirou. Peça um novo e-mail." | `TOKEN_INVALIDO` |
| Troca: senha atual errada, ou conta bloqueada | 400, `erros.senhaAtual`: "A senha atual está incorreta." | — |
| Troca: senha nova igual à atual | 400, `erros.novaSenha`: "A nova senha deve ser diferente da atual." | — |
| Senha nova fora das regras | 400, `erros.novaSenha`, com as mensagens das regras do contrato v1 | — |
| Corpo inválido, campo ausente ou e-mail mal formado | 400, como nas demais rotas | — |

**Código novo**: `TOKEN_INVALIDO`, com `NegocioProblemDetails`. O motivo real nunca aparece na resposta (FR-006, FR-016).

## Configuração nova

**API, seção `Sessoes`**:

| Chave | Padrão | Faixa |
|-------|:------:|:-----:|
| `ValidadeTokenRenovacaoDias` | 14 | 1 a 90 |
| `DuracaoMaximaDias` | 30 | 1 a 365; não menor que a validade do token |

**API, seção `Jwt`**: o padrão de `DuracaoTokenMinutos` passa de 60 para 15.

**API, seção `LimiteRequisicoes`**: `RecuperacaoOrigemEmail`, `RecuperacaoOrigem`, `ReenvioOrigemEmail`, `ReenvioOrigem`, `Redefinicao`, `Confirmacao`, `Renovacao` e `Logout`, cada uma com `Limite` e `JanelaSegundos`, nas faixas do REQ-23. Padrões em [data-model](../data-model.md#limites-configuração-sem-tabela).

**Worker, seção `Email`**: em [R-04](../research.md#r-04-pacote-e-configuração-do-envio-fr-022-fr-023).

**Worker, seção `Limpeza`**: `RetencaoSessoesHoras` e `RetencaoTokensUsoUnicoHoras`, padrão 24, de 1 a 8.760.

## Observabilidade

Logs e métricas em [R-10](../research.md#r-10-logs-e-métricas-fr-026). Nenhum sinal contém token, senha, e-mail completo ou endereço de origem (SC-008).
