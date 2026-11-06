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

    /// <summary>Não nula = conta desativada (REQ-25, R-01). Independente do bloqueio por erros de senha.</summary>
    public DateTimeOffset? DesativadoEm { get; set; }

    /// <summary>Preenchida na reativação e mantida depois: avisos anteriores a ela não vão aos destinos próprios da conta (R-06).</summary>
    public DateTimeOffset? ReativadoEm { get; set; }
}
