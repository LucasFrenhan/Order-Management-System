using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>Configuração da seção <c>GatewayPagamento</c> do gateway simulado (REQ-09, R-02).</summary>
public sealed class OpcoesGatewayPagamento
{
    public const string Secao = "GatewayPagamento";

    /// <summary>Chance de aprovação de cada cobrança, de 0 a 100 (FR-004).</summary>
    public double TaxaAprovacaoPercentual { get; set; } = 90;
}

/// <summary>Taxa de 0 a 100 (FR-005), nomeando a chave sem mostrar o valor.</summary>
public sealed class ValidadorOpcoesGatewayPagamento : IValidateOptions<OpcoesGatewayPagamento>
{
    public ValidateOptionsResult Validate(string? name, OpcoesGatewayPagamento options) =>
        options.TaxaAprovacaoPercentual is >= 0 and <= 100
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail($"{OpcoesGatewayPagamento.Secao}:{nameof(OpcoesGatewayPagamento.TaxaAprovacaoPercentual)} deve estar entre 0 e 100.");
}
