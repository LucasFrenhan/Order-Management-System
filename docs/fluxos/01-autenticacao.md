# Fluxo 1: autenticação

**Rotas**: `POST /api/v1/auth/register` e `POST /api/v1/auth/login`, públicas.
**Origem**: REQ-01 ([contrato](../../specs/001-contratos-api/contracts/openapi-v1.yaml)) e
REQ-03 ([spec](../../specs/003-autenticacao-identity/spec.md),
[token](../../specs/003-autenticacao-identity/contracts/token-acesso.md),
[configuração](../../specs/003-autenticacao-identity/contracts/configuracao.md)).

## Perfis

| Perfil | Como surge | O que pode |
|--------|------------|------------|
| `Cliente` | Registro pela API | Criar pedidos, consultar os próprios e cancelar o próprio pedido enquanto ele está `Pendente` |
| `Admin` | Usuário inicial, por configuração | Consultar e operar qualquer pedido, inclusive o despacho |
| `OperadorLogistica` | Usuário inicial, por configuração | O mesmo que o Admin nas rotas de pedido |

Não existe rota para promover um usuário. Os usuários iniciais de desenvolvimento são
`admin@oms.local` / `AdminDev2026` e `operador@oms.local` / `OperadorDev2026`.

## Caminho feliz

### Registro

```http
POST /api/v1/auth/register
Content-Type: application/json

{ "nome": "Maria Souza", "email": "maria@exemplo.com.br", "senha": "Segura2026" }
```

```http
HTTP/1.1 201 Created

{ "id": "0199f1a2-…", "nome": "Maria Souza", "email": "maria@exemplo.com.br", "perfis": ["Cliente"] }
```

- A conta nasce sempre com o perfil `Cliente`.
- O nome e o e-mail são gravados sem os espaços das pontas. Fora isso, o e-mail é guardado
  como foi digitado.
- A senha é guardada só como hash com sal, e nunca aparece em respostas nem em logs.

### Login

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "maria@exemplo.com.br", "senha": "Segura2026" }
```

```http
HTTP/1.1 200 OK

{ "tokenAcesso": "eyJhbGciOiJIUzI1NiIs…", "tipoToken": "Bearer", "expiraEm": "2026-10-24T15:00:00Z", "perfis": ["Cliente"] }
```

- O token é um JWT `HS256` com `sub` (o id do usuário), um `role` por perfil, emissor,
  audiência e validade de 60 minutos.
- O token não leva nome, e-mail nem outro dado pessoal.
- As rotas protegidas recebem o token no cabeçalho `Authorization: Bearer <token>`.

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Nome com menos de 2 ou mais de 100 caracteres, sem contar os espaços das pontas | 400, `erros.nome`: "O nome deve ter entre 2 e 100 caracteres, sem contar espaços nas pontas." | `"nome": " A "` | Regra do contrato v1 |
| E-mail ausente, mal formado ou com mais de 256 caracteres | 400, `erros.email` | `"email": "maria"` | Regra do contrato v1 |
| Senha fora de 8 a 128 caracteres, ou sem maiúscula, minúscula e dígito | 400, `erros.senha`: "A senha deve ter ao menos uma letra maiúscula, uma letra minúscula e um dígito." | `"senha": "segura2026"` | As regras são exatamente as do contrato: nenhuma regra extra pode recusar uma senha que o contrato aceita (FR-005) |
| Corpo ausente ou JSON inválido | 400: "O corpo da requisição está ausente ou não é um JSON válido." | `{ "nome": ` | Erro de formato antes de qualquer regra |
| E-mail já cadastrado, sem diferenciar maiúsculas | 409 `USUARIO_EMAIL_JA_CADASTRADO`: "Já existe uma conta com este e-mail." | `MARIA@exemplo.com.br` depois de `maria@exemplo.com.br` | O e-mail identifica a conta (FR-003) |
| Dois registros simultâneos com o mesmo e-mail | Um recebe 201 e o outro 409 `USUARIO_EMAIL_JA_CADASTRADO` | Duplo clique no botão de cadastro | O índice único decide a corrida; nunca há duas contas com o mesmo e-mail |
| E-mail com espaços nas pontas | Aceito e aparado: é a mesma conta | `" maria@exemplo.com.br "` | Evita contas duplicadas por espaço copiado |
| E-mail inexistente, ou senha errada | 401 "Credenciais inválidas." / "E-mail ou senha incorretos." nos dois casos | — | A resposta igual impede descobrir quais e-mails têm conta (FR-008) |
| 5 erros de senha seguidos | Conta bloqueada por 15 minutos; o login responde o mesmo 401, até com a senha certa; log 3004 | Ataque de força bruta | Limita tentativas sem revelar o bloqueio. Um login certo zera a contagem (FR-009) |
| Senha com espaços | Aceita como foi digitada; o login exige a senha idêntica | `" Segura2026 "` | A senha não é alterada |
| Token sem `Bearer`, mal formado, expirado ou assinado com outra chave, emissor ou audiência | 401 "Não autenticado." / "Informe um token de acesso válido." e o cabeçalho `WWW-Authenticate: Bearer` | Token de outro ambiente | Só tokens emitidos por esta API, dentro da validade (tolerância de 30 s) |
| Token válido, mas sem o perfil exigido pela rota | 403 "Acesso negado." | Cliente chamando a rota de rastreio | Autorização por perfil (REQ-01) |
| Perfil alterado depois do login | O token mantém os perfis antigos até expirar | — | O token é autossuficiente; o perfil novo vale no próximo login |
| Conta sem nenhum perfil (dado inconsistente) | Login funciona, mas toda rota protegida responde 403 | — | Nenhum acesso sem perfil |

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| Banco fora do ar na partida, com `Banco:PrepararNaInicializacao=true` | A API não sobe. A mensagem nomeia o banco (`ConnectionStrings:SqlServer`) sem expor dados de conexão | Sem banco não há perfis nem usuários iniciais (FR-018) |
| Migration pendente fora de Development e de Testes | A API não sobe e pede a aplicação das migrations pelo comando documentado | A API não altera o esquema em produção (FR-017) |
| Banco fora do ar durante o registro ou o login | 500 "Erro interno.", sem detalhes; nenhum registro fica pela metade | — |
| Configuração de usuário inicial inválida | A API não sobe; a mensagem cita a chave (por exemplo `UsuariosIniciais:0:Senha`), sem mostrar o valor | Falha cedo, sem vazar segredos |

## Observabilidade

Logs 3001 (registro), 3002 (login), 3003 (login recusado) e 3004 (bloqueio). A pessoa é
identificada pelo id e pelo e-mail mascarado (`m***@exemplo.com.br`), nunca pela senha,
pelo hash, pelo token ou pelo e-mail completo (FR-019).

## Limitações

- Não há renovação de token (refresh), troca de senha nem recuperação de conta.
- Não há rota de gestão de usuários ou de perfis.
