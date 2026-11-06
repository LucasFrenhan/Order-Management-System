namespace PedidoFulfillment.Application.Contratos.Usuarios;

/// <summary>Se a conta pode entrar no sistema (REQ-25). Independente do bloqueio por erros de senha.</summary>
public enum SituacaoUsuario
{
    Ativa,
    Desativada
}
