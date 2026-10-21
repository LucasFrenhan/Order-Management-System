using Microsoft.AspNetCore.Identity;

namespace PedidoFulfillment.Infrastructure.Identidade;

/// <summary>
/// Conta de usuário do Identity (data-model.md). <c>UserName</c> recebe o próprio e-mail.
/// </summary>
public sealed class Usuario : IdentityUser<Guid>
{
    public Usuario()
    {
        Id = Guid.CreateVersion7();
        SecurityStamp = Guid.NewGuid().ToString();
    }

    public string Nome { get; set; } = "";
}
