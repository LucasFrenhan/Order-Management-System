# Fluxo 13: notificação de mudança de status

**Onde roda**: na gravação de qualquer mudança de status (API e Worker), no Worker (webhooks,
`ProcessadorNotificacoes`) e na API (eventos em tempo real, `LeitorAvisos` e rota SSE).
**Origem**: REQ-18 ([spec](../../specs/018-notificacao-status/spec.md),
[research](../../specs/018-notificacao-status/research.md),
[contrato da API](../../specs/018-notificacao-status/contracts/api-notificacoes.md),
[contrato do webhook](../../specs/018-notificacao-status/contracts/webhooks.md)), issue #43.

## Para que serve

Quem acompanha um pedido fica sabendo da mudança sem consultar a API de tempos em tempos. Há
dois canais, e a consulta do pedido continua sendo a fonte da verdade: a notificação avisa, o
`GET /api/v1/pedidos/{id}` confirma.

| Canal | Para quem | Garantia |
|-------|-----------|----------|
| Webhook | Sistemas de fora (loja, ERP, parceiro) | Pelo menos uma vez, com novas tentativas por 24 h |
| SSE (`text/event-stream`) | Aplicação conectada (front-end, painel) | Enquanto a conexão está aberta; reconexão recupera até 500 avisos perdidos |

**Por que sem broker na API**: a API lê os avisos do banco, que ela já usa, e continua sem
nenhuma permissão no RabbitMQ (REQ-14). Os webhooks também leem do banco: com o broker fora do
ar, os dois canais continuam funcionando (decisão do clarify e do plano, research R-05 e R-11).

## Caminho feliz

```mermaid
sequenceDiagram
    participant API
    participant DB as SQL Server
    participant W as Worker
    participant R as Receptor
    actor A as Aplicação
    API->>DB: mudança de status + histórico + pedido.status-alterado (outbox) + aviso
    A->>API: GET /api/v1/notificacoes/eventos
    API-->>A: conectado
    loop a cada 10 s, por instância
        API->>DB: avisos novos (cursor de rowversion)
        API-->>A: pedido.status-alterado
    end
    loop a cada 5 s
        W->>DB: avisos novos → uma entrega por destino; entregas vencidas
        W->>R: POST assinado
        R-->>W: 2xx
        W->>DB: entrega Entregue
    end
    R->>API: GET /api/v1/pedidos/{id} (confirma)
```

1. **O aviso nasce na gravação.** O `ArmazenamentoPedidos.SalvarAsync` transforma cada entrada
   nova do histórico (exceto a criação) em um `pedido.status-alterado` no outbox e em uma linha
   em `AvisosPedido`, com o corpo da notificação pronto. A confirmação do estorno gera o aviso
   `pedido.estorno-confirmado`. Tudo na mesma transação da mudança: se ela não grava, nada sai.
2. **Leitura sem lacunas.** API e Worker leem `AvisosPedido` por `Posicao` (`rowversion`),
   só abaixo de `MIN_ACTIVE_ROWVERSION()`: um aviso de transação ainda aberta segura os
   seguintes, e nenhum fica para trás.
3. **Webhook.** A rodada do Worker cria uma `EntregaNotificacao` por destino ativo que deve
   receber o aviso (os globais e os do Cliente dono, cadastrados até o aviso) e avança o cursor
   na mesma transação. Depois envia as entregas vencidas, no máximo uma por destino e até 8 ao
   mesmo tempo.
4. **SSE.** Cada instância da API lê os avisos novos a cada 10 s e entrega às conexões abertas
   cujo filtro aceita o aviso.

### Corpo da notificação (webhook e `data` do SSE)

```json
{
  "idNotificacao": "0193f0a4-1d2c-7e3f-8a9b-0c1d2e3f4a5b",
  "tipo": "pedido.status-alterado",
  "versao": 1,
  "ocorridoEm": "2026-11-01T14:00:07+00:00",
  "dados": {
    "pedidoId": "0193f0a2-…",
    "numero": "PED-20261101-000042",
    "statusAnterior": "Pendente",
    "statusNovo": "Processando",
    "sequencia": 2,
    "link": "/api/v1/pedidos/0193f0a2-…"
  }
}
```

Sem e-mail, endereço, itens, valores, dados de pagamento, transportadora, rastreio ou o texto
do motivo (FR-002). `pedido.estorno-confirmado` traz só `pedidoId`, `numero` e `link`. Schema:
[`notificacao.v1.schema.json`](../../specs/018-notificacao-status/contracts/notificacoes/notificacao.v1.schema.json).

