using Microsoft.Extensions.Options;

namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>
/// Seção <c>Simulacao</c> (REQ-19, research R-03; contrato da API › Configuração): chances, latência
/// e o modo determinístico dos testes. As mesmas regras valem na partida e na rota de controle.
/// </summary>
public sealed class OpcoesSimulacao
{
    public const string Secao = "Simulacao";

    /// <summary>Testes (FR-007): na partida, sem falhas sorteadas e sem latência; só aprova.</summary>
    public bool Deterministico { get; set; }

    /// <summary>Fixa o sorteio, para reproduzir uma sequência.</summary>
    public int? Semente { get; set; }

    public int LatenciaMinimaMs { get; set; } = 40;

    public int LatenciaMaximaMs { get; set; } = 250;

    public double CaudaPercentual { get; set; } = 5;

    public int CaudaMinimoMs { get; set; } = 1000;

    public int CaudaMaximoMs { get; set; } = 5000;

    /// <summary>Chance de aprovação entre as chamadas que efetivam; a recusa é o complemento.</summary>
    public double TaxaAprovacaoPercentual { get; set; } = 90;

    public double IndisponivelPercentual { get; set; } = 2;

    public double LimiteTaxaPercentual { get; set; } = 1;

    public int RetryAfterSegundos { get; set; } = 1;

    public double ErroInternoPercentual { get; set; } = 1;

    public double SemRespostaPercentual { get; set; } = 1;

    public double ProcessarSemResponderPercentual { get; set; } = 1;

    public int SemRespostaMs { get; set; } = 15000;

    public OpcoesSimulacao Copiar() => (OpcoesSimulacao)MemberwiseClone();

    /// <summary>Soma das chances de falha, que não pode passar de 100.</summary>
    public double SomaFalhas =>
        IndisponivelPercentual + LimiteTaxaPercentual + ErroInternoPercentual + SemRespostaPercentual + ProcessarSemResponderPercentual;

    /// <summary>As violações, cada uma nomeando a chave sem o valor (FR-018).</summary>
    public IReadOnlyList<string> Validar()
    {
        var erros = new List<string>();
        Faixa(erros, nameof(LatenciaMinimaMs), LatenciaMinimaMs, 0, 60000);
        Faixa(erros, nameof(LatenciaMaximaMs), LatenciaMaximaMs, 0, 60000);
        if (LatenciaMinimaMs > LatenciaMaximaMs)
        {
            erros.Add($"{Secao}:{nameof(LatenciaMinimaMs)} não pode passar de {Secao}:{nameof(LatenciaMaximaMs)}.");
        }

        Faixa(erros, nameof(CaudaMinimoMs), CaudaMinimoMs, 0, 60000);
        Faixa(erros, nameof(CaudaMaximoMs), CaudaMaximoMs, 0, 60000);
        if (CaudaMinimoMs > CaudaMaximoMs)
        {
            erros.Add($"{Secao}:{nameof(CaudaMinimoMs)} não pode passar de {Secao}:{nameof(CaudaMaximoMs)}.");
        }

        Percentual(erros, nameof(CaudaPercentual), CaudaPercentual);
        Percentual(erros, nameof(TaxaAprovacaoPercentual), TaxaAprovacaoPercentual);
        Percentual(erros, nameof(IndisponivelPercentual), IndisponivelPercentual);
        Percentual(erros, nameof(LimiteTaxaPercentual), LimiteTaxaPercentual);
        Percentual(erros, nameof(ErroInternoPercentual), ErroInternoPercentual);
        Percentual(erros, nameof(SemRespostaPercentual), SemRespostaPercentual);
        Percentual(erros, nameof(ProcessarSemResponderPercentual), ProcessarSemResponderPercentual);
        if (SomaFalhas > 100)
        {
            erros.Add($"A soma das chances de falha de {Secao} não pode passar de 100.");
        }

        Faixa(erros, nameof(RetryAfterSegundos), RetryAfterSegundos, 1, 60);
        Faixa(erros, nameof(SemRespostaMs), SemRespostaMs, 100, 120000);
        return erros;
    }

    private static void Faixa(List<string> erros, string chave, int valor, int minimo, int maximo)
    {
        if (valor < minimo || valor > maximo)
        {
            erros.Add($"{Secao}:{chave} deve estar entre {minimo} e {maximo}.");
        }
    }

    private static void Percentual(List<string> erros, string chave, double valor)
    {
        if (double.IsNaN(valor) || valor < 0 || valor > 100)
        {
            erros.Add($"{Secao}:{chave} deve estar entre 0 e 100.");
        }
    }
}

/// <summary>Validação na partida: um valor fora da faixa impede a API de subir.</summary>
public sealed class ValidadorOpcoesSimulacao : IValidateOptions<OpcoesSimulacao>
{
    public ValidateOptionsResult Validate(string? name, OpcoesSimulacao options) =>
        options.Validar() is { Count: > 0 } erros ? ValidateOptionsResult.Fail(erros) : ValidateOptionsResult.Success;
}
