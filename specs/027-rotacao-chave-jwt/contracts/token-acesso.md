# Contrato: token de acesso (REQ-27)

Muda só o cabeçalho do token definido no
[contrato do REQ-03](../../003-autenticacao-identity/contracts/token-acesso.md). As claims, a
resposta de login e o contrato v1 (OpenAPI) não mudam.

## Cabeçalho

| Campo | Valor |
|-------|-------|
| `alg` | `HS256` (o único aceito na validação) |
| `typ` | `JWT` |
| `kid` | **Novo.** O identificador da chave ativa (`Jwt:ChaveAtiva`) que assinou o token |

## Regras de validação

| Token | Resposta |
|-------|----------|
| `kid` de uma chave configurada (ativa ou aceita) e assinatura que confere | Segue para as demais validações de hoje (emissor, audiência, validade) |
| `kid` de uma chave retirada ou desconhecido | 401, o mesmo corpo de token inválido; log 3027 |
| Sem `kid` (inclusive os emitidos antes do REQ-27) | 401, o mesmo corpo de token inválido; log 3027 com `(ausente)` |
| `kid` configurado e assinatura que não confere | 401, o mesmo corpo de token inválido; sem log 3027 |

O corpo do 401 é o de hoje (`ProblemDetails` "Não autenticado." / "Informe um token de acesso
válido.", com `WWW-Authenticate: Bearer`).

## Compatibilidade

O `kid` é um campo novo do cabeçalho: clientes que tratam o token como opaco, como pede o contrato
v1, não são afetados. Na implantação, os tokens em uso (sem `kid`) passam a receber 401 uma vez, e o
cliente renova a sessão (REQ-24) sem digitar a senha. A renovação não depende da chave do JWT.
