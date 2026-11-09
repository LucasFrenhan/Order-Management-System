# Fluxo 1: autenticação

**Rotas**: em `/api/v1/auth`, as públicas `register`, `login`, `renovacao`, `logout`, `recuperacao-senha`,
`redefinicao-senha`, `confirmacao-email` e `confirmacao-email/reenvio`; e `troca-senha`, que exige token. A gestão pelo
Admin fica em `/api/v1/admin/usuarios`.
**Origem**: REQ-01 ([contrato](../../specs/001-contratos-api/contracts/openapi-v1.yaml)),
REQ-03 ([spec](../../specs/003-autenticacao-identity/spec.md),
[token](../../specs/003-autenticacao-identity/contracts/token-acesso.md),
[configuração](../../specs/003-autenticacao-identity/contracts/configuracao.md)),
REQ-23 ([spec](../../specs/023-rate-limiting/spec.md),
[contrato do limite](../../specs/023-rate-limiting/contracts/limite-requisicoes.md)) e
REQ-24 ([spec](../../specs/024-ciclo-autenticacao/spec.md),
[contrato](../../specs/024-ciclo-autenticacao/contracts/autenticacao.md)) e
REQ-25 ([spec](../../specs/025-gestao-usuarios/spec.md),
[contrato](../../specs/025-gestao-usuarios/contracts/gestao-usuarios.md)) e
REQ-27 ([spec](../../specs/027-rotacao-chave-jwt/spec.md),
[configuração](../../specs/027-rotacao-chave-jwt/contracts/configuracao.md),
[token](../../specs/027-rotacao-chave-jwt/contracts/token-acesso.md)). O envio dos e-mails está no
[fluxo 16](16-envio-de-email.md).

## Perfis

