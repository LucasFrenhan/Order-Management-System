namespace PedidoFulfillment.Domain.Usuarios;

/// <summary>
/// Perfis de acesso do sistema. Os nomes são usados como roles no token de acesso.
/// </summary>
public enum PerfilUsuario
{
    Cliente = 1,
    Admin,
    OperadorLogistica
}
