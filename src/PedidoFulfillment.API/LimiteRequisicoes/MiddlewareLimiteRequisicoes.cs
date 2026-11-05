using PedidoFulfillment.API.Erros;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.API.LimiteRequisicoes;

/// <summary>
/// Limite de requisições das rotas marcadas (REQ-23). Middleware, e não filtro de endpoint: o corpo
/// é lido antes dos filtros, e um JSON inválido falharia sem contar (R-02). A regra fica no serviço;
/// aqui só há o transporte: ler a origem e o e-mail, e responder 429.
/// </summary>
public sealed class MiddlewareLimiteRequisicoes(RequestDelegate proximo)
{
    /// <summary>Igual em todos os casos: não revela os limites, nem qual foi excedido, nem nada da conta (FR-013).</summary>
    private static readonly Falha Excedido = new(
        TipoFalha.RegraNegocio,
        "Muitas requisições.",
        "Aguarde o tempo indicado no cabeçalho Retry-After e tente de novo.",
        CodigosErro.LimiteRequisicoesExcedido);

    public async Task InvokeAsync(HttpContext contexto, IServicoLimiteRequisicoes servico)
    {
        var metadados = contexto.GetEndpoint()?.Metadata.GetMetadata<MetadadosLimiteRequisicoes>();
        if (metadados is null)
        {
            await proximo(contexto);
            return;
        }

        var cancelamento = contexto.RequestAborted;
        var email = metadados.Rota is RotaLimitada.Login or RotaLimitada.RecuperacaoSenha or RotaLimitada.ReenvioConfirmacao
            ? await LeitorEmailCorpo.LerAsync(contexto.Request, cancelamento)
            : null;
        var decisao = await servico.AvaliarAsync(metadados.Rota, contexto.Connection.RemoteIpAddress, email, cancelamento);
        if (decisao.Aceita)
        {
            await proximo(contexto);
            return;
        }

        contexto.Response.Headers.RetryAfter = decisao.RetryAfterSegundos.ToString(System.Globalization.CultureInfo.InvariantCulture);
        await Excedido.ParaProblema(StatusCodes.Status429TooManyRequests).ExecuteAsync(contexto);
    }
}
