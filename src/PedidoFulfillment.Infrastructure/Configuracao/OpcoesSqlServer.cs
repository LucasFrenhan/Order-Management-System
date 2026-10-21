using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// String de conexão <c>ConnectionStrings:SqlServer</c>, obrigatória em todos os ambientes.
/// </summary>
public sealed class OpcoesSqlServer
{
    public const string NomeConexao = "SqlServer";

    public string StringConexao { get; set; } = "";
}

/// <summary>
/// Falha nomeando a chave, sem mostrar o valor (FR-012).
/// </summary>
public sealed class ValidadorOpcoesSqlServer : IValidateOptions<OpcoesSqlServer>
{
    public ValidateOptionsResult Validate(string? name, OpcoesSqlServer options) =>
        string.IsNullOrWhiteSpace(options.StringConexao)
            ? ValidateOptionsResult.Fail("A configuração ConnectionStrings:SqlServer é obrigatória.")
            : ValidateOptionsResult.Success;
}
