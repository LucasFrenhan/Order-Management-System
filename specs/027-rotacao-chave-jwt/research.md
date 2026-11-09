# Research: Rotação da chave de assinatura do JWT (REQ-27)

Decisões técnicas do plano, cada uma com o motivo e as alternativas. Os itens R-08 e R-09 são o
estudo pedido pela issue #60 (FR-011); o resumo deles vai para `docs/rotacao-chave-jwt.md`.

## R-01 — Formato da configuração das chaves

**Decision**: lista indexada na seção `Jwt`, mais o identificador da chave ativa:

```text
Jwt:Chaves:0:Identificador = 2026-11-jwt-a
Jwt:Chaves:0:Valor         = <segredo>
Jwt:Chaves:1:Identificador = 2026-12-jwt-b
Jwt:Chaves:1:Valor         = <segredo>
Jwt:ChaveAtiva             = 2026-12-jwt-b
```

Em variável de ambiente: `Jwt__Chaves__0__Identificador`, `Jwt__Chaves__0__Valor`, `Jwt__ChaveAtiva`.
O valor continua sendo texto, com o tamanho medido em bytes UTF-8, como a `Jwt:ChaveAssinatura`
de hoje: migrar o mesmo texto para `Jwt:Chaves:0:Valor` mantém as assinaturas e os cursores
derivados (R-06).

**Rationale**: o identificador fica no valor, e não no nome da chave de configuração, porque o
formato permitido (`-` e `.`) não é aceito em nome de variável de ambiente em vários shells. A
chave ativa separada da lista torna a etapa 2 da rotação uma troca de um só valor.

**Alternatives considered**:
- Dicionário `Jwt:Chaves:<identificador> = <valor>`: mais curto, mas o identificador vira parte
  do nome da variável de ambiente (`Jwt__Chaves__2026-11-jwt-a`), o que muitos shells recusam.
- Marcar a ativa com `Ativa: true` em cada item: permite duas ativas por engano e exige mexer em
  dois itens na etapa 2.
- Valor em Base64 decodificado: mais entropia por caractere, mas quebra a continuidade com a chave
  atual, que é texto.

## R-02 — Validação com várias chaves e recusa do token sem `kid`

**Decision**: `TokenValidationParameters.IssuerSigningKeys` com uma `SymmetricSecurityKey` por chave
configurada, cada uma com `KeyId` igual ao identificador, e `TryAllIssuerSigningKeys = false`. Um
`IssuerSigningKeyResolver` próprio devolve só a chave cujo identificador é igual ao `kid` do token
em comparação ordinal (diferencia maiúsculas de minúsculas); sem `kid` ou com `kid` desconhecido,
devolve nenhuma, e a validação falha com 401. O algoritmo continua restrito a `HS256`.

**Rationale**: `TryAllIssuerSigningKeys` é verdadeiro por padrão na Microsoft.IdentityModel e, com
ele, um token sem `kid` (ou com `kid` desconhecido) é testado contra todas as chaves e aceito: isso
contraria a decisão do specify (FR-014). O resolvedor próprio torna a comparação explícita e
independente da versão da biblioteca; os testes de integração (FR-013) confirmam o comportamento.

**Alternatives considered**: só `IssuerSigningKeys` com `TryAllIssuerSigningKeys = false`, sem
resolvedor: provavelmente suficiente, mas a forma de comparar o `kid` fica implícita na biblioteca.

## R-03 — Emissão com a chave ativa

**Decision**: o `EmissorTokenJwt` assina com a `SymmetricSecurityKey` da chave ativa, com `KeyId`
preenchido; o `JsonWebTokenHandler` põe o `kid` no cabeçalho a partir do `KeyId`. As chaves são
montadas uma vez, num singleton `ConjuntoChavesJwt`, usado pelo emissor, pela validação, pela chave
do cursor (R-06) e pelo log da partida (R-05).

**Rationale**: uma só fonte para as quatro pontas evita que a emissão e a validação divirjam, e não
recria as chaves a cada token.

**Alternatives considered**: ler `IOptions<OpcoesJwt>` em cada ponta, como hoje: repete a conversão
para bytes e a busca da ativa em três lugares.

## R-04 — Validação da configuração na partida

