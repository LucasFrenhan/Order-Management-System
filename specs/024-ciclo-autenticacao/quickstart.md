# Quickstart: validar o ciclo de vida da autenticação (REQ-24)

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/autenticacao.md](./contracts/autenticacao.md)

## Pré-requisitos

- .NET 10 SDK e Docker.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~Sessoes|FullyQualifiedName~Senha|FullyQualifiedName~ConfirmacaoEmail|FullyQualifiedName~Emails"
dotnet test
dotnet format --verify-no-changes
```

## 2. Ambiente local

```bash
docker compose --profile demo up -d --wait --build
```

A API responde em `http://localhost:5230`, e os e-mails capturados aparecem em `http://localhost:8025`. Em PowerShell, use `curl.exe`.

## 3. Roteiro manual

### 3.1 Registro e confirmação

Registre uma conta. **Esperado**: 201 na hora, e em até 15 s um e-mail de confirmação no Mailpit. Envie o token para `POST /api/v1/auth/confirmacao-email`.

**Esperado**: 204, e repetir o mesmo token responde 400 `TOKEN_INVALIDO`.

### 3.2 Renovação e reutilização

1. Faça login. **Esperado**: a resposta traz `tokenRenovacao` e o `expiraEm` em 15 minutos.
2. Envie o token para `POST /api/v1/auth/renovacao`. **Esperado**: 200, com um par novo.
3. Envie o **mesmo** token de novo. **Esperado**: 401.
4. Envie o token **novo**. **Esperado**: 401 também, porque a reutilização encerrou a sessão.

### 3.3 Logout

Faça login, chame `POST /api/v1/auth/logout` e tente renovar com esse token.

**Esperado**: 204 no logout, 401 na renovação, e 204 ao repetir o logout. O token de acesso continua funcionando até expirar.

### 3.4 Recuperação de ponta a ponta (SC-004)

1. Peça `POST /api/v1/auth/recuperacao-senha` com o e-mail da conta. **Esperado**: 202.
2. Peça o mesmo com um e-mail **sem conta**. **Esperado**: o mesmo 202, e nenhum e-mail novo no Mailpit para ele.
3. Com o token do e-mail, chame `POST /api/v1/auth/redefinicao-senha`. **Esperado**: 204 e um segundo e-mail, avisando da troca.
4. Faça login com a senha nova. **Esperado**: 200. Com a antiga, 401.
5. Renove com o token de uma sessão anterior. **Esperado**: 401.

### 3.5 Troca de senha

Chame `POST /api/v1/auth/troca-senha` com o token de acesso. **Esperado**: 400 com a senha atual errada, 204 com a certa, e a renovação da sessão anterior recusada.

### 3.6 SMTP fora do ar (SC-007)

1. `docker compose stop mailpit`.
2. Peça uma recuperação. **Esperado**: o mesmo 202.
3. Veja o log 3017 no Worker.
4. `docker compose start mailpit`. **Esperado**: o e-mail chega em até 5 minutos.

### 3.7 Nada de token no banco (SC-006)

Com um token capturado no Mailpit, procure-o nas tabelas `TokensUsoUnico`, `TokensRenovacao` e `SolicitacoesEmail`.

**Esperado**: só colunas `binary(32)`, sem o token, e `SolicitacoesEmail` vazia depois de processar.

### 3.8 Limite de requisições

Peça 4 recuperações seguidas para o mesmo e-mail. **Esperado**: três 202 e um 429 com `Retry-After`.

## 4. Logs

`docker compose logs api worker | grep -E '\[30(1[0-8])\]'`: e-mail mascarado e nenhum token.
