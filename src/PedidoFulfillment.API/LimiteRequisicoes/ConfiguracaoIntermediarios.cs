using System.Net;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.API.LimiteRequisicoes;

/// <summary>
/// Cliente real por trás de intermediários confiáveis (REQ-23, FR-004, R-06). Só o
/// <c>X-Forwarded-For</c>; <c>Host</c> e <c>Proto</c> não mudam.
/// </summary>
public static class ConfiguracaoIntermediarios
{
    /// <summary>
    /// Sem intermediário configurado, o middleware nem entra: com as listas vazias, o
    /// <c>ForwardedHeaders</c> aceitaria o cabeçalho de qualquer um, e o limite seria burlado com
    /// um endereço forjado.
    /// </summary>
    public static WebApplication UsarIntermediariosConfiaveis(this WebApplication app)
    {
        var intermediarios = app.Services.GetRequiredService<IOptions<OpcoesLimiteRequisicoes>>().Value.IntermediariosConfiaveis;
        if (intermediarios.Count > 0)
        {
            app.UseForwardedHeaders(Opcoes(intermediarios));
        }

        return app;
    }

    /// <summary>
    /// As listas padrão confiam em loopback, então são limpas. Sem limite de saltos, a cadeia é lida
    /// da direita para a esquerda até o primeiro endereço não confiável, que é o cliente real: um
    /// valor forjado à esquerda dele é ignorado.
    /// </summary>
    internal static ForwardedHeadersOptions Opcoes(IEnumerable<string> intermediarios)
    {
        var opcoes = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = null
        };
        opcoes.KnownProxies.Clear();
        opcoes.KnownIPNetworks.Clear();

        foreach (var intermediario in intermediarios)
        {
            if (IPAddress.TryParse(intermediario, out var endereco))
            {
                opcoes.KnownProxies.Add(endereco);
            }
            else
            {
                opcoes.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(intermediario));
            }
        }

        return opcoes;
    }
}
