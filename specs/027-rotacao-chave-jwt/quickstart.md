# Quickstart: validar a rotação da chave do JWT (REQ-27)

**Spec**: [spec.md](./spec.md) | **Contratos**: [configuração](./contracts/configuracao.md) e
[token de acesso](./contracts/token-acesso.md)

## Pré-requisitos

- .NET 10 SDK e Docker.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~ChavesJwt|FullyQualifiedName~RotacaoChave|FullyQualifiedName~EmissorToken|FullyQualifiedName~ChaveCursor|FullyQualifiedName~ValidadorOpcoesJwt"
dotnet test
dotnet format --verify-no-changes
```

**Esperado**: tudo verde. Os testes cobrem a lista do FR-013.

## 2. Ambiente local

Suba só a infraestrutura e rode a API pelo `dotnet run`, para trocar as chaves por variável de
ambiente a cada etapa:

```bash
docker compose up -d --wait
```

A API usa a chave `desenvolvimento-local` do `appsettings.Development.json`. Em PowerShell, use
`curl.exe` e `$env:NOME = "valor"` no lugar de `export`. Gere as chaves novas como em
[research R-10](./research.md#r-10--geração-de-chave-nova).

## 3. Roteiro manual

### 3.1 Partida e emissão

```bash
dotnet run --project src/PedidoFulfillment.API
```

1. **Esperado no console**: o log 3026 `Chaves do JWT: ativa desenvolvimento-local; aceitas -.`
2. Faça login e decodifique o cabeçalho do `tokenAcesso` (a primeira parte, em Base64url).
   **Esperado**: `"kid":"desenvolvimento-local"`. Guarde o token como **T1** e o `tokenRenovacao`.

### 3.2 Etapa 1: acrescentar a chave nova

Pare a API e suba com a chave B como aceita:

```bash
export Jwt__Chaves__1__Identificador=local-b
export Jwt__Chaves__1__Valor="$(openssl rand -base64 48)"
dotnet run --project src/PedidoFulfillment.API
```

**Esperado**: log 3026 com `ativa desenvolvimento-local; aceitas local-b`; T1 continua com 200 em
`GET /api/v1/pedidos`; um login novo ainda sai com `kid` `desenvolvimento-local`.

### 3.3 Etapa 2: ativar a chave nova

```bash
export Jwt__ChaveAtiva=local-b
```

Reinicie. **Esperado**: log 3026 com `ativa local-b; aceitas desenvolvimento-local`; T1 continua
com 200; um login novo sai com `kid` `local-b` (guarde como **T2**).

### 3.4 Etapa 3: retirar a chave antiga

Depois da validade máxima de T1 (15 min mais 30 s), ou já, para ver a recusa, deixe só a chave B
na posição 0 (as variáveis prevalecem sobre o arquivo de desenvolvimento):

```bash
export Jwt__Chaves__0__Identificador=local-b
export Jwt__Chaves__0__Valor="<o mesmo valor de local-b>"
unset Jwt__Chaves__1__Identificador Jwt__Chaves__1__Valor
```

Reinicie. **Esperado**:

- T2 com 200.
- T1, se ainda não expirou, com 401 "Não autenticado." e o log 3027
  `Token recusado: chave desenvolvimento-local fora da configuração.`
- `POST /api/v1/auth/renovacao` com o `tokenRenovacao` da 3.1: 200, com um token de `kid` `local-b`
  (retirada de emergência, SC-003).

### 3.5 Token sem `kid`

Monte um token sem `kid` assinado com a chave ativa (por exemplo, com o `GeradorTokenTeste` e
`identificadorChave: null`) e envie a uma rota protegida.

**Esperado**: 401 e o log 3027 com `(ausente)`.

### 3.6 Configuração inválida

Suba a API com cada caso abaixo e confira a mensagem do
[contrato](./contracts/configuracao.md#mensagens-de-falha-na-partida), sem nenhum valor de chave:

- `Jwt__ChaveAssinatura` preenchida (configuração antiga).
- Uma quarta chave (`Jwt__Chaves__3__...`).
- `Jwt__Chaves__1__Identificador` igual ao da posição 0.
- `Jwt__Chaves__1__Valor` com menos de 32 caracteres.
- `Jwt__ChaveAtiva` com um identificador que não está na lista.
- `Listagens__ChaveCursor` com menos de 32 caracteres.

### 3.7 Cursor com chave própria

Suba com `Listagens__ChaveCursor` (gerada como na R-10), pegue um `proximo` de
`GET /api/v1/pedidos?tamanho=1` e repita as etapas 3.2 a 3.4.

**Esperado**: o mesmo cursor continua aceito depois de cada etapa (SC-006). Sem
`Listagens__ChaveCursor`, o cursor recebe 400 depois da etapa 2.

## 4. Limpeza

```bash
docker compose down
```

Remova as variáveis de ambiente criadas no roteiro.
