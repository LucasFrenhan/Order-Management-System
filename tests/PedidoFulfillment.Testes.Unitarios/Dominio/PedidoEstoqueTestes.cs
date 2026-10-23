using FluentAssertions;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Reserva e devolução do estoque pelo pedido (REQ-08, data-model, R-02).</summary>
public sealed class PedidoEstoqueTestes
{
    private const string Numero = "PED-20261022-000001";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Reserva = Criacao.AddMinutes(1);
    private static readonly DateTimeOffset Cancelamento = Criacao.AddMinutes(2);

    private static Produto NovoProduto(string sku, int estoque, bool ativo = true) =>
        new(sku, $"Produto {sku}", null, 10m, estoque, ativo, Criacao);

    private static Pedido NovoPedido(params (Produto Produto, int Quantidade)[] itens) =>
        new(Guid.CreateVersion7(), Numero, itens, Criacao);

    private static Dictionary<Guid, Produto> Dicionario(params Produto[] produtos) => produtos.ToDictionary(p => p.Id);

    [Fact]
    public void Reserva_com_todos_os_itens_disponiveis_abate_o_estoque_e_marca_o_pedido()
    {
        var a = NovoProduto("A", 10);
        var b = NovoProduto("B", 3);
        var pedido = NovoPedido((a, 4), (b, 1));

        var faltas = pedido.ReservarEstoque(Dicionario(a, b), Reserva);

        faltas.Should().BeEmpty();
        a.QuantidadeEstoque.Should().Be(6);
        b.QuantidadeEstoque.Should().Be(2);
        pedido.EstoqueReservado.Should().BeTrue();
        pedido.AtualizadoEm.Should().Be(Reserva);
        pedido.Status.Should().Be(StatusPedido.Pendente);
        pedido.Historico.Should().ContainSingle("a reserva não muda o status");
    }

    [Fact]
    public void Quantidade_igual_ao_estoque_e_reservada_e_zera_o_produto()
    {
        var a = NovoProduto("A", 2);
        var pedido = NovoPedido((a, 2));

        pedido.ReservarEstoque(Dicionario(a), Reserva).Should().BeEmpty();

        a.QuantidadeEstoque.Should().Be(0);
        pedido.EstoqueReservado.Should().BeTrue();
    }

    [Fact]
    public void Item_sem_estoque_ou_inativo_nao_reserva_nada()
    {
        var disponivel = NovoProduto("A", 10);
        var semEstoque = NovoProduto("B", 1);
        var inativo = NovoProduto("C", 5, ativo: false);
        var pedido = NovoPedido((disponivel, 2), (semEstoque, 3), (inativo, 1));

        var faltas = pedido.ReservarEstoque(Dicionario(disponivel, semEstoque, inativo), Reserva);

        faltas.Should().BeEquivalentTo(
        [
            new FaltaEstoque(semEstoque.Id, 3, 1, false),
            new FaltaEstoque(inativo.Id, 1, 5, true)
        ]);
        disponivel.QuantidadeEstoque.Should().Be(10);
        semEstoque.QuantidadeEstoque.Should().Be(1);
        inativo.QuantidadeEstoque.Should().Be(5);
        pedido.EstoqueReservado.Should().BeFalse();
        pedido.AtualizadoEm.Should().Be(Criacao);
    }

    [Fact]
    public void Reservar_de_novo_e_erro_de_programacao()
    {
        var a = NovoProduto("A", 10);
        var pedido = NovoPedido((a, 1));
        pedido.ReservarEstoque(Dicionario(a), Reserva);

        var reservar = () => pedido.ReservarEstoque(Dicionario(a), Reserva);

        reservar.Should().Throw<InvalidOperationException>();
        a.QuantidadeEstoque.Should().Be(9);
    }

    [Fact]
    public void Reservar_sem_um_dos_produtos_e_erro_de_programacao()
    {
        var a = NovoProduto("A", 10);
        var b = NovoProduto("B", 10);
        var pedido = NovoPedido((a, 1), (b, 1));

        var reservar = () => pedido.ReservarEstoque(Dicionario(a), Reserva);

        reservar.Should().Throw<InvalidOperationException>();
        a.QuantidadeEstoque.Should().Be(10);
        pedido.EstoqueReservado.Should().BeFalse();
    }

    [Fact]
    public void Devolucao_soma_as_quantidades_mesmo_em_produto_desativado_depois()
    {
        var a = NovoProduto("A", 10);
        var b = NovoProduto("B", 3);
        var pedido = NovoPedido((a, 4), (b, 3));
        pedido.ReservarEstoque(Dicionario(a, b), Reserva);
        b.Desativar(Reserva);

        pedido.DevolverEstoque(Dicionario(a, b), Cancelamento);

        a.QuantidadeEstoque.Should().Be(10);
        b.QuantidadeEstoque.Should().Be(3);
        b.Ativo.Should().BeFalse();
        pedido.EstoqueReservado.Should().BeFalse();
        pedido.AtualizadoEm.Should().Be(Cancelamento);
    }

    [Fact]
    public void Devolver_sem_reserva_e_erro_de_programacao()
    {
        var a = NovoProduto("A", 10);
        var pedido = NovoPedido((a, 1));

        var devolver = () => pedido.DevolverEstoque(Dicionario(a), Cancelamento);

        devolver.Should().Throw<InvalidOperationException>();
        a.QuantidadeEstoque.Should().Be(10);
    }
}
