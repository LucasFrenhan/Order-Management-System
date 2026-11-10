using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>
/// Último erro do estorno guardado para a operação (REQ-28, R-05): só categorias fechadas, nunca a
/// mensagem da exceção, que pode trazer endereço, corpo de resposta ou identificadores do gateway.
/// </summary>
public static class DescricaoFalhaEstorno
{
    private const string PrefixoRecusa = "Recusado: ";

    /// <summary>O texto do motivo do gateway é cortado para caber com o prefixo.</summary>
    public const int TamanhoMaximoMotivo = 180;

    public static string Recusado(string? motivo)
    {
        var texto = string.IsNullOrWhiteSpace(motivo) ? "sem motivo informado." : motivo.Trim();
        return PrefixoRecusa + (texto.Length > TamanhoMaximoMotivo ? texto[..TamanhoMaximoMotivo] : texto);
    }

    public static string Falha(Exception excecao) => excecao switch
    {
        FalhaGatewayPagamentoException { Tipo: TipoFalhaGateway.Passageira } => "Falha passageira do gateway.",
        FalhaGatewayPagamentoException => "Falha permanente do gateway.",
        _ => $"Erro inesperado: {excecao.GetType().Name}."
    };

    /// <summary>Garante o tamanho da coluna mesmo com um nome de tipo muito longo.</summary>
    internal static string Limitar(string texto) =>
        texto.Length > Pagamento.TamanhoMaximoUltimoErroEstorno ? texto[..Pagamento.TamanhoMaximoUltimoErroEstorno] : texto;
}
