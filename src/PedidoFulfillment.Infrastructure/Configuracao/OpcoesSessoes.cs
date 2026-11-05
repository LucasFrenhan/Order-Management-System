using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>Configuração da seção <c>Sessoes</c> da API (REQ-24, FR-005).</summary>
public sealed class OpcoesSessoes
{
    public const string Secao = "Sessoes";

    public int ValidadeTokenRenovacaoDias { get; set; } = 14;

    public int DuracaoMaximaDias { get; set; } = 30;

    public ConfiguracaoSessoes ParaConfiguracao() =>
        new(TimeSpan.FromDays(ValidadeTokenRenovacaoDias), TimeSpan.FromDays(DuracaoMaximaDias));
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesSessoes : IValidateOptions<OpcoesSessoes>
{
    public ValidateOptionsResult Validate(string? name, OpcoesSessoes options)
    {
        var faixas = new FaixasOpcoes(OpcoesSessoes.Secao);
        faixas.Limitar(options.ValidadeTokenRenovacaoDias, 1, 90, nameof(OpcoesSessoes.ValidadeTokenRenovacaoDias));
        faixas.Limitar(options.DuracaoMaximaDias, 1, 365, nameof(OpcoesSessoes.DuracaoMaximaDias));
        if (options.DuracaoMaximaDias < options.ValidadeTokenRenovacaoDias)
        {
            faixas.Falhar($"{OpcoesSessoes.Secao}:{nameof(OpcoesSessoes.DuracaoMaximaDias)} não pode ser menor que {nameof(OpcoesSessoes.ValidadeTokenRenovacaoDias)}.");
        }

        return faixas.Resultado();
    }
}
