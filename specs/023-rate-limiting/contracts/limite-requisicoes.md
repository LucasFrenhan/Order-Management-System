# Contrato: limite de requisições (REQ-23)

**Spec**: [spec.md](../spec.md) | **Contrato v1**: [openapi-v1.yaml](../../001-contratos-api/contracts/openapi-v1.yaml)

## Resposta 429

Acréscimo a `POST /api/v1/auth/login` e `POST /api/v1/auth/register`. As demais respostas das duas rotas não mudam.

```http
HTTP/1.1 429 Too Many Requests
Content-Type: application/problem+json
Retry-After: 42

{
  "type": "about:blank",
  "title": "Muitas requisições.",
  "status": 429,
  "detail": "Aguarde o tempo indicado no cabeçalho Retry-After e tente de novo.",
  "instance": "/api/v1/auth/login",
  "traceId": "00-…",
  "codigo": "LIMITE_REQUISICOES_EXCEDIDO"
}
```

| Item | Regra |
|------|-------|
| Schema | `NegocioProblemDetails`, já existente |
| `codigo` | `LIMITE_REQUISICOES_EXCEDIDO`, novo e estável |
| `Retry-After` | Segundos inteiros, arredondados para cima, no mínimo 1. Com dois limites excedidos, a maior espera |
| Conteúdo | Igual em todos os casos: não revela os limites, nem qual foi excedido, se a conta existe ou se está bloqueada |
| Efeitos | Não avalia a senha, não conta para o bloqueio de conta, não cria conta e não prolonga a espera |

O `429` do SSE continua como está (`ProblemDetails`, sem `Retry-After`, REQ-18).

## Configuração

Seção `LimiteRequisicoes`, validada na partida da API. Um valor inválido impede a partida, e a mensagem cita a
chave, sem o valor.

| Chave | Padrão | Faixa | Observação |
|-------|:------:|-------|------------|
| `ChaveContagem` | sem padrão | Base64 de 32 bytes | Obrigatória e igual em todas as instâncias. Há valor de desenvolvimento em `appsettings.Development.json` |
| `LoginOrigemEmail:Limite` | 10 | 1 a 1.000.000 | |
| `LoginOrigemEmail:JanelaSegundos` | 60 | 1 a 86.400 | |
| `LoginOrigem:Limite` | 100 | 1 a 1.000.000 | |
| `LoginOrigem:JanelaSegundos` | 60 | 1 a 86.400 | |
| `Registro:Limite` | 20 | 1 a 1.000.000 | |
| `Registro:JanelaSegundos` | 3.600 | 1 a 86.400 | |
| `TempoMaximoEsperaMs` | 250 | 50 a 5.000 | Acima disso, a requisição segue sem o limite |
| `PausaAposFalhaSegundos` | 5 | 0 a 300 | Tempo sem tentar o armazenamento depois de uma falha |
| `IntermediariosConfiaveis` | vazia | IPs ou faixas CIDR | Quem pode informar a origem por `X-Forwarded-For` |

**Não existe opção para desligar o limite** (FR-018).

**Em produção com balanceador, `IntermediariosConfiaveis` é obrigatório na prática**: sem ele, todos os clientes
dividem a contagem do endereço do balanceador. Esse caso não dá para validar na partida, então fica no fluxo 1.

Seção `Limpeza`, no Worker:

| Chave | Padrão | Faixa |
|-------|:------:|:-----:|
| `IntervaloContagensSegundos` | 60 | 10 a 3.600 |
| `LotesContagensPorRodada` | 100 | 1 a 1.000 |

## Observabilidade

| Sinal | Quando | Conteúdo |
|-------|--------|----------|
| Log 3007 `LimiteRequisicoesExcedido` (Warning) | Primeira recusa de cada contagem em cada janela | Política e, em `login_origem_email`, o e-mail mascarado |
| Log 3008 `FalhaArmazenamentoLimite` (Warning) | Falha do armazenamento, uma por pausa | O tipo da falha |
| Log 3009 `AquecimentoLimiteIgnorado` (Debug) | O pool de conexões não pôde ser aberto ao subir (R-04) | O tipo da falha |
| `oms.limite_requisicoes.recusadas` (contador) | Toda recusa, inclusive pelo bloqueio local | Atributo `oms.limite.politica`: `login_origem_email`, `login_origem` ou `registro` |
| `oms.limite_requisicoes.falhas_armazenamento` (contador) | Toda requisição que seguiu sem o limite, inclusive durante a pausa | Sem atributos |

Nenhum sinal contém senha, e-mail completo ou endereço de origem (FR-020, FR-021, SC-007).
