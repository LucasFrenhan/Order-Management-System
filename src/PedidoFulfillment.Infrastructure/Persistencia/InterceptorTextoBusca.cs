using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Mantém <c>Produtos.TextoBusca</c> (nome, SKU e descrição numa coluna só), que o índice
/// full-text usa (REQ-22, research R-03). Uma coluna só faz a busca exigir todas as palavras
/// entre os três campos. Preenchida em todo produto adicionado ou modificado, por qualquer
/// caminho (API, catálogo inicial, testes), sem o domínio conhecer a busca.
/// </summary>
internal sealed class InterceptorTextoBusca : SaveChangesInterceptor
{
    public const string Propriedade = "TextoBusca";

    public const int Tamanho = Produto.TamanhoMaximoNome + Produto.TamanhoMaximoSku + Produto.TamanhoMaximoDescricao + 10;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Preencher(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Preencher(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public static string Texto(Produto produto) => $"{produto.Nome} {produto.Sku} {produto.Descricao}".TrimEnd();

    private static void Preencher(DbContext? contexto)
    {
        if (contexto is null)
        {
            return;
        }

        foreach (var entrada in contexto.ChangeTracker.Entries<Produto>())
        {
            if (entrada.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var propriedade = entrada.Property<string>(Propriedade);
            var texto = Texto(entrada.Entity);
            if (propriedade.CurrentValue != texto)
            {
                propriedade.CurrentValue = texto;
            }
        }
    }
}
