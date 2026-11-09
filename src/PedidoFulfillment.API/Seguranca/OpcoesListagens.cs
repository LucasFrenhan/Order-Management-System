using System.Text;
using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Seção <c>Listagens</c> (REQ-27, research R-06): a chave própria, opcional, dos cursores das listagens de
/// pedidos, produtos e usuários. Com ela, rotacionar a chave do JWT não invalida os cursores em uso.
/// </summary>
public sealed class OpcoesListagens
{
    public const string Secao = "Listagens";

    /// <summary>Opcional; preenchida, no mínimo 32 bytes em UTF-8. Secreta, nunca exibida.</summary>
    public string ChaveCursor { get; set; } = "";
}

public static class ConfiguracaoListagens
{
    /// <summary>Registra a chave dos cursores das listagens, com a validação da chave própria na partida.</summary>
    public static IServiceCollection AdicionarChaveCursor(this IServiceCollection services)
    {
        services.AddOptions<OpcoesListagens>()
            .BindConfiguration(OpcoesListagens.Secao)
            .Validate(o => o.ChaveCursor.Length == 0 || Encoding.UTF8.GetByteCount(o.ChaveCursor) >= ValidadorOpcoesJwt.MinimoBytesChave,
                "Listagens:ChaveCursor deve ter ao menos 32 bytes.")
            .ValidateOnStart();
        services.AddSingleton<IChaveCursor, ChaveCursorListagens>();
        return services;
    }
}
