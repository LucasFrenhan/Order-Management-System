using FluentAssertions;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

public sealed class PagamentoEntregaTestes
{
    private static readonly DateTimeOffset Instante = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    [Fact]
    public void Pagamento_nasce_pendente_com_valor_arredondado()
    {
        var pagamento = new Pagamento(PedidoId, 10.005m, Instante);

        pagamento.Id.Version.Should().Be(7);
        pagamento.PedidoId.Should().Be(PedidoId);
        pagamento.Valor.Should().Be(10.00m);
        pagamento.Situacao.Should().Be(SituacaoPagamento.Pendente);
        pagamento.IdentificadorTransacao.Should().BeNull();
        pagamento.ProcessadoEm.Should().BeNull();
        pagamento.CriadoEm.Should().Be(Instante);
        pagamento.AtualizadoEm.Should().Be(Instante);
    }

    [Fact]
    public void Pagamento_com_valor_negativo_e_recusado()
    {
        var criar = () => new Pagamento(PedidoId, -1m, Instante);

        criar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RegistrarResultado_preenche_situacao_transacao_e_datas()
    {
        var pagamento = new Pagamento(PedidoId, 10m, Instante);
        var depois = Instante.AddMinutes(1);

        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, "TX-1", depois);

        pagamento.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        pagamento.IdentificadorTransacao.Should().Be("TX-1");
        pagamento.ProcessadoEm.Should().Be(depois);
        pagamento.AtualizadoEm.Should().Be(depois);
    }

    [Fact]
    public void RegistrarResultado_recusa_transacao_acima_de_100_caracteres()
    {
        var pagamento = new Pagamento(PedidoId, 10m, Instante);

        var registrar = () => pagamento.RegistrarResultado(SituacaoPagamento.Recusado, new string('t', 101), Instante);

        registrar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OrdemEntrega_nasce_sem_dados_de_despacho()
    {
        var ordem = new OrdemEntrega(PedidoId, Instante);

        ordem.Id.Version.Should().Be(7);
        ordem.PedidoId.Should().Be(PedidoId);
        ordem.Transportadora.Should().BeNull();
        ordem.CodigoRastreio.Should().BeNull();
        ordem.DespachadoEm.Should().BeNull();
        ordem.CriadoEm.Should().Be(Instante);
        ordem.AtualizadoEm.Should().Be(Instante);
    }

    [Fact]
    public void RegistrarDespacho_preenche_os_dados_e_a_data()
    {
        var ordem = new OrdemEntrega(PedidoId, Instante);
        var depois = Instante.AddMinutes(1);

        ordem.RegistrarDespacho(" Transportadora X ", " BR123 ", depois);

        ordem.Transportadora.Should().Be("Transportadora X");
        ordem.CodigoRastreio.Should().Be("BR123");
        ordem.DespachadoEm.Should().Be(depois);
        ordem.AtualizadoEm.Should().Be(depois);
    }

    [Theory]
    [InlineData("", "BR123")]
    [InlineData("Transportadora X", " ")]
    public void RegistrarDespacho_recusa_texto_vazio(string transportadora, string codigo)
    {
        var registrar = () => new OrdemEntrega(PedidoId, Instante).RegistrarDespacho(transportadora, codigo, Instante);

        registrar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RegistrarDespacho_recusa_textos_acima_do_limite()
    {
        var ordem = new OrdemEntrega(PedidoId, Instante);

        new Action(() => ordem.RegistrarDespacho(new string('t', 101), "BR123", Instante)).Should().Throw<ArgumentException>();
        new Action(() => ordem.RegistrarDespacho("Transportadora X", new string('c', 51), Instante)).Should().Throw<ArgumentException>();
        ordem.DespachadoEm.Should().BeNull();
    }
}
