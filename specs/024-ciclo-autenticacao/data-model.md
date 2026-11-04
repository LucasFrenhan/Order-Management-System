# Modelo de dados: ciclo de vida da autenticação (REQ-24)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Todas na migration `CicloAutenticacao`. São dados técnicos de autenticação e ficam na Infrastructure, como as tabelas do Identity. A coluna existente `Usuarios.EmailConfirmed` passa a ser usada.

## `SessoesAutenticacao`

Uma linha por login.

| Coluna | Tipo | Regras |
|--------|------|--------|
| `Id` | `uniqueidentifier`, PK | |
| `UsuarioId` | `uniqueidentifier`, FK para `Usuarios` | Índice, para encerrar as sessões da conta (FR-011) |
| `IniciadaEm` | `datetimeoffset` | |
| `ExpiraEm` | `datetimeoffset` | `IniciadaEm` + 30 dias por padrão; a rotação não estende (FR-005). Índice, para a limpeza |
| `EncerradaEm` | `datetimeoffset`, nulo | Preenchida no logout, na reutilização e na troca ou redefinição de senha |

## `TokensRenovacao`

| Coluna | Tipo | Regras |
|--------|------|--------|
| `HashToken` | `binary(32)`, PK | SHA-256 do token; o token nunca é guardado (FR-003) |
| `SessaoId` | `uniqueidentifier`, FK para `SessoesAutenticacao` | Em cascata |
| `ExpiraEm` | `datetimeoffset` | Criação + 14 dias por padrão, limitado ao `ExpiraEm` da sessão |
| `UsadoEm` | `datetimeoffset`, nulo | Preenchido na rotação, de forma atômica (R-02) |

**Ciclo do token**:

```text
criado -> usado (na rotação, 1 vez)
usado -> apresentado de novo -> reutilização: 401 e a sessão é encerrada
expirado, ou sessão encerrada -> 401
```

## `TokensUsoUnico`

| Coluna | Tipo | Regras |
|--------|------|--------|
| `HashToken` | `binary(32)`, PK | SHA-256 do token |
| `UsuarioId` | `uniqueidentifier`, FK para `Usuarios` | Índice com `Finalidade` |
| `Finalidade` | `varchar(30)` | `RecuperacaoSenha` ou `ConfirmacaoEmail` |
| `ExpiraEm` | `datetimeoffset` | 30 minutos ou 24 horas por padrão. Índice, para a limpeza |
| `UsadoEm` | `datetimeoffset`, nulo | Consumido de forma atômica |

Um token novo apaga os anteriores da mesma conta e finalidade (FR-014).

## `SolicitacoesEmail`

A fila do envio. Não guarda token nem corpo (R-03).

| Coluna | Tipo | Regras |
|--------|------|--------|
| `Id` | `uniqueidentifier`, PK | |
| `Tipo` | `varchar(30)` | `RecuperacaoSenha`, `ConfirmacaoEmail` ou `AvisoSenhaRedefinida` |
| `Destino` | `nvarchar(256)` | Como foi informado, aparado. Pode não ter conta |
| `CriadaEm` | `datetimeoffset` | Descarte depois de 24 h |
| `ProximaTentativaEm` | `datetimeoffset` | Índice, para a rodada do Worker |
| `Tentativas` | `int` | Espera crescente, de 30 s a 2 min |

A linha é **apagada** ao ser processada, com ou sem envio. Assim, um e-mail sem conta fica no banco só até a rodada seguinte.

## Limites (configuração, sem tabela)

| Política | Rota | Contagem | Padrão |
|----------|------|----------|:------:|
| `recuperacao_origem_email`, `reenvio_origem_email` | Recuperação, reenvio | Origem + e-mail | 3 por hora |
| `recuperacao_origem`, `reenvio_origem` | Recuperação, reenvio | Origem | 20 por hora |
| `redefinicao`, `confirmacao` | Redefinição, confirmação | Origem | 10 por minuto |
| `renovacao`, `logout` | Renovação, logout | Origem | 120 por minuto |

As contagens usam a tabela `ContagensLimite` do REQ-23.
