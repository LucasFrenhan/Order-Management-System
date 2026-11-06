namespace PedidoFulfillment.Application.Emails;

/// <summary>Os e-mails que o sistema envia (REQ-24, R-03).</summary>
public enum TipoEmail
{
    RecuperacaoSenha,
    ConfirmacaoEmail,
    AvisoSenhaRedefinida,

    /// <summary>Convite para a conta criada pelo Admin definir a primeira senha (REQ-25, R-05).</summary>
    ConviteUsuario
}
