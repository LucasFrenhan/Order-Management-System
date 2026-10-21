# Quickstart: validação do REQ-03

**Spec**: [spec.md](./spec.md) | **Token**: [contracts/token-acesso.md](./contracts/token-acesso.md) |
**Configuração**: [contracts/configuracao.md](./contracts/configuracao.md)

## Pré-requisitos

- SDK do .NET 10 e Docker com Compose v2 (REQ-02).
- Ferramentas locais: `dotnet tool restore` (instala o `dotnet-ef` do manifesto).

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde. Com o Docker no ar, os testes com banco real rodam; sem ele,
aparecem como ignorados ("Docker indisponível"). O teste de conformidade do contrato v1
continua passando (FR-015).

## 2. API com banco novo (Development)

```powershell
docker compose down -v; docker compose up -d --wait
$env:ASPNETCORE_ENVIRONMENT = "Development"
dotnet run --project src/PedidoFulfillment.API --launch-profile http
```

**Esperado**: a API cria o banco `PedidoFulfillment`, aplica a migration
`InicialIdentidade`, cria os perfis `Cliente`, `Admin` e `OperadorLogistica` e os
usuários iniciais de desenvolvimento (FR-011, FR-012, FR-017). Reiniciar a API não duplica
nada.

## 3. Registro e login (User Stories 1 e 2)

Endereço base: `http://localhost:5230/api/v1/auth`.

| Requisição | Esperado |
|------------|----------|
| `POST /register` `{"nome":"  Maria Silva ","email":"maria@exemplo.com.br","senha":"SenhaForte1"}` | 201, `nome` `Maria Silva`, `perfis` `["Cliente"]`, sem senha |
| O mesmo registro com `MARIA@exemplo.com.br` | 409, `codigo` `USUARIO_EMAIL_JA_CADASTRADO` |
| `POST /login` `{"email":"Maria@Exemplo.com.br","senha":"SenhaForte1"}` | 200, `tipoToken` `Bearer`, `expiraEm` = agora + 60 min, `perfis` `["Cliente"]` |
| `POST /login` com senha errada | 401 "Credenciais inválidas." |
| `POST /login` com e-mail inexistente | 401 idêntico ao anterior (FR-008) |
| 5 logins seguidos com senha errada e depois a senha correta | 401 genérico também com a senha correta (bloqueio de 15 min, FR-009) |

## 4. Perfis com tokens reais (User Story 3)

Faça login com `admin@oms.local` / `AdminDev2026` e com a conta de Cliente da §3.

| Requisição | Token | Esperado |
|------------|-------|----------|
| `POST /api/v1/pedidos` (com `X-Idempotency-Key`) | Cliente | passa pela autorização (hoje 501, até o REQ-05) |
| `POST /api/v1/pedidos` | Admin | 403 |
| `PATCH /api/v1/pedidos/{id}/status` | Cliente | passa pela autorização (o Cliente pode cancelar o próprio pedido Pendente; as transições são regra do REQ-05) |
| `PATCH /api/v1/pedidos/{id}/status` | Admin | passa pela autorização (hoje 501) |
| qualquer rota protegida | sem token | 401 |

## 5. Logs de segurança

No console da API, depois da §3:

**Esperado**: eventos `UsuarioRegistrado`, `LoginRealizado`, `LoginRecusado` e
`ContaBloqueada`, com `m***@exemplo.com.br` e o id do usuário; nenhuma senha, nenhum token
e nenhum e-mail completo (FR-019, SC-003).

## 6. Fora de Development

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
$env:Jwt__ChaveAssinatura = "chave-local-de-teste-com-mais-de-32-bytes"
$env:ConnectionStrings__SqlServer = "Server=localhost,1433;Database=PedidoFulfillmentProducao;User Id=sa;Password=OmsDev_2026!Local;Encrypt=True;TrustServerCertificate=True"
# + variáveis RabbitMQ__* do REQ-02
dotnet run --project src/PedidoFulfillment.API
```

**Esperado**:

- Banco sem migrations: a API não inicia e diz que há migrations pendentes (FR-017).
- Depois de `dotnet ef database update --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API`
  (com as mesmas variáveis), a API inicia.
- `docker compose stop sqlserver` e iniciar a API: ela não inicia, e a mensagem nomeia o
  banco sem mostrar a string de conexão (FR-018).
- Com `$env:Banco__PrepararNaInicializacao = "false"`, a API inicia mesmo com o banco
  parado, como no REQ-02.
