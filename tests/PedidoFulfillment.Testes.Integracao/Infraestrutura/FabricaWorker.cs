using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

    /// <summary>Recebe a exceção original de uma falha de partida (ver <see cref="Infraestrutura.FalhaPartida"/>).</summary>
    public FalhaPartida FalhaPartida { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:SqlServer", StringConexao);
        builder.UseSetting("Banco:PrepararNaInicializacao", "false");
        builder.UseSetting("Mensageria:IntervaloDespachoMs", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:0", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:1", "200");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:2", "400");
        // Nenhum teste depende de sorte (REQ-09, R-09): aprova tudo, salvo se o teste configurar outra taxa.
        builder.UseSetting("GatewayPagamento:TaxaAprovacaoPercentual", "100");
        foreach (var (chave, valor) in ConfiguracoesBroker.Concat(Configuracoes))
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(FalhaPartida));
        if (Servicos is not null)
        {
            builder.ConfigureTestServices(Servicos);
        }
    }
}
