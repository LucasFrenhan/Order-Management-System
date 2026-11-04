using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.API.LimiteRequisicoes;

/// <summary>Liga uma rota às políticas do limite de requisições (REQ-23, FR-005).</summary>
public sealed record MetadadosLimiteRequisicoes(RotaLimitada Rota);

public static class ExtensoesMetadadosLimiteRequisicoes
{
    public static TBuilder ComLimiteRequisicoes<TBuilder>(this TBuilder builder, RotaLimitada rota)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosLimiteRequisicoes(rota));
}
