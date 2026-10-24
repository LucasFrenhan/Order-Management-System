# Data Model: limpeza de mensageria e reprocessamento da DLQ (REQ-14)

**Spec**: [spec.md](./spec.md) | **Research**: [research.md](./research.md)

Só as mudanças sobre o modelo dos REQ-04 a REQ-13.

## Pedido de reenvio (`PedidosReenvioDlq`, tabela nova)

| Campo | Tipo | Regra |
|-------|------|-------|
| `Id` | `uniqueidentifier` | UUID v7, gerado no registro |
| `AutorId` | `uniqueidentifier` | `sub` do token do Admin; obrigatório |
| `Consumidor` | `nvarchar(100)` | Um dos `ConsumidoresConhecidos.Nomes` (`estoque`, `pagamento`, `entrega`) |
| `TipoEvento` | `nvarchar(100)?` | Filtro opcional; aparado |
| `FalhouDe`, `FalhouAte` | `datetimeoffset?` | Filtro opcional; se os dois, `FalhouAte >= FalhouDe`; UTC |
| `TrechoMotivo` | `nvarchar(200)?` | Filtro opcional; aparado |
| `Situacao` | texto (enum) | `Pendente`, `EmExecucao`, `Concluido`, `Falhou`; check constraint |
| `RegistradoEm` | `datetimeoffset` | Instante do registro pela API |
| `IniciadoEm`, `ConcluidoEm` | `datetimeoffset?` | Preenchidos pelo Worker |
| `Reenviadas`, `BloqueadasPeloLimite`, `ForaDoFiltro` | `int`, padrão 0 | `>= 0` |
| `MotivoFalha` | `nvarchar(500)?` | Só em `Falhou`; sem dados internos sensíveis |

Índice: (`Situacao`, `RegistradoEm`), para a fila do executor e para a limpeza.

### Ciclo de vida

```text
Pendente ──(Worker inicia)──▶ EmExecucao ──(terminou)──▶ Concluido
                                  │  ▲
                                  │  └── retomado se o Worker caiu (só quem tem o lock executa)
                                  └──(broker caiu no meio)──▶ Falhou
```

- `Concluido` e `Falhou` são finais e entram na limpeza depois da retenção (90 dias).
- As contagens são gravadas no fim; num `Falhou`, são as parciais até a queda.

Métodos da entidade (Application): `Registrar(autor, consumidor, filtro, instante)` (fábrica),
`Iniciar(instante)`, `Concluir(contagens, instante)`, `Falhar(contagens, motivo, instante)`;
cada um recusa a transição fora do ciclo acima.

## Índices novos para a limpeza

| Tabela | Índice | Uso |
|--------|--------|-----|
| `EventosSaida` | `IX_EventosSaida_Enviados` em `EnviadoEm`, filtrado `[EnviadoEm] IS NOT NULL` | `DELETE TOP` dos enviados antigos |
| `EventosProcessados` | `IX_EventosProcessados_ProcessadoEm` | `DELETE TOP` dos processados antigos |
| `RegistrosIdempotencia` | `IX_RegistrosIdempotencia_CriadoEm` | `DELETE TOP` das chaves antigas |

Nenhuma coluna muda nessas tabelas.

## Mensagem da DLQ (transporte)

Cabeçalhos novos ou alterados no reenvio: ver [research R-06](./research.md#r-06-cabeçalhos-da-mensagem-reenviada).
O envelope do evento v1 não muda.

## Usuários do broker

| Usuário | Quem usa | Permissões no vhost `/` (configurar, escrever, ler) |
|---------|----------|------------------------------------------------------|
| `oms` (`OMS_RABBITMQ_USUARIO`) | Worker | `.*`, `.*`, `.*` (como hoje) |
| `oms-api` (`OMS_RABBITMQ_API_USUARIO`) | API | `^$`, `^$`, `^$`: só abre a conexão da prontidão |
