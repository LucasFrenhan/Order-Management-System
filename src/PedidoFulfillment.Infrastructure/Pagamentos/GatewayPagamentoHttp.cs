using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Observabilidade;
using Polly.CircuitBreaker;
using Polly.Timeout;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// O gateway de pagamento pela rede (REQ-19, FR-009; contrato do cliente): cobra, estorna e
/// consulta na API de pagamento com o pedido como <c>Idempotency-Key</c>. As novas tentativas, os
/// tempos limite e o circuito ficam no pipeline de resiliência do <see cref="HttpClient"/>
/// (<see cref="ExtensoesPagamento"/>); aqui se lê a resposta final e se traduz o que não é
/// resultado em exceção (research R-07). Nada aqui grava: sem resultado, o chamador não grava.
/// </summary>
internal sealed class GatewayPagamentoHttp(HttpClient http, EstadoGatewayPagamento estado, TimeProvider relogio, ILogger<GatewayPagamentoHttp> logger)
    : IGatewayPagamento
{
    public const string CabecalhoChaveApi = "X-Api-Key";
    public const string CabecalhoChave = "Idempotency-Key";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<ResultadoCobranca> CobrarAsync(Guid pedidoId, decimal valor, CancellationToken cancellationToken)
    {
        using var requisicao = Requisicao(HttpMethod.Post, "v1/cobrancas", pedidoId, OperacaoGateway.Cobranca);
        requisicao.Content = JsonContent.Create(new { valor }, options: Json);
        var resultado = await ChamarAsync(requisicao, pedidoId, OperacaoGateway.Cobranca, TelemetriaOms.Spans.CobrarPagamento,
            ClassificacaoRespostaGateway.LerCobranca, r => r.Aprovado ? TelemetriaOms.Resultados.Aprovado : TelemetriaOms.Resultados.Recusado, cancellationToken);
        return resultado;
    }

    public async Task<ResultadoEstorno> EstornarAsync(Guid pedidoId, string identificadorTransacao, decimal valor, CancellationToken cancellationToken)
    {
        using var requisicao = Requisicao(HttpMethod.Post, "v1/estornos", pedidoId, OperacaoGateway.Estorno);
        requisicao.Content = JsonContent.Create(new { identificadorCobranca = identificadorTransacao, valor }, options: Json);
        return await ChamarAsync(requisicao, pedidoId, OperacaoGateway.Estorno, TelemetriaOms.Spans.EstornarPagamento,
            ClassificacaoRespostaGateway.LerEstorno, r => r.Confirmado ? TelemetriaOms.Resultados.Confirmado : TelemetriaOms.Resultados.EstornoRecusado, cancellationToken);
    }

    public async Task<ConsultaCobranca> ConsultarCobrancaAsync(Guid pedidoId, CancellationToken cancellationToken)
    {
        using var requisicao = Requisicao(HttpMethod.Get, $"v1/cobrancas/{pedidoId}", pedidoId, OperacaoGateway.Consulta);
        return await ChamarAsync(requisicao, pedidoId, OperacaoGateway.Consulta, TelemetriaOms.Spans.ConsultarCobranca,
            ClassificacaoRespostaGateway.LerConsulta,
            c => c.Situacao switch
            {
                SituacaoConsultaCobranca.Aprovada => TelemetriaOms.Resultados.Aprovado,
                SituacaoConsultaCobranca.Recusada => TelemetriaOms.Resultados.Recusado,
                _ => TelemetriaOms.Resultados.NaoEncontrada
            },
            cancellationToken,
            naoEncontrada: ConsultaCobranca.NaoEncontrada);
    }

    private static HttpRequestMessage Requisicao(HttpMethod metodo, string rota, Guid pedidoId, OperacaoGateway operacao)
    {
        var requisicao = new HttpRequestMessage(metodo, rota);
        if (operacao != OperacaoGateway.Consulta)
        {
            // A mesma chave em toda tentativa, reentrega, reconciliação e reinício (FR-017).
            requisicao.Headers.Add(CabecalhoChave, pedidoId.ToString());
        }

        requisicao.Options.Set(MarcadorTentativaHandler.ChavePedido, pedidoId);
        requisicao.Options.Set(MarcadorTentativaHandler.ChaveOperacao, operacao);
        return requisicao;
    }

    /// <summary>
    /// Envia pelo pipeline, lê a resposta final e mede a chamada (span, métricas). Com o circuito
    /// aberto, a chamada nem sai: <see cref="GatewayPagamentoIndisponivelException"/> (FR-016a).
    /// </summary>
    private async Task<T> ChamarAsync<T>(
        HttpRequestMessage requisicao,
        Guid pedidoId,
        OperacaoGateway operacao,
        string nomeSpan,
        Func<CorpoOperacaoGateway?, T> ler,
        Func<T, string> resultadoDe,
        CancellationToken cancellationToken,
        T? naoEncontrada = null)
        where T : class
    {
        using var span = TelemetriaOms.Fonte.StartActivity(nomeSpan, ActivityKind.Internal);
        span?.SetTag(TelemetriaOms.Atributos.PedidoId, pedidoId.ToString());
        span?.SetTag(TelemetriaOms.Atributos.PagamentoOperacao, NomeOperacao(operacao));
        var inicio = relogio.GetTimestamp();
        var resultado = TelemetriaOms.Resultados.FalhaPassageira;
        string? tipoFalha = null;
        try
        {
            using var resposta = await EnviarAsync(requisicao, operacao, cancellationToken);
            var classe = ClassificacaoRespostaGateway.Classificar(resposta);
            if (classe == ClasseResposta.NaoEncontrada && naoEncontrada is not null)
            {
                resultado = resultadoDe(naoEncontrada);
                return naoEncontrada;
            }

            if (classe == ClasseResposta.Resultado)
            {
                T lido;
                try
                {
                    lido = ler(await LerCorpoAsync(resposta, operacao, cancellationToken));
                }
                catch (FalhaGatewayPagamentoException)
                {
                    EventosGatewayPagamento.ErroPermanente(logger, operacao, pedidoId, (int)resposta.StatusCode, "corpo-fora-do-contrato");
                    throw;
                }

                resultado = resultadoDe(lido);
                return lido;
            }

            if (classe == ClasseResposta.Passageira)
            {
                tipoFalha = ClassificacaoRespostaGateway.TipoFalha(resposta.StatusCode);
                throw new FalhaGatewayPagamentoException(TipoFalhaGateway.Passageira, operacao,
                    $"HTTP {(int)resposta.StatusCode} ({tipoFalha}) depois de {MarcadorTentativaHandler.Tentativas(requisicao)} tentativa(s).");
            }

            var codigo = await LerCodigoAsync(resposta, cancellationToken);
            EventosGatewayPagamento.ErroPermanente(logger, operacao, pedidoId, (int)resposta.StatusCode, codigo);
            resultado = TelemetriaOms.Resultados.ErroPermanente;
            throw new FalhaGatewayPagamentoException(TipoFalhaGateway.Permanente, operacao, $"HTTP {(int)resposta.StatusCode} ({codigo}).");
        }
        catch (FalhaGatewayPagamentoException excecao) when (excecao.Tipo == TipoFalhaGateway.Permanente)
        {
            resultado = TelemetriaOms.Resultados.ErroPermanente;
            span.MarcarErro(nameof(FalhaGatewayPagamentoException));
            throw;
        }
        catch (FalhaGatewayPagamentoException)
        {
            span.MarcarErro(nameof(FalhaGatewayPagamentoException));
            throw;
        }
        catch (GatewayPagamentoIndisponivelException)
        {
            resultado = TelemetriaOms.Resultados.CircuitoAberto;
            span.MarcarErro(nameof(GatewayPagamentoIndisponivelException));
            throw;
        }
        catch (TimeoutRejectedException excecao) when (!cancellationToken.IsCancellationRequested)
        {
            // Tempo da tentativa (última) ou o total esgotado: resultado incerto, nada gravado (FR-013).
            resultado = TelemetriaOms.Resultados.Incerto;
            tipoFalha = ClassificacaoRespostaGateway.TipoTempoEsgotado;
            span.MarcarErro(nameof(TimeoutRejectedException));
            throw new FalhaGatewayPagamentoException(TipoFalhaGateway.Passageira, operacao,
                $"tempo esgotado depois de {MarcadorTentativaHandler.Tentativas(requisicao)} tentativa(s).", excecao);
        }
        catch (HttpRequestException excecao) when (!cancellationToken.IsCancellationRequested)
        {
            tipoFalha = ClassificacaoRespostaGateway.TipoConexao;
            span.MarcarErro(nameof(HttpRequestException));
            throw new FalhaGatewayPagamentoException(TipoFalhaGateway.Passageira, operacao,
                $"falha de conexão depois de {MarcadorTentativaHandler.Tentativas(requisicao)} tentativa(s).", excecao);
        }
        finally
        {
            span?.SetTag(TelemetriaOms.Atributos.Resultado, resultado);
            span?.SetTag(TelemetriaOms.Atributos.PagamentoTentativas, MarcadorTentativaHandler.Tentativas(requisicao));
            var atributos = new TagList
            {
                { TelemetriaOms.Atributos.PagamentoOperacao, NomeOperacao(operacao) },
                { TelemetriaOms.Atributos.Resultado, resultado }
            };
            if (tipoFalha is not null)
            {
                atributos.Add(TelemetriaOms.Atributos.PagamentoTipoFalha, tipoFalha);
            }

            TelemetriaOms.ChamadasPagamento.Add(1, atributos);
            TelemetriaOms.DuracaoPagamento.Record(relogio.GetElapsedTime(inicio).TotalSeconds, atributos);
        }
    }

    private async Task<HttpResponseMessage> EnviarAsync(HttpRequestMessage requisicao, OperacaoGateway operacao, CancellationToken cancellationToken)
    {
        try
        {
            return await http.SendAsync(requisicao, cancellationToken);
        }
        catch (BrokenCircuitException excecao)
        {
            var disponivelEm = estado.DisponivelEm is { } ate && ate > relogio.GetUtcNow()
                ? ate
                : relogio.GetUtcNow() + TimeSpan.FromSeconds(1);
            throw new GatewayPagamentoIndisponivelException(disponivelEm, excecao);
        }
    }

    private static async Task<CorpoOperacaoGateway?> LerCorpoAsync(HttpResponseMessage resposta, OperacaoGateway operacao, CancellationToken cancellationToken)
    {
        try
        {
            return await resposta.Content.ReadFromJsonAsync<CorpoOperacaoGateway>(Json, cancellationToken);
        }
        catch (JsonException)
        {
            throw ClassificacaoRespostaGateway.ForaDoContrato(operacao);
        }
    }

    private static async Task<string> LerCodigoAsync(HttpResponseMessage resposta, CancellationToken cancellationToken)
    {
        try
        {
            var problema = await resposta.Content.ReadFromJsonAsync<ProblemaGateway>(Json, cancellationToken);
            return problema?.Codigo ?? (resposta.StatusCode == HttpStatusCode.Unauthorized ? "credencial-recusada" : "sem-codigo");
        }
        catch (Exception excecao) when (excecao is JsonException or NotSupportedException or InvalidOperationException)
        {
            return resposta.StatusCode == HttpStatusCode.Unauthorized ? "credencial-recusada" : "sem-codigo";
        }
    }

    public static string NomeOperacao(OperacaoGateway operacao) => operacao switch
    {
        OperacaoGateway.Cobranca => "cobranca",
        OperacaoGateway.Estorno => "estorno",
        _ => "consulta"
    };
}
