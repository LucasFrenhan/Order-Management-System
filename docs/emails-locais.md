# E-mails no ambiente local

[← Voltar ao README](../README.md)

O sistema envia e-mails de confirmação de conta, de recuperação de senha e de aviso de senha redefinida. No ambiente local
e na demonstração, o [Mailpit](https://mailpit.axllent.org/) captura esses e-mails, e nada sai para a internet. As regras
do envio estão no [fluxo 16](fluxos/16-envio-de-email.md), e as rotas que pedem os e-mails, no
[fluxo 1](fluxos/01-autenticacao.md).

| O quê | Endereço | Variável, para trocar a porta |
|-------|----------|-------------------------------|
| Tela dos e-mails capturados | <http://localhost:8025> | `OMS_MAILPIT_TELA_PORTA` |
| SMTP, usado pelo Worker | `localhost:1025` | `OMS_MAILPIT_SMTP_PORTA` |

O Mailpit sobe junto com o banco e o broker, com ou sem o perfil `demo`, porque o Worker pelo SDK também precisa dele:

```powershell
docker compose up -d --wait --build
```

## Exemplo: recuperar uma senha

A API não tem telas, então o token vem do e-mail capturado. O Worker processa os pedidos a cada 10 s. Em PowerShell, use
`curl.exe`, e escreva o JSON do corpo em um arquivo.

```bash
curl -X POST http://localhost:5230/api/v1/auth/recuperacao-senha \
  -H "Content-Type: application/json" -d '{"email":"maria@exemplo.com.br"}'
```

Abra <http://localhost:8025>, copie o token do e-mail "Recuperação de senha" e defina a senha nova:

```bash
curl -X POST http://localhost:5230/api/v1/auth/redefinicao-senha \
  -H "Content-Type: application/json" -d '{"token":"<token do e-mail>","novaSenha":"OutraSenha2"}'
```

Sem conta para esse e-mail, a resposta é o mesmo 202, e nenhum e-mail aparece: é assim que a API não revela quais e-mails
têm conta.

## Ler os e-mails por script

O Mailpit também tem uma API, útil em roteiros de teste:

```bash
curl -G http://localhost:8025/api/v1/search --data-urlencode "query=to:maria@exemplo.com.br"
curl http://localhost:8025/api/v1/message/<ID>     # o ID vem da busca; o campo Text traz o corpo
```

## O que saber

- **Os e-mails ficam só em memória**: reiniciar o contêiner apaga os anteriores.
- **Testar o SMTP fora do ar**: `docker compose stop mailpit`. As rotas continuam respondendo como de costume, e o Worker
  registra o log 3017. Depois de `docker compose start mailpit`, o e-mail chega em até 5 minutos.
- **Só para desenvolvimento**: a configuração local usa `Email:Seguranca=Nenhuma`, sem TLS, que o Worker recusa fora de
  Development e de testes. Em outros ambientes, a seção `Email` é obrigatória, ou o Worker não sobe
  ([configuração](fluxos/16-envio-de-email.md#configuração)).
