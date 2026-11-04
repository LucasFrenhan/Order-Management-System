using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>Instâncias da API e chamadas às duas rotas, para os testes do limite de requisições (REQ-23).</summary>
internal static class CenarioLimite
{
    public const string RotaLogin = "/api/v1/auth/login";
    public const string RotaRegistro = "/api/v1/auth/register";
    public const string SenhaValida = "SenhaForte1";
    public const string SenhaErrada = "SenhaErrada1";
    public const string JanelaLonga = "3600";

    public static readonly IPAddress Origem = IPAddress.Parse("203.0.113.7");
    public static readonly IPAddress OutraOrigem = IPAddress.Parse("203.0.113.8");

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Limites da política dada, com a janela dada, e altos nas outras.</summary>
    public static Dictionary<string, string?> Limites(
        int? loginOrigemEmail = null, int? loginOrigem = null, int? registro = null, string janela = JanelaLonga)
    {
        Dictionary<string, string?> valores = new()
        {
            ["LimiteRequisicoes:LoginOrigemEmail:JanelaSegundos"] = janela,
            ["LimiteRequisicoes:LoginOrigem:JanelaSegundos"] = janela,
            ["LimiteRequisicoes:Registro:JanelaSegundos"] = janela
        };
        if (loginOrigemEmail is { } email)
        {
            valores["LimiteRequisicoes:LoginOrigemEmail:Limite"] = email.ToString(CultureInfo.InvariantCulture);
        }

        if (loginOrigem is { } origem)
        {
            valores["LimiteRequisicoes:LoginOrigem:Limite"] = origem.ToString(CultureInfo.InvariantCulture);
        }

        if (registro is { } contas)
        {
            valores["LimiteRequisicoes:Registro:Limite"] = contas.ToString(CultureInfo.InvariantCulture);
        }

        return valores;
    }

    /// <summary>Uma instância da API sobre o banco dado. Só a primeira precisa preparar o banco.</summary>
    public static FabricaApi Instancia(
        string stringConexao, Dictionary<string, string?> limites, IPAddress? endereco = null, TimeProvider? relogio = null, bool prepararBanco = true) => new()
        {
            PrepararBanco = prepararBanco,
            StringConexao = stringConexao,
            Configuracoes = limites,
            EnderecoConexao = endereco ?? Origem,
            Relogio = relogio
        };

    /// <summary>A API sem banco: autenticação falsa e o estado dos limites em memória.</summary>
    public static FabricaApi SemBanco(
        Dictionary<string, string?> configuracoes, IPAddress? endereco = null, IArmazenamentoContagens? armazenamento = null,
        CapturaLogs? logs = null, ColetorTelemetria? telemetria = null) => new()
        {
            ServicoAutenticacao = LoginRecusado(),
            ArmazenamentoContagens = armazenamento,
            Configuracoes = configuracoes,
            EnderecoConexao = endereco ?? Origem,
            Logs = logs,
            Telemetria = telemetria
        };

    public static Task<HttpResponseMessage> LoginAsync(HttpClient cliente, string email = "maria@exemplo.com.br", string senha = SenhaErrada) =>
        cliente.PostAsJsonAsync(RotaLogin, new { email, senha }, Cancelamento);

    public static Task<HttpResponseMessage> RegistroAsync(HttpClient cliente, string email) =>
        cliente.PostAsJsonAsync(RotaRegistro, new { nome = "Maria Souza", email, senha = SenhaValida }, Cancelamento);

    /// <summary>Segundos do <c>Retry-After</c> de uma resposta 429.</summary>
    public static int RetryAfter(HttpResponseMessage resposta) =>
        int.Parse(resposta.Headers.GetValues("Retry-After").Single(), CultureInfo.InvariantCulture);

    private static ServicoAutenticacaoFalso LoginRecusado() => new()
    {
        ResultadoLogin = Resultado<LoginResponseDTO>.Falhou(new Falha(TipoFalha.NaoAutenticado, "Credenciais inválidas."))
    };
}
