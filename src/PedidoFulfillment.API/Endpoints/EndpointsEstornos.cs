using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Estornos;
using PedidoFulfillment.Application.Pagamentos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Estornos pela operação (REQ-28): listagem, consulta e tentativa forçada para o Admin e o OperadorLogistica
/// (<c>PoliticaEstornos</c>), e baixa só para o Admin ativo (<c>PoliticaBaixaEstorno</c>). Fora de
/// <c>/admin</c>, que é só do Admin (R-10). Só roteiam: validam,
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

        // Só registra: a rodada do Worker faz a tentativa, primeiro da fila (FR-011, R-04).
        grupo.MapPost("/{id}/tentativas", async (Guid id, ClaimsPrincipal usuario, IServicoAcoesEstornos servico, CancellationToken cancellationToken) =>
                (await servico.PedirTentativaAsync(usuario.ObterUsuarioId(), id, cancellationToken))
                    .ParaResposta(estorno => TypedResults.Accepted($"/api/v1/estornos/{estorno.EstornoId}", estorno)))
            .WithName("PedirTentativaEstorno")
            .WithSummary("Pede uma nova tentativa do estorno pendente; a rodada de estornos a faz antes dos demais.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaEstornos)
            .Produces<EstornoResponseDTO>(StatusCodes.Status202Accepted)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        // Só o Admin ainda ativo no banco (FR-020); espera a rodada até 5 s (FR-009).
        grupo.MapPost("/{id}/baixa", async (
                Guid id,
                BaixaEstornoRequestDTO baixa,
                ClaimsPrincipal usuario,
                IServicoAcoesEstornos servico,
                CancellationToken cancellationToken) =>
                (await servico.DarBaixaAsync(usuario.ObterUsuarioId(), id, baixa, cancellationToken))
                    .ParaResposta(estorno => TypedResults.Ok(estorno)))
            .WithName("DarBaixaEstorno")
            .WithSummary("Dá baixa num estorno pendente resolvido fora do sistema, com motivo; o gateway não é chamado.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaBaixaEstorno)
            .AddEndpointFilter<FiltroValidacao<BaixaEstornoRequestDTO>>()
            .Produces<EstornoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        return app;
    }
}
