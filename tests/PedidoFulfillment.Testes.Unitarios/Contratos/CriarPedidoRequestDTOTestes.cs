using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

public sealed class CriarPedidoRequestDTOTestes
{
    private static ItemPedidoDTO Item(int quantidade = 1) => new() { ProdutoId = Guid.NewGuid(), Quantidade = quantidade };

    [Fact]
    public void Pedido_valido_nao_tem_erros()
    {
        Validacao.Validar(new CriarPedidoRequestDTO { Itens = [Item(), Item(2)] }).Should().BeEmpty();
    }

    [Fact]
    public void Pedido_sem_itens_e_rejeitado()
    {
        Validacao.Validar(new CriarPedidoRequestDTO { Itens = [] })["Itens"]
            .Should().Contain("O pedido deve ter entre 1 e 50 itens.");
    }

    [Fact]
    public void Pedido_com_itens_nulos_e_rejeitado()
    {
        Validacao.Validar(new CriarPedidoRequestDTO { Itens = null! }).Should().ContainKey("Itens");
    }

    [Fact]
    public void Pedido_com_mais_de_50_itens_e_rejeitado()
    {
        var itens = Enumerable.Range(0, 51).Select(_ => Item()).ToArray();

        Validacao.Validar(new CriarPedidoRequestDTO { Itens = itens }).Should().ContainKey("Itens");
    }

    [Fact]
    public void Produto_repetido_no_pedido_e_rejeitado()
    {
        var item = Item();

        Validacao.Validar(new CriarPedidoRequestDTO { Itens = [item, item with { Quantidade = 3 }] })["Itens"]
            .Should().Contain("Cada produto pode aparecer no máximo uma vez.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000)]
    public void Quantidade_fora_de_1_a_999_e_rejeitada(int quantidade)
    {
        Validacao.Validar(Item(quantidade))["Quantidade"].Should().Contain("A quantidade deve estar entre 1 e 999.");
    }

    [Fact]
    public void Produto_vazio_e_rejeitado()
    {
        Validacao.Validar(new ItemPedidoDTO { ProdutoId = Guid.Empty, Quantidade = 1 })["ProdutoId"]
            .Should().Contain("O produto é obrigatório.");
    }
}
