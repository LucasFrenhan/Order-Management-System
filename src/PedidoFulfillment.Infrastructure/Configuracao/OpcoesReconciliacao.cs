using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Reconciliacao</c> (REQ-12, R-10). Todos os valores têm padrão.
/// </summary>
public sealed class OpcoesReconciliacao
{
    public const string Secao = "Reconciliacao";

    /// <summary>
    /// Ligada em produção (FR-007). Os testes de integração a desligam por padrão: muitos semeiam
    /// pedidos com datas fixas, que pareceriam parados.
    /// </summary>
    public bool Habilitada { get; set; } = true;

    public int IntervaloCicloSegundos { get; set; } = 300;

    public int LimiteParadoSegundos { get; set; } = 600;

    public int LimiteEsgotamentoSegundos { get; set; } = 86400;

    public int CiclosFalhaEsgotamento { get; set; } = 3;

    public int TamanhoLote { get; set; } = 100;

    public TimeSpan IntervaloCiclo => TimeSpan.FromSeconds(IntervaloCicloSegundos);

    public TimeSpan LimiteParado => TimeSpan.FromSeconds(LimiteParadoSegundos);

    public TimeSpan LimiteEsgotamento => TimeSpan.FromSeconds(LimiteEsgotamentoSegundos);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesReconciliacao : IValidateOptions<OpcoesReconciliacao>
{
    public ValidateOptionsResult Validate(string? name, OpcoesReconciliacao options)
    {
        var falhas = new List<string>();
        Limitar(options.IntervaloCicloSegundos, 1, 86400, nameof(OpcoesReconciliacao.IntervaloCicloSegundos));
        Limitar(options.LimiteParadoSegundos, 1, 604800, nameof(OpcoesReconciliacao.LimiteParadoSegundos));
        Limitar(options.LimiteEsgotamentoSegundos, 1, 2592000, nameof(OpcoesReconciliacao.LimiteEsgotamentoSegundos));
        Limitar(options.CiclosFalhaEsgotamento, 1, 100, nameof(OpcoesReconciliacao.CiclosFalhaEsgotamento));
        Limitar(options.TamanhoLote, 1, 1000, nameof(OpcoesReconciliacao.TamanhoLote));
        if (options.LimiteEsgotamentoSegundos <= options.LimiteParadoSegundos)
        {
            falhas.Add("Reconciliacao:LimiteEsgotamentoSegundos deve ser maior que Reconciliacao:LimiteParadoSegundos.");
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);

        void Limitar(int valor, int minimo, int maximo, string chave)
        {
            if (valor < minimo || valor > maximo)
            {
                falhas.Add($"Reconciliacao:{chave} deve estar entre {minimo} e {maximo}.");
            }
        }
    }
}
