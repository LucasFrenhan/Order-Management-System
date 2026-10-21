using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Saude;

/// <summary>
/// Endereços de saúde (FR-015 a FR-018) sem Docker: a <see cref="FabricaApi"/> aponta banco e
/// broker para portas fechadas.
/// </summary>
public sealed class SaudeTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    [Fact]
    public async Task Vida_responde_200_sem_token_e_sem_consultar_dependencias()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        resposta.Content.Headers.ContentType!.MediaType.Should().Be("application/json");
        (await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().Be("""{"status":"Healthy"}""");
    }

    [Fact]
    public async Task Prontidao_com_dependencias_fora_do_ar_responde_503_sem_dados_de_conexao()
    {
        var resposta = await fabrica.CreateClient().GetAsync("/health/pronto", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        var texto = await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        DeveTerTudoUnhealthy(texto);
        texto.Should().NotContain("127.0.0.1").And.NotContain("Server=").And.NotContain("senha-de-teste")
            .And.NotContain("teste").And.NotContain("Exception").And.NotContain(" at ");
    }

    [Fact]
    public async Task Prontidao_com_dependencia_travada_responde_503_dentro_do_limite()
    {
        // Aceita a conexão TCP (pelo backlog do sistema) e nunca responde.
        var ouvinte = new TcpListener(IPAddress.Loopback, 0);
        ouvinte.Start();
        try
        {
            var porta = ((IPEndPoint)ouvinte.LocalEndpoint).Port.ToString(CultureInfo.InvariantCulture);
            await using var fabricaTravada = new FabricaApi
            {
                Configuracoes = new Dictionary<string, string?>
                {
                    ["ConnectionStrings:SqlServer"] =
                        $"Server=127.0.0.1,{porta};Database=PedidoFulfillment;User Id=sa;Password=senha-de-teste;Encrypt=True;TrustServerCertificate=True",
                    ["RabbitMQ:Servidor"] = "127.0.0.1",
                    ["RabbitMQ:Porta"] = porta
                }
            };
            var cliente = fabricaTravada.CreateClient();
            var cronometro = Stopwatch.StartNew();

            var resposta = await cliente.GetAsync("/health/pronto", TestContext.Current.CancellationToken);

            cronometro.Elapsed.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(7));
            resposta.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            DeveTerTudoUnhealthy(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        }
        finally
        {
            ouvinte.Stop();
        }
    }

    [Fact]
    public async Task Enderecos_de_saude_ficam_fora_do_documento_openapi()
    {
        var documento = await fabrica.CreateClient().GetStringAsync("/openapi/v1.json", TestContext.Current.CancellationToken);

        using var json = JsonDocument.Parse(documento);
        json.RootElement.GetProperty("paths").EnumerateObject().Select(p => p.Name)
            .Should().NotContain(caminho => caminho.StartsWith("/health", StringComparison.Ordinal));
    }

    private static void DeveTerTudoUnhealthy(string texto)
    {
        using var json = JsonDocument.Parse(texto);
        var raiz = json.RootElement;
        raiz.GetProperty("status").GetString().Should().Be("Unhealthy");
        var dependencias = raiz.GetProperty("dependencias");
        dependencias.GetProperty("sqlserver").GetString().Should().Be("Unhealthy");
        dependencias.GetProperty("rabbitmq").GetString().Should().Be("Unhealthy");
    }
}
