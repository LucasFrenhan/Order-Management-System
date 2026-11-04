using Microsoft.Extensions.Caching.Memory;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Infrastructure.LimiteRequisicoes;

/// <summary>
/// Recusas conhecidas por esta instância (R-05). Com a memória cheia, uma recusa nova não é guardada
/// e simplesmente volta a ser decidida pelo armazenamento.
/// </summary>
internal sealed class BloqueiosLocaisMemoria(TimeProvider relogio) : IBloqueiosLocais, IDisposable
{
    private const int MaximoEntradas = 100_000;

    private readonly MemoryCache recusas = new(new MemoryCacheOptions { SizeLimit = MaximoEntradas });

    public void Registrar(byte[] chave, TimeSpan espera)
    {
        if (espera <= TimeSpan.Zero)
        {
            return;
        }

        recusas.Set(Convert.ToHexString(chave), relogio.GetUtcNow() + espera, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = espera,
            Size = 1
        });
    }

    public bool TentarObter(byte[] chave, out TimeSpan espera)
    {
        espera = TimeSpan.Zero;
        if (!recusas.TryGetValue(Convert.ToHexString(chave), out DateTimeOffset ate))
        {
            return false;
        }

        espera = ate - relogio.GetUtcNow();
        return espera > TimeSpan.Zero;
    }

    public void Dispose() => recusas.Dispose();
}