### O que gera aviso

| Mudança | Origem no evento | Aviso |
|---------|------------------|-------|
| Pagamento aprovado ou recusado, ordem de entrega criada | `Worker` | `pedido.status-alterado` |
| `PATCH /status` e cancelamento pela operação | `Operacao` | `pedido.status-alterado` |
| Cancelamento pelo Cliente | `Cliente` | `pedido.status-alterado` |
| Despacho com rastreio | `Operacao` | `pedido.status-alterado` |
| Cancelamento pela falta de estoque ou pela reconciliação | `Worker` | `pedido.status-alterado` |
| Estorno confirmado | — | `pedido.estorno-confirmado` |
| Criação do pedido | — | nenhum (o Cliente já recebeu o 201) |
| Correção do rastreio | — | nenhum |

A origem segue os perfis do autor, com a regra do REQ-15: quem tem perfil de operação (Admin
ou OperadorLogistica) muda como `Operacao`, mesmo sendo também Cliente. A origem só vai no
evento do broker, não no corpo da notificação.

## Destinos de webhook

| Rota | Quem | Resposta |
|------|------|----------|
| `POST /api/v1/notificacoes/destinos` | Cliente (os próprios) e Admin (globais) | 201 com o `segredo` (`whsec_…`), só nesta resposta |
| `GET /api/v1/notificacoes/destinos` | Cliente e Admin | 200 com a lista do escopo, sem o segredo |
| `DELETE /api/v1/notificacoes/destinos/{id}` | Dono, ou qualquer Admin para os globais | 204; entregas pendentes viram `Descartada` |
| `POST /api/v1/notificacoes/destinos/{id}/reativacao` | Idem | 200; zera as falhas; idempotente |
| `GET /api/v1/notificacoes/destinos/{id}/entregas?limite=50` | Idem | 200, da mais nova para a mais antiga (limite de 1 a 200) |

O OperadorLogistica não gerencia destinos (403). Destino de outro Cliente, ou global para um
Cliente, responde 404. O Admin não vê os destinos dos Clientes.

### Erros tratados

| Situação | Resposta | Exemplo | Motivo |
|----------|----------|---------|--------|
| URL vazia ou só com espaços | 400 | "A URL é obrigatória." | FR-008 |
| URL com mais de 2048 caracteres | 400 | "A URL deve ter até 2048 caracteres." | Tamanho da coluna |
| URL relativa ou fora de http/https | 400 | "A URL deve ser absoluta, com https." | FR-008 |
| URL com usuário e senha | 400 | "A URL não pode ter usuário nem senha." | FR-008 |
| `http` fora de Development e dos testes | 400 | "A URL deve ser absoluta, com https." | FR-008 |
| Host que não resolve ou resolve para endereço interno | 400 `NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO` | "O endereço do destino não é público." | Proteção contra SSRF (FR-008) |
| Sexto destino do Cliente, ou sexto global | 409 `NOTIFICACAO_DESTINO_LIMITE` | "O limite de 5 destinos foi atingido." | FR-010 |
| `limite` fora de 1 a 200 | 400 | "O limite deve estar entre 1 e 200." | Contrato |

Cadastros simultâneos do mesmo dono esperam um pelo outro (lock de aplicação
`oms-destinos:{dono}`), e o limite não é ultrapassado.

## Entrega do webhook

Cabeçalhos do [Standard Webhooks](https://www.standardwebhooks.com/): `webhook-id` (o
`idNotificacao`, igual em todas as tentativas), `webhook-timestamp` (segundos Unix) e
`webhook-signature` (`v1,` + Base64 de HMAC-SHA256 de `{id}.{timestamp}.{corpo}` com o segredo).
O receptor confere a assinatura, deduplica por `webhook-id` e ignora `sequencia` menor ou igual
à última que tratou do pedido: não há garantia de ordem.

| Resposta do receptor | O que acontece |
|----------------------|----------------|
| `2xx` em até 10 s | `Entregue`; as falhas seguidas do destino voltam a zero |
| `410 Gone` | Entrega `Esgotada` e destino desativado na hora (motivo `410 Gone`); pendentes do destino viram `Descartada` |
| Outra resposta, `3xx` (não seguido), tempo esgotado, falha de conexão ou endereço recusado | Nova tentativa em 30 s, 2 min, 10 min, 30 min, 1 h e depois a cada 2 h |
| Falha no limite de 24 h desde a primeira tentativa | `Esgotada` e destino desativado (motivo "Entrega … esgotada") |
| Segredo que não decifra (chave de cifra trocada) | Falha "segredo ilegível", sem enviar nada, até esgotar |

