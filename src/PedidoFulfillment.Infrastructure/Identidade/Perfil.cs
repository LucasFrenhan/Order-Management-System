using Microsoft.AspNetCore.Identity;

namespace PedidoFulfillment.Infrastructure.Identidade;

/// <summary>
/// Perfil de acesso do Identity: um por valor de <c>PerfilUsuario</c> (FR-011).
/// </summary>
public sealed class Perfil : IdentityRole<Guid>
{
    public Perfil()
    {
        Id = Guid.CreateVersion7();
    }

    public Perfil(string nome) : this()
    {
        Name = nome;
    }
}
