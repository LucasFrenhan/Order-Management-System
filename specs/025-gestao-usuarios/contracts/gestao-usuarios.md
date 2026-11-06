# Contrato: gestão de usuários e perfis (REQ-25)

**Spec**: [spec.md](../spec.md) | **Data model**: [data-model.md](../data-model.md) | **Decisões**: [research.md](../research.md)

Acréscimos ao `specs/001-contratos-api/contracts/openapi-v1.yaml`: o caminho `/api/v1/admin/usuarios` e seus subcaminhos, os schemas e os quatro códigos de erro novos. Nada do que existe muda (Princípio II).

## Rotas

Todas exigem Admin ativo: 401 sem token ou com token inválido; 403 sem o perfil Admin, ou para quem deixou de ser Admin ativo, mesmo com o token válido. Ficam fora do limite de requisições.

| Rota | Sucesso | Outras respostas |
|------|---------|------------------|
| `GET /api/v1/admin/usuarios?perfil=&situacao=&busca=&tamanho=&cursor=` | 200 `PaginaUsuariosResponseDTO` | 400 |
| `GET /api/v1/admin/usuarios/{id}` | 200 `UsuarioGestaoResponseDTO` | 400 (id não UUID), 404 |
| `POST /api/v1/admin/usuarios` | 201 + `Location`, `UsuarioGestaoResponseDTO` | 400, 409 `USUARIO_EMAIL_JA_CADASTRADO` |
| `PUT /api/v1/admin/usuarios/{id}/perfis/{perfil}` | 200 `UsuarioGestaoResponseDTO` (idempotente) | 400, 404, 409 `USUARIO_PERFIS_INCOMPATIVEIS` |
| `DELETE /api/v1/admin/usuarios/{id}/perfis/{perfil}` | 200 `UsuarioGestaoResponseDTO` (idempotente) | 400, 404, 409 `USUARIO_ULTIMO_ADMIN`, `USUARIO_PROPRIA_CONTA`, `USUARIO_ULTIMO_PERFIL` |
| `POST /api/v1/admin/usuarios/{id}/desativacao` | 200 `UsuarioGestaoResponseDTO` (idempotente) | 400, 404, 409 `USUARIO_ULTIMO_ADMIN`, `USUARIO_PROPRIA_CONTA` |
| `POST /api/v1/admin/usuarios/{id}/reativacao` | 200 `UsuarioGestaoResponseDTO` (idempotente) | 400, 404 |

