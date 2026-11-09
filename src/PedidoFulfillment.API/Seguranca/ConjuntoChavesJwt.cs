using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// As chaves do JWT montadas uma vez a partir de <see cref="OpcoesJwt"/> já validadas (REQ-27, research R-03):
/// a ativa emite, e todas, a ativa inclusive, validam. Usado pela emissão, pela validação, pela chave dos cursores
/// e pelo log da partida, para as quatro pontas nunca divergirem.
/// </summary>
public sealed class ConjuntoChavesJwt
{
    private readonly Dictionary<string, SymmetricSecurityKey> porIdentificador;

    public ConjuntoChavesJwt(IOptions<OpcoesJwt> opcoes)
    {
        var jwt = opcoes.Value;
        porIdentificador = new Dictionary<string, SymmetricSecurityKey>(StringComparer.Ordinal);
        var todas = new List<SymmetricSecurityKey>();
        foreach (var chave in jwt.Chaves)
        {
            var simetrica = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave.Valor)) { KeyId = chave.Identificador };
            porIdentificador[chave.Identificador] = simetrica;
            todas.Add(simetrica);
        }

        Todas = todas;
        Ativa = porIdentificador[jwt.ChaveAtiva];
        ValorAtiva = Encoding.UTF8.GetBytes(jwt.Chaves.First(c => string.Equals(c.Identificador, jwt.ChaveAtiva, StringComparison.Ordinal)).Valor);
        IdentificadoresAceitos = jwt.Chaves.Select(c => c.Identificador)
            .Where(i => !string.Equals(i, jwt.ChaveAtiva, StringComparison.Ordinal)).ToArray();
    }

    /// <summary>A chave que assina os tokens novos, com <c>KeyId</c> igual ao identificador.</summary>
    public SymmetricSecurityKey Ativa { get; }

    /// <summary>As chaves aceitas na validação, a ativa inclusive, na ordem da configuração.</summary>
    public IReadOnlyList<SymmetricSecurityKey> Todas { get; }

    /// <summary>Os identificadores das chaves que só validam, na ordem da configuração.</summary>
    public IReadOnlyList<string> IdentificadoresAceitos { get; }

    /// <summary>Bytes UTF-8 do valor da chave ativa, base da chave dos cursores sem chave própria (R-06).</summary>
    public byte[] ValorAtiva { get; }

    public bool Contem(string? identificador) => identificador is not null && porIdentificador.ContainsKey(identificador);

    /// <summary>A chave do identificador, em comparação ordinal; nenhuma se não estiver configurada.</summary>
    public SecurityKey? Buscar(string? identificador) =>
        identificador is not null && porIdentificador.TryGetValue(identificador, out var chave) ? chave : null;
}
