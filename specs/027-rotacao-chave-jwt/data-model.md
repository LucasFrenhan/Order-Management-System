# Data Model: Rotação da chave de assinatura do JWT (REQ-27)

Nada é persistido: as chaves vêm da configuração e vivem em memória durante a execução. Não há
migration.

## OpcoesJwt (seção `Jwt`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `Emissor` | texto | Obrigatório (como hoje) |
| `Audiencia` | texto | Obrigatório (como hoje) |
| `DuracaoTokenMinutos` | inteiro | Padrão 15 (como hoje) |
| `Chaves` | lista de `ChaveJwtConfigurada` | De 1 a 3 itens; identificadores únicos |
| `ChaveAtiva` | texto | Obrigatório; igual ao `Identificador` de um item de `Chaves` |
| `ChaveAssinatura` | texto | Mantido só para detectar a configuração antiga: preenchido, a partida falha |

## ChaveJwtConfigurada (item de `Jwt:Chaves`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `Identificador` | texto | De 1 a 64 caracteres em `[A-Za-z0-9._-]`; único na lista (comparação ordinal); público |
| `Valor` | texto | No mínimo 32 bytes em UTF-8; secreto, nunca exibido em mensagem ou log |

## ConjuntoChavesJwt (em memória, singleton)

Montado uma vez a partir de `OpcoesJwt` já validadas.

| Membro | Conteúdo |
|--------|----------|
| `Ativa` | A chave de assinatura (`SymmetricSecurityKey` com `KeyId`) usada na emissão |
| `Todas` | As chaves aceitas na validação, a ativa inclusive |
| `ValorAtiva` | Bytes da chave ativa, para a derivação da chave do cursor |
| `Contem(identificador)` | Se o identificador está no conjunto (comparação ordinal) |

## OpcoesListagens (seção `Listagens`)

| Campo | Tipo | Regra |
|-------|------|-------|
| `ChaveCursor` | texto | Opcional; se preenchido, no mínimo 32 bytes em UTF-8; secreto |

## Situações de uma chave

```text
(nova) --acrescentar--> aceita --ativar--> ativa --trocar a ativa--> aceita --retirar--> (fora da configuração)
```

- **Aceita**: está em `Jwt:Chaves` e não é a `Jwt:ChaveAtiva`; valida tokens, não emite.
- **Ativa**: está em `Jwt:Chaves` e é a `Jwt:ChaveAtiva`; emite e valida.
- **Retirada**: não está mais em `Jwt:Chaves`; tokens assinados com ela recebem 401 e o log 3027.

A situação muda só com a troca da configuração e o reinício da instância.
