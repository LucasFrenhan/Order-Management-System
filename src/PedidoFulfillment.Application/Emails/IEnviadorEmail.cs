namespace PedidoFulfillment.Application.Emails;

/// <summary>Como terminou uma tentativa de envio (REQ-24, R-03).</summary>
public enum ResultadoEnvioEmail
{
    Enviado,

    /// <summary>Sem conexão com o servidor: as próximas solicitações falhariam igual, e a rodada termina.</summary>
    FalhaConexao,

    /// <summary>O servidor respondeu, mas o envio pode dar certo mais tarde.</summary>
    FalhaTemporaria,

    /// <summary>O servidor recusou o destinatário com um código 5xx: insistir não adianta.</summary>
    RecusaPermanente
}

/// <summary>O envio por SMTP, atrás de uma porta: só o Worker envia (REQ-24, R-04).</summary>
public interface IEnviadorEmail
{
    Task<ResultadoEnvioEmail> EnviarAsync(MensagemEmail mensagem, CancellationToken cancellationToken);
}
