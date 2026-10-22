using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Mensageria</c> (REQ-06, data-model). Todos os valores têm padrão.
/// </summary>
public sealed class OpcoesMensageria
{
    public const string Secao = "Mensageria";

    public int IntervaloDespachoMs { get; set; } = 1000;

    public int TamanhoLote { get; set; } = 50;

    /// <summary>
    /// Espera antes de cada uma das 3 novas tentativas do consumidor (FR-012). Sem padrão no
    /// próprio array: o binder de configuração acrescentaria os valores configurados aos padrões.
    /// </summary>
    public int[]? IntervalosNovaTentativaMs { get; set; }

    /// <summary>Os intervalos configurados ou, sem configuração, 1 s, 2 s e 4 s.</summary>
    public IReadOnlyList<int> Intervalos => IntervalosNovaTentativaMs is { Length: > 0 } ? IntervalosNovaTentativaMs : [1000, 2000, 4000];

    public int PrefetchConsumidor { get; set; } = 10;
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesMensageria : IValidateOptions<OpcoesMensageria>
{
    public ValidateOptionsResult Validate(string? name, OpcoesMensageria options)
    {
        var falhas = new List<string>();
        var intervalos = options.Intervalos;
        if (intervalos.Count != 3 || intervalos[0] <= 0 || intervalos.Zip(intervalos.Skip(1)).Any(par => par.Second <= par.First))
        {
            falhas.Add("Mensageria:IntervalosNovaTentativaMs deve ter 3 valores positivos e crescentes.");
        }

        Limitar(options.TamanhoLote, 1, 500, nameof(OpcoesMensageria.TamanhoLote));
        Limitar(options.IntervaloDespachoMs, 100, 60000, nameof(OpcoesMensageria.IntervaloDespachoMs));
        Limitar(options.PrefetchConsumidor, 1, 1000, nameof(OpcoesMensageria.PrefetchConsumidor));
        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);

        void Limitar(int valor, int minimo, int maximo, string chave)
        {
            if (valor < minimo || valor > maximo)
            {
                falhas.Add($"Mensageria:{chave} deve estar entre {minimo} e {maximo}.");
            }
        }
    }
}
