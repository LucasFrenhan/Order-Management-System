using System.Net.Sockets;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Infrastructure.Emails;

/// <summary>
/// Envio por SMTP com o MailKit (REQ-24, R-04). Sem <c>ProtocolLogger</c>: ele gravaria o corpo, com o
/// token, no log (R-10). Uma conexão por mensagem: o volume é baixo e limitado pelo limite de requisições.
/// </summary>
internal sealed class EnviadorEmailSmtp(IOptions<OpcoesEmail> opcoes) : IEnviadorEmail
{
    public async Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken cancellationToken)
    {
        var configuracao = opcoes.Value;
        if (!MailboxAddress.TryParse(mensagem.Destino, out var destino))
        {
            return ResultadoEnvioEmail.RecusaPermanente;
        }

        using var cliente = new SmtpClient { Timeout = (int)configuracao.TempoLimite.TotalMilliseconds };
        try
        {
            await cliente.ConnectAsync(configuracao.Servidor, configuracao.Porta, OpcaoSeguranca(configuracao.Seguranca), cancellationToken);
            if (configuracao.Usuario.Length > 0)
            {
                await cliente.AuthenticateAsync(configuracao.Usuario, configuracao.Senha, cancellationToken);
            }
        }
        catch (Exception excecao) when (EhFalhaConexao(excecao))
        {
            return ResultadoEnvioEmail.FalhaConexao;
        }

        try
        {
            await cliente.SendAsync(Montar(configuracao.Remetente, destino, mensagem), cancellationToken);
            await cliente.DisconnectAsync(true, cancellationToken);
            return ResultadoEnvioEmail.Enviado;
        }
        catch (SmtpCommandException excecao) when (excecao.ErrorCode == SmtpErrorCode.RecipientNotAccepted && (int)excecao.StatusCode >= 500)
        {
            return ResultadoEnvioEmail.RecusaPermanente;
        }
        catch (Exception excecao) when (excecao is SmtpCommandException or SmtpProtocolException or ServiceNotConnectedException or IOException or TimeoutException)
        {
            return ResultadoEnvioEmail.FalhaTemporaria;
        }
    }

    private static MimeMessage Montar(string remetente, MailboxAddress destino, MensagemEmail mensagem)
    {
        var mime = new MimeMessage { Subject = mensagem.Assunto, Body = new TextPart("plain") { Text = mensagem.Corpo } };
        mime.From.Add(MailboxAddress.Parse(remetente));
        mime.To.Add(destino);
        return mime;
    }

    private static SecureSocketOptions OpcaoSeguranca(string seguranca) => seguranca switch
    {
        OpcoesEmail.SegurancaSslOnConnect => SecureSocketOptions.SslOnConnect,
        OpcoesEmail.SegurancaNenhuma => SecureSocketOptions.None,
        _ => SecureSocketOptions.StartTls
    };

    private static bool EhFalhaConexao(Exception excecao) =>
        excecao is SocketException or IOException or TimeoutException or SmtpProtocolException or SmtpCommandException or AuthenticationException or SslHandshakeException;
}
