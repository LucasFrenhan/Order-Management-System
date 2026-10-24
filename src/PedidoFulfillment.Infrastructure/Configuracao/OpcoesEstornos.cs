using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Estornos</c> (REQ-13, R-09). Todos os valores têm padrão.
/// </summary>
public sealed class OpcoesEstornos
{
    public const string Secao = "Estornos";

    /// <summary>
    /// Ligado em produção (FR-002). Os testes de integração o desligam por padrão: muitos semeiam
    /// pedidos cancelados com pagamento, que o processador tocaria.
    /// </summary>
    public bool Habilitado { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 60;

    public int TamanhoLote { get; set; } = 100;

    public int LimiteAlertaSegundos { get; set; } = 86400;

    public int TentativasAlerta { get; set; } = 3;

    public TimeSpan Intervalo => TimeSpan.FromSeconds(IntervaloSegundos);

    public TimeSpan LimiteAlerta => TimeSpan.FromSeconds(LimiteAlertaSegundos);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesEstornos : IValidateOptions<OpcoesEstornos>
{
    public ValidateOptionsResult Validate(string? name, OpcoesEstornos options)
    {
        var falhas = new List<string>();
        Limitar(options.IntervaloSegundos, 1, 3600, nameof(OpcoesEstornos.IntervaloSegundos));
        Limitar(options.TamanhoLote, 1, 1000, nameof(OpcoesEstornos.TamanhoLote));
        Limitar(options.LimiteAlertaSegundos, 1, 2592000, nameof(OpcoesEstornos.LimiteAlertaSegundos));
        Limitar(options.TentativasAlerta, 1, 100, nameof(OpcoesEstornos.TentativasAlerta));
        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);

        void Limitar(int valor, int minimo, int maximo, string chave)
        {
            if (valor < minimo || valor > maximo)
            {
                falhas.Add($"{OpcoesEstornos.Secao}:{chave} deve estar entre {minimo} e {maximo}.");
            }
        }
    }
}
