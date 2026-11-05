using System.Net.Http.Json;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Um Mailpit real em contêiner, para os testes que passam pelo SMTP (REQ-24, R-05, R-11). Sem Docker,
/// os testes são ignorados, exceto com <c>OMS_EXIGIR_DOCKER=true</c>, quando falham (REQ-07).
/// </summary>
public sealed class MailpitTeste : IAsyncLifetime
{
    private const string Imagem = "axllent/mailpit:v1.33.0";
    private const int PortaSmtp = 1025;
    private const int PortaTela = 8025;

    private IContainer? container;
    private HttpClient? tela;

    public bool DockerDisponivel { get; private set; }

    public string Servidor => container!.Hostname;

    public int Porta => container!.GetMappedPublicPort(PortaSmtp);

    public async ValueTask InitializeAsync()
    {
        try
        {
            container = new ContainerBuilder(Imagem)
                .WithPortBinding(PortaSmtp, true)
                .WithPortBinding(PortaTela, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(r => r.ForPort(PortaTela).ForPath("/readyz")))
                .Build();
            await container.StartAsync();
            tela = new HttpClient { BaseAddress = new Uri($"http://{container.Hostname}:{container.GetMappedPublicPort(PortaTela)}") };
            DockerDisponivel = true;
        }
        catch (DockerUnavailableException excecao)
        {
            if (AmbienteDocker.Obrigatorio())
            {
                throw new InvalidOperationException(AmbienteDocker.MensagemObrigatorio, excecao);
            }

            DockerDisponivel = false;
        }
    }

    /// <summary>Os e-mails capturados para um destinatário, dos mais recentes para os mais antigos.</summary>
    public async Task<IReadOnlyList<EmailCapturado>> MensagensParaAsync(string destino)
    {
        var cancelamento = TestContext.Current.CancellationToken;
        var lista = await tela!.GetFromJsonAsync<JsonElement>($"/api/v1/search?query={Uri.EscapeDataString($"to:{destino}")}", cancelamento);
        var mensagens = new List<EmailCapturado>();
        foreach (var item in lista.GetProperty("messages").EnumerateArray())
        {
            var completa = await tela!.GetFromJsonAsync<JsonElement>($"/api/v1/message/{item.GetProperty("ID").GetString()}", cancelamento);
            mensagens.Add(new EmailCapturado(completa.GetProperty("Subject").GetString()!, completa.GetProperty("Text").GetString()!));
        }

        return mensagens;
    }

    public async ValueTask DisposeAsync()
    {
        tela?.Dispose();
        if (container is not null)
        {
            await container.DisposeAsync();
        }
    }
}

public sealed record EmailCapturado(string Assunto, string Texto);
