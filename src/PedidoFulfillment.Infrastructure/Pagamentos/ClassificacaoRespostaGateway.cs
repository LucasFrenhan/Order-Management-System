using System.Net;
using PedidoFulfillment.Application.Pagamentos;
using Polly;
using Polly.Timeout;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>Como uma resposta do gateway é tratada (REQ-19, FR-010; contrato do cliente › Classificação).</summary>
internal enum ClasseResposta
{
    /// <summary><c>2xx</c> com o corpo do contrato: aprovação, recusa, confirmação.</summary>
    Resultado = 1,

    /// <summary><c>404</c> na consulta: a chave não existe (resultado, não falha).</summary>
    NaoEncontrada,

    /// <summary>Repetir pode resolver: <c>5xx</c>, <c>429</c> e <c>409</c> de operação em andamento.</summary>
    Passageira,

    /// <summary>Repetir não resolve: <c>400</c>, <c>401</c>, <c>403</c>, <c>409</c> de chave reutilizada e o resto.</summary>
    Permanente
}

/// <summary>Corpo de uma cobrança ou estorno devolvido pela API (contrato da API).</summary>
internal sealed record CorpoOperacaoGateway(string? Situacao, string? Identificador, string? Motivo);

/// <summary>A extensão <c>codigo</c> dos <c>ProblemDetails</c> da API.</summary>
internal sealed record ProblemaGateway(string? Codigo);

/// <summary>
/// A tabela de classificação do contrato do cliente (REQ-19, research R-07), sem estado: usada
/// pelo pipeline de resiliência (o que repetir e o que conta para o circuito) e pelo
/// <see cref="GatewayPagamentoHttp"/> (a resposta final).
/// </summary>
internal static class ClassificacaoRespostaGateway
{
    public const string TipoIndisponivel = "indisponivel";
    public const string TipoLimiteTaxa = "limite-taxa";
    public const string TipoErroInterno = "erro-interno";
    public const string TipoEmAndamento = "em-andamento";
    public const string TipoConexao = "conexao";
    public const string TipoTempoEsgotado = "tempo-esgotado";

    /// <summary>
    /// A API só manda <c>Retry-After</c> num <c>409</c> quando a operação está em andamento; o de
    /// chave reutilizada não tem (contrato da API), e assim o pipeline decide sem ler o corpo.
    /// </summary>
    public static ClasseResposta Classificar(HttpStatusCode status, bool temRetryAfter) => status switch
    {
        >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices => ClasseResposta.Resultado,
        HttpStatusCode.NotFound => ClasseResposta.NaoEncontrada,
        HttpStatusCode.InternalServerError or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
            or HttpStatusCode.GatewayTimeout or HttpStatusCode.TooManyRequests => ClasseResposta.Passageira,
        HttpStatusCode.Conflict when temRetryAfter => ClasseResposta.Passageira,
        _ => ClasseResposta.Permanente
    };

    public static ClasseResposta Classificar(HttpResponseMessage resposta) =>
        Classificar(resposta.StatusCode, resposta.Headers.RetryAfter is not null);

    /// <summary>Falha passageira ou tempo esgotado: repetir com a mesma chave (FR-011, FR-013).</summary>
    public static bool Repetir(Outcome<HttpResponseMessage> resultado) => resultado switch
    {
        { Exception: TimeoutRejectedException or HttpRequestException } => true,
        { Result: { } resposta } => Classificar(resposta) == ClasseResposta.Passageira,
        _ => false
    };

    /// <summary>
    /// O que conta como falha para o circuito (FR-016): as passageiras, menos a operação em
    /// andamento, que é a própria API respondendo. Resultados e erros permanentes não contam.
    /// </summary>
    public static bool ContaParaCircuito(Outcome<HttpResponseMessage> resultado) => resultado switch
    {
        { Exception: TimeoutRejectedException or HttpRequestException } => true,
        { Result: { StatusCode: not HttpStatusCode.Conflict } resposta } => Classificar(resposta) == ClasseResposta.Passageira,
        _ => false
    };

    /// <summary>Tempo esgotado: a API pode ter efetivado sem a resposta chegar (FR-010).</summary>
    public static bool Incerto(Outcome<HttpResponseMessage> resultado) => resultado.Exception is TimeoutRejectedException;

    /// <summary>O tipo da falha passageira para os logs e as métricas (log 7013).</summary>
    public static string TipoFalha(Outcome<HttpResponseMessage> resultado) => resultado switch
    {
        { Exception: TimeoutRejectedException } => TipoTempoEsgotado,
        { Exception: HttpRequestException } => TipoConexao,
        { Result: { } resposta } => TipoFalha(resposta.StatusCode),
        _ => TipoConexao
    };

    public static string TipoFalha(HttpStatusCode status) => status switch
    {
        HttpStatusCode.ServiceUnavailable => TipoIndisponivel,
        HttpStatusCode.TooManyRequests => TipoLimiteTaxa,
        HttpStatusCode.Conflict => TipoEmAndamento,
        _ => TipoErroInterno
    };

    /// <summary>Aprovada ou recusada; qualquer outra coisa está fora do contrato.</summary>
    public static ResultadoCobranca LerCobranca(CorpoOperacaoGateway? corpo) => corpo switch
    {
        { Situacao: "aprovada", Identificador: { Length: > 0 } identificador } => ResultadoCobranca.Aprovada(identificador),
        { Situacao: "recusada", Motivo: { Length: > 0 } motivo } => ResultadoCobranca.Recusada(motivo),
        _ => throw ForaDoContrato(OperacaoGateway.Cobranca)
    };

    public static ResultadoEstorno LerEstorno(CorpoOperacaoGateway? corpo) => corpo switch
    {
        { Situacao: "confirmado", Identificador: { Length: > 0 } identificador } => ResultadoEstorno.Confirmar(identificador),
        { Situacao: "recusado", Motivo: { Length: > 0 } motivo } => ResultadoEstorno.Recusar(motivo),
        _ => throw ForaDoContrato(OperacaoGateway.Estorno)
    };

    public static ConsultaCobranca LerConsulta(CorpoOperacaoGateway? corpo) => corpo switch
    {
        { Situacao: "aprovada", Identificador: { Length: > 0 } identificador } => ConsultaCobranca.Aprovada(identificador),
        { Situacao: "recusada", Motivo: { Length: > 0 } motivo } => ConsultaCobranca.Recusada(motivo),
        _ => throw ForaDoContrato(OperacaoGateway.Consulta)
    };

    public static FalhaGatewayPagamentoException ForaDoContrato(OperacaoGateway operacao) =>
        new(TipoFalhaGateway.Permanente, operacao, "resposta fora do contrato.");
}
