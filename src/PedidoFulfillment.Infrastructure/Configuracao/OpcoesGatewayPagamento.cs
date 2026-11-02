using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Seção <c>GatewayPagamento</c> do cliente HTTP da API de pagamento (REQ-19, FR-018, research
/// R-06; contrato do cliente › Configuração). A taxa de aprovação deixou de ser do Worker: é da
/// API simulada.
/// </summary>
public sealed class OpcoesGatewayPagamento
{
    public const string Secao = "GatewayPagamento";

    /// <summary>Endereço da API de pagamento (obrigatório, <c>http</c> ou <c>https</c>).</summary>
    public string? UrlBase { get; set; }

    /// <summary>Chave enviada em <c>X-Api-Key</c>; nunca aparece em log ou trace (FR-002).</summary>
    public string? ChaveApi { get; set; }

    /// <summary>Tempo limite de cada tentativa (FR-012).</summary>
    public int TempoTentativaMs { get; set; } = 3000;

    /// <summary>Tentativas por chamada, contando a primeira (FR-011).</summary>
    public int Tentativas { get; set; } = 3;

    /// <summary>Espera base entre as tentativas, crescente e com jitter.</summary>
    public int EsperaBaseMs { get; set; } = 300;

    /// <summary>Limite total de uma chamada, com todas as tentativas e esperas (FR-011, FR-019).</summary>
    public int TempoTotalMs { get; set; } = 10000;

    public OpcoesCircuitoGateway Circuito { get; set; } = new();
}

/// <summary>Circuito do gateway (REQ-19, FR-016), por instância do Worker.</summary>
public sealed class OpcoesCircuitoGateway
{
    /// <summary>Proporção de falhas na janela que abre o circuito.</summary>
    public double ProporcaoFalhas { get; set; } = 0.5;

    /// <summary>Chamadas mínimas na janela antes de o circuito poder abrir.</summary>
    public int MinimoChamadas { get; set; } = 6;

    public int JanelaSegundos { get; set; } = 30;

    /// <summary>Tempo com o circuito aberto, sem chamar a API, antes da chamada de teste.</summary>
    public int PausaSegundos { get; set; } = 30;
}

/// <summary>Faixas do contrato do cliente; cada erro nomeia a chave sem mostrar o valor (FR-018).</summary>
public sealed class ValidadorOpcoesGatewayPagamento : IValidateOptions<OpcoesGatewayPagamento>
{
    public const int TamanhoMinimoChave = 16;

    public ValidateOptionsResult Validate(string? name, OpcoesGatewayPagamento options)
    {
        var erros = new List<string>();
        if (!Uri.TryCreate(options.UrlBase, UriKind.Absolute, out var url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            erros.Add($"{Chave(nameof(OpcoesGatewayPagamento.UrlBase))} é obrigatória e deve ser uma URL absoluta http ou https.");
        }

        if (string.IsNullOrWhiteSpace(options.ChaveApi) || options.ChaveApi.Length < TamanhoMinimoChave)
        {
            erros.Add($"{Chave(nameof(OpcoesGatewayPagamento.ChaveApi))} é obrigatória e deve ter pelo menos {TamanhoMinimoChave} caracteres.");
        }

        Faixa(erros, nameof(OpcoesGatewayPagamento.TempoTentativaMs), options.TempoTentativaMs, 100, 30000);
        Faixa(erros, nameof(OpcoesGatewayPagamento.Tentativas), options.Tentativas, 1, 6);
        Faixa(erros, nameof(OpcoesGatewayPagamento.EsperaBaseMs), options.EsperaBaseMs, 10, 10000);
        if (options.TempoTotalMs <= options.TempoTentativaMs || options.TempoTotalMs > 60000)
        {
            erros.Add($"{Chave(nameof(OpcoesGatewayPagamento.TempoTotalMs))} deve ser maior que {Chave(nameof(OpcoesGatewayPagamento.TempoTentativaMs))} e no máximo 60000.");
        }

        var circuito = options.Circuito;
        if (double.IsNaN(circuito.ProporcaoFalhas) || circuito.ProporcaoFalhas < 0.1 || circuito.ProporcaoFalhas > 1.0)
        {
            erros.Add($"{Chave("Circuito:ProporcaoFalhas")} deve estar entre 0.1 e 1.0.");
        }

        Faixa(erros, "Circuito:MinimoChamadas", circuito.MinimoChamadas, 2, 100);
        Faixa(erros, "Circuito:JanelaSegundos", circuito.JanelaSegundos, 1, 600);
        Faixa(erros, "Circuito:PausaSegundos", circuito.PausaSegundos, 1, 600);
        return erros.Count > 0 ? ValidateOptionsResult.Fail(erros) : ValidateOptionsResult.Success;
    }

    private static string Chave(string nome) => $"{OpcoesGatewayPagamento.Secao}:{nome}";

    private static void Faixa(List<string> erros, string nome, int valor, int minimo, int maximo)
    {
        if (valor < minimo || valor > maximo)
        {
            erros.Add($"{Chave(nome)} deve estar entre {minimo} e {maximo}.");
        }
    }
}
