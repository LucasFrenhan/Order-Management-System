using FluentAssertions;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Despacho e correção do rastreio pelo pedido (REQ-11, data-model, R-05).</summary>
public sealed class PedidoRastreioTestes
{
    private const string Numero = "PED-20261023-000001";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Pronto = Criacao.AddMinutes(5);
    private static readonly DateTimeOffset Despacho = Criacao.AddMinutes(30);
    private static readonly Guid Operador = Guid.CreateVersion7();

    private readonly Produto produto = new("A", "Produto A", null, 10m, 10, true, Criacao);

    /// <summary>Pedido levado pela operação até o status pedido; com ordem, ela nasce em <see cref="Pronto"/>.</summary>
    private Pedido PedidoEm(StatusPedido status, bool comOrdem = true)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), Numero, [(produto, 1)], Criacao);
        StatusPedido[] caminho = status switch
        {
            StatusPedido.Pendente => [],
            StatusPedido.Processando => [StatusPedido.Processando],
            StatusPedido.ProntoParaEnvio => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio],
            StatusPedido.Despachado => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado],
            _ => [status]
        };
        foreach (var passo in caminho)
        {
            pedido.MudarStatus(passo, Operador, null, Pronto);
        }

        if (comOrdem)
        {
            typeof(Pedido).GetProperty(nameof(Pedido.OrdemEntrega))!.SetValue(pedido, new OrdemEntrega(pedido.Id, Pronto));
        }

        return pedido;
    }

    [Theory]
    [InlineData(StatusPedido.Pendente, false)]
    [InlineData(StatusPedido.Processando, false)]
    [InlineData(StatusPedido.ProntoParaEnvio, true)]
    [InlineData(StatusPedido.Despachado, true)]
    [InlineData(StatusPedido.Cancelado, false)]
    [InlineData(StatusPedido.PagamentoRejeitado, false)]
    public void Pode_registrar_rastreio_so_pronto_ou_despachado(StatusPedido status, bool pode) =>
        PedidoEm(status).PodeRegistrarRastreio.Should().Be(pode);

    [Fact]
    public void Despacho_de_pedido_pronto_grava_o_rastreio_e_leva_a_despachado()
    {
        var pedido = PedidoEm(StatusPedido.ProntoParaEnvio);
        var ordem = pedido.OrdemEntrega;

        var resultado = pedido.RegistrarRastreio("Correios", "BR123456789BR", Operador, Despacho);

        resultado.Should().Be(ResultadoRastreio.Despachado);
        pedido.OrdemEntrega.Should().BeSameAs(ordem);
        (ordem!.Transportadora, ordem.CodigoRastreio, ordem.DespachadoEm, ordem.CriadoEm).Should().Be(("Correios", "BR123456789BR", (DateTimeOffset?)Despacho, Pronto));
        pedido.Status.Should().Be(StatusPedido.Despachado);
        pedido.AtualizadoEm.Should().Be(Despacho);
        var entrada = pedido.Historico.Last();
        (entrada.StatusAnterior, entrada.StatusNovo, entrada.AutorId, entrada.Motivo, entrada.OcorridoEm)
            .Should().Be((StatusPedido.ProntoParaEnvio, StatusPedido.Despachado, (Guid?)Operador, Pedido.MotivoPedidoDespachado, Despacho));
        Pedido.MotivoPedidoDespachado.Should().Be("Pedido despachado.");
    }

    [Fact]
    public void Despacho_de_pedido_pronto_sem_ordem_cria_a_ordem()
    {
        var pedido = PedidoEm(StatusPedido.ProntoParaEnvio, comOrdem: false);

        pedido.RegistrarRastreio("Correios", "BR123456789BR", Operador, Despacho).Should().Be(ResultadoRastreio.Despachado);

        var ordem = pedido.OrdemEntrega!;
        (ordem.PedidoId, ordem.CriadoEm, ordem.Transportadora, ordem.CodigoRastreio, ordem.DespachadoEm)
            .Should().Be((pedido.Id, Despacho, "Correios", "BR123456789BR", (DateTimeOffset?)Despacho));
        pedido.Status.Should().Be(StatusPedido.Despachado);
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public void Rastreio_fora_de_pronto_ou_despachado_falha_sem_alterar_nada(StatusPedido status)
    {
        var pedido = PedidoEm(status);

        DeveFalharSemAlterar<InvalidOperationException>(pedido, "Correios", "BR1");
    }

    [Theory]
    [InlineData(101, 10)]
    [InlineData(10, 51)]
    public void Transportadora_ou_codigo_acima_do_tamanho_falha_sem_alterar_nada(int tamanhoTransportadora, int tamanhoCodigo)
    {
        var pedido = PedidoEm(StatusPedido.ProntoParaEnvio, comOrdem: false);

        DeveFalharSemAlterar<ArgumentException>(pedido, new string('T', tamanhoTransportadora), new string('C', tamanhoCodigo));
    }

    private static void DeveFalharSemAlterar<TExcecao>(Pedido pedido, string transportadora, string codigo)
        where TExcecao : Exception
    {
        var (status, entradas, atualizadoEm, ordem) = (pedido.Status, pedido.Historico.Count, pedido.AtualizadoEm, pedido.OrdemEntrega);

        var registrar = () => pedido.RegistrarRastreio(transportadora, codigo, Operador, Despacho);

        registrar.Should().Throw<TExcecao>();
        (pedido.Status, pedido.Historico.Count, pedido.AtualizadoEm).Should().Be((status, entradas, atualizadoEm));
        pedido.OrdemEntrega.Should().BeSameAs(ordem);
        (ordem?.Transportadora, ordem?.CodigoRastreio, ordem?.DespachadoEm).Should().Be(((string?)null, (string?)null, (DateTimeOffset?)null));
    }
}
