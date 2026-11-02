using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Notificacoes</c> (REQ-18, research R-18). Só a <see cref="ChaveCifra"/>
/// não tem padrão: sem ela a API e o Worker não sobem, como sem a chave do JWT.
/// </summary>
public sealed class OpcoesNotificacoes
{
    public const string Secao = "Notificacoes";

    /// <summary>Ambiente das fábricas de teste, o único além de Development que aceita a rede local.</summary>
    public const string AmbienteTestes = "Testes";

    /// <summary>Base64 de 32 bytes: chave AES-GCM dos segredos dos destinos.</summary>
    public string ChaveCifra { get; set; } = "";

    /// <summary>Libera <c>http</c> e endereços internos; só em Development e nos testes (FR-008).</summary>
    public bool PermitirRedeLocal { get; set; }

    public int DestinosPorCliente { get; set; } = 5;

    public int DestinosGlobais { get; set; } = 5;

    public OpcoesEnvioNotificacoes Envio { get; set; } = new();

    public OpcoesSseNotificacoes Sse { get; set; } = new();

    /// <summary>A chave decodificada; válida depois da validação na partida.</summary>
    public byte[] ChaveCifraBytes() => Convert.FromBase64String(ChaveCifra);
}

/// <summary>Entrega dos webhooks pelo Worker (REQ-18, FR-012 a FR-015).</summary>
public sealed class OpcoesEnvioNotificacoes
{
    /// <summary>Ligado em produção. Os testes de integração o desligam por padrão, como os estornos.</summary>
    public bool Habilitado { get; set; } = true;

    public int IntervaloSegundos { get; set; } = 5;

    public int TempoLimiteSegundos { get; set; } = 10;

    public int Paralelismo { get; set; } = 8;

    public int LimiteHoras { get; set; } = 24;

    public int TamanhoLote { get; set; } = 500;

    public TimeSpan Intervalo => TimeSpan.FromSeconds(IntervaloSegundos);

    public TimeSpan TempoLimite => TimeSpan.FromSeconds(TempoLimiteSegundos);

    public TimeSpan Limite => TimeSpan.FromHours(LimiteHoras);
}

/// <summary>Eventos em tempo real pela API (REQ-18, FR-019 a FR-024).</summary>
public sealed class OpcoesSseNotificacoes
{
    public int IntervaloLeituraSegundos { get; set; } = 10;

    public int ConexoesPorUsuario { get; set; } = 3;

    public int SinalDeVidaSegundos { get; set; } = 30;

    public int MaximoReenvio { get; set; } = 500;

    public TimeSpan IntervaloLeitura => TimeSpan.FromSeconds(IntervaloLeituraSegundos);

    public TimeSpan SinalDeVida => TimeSpan.FromSeconds(SinalDeVidaSegundos);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesNotificacoes(IHostEnvironment ambiente) : IValidateOptions<OpcoesNotificacoes>
{
    public const int TamanhoChave = 32;

    public ValidateOptionsResult Validate(string? name, OpcoesNotificacoes options)
    {
        var faixas = new FaixasOpcoes(OpcoesNotificacoes.Secao);
        if (!ChaveValida(options.ChaveCifra))
        {
            faixas.Falhar($"{OpcoesNotificacoes.Secao}:{nameof(OpcoesNotificacoes.ChaveCifra)} deve ser o Base64 de {TamanhoChave} bytes.");
        }

        if (options.PermitirRedeLocal && !ambiente.IsDevelopment() && !ambiente.IsEnvironment(OpcoesNotificacoes.AmbienteTestes))
        {
            faixas.Falhar($"{OpcoesNotificacoes.Secao}:{nameof(OpcoesNotificacoes.PermitirRedeLocal)} só é aceito em Development e nos testes.");
        }

        faixas.Limitar(options.DestinosPorCliente, 1, 50, nameof(OpcoesNotificacoes.DestinosPorCliente));
        faixas.Limitar(options.DestinosGlobais, 1, 50, nameof(OpcoesNotificacoes.DestinosGlobais));
        faixas.Limitar(options.Envio.IntervaloSegundos, 1, 300, "Envio:IntervaloSegundos");
        faixas.Limitar(options.Envio.TempoLimiteSegundos, 1, 60, "Envio:TempoLimiteSegundos");
        faixas.Limitar(options.Envio.Paralelismo, 1, 64, "Envio:Paralelismo");
        faixas.Limitar(options.Envio.LimiteHoras, 1, 168, "Envio:LimiteHoras");
        faixas.Limitar(options.Envio.TamanhoLote, 1, 10000, "Envio:TamanhoLote");
        faixas.Limitar(options.Sse.IntervaloLeituraSegundos, 1, 60, "Sse:IntervaloLeituraSegundos");
        faixas.Limitar(options.Sse.ConexoesPorUsuario, 1, 100, "Sse:ConexoesPorUsuario");
        faixas.Limitar(options.Sse.SinalDeVidaSegundos, 5, 300, "Sse:SinalDeVidaSegundos");
        faixas.Limitar(options.Sse.MaximoReenvio, 1, 10000, "Sse:MaximoReenvio");
        return faixas.Resultado();
    }

    private static bool ChaveValida(string? chave)
    {
        if (string.IsNullOrWhiteSpace(chave))
        {
            return false;
        }

        var bytes = new byte[TamanhoChave + 1];
        return Convert.TryFromBase64String(chave, bytes, out var lidos) && lidos == TamanhoChave;
    }
}
