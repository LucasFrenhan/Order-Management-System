using System.Net.Mail;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Configuração da seção <c>Email</c> do Worker (REQ-24, R-04). Sem ela, fora de Development, o Worker
/// não sobe: uma recuperação de senha que falha em silêncio é pior que uma implantação que para na hora.
/// </summary>
public sealed class OpcoesEmail
{
    public const string Secao = "Email";
    public const string SegurancaStartTls = "StartTls";
    public const string SegurancaSslOnConnect = "SslOnConnect";
    public const string SegurancaNenhuma = "Nenhuma";

    /// <summary>Os testes o desligam, como as demais rotinas.</summary>
    public bool Habilitado { get; set; } = true;

    public string Servidor { get; set; } = "";

    public int Porta { get; set; } = 587;

    public string Usuario { get; set; } = "";

    public string Senha { get; set; } = "";

    public string Remetente { get; set; } = "";

    public string Seguranca { get; set; } = SegurancaStartTls;

    /// <summary>Com ela, o e-mail traz um link para a aplicação.</summary>
    public string UrlBaseAplicacao { get; set; } = "";

    public int ValidadeRecuperacaoMinutos { get; set; } = 30;

    public int ValidadeConfirmacaoHoras { get; set; } = 24;

    /// <summary>Por operação SMTP (analyze U2).</summary>
    public int TempoLimiteSegundos { get; set; } = 10;

    public int IntervaloSegundos { get; set; } = 10;

    public int TamanhoLote { get; set; } = 50;

    public TimeSpan ValidadeRecuperacao => TimeSpan.FromMinutes(ValidadeRecuperacaoMinutos);

    public TimeSpan ValidadeConfirmacao => TimeSpan.FromHours(ValidadeConfirmacaoHoras);

    public TimeSpan TempoLimite => TimeSpan.FromSeconds(TempoLimiteSegundos);

    public TimeSpan Intervalo => TimeSpan.FromSeconds(IntervaloSegundos);
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores (FR-023).</summary>
public sealed class ValidadorOpcoesEmail(string ambiente) : IValidateOptions<OpcoesEmail>
{
    public ValidateOptionsResult Validate(string? name, OpcoesEmail options)
    {
        var faixas = new FaixasOpcoes(OpcoesEmail.Secao);
        if (options.Habilitado)
        {
            if (string.IsNullOrWhiteSpace(options.Servidor))
            {
                faixas.Falhar($"{OpcoesEmail.Secao}:{nameof(OpcoesEmail.Servidor)} é obrigatório.");
            }

            if (!MailAddress.TryCreate(options.Remetente, out _))
            {
                faixas.Falhar($"{OpcoesEmail.Secao}:{nameof(OpcoesEmail.Remetente)} deve ser um e-mail válido.");
            }
        }

        ValidarSeguranca(faixas, options.Seguranca);
        faixas.Limitar(options.Porta, 1, 65535, nameof(OpcoesEmail.Porta));
        faixas.Limitar(options.ValidadeRecuperacaoMinutos, 5, 1440, nameof(OpcoesEmail.ValidadeRecuperacaoMinutos));
        faixas.Limitar(options.ValidadeConfirmacaoHoras, 1, 168, nameof(OpcoesEmail.ValidadeConfirmacaoHoras));
        faixas.Limitar(options.TempoLimiteSegundos, 1, 60, nameof(OpcoesEmail.TempoLimiteSegundos));
        faixas.Limitar(options.IntervaloSegundos, 1, 300, nameof(OpcoesEmail.IntervaloSegundos));
        faixas.Limitar(options.TamanhoLote, 1, 1000, nameof(OpcoesEmail.TamanhoLote));
        return faixas.Resultado();
    }

    private void ValidarSeguranca(FaixasOpcoes faixas, string seguranca)
    {
        switch (seguranca)
        {
            case OpcoesEmail.SegurancaStartTls or OpcoesEmail.SegurancaSslOnConnect:
                break;
            case OpcoesEmail.SegurancaNenhuma when ambiente == Environments.Development || ambiente == "Testes":
                break;
            case OpcoesEmail.SegurancaNenhuma:
                faixas.Falhar($"{OpcoesEmail.Secao}:{nameof(OpcoesEmail.Seguranca)} só aceita {OpcoesEmail.SegurancaNenhuma} em Development e em testes.");
                break;
            default:
                faixas.Falhar($"{OpcoesEmail.Secao}:{nameof(OpcoesEmail.Seguranca)} deve ser {OpcoesEmail.SegurancaStartTls}, {OpcoesEmail.SegurancaSslOnConnect} ou {OpcoesEmail.SegurancaNenhuma}.");
                break;
        }
    }
}
