using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.PagamentosSimulados.Operacoes;

/// <summary>Corpo de <c>POST /v1/cobrancas</c>.</summary>
public sealed record CorpoCobranca(decimal? Valor);

/// <summary>Corpo de <c>POST /v1/estornos</c>.</summary>
public sealed record CorpoEstorno(string? IdentificadorCobranca, decimal? Valor);

/// <summary>Cobrança guardada, como a API a devolve.</summary>
public sealed record RespostaCobranca(Guid Chave, string? Identificador, string Situacao, string? Motivo, decimal Valor, DateTimeOffset? ProcessadaEm);

/// <summary>Estorno guardado, como a API o devolve.</summary>
public sealed record RespostaEstorno(Guid Chave, string? Identificador, string Situacao, string? Motivo, decimal Valor, DateTimeOffset? ProcessadoEm);

/// <summary>
/// Cobrança, estorno e consulta pela chave (REQ-19, FR-004 a FR-006; contrato da API). Em toda
/// chamada, o comportamento é decidido antes de consultar a memória (research R-03): uma falha
/// simulada nunca cria, remove nem altera uma operação; as que efetivam passam pela memória
/// idempotente (R-04).
/// </summary>
public sealed class ServicoPagamentosSimulados(
    ArmazenamentoOperacoes armazenamento, ControleSimulacao controle, TimeProvider relogio, ILogger<ServicoPagamentosSimulados> logger)
{
    public const string CabecalhoChave = "Idempotency-Key";
    public const string CabecalhoRepeticao = "Idempotent-Replayed";
    public const string MotivoRecusaCobranca = "Pagamento recusado pelo emissor.";
    public const string MotivoCobrancaDesconhecida = "Cobrança desconhecida.";
    public const string MotivoValorDiferente = "Valor diferente do cobrado.";

    public Task<IResult> CobrarAsync(HttpContext contexto, CorpoCobranca? corpo)
    {
        var erros = new Dictionary<string, string[]>();
        var chave = LerChave(contexto, erros);
        var valor = LerValor(corpo?.Valor, erros);
        return erros.Count > 0
            ? Task.FromResult(Results.ValidationProblem(erros))
            : ExecutarAsync(contexto, TipoOperacao.Cobranca, chave, valor, null);
    }

    public Task<IResult> EstornarAsync(HttpContext contexto, CorpoEstorno? corpo)
    {
        var erros = new Dictionary<string, string[]>();
        var chave = LerChave(contexto, erros);
        var valor = LerValor(corpo?.Valor, erros);
        if (string.IsNullOrWhiteSpace(corpo?.IdentificadorCobranca))
        {
            erros["identificadorCobranca"] = ["O identificador da cobrança é obrigatório."];
        }

        return erros.Count > 0
            ? Task.FromResult(Results.ValidationProblem(erros))
            : ExecutarAsync(contexto, TipoOperacao.Estorno, chave, valor, corpo!.IdentificadorCobranca);
    }

    /// <summary>
    /// <c>GET /v1/cobrancas/{chave}</c> e <c>/v1/estornos/{chave}</c>: sofre a latência, mas não as
    /// falhas sorteadas; o roteiro e o modo forçado valem (contrato › Consulta).
    /// </summary>
    public async Task<IResult> ConsultarAsync(HttpContext contexto, TipoOperacao tipo, string chaveTexto)
    {
        if (!Guid.TryParse(chaveTexto, out var chave))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["chave"] = ["A chave deve ser um GUID."] });
        }

        var decisao = controle.Decidir(tipo);
        await EsperarAsync(decisao.LatenciaMs, contexto.RequestAborted);
        if (decisao.Comportamento.EhFalha() && !decisao.Sorteado)
        {
            return await FalharAsync(contexto, tipo, chave, decisao.Comportamento);
        }

        if (armazenamento.Obter(tipo, chave) is not { } operacao)
        {
            return Problema(StatusCodes.Status404NotFound, "Operação não encontrada.", "nao-encontrada");
        }

        return operacao.Concluida ? Results.Ok(Corpo(operacao)) : EmAndamento(contexto);
    }

    private async Task<IResult> ExecutarAsync(HttpContext contexto, TipoOperacao tipo, Guid chave, decimal valor, string? identificadorCobranca)
    {
        var decisao = controle.Decidir(tipo);
        if (decisao.Comportamento.EhFalha())
        {
            armazenamento.ContarChamada(tipo, chave);
            await EsperarAsync(decisao.LatenciaMs, contexto.RequestAborted);
            return await FalharAsync(contexto, tipo, chave, decisao.Comportamento);
        }

        var registro = armazenamento.Registrar(tipo, chave, valor, identificadorCobranca);
        var operacao = registro.Operacao;
        if (!registro.Nova)
        {
            await EsperarAsync(decisao.LatenciaMs, contexto.RequestAborted);
            return await RepetirAsync(contexto, operacao, valor, identificadorCobranca, decisao);
        }

        // Uma API real conclui o que começou mesmo que o cliente desista: a latência da operação
        // nova não é cancelada pela desconexão (é assim que nasce o resultado incerto).
        await EsperarAsync(decisao.LatenciaMs, CancellationToken.None);
        Concluir(operacao, decisao.Comportamento);
        Logs.OperacaoProcessada(logger, Nome(tipo), chave, Situacao(operacao), decisao.Comportamento.Nome(), decisao.LatenciaMs);
        if (decisao.Comportamento == Comportamento.ProcessarSemResponder && !await SegurarRespostaAsync(contexto))
        {
            return Results.Empty;
        }

        return Results.Json(Corpo(operacao), statusCode: StatusCodes.Status201Created);
    }

    private async Task<IResult> RepetirAsync(
        HttpContext contexto, OperacaoPagamento operacao, decimal valor, string? identificadorCobranca, DecisaoComportamento decisao)
    {
        if (operacao.Valor != valor || !string.Equals(operacao.IdentificadorCobranca, identificadorCobranca, StringComparison.Ordinal))
        {
            Logs.ChaveReutilizada(logger, Nome(operacao.Tipo), operacao.Chave);
            return Problema(StatusCodes.Status409Conflict, "A chave já foi usada com outro corpo.", "chave-reutilizada");
        }

        if (!operacao.Concluida)
        {
            return EmAndamento(contexto);
        }

        if (decisao.Comportamento == Comportamento.ProcessarSemResponder && !await SegurarRespostaAsync(contexto))
        {
            return Results.Empty;
        }

        Logs.RepeticaoIdempotente(logger, Nome(operacao.Tipo), operacao.Chave, Situacao(operacao), operacao.Chamadas);
        contexto.Response.Headers[CabecalhoRepeticao] = "true";
        return Results.Ok(Corpo(operacao));
    }

    /// <summary>Aprova, recusa ou, no estorno, aplica as duas regras de recusa (contrato › Estorno).</summary>
    private void Concluir(OperacaoPagamento operacao, Comportamento comportamento)
    {
        var agora = relogio.GetUtcNow();
        if (operacao.Tipo == TipoOperacao.Estorno)
        {
            var cobranca = armazenamento.CobrancaAprovada(operacao.IdentificadorCobranca!);
            if (cobranca is null)
            {
                operacao.Recusar(MotivoCobrancaDesconhecida, agora);
            }
            else if (cobranca.Valor != operacao.Valor)
            {
                operacao.Recusar(MotivoValorDiferente, agora);
            }
            else
            {
                operacao.Efetivar(agora);
            }

            return;
        }

        if (comportamento == Comportamento.Recusar)
        {
            operacao.Recusar(MotivoRecusaCobranca, agora);
        }
        else
        {
            operacao.Efetivar(agora);
        }
    }

    private async Task<IResult> FalharAsync(HttpContext contexto, TipoOperacao tipo, Guid chave, Comportamento comportamento)
    {
        Logs.FalhaSimulada(logger, Nome(tipo), chave, comportamento.Nome());
        switch (comportamento)
        {
            case Comportamento.Indisponivel:
                return Problema(StatusCodes.Status503ServiceUnavailable, "Serviço indisponível.", "indisponivel");
            case Comportamento.LimiteTaxa:
                contexto.Response.Headers.RetryAfter = controle.Parametros.RetryAfterSegundos.ToString(CultureInfo.InvariantCulture);
                return Problema(StatusCodes.Status429TooManyRequests, "Limite de requisições excedido.", "limite-taxa");
            case Comportamento.SemResposta:
                return await SegurarRespostaAsync(contexto)
                    ? Problema(StatusCodes.Status504GatewayTimeout, "Sem resposta do processador.", "sem-resposta")
                    : Results.Empty;
            default:
                return Problema(StatusCodes.Status500InternalServerError, "Erro interno.", "erro-interno");
        }
    }

    /// <summary>Segura a resposta por <c>SemRespostaMs</c>; falso se o cliente desistiu antes.</summary>
    private async Task<bool> SegurarRespostaAsync(HttpContext contexto)
    {
        try
        {
            await Task.Delay(controle.Parametros.SemRespostaMs, contexto.RequestAborted);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static async Task EsperarAsync(int latenciaMs, CancellationToken cancellationToken)
    {
        if (latenciaMs <= 0)
        {
            return;
        }

        try
        {
            await Task.Delay(latenciaMs, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // O cliente desistiu; a resposta não chega a ninguém.
        }
    }

    private static IResult EmAndamento(HttpContext contexto)
    {
        contexto.Response.Headers.RetryAfter = "1";
        return Problema(StatusCodes.Status409Conflict, "A operação com esta chave ainda está em processamento.", "operacao-em-andamento");
    }

    private static Guid LerChave(HttpContext contexto, Dictionary<string, string[]> erros)
    {
        if (Guid.TryParse(contexto.Request.Headers[CabecalhoChave].ToString(), out var chave))
        {
            return chave;
        }

        erros[CabecalhoChave] = ["O cabeçalho Idempotency-Key é obrigatório e deve ser um GUID."];
        return Guid.Empty;
    }

    private static decimal LerValor(decimal? valor, Dictionary<string, string[]> erros)
    {
        if (valor is not { } informado || informado <= 0 || decimal.Round(informado, 2) != informado)
        {
            erros["valor"] = ["O valor é obrigatório, maior que zero e com até duas casas decimais."];
            return 0;
        }

        return informado;
    }

    public static IResult Problema(int status, string titulo, string codigo) =>
        Results.Problem(statusCode: status, title: titulo, extensions: new Dictionary<string, object?> { ["codigo"] = codigo });

    public static object Corpo(OperacaoPagamento operacao) => operacao.Tipo == TipoOperacao.Cobranca
        ? new RespostaCobranca(operacao.Chave, operacao.Identificador, Situacao(operacao), operacao.Motivo, operacao.Valor, operacao.ProcessadaEm)
        : new RespostaEstorno(operacao.Chave, operacao.Identificador, Situacao(operacao), operacao.Motivo, operacao.Valor, operacao.ProcessadaEm);

    public static string Situacao(OperacaoPagamento operacao) => operacao.Situacao switch
    {
        SituacaoOperacao.Aprovada => "aprovada",
        SituacaoOperacao.Recusada => "recusada",
        SituacaoOperacao.Confirmado => "confirmado",
        SituacaoOperacao.Recusado => "recusado",
        _ => "em-andamento"
    };

    public static string Nome(TipoOperacao tipo) => tipo == TipoOperacao.Cobranca ? "cobranca" : "estorno";
}
