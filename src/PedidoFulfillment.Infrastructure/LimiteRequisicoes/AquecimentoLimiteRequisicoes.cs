using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>
/// Abre o pool de conexões do limite ao subir (R-04, analyze U1). Em segundo plano, sem o tempo máximo
/// das requisições e sem impedir a partida: o banco fora tem o seu próprio tratamento.
/// </summary>
internal sealed partial class AquecimentoLimiteRequisicoes(IArmazenamentoContagens armazenamento, ILogger<AquecimentoLimiteRequisicoes> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (armazenamento is not ArmazenamentoContagensSql sql)
        {
            return;
        }

        try
        {
            await sql.AquecerAsync(stoppingToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                AquecimentoIgnorado(logger, excecao.GetType().Name);
            }
        }
        catch (OperationCanceledException)
        {
            // Encerramento da API.
        }
    }

    [LoggerMessage(EventId = 3009, EventName = "AquecimentoLimiteIgnorado", Level = LogLevel.Debug,
        Message = "Aquecimento do limite de requisições ignorado ({TipoFalha}).")]
    private static partial void AquecimentoIgnorado(ILogger logger, string tipoFalha);
}
