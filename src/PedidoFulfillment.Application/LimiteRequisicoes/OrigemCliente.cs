using System.Net;
using System.Net.Sockets;

namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// A origem que forma a contagem (FR-004, R-06): IPv4 pelo endereço exato, IPv4 mapeado em IPv6
/// como IPv4 e IPv6 pelo bloco <c>/64</c>, porque um único cliente costuma controlar o bloco
/// inteiro. Sem endereço, todos dividem uma só contagem: nunca deixa de limitar.
/// </summary>
public static class OrigemCliente
{
    public const string Desconhecida = "desconhecida";

    private const int BytesDoBloco64 = 8;

    public static string Normalizar(IPAddress? endereco)
    {
        if (endereco is null)
        {
            return Desconhecida;
        }

        if (endereco.IsIPv4MappedToIPv6)
        {
            return endereco.MapToIPv4().ToString();
        }

        if (endereco.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return endereco.ToString();
        }

        var bytes = endereco.GetAddressBytes();
        Array.Clear(bytes, BytesDoBloco64, bytes.Length - BytesDoBloco64);
        return new IPAddress(bytes).ToString();
    }
}
