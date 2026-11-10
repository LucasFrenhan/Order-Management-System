using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Estornos;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Estornos pela operação (REQ-28): listagem e consulta para o Admin e o OperadorLogistica
/// (<c>PoliticaEstornos</c>). Fora de <c>/admin</c>, que é só do Admin (R-10). Só roteiam: validam,
/// delegam ao serviço e traduzem o resultado.
/// </summary>
public static class EndpointsEstornos
{
    public static WebApplication MapearEndpointsEstornos(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/estornos").WithTags("Estornos");

        grupo.MapGet("/pendentes", async (
                [AsParameters] ParametrosListarEstornos parametros,
                ClaimsPrincipal usuario,
                IServicoAcoesEstornos servico,
                CancellationToken cancellationToken) =>
                (await servico.ListarPendentesAsync(parametros.ParaRequisicao(), usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(pagina => TypedResults.Ok(pagina)))
            .WithName("ListarEstornosPendentes")
            .WithSummary("Lista os estornos pendentes, do pedido de estorno mais antigo para o mais novo, com tentativas, último erro e alerta.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaEstornos)
            .AddEndpointFilter<FiltroValidacao<ParametrosListarEstornos>>()
            .AddOpenApiOperationTransformer(ParametrosListagemEstornosOpenApi.AplicarAsync)
            .Produces<PaginaEstornosResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest);

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (REQ-05, R-10).
        grupo.MapGet("/{id}", async (Guid id, IServicoAcoesEstornos servico, CancellationToken cancellationToken) =>
                (await servico.ObterAsync(id, cancellationToken)).ParaResposta(estorno => TypedResults.Ok(estorno)))
            .WithName("ObterEstorno")
            .WithSummary("Consulta um estorno pendente, confirmado pelo gateway ou baixado manualmente.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaEstornos)
            .Produces<EstornoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        return app;
    }
}
