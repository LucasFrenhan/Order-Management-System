using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Worker;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// O Worker em memória (REQ-06, T016), com despacho e novas tentativas curtos para os testes.
/// </summary>
public sealed class FabricaWorker : WebApplicationFactory<ProgramaWorker>
{
    public required string StringConexao { get; init; }

    public required IReadOnlyDictionary<string, string?> ConfiguracoesBroker { get; init; }

    /// <summary>Registros extras, por exemplo manipuladores de verificação.</summary>
    public Action<IServiceCollection>? Servicos { get; init; }

    public IReadOnlyDictionary<string, string?> Configuracoes { get; init; } = new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:SqlServer", StringConexao);
        builder.UseSetting("Banco:PrepararNaInicializacao", "false");
        builder.UseSetting("Mensageria:IntervaloDespachoMs", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:0", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:1", "200");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:2", "400");
        foreach (var (chave, valor) in ConfiguracoesBroker.Concat(Configuracoes))
        {
            builder.UseSetting(chave, valor);
        }

        if (Servicos is not null)
        {
            builder.ConfigureTestServices(Servicos);
        }
    }
}
