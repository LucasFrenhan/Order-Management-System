using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.PagamentosSimulados.Seguranca;

/// <summary>
/// Autenticação por chave (REQ-19, FR-002 e FR-007a, research R-05): <c>X-Api-Key</c> nas rotas
/// <c>/v1</c> e <c>X-Chave-Controle</c> nas rotas <c>/controle</c>; <c>/health</c> sem chave. Vem
/// antes de qualquer comportamento simulado, e a recusa é igual em todos os casos.
/// </summary>
internal sealed class AutenticacaoChaves(RequestDelegate proximo, IOptions<OpcoesSeguranca> opcoes, ILogger<AutenticacaoChaves> logger)
{
    public const string CabecalhoChaveApi = "X-Api-Key";
    public const string CabecalhoChaveControle = "X-Chave-Controle";
    public const string TituloRecusa = "Credencial inválida.";

    private readonly byte[] chaveApi = Encoding.UTF8.GetBytes(opcoes.Value.ChaveApi);
    private readonly byte[] chaveControle = Encoding.UTF8.GetBytes(opcoes.Value.ChaveControle);

    public async Task InvokeAsync(HttpContext contexto)
    {
        if (Exigida(contexto.Request.Path) is not { } exigida)
        {
            await proximo(contexto);
            return;
        }

        var (cabecalho, esperada) = exigida;
        if (Confere(contexto.Request.Headers[cabecalho].ToString(), esperada))
        {
            await proximo(contexto);
            return;
        }

        Logs.CredencialRecusada(logger, contexto.Request.Method, contexto.Request.Path.Value ?? "");
        await Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: TituloRecusa).ExecuteAsync(contexto);
    }

    /// <summary>Comparação em tempo constante (<see cref="CryptographicOperations.FixedTimeEquals"/>).</summary>
    public static bool Confere(string? recebida, byte[] esperada) =>
        !string.IsNullOrEmpty(recebida) && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebida), esperada);

    private (string Cabecalho, byte[] Esperada)? Exigida(PathString rota)
    {
        if (rota.StartsWithSegments("/v1", StringComparison.OrdinalIgnoreCase))
        {
            return (CabecalhoChaveApi, chaveApi);
        }

        if (rota.StartsWithSegments("/controle", StringComparison.OrdinalIgnoreCase))
        {
            return (CabecalhoChaveControle, chaveControle);
        }

        return null;
    }
}
