# Contrato: token de acesso (REQ-03)

O formato da resposta de login (`LoginResponseDTO`) é o do contrato v1
([openapi-v1.yaml](../../001-contratos-api/contracts/openapi-v1.yaml)) e não muda. Este
documento fixa o conteúdo do token emitido, que as rotas protegidas validam desde o REQ-01.

## Cabeçalho

| Campo | Valor |
|-------|-------|
| `alg` | `HS256` (o único aceito na validação) |
| `typ` | `JWT` |

## Claims

| Claim | Valor | Usado por |
|-------|-------|-----------|
| `sub` | id do usuário (Guid) | `ExtensoesUsuario.ObterUsuarioId` |
| `role` | um claim por perfil: `Cliente`, `Admin` ou `OperadorLogistica` | políticas de autorização |
| `iss` | `Jwt:Emissor` (`PedidoFulfillment`) | validação |
| `aud` | `Jwt:Audiencia` (`PedidoFulfillment`) | validação |
| `iat` | instante da emissão (Unix) | — |
| `nbf` | igual a `iat` | validação |
| `exp` | `iat` + `Jwt:DuracaoTokenMinutos` (60) | validação (tolerância de 30 s) |
| `jti` | Guid novo por token | — |

O token não leva nome, e-mail nem nenhum outro dado pessoal.

## Correspondência com a resposta

- `tokenAcesso`: o JWT acima.
- `tipoToken`: `Bearer`.
- `expiraEm`: o mesmo instante de `exp`, em UTC e ISO 8601.
- `perfis`: os mesmos valores dos claims `role`.