**Rota existente**: `POST /api/v1/auth/redefinicao-senha` passa a aceitar também o token de convite. O corpo e as respostas não mudam; só a descrição ganha essa frase ([R-05](../research.md#r-05-o-convite-usa-a-rota-existente-de-redefinição-fr-008-fr-023)).

## Exemplos

### Criação

```http
POST /api/v1/admin/usuarios
Authorization: Bearer <token do Admin>
Content-Type: application/json

{ "nome": "Ana Lima", "email": "ana@exemplo.com.br", "perfil": "OperadorLogistica" }
```

```http
HTTP/1.1 201 Created
Location: /api/v1/admin/usuarios/0199f1a2-0000-7000-8000-000000000001

{ "id": "0199f1a2-0000-7000-8000-000000000001", "nome": "Ana Lima", "email": "ana@exemplo.com.br",
  "perfis": ["OperadorLogistica"], "situacao": "Ativa", "emailConfirmado": false, "convitePendente": true }
```

### Definir a senha pelo convite

```http
POST /api/v1/auth/redefinicao-senha
Content-Type: application/json

{ "token": "<token do e-mail de convite>", "novaSenha": "Segura2026" }
```

`204 No Content`. Depois disso, o login funciona, e a consulta mostra `emailConfirmado: true` e `convitePendente: false`.

### Listagem

```http
GET /api/v1/admin/usuarios?perfil=Admin&situacao=Ativa&busca=lima&tamanho=2
Authorization: Bearer <token do Admin>
```

```http
HTTP/1.1 200 OK

{ "itens": [ { "id": "…", "nome": "Ana Lima", "email": "ana@exemplo.com.br", "perfis": ["Admin"],
               "situacao": "Ativa", "emailConfirmado": true, "convitePendente": false } ],
  "tamanho": 2, "proximo": null, "anterior": null }
```

### Último Admin

```http
HTTP/1.1 409 Conflict

{ "title": "Último Admin.", "detail": "O sistema precisa de ao menos um Admin ativo.", "codigo": "USUARIO_ULTIMO_ADMIN" }
```

## Schemas (resumo)

- `CriarUsuarioInternoRequestDTO`: obrigatórios `nome`, `email` e `perfil` (`Admin` ou `OperadorLogistica`). Nome e e-mail seguem as regras e as mensagens do registro. `Cliente` ou um valor desconhecido em `perfil` é 400. Sem campo de senha.
- `UsuarioGestaoResponseDTO`: obrigatórios `id`, `nome`, `email`, `perfis`, `situacao` (`Ativa` ou `Desativada`), `emailConfirmado` e `convitePendente`. Nunca traz senha, hash, carimbo de segurança ou token.
- `PaginaUsuariosResponseDTO`: `itens`, `tamanho`, `proximo` e `anterior`, os dois últimos podendo ser nulos, como em produtos.

## Parâmetros da listagem

| Parâmetro | Regra | Erro |
|-----------|-------|------|
| `perfil` | `Admin`, `OperadorLogistica` ou `Cliente` | 400, `erros.perfil` |
| `situacao` | `Ativa` ou `Desativada` | 400, `erros.situacao` |
| `busca` | 2 a 100 caracteres, sem os espaços das pontas; trecho do nome ou do e-mail, sem diferenciar maiúsculas nem acentos | 400, `erros.busca` |
| `tamanho` | 1 a 100, padrão 20 | 400, `erros.tamanho` |
| `cursor` | Opaco. Só vale para quem o recebeu e para os mesmos filtros | 400, `erros.cursor` |

Os filtros se combinam por "e". A ordem é por nome e, no empate, por id.

## Códigos de erro novos

| Código | Status | `title` / `detail` | Quando |
|--------|--------|--------------------|--------|
| `USUARIO_ULTIMO_ADMIN` | 409 | "Último Admin." / "O sistema precisa de ao menos um Admin ativo." | O único Admin ativo tenta retirar o próprio perfil Admin ou desativar a própria conta |
| `USUARIO_PROPRIA_CONTA` | 409 | "Operação sobre a própria conta." / "Peça a outro Admin para fazer esta alteração." | Um Admin tenta o mesmo, com outro Admin ativo |
| `USUARIO_ULTIMO_PERFIL` | 409 | "Último perfil da conta." / "A conta precisa manter ao menos um perfil." | A retirada deixaria a conta sem perfil |
| `USUARIO_PERFIS_INCOMPATIVEIS` | 409 | "Perfis incompatíveis." / "O perfil Cliente não se combina com perfis internos." | A atribuição misturaria os grupos |

A ordem em que essas regras são avaliadas está em [R-04](../research.md#r-04-ordem-das-regras-e-onde-elas-ficam-fr-003-fr-011-fr-013).

## Respostas que não mudam

Uma conta desativada não ganha resposta nova em nenhuma rota pública:

| Rota | Resposta |
|------|----------|
| Login, com a senha certa ou errada | 401 "Credenciais inválidas." |
| Renovação | 401 "Sessão inválida." |
| Recuperação e reenvio | 202, sem e-mail |
| Redefinição e confirmação | 400 `TOKEN_INVALIDO` |
| Registro com o mesmo e-mail | 409 `USUARIO_EMAIL_JA_CADASTRADO` |
| Troca de senha, com token ainda válido | 400, `erros.senhaAtual` |

## Configuração

Seção `Email`: `ValidadeConviteHoras`, padrão 72, de 1 a 720. Fora da faixa, o Worker não sobe, e a mensagem cita a chave.

## E-mail de convite

Assunto e corpo em português, com o token e, se `Email:UrlBaseAplicacao` estiver configurada, o link para a aplicação. Diz a validade e que, vencido o prazo, basta usar a recuperação de senha. Não traz a senha nem o nome de quem criou a conta.
