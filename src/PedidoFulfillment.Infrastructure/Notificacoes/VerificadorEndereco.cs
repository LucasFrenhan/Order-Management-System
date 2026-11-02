using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Conferência do host no cadastro (REQ-18, FR-008): resolve o nome e recusa se <b>qualquer</b>
/// endereço for interno, ou se não resolver. O envio confere de novo, na conexão (FR-016).
/// </summary>
internal sealed class VerificadorEndereco(IOptions<OpcoesNotificacoes> opcoes, ILogger<VerificadorEndereco> logger) : IVerificadorEndereco
{
    public async Task<bool> EnderecoPublicoAsync(Uri url, CancellationToken cancellationToken)
    {
        IPAddress[] enderecos;
        try
        {
            enderecos = IPAddress.TryParse(url.IdnHost, out var literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(url.IdnHost, cancellationToken);
        }
        catch (SocketException)
        {
            return false;
        }

        var proibido = enderecos.FirstOrDefault(e => EnderecosProibidos.Proibido(e, opcoes.Value.PermitirRedeLocal));
        if (proibido is not null)
        {
            EventosNotificacao.EnderecoRecusado(logger, url.GetLeftPart(UriPartial.Authority), proibido.ToString());
            return false;
        }

        return enderecos.Length > 0;
    }
}
