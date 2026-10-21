# Data Model: Autenticação e autorização de usuários (REQ-03)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

O banco da aplicação (`PedidoFulfillment`) nasce neste REQ, com a migration
`InicialIdentidade`. As entidades de negócio entram no REQ-04, em migrations seguintes, no
mesmo `ContextoBancoDados` (R-07).

## Entidades persistidas

### Usuario (tabela `Usuarios`)

Estende o usuário do Identity (`IdentityUser<Guid>`). Campos relevantes:

| Campo | Tipo | Regra |
|-------|------|-------|
| Id | Guid | chave; `Guid.CreateVersion7()` |
| Nome | nvarchar(100) | obrigatório; guardado sem espaços nas pontas (FR-004) |
| Email | nvarchar(256) | obrigatório; como informado no registro (FR-004) |
| NormalizedEmail | nvarchar(256) | índice **único** `EmailIndex` (FR-003) |
| UserName | nvarchar(256) | igual a `Email` |
| NormalizedUserName | nvarchar(256) | índice único `UserNameIndex` (padrão do Identity) |
| PasswordHash | nvarchar(max) | hash com sal do Identity (PBKDF2); nunca sai da Infrastructure (FR-002) |
| AccessFailedCount | int | erros seguidos de login; zerado no login bem-sucedido (FR-009) |
| LockoutEnabled | bit | `true` para todo usuário |
| LockoutEnd | datetimeoffset? | fim do bloqueio; 15 min após o 5º erro (FR-009) |
| SecurityStamp, ConcurrencyStamp | nvarchar(max) | controle do Identity |

Os demais campos do Identity (telefone, 2FA, confirmação de e-mail) existem na tabela, mas
não são usados (fora do escopo, REQ-01).

### Perfil (tabela `Perfis`)

`IdentityRole<Guid>`. Uma linha por valor de `PerfilUsuario`: `Cliente`, `Admin`,
`OperadorLogistica`. `NormalizedName` com índice único `RoleNameIndex`. Criados na
inicialização, sem duplicar (FR-011).

### UsuarioPerfil (tabela `UsuariosPerfis`)

Associação N:N entre `Usuarios` e `Perfis` (`UserId`, `RoleId`), chave composta. Registro
público: sempre `Cliente`. Usuários iniciais: `Admin` ou `OperadorLogistica`.

### Tabelas auxiliares do Identity

`UsuariosClaims`, `PerfisClaims`, `UsuariosLogins` e `UsuariosTokens`: criadas pelo
modelo do Identity e não usadas neste REQ.

## Modelos de aplicação (não persistidos)

### ResultadoCredenciais (Application)

Devolvido por `IGestorIdentidade.VerificarCredenciaisAsync` (R-05):

| Situação | Quando | Resposta da API | Evento de log |
|----------|--------|-----------------|---------------|
| `Valida` (com id, nome, perfis) | senha correta e conta não bloqueada | 200 | `LoginRealizado` |
| `Invalida` (id opcional) | e-mail sem conta ou senha errada | 401 genérico | `LoginRecusado` (`CredenciaisInvalidas`) |
| `Bloqueada` (id) | conta em bloqueio | 401 genérico | `LoginRecusado` (`ContaBloqueada`) |
| `BloqueadaAgora` (id) | o erro atual foi o 5º seguido | 401 genérico | `LoginRecusado` + `ContaBloqueada` |

### Ciclo do bloqueio

```text
Livre ──(erro, contagem < 5)──▶ Livre (contagem + 1)
Livre ──(5º erro seguido)─────▶ Bloqueada (LockoutEnd = agora + 15 min, contagem zerada)
Bloqueada ──(qualquer login)──▶ Bloqueada (401 genérico, senha nem é verificada)
Bloqueada ──(15 min passam)───▶ Livre
Livre ──(login correto)───────▶ Livre (contagem zerada)
```

### Token de acesso

Ver [contracts/token-acesso.md](./contracts/token-acesso.md). Não é guardado no servidor.

## Configuração

Ver [contracts/configuracao.md](./contracts/configuracao.md): `Banco:PrepararNaInicializacao`
e `UsuariosIniciais`.
