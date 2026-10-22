using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>Dados válidos para os testes de persistência do REQ-04.</summary>
internal static class DadosPersistencia
{
    public static readonly DateTimeOffset Instante = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    private static int sequencia;

    public static async Task<Guid> NovoUsuarioAsync(ContextoBancoDados contexto)
    {
        var email = $"usuario-{Guid.NewGuid():N}@exemplo.com.br";
        var usuario = new Usuario
        {
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            Nome = "Usuário de teste"
        };
        contexto.Users.Add(usuario);
        await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        return usuario.Id;
    }

    public static Produto NovoProduto(string? sku = null, decimal preco = 10m, int estoque = 10) =>
        new(sku ?? $"SKU-{Guid.NewGuid():N}"[..20], "Produto de teste", null, preco, estoque, ativo: true, Instante);

    public static Pedido NovoPedido(Guid clienteId, params Produto[] produtos) =>
        new(clienteId, NovoNumero(), produtos.Select(p => (p, 1)), Instante);

    /// <summary>Número único no padrão <c>PED-AAAAMMDD-NNNNNN</c>.</summary>
    public static string NovoNumero() => $"PED-20261022-{Interlocked.Increment(ref sequencia) % 1_000_000:D6}";
}
