using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// AES-GCM do segredo dos destinos com a chave <c>Notificacoes:ChaveCifra</c> (REQ-18, FR-009,
/// research R-15). Formato: nonce (12) + texto cifrado + tag (16). A API cifra; o Worker decifra.
/// </summary>
internal sealed class CifraSegredos(IOptions<OpcoesNotificacoes> opcoes) : ICifraSegredos
{
    private const int TamanhoNonce = 12;
    private const int TamanhoTag = 16;

    public byte[] Cifrar(byte[] segredo)
    {
        ArgumentNullException.ThrowIfNull(segredo);
        var resultado = new byte[TamanhoNonce + segredo.Length + TamanhoTag];
        var nonce = resultado.AsSpan(0, TamanhoNonce);
        RandomNumberGenerator.Fill(nonce);
        using var aes = new AesGcm(opcoes.Value.ChaveCifraBytes(), TamanhoTag);
        aes.Encrypt(nonce, segredo, resultado.AsSpan(TamanhoNonce, segredo.Length), resultado.AsSpan(TamanhoNonce + segredo.Length));
        return resultado;
    }

    public byte[] Decifrar(byte[] cifrado)
    {
        ArgumentNullException.ThrowIfNull(cifrado);
        if (cifrado.Length <= TamanhoNonce + TamanhoTag)
        {
            throw new CryptographicException("Segredo cifrado com tamanho inválido.");
        }

        var tamanho = cifrado.Length - TamanhoNonce - TamanhoTag;
        var segredo = new byte[tamanho];
        using var aes = new AesGcm(opcoes.Value.ChaveCifraBytes(), TamanhoTag);
        aes.Decrypt(cifrado.AsSpan(0, TamanhoNonce), cifrado.AsSpan(TamanhoNonce, tamanho), cifrado.AsSpan(TamanhoNonce + tamanho), segredo);
        return segredo;
    }
}
