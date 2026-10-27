using System.Diagnostics;
using PedidoFulfillment.API.Seguranca;

namespace PedidoFulfillment.API.Observabilidade;

/// <summary>
/// Marca o span da requisição com o usuário e o pedido (REQ-16, FR-001; contrato › Atributos
/// acrescentados): <c>user.id</c> é o identificador do token, nunca o e-mail, e
/// <c>oms.pedido.id</c> vem da rota ou, na criação, do <c>Location</c> da resposta. Não muda a
/// resposta nem o comportamento; sem telemetria, não há span e nada é feito.
/// </summary>
public static class AtributosRequisicao
{
    private const string AtributoUsuario = "user.id";
    private const string AtributoPedido = "oms.pedido.id";
    private const string RotaPedidos = "/api/v1/pedidos";

    /// <summary>Depois do <c>UseAuthentication</c>, para o usuário já estar lido.</summary>
    public static IApplicationBuilder UsarAtributosRequisicao(this IApplicationBuilder app) => app.Use(async (contexto, proximo) =>
    {
        if (Activity.Current is not { IsAllDataRequested: true } span)
        {
            await proximo(contexto);
            return;
        }

        if (contexto.User.ObterUsuarioId() is var usuarioId && usuarioId != Guid.Empty)
        {
            span.SetTag(AtributoUsuario, usuarioId.ToString());
        }

        var ehPedidos = contexto.Request.Path.StartsWithSegments(RotaPedidos);
        if (ehPedidos && contexto.Request.RouteValues.TryGetValue("id", out var id) && Guid.TryParse(id?.ToString(), out var pedidoId))
        {
            span.SetTag(AtributoPedido, pedidoId.ToString());
        }

        await proximo(contexto);

        // Criação: o id só existe depois; o Location é /api/v1/pedidos/{id}.
        if (ehPedidos && contexto.Response.StatusCode == StatusCodes.Status201Created
            && Guid.TryParse(contexto.Response.Headers.Location.ToString().Split('/').LastOrDefault(), out var criado))
        {
            span.SetTag(AtributoPedido, criado.ToString());
        }
    });
}
