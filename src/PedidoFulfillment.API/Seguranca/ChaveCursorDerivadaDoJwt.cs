using System.Security.Cryptography;
using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Chave do cursor da listagem derivada por HKDF-SHA256 da chave ativa do JWT (REQ-21, research R-04; REQ-27).
/// Provisória: o REQ-27 a substitui por uma chave própria opcional (specs/027-rotacao-chave-jwt, R-06).
/// </summary>
public sealed class ChaveCursorDerivadaDoJwt(ConjuntoChavesJwt chaves) : IChaveCursor
{
    private static readonly byte[] Contexto = "oms/listagem-pedidos/cursor/v1"u8.ToArray();

    private readonly Lazy<byte[]> chave = new(() => HKDF.DeriveKey(HashAlgorithmName.SHA256, chaves.ValorAtiva, 32, info: Contexto));

    public byte[] Chave => chave.Value;
}
