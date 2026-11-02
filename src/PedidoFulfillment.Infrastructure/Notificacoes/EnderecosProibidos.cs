using System.Net;
using System.Net.Sockets;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Endereços que um webhook nunca pode alcançar (REQ-18, FR-008 e FR-016, research R-08): a mesma
/// lista no cadastro e no <c>ConnectCallback</c> do envio, que cobre a troca de DNS depois do cadastro.
/// </summary>
internal static class EnderecosProibidos
{
    private static readonly (IPAddress Rede, int Prefixo)[] FaixasIPv4 =
    [
        (IPAddress.Parse("0.0.0.0"), 8),
        (IPAddress.Parse("10.0.0.0"), 8),
        (IPAddress.Parse("100.64.0.0"), 10),
        (IPAddress.Parse("127.0.0.0"), 8),
        (IPAddress.Parse("169.254.0.0"), 16),
        (IPAddress.Parse("172.16.0.0"), 12),
        (IPAddress.Parse("192.0.0.0"), 24),
        (IPAddress.Parse("192.168.0.0"), 16),
        (IPAddress.Parse("198.18.0.0"), 15),
        (IPAddress.Parse("224.0.0.0"), 4),
        (IPAddress.Parse("240.0.0.0"), 4)
    ];

    private static readonly (IPAddress Rede, int Prefixo)[] FaixasIPv6 =
    [
        (IPAddress.Parse("::"), 128),
        (IPAddress.Parse("::1"), 128),
        (IPAddress.Parse("fc00::"), 7),
        (IPAddress.Parse("fe80::"), 10),
        (IPAddress.Parse("ff00::"), 8),
        (IPAddress.Parse("64:ff9b::"), 96)
    ];

    /// <summary>
    /// Verdadeiro se o endereço é interno (loopback, rede privada, link-local e metadados de nuvem,
    /// CGNAT, multicast, reservado ou o equivalente IPv6). Com <paramref name="permitirRedeLocal"/>,
    /// só nos testes e em Development, nada é proibido.
    /// </summary>
    public static bool Proibido(IPAddress endereco, bool permitirRedeLocal = false)
    {
        if (permitirRedeLocal)
        {
            return false;
        }

        if (endereco.IsIPv4MappedToIPv6)
        {
            endereco = endereco.MapToIPv4();
        }

        var faixas = endereco.AddressFamily == AddressFamily.InterNetwork ? FaixasIPv4 : FaixasIPv6;
        return faixas.Any(f => NaFaixa(endereco, f.Rede, f.Prefixo));
    }

    private static bool NaFaixa(IPAddress endereco, IPAddress rede, int prefixo)
    {
        var a = endereco.GetAddressBytes();
        var b = rede.GetAddressBytes();
        if (a.Length != b.Length)
        {
            return false;
        }

        var bytesInteiros = prefixo / 8;
        for (var i = 0; i < bytesInteiros; i++)
        {
            if (a[i] != b[i])
            {
                return false;
            }
        }

        var resto = prefixo % 8;
        if (resto == 0)
        {
            return true;
        }

        var mascara = (byte)(0xFF << (8 - resto));
        return (a[bytesInteiros] & mascara) == (b[bytesInteiros] & mascara);
    }
}
