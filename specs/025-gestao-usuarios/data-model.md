# Data model: gestão de usuários e perfis (REQ-25)

**Spec**: [spec.md](./spec.md) | **Plano**: [plan.md](./plan.md) | **Decisões**: [research.md](./research.md)

Migration `GestaoUsuarios`, só com acréscimos. Contas existentes ficam ativas, com as duas datas nulas.

## `Usuarios` (tabela existente)

| Coluna nova | Tipo | Regra |
|-------------|------|-------|
| `DesativadoEm` | `datetimeoffset`, opcional | Preenchida na desativação; nula na reativação. Não nula = conta desativada |
| `ReativadoEm` | `datetimeoffset`, opcional | Preenchida na reativação e mantida depois. Avisos anteriores a ela não vão aos destinos próprios da conta (R-06) |

**Índice novo**: `IX_Usuarios_Nome_Id` (`Nome`, `Id`), para a listagem por cursor.

**Situação derivada, sem coluna própria**:

| Campo da resposta | Como se obtém |
|-------------------|---------------|
| `situacao` | `Desativada` se `DesativadoEm` não é nula; senão `Ativa` |
| `convitePendente` | `PasswordHash` nulo |
| `emailConfirmado` | `EmailConfirmed` |

## Admin ativo

Conta com linha em `UsuariosPerfis` para o perfil Admin, `DesativadoEm` nula e `PasswordHash` não nulo. O bloqueio por erros de senha não entra na conta.

## Transições

```text
              criação pelo Admin                definir senha pelo convite
                     │                          ou pela recuperação
                     ▼                                   │
          Ativa, convite pendente ───────────────────────┴──▶ Ativa
                     │  ▲                                      │  ▲
          desativar  │  │ reativar                  desativar  │  │ reativar
                     ▼  │                                      ▼  │
       Desativada, convite pendente                        Desativada
```

- **Desativar**: em uma transação, grava `DesativadoEm`, encerra as sessões e apaga os tokens de uso único da conta.
- **Reativar**: em uma transação, limpa `DesativadoEm`, grava `ReativadoEm` e encerra de novo as sessões (R-08). A senha e os perfis não mudam.
- **Convite pendente depois de reativar**: o token foi apagado, então a pessoa usa a recuperação de senha.
- **Registro público**: nasce `Ativa`, sem convite pendente, como hoje.

## Tokens de uso único e e-mails

| Enum | Valor novo | Observação |
|------|------------|------------|
| `FinalidadeTokenUsoUnico` | `ConviteUsuario` | No fim do enum, para não mudar os valores já gravados. Mesma forma da recuperação: só o SHA-256 |
| `TipoEmail` | `ConviteUsuario` | No fim do enum, pelo mesmo motivo. O corpo vem de `ModelosEmail`, em português |

A limpeza do REQ-24 já cobre a tabela de tokens, sem distinção de finalidade.

## Perfis por conta

| Grupo | Perfis | Regra |
|-------|--------|-------|
| Interno | `Admin`, `OperadorLogistica` | Podem se combinar. A conta nasce só pela criação do Admin ou pela carga inicial |
| Cliente | `Cliente` | Não se combina com os internos. A conta nasce pelo registro público |

Dados anteriores que já misturem os grupos, ou sem nenhum perfil, seguem os casos de borda da [spec](./spec.md#edge-cases). A regra é de aplicação, sem restrição no banco.

## Configuração

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `Email:ValidadeConviteHoras` | 72 | 1 a 720 |
