using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Chave do cursor da listagem derivada por HKDF-SHA256 da <c>Jwt:ChaveAssinatura</c> (REQ-21,
/// research R-04): a mesma em todas as instâncias, sem configuração nova e sem usar a chave do
/// JWT diretamente. Trocar a chave do JWT (#60) invalida os cursores em uso (400). Pode passar a
/// uma chave própria do cursor: basta registrar outra <see cref="IChaveCursor"/>.
/// </summary>
public sealed class ChaveCursorDerivadaDoJwt(IOptions<OpcoesJwt> jwt) : IChaveCursor
{
    private static readonly byte[] Contexto = "oms/listagem-pedidos/cursor/v1"u8.ToArray();

    private readonly Lazy<byte[]> chave = new(() =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(jwt.Value.ChaveAssinatura), 32, info: Contexto));

    public byte[] Chave => chave.Value;
}
