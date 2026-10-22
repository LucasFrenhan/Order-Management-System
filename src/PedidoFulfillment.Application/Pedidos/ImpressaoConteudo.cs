using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using PedidoFulfillment.Application.Contratos.Pedidos;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Impressão do conteúdo de uma criação (FR-008): SHA-256 do texto canônico, com os itens
/// ordenados por produto, no formato <c>produtoId:quantidade</c> separados por <c>;</c>
/// (<c>contracts/idempotencia.md</c>).
/// </summary>
public static class ImpressaoConteudo
{
    public static string Calcular(CriarPedidoRequestDTO pedido)
    {
        var canonico = string.Join(';', pedido.Itens
            .OrderBy(i => i.ProdutoId)
            .Select(i => string.Create(CultureInfo.InvariantCulture, $"{i.ProdutoId:D}:{i.Quantidade}")));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonico)));
    }
}
