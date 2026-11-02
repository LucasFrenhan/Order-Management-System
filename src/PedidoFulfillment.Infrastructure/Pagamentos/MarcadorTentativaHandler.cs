using System.Diagnostics;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Conta as tentativas de uma chamada ao gateway e marca cada span de cliente HTTP com
/// <c>oms.pagamento.tentativa</c> (REQ-19, FR-020, research R-12). Fica dentro do handler de
/// resiliência, então roda uma vez por tentativa.
/// </summary>
/// <remarks>
/// O span de cliente HTTP nativo (fonte <c>System.Net.Http</c>) nasce abaixo de todos os
/// <see cref="DelegatingHandler"/>, então o número da tentativa vai por um
/// <see cref="AsyncLocal{T}"/> que um <see cref="ActivityListener"/> lê quando o span começa. O
/// ouvinte não amostra nada: só põe o atributo no span que o OpenTelemetry já decidiu gravar.
/// </remarks>
internal sealed class MarcadorTentativaHandler : DelegatingHandler
{
    public const string FonteHttpCliente = "System.Net.Http";

    public static readonly HttpRequestOptionsKey<Guid> ChavePedido = new("oms.pagamento.pedido");
    public static readonly HttpRequestOptionsKey<OperacaoGateway> ChaveOperacao = new("oms.pagamento.operacao");
    public static readonly HttpRequestOptionsKey<int> ChaveTentativas = new("oms.pagamento.tentativas");

    private static readonly AsyncLocal<int> TentativaAtual = new();

    private static readonly ActivityListener Ouvinte = CriarOuvinte();

    /// <summary>Tentativas feitas até agora na chamada desta requisição.</summary>
    public static int Tentativas(HttpRequestMessage requisicao) =>
        requisicao.Options.TryGetValue(ChaveTentativas, out var tentativas) ? tentativas : 0;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        GC.KeepAlive(Ouvinte);
        var tentativa = Tentativas(request) + 1;
        request.Options.Set(ChaveTentativas, tentativa);
        TentativaAtual.Value = tentativa;
        try
        {
            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            TentativaAtual.Value = 0;
        }
    }

    private static ActivityListener CriarOuvinte()
    {
        var ouvinte = new ActivityListener
        {
            ShouldListenTo = fonte => fonte.Name == FonteHttpCliente,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.None,
            ActivityStarted = span =>
            {
                if (TentativaAtual.Value is > 0 and var tentativa)
                {
                    span.SetTag(TelemetriaOms.Atributos.PagamentoTentativa, tentativa);
                }
            }
        };
        ActivitySource.AddActivityListener(ouvinte);
        return ouvinte;
    }
}
