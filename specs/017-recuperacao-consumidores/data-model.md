# Data Model: Recuperação dos consumidores depois de o broker reiniciar

**Requisito**: REQ-17 | **Spec**: [spec.md](./spec.md)

Nenhuma tabela, coluna, migration ou contrato de evento muda. As entidades abaixo vivem só na
memória do processo de workers.

## Inscrição de consumidor (`EstadoConsumidores`)

Singleton por host, com uma entrada por consumidor conhecido (`IDescritorConsumidor`:
`estoque`, `pagamento`, `entrega`).

| Campo | Tipo | Regra |
|-------|------|-------|
| `Consumidor` | `string` | Chave; o nome do consumidor, igual ao da fila (`oms.{consumidor}`) |
| `Inscrito` | `bool` | Começa falso; verdadeiro depois do `basic.consume` da sessão; falso no sinal de perda |
| `SemInscricaoDesde` | `DateTimeOffset?` | Instante da perda; usado no 5018 (`DuracaoMs`); nulo enquanto inscrito |

- Pronto (`consumidores = Healthy`) ⇔ todas as entradas com `Inscrito = true`.
- Cada mudança é espelhada em `EstadoMetricas` para o gauge `oms.mensageria.consumidor.inscrito`.

### Transições

```text
            partida                basic.consume ok
 (sem inscrição) ──────► abrindo ─────────────────────► inscrito
       ▲                   │  ▲                            │
       │                   │  │ espera 1,2,4,8,10 s        │ sinal de perda
       │      falha (5017) │  │                            │ (5016)
       │                   ▼  │                            ▼
       │                esperando ◄───────────────── sem inscrição
       │                                                  │
       └──────────────── encerramento do Worker ◄─────────┘ (sai do laço)
```

- `abrindo → inscrito` depois de uma perda soma 1 em `oms.mensageria.consumidor.reinscricoes` e
  registra 5018.
- Na partida, as falhas continuam registrando 5008, como hoje.

## Sessão de consumo (`SessaoConsumo`)

Criada por tentativa, descartada no sinal de perda. Uma por consumidor por vez.

| Campo | Tipo | Regra |
|-------|------|-------|
| `CanalConsumo` | `IChannel` | Recebe as entregas; o ack usa sempre este canal |
| `CanalPublicacao` | `IChannel` | Com confirmação; republica na espera e na DLQ |
| `Perdida` | `Task<MotivoPerda>` | Completa no primeiro sinal (`ChannelShutdownAsync` de um dos canais ou `UnregisteredAsync` não pedido) |

## Motivo da perda (`MotivoPerda`)

| Campo | Tipo | Valores |
|-------|------|---------|
| `Origem` | `string` | `canal` ou `cancelamento` |
| `Motivo` | `string` | `ReplyCode ReplyText` do fechamento, ou `cancelada pelo broker` |
