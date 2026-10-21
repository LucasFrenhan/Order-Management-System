using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas de pedidos. Só roteiam: validam, delegam ao serviço e traduzem o resultado.
/// </summary>
public static class EndpointsPedidos
{
    public const string CabecalhoIdempotencia = "X-Idempotency-Key";

    public static WebApplication MapearEndpointsPedidos(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/pedidos").WithTags("Pedidos");

        grupo.MapPost("", async (
                [FromHeader(Name = CabecalhoIdempotencia)] Guid chaveIdempotencia,
                CriarPedidoRequestDTO pedido,
                ClaimsPrincipal usuario,
                IServicoPedidos servico,
                CancellationToken cancellationToken) =>
                (await servico.CriarAsync(usuario.ObterUsuarioId(), chaveIdempotencia, pedido, cancellationToken))
                    .ParaResposta(criado => TypedResults.Created($"/api/v1/pedidos/{criado.Id}", criado)))
            .WithName("CriarPedido")
            .WithSummary("Cria um pedido para o Cliente autenticado (idempotente por X-Idempotency-Key).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCliente)
            .AddEndpointFilter<FiltroValidacao<CriarPedidoRequestDTO>>()
            .Produces<PedidoResponseDTO>(StatusCodes.Status201Created)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict, StatusCodes.Status422UnprocessableEntity);

        return app;
    }
}
