using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Número <c>PED-AAAAMMDD-NNNNNN</c> com o sufixo vindo de uma sequência do SQL Server, atômica
/// entre criações simultâneas (research R-05 do REQ-05). O índice único do número continua
/// sendo a garantia final.
/// </summary>
internal sealed class GeradorNumeroPedido(ContextoBancoDados contexto) : IGeradorNumeroPedido
{
    private const int LimiteSufixo = 1_000_000;

    public async Task<string> ProximoAsync(DateTimeOffset instante, CancellationToken cancellationToken)
    {
        var valor = await contexto.Database
            .SqlQueryRaw<long>($"SELECT NEXT VALUE FOR {ContextoBancoDados.SequenciaNumeroPedido} AS Value")
            .SingleAsync(cancellationToken);
        return string.Create(CultureInfo.InvariantCulture, $"PED-{instante.UtcDateTime:yyyyMMdd}-{valor % LimiteSufixo:D6}");
    }
}
