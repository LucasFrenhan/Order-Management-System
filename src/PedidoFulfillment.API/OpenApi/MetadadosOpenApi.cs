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

/// <summary>
/// Indica que o 400 do endpoint também pode ser o <c>TOKEN_INVALIDO</c>, com o schema de negócio,
/// além do erro de validação (REQ-24, contrato › Mensagens).
/// </summary>
public sealed record MetadadosErroToken;

public static class ExtensoesMetadadosOpenApi
{
    public static TBuilder ComErroToken<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosErroToken());

    public static TBuilder ProduzErros<TBuilder>(this TBuilder builder, params int[] codigos)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosRespostasErro(codigos));

    public static TBuilder ComCabecalhoLocation<TBuilder>(this TBuilder builder)
        where TBuilder : IEndpointConventionBuilder =>
        builder.WithMetadata(new MetadadosCabecalhoLocation());
}
