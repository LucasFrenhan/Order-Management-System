using FluentAssertions;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

public sealed class PedidoTestes
{
    private const string Numero = "PED-20261022-000001";
    private static readonly DateTimeOffset Instante = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static Produto NovoProduto(string sku, decimal preco) => new(sku, $"Produto {sku}", null, preco, 10, true, Instante);

    [Fact]
    public void Item_copia_nome_e_preco_do_produto_e_calcula_o_subtotal()
    {
        var produto = NovoProduto("A", 12.50m);

        var pedido = new Pedido(ClienteId, Numero, [(produto, 3)], Instante);
        produto.AlterarDados("Outro nome", null, 99m, Instante.AddMinutes(1));

        var item = pedido.Itens.Should().ContainSingle().Subject;
        item.PedidoId.Should().Be(pedido.Id);
        item.ProdutoId.Should().Be(produto.Id);
        item.NomeProduto.Should().Be("Produto A");
        item.PrecoUnitario.Should().Be(12.50m);
        item.Quantidade.Should().Be(3);
        item.Subtotal.Should().Be(37.50m);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Item_com_quantidade_nao_positiva_e_recusado(int quantidade)
    {
        var criar = () => new Pedido(ClienteId, Numero, [(NovoProduto("A", 1m), quantidade)], Instante);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Criacao_comeca_pendente_em_brl_com_total_e_entrada_de_criacao()
    {
        var pedido = new Pedido(ClienteId, Numero, [(NovoProduto("A", 10m), 2), (NovoProduto("B", 5.25m), 1)], Instante);

        pedido.Id.Version.Should().Be(7);
        pedido.Numero.Should().Be(Numero);
        pedido.ClienteId.Should().Be(ClienteId);
        pedido.Status.Should().Be(StatusPedido.Pendente);
        pedido.Moeda.Should().Be("BRL");
        pedido.ValorTotal.Should().Be(25.25m);
        pedido.CriadoEm.Should().Be(Instante);
        pedido.AtualizadoEm.Should().Be(Instante);
        pedido.Pagamento.Should().BeNull();
        pedido.OrdemEntrega.Should().BeNull();

        var entrada = pedido.Historico.Should().ContainSingle().Subject;
        entrada.PedidoId.Should().Be(pedido.Id);
        entrada.StatusAnterior.Should().BeNull();
        entrada.StatusNovo.Should().Be(StatusPedido.Pendente);
        entrada.OcorridoEm.Should().Be(Instante);
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().BeNull();
    }

    [Fact]
    public void Criacao_sem_itens_e_recusada()
    {
        var criar = () => new Pedido(ClienteId, Numero, [], Instante);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criacao_com_o_mesmo_produto_duas_vezes_e_recusada()
    {
        var produto = NovoProduto("A", 1m);

        var criar = () => new Pedido(ClienteId, Numero, [(produto, 1), (produto, 2)], Instante);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Criacao_sem_cliente_e_recusada()
    {
        var criar = () => new Pedido(Guid.Empty, Numero, [(NovoProduto("A", 1m), 1)], Instante);

        criar.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("PED-20261022-1")]
    [InlineData("PED-20261022-0000001")]
    public void Criacao_com_numero_fora_do_tamanho_e_recusada(string numero)
    {
        var criar = () => new Pedido(ClienteId, numero, [(NovoProduto("A", 1m), 1)], Instante);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void MudarStatus_atualiza_o_pedido_e_encadeia_o_historico()
    {
        var pedido = new Pedido(ClienteId, Numero, [(NovoProduto("A", 1m), 1)], Instante);
        var autorId = Guid.CreateVersion7();
        var t1 = Instante.AddMinutes(1);
        var t2 = Instante.AddMinutes(2);

        pedido.MudarStatus(StatusPedido.Processando, autorId, null, t1);
        pedido.MudarStatus(StatusPedido.Cancelado, autorId, "Desistência", t2);

        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.AtualizadoEm.Should().Be(t2);
        pedido.CriadoEm.Should().Be(Instante);
        pedido.Historico.Select(h => (h.StatusAnterior, h.StatusNovo)).Should().Equal(
            (null, StatusPedido.Pendente),
            (StatusPedido.Pendente, StatusPedido.Processando),
            (StatusPedido.Processando, StatusPedido.Cancelado));
        var ultima = pedido.Historico.Last();
        ultima.AutorId.Should().Be(autorId);
        ultima.OcorridoEm.Should().Be(t2);
        ultima.Motivo.Should().Be("Desistência");
    }

    [Fact]
    public void MudarStatus_com_motivo_acima_de_500_caracteres_e_recusado()
    {
        var pedido = new Pedido(ClienteId, Numero, [(NovoProduto("A", 1m), 1)], Instante);

        var mudar = () => pedido.MudarStatus(StatusPedido.Cancelado, null, new string('m', 501), Instante);

        mudar.Should().Throw<ArgumentException>();
        pedido.Status.Should().Be(StatusPedido.Pendente);
        pedido.Historico.Should().ContainSingle();
    }

    [Fact]
    public void MudarStatus_nao_valida_a_transicao_que_e_regra_do_REQ_05()
    {
        var pedido = new Pedido(ClienteId, Numero, [(NovoProduto("A", 1m), 1)], Instante);
        pedido.MudarStatus(StatusPedido.Despachado, null, null, Instante);

        pedido.MudarStatus(StatusPedido.Pendente, null, null, Instante);

        pedido.Status.Should().Be(StatusPedido.Pendente);
    }
}