| Perfil | Como surge | O que pode |
|--------|------------|------------|
| `Cliente` | Registro pela API | Criar pedidos, consultar os próprios e cancelar o próprio pedido enquanto ele está `Pendente` |
| `Admin` | Usuário inicial, por configuração, ou [criação pelo Admin](#gestão-de-usuários) | Consultar e operar qualquer pedido, inclusive o despacho; pedir e consultar o reenvio da DLQ (fluxo 11); gerir usuários e perfis |
| `OperadorLogistica` | Usuário inicial, por configuração, ou [criação pelo Admin](#gestão-de-usuários) | O mesmo que o Admin nas rotas de pedido; sem acesso às rotas administrativas (403) |

`Admin` e `OperadorLogistica` podem se combinar, mas não com `Cliente`: uma conta do registro público nunca vira interna,
e quem é cliente e também da operação usa duas contas, com e-mails diferentes. O primeiro Admin de um ambiente vem da carga
inicial. Os usuários iniciais de desenvolvimento são `admin@oms.local` / `AdminDev2026` e
`operador@oms.local` / `OperadorDev2026`.

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
- O registro pede um e-mail de confirmação na própria transação, sem mudar a resposta nem atrasá-la.

### Login

```http
POST /api/v1/auth/login
Content-Type: application/json

{ "email": "maria@exemplo.com.br", "senha": "Segura2026" }
```

```http
HTTP/1.1 200 OK

{
  "tokenAcesso": "eyJhbGciOiJIUzI1NiIs…", "tipoToken": "Bearer", "expiraEm": "2026-10-24T15:00:00Z", "perfis": ["Cliente"],
  "tokenRenovacao": "8sJq0rS1aVb3nXyZ…", "tokenRenovacaoExpiraEm": "2026-11-07T14:45:00Z"
}
```

- O token de acesso é um JWT `HS256` com `sub` (o id do usuário), um `role` por perfil, emissor,
  audiência e validade de 15 minutos por padrão (`Jwt:DuracaoTokenMinutos`). Leia o `expiraEm`.
- O cabeçalho do token traz o `kid`, o identificador da chave que o assinou (REQ-27).
- O token não leva nome, e-mail nem outro dado pessoal.
- As rotas protegidas recebem o token no cabeçalho `Authorization: Bearer <token>`.
- O login não exige o e-mail confirmado (REQ-24, FR-020).

### Chaves de assinatura

A API aceita de 1 a 3 chaves (`Jwt:Chaves`), cada uma com um identificador, e emite com a chave ativa
(`Jwt:ChaveAtiva`). Um token é aceito quando o `kid` dele é de uma chave configurada, ativa ou não, e a assinatura
confere. Assim a chave troca sem derrubar ninguém: acrescentar a nova, ativá-la e retirar a antiga depois da validade
máxima do token ([procedimento](../rotacao-chave-jwt.md)).

- O token sem `kid`, com `kid` de uma chave retirada ou com `kid` desconhecido recebe o mesmo 401 de um token inválido,
  com o log 3027. A comparação do `kid` diferencia maiúsculas de minúsculas.
- A renovação da sessão não depende da chave do JWT: depois de uma retirada de emergência, o cliente recebe o 401, renova
  e passa a usar um token da chave ativa, sem digitar a senha.
- Configuração inválida impede a API de subir, com mensagens que citam só o identificador ou a posição na lista
  ([mensagens](../../specs/027-rotacao-chave-jwt/contracts/configuracao.md#mensagens-de-falha-na-partida)). A antiga
  `Jwt:ChaveAssinatura` não é mais aceita.
- Os cursores das listagens (inclusive a de usuários, abaixo) são assinados com `Listagens:ChaveCursor`, quando
  configurada; sem ela, com uma chave derivada da chave ativa do JWT, e a troca da ativa invalida os cursores em uso.

### Renovação

```http
POST /api/v1/auth/renovacao
Content-Type: application/json

{ "tokenRenovacao": "8sJq0rS1aVb3nXyZ…" }
```

A resposta 200 tem o mesmo corpo do login, com um par novo e os perfis atuais. Um login abre uma sessão:

- **Cada token de renovação serve uma só vez**. Vale 14 dias, renovados a cada rotação, e a sessão dura no máximo
  30 dias desde o login, mesmo renovando (seção `Sessoes`).
- **Reutilização encerra a sessão inteira**: apresentar de novo um token já usado responde 401 e invalida também o
  token mais recente dela. Se o token foi roubado, o ladrão perde o acesso.
- **Renove uma vez de cada vez**: de duas renovações simultâneas, só uma é aceita, e a outra conta como reutilização.
- **Conta bloqueada continua renovando**: o bloqueio impede a adivinhação de senha, e um atacante não derruba quem
  já está conectado.
- Os tokens são opacos, de 256 bits, e o banco guarda só o SHA-256 deles.

### Logout

`POST /api/v1/auth/logout`, com `{ "tokenRenovacao": "…" }`, responde 204 em qualquer caso e não exige token de
acesso, para funcionar também com ele expirado. Encerra só a sessão daquele token. O token de acesso em uso continua
valendo até expirar.

### Troca de senha

```http
POST /api/v1/auth/troca-senha
Authorization: Bearer <token>
Content-Type: application/json

{ "senhaAtual": "Segura2026", "novaSenha": "OutraSenha2" }
```

Responde 204 e encerra **todas** as sessões da conta, inclusive a do dispositivo que trocou. Vale para todos os perfis.

### Recuperação de senha

1. `POST /api/v1/auth/recuperacao-senha`, com `{ "email": "…" }`, responde 202 em qualquer caso. A API só grava a
   solicitação, sem consultar a conta, então a resposta e o tempo não revelam se o e-mail tem conta.
2. O Worker envia um token de uso único, válido por 30 minutos ([fluxo 16](16-envio-de-email.md)). Um pedido novo
   invalida o anterior.
3. `POST /api/v1/auth/redefinicao-senha`, com `{ "token": "…", "novaSenha": "…" }`, responde 204. Só o token: ele
   identifica a conta sozinho, e o link não traz dados pessoais.

A redefinição troca a senha, zera o bloqueio e a contagem de erros, confirma o e-mail, encerra todas as sessões e pede
um e-mail de aviso, tudo em uma transação.

### Confirmação de e-mail

`POST /api/v1/auth/confirmacao-email`, com `{ "token": "…" }`, responde 204. O token vale 24 horas e é de uso único.
`POST /api/v1/auth/confirmacao-email/reenvio`, com `{ "email": "…" }`, responde 202 em qualquer caso, e só uma conta
existente e não confirmada recebe o e-mail. Os usuários iniciais nascem confirmados, e as contas anteriores ao REQ-24
continuam como estão: nada depende da confirmação por enquanto.

## Gestão de usuários

Todas as rotas exigem um **Admin ativo**: ter o perfil no token e, conferido no banco a cada chamada, ainda ter o perfil,
estar ativo e já ter definido a senha. Quem perdeu o Admin ou foi desativado recebe 403 na hora, mesmo com o token válido.
O bloqueio por erros de senha não tira ninguém dessa conta, porque é temporário. As rotas ficam fora do limite de requisições.

| Rota | O que faz | Sucesso |
|------|-----------|---------|
| `GET /api/v1/admin/usuarios?perfil=&situacao=&busca=&tamanho=&cursor=` | Lista por nome, com cursor, de 1 a 100 por página (padrão 20) | 200 |
| `GET /api/v1/admin/usuarios/{id}` | Consulta uma conta | 200 |
| `POST /api/v1/admin/usuarios` | Cria uma conta interna sem senha e pede o convite | 201 + `Location` |
| `PUT /api/v1/admin/usuarios/{id}/perfis/{perfil}` | Atribui um perfil; repetir não muda nada | 200 |
| `DELETE /api/v1/admin/usuarios/{id}/perfis/{perfil}` | Retira um perfil; repetir não muda nada | 200 |
| `POST /api/v1/admin/usuarios/{id}/desativacao` | Desativa a conta; repetir não muda nada | 200 |
| `POST /api/v1/admin/usuarios/{id}/reativacao` | Reativa a conta; repetir não muda nada | 200 |

```http
HTTP/1.1 201 Created
Location: /api/v1/admin/usuarios/0199f1a2-…

{ "id": "0199f1a2-…", "nome": "Ana Lima", "email": "ana@exemplo.com.br", "perfis": ["OperadorLogistica"],
  "situacao": "Ativa", "emailConfirmado": false, "convitePendente": true }
```

- **Nenhuma resposta traz dado de credencial**: nem senha, hash, carimbo de segurança ou token.
- **Busca**: trecho do nome ou do e-mail, de 2 a 100 caracteres, sem diferenciar maiúsculas nem acentos. Lê a tabela inteira.
- **O Admin não edita** o nome nem o e-mail de outra conta, e não há exclusão: a desativação é o único meio de tirar o acesso
  de uma conta com um só perfil.

### Convite

A conta nasce sem senha, e o Admin nunca a conhece. O Worker envia um token de uso único, que vale 72 horas a contar do envio
(`Email:ValidadeConviteHoras`). A pessoa define a senha em `POST /api/v1/auth/redefinicao-senha`, a mesma rota e as mesmas
respostas da recuperação. Isso confirma o e-mail e não gera o e-mail de aviso de senha redefinida.

Enquanto a senha não é definida, a consulta mostra `convitePendente: true`, o login responde o 401 de sempre, sem contar erro
para o bloqueio, e um Admin convidado não conta como Admin ativo. Não há rota para reenviar o convite: vencido, ou descartado
depois de 24 horas sem conseguir enviar ([fluxo 16](16-envio-de-email.md)), basta a recuperação de senha. O primeiro token de
convite ou de recuperação a ser usado invalida o outro.

### Último Admin e ordem das regras

Toda alteração roda uma de cada vez, entre todas as instâncias (lock de aplicação `oms-gestao-usuarios`), e confere de novo quem
chama. Se dois Admins tentam tirar o acesso um do outro ao mesmo tempo, um é aceito, e o outro recebe 403. As regras são
avaliadas nesta ordem:

1. Quem chama deixou de ser Admin ativo: 403.
2. Conta inexistente: 404.
3. A conta já está no estado pedido: 200, sem alterar nem auditar.
4. A operação tiraria o único Admin ativo: 409 `USUARIO_ULTIMO_ADMIN`.
5. Um Admin desativando a própria conta ou retirando o próprio perfil Admin: 409 `USUARIO_PROPRIA_CONTA`.
6. A retirada deixaria a conta sem perfil: 409 `USUARIO_ULTIMO_PERFIL`. Para rebaixar um Admin que só tem esse perfil, atribua
   `OperadorLogistica` e depois retire `Admin`.
7. A atribuição misturaria `Cliente` com perfil interno: 409 `USUARIO_PERFIS_INCOMPATIVEIS`.

### Conta desativada

A desativação é um estado próprio, independente do bloqueio por erros de senha: a redefinição de senha não reativa, e a
reativação não mexe no bloqueio. Em uma transação, ela encerra todas as sessões e apaga os tokens de uso único da conta.
Nenhuma resposta pública revela a situação:

| Rota | Resposta para a conta desativada |
|------|----------------------------------|
| Login, com a senha certa ou errada | O mesmo 401 "Credenciais inválidas.", em tempo equivalente e sem contar erro; o motivo fica só no log 3003 (`ContaDesativada`) |
| Renovação | O mesmo 401 "Sessão inválida." |
| Recuperação e reenvio | O mesmo 202, sem e-mail |
| Redefinição e confirmação | O mesmo 400 `TOKEN_INVALIDO` |
| Troca de senha, com token ainda válido | O mesmo 400 de `senhaAtual`, sem contar erro |
| Registro com o mesmo e-mail | O mesmo 409 `USUARIO_EMAIL_JA_CADASTRADO` |

Os pedidos continuam como estão e seguem o fluxo normal, inclusive o estorno devido. Os webhooks próprios da conta ficam suspensos
([fluxo 13](13-notificacao-de-status.md#entrega-do-webhook)). A reativação devolve o login com a mesma senha e os mesmos perfis, e
encerra de novo as sessões: nenhuma sessão anterior volta a valer. Quem nunca definiu a senha usa a recuperação, porque o convite foi
apagado. A carga inicial nunca altera uma conta existente, então uma conta dela desativada continua assim depois de reiniciar.

## Limite de requisições

O limite é uma regra do sistema, e não de uma instância: quem esgota as tentativas pela API A continua
barrado pela API B, porque as contagens ficam no SQL Server (tabela `ContagensLimite`). A verificação
vem antes de qualquer outra regra da rota e não muda as respostas de quem está dentro do limite.

| Política | Rota | O que forma a contagem | Padrão |
|----------|------|------------------------|:------:|
| `login_origem_email` | Login | Origem + e-mail | 10 por minuto |
| `login_origem` | Login | Origem | 100 por minuto |
| `registro` | Registro | Origem | 20 por hora |
| `recuperacao_origem_email`, `reenvio_origem_email` | Recuperação, reenvio | Origem + e-mail | 3 por hora |
| `recuperacao_origem`, `reenvio_origem` | Recuperação, reenvio | Origem | 20 por hora |
| `redefinicao`, `confirmacao` | Redefinição, confirmação | Origem | 10 por minuto |
| `renovacao`, `logout` | Renovação, logout | Origem | 120 por minuto |

A troca de senha exige token e fica fora do limite; os erros dela contam para o bloqueio de conta.

- **Janela fixa**: começa na primeira requisição da contagem e recomeça ao terminar. Quem insiste durante
  a espera não a prolonga.
- **Dois limites no login, na recuperação e no reenvio**: exceder qualquer um barra a tentativa, e a tentativa barrada por um não consome
  o outro. Assim, quem insiste em um e-mail não trava o login do resto da sua rede.
- **E-mail**: sem diferenciar maiúsculas e sem os espaços das pontas. Corpo inválido, ou sem um e-mail utilizável,
  conta só pela origem e segue respondendo o 400 de sempre.
- **Origem**: IPv4 pelo endereço exato; IPv6 pelo bloco `/64`, porque um cliente costuma controlar o bloco inteiro.
- **Bloqueio de conta**: não muda, e os dois se completam. O limite segura o volume de uma origem, e o bloqueio
  protege a senha contra ataques de muitas origens. A tentativa barrada não avalia a senha nem conta para o bloqueio.

### Configuração

Seção `LimiteRequisicoes`, validada na partida; os limites e as faixas estão no
[contrato](../../specs/023-rate-limiting/contracts/limite-requisicoes.md#configuração). Não existe opção para desligar o limite.

- **`ChaveContagem`**: obrigatória fora de Development (Base64 de 32 bytes) e igual em todas as instâncias. Sem ela,
  a API não sobe. Trocá-la só zera as contagens.
- **`IntermediariosConfiaveis`**: lista de IPs e faixas CIDR que podem informar a origem por `X-Forwarded-For`.

  > **Com balanceador em produção, preencha esta lista.** Sem ela, todos os clientes dividem a contagem do endereço do
  > balanceador, e um deles pode barrar os demais. A API não tem como detectar isso na partida.

  Vazia, o cabeçalho é ignorado. Preenchida, a cadeia é lida da direita para a esquerda até o primeiro endereço
  não confiável, e um valor forjado à esquerda dele é ignorado.

## Erros conhecidos e tratados

| Situação | Resposta | Exemplo | Por quê |
|----------|----------|---------|---------|
| Token sem `kid`, de chave retirada ou com `kid` desconhecido | 401 "Não autenticado." / "Informe um token de acesso válido.", com `WWW-Authenticate: Bearer`; log 3027 | Token emitido antes do REQ-27, ou da chave antiga depois da etapa 3 | O mesmo 401 de qualquer token inválido; o cliente renova a sessão (REQ-27, FR-004, FR-014) |
| Limite de requisições excedido | 429 `LIMITE_REQUISICOES_EXCEDIDO`: "Muitas requisições." / "Aguarde o tempo indicado no cabeçalho Retry-After e tente de novo.", com `Retry-After` em segundos inteiros | 11ª tentativa em 1 minuto com o mesmo e-mail | Igual para conta existente, inexistente ou bloqueada, e sem os valores dos limites (REQ-23, FR-013) |
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
| Perfil alterado depois do login | O token mantém os perfis antigos até expirar | — | O token é autossuficiente; o perfil novo vale na próxima renovação ou no próximo login |
| Renovação com token inválido, expirado, reutilizado ou de sessão encerrada | 401 "Sessão inválida." / "Faça login novamente." em todos os casos | Token de um dispositivo que já fez logout | O motivo real não aparece (REQ-24, FR-006) |
| A resposta da renovação se perde na rede | O cliente fica com um token já usado; ao repetir, 401 e a sessão encerrada | Queda de conexão durante a renovação | Sem tolerância para repetição, por decisão do usuário: quem não recebeu a resposta faz login de novo |
| Logout com token revogado, expirado ou inexistente | O mesmo 204 | Duplo clique em "sair" | Repetir não é erro e não revela nada (FR-008) |
| Recuperação ou reenvio com e-mail sem conta | O mesmo 202, sem corpo, e nenhum e-mail | `ninguem@exemplo.com.br` | Não revela quais e-mails têm conta (FR-013, FR-019) |
| Redefinição com token inválido, expirado ou usado | 400 `TOKEN_INVALIDO`: "Token inválido." / "O token é inválido ou expirou. Peça uma nova recuperação." | Link de um e-mail antigo | O motivo real não aparece, e a senha não muda (FR-016) |
| Confirmação com token inválido, expirado ou usado | 400 `TOKEN_INVALIDO`: "Token inválido." / "O token é inválido ou expirou. Peça um novo e-mail." | Link clicado duas vezes | O motivo real não aparece |
| Senha nova fora das regras, na troca ou na redefinição | 400, `erros.novaSenha`, com as mensagens das regras do contrato v1; o token não é consumido | `"novaSenha": "curta1A"` | As mesmas regras do registro (R-09) |
| Troca com a senha atual errada, ou com a conta bloqueada | 400, `erros.senhaAtual`: "A senha atual está incorreta."; log 3013 | — | O erro conta para o bloqueio, e a resposta é a mesma com a conta bloqueada, sem revelá-lo (FR-012) |
| Troca com a senha nova igual à atual | 400, `erros.novaSenha`: "A nova senha deve ser diferente da atual." | — | FR-010 |
| Troca sem token, ou com token sem perfil | 401, ou 403 | — | É a única rota nova protegida |
| Conta sem nenhum perfil (dado inconsistente) | Login funciona, mas toda rota protegida responde 403. Aparece na gestão com a lista vazia, e o Admin pode atribuir um perfil de qualquer grupo | — | Nenhum acesso sem perfil |
| Gestão com token válido de quem perdeu o Admin ou foi desativado | 403 "Acesso negado." | Admin rebaixado por outro | A rota confere o banco a cada chamada (REQ-25, FR-002) |
| Único Admin ativo retirando o próprio perfil ou se desativando | 409 `USUARIO_ULTIMO_ADMIN`: "Último Admin." / "O sistema precisa de ao menos um Admin ativo."; log 3025 | — | O sistema nunca fica sem Admin (FR-013) |
| Admin retirando o próprio perfil Admin ou se desativando, com outro Admin ativo | 409 `USUARIO_PROPRIA_CONTA`: "Operação sobre a própria conta." / "Peça a outro Admin para fazer esta alteração."; log 3025 | — | Evita perder o acesso por engano (FR-003) |
| Retirada do único perfil | 409 `USUARIO_ULTIMO_PERFIL`: "Último perfil da conta." / "A conta precisa manter ao menos um perfil." | `Admin` de quem só tem esse perfil | FR-011 |
| Atribuição que mistura `Cliente` com perfil interno | 409 `USUARIO_PERFIS_INCOMPATIVEIS`: "Perfis incompatíveis." / "O perfil Cliente não se combina com perfis internos." | `Admin` em conta do registro público | FR-010 |
| Criação com `Cliente` ou perfil desconhecido | 400, `erros.perfil`: "O perfil deve ser Admin ou OperadorLogistica." | `"perfil": "Cliente"` | Conta de Cliente nasce no registro |
| Perfil desconhecido na rota de atribuição, retirada ou no filtro | 400, `erros.perfil`: "O perfil deve ser Admin, OperadorLogistica ou Cliente." | `/perfis/Auditor` | Não é um 404 de rota (R-09) |
| Conta inexistente na gestão | 404 "Usuário não encontrado." / "Não existe usuário com este identificador." | — | — |
| Lock da gestão indisponível por 10 s | 500 "Erro interno.", e nada muda | Banco sobrecarregado | R-03 |

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| Banco fora do ar na partida, com `Banco:PrepararNaInicializacao=true` | A API não sobe. A mensagem nomeia o banco (`ConnectionStrings:SqlServer`) sem expor dados de conexão | Sem banco não há perfis nem usuários iniciais (FR-018) |
| Migration pendente fora de Development e de Testes | A API não sobe e pede a aplicação das migrations pelo comando documentado | A API não altera o esquema em produção (FR-017) |
| Banco fora do ar durante o registro ou o login | 500 "Erro interno.", sem detalhes; nenhum registro fica pela metade | — |
| Configuração de usuário inicial inválida | A API não sobe; a mensagem cita a chave (por exemplo `UsuariosIniciais:0:Senha`), sem mostrar o valor | Falha cedo, sem vazar segredos |
| Armazenamento dos limites indisponível, ou mais de 250 ms sem responder | A requisição segue sem o limite e recebe a resposta normal da rota. A instância fica 5 s sem tentar de novo, e cada recusa já conhecida por ela continua valendo. Log 3008 por pausa e métrica por requisição | O login não cai por causa do limite, e o bloqueio de conta continua protegendo as senhas (REQ-23, FR-012) |
| Deadlock na contagem do limite | Uma nova tentativa, sem abrir a pausa. Se persistir, a requisição segue sem o limite, com log 3008 e métrica, e a instância continua consultando o armazenamento | É uma disputa momentânea, e não um armazenamento fora do ar. Contagens de chaves diferentes não disputam entre si (REQ-23, R-03; issue #75) |
| Configuração do limite inválida | A API não sobe; a mensagem cita a chave (por exemplo `LimiteRequisicoes:Registro:Limite`), sem mostrar o valor | Falha cedo, sem vazar segredos (REQ-23, FR-018) |
| Configuração das sessões inválida | A API não sobe; a mensagem cita a chave (por exemplo `Sessoes:DuracaoMaximaDias`), sem mostrar o valor | REQ-24 |
| Configuração das chaves do JWT inválida (sem chave, mais de 3, identificador fora do formato ou repetido, valor com menos de 32 bytes, ativa fora da lista, `Jwt:ChaveAssinatura` antiga, `Listagens:ChaveCursor` curta) | A API não sobe; todas as falhas saem juntas, citando o identificador ou a posição, sem mostrar valores | Erro de configuração aparece na partida, e não como 401 para todos (REQ-27, FR-005 a FR-008) |
| Serviço de e-mail fora do ar | As rotas respondem como de costume, e o envio é tentado de novo ([fluxo 16](16-envio-de-email.md)) | O envio nunca acontece durante a requisição (FR-013, FR-021) |
| Falha no meio da troca ou da redefinição | 500 "Erro interno." e nada muda: o token não é gasto, e a senha e as sessões continuam como estavam | Tudo em uma transação (R-09) |

## Observabilidade

Logs 3001 (registro), 3002 (login), 3003 (login recusado) e 3004 (bloqueio). A pessoa é
identificada pelo id e pelo e-mail mascarado (`m***@exemplo.com.br`), nunca pela senha,
pelo hash, pelo token ou pelo e-mail completo (FR-019).

O ciclo da autenticação (REQ-24) acrescenta:

- **Log 3010** (`RenovacaoReutilizada`, Warning): quando a reutilização encerra a sessão. Em uma disputa, só quem a
  encerrou registra.
- **Log 3011** (`SessaoEncerrada`), só no encerramento real; **3012** (`SenhaTrocada`); **3013** (`TrocaSenhaRecusada`,
  Warning); **3014** (`SenhaRedefinida`) e **3015** (`EmailConfirmado`). Todos com o id da conta.
- **O pedido de recuperação não gera log na API**, porque ela nem sabe se a conta existe. Quem registra é o Worker, ao
  enviar (log 3016, [fluxo 16](16-envio-de-email.md)).
- **Métrica** `oms.autenticacao.renovacoes`, com `oms.resultado` igual a `renovada`, `recusada` ou `reutilizada`
  ([fluxo 12](12-rastreabilidade-e-metricas.md#métricas)).

Nenhum sinal traz token, senha, e-mail completo ou endereço de origem (SC-008).

A gestão de usuários (REQ-25) acrescenta a auditoria, nos logs, depois de a transação terminar, com a ação, o id de quem fez,
o id da conta e o perfil, quando houver, e sem nome, e-mail ou endereço de origem. Repetir uma operação sem efeito não gera log.

- **Log 3019** (`ConviteAceito`): a senha foi definida pelo convite.
- **Log 3020** (`UsuarioInternoCriado`), **3021** (`PerfilAtribuido`), **3022** (`PerfilRetirado`), **3023** (`UsuarioDesativado`) e
  **3024** (`UsuarioReativado`), todos Information.
- **Log 3025** (`AlteracaoUsuarioRecusada`, Warning): recusa por último Admin ou por própria conta, com o código.

O limite de requisições (REQ-23) acrescenta:

- **Log 3007** (`LimiteRequisicoesExcedido`, Warning): a primeira recusa de cada contagem em cada janela, com a política
  e, nas políticas por origem + e-mail, o e-mail mascarado. Uma inundação não inunda também o log.
- **Log 3008** (`FalhaArmazenamentoLimite`, Warning): uma por pausa, só com o tipo da falha. **Log 3009**
  (`AquecimentoLimiteIgnorado`, Debug): o pool de conexões não pôde ser aberto ao subir.
- **Métricas**: `oms.limite_requisicoes.recusadas` e `oms.limite_requisicoes.falhas_armazenamento`
  ([fluxo 12](12-rastreabilidade-e-metricas.md#métricas)). Nenhum desses sinais traz o endereço de origem.

As contagens guardam só um HMAC da política, da origem e do e-mail, e a limpeza a cada 60 s no Worker remove as
vencidas ([fluxo 11](11-limpeza-e-reenvio-da-dlq.md#limpeza)).

A rotação da chave do JWT (REQ-27) acrescenta:

- **Log 3026** (`ChavesJwtCarregadas`, Information), na partida: "Chaves do JWT: ativa {ChaveAtiva}; aceitas
  {ChavesAceitas}.", com `-` quando não há aceitas. Mostra se cada instância já recebeu a configuração da etapa.
- **Log 3027** (`TokenChaveDesconhecida`, Warning), a cada token recusado por `kid` ausente, retirado ou desconhecido:
  "Token recusado: chave {IdentificadorChave} fora da configuração.". O `kid` só entra se tiver o formato de um
  identificador; senão, `(inválido)`, e sem `kid`, `(ausente)`. Assinatura errada com `kid` configurado não gera o 3027.

Nenhum dos dois traz o valor de uma chave, o token, o usuário ou o endereço de origem.

## Limitações

- **Token de acesso depois do logout, da troca de senha, da desativação ou da retirada de perfil**: continua valendo até
  expirar, em no máximo 15 minutos por padrão, porque não há consulta por requisição. É o limite do risco de um token vazado
  (REQ-24, FR-009; REQ-25, FR-002). Só as rotas de gestão conferem o banco: um Admin removido ainda usa o reenvio da DLQ e o
  catálogo até o token expirar.
- **Duas renovações simultâneas encerram a sessão**: o cliente precisa renovar uma vez de cada vez. Sem tolerância para
  repetição, por decisão do usuário (REQ-24, FR-004).
- **O registro revela e-mails cadastrados**: o 409 continua, porque mudá-lo quebraria o contrato v1. O limite do REQ-23
  reduz o abuso (REQ-24, FR-025).
- **Sem lista de sessões nem "encerrar todas" como rota**: só a troca e a redefinição de senha encerram todas (REQ-24).
- **Sem telas**: a API não tem interface. O e-mail traz o token e, com `Email:UrlBaseAplicacao`, um link para a aplicação.
- **Sem exigir a confirmação**: o e-mail confirmado só fica registrado; uma regra futura pode exigi-lo em ações específicas.
- **Virada da janela do limite**: quem usa todo o limite no fim de uma janela e todo o limite no início da seguinte
  passa até 2 vezes o limite em pouco tempo. A média continua limitada (REQ-23).
- **Troca de endereço**: o limite vale para a origem de rede. Quem troca de endereço a cada tentativa só é contido pelo
  bloqueio de conta.
- **Bloqueio de conta por terceiro**: errar a senha de propósito, a partir de várias origens, bloqueia a conta de outra
  pessoa por 15 minutos. Vem do REQ-03, e o limite não resolve quando as origens são muitas.
- **Inundação de muitas origens**: pode esgotar as 20 conexões reservadas ao limite e deixar uma instância sem ele
  enquanto durar, pela regra de seguir sem o limite. O bloqueio de conta continua protegendo as senhas (REQ-23).
- **Um 401 por cliente na implantação do REQ-27**: os tokens emitidos antes não têm `kid` e são recusados uma vez; o
  cliente renova a sessão sem digitar a senha. Decisão do usuário, sem período de transição (REQ-27, FR-014).
- **Volume do log 3027**: cada token forjado com `kid` desconhecido gera um aviso, e as rotas protegidas não têm limite
  de requisições. O nível de log e a coleta absorvem o volume (REQ-27, R-05).
- **Rotação sem recarga**: as chaves são lidas na partida; cada etapa da rotação exige reiniciar as instâncias. Trocar
  a própria `Listagens:ChaveCursor` invalida os cursores em uso (400), sem rotação com várias chaves para ela (REQ-27).

A falta de rotas de gestão de usuários e de perfis foi resolvida pela issue
[#54](https://github.com/LucasFrenhan/Order-Management-System/issues/54) (REQ-25).
