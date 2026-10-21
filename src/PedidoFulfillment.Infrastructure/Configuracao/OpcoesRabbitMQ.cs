using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>RabbitMQ</c>, obrigatória em todos os ambientes.
/// </summary>
public sealed class OpcoesRabbitMQ
{
    public const string Secao = "RabbitMQ";

    public string Servidor { get; set; } = "";

    public int Porta { get; set; }

    public string Usuario { get; set; } = "";

    public string Senha { get; set; } = "";

    public string VirtualHost { get; set; } = "";
}

/// <summary>
/// Acumula todas as falhas, nomeando cada chave sem mostrar valores (FR-012).
/// </summary>
public sealed class ValidadorOpcoesRabbitMQ : IValidateOptions<OpcoesRabbitMQ>
{
    public ValidateOptionsResult Validate(string? name, OpcoesRabbitMQ options)
    {
        var falhas = new List<string>();
        Exigir(options.Servidor, nameof(OpcoesRabbitMQ.Servidor));
        if (options.Porta is < 1 or > 65535)
        {
            falhas.Add("RabbitMQ:Porta deve estar entre 1 e 65535.");
        }

        Exigir(options.Usuario, nameof(OpcoesRabbitMQ.Usuario));
        Exigir(options.Senha, nameof(OpcoesRabbitMQ.Senha));
        Exigir(options.VirtualHost, nameof(OpcoesRabbitMQ.VirtualHost));
        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);

        void Exigir(string valor, string campo)
        {
            if (string.IsNullOrWhiteSpace(valor))
            {
                falhas.Add($"A configuração RabbitMQ:{campo} é obrigatória.");
            }
        }
    }
}
