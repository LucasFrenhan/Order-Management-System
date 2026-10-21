namespace PedidoFulfillment.API.OpenApi;

/// <summary>
/// Códigos de erro que um endpoint declara no contrato, além dos automáticos
/// (401/403 para rotas protegidas e 500 para todas).
/// </summary>
public sealed record MetadadosRespostasErro(IReadOnlyList<int> Codigos);

/// <summary>
/// Indica que a resposta 201 do endpoint traz o cabeçalho <c>Location</c>.
/// </summary>
public sealed record MetadadosCabecalhoLocation;

public static class ExtensoesMetadadosOpenApi
{
    public static TBuilder ProduzErros<TBuilder>(this TBuilder builder, params int[] codigos)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosRespostasErro(codigos));

    public static TBuilder ComCabecalhoLocation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosCabecalhoLocation());
}
