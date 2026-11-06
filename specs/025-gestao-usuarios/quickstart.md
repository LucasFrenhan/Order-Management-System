# Quickstart: validar a gestão de usuários e perfis (REQ-25)

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/gestao-usuarios.md](./contracts/gestao-usuarios.md)

## Pré-requisitos

- .NET 10 SDK e Docker.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~GestaoUsuarios|FullyQualifiedName~ContaDesativada|FullyQualifiedName~Convite"
dotnet test
dotnet format --verify-no-changes
```

Esperado: tudo verde, sem afrouxar asserções existentes, e o comparador de contrato sem mudança incompatível (SC-008).

## 2. Ambiente local

```bash
docker compose --profile demo up -d --wait --build
```

A API responde em `http://localhost:5230`, e os e-mails capturados aparecem em `http://localhost:8025`. Em PowerShell, use `curl.exe`. O Admin inicial é `admin@oms.local` / `AdminDev2026`.

## 3. Roteiro

Use o token de login do Admin inicial em `Authorization: Bearer <token>`. As rotas e os corpos estão no [contrato](./contracts/gestao-usuarios.md).

| # | Ação | Resultado esperado | Critério |
|---|------|--------------------|----------|
| 1 | Criar um OperadorLogistica | 201, `convitePendente: true`, sem dado de credencial; login com qualquer senha dá 401 | US2 |
| 2 | Ler o convite no Mailpit e definir a senha | 204; login funciona; rotas de operação respondem; gestão dá 403. Tudo em menos de 2 min desde o passo 1, sem reiniciar nada | SC-001, SC-002 |
| 3 | Criar um segundo Admin e aceitar o convite | Lista com `perfil=Admin` mostra os dois | US1 |
| 4 | Como segundo Admin, retirar o perfil Admin do primeiro; com o token antigo do primeiro, chamar a listagem de usuários | 200 na retirada; 403 na listagem, mesmo com o token válido. Devolva depois o perfil ao primeiro | SC-002 |
| 5 | Como um Admin, retirar o próprio perfil Admin e desativar a própria conta | 409 `USUARIO_PROPRIA_CONTA` nos dois | US3, US4 |
| 6 | Desativar o outro Admin e repetir o passo anterior com o Admin restante | 409 `USUARIO_ULTIMO_ADMIN` nos dois | US3, US4 |
| 7 | Atribuir `Admin` a uma conta criada no registro público | 409 `USUARIO_PERFIS_INCOMPATIVEIS` | US3 |
| 8 | Cliente com um pedido e um webhook: desativar a conta; tentar login e renovação; pedir a recuperação de senha | 401 nos dois primeiros, iguais aos de sempre; 202 na recuperação e nenhum e-mail no Mailpit | SC-004, US4 |
| 9 | Mudar o status do pedido durante a desativação e reativar a conta | O webhook não recebe esse aviso nem depois de reativar; uma mudança nova chega | US4 |
| 10 | Conferir o pedido e o login depois de reativar | Pedido igual; login com a mesma senha; a sessão antiga continua recusada | SC-005, US4 |
| 11 | Conferir os logs da API | Um log 3020 a 3025 por alteração, com ids e sem nome, e-mail ou endereço de origem | SC-006, SC-007 |

## 4. Medição da busca

Pendente desde o plano ([R-07](./research.md#r-07-listagem-e-busca-fr-004)): com 100 mil contas, a listagem com `busca` deve ter p95 abaixo de 500 ms. Se falhar, avise o usuário antes do PR, com o número medido.

## 5. Limpeza

```bash
docker compose --profile demo down -v
```

Confira no fim que os testes não deixaram contêineres, volumes ou imagens temporários no Docker.
