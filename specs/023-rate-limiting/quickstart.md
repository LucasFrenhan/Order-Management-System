# Quickstart: validar o limite de requisições (REQ-23)

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/limite-requisicoes.md](./contracts/limite-requisicoes.md)

## Pré-requisitos

- .NET 10 SDK e Docker.

## 1. Testes automatizados

```bash
dotnet build
dotnet test --filter "FullyQualifiedName~LimiteRequisicoes"
dotnet test
dotnet format --verify-no-changes
```

Os testes de integração cobrem o estado compartilhado com duas instâncias da API sobre o mesmo banco.

## 2. Ambiente local

```bash
docker compose up -d --wait --build
```

A API usa os limites padrão, com uma instância. Em PowerShell, use `curl.exe`.

## 3. Roteiro manual

### 3.1 Limite por origem + e-mail

Faça 11 tentativas com o mesmo e-mail, sem conta:

```bash
for i in $(seq 1 11); do
  curl -s -o /dev/null -w "%{http_code} " -X POST http://localhost:5230/api/v1/auth/login \
    -H 'Content-Type: application/json' -d '{"email":"ninguem@exemplo.com.br","senha":"Errada2026"}'
done
```

**Esperado**: dez `401` e um `429`. A 12ª, com `curl -i`, mostra o `Retry-After` e o `codigo`
`LIMITE_REQUISICOES_EXCEDIDO`. Depois de esperar esse tempo, a tentativa volta a responder `401`.

### 3.2 Outro e-mail da mesma origem

Com o limite anterior esgotado, faça login com `admin@oms.local` / `AdminDev2026`.

**Esperado**: `200`. As tentativas barradas do item 3.1 não consumiram o limite por origem.

### 3.3 Limite do registro

Faça 21 registros com e-mails diferentes e válidos. **Esperado**: vinte `201` e um `429`, sem a última conta.

### 3.4 Estado compartilhado, na tabela

```bash
docker compose exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" \
  -d PedidoFulfillment -Q "SELECT TOP 5 CONVERT(varchar(12), Chave, 2), Contador, FimJanela, BloqueioRegistrado FROM ContagensLimite"
```

**Esperado**: as chaves em hexadecimal, sem e-mail nem endereço legíveis.

### 3.5 Duas instâncias

Suba uma segunda API pelo SDK, com o mesmo banco, em outra porta. Esgote o limite pela porta 5230 e tente o
mesmo e-mail pela outra. **Esperado**: `429`, com o `Retry-After` até 1 s diferente do outro.

### 3.6 Armazenamento indisponível

Pare o banco (`docker compose stop sqlserver`) e faça login.

**Esperado**: o login responde `500`, porque depende do banco, e não `429`. O log traz um 3008 por pausa.
Volte o banco com `docker compose start sqlserver`.

## 4. Verificação sob carga (R-01)

Com o limite padrão:

1. Meça o p95 de `POST /api/v1/pedidos`, com 4 clientes simultâneos, por 1 minuto, sem a inundação.
2. Repita a medição com 32 clientes fazendo login, sem parar, contra um mesmo e-mail e da mesma origem.
3. Durante a inundação, leia duas vezes, com 30 s de intervalo, quantas vezes o lote rodou no banco:

   ```sql
   SELECT SUM(s.execution_count) FROM sys.dm_exec_query_stats s
   CROSS APPLY sys.dm_exec_sql_text(s.sql_handle) t WHERE t.text LIKE '%ContagensLimite%UPDLOCK%';
   ```

**Esperado**:

- p95 da criação de pedidos com a inundação até **20% acima** do p95 sem ela;
- contagem do lote praticamente parada entre as duas leituras: as tentativas são barradas pelo bloqueio local.

Se não atender, registre no PR e reveja a [decisão R-01](./research.md#r-01-armazenamento-compartilhado-sql-server-ou-redis).

## 5. Log e métrica

- `docker compose logs api | grep 3007`: um log por contagem por janela, com o e-mail mascarado (`n***@exemplo.com.br`).
- No Aspire Dashboard: `oms.limite_requisicoes.recusadas`, com uma contagem por recusa.
