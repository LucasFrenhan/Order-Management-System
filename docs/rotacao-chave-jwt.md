# Rotação da chave do JWT

[← Voltar ao README](../README.md)

O token de acesso é assinado com HMAC-SHA256 por uma de até 3 chaves configuradas. Cada chave tem um identificador, que
vai no cabeçalho do token (`kid`), e uma delas, a ativa, assina os tokens novos. As demais só validam. Isso permite trocar
a chave sem derrubar as sessões (REQ-27, [spec](../specs/027-rotacao-chave-jwt/spec.md); comportamento no
[fluxo 1](fluxos/01-autenticacao.md#chaves-de-assinatura)).

## Configuração

| Chave | Variável de ambiente | Regra |
|-------|----------------------|-------|
| `Jwt:Chaves:N:Identificador` | `Jwt__Chaves__N__Identificador` | De 1 a 64 caracteres entre letras sem acento, dígitos, `-`, `_` e `.`; único; diferencia maiúsculas de minúsculas |
| `Jwt:Chaves:N:Valor` | `Jwt__Chaves__N__Valor` | Secreto; no mínimo 32 bytes em UTF-8 |
| `Jwt:ChaveAtiva` | `Jwt__ChaveAtiva` | O identificador de uma das chaves; é a que emite |
| `Listagens:ChaveCursor` | `Listagens__ChaveCursor` | Opcional e secreta; no mínimo 32 bytes. Recomendada em produção (veja [Cursores](#cursores-das-listagens)) |

`N` vai de 0 a 2. Com configuração inválida, a API não sobe, e a mensagem cita o identificador ou a posição, nunca o
valor ([mensagens](../specs/027-rotacao-chave-jwt/contracts/configuracao.md#mensagens-de-falha-na-partida)).

As variáveis de ambiente sobrescrevem o arquivo **posição por posição**: `Jwt__Chaves__0__Valor` troca só o valor da
posição 0, e uma posição definida só no arquivo continua valendo. Ao retirar uma chave, confira as duas origens.

## Gerar uma chave nova

Use um gerador criptográfico. Os comandos abaixo produzem 64 caracteres (48 bytes aleatórios em Base64):

```bash
openssl rand -base64 48
```

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))
```

Não use `Get-Random` nem `$RANDOM`, que não são criptográficos. Gere uma chave nova a cada rotação e nunca reaproveite um
valor com outro identificador.

## Procedimento

O tempo mínimo de espera antes de retirar a chave antiga é a validade máxima do token de acesso mais a tolerância de
relógio da validação: `Jwt:DuracaoTokenMinutos` + 30 s, ou **15 min 30 s** com o padrão.

| Etapa | Configuração | Efeito | Conferência |
|-------|--------------|--------|-------------|
| 1. Acrescentar | A chave nova B entra em `Jwt:Chaves`; a ativa continua a antiga A | Nada muda para os clientes: os tokens continuam saindo com A, e B passa a ser aceita | Log 3026 `ativa A; aceitas B` em **todas** as instâncias |
| 2. Ativar | `Jwt:ChaveAtiva` passa a B | Os tokens novos saem com B; os de A continuam aceitos | Log 3026 `ativa B; aceitas A` em todas as instâncias |
| 3. Retirar | A sai de `Jwt:Chaves`, depois do tempo mínimo contado a partir da última instância da etapa 2 | Um token de A que ainda existisse receberia 401, mas todos já expiraram | Log 3026 `ativa B; aceitas -`; nenhum log 3027 com `A` |

Cada etapa só vale depois de reiniciar as instâncias, uma de cada vez: as chaves são lidas na partida. **A etapa 1 precisa
chegar a todas as instâncias antes da etapa 2**; senão, uma instância que ainda não conhece B recusa os tokens que outra
já emite com B.

## Retirada de emergência

Quando uma chave vaza, não espere o prazo: quem tem o valor dela forja tokens aceitos enquanto ela estiver configurada.

1. Gere a chave nova C e configure-a como **ativa**, retirando a vazada **na mesma etapa**.
2. Reinicie todas as instâncias o mais rápido possível.

Os tokens da chave vazada recebem 401, com o log 3027. A renovação da sessão não depende da chave do JWT: o cliente renova
com o token de renovação e recebe um token de C, sem digitar a senha. Durante o reinício, uma instância antiga ainda pode
emitir com a chave vazada, e a nova recusa esse token; o cliente renova de novo, e a situação se resolve quando todas as
instâncias tiverem a configuração nova.

O limite de 3 chaves comporta uma emergência no meio de uma rotação planejada: com A e B configuradas, C é a terceira.

## Migração da configuração anterior

A antiga `Jwt:ChaveAssinatura` não é mais aceita: a API não sobe com ela. Para migrar:

```text
Jwt__Chaves__0__Identificador=<um identificador, por exemplo 2026-11-jwt-a>
Jwt__Chaves__0__Valor=<o mesmo valor da Jwt__ChaveAssinatura>
Jwt__ChaveAtiva=<o mesmo identificador>
```

e remova `Jwt__ChaveAssinatura`. Usar o mesmo valor mantém válidos os cursores das listagens. Os tokens emitidos antes da
migração não têm `kid` e recebem 401 uma vez: cada cliente renova a sessão sem digitar a senha.

## Cursores das listagens

Os cursores das listagens de pedidos, produtos e usuários são assinados com `Listagens:ChaveCursor`. Sem ela, a chave dos
cursores é derivada da chave ativa do JWT, e a etapa 2 invalida os cursores em uso (400; o cliente recomeça da primeira
página). Com ela, a rotação do JWT não afeta os cursores. Trocar a própria `Listagens:ChaveCursor` invalida os cursores em
uso.

## Estudo: assinatura assimétrica e origem das chaves

Detalhes em [research R-08 e R-09](../specs/027-rotacao-chave-jwt/research.md#r-08--estudo-assinatura-assimétrica-rs256-e-es256).

| Critério | HS256 (atual) | RS256 | ES256 |
|----------|---------------|-------|-------|
| Quem pode emitir | Quem valida (segredo compartilhado) | Só quem tem a chave privada | Só quem tem a chave privada |
| Chave pública publicável (JWKS) | Não | Sim | Sim |
| Tamanho da assinatura | 32 bytes | 256 bytes (RSA 2048) | 64 bytes |
| Custo de assinar / validar | Mínimo / mínimo | Alto / baixo | Baixo / médio |

**Recomendação**: manter o HS256 com rotação enquanto só a própria API emite e valida o token. Adotar o **ES256**, com a
chave pública publicada (JWKS), quando outro serviço precisar validar tokens, como um gateway, o Worker ou um provedor de
identidade. A migração usa o mesmo procedimento: a chave ES256 entra como aceita, vira ativa e a HS256 sai.

| Origem das chaves | Encaixe | Dependência |
|-------------------|---------|-------------|
| Variáveis de ambiente (atual) | Já funciona | Nenhuma |
| Arquivos de segredo do Docker/Kubernetes | Valor montado como arquivo, fora do `docker inspect` | `Microsoft.Extensions.Configuration.KeyPerFile` |
| Cofre da nuvem (Azure Key Vault, AWS Secrets Manager, HashiCorp Vault) | Provedor de configuração; o valor não passa pelo ambiente | Pacote do provedor e identidade da aplicação |

**Recomendação**: quando houver ambiente de produção definido, usar o cofre da nuvem escolhida pelo provedor de
configuração oficial, ou arquivos de segredo montados em Docker/Kubernetes sem nuvem. As chaves já entram pelo
`IConfiguration`, então nada muda no modelo de chaves; a dependência nova fica para um requisito futuro, como pede a
constituição.
