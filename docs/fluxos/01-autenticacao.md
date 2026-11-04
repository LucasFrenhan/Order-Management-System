# Fluxo 1: autenticação

**Rotas**: `POST /api/v1/auth/register` e `POST /api/v1/auth/login`, públicas.
**Origem**: REQ-01 ([contrato](../../specs/001-contratos-api/contracts/openapi-v1.yaml)),
REQ-03 ([spec](../../specs/003-autenticacao-identity/spec.md),
[token](../../specs/003-autenticacao-identity/contracts/token-acesso.md),
[configuração](../../specs/003-autenticacao-identity/contracts/configuracao.md)) e
REQ-23 ([spec](../../specs/023-rate-limiting/spec.md),
[contrato do limite](../../specs/023-rate-limiting/contracts/limite-requisicoes.md)).

## Perfis

| Perfil | Como surge | O que pode |
|--------|------------|------------|
| `Cliente` | Registro pela API | Criar pedidos, consultar os próprios e cancelar o próprio pedido enquanto ele está `Pendente` |
| `Admin` | Usuário inicial, por configuração | Consultar e operar qualquer pedido, inclusive o despacho; pedir e consultar o reenvio da DLQ (fluxo 11) |
| `OperadorLogistica` | Usuário inicial, por configuração | O mesmo que o Admin nas rotas de pedido; sem acesso às rotas administrativas (403) |

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

## Limite de requisições

O limite é uma regra do sistema, e não de uma instância: quem esgota as tentativas pela API A continua
barrado pela API B, porque as contagens ficam no SQL Server (tabela `ContagensLimite`). A verificação
vem antes de qualquer outra regra da rota e não muda as respostas de quem está dentro do limite.

| Política | Rota | O que forma a contagem | Padrão |
|----------|------|------------------------|:------:|
| `login_origem_email` | Login | Origem + e-mail | 10 por minuto |
| `login_origem` | Login | Origem | 100 por minuto |
| `registro` | Registro | Origem | 20 por hora |

- **Janela fixa**: começa na primeira requisição da contagem e recomeça ao terminar. Quem insiste durante
  a espera não a prolonga.
- **Dois limites no login**: exceder qualquer um barra a tentativa, e a tentativa barrada por um não consome
  o outro. Assim, quem insiste em um e-mail não trava o login do resto da sua rede.
- **E-mail**: sem diferenciar maiúsculas e sem os espaços das pontas. Corpo inválido, ou sem um e-mail utilizável,
  conta só em `login_origem` e segue respondendo o 400 de sempre.
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
| Perfil alterado depois do login | O token mantém os perfis antigos até expirar | — | O token é autossuficiente; o perfil novo vale no próximo login |
| Conta sem nenhum perfil (dado inconsistente) | Login funciona, mas toda rota protegida responde 403 | — | Nenhum acesso sem perfil |

## Falhas externas

| Falha | Comportamento | Por quê |
|-------|---------------|---------|
| Banco fora do ar na partida, com `Banco:PrepararNaInicializacao=true` | A API não sobe. A mensagem nomeia o banco (`ConnectionStrings:SqlServer`) sem expor dados de conexão | Sem banco não há perfis nem usuários iniciais (FR-018) |
| Migration pendente fora de Development e de Testes | A API não sobe e pede a aplicação das migrations pelo comando documentado | A API não altera o esquema em produção (FR-017) |
| Banco fora do ar durante o registro ou o login | 500 "Erro interno.", sem detalhes; nenhum registro fica pela metade | — |
| Configuração de usuário inicial inválida | A API não sobe; a mensagem cita a chave (por exemplo `UsuariosIniciais:0:Senha`), sem mostrar o valor | Falha cedo, sem vazar segredos |
| Armazenamento dos limites indisponível, ou mais de 250 ms sem responder | A requisição segue sem o limite e recebe a resposta normal da rota. A instância fica 5 s sem tentar de novo, e cada recusa já conhecida por ela continua valendo. Log 3008 por pausa e métrica por requisição | O login não cai por causa do limite, e o bloqueio de conta continua protegendo as senhas (REQ-23, FR-012) |
| Configuração do limite inválida | A API não sobe; a mensagem cita a chave (por exemplo `LimiteRequisicoes:Registro:Limite`), sem mostrar o valor | Falha cedo, sem vazar segredos (REQ-23, FR-018) |

## Observabilidade

Logs 3001 (registro), 3002 (login), 3003 (login recusado) e 3004 (bloqueio). A pessoa é
identificada pelo id e pelo e-mail mascarado (`m***@exemplo.com.br`), nunca pela senha,
pelo hash, pelo token ou pelo e-mail completo (FR-019).

O limite de requisições (REQ-23) acrescenta:

- **Log 3007** (`LimiteRequisicoesExcedido`, Warning): a primeira recusa de cada contagem em cada janela, com a política
  e, em `login_origem_email`, o e-mail mascarado. Uma inundação não inunda também o log.
- **Log 3008** (`FalhaArmazenamentoLimite`, Warning): uma por pausa, só com o tipo da falha. **Log 3009**
  (`AquecimentoLimiteIgnorado`, Debug): o pool de conexões não pôde ser aberto ao subir.
- **Métricas**: `oms.limite_requisicoes.recusadas` e `oms.limite_requisicoes.falhas_armazenamento`
  ([fluxo 12](12-rastreabilidade-e-metricas.md#métricas)). Nenhum desses sinais traz o endereço de origem.

As contagens guardam só um HMAC da política, da origem e do e-mail, e a limpeza a cada 60 s no Worker remove as
vencidas ([fluxo 11](11-limpeza-e-reenvio-da-dlq.md#limpeza)).

## Limitações

- Não há renovação de token (refresh), troca de senha nem recuperação de conta.
- Não há rota de gestão de usuários ou de perfis.
- **Virada da janela do limite**: quem usa todo o limite no fim de uma janela e todo o limite no início da seguinte
  passa até 2 vezes o limite em pouco tempo. A média continua limitada (REQ-23).
- **Troca de endereço**: o limite vale para a origem de rede. Quem troca de endereço a cada tentativa só é contido pelo
  bloqueio de conta.
- **Bloqueio de conta por terceiro**: errar a senha de propósito, a partir de várias origens, bloqueia a conta de outra
  pessoa por 15 minutos. Vem do REQ-03, e o limite não resolve quando as origens são muitas.
- **Inundação de muitas origens**: pode esgotar as 20 conexões reservadas ao limite e deixar uma instância sem ele
  enquanto durar, pela regra de seguir sem o limite. O bloqueio de conta continua protegendo as senhas (REQ-23).
