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

    /// <summary>Pedido despachado com Correios/BR123 em <see cref="Despacho"/>.</summary>
    private Pedido PedidoDespachado()
    {
        var pedido = PedidoEm(StatusPedido.ProntoParaEnvio);
        pedido.RegistrarRastreio("Correios", "BR123", Operador, Despacho).Should().Be(ResultadoRastreio.Despachado);
        return pedido;
    }

    [Fact]
    public void Mesmos_dados_em_pedido_despachado_nao_mudam_nada()
    {
        var pedido = PedidoDespachado();
        var (entradas, atualizadoEm, ordemAtualizadaEm) = (pedido.Historico.Count, pedido.AtualizadoEm, pedido.OrdemEntrega!.AtualizadoEm);

        pedido.RegistrarRastreio(" Correios ", "BR123 ", Operador, Despacho.AddHours(1)).Should().Be(ResultadoRastreio.SemMudanca);

        (pedido.Historico.Count, pedido.AtualizadoEm, pedido.OrdemEntrega.AtualizadoEm).Should().Be((entradas, atualizadoEm, ordemAtualizadaEm));
    }

    [Theory]
    [InlineData("Correios", "BR999")]
    [InlineData("Jadlog", "BR123")]
    [InlineData("Correios", "br123")]
    public void Dados_diferentes_em_pedido_despachado_corrigem_o_rastreio_sem_mudar_status_nem_historico(string transportadora, string codigo)
    {
        var pedido = PedidoDespachado();
        var entradas = pedido.Historico.Count;
        var correcao = Despacho.AddHours(1);

        pedido.RegistrarRastreio(transportadora, codigo, Operador, correcao).Should().Be(ResultadoRastreio.Corrigido);

        var ordem = pedido.OrdemEntrega!;
        (ordem.Transportadora, ordem.CodigoRastreio, ordem.DespachadoEm, ordem.AtualizadoEm)
            .Should().Be((transportadora, codigo, (DateTimeOffset?)Despacho, correcao));
        (pedido.Status, pedido.Historico.Count, pedido.AtualizadoEm).Should().Be((StatusPedido.Despachado, entradas, correcao));
    }

    [Fact]
    public void Pedido_despachado_pela_rota_de_status_sem_ordem_ganha_a_ordem_com_a_data_do_registro()
    {
        var pedido = PedidoEm(StatusPedido.Despachado, comOrdem: false);
        var entradas = pedido.Historico.Count;

        pedido.RegistrarRastreio("Correios", "BR1", Operador, Despacho).Should().Be(ResultadoRastreio.Corrigido);

        var ordem = pedido.OrdemEntrega!;
        (ordem.PedidoId, ordem.Transportadora, ordem.CodigoRastreio, ordem.DespachadoEm).Should().Be((pedido.Id, "Correios", "BR1", (DateTimeOffset?)Despacho));
        (pedido.Status, pedido.Historico.Count).Should().Be((StatusPedido.Despachado, entradas));
    }

    [Fact]
    public void Pedido_despachado_com_ordem_sem_data_ganha_a_data_do_registro()
    {
        var pedido = PedidoEm(StatusPedido.Despachado);

        pedido.RegistrarRastreio("Correios", "BR1", Operador, Despacho).Should().Be(ResultadoRastreio.Corrigido);

        pedido.OrdemEntrega!.DespachadoEm.Should().Be(Despacho);
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
