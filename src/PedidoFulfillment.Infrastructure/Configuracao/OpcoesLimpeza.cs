using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Limpeza</c> (REQ-14, R-10). Todos os valores têm padrão; as
/// retenções mínimas protegem a janela de reentrega e a garantia de 24 h das chaves (REQ-05).
/// </summary>
public sealed class OpcoesLimpeza
{
    public const string Secao = "Limpeza";

    /// <summary>Ligada em produção. Os testes de integração a desligam por padrão.</summary>
    public bool Habilitada { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 3600;

    public int TamanhoLote { get; set; } = 1000;

    public int LotesPorRodada { get; set; } = 10;

    public int RetencaoEnviadosHoras { get; set; } = 168;

    public int RetencaoProcessadosHoras { get; set; } = 168;

    public int RetencaoChavesHoras { get; set; } = 48;

    public int RetencaoPedidosReenvioHoras { get; set; } = 2160;

    public TimeSpan Intervalo => TimeSpan.FromSeconds(IntervaloSegundos);

    public TimeSpan RetencaoEnviados => TimeSpan.FromHours(RetencaoEnviadosHoras);

    public TimeSpan RetencaoProcessados => TimeSpan.FromHours(RetencaoProcessadosHoras);

    public TimeSpan RetencaoChaves => TimeSpan.FromHours(RetencaoChavesHoras);

    public TimeSpan RetencaoPedidosReenvio => TimeSpan.FromHours(RetencaoPedidosReenvioHoras);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesLimpeza : IValidateOptions<OpcoesLimpeza>
{
    public ValidateOptionsResult Validate(string? name, OpcoesLimpeza options)
    {
        var faixas = new FaixasOpcoes(OpcoesLimpeza.Secao);
        faixas.Limitar(options.IntervaloSegundos, 60, 86400, nameof(OpcoesLimpeza.IntervaloSegundos));
        faixas.Limitar(options.TamanhoLote, 1, 10000, nameof(OpcoesLimpeza.TamanhoLote));
        faixas.Limitar(options.LotesPorRodada, 1, 1000, nameof(OpcoesLimpeza.LotesPorRodada));
        faixas.Limitar(options.RetencaoEnviadosHoras, 1, 8760, nameof(OpcoesLimpeza.RetencaoEnviadosHoras));
        faixas.Limitar(options.RetencaoProcessadosHoras, 24, 8760, nameof(OpcoesLimpeza.RetencaoProcessadosHoras));
        faixas.Limitar(options.RetencaoChavesHoras, 24, 8760, nameof(OpcoesLimpeza.RetencaoChavesHoras));
        faixas.Limitar(options.RetencaoPedidosReenvioHoras, 24, 87600, nameof(OpcoesLimpeza.RetencaoPedidosReenvioHoras));
        return faixas.Resultado();
    }
}

/// <summary>Configuração da seção <c>ReenvioDlq</c> (REQ-14, R-08 e R-10).</summary>
public sealed class OpcoesReenvioDlq
{
    public const string Secao = "ReenvioDlq";

    /// <summary>Executor dos pedidos de reenvio. Os testes de integração o desligam por padrão.</summary>
    public bool Habilitado { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 60;

    public int MaximoReenvios { get; set; } = 3;

    public int MensagensPorPedido { get; set; } = 1000;

    /// <summary>Aviso de tamanho da DLQ, com chave própria (analyze D1).</summary>
    public bool AvisoHabilitado { get; set; } = true;

    public int AvisoLimite { get; set; } = 10;

    public int AvisoIntervaloSegundos { get; set; } = 300;

    /// <summary>
    /// Leitura da DLQ para a métrica, só com a telemetria ligada (REQ-16, clarify 2); o aviso
    /// continua no máximo a cada <see cref="AvisoIntervaloSegundos"/>.
    /// </summary>
    public int MetricaIntervaloSegundos { get; set; } = 60;

    public TimeSpan Intervalo => TimeSpan.FromSeconds(IntervaloSegundos);

    public TimeSpan AvisoIntervalo => TimeSpan.FromSeconds(AvisoIntervaloSegundos);

    public TimeSpan MetricaIntervalo => TimeSpan.FromSeconds(MetricaIntervaloSegundos);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesReenvioDlq : IValidateOptions<OpcoesReenvioDlq>
{
    public ValidateOptionsResult Validate(string? name, OpcoesReenvioDlq options)
    {
        var faixas = new FaixasOpcoes(OpcoesReenvioDlq.Secao);
        faixas.Limitar(options.IntervaloSegundos, 1, 3600, nameof(OpcoesReenvioDlq.IntervaloSegundos));
        faixas.Limitar(options.MaximoReenvios, 1, 100, nameof(OpcoesReenvioDlq.MaximoReenvios));
        faixas.Limitar(options.MensagensPorPedido, 1, 10000, nameof(OpcoesReenvioDlq.MensagensPorPedido));
        faixas.Limitar(options.AvisoLimite, 1, 1000000, nameof(OpcoesReenvioDlq.AvisoLimite));
        faixas.Limitar(options.AvisoIntervaloSegundos, 10, 86400, nameof(OpcoesReenvioDlq.AvisoIntervaloSegundos));
        faixas.Limitar(options.MetricaIntervaloSegundos, 10, 300, nameof(OpcoesReenvioDlq.MetricaIntervaloSegundos));
        return faixas.Resultado();
    }
}

/// <summary>Faixas de opções inteiras, com a mensagem "Secao:Chave deve estar entre X e Y.".</summary>
internal sealed class FaixasOpcoes(string secao)
{
    private readonly List<string> falhas = [];

    public void Limitar(int valor, int minimo, int maximo, string chave)
    {
        if (valor < minimo || valor > maximo)
        {
            falhas.Add($"{secao}:{chave} deve estar entre {minimo} e {maximo}.");
        }
    }

    /// <summary>Falha que não é faixa inteira (por exemplo, a taxa de amostragem, REQ-16).</summary>
    public void Falhar(string mensagem) => falhas.Add(mensagem);

    public ValidateOptionsResult Resultado() => falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
}
