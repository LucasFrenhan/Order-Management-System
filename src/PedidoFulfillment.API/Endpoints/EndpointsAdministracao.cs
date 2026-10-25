using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Administracao;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas administrativas do reenvio da DLQ (REQ-14, contrato), só para Admin. Só registram e
/// consultam o pedido: quem fala com o broker é o processo de workers.
/// </summary>
public static class EndpointsAdministracao
{
    public static WebApplication MapearEndpointsAdministracao(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/admin/dlq").WithTags("Administracao")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaAdministracao);

        grupo.MapPost("/{consumidor}/reenvios", async (
                string consumidor,
                RegistrarReenvioDlqRequestDTO filtro,
                ClaimsPrincipal usuario,
                IServicoReenvioDlq servico,
                CancellationToken cancellationToken) =>
                (await servico.RegistrarAsync(usuario.ObterUsuarioId(), consumidor, filtro, cancellationToken))
                    .ParaResposta(pedido => TypedResults.Accepted($"/api/v1/admin/dlq/reenvios/{pedido.Id}", pedido)))
            .WithName("RegistrarReenvioDlq")
            .WithSummary("Pede o reenvio das mensagens da DLQ de um consumidor; o processo de workers o executa.")
            .AddEndpointFilter<FiltroValidacao<RegistrarReenvioDlqRequestDTO>>()
            .Produces<PedidoReenvioDlqResponseDTO>(StatusCodes.Status202Accepted)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest);

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (REQ-05, R-10).
        grupo.MapGet("/reenvios/{id}", async (Guid id, IServicoReenvioDlq servico, CancellationToken cancellationToken) =>
                (await servico.ObterAsync(id, cancellationToken)).ParaResposta(pedido => TypedResults.Ok(pedido)))
            .WithName("ObterReenvioDlq")
            .WithSummary("Consulta um pedido de reenvio da DLQ, com a situação e as contagens.")
            .Produces<PedidoReenvioDlqResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        return app;
    }
}
