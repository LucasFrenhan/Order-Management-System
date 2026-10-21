using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Dados de exemplo alinhados aos exemplos do contrato (<c>PedidoPendente</c>).
/// </summary>
public static class Exemplos
{
    public static readonly Guid ProdutoTeclado = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");
    public static readonly Guid ProdutoMouse = Guid.Parse("9b2f1a3c-1d4e-4f5a-8b6c-7d8e9f0a1b2c");

    public static PedidoResponseDTO Pedido(StatusPedido status = StatusPedido.Pendente, Guid? clienteId = null) => new()
    {
        Id = Guid.Parse("0f8fad5b-d9cb-469f-a165-70867728950e"),
        Numero = "PED-20261021-000123",
        ClienteId = clienteId ?? Guid.Parse("3f2b8c1e-6a4d-4e2f-9b1a-0c7d5e8f9a10"),
        Status = status,
        Itens =
        [
            new() { ProdutoId = ProdutoTeclado, NomeProduto = "Teclado Mecânico", Quantidade = 2, PrecoUnitario = 349.90m, Subtotal = 699.80m },
            new() { ProdutoId = ProdutoMouse, NomeProduto = "Mouse sem fio", Quantidade = 1, PrecoUnitario = 129.90m, Subtotal = 129.90m }
        ],
        ValorTotal = 829.70m,
        CriadoEm = new DateTimeOffset(2026, 10, 21, 15, 30, 0, TimeSpan.Zero),
        AtualizadoEm = new DateTimeOffset(2026, 10, 21, 15, 30, 0, TimeSpan.Zero)
    };

    public static object CorpoCriarPedido() => new
    {
        itens = new[]
        {
            new { produtoId = ProdutoTeclado, quantidade = 2 },
            new { produtoId = ProdutoMouse, quantidade = 1 }
        }
    };
}
