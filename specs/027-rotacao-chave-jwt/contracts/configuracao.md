# Contrato: configuração das chaves (REQ-27)

Valem as regras do REQ-02: variáveis de ambiente prevalecem sobre os arquivos, e só valores de
desenvolvimento podem estar versionados. Os nomes em variável de ambiente trocam `:` por `__`.

## Jwt:Chaves (lista, obrigatória)

| Chave | Variável de ambiente | Regra |
|-------|----------------------|-------|
| `Jwt:Chaves:N:Identificador` | `Jwt__Chaves__N__Identificador` | 1 a 64 caracteres em `A-Z`, `a-z`, `0-9`, `-`, `_` e `.`; único |
| `Jwt:Chaves:N:Valor` | `Jwt__Chaves__N__Valor` | No mínimo 32 bytes em UTF-8; secreto |

`N` vai de 0 a 2 (no máximo 3 chaves).

## Jwt:ChaveAtiva (texto, obrigatória)

Variável `Jwt__ChaveAtiva`. O identificador de uma das chaves de `Jwt:Chaves`; é a que emite.

## Jwt:ChaveAssinatura (removida)

Preenchida, a partida falha, mesmo com o formato novo também preenchido. Para migrar sem invalidar
os cursores, copie o mesmo valor para `Jwt:Chaves:0:Valor`.

## Listagens:ChaveCursor (texto, opcional)

Variável `Listagens__ChaveCursor`. Se preenchida, no mínimo 32 bytes em UTF-8, e a chave dos
cursores das listagens (pedidos, produtos e usuários) passa a vir dela. Vazia, a chave dos cursores
é derivada da chave ativa do JWT, e a troca da chave ativa invalida os cursores em uso (400).

## Mensagens de falha na partida

Todas as falhas são reunidas numa só `OptionsValidationException`. Nenhuma traz um valor de chave.

| Situação | Mensagem |
|----------|----------|
| `Jwt:ChaveAssinatura` preenchida | `Jwt:ChaveAssinatura não é mais aceita. Configure Jwt:Chaves e Jwt:ChaveAtiva (docs/rotacao-chave-jwt.md).` |
| Nenhuma chave | `Jwt:Chaves deve ter ao menos uma chave.` |
| Mais de 3 chaves | `Jwt:Chaves aceita no máximo 3 chaves.` |
| Identificador vazio ou fora do formato | `Jwt:Chaves:{N}:Identificador deve ter de 1 a 64 caracteres entre letras sem acento, dígitos, '-', '_' e '.'.` |
| Identificador repetido | `Jwt:Chaves: o identificador '{id}' está repetido.` |
| Valor curto | `Jwt:Chaves: a chave '{id}' deve ter ao menos 32 bytes.` |
| Chave ativa vazia | `Jwt:ChaveAtiva é obrigatória.` |
| Chave ativa fora da lista | `Jwt:ChaveAtiva '{id}' não está entre as chaves de Jwt:Chaves.` |
| Emissor ou audiência vazios | `Jwt:Emissor e Jwt:Audiencia são obrigatórios.` (como hoje) |
| `Listagens:ChaveCursor` curta | `Listagens:ChaveCursor deve ter ao menos 32 bytes.` |

`{N}` é a posição na lista; `{id}` é o identificador, que não é segredo. O identificador fora do
formato é citado só pela posição, porque pode ser um valor colado no campo errado.

## Logs

| ID | Nome | Nível | Quando | Texto |
|----|------|-------|--------|-------|
| 3026 | `ChavesJwtCarregadas` | Information | Na partida | `Chaves do JWT: ativa {ChaveAtiva}; aceitas {ChavesAceitas}.` |
| 3027 | `TokenChaveDesconhecida` | Warning | Token recusado por `kid` ausente, retirado ou desconhecido | `Token recusado: a chave {IdentificadorChave} não está configurada.` |

No 3027, `{IdentificadorChave}` é o `kid` recebido quando ele tem o formato de um identificador,
`(ausente)` quando o token não tem `kid` e `(inválido)` nos demais casos. Nenhum dos dois registra o
token, o usuário ou o endereço de origem. A lista `{ChavesAceitas}` é vazia (`-`) com uma só chave.

## Valores de desenvolvimento e de testes

- `appsettings.Development.json`: uma chave, `desenvolvimento-local`, com o mesmo valor
  `SOMENTE-DESENVOLVIMENTO-...` de hoje, e `ChaveAtiva` igual a ela.
- `FabricaApi`: uma chave, `teste-a`, com o valor `ChaveTeste` de hoje, e `ChaveAtiva = teste-a`.
