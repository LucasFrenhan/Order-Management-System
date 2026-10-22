using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Catálogo inicial, SOMENTE Development e Testes (FR-020). Identificadores e SKUs fixos,
/// publicados em <c>specs/004-dominio-persistencia/contracts/catalogo-inicial.md</c>.
/// </summary>
internal static class CatalogoInicial
{
    public static IReadOnlyList<Produto> Produtos(DateTimeOffset instante) =>
    [
        Produto(1, "Teclado mecânico", 349.90m, 50, true, instante),
        Produto(2, "Mouse sem fio", 129.90m, 100, true, instante),
        Produto(3, "Monitor 27 polegadas", 1899.00m, 20, true, instante),
        Produto(4, "Headset com microfone", 259.90m, 40, true, instante),
        Produto(5, "Webcam Full HD", 219.90m, 30, true, instante),
        Produto(6, "Cabo HDMI 2 m", 39.90m, 500, true, instante),
        Produto(7, "Suporte de notebook", 89.90m, 60, true, instante),
        // Estoque 1: disputa do último item (concorrência).
        Produto(8, "Hub USB-C", 199.90m, 1, true, instante),
        // Estoque zero e inativo: PRODUTO_INDISPONIVEL no REQ-05.
        Produto(9, "Cadeira ergonômica", 1299.00m, 0, true, instante),
        Produto(10, "Mousepad estendido", 59.90m, 80, false, instante)
    ];

    private static Produto Produto(int numero, string nome, decimal preco, int estoque, bool ativo, DateTimeOffset instante) =>
        new($"CAT-{numero:D3}", nome, null, preco, estoque, ativo, instante, Guid.Parse($"0192b6a0-0000-7000-8000-{numero:D12}"));
}
