using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Observabilidade</c> (REQ-16, data-model). Todos os valores têm
/// padrão; o destino vem das variáveis padrão do OpenTelemetry (<c>OTEL_*</c>).
/// </summary>
public sealed class OpcoesObservabilidade
{
    public const string Secao = "Observabilidade";

    /// <summary>Variável padrão do OpenTelemetry com o destino OTLP; preenchida, liga a telemetria.</summary>
    public const string ChaveDestino = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Liga o SDK sem destino (os testes coletam em memória, sem exportar).</summary>
    public bool Habilitada { get; set; }

    /// <summary>Razão da amostragem na origem do trace (FR-022).</summary>
    public double TaxaAmostragem { get; set; } = 1.0;

    public int IntervaloAmostragemOutboxSegundos { get; set; } = 30;

    public TimeSpan IntervaloAmostragemOutbox => TimeSpan.FromSeconds(IntervaloAmostragemOutboxSegundos);

    /// <summary>Sem destino configurado e sem a chave, a telemetria fica desligada (FR-020).</summary>
    public static bool Ligada(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration[ChaveDestino]) || configuration.GetValue<bool>($"{Secao}:{nameof(Habilitada)}");

    public static bool TemDestino(IConfiguration configuration) => !string.IsNullOrWhiteSpace(configuration[ChaveDestino]);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesObservabilidade : IValidateOptions<OpcoesObservabilidade>
{
    public ValidateOptionsResult Validate(string? name, OpcoesObservabilidade options)
    {
        var faixas = new FaixasOpcoes(OpcoesObservabilidade.Secao);
        // Comparação invertida para recusar NaN, que falha em qualquer comparação.
        if (!(options.TaxaAmostragem >= 0 && options.TaxaAmostragem <= 1))
        {
            faixas.Falhar($"{OpcoesObservabilidade.Secao}:{nameof(OpcoesObservabilidade.TaxaAmostragem)} deve estar entre 0 e 1.");
        }

        faixas.Limitar(options.IntervaloAmostragemOutboxSegundos, 5, 300, nameof(OpcoesObservabilidade.IntervaloAmostragemOutboxSegundos));
        return faixas.Resultado();
    }
}
