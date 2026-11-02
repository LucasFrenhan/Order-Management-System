using Microsoft.Extensions.Logging;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Mensageria;

/// <summary>
/// Mantém um consumidor inscrito enquanto o Worker estiver no ar (REQ-17, R-02 a R-04): abre a
/// sessão, espera o sinal de perda, fecha a sessão e abre outra. Um único dono por inscrição:
/// nunca duas sessões do mesmo consumidor ao mesmo tempo (FR-006). A recuperação automática do
/// RabbitMQ.Client fica desligada (<see cref="ConexaoRabbitMQ"/>), para não refazer a inscrição
/// junto com este laço.
/// </summary>
internal sealed partial class SupervisorInscricao(
    string consumidor,
    Func<CancellationToken, Task<ISessaoConsumo>> abrir,
    Func<TimeSpan, CancellationToken, Task> esperar,
    EstadoConsumidores estado,
    TimeProvider relogio,
    ILogger logger)
{
    /// <summary>
    /// Espera antes de cada tentativa (R-04): na hora, depois 1, 2, 4, 8 e 10 s, ficando em 10 s.
    /// Com o broker de volta, a próxima tentativa sai em no máximo 10 s, dentro de 1 min (FR-003).
    /// </summary>
    internal static readonly IReadOnlyList<TimeSpan> Intervalos =
        [TimeSpan.Zero, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(4), TimeSpan.FromSeconds(8), TimeSpan.FromSeconds(10)];

    /// <summary>
    /// Espera mínima de uma pausa (REQ-19, R-09): se a dependência já deveria estar de volta, a
    /// sessão não é reaberta em laço.
    /// </summary>
    internal static readonly TimeSpan PausaMinima = TimeSpan.FromMilliseconds(200);

    public async Task ExecutarAsync(CancellationToken cancellationToken)
    {
        var falhas = 0;
        var perdeuInscricao = false;
        TimeSpan? esperaPausa = null;
        DateTimeOffset? pausadoDesde = null;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var espera = esperaPausa ?? Intervalo(falhas);
                esperaPausa = null;
                if (espera > TimeSpan.Zero)
                {
                    await esperar(espera, cancellationToken);
                }

                ISessaoConsumo sessao;
                try
                {
                    sessao = await abrir(cancellationToken);
                }
                catch (Exception excecao) when (!cancellationToken.IsCancellationRequested)
                {
                    falhas++;
                    if (pausadoDesde is not null)
                    {
                        // A retomada falhou (broker fora, por exemplo): agora é perda de inscrição de
                        // verdade, e a prontidão deixa de contar o consumidor como pausado.
                        estado.MarcarSemInscricao(consumidor, relogio.GetUtcNow());
                        pausadoDesde = null;
                        perdeuInscricao = true;
                    }

                    if (perdeuInscricao)
                    {
                        FalhaReinscricao(logger, consumidor, excecao.GetType().Name, excecao.Message, (long)Intervalo(falhas).TotalMilliseconds);
                    }
                    else
                    {
                        FalhaInicio(logger, consumidor, excecao.GetType().Name, excecao.Message);
                    }

                    continue;
                }

                await using (sessao)
                {
                    falhas = 0;
                    if (pausadoDesde is { } desdePausa)
                    {
                        // A reabertura depois da pausa não é reinscrição (REQ-19, R-09): só o log de retomada.
                        ConsumoRetomado(logger, consumidor, (long)(relogio.GetUtcNow() - desdePausa).TotalMilliseconds);
                    }
                    else if (perdeuInscricao)
                    {
                        var desde = estado.SemInscricaoDesde(consumidor) ?? relogio.GetUtcNow();
                        ConsumidorReinscrito(logger, consumidor, (long)(relogio.GetUtcNow() - desde).TotalMilliseconds);
                        TelemetriaOms.Reinscricoes.Add(1, new KeyValuePair<string, object?>(TelemetriaOms.Atributos.Consumidor, consumidor));
                    }

                    pausadoDesde = null;
                    estado.MarcarInscrito(consumidor);
                    var motivo = await sessao.Perdida.WaitAsync(cancellationToken);
                    if (motivo is { Origem: MotivoPerda.Pausa, DisponivelEm: { } disponivelEm })
                    {
                        // Dependência fora do ar (REQ-19, FR-016a): a sessão fecha, as mensagens
                        // esperam na fila, e a prontidão continua saudável até a retomada.
                        var agora = relogio.GetUtcNow();
                        estado.MarcarPausado(consumidor, disponivelEm);
                        ConsumoPausado(logger, consumidor, disponivelEm);
                        pausadoDesde = agora;
                        esperaPausa = disponivelEm - agora > PausaMinima ? disponivelEm - agora : PausaMinima;
                        continue;
                    }

                    estado.MarcarSemInscricao(consumidor, relogio.GetUtcNow());
                    InscricaoPerdida(logger, consumidor, motivo.Origem, motivo.Motivo);
                    perdeuInscricao = true;
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // Encerramento do Worker: sai sem abrir outra sessão; a aberta já foi fechada.
                return;
            }
        }
    }

    private static TimeSpan Intervalo(int falhas) => Intervalos[Math.Min(falhas, Intervalos.Count - 1)];

    [LoggerMessage(EventId = 5008, EventName = "FalhaInicioConsumidor", Level = LogLevel.Warning,
        Message = "Consumidor {Consumidor} não iniciou ({Erro}): {Mensagem}. Nova tentativa em instantes.")]
    private static partial void FalhaInicio(ILogger logger, string consumidor, string erro, string mensagem);

    [LoggerMessage(EventId = 5016, EventName = "InscricaoPerdida", Level = LogLevel.Warning,
        Message = "Consumidor {Consumidor} perdeu a inscrição ({Origem}): {Motivo}")]
    private static partial void InscricaoPerdida(ILogger logger, string consumidor, string origem, string motivo);

    [LoggerMessage(EventId = 5017, EventName = "FalhaReinscricao", Level = LogLevel.Warning,
        Message = "Consumidor {Consumidor} não voltou a se inscrever ({Erro}): {Mensagem}. Nova tentativa em {EsperaMs} ms.")]
    private static partial void FalhaReinscricao(ILogger logger, string consumidor, string erro, string mensagem, long esperaMs);

    [LoggerMessage(EventId = 5018, EventName = "ConsumidorReinscrito", Level = LogLevel.Information,
        Message = "Consumidor {Consumidor} voltou a consumir depois de {DuracaoMs} ms sem inscrição.")]
    private static partial void ConsumidorReinscrito(ILogger logger, string consumidor, long duracaoMs);

    [LoggerMessage(EventId = 5020, EventName = "ConsumoPausado", Level = LogLevel.Warning,
        Message = "Consumo de {Consumidor} pausado até {DisponivelEm:O}: dependência indisponível. As mensagens esperam na fila, sem nova tentativa nem DLQ.")]
    private static partial void ConsumoPausado(ILogger logger, string consumidor, DateTimeOffset disponivelEm);

    [LoggerMessage(EventId = 5021, EventName = "ConsumoRetomado", Level = LogLevel.Information,
        Message = "Consumo de {Consumidor} retomado depois de {DuracaoMs} ms de pausa.")]
    private static partial void ConsumoRetomado(ILogger logger, string consumidor, long duracaoMs);
}