**Decision**: um `IValidateOptions<OpcoesJwt>` (`ValidadorOpcoesJwt`) que junta todas as falhas, com
`ValidateOnStart`, no lugar dos `Validate(...)` em linha de hoje. As mensagens estão no
[contrato de configuração](contracts/configuracao.md); citam o identificador (público) ou a posição
na lista, nunca o valor. Um identificador fora do formato é citado só pela posição, porque pode ser
um segredo colado no campo errado. A presença de `Jwt:ChaveAssinatura` falha a partida mesmo com o
formato novo preenchido (FR-007). A chave do cursor tem o próprio validador (R-06).

**Rationale**: um validador em classe testa cada regra isoladamente e devolve todas as falhas de uma
vez, o que poupa reinícios a quem corrige a configuração. É o padrão de `ConfiguracaoAusenteTestes`
(a partida lança `OptionsValidationException` com as mensagens).

**Alternatives considered**: manter vários `.Validate(...)` em linha: não citam o identificador
repetido nem juntam as falhas de forma legível.

## R-05 — Logs da partida e da recusa

**Decision**: dois eventos novos na API (`EventosChavesJwt`), na faixa 3xxx da autenticação, que tem
3001 a 3025 em uso:

- **3026 `ChavesJwtCarregadas`** (Information), na partida, por um `IHostedService`: o identificador
  da chave ativa e os das aceitas.
- **3027 `TokenChaveDesconhecida`** (Warning), no `OnAuthenticationFailed` do JwtBearer: lê o
  cabeçalho do token recebido sem validá-lo (`JsonWebTokenHandler.ReadJsonWebToken`) e, quando o
  `kid` está ausente ou não está no conjunto, registra o `kid` apresentado. Um `kid` fora do formato
  do identificador vira `(inválido)` e um ausente vira `(ausente)`, para que texto de quem chama não
  entre cru no log (injeção de log). Não registra o token, o `sub` nem o endereço de origem.

**Rationale**: o 3026 mostra qual chave cada instância usa (o caso das instâncias com configurações
diferentes); o 3027 mostra que ainda chegam tokens de uma chave retirada. A resposta ao cliente não
muda (FR-004).

**Alternatives considered**: classificar pela exceção da biblioteca (`SecurityTokenSignatureKeyNotFoundException`):
o tipo varia entre versões e entre "sem `kid`" e "sem chave"; ler o cabeçalho é estável. Uma
métrica de recusas foi descartada no clarify.

**Risco aceito**: um volume alto de tokens forjados gera um volume alto de 3027. O limite de
requisições (REQ-23) cobre só login e registro; o filtro de nível de log e a coleta OTLP absorvem o
volume. Fica nas limitações do fluxo 1.

## R-06 — Chave própria do cursor

**Decision**: configuração opcional `Listagens:ChaveCursor` (texto, no mínimo 32 bytes UTF-8),
validada na partida por `OpcoesListagens`. A `ChaveCursorDerivadaDoJwt` dá lugar a `ChaveCursorListagens`:
com `Listagens:ChaveCursor`, deriva a chave por HKDF-SHA256 dela; sem ela, da chave ativa do JWT. O
contexto do HKDF continua `oms/listagem-pedidos/cursor/v1`.

**Rationale**: com o mesmo contexto e o mesmo texto da chave atual migrado para a chave ativa, os
cursores em uso continuam válidos na implantação. Com a chave própria configurada, a rotação do JWT
não toca nos cursores (SC-006). O HKDF normaliza o tamanho para 32 bytes nos dois casos. A chave
serve às três listagens (pedidos, produtos e usuários), pela mesma `IChaveCursor`.

**Alternatives considered**:
- Chave própria obrigatória: mais uma configuração obrigatória em toda instalação; o usuário
  escolheu a opcional.
- Usar os bytes da chave própria sem HKDF: exigiria exatamente 32 bytes ou outra regra.

Trocar a própria `Listagens:ChaveCursor` invalida os cursores em uso (400), como hoje; fica nas
limitações, sem rotação com várias chaves para o cursor.

## R-07 — Testes

**Decision**:
- `GeradorTokenTeste` ganha o parâmetro `identificadorChave` (padrão: o da chave de teste) e
  `chave`, para assinar com qualquer chave, sem `kid` (`identificadorChave: null`) ou com `kid`
  desconhecido.
- `FabricaApi` passa a configurar `Jwt:Chaves:0` e `Jwt:ChaveAtiva`; os cenários de rotação usam
  `Configuracoes` para acrescentar, ativar e retirar chaves, e `CapturaLogs` para os eventos 3026 e
  3027.