Um destino desativado não recebe entregas novas; os avisos do período não são recuperados
depois da reativação.

**Conta desativada** (REQ-25, [fluxo 1](01-autenticacao.md#conta-desativada)): os destinos próprios dela não recebem os avisos
gerados durante a desativação nem os que estavam pendentes, que viram `Descartada`, e nada disso é reenviado. Um aviso anterior à
reativação também não sai depois dela, mesmo atrasado no Worker. Os destinos e os segredos são mantidos, e nem o `Ativo` nem as falhas
seguidas do destino mudam: a reativação retoma os envios a partir dali. Os destinos globais não mudam, mesmo que a conta tenha cadastrado
algum deles.

## Eventos em tempo real (SSE)

`GET /api/v1/notificacoes/eventos[?pedidoId=…]`, com o token de sempre. O Cliente recebe os
próprios pedidos; Admin e OperadorLogistica, todos.

| Evento | Quando |
|--------|--------|
| `conectado` | Primeiro evento sem `Last-Event-ID`; o `id` é o do último aviso gravado (ou o GUID zero) |
| `pedido.status-alterado`, `pedido.estorno-confirmado` | Cada aviso visível ao usuário, com `id` = `idNotificacao` |
| `sincronizacao-necessaria` | `Last-Event-ID` desconhecido (`AvisoDesconhecido`), mais de 500 perdidos (`LimiteReenvio`) ou fila cheia (`ClienteLento`) |
| `sinal` | A cada 30 s sem outro evento |

A aplicação abre a conexão **antes** de consultar o pedido: o que mudar entre o `conectado` e o
`GET` chega pela conexão. Ao reconectar com `Last-Event-ID`, recebe os avisos perdidos (só os
do pedido, numa conexão de um pedido), em ordem, antes dos novos e sem repetição.

A conexão de uma conta desativada termina, no máximo, quando o token de acesso expira. Depois de reativar, uma reconexão com
`Last-Event-ID` pode trazer avisos do período desativado, dentro da retenção. É o esperado: a suspensão vale para os webhooks, e a conta
já recuperou o acesso a esses pedidos (REQ-25).

| Situação | Resposta |
|----------|----------|
| Sem token ou token inválido | 401 |
| Token sem perfil | 403 |
| `pedidoId` inválido | 400 |
| `pedidoId` inexistente ou de outro Cliente | 404, como no `GET` do pedido |
| Quarta conexão do usuário na mesma instância | 429 `NOTIFICACAO_SSE_LIMITE_CONEXOES` "O limite de 3 conexões de eventos foi atingido." |
| Token expira | A API encerra a conexão, a cada 15 minutos por padrão desde o REQ-24. O cliente renova a sessão ([fluxo 1](01-autenticacao.md#renovação)) e reconecta com `Last-Event-ID`, sem perder avisos dentro da retenção |
| Aplicação lenta (100 avisos na fila) | `sincronizacao-necessaria` e fim da conexão |

## `ETag` na consulta

O `GET /api/v1/pedidos/{id}` devolve `ETag` forte da versão do pedido; com `If-None-Match`
igual, responde 304, sem corpo. Detalhes no [fluxo 3](03-consulta-e-mudanca-de-status.md).

## Concorrência

| Situação | Comportamento |
|----------|---------------|
| Transação lenta grava um aviso depois de outros já confirmados | A leitura por `rowversion` abaixo de `MIN_ACTIVE_ROWVERSION()` segura os seguintes até ela confirmar; nenhum aviso é pulado |
| Duas mudanças no mesmo pedido | A concorrência otimista do pedido deixa uma vencer; a sequência é única e crescente |
| Várias instâncias da API | Cada uma lê o banco por conta própria; cada conexão recebe o aviso na instância em que está |
| Várias instâncias do Worker | Uma rodada por vez (lock `oms-notificacoes`), e o índice único `(DestinoId, AvisoId)` impede entrega repetida |
| Mudança gravada fora do `SalvarAsync` num consumidor | A base de consumo recusa a gravação (erro de programação); a mensagem vai para as novas tentativas e a DLQ |

## Falhas externas

| Falha | Comportamento |
|-------|---------------|
| Receptor fora do ar ou lento | Só a entrega dele espera; os outros destinos recebem no prazo |
| Broker fora do ar | Não afeta os webhooks nem o SSE; só o `pedido.status-alterado` para outros sistemas espera no outbox (fluxo 8) |
| Banco fora do ar | As mudanças também não gravam; as conexões SSE continuam com o sinal de vida, e a leitura e a rodada tentam de novo no próximo ciclo (log 10014) |
| Destino que passa a apontar para a rede interna | O IP é conferido em cada conexão; a entrega falha com "endereço recusado" e o log 10009 |

## Configuração (seção `Notificacoes`, validada na partida da API e do Worker)

| Chave | Padrão | Faixa |
|-------|--------|-------|
| `ChaveCifra` | — (obrigatória) | Base64 de 32 bytes, igual na API e no Worker |
| `PermitirRedeLocal` | `false` | `true` só em Development e nos testes |
| `DestinosPorCliente` / `DestinosGlobais` | 5 / 5 | 1 a 50 |
| `Envio:Habilitado` | `true` | — |
| `Envio:IntervaloSegundos` | 5 | 1 a 300 |
| `Envio:TempoLimiteSegundos` | 10 | 1 a 60 |
| `Envio:Paralelismo` | 8 | 1 a 64 |
| `Envio:LimiteHoras` | 24 | 1 a 168 |
| `Envio:TamanhoLote` | 500 | 1 a 10 000 |
| `Sse:IntervaloLeituraSegundos` | 10 | 1 a 60 |
| `Sse:ConexoesPorUsuario` | 3 | 1 a 100 |
| `Sse:SinalDeVidaSegundos` | 30 | 5 a 300 |
| `Sse:MaximoReenvio` | 500 | 1 a 10 000 |

Sem a chave, ou com outro tamanho, nenhum dos dois processos sobe:
"Notificacoes:ChaveCifra deve ser o Base64 de 32 bytes.". A limpeza (fluxo 11) apaga as
entregas finalizadas e os avisos sem entrega depois de 7 dias.

## Observabilidade

| Log | Nível | Quando |
|-----|-------|--------|
| 10001 `DestinoNotificacaoCadastrado` | Information | Cadastro (id, host, global e usuário) |
| 10002 `DestinoNotificacaoRemovido` | Information | Remoção, com as pendentes descartadas |
| 10003 `DestinoNotificacaoReativado` | Information | Reativação |
| 10004 `DestinoNotificacaoDesativado` | Warning | Desativação por esgotamento ou 410 |
| 10005 `EntregasNotificacaoCriadas` | Information | Resumo da distribuição (avisos e entregas) |
| 10006 `NotificacaoEntregue` | Information | 2xx do receptor |
| 10007 `FalhaEntregaNotificacao` | Warning | Falha com nova tentativa marcada |
| 10008 `EntregaNotificacaoEsgotada` | Error | Entrega esgotada |
| 10009 `EnderecoNotificacaoRecusado` | Warning | Host resolve para endereço interno (cadastro ou envio) |
| 10010 `ResumoEnvioNotificacoes` | Information | Fim da rodada de envio com entregas |
| 10011 `ConexaoSseAberta` | Information | Conexão de eventos aberta |
| 10012 `ConexaoSseEncerrada` | Information | Conexão encerrada (motivo) |
| 10013 `ConexaoSseRecusada` | Warning | Limite de conexões |
| 10014 `FalhaLeituraAvisos` | Warning | Falha na leitura, na rodada ou numa entrega; tenta de novo |

A URL do destino aparece só com esquema e host; o segredo, o corpo enviado e o corpo da
resposta nunca aparecem. Métricas: `oms.notificacoes.entregas` (`oms.resultado` = `entregue`,
`falha`, `esgotada`), `oms.notificacoes.latencia`, `oms.notificacoes.pendentes`,
`oms.notificacoes.destinos_desativados` e `oms.notificacoes.sse.conexoes`. O span
`notificacao enviar` liga-se ao trace da mudança por link; o `traceparent` não vai para o
receptor. Detalhes no [fluxo 12](12-rastreabilidade-e-metricas.md).

## Limitações

- O SSE só reenvia o que se perdeu dentro da retenção de 7 dias e até 500 avisos; além disso, a
  aplicação confirma pelo `GET`.
- O limite de conexões SSE é por instância da API, não global.
- Não há reenvio manual de entrega esgotada nem troca do segredo sem recadastro.
- Avisos gravados antes do REQ-18 não existem: não há aviso retroativo.
- A correção do rastreio não gera aviso.
