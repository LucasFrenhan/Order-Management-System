# Modelo de dados: limite de requisições (REQ-23)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

## Tabela `ContagensLimite` (migration `LimiteRequisicoes`)

Uma linha por contagem de uma política e uma identidade. É um estado técnico e descartável: perder uma
linha só devolve tentativas até o fim da janela. Por isso a entidade fica na `Infrastructure`, fora do domínio.

| Coluna | Tipo | Regras |
|--------|------|--------|
| `Chave` | `binary(32)`, PK | HMAC-SHA256 de política, origem e e-mail ([R-08](./research.md#r-08-forma-não-legível-logs-e-métricas-fr-010-fr-020-fr-021)). Sem dados legíveis (FR-010) |
| `Contador` | `int` | Requisições aceitas na janela. Nunca passa do limite (FR-007) |
| `FimJanela` | `datetime2(3)` | Em UTC, pelo relógio do banco (FR-008) |
| `BloqueioRegistrado` | `bit`, padrão 0 | A primeira recusa da janela já gerou o log (FR-020). Volta a 0 com a janela nova |

**Índice**: `IX_ContagensLimite_FimJanela` (`FimJanela`), para a limpeza (R-07).

**Tamanho**: cerca de 50 bytes por linha, mais os índices.

## Ciclo de uma contagem

```text
sem linha / janela vencida ── requisição aceita ──► em andamento (Contador = 1, nova FimJanela)
em andamento (Contador < limite) ── requisição aceita ──► Contador + 1
em andamento (Contador = limite) ── requisição ──► recusada: Contador igual
                                                   1ª recusa: BloqueioRegistrado = 1 e log 3007
FimJanela passou ── próxima requisição ──► recomeça (Contador = 1, BloqueioRegistrado = 0)
FimJanela passou ── limpeza no Worker ──► linha removida
```

Uma requisição barrada por uma contagem não muda a outra contagem do mesmo login (FR-002).

## Política de limite (configuração, sem tabela)

| Política | Rota | Identidade | Limite padrão | Janela padrão |
|----------|------|------------|:-------------:|:-------------:|
| `login_origem_email` | `POST /api/v1/auth/login` | Origem + e-mail | 10 | 60 s |
| `login_origem` | `POST /api/v1/auth/login` | Origem | 100 | 60 s |
| `registro` | `POST /api/v1/auth/register` | Origem | 20 | 3.600 s |

Um login sem e-mail utilizável conta só em `login_origem`. Faixas válidas no
[contrato](./contracts/limite-requisicoes.md#configuração).

## Origem (normalização, sem tabela)

| Endereço da conexão, depois dos intermediários | Origem |
|---------------------------------|--------|
| IPv4 | O endereço exato |
| IPv4 mapeado em IPv6 (`::ffff:a.b.c.d`) | O IPv4 |
| IPv6 | O bloco `/64` |
| Sem endereço | `desconhecida`, uma só contagem |

## Bloqueio local (memória, sem tabela)

É por instância e nunca compartilhado ([R-05](./research.md#r-05-bloqueio-local-para-aliviar-o-banco)): a chave
de uma contagem recusada e até quando vale, até 100 mil entradas. Some no reinício, e o armazenamento volta a decidir.
