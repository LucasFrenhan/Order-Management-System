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

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (R-10).
        grupo.MapGet("/{id}", async (Guid id, ClaimsPrincipal usuario, IServicoPedidos servico, CancellationToken cancellationToken) =>
                (await servico.ObterAsync(id, usuario.ObterUsuarioId(), usuario.ObterPerfis(), cancellationToken))
                    .ParaResposta(pedido => TypedResults.Ok(pedido)))
            .WithName("ObterPedido")
            .WithSummary("Consulta um pedido pelo identificador.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaConsultaPedido)
            .Produces<PedidoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        grupo.MapPatch("/{id}/status", async (
                Guid id,
                AtualizarStatusPedidoRequestDTO atualizacao,
                ClaimsPrincipal usuario,
                IServicoPedidos servico,
                CancellationToken cancellationToken) =>
                (await servico.AtualizarStatusAsync(id, usuario.ObterUsuarioId(), usuario.ObterPerfis(), atualizacao, cancellationToken))
                    .ParaResposta(pedido => TypedResults.Ok(pedido)))
            .WithName("AtualizarStatusPedido")
            .WithSummary("Altera o status de um pedido conforme as transições permitidas.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaAtualizacaoStatusPedido)
            .AddEndpointFilter<FiltroValidacao<AtualizarStatusPedidoRequestDTO>>()
            .Produces<PedidoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapPatch("/{id}/rastreio", async (
                Guid id,
                RegistrarRastreioRequestDTO rastreio,
                ClaimsPrincipal usuario,
                IServicoPedidos servico,
                CancellationToken cancellationToken) =>
                (await servico.RegistrarRastreioAsync(id, usuario.ObterUsuarioId(), rastreio, cancellationToken))
                    .ParaResposta(pedido => TypedResults.Ok(pedido)))
            .WithName("RegistrarRastreioPedido")
            .WithSummary("Registra a transportadora e o código de rastreio e despacha o pedido (ou corrige o rastreio).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaRastreioPedido)
            .AddEndpointFilter<FiltroValidacao<RegistrarRastreioRequestDTO>>()
            .Produces<PedidoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        return app;
    }
}