- As regras do validador são testadas sem subir a API, no projeto de integração (o de unidade não
  referencia a API), e a falha da partida é testada pela `FabricaApi`, como em
  `ConfiguracaoAusenteTestes`.
- O cenário de emergência (US2, cenário 4) usa o banco (Testcontainers), como `SessoesTestes`: login
  com a chave A, retirada de A, 401, renovação e token novo com a chave ativa.

**Rationale**: reusa a infraestrutura dos REQs 03, 23 e 24, sem dependência nova.

## R-08 — Estudo: assinatura assimétrica (RS256 e ES256)

| Critério | HS256 (atual) | RS256 | ES256 |
|----------|---------------|-------|-------|
| Quem pode emitir | Quem valida (segredo compartilhado) | Só quem tem a chave privada | Só quem tem a chave privada |
| Chave pública publicável (JWKS) | Não | Sim | Sim |
| Tamanho da assinatura | 32 bytes (43 caracteres) | 256 bytes com RSA 2048 (342 caracteres) | 64 bytes (86 caracteres) |
| Custo de assinar | Mínimo | Alto (operação privada RSA) | Baixo |
| Custo de validar | Mínimo | Baixo | Médio |
| Suporte nas bibliotecas | Universal | Universal | Amplo |
| Material da chave | Texto de 32 bytes ou mais | PEM/PKCS#8 de 2048 bits ou mais | PEM/PKCS#8 P-256 |

**Conclusão**: hoje só a própria API emite e valida o token, então a vantagem principal da
assimétrica (validar sem poder emitir) não tem a quem servir. A recomendação é manter o HS256 com
rotação (este REQ) e adotar **ES256** com publicação de JWKS quando um segundo serviço precisar
validar tokens, como um gateway, o Worker ou um IdP (fora do escopo). O ES256 ganha do RS256 pelo
token menor e pela assinatura barata. O desenho deste REQ (conjunto de chaves com `kid` e uma ativa)
serve à migração sem mudar o procedimento: a chave ES256 entra como aceita, vira ativa e a HS256 é
retirada depois da validade máxima.

## R-09 — Estudo: origem das chaves fora do appsettings

| Opção | Encaixe no projeto | Rotação | Dependência |
|-------|--------------------|---------|-------------|
| Variáveis de ambiente (atual) | Já funciona; o compose e o CI passam assim | Reinício por instância | Nenhuma |
| Arquivos de segredo do Docker/Kubernetes (key-per-file) | `Jwt__Chaves__0__Valor` vira um arquivo montado; não fica no `docker inspect` | Reinício (ou recarga, com `reloadOnChange`) | `Microsoft.Extensions.Configuration.KeyPerFile` |
| Azure Key Vault | Provedor de configuração; o valor nunca passa pelo ambiente | Recarga periódica do provedor | `Azure.Extensions.AspNetCore.Configuration.Secrets` e identidade gerenciada |
| AWS Secrets Manager / HashiCorp Vault | Provedores de configuração da comunidade ou agente injetando arquivo | Depende do agente | Pacote de terceiros ou agente |

**Conclusão**: como as chaves entram pelo `IConfiguration`, qualquer origem acima funciona sem
mudar o modelo deste REQ. A recomendação é, quando houver ambiente de produção definido, usar o
cofre da nuvem escolhida pelo provedor de configuração oficial, ou arquivos de segredo montados se
for Docker/Kubernetes sem nuvem. Ambos exigem dependência nova e justificativa no plano
(constituição), por isso ficam para um requisito futuro. A recarga sem reinício também fica fora:
mudar a chave ativa em quente exige que o `ConjuntoChavesJwt` observe as opções
(`IOptionsMonitor`), o que este REQ não faz.

## R-10 — Geração de chave nova

**Decision**: o procedimento documenta comandos que usam gerador criptográfico e produzem texto de
64 caracteres (48 bytes aleatórios em Base64):

- Bash: `openssl rand -base64 48`
- PowerShell 7: `[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(48))`

**Rationale**: o resultado é ASCII (64 bytes UTF-8, o dobro do mínimo) e cabe em variável de
ambiente sem escape. `Get-Random` e `$RANDOM` não são criptográficos e ficam desaconselhados.
