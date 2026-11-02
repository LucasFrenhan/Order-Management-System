using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Resultado de uma chamada ao receptor: o código HTTP, se houve resposta, e o tipo do erro.</summary>
internal sealed record ResultadoEnvio(int? CodigoHttp, string? Erro)
{
    public bool Sucesso => CodigoHttp is >= 200 and < 300;
}

/// <summary>O host resolveu para um endereço interno na hora da conexão (REQ-18, FR-016).</summary>
internal sealed class EnderecoRecusadoException(string host, IPAddress endereco)
    : IOException($"O host {host} resolve para um endereço interno ({endereco}).")
{
    public string Host { get; } = host;

    public IPAddress Endereco { get; } = endereco;
}

/// <summary>
/// O <c>POST</c> de um webhook (REQ-18, FR-013, FR-014 e FR-016): cliente próprio, sem seguir
/// redirecionamento, sem propagar o <c>traceparent</c> e conferindo o IP na conexão. Não lê o corpo
/// da resposta.
/// </summary>
internal sealed class EnviadorWebhook(IHttpClientFactory fabrica, TimeProvider relogio)
{
    public const string NomeCliente = "oms-webhooks";

    public const string ErroTempoEsgotado = "tempo esgotado";
    public const string ErroEnderecoRecusado = "endereço recusado";
    public const string ErroConexao = "falha de conexão";
    public const string ErroSegredoIlegivel = "segredo ilegível";

    public async Task<ResultadoEnvio> EnviarAsync(string url, byte[] segredo, Guid idNotificacao, string corpo, CancellationToken cancellationToken)
    {
        var id = idNotificacao.ToString();
        var timestamp = AssinaturaWebhook.Timestamp(relogio.GetUtcNow());
        using var requisicao = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(corpo, Encoding.UTF8, "application/json")
        };
        requisicao.Headers.Add(AssinaturaWebhook.CabecalhoId, id);
        requisicao.Headers.Add(AssinaturaWebhook.CabecalhoTimestamp, timestamp);
        requisicao.Headers.Add(AssinaturaWebhook.CabecalhoAssinatura, AssinaturaWebhook.Assinar(segredo, id, timestamp, corpo));

        try
        {
            using var resposta = await fabrica.CreateClient(NomeCliente)
                .SendAsync(requisicao, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new ResultadoEnvio((int)resposta.StatusCode, null);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ResultadoEnvio(null, ErroTempoEsgotado);
        }
        catch (HttpRequestException excecao) when (excecao.InnerException is EnderecoRecusadoException)
        {
            return new ResultadoEnvio(null, ErroEnderecoRecusado);
        }
        catch (HttpRequestException)
        {
            return new ResultadoEnvio(null, ErroConexao);
        }
    }

    /// <summary>O handler do cliente: sem redirecionamento, sem proxy, sem <c>traceparent</c> e com o IP conferido.</summary>
    public static SocketsHttpHandler Handler(bool permitirRedeLocal, Action<string, IPAddress>? aoRecusar = null) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        // O receptor é de fora: o contexto do trace não sai do OMS (FR-027).
        ActivityHeadersPropagator = DistributedContextPropagator.CreateNoOutputPropagator(),
        ConnectCallback = async (contexto, cancellationToken) =>
        {
            var host = contexto.DnsEndPoint.Host;
            var enderecos = IPAddress.TryParse(host, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(host, cancellationToken);
            if (enderecos.FirstOrDefault(e => EnderecosProibidos.Proibido(e, permitirRedeLocal)) is { } proibido)
            {
                aoRecusar?.Invoke(host, proibido);
                throw new EnderecoRecusadoException(host, proibido);
            }

            var socket = new Socket(SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(enderecos, contexto.DnsEndPoint.Port, cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }
    };
}
