using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Chave dos cursores das listagens de pedidos, produtos e usuários, por HKDF-SHA256 (REQ-21 R-04; REQ-27 R-06).
/// Vem da <c>Listagens:ChaveCursor</c> quando configurada, e a rotação do JWT não a afeta; sem ela, vem da chave
/// ativa do JWT, e a troca da ativa invalida os cursores em uso (400). O contexto do HKDF é o mesmo desde o REQ-21:
/// com o mesmo valor da antiga <c>Jwt:ChaveAssinatura</c> na chave ativa, os cursores continuam válidos.
/// </summary>
public sealed class ChaveCursorListagens(IOptions<OpcoesListagens> listagens, ConjuntoChavesJwt chavesJwt) : IChaveCursor
{
    private static readonly byte[] Contexto = "oms/listagem-pedidos/cursor/v1"u8.ToArray();

    private readonly Lazy<byte[]> chave = new(() =>
    {
        var propria = listagens.Value.ChaveCursor;
        var origem = propria.Length > 0 ? Encoding.UTF8.GetBytes(propria) : chavesJwt.ValorAtiva;
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, origem, 32, info: Contexto);
    });

    public byte[] Chave => chave.Value;
}
