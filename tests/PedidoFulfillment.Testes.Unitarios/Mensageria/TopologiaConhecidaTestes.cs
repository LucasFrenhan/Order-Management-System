using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Consumidores conhecidos e tipos que exigem rota (REQ-12, R-01, FR-003 a FR-005).</summary>
public sealed class TopologiaConhecidaTestes
{
    private static readonly TopologiaConhecida Atual = new(
    [
        new DescritorFalso("estoque", ["pedido.criado"]),
        new DescritorFalso("pagamento", ["estoque.reservado"]),
        new DescritorFalso("entrega", ["pagamento.aprovado"])
    ]);

    [Theory]
    [InlineData("pedido.criado", true)]
    [InlineData("estoque.reservado", true)]
    [InlineData("pagamento.aprovado", true)]
    [InlineData("estoque.insuficiente", false)]
    [InlineData("pagamento.recusado", false)]
    [InlineData("entrega.criada", false)]
    public void So_os_tipos_com_consumidor_exigem_rota(string tipo, bool exige)
    {
        Atual.ExigeRota(tipo).Should().Be(exige);
    }

    [Fact]
    public void Sem_descritores_nenhum_tipo_exige_rota()
    {
        var topologia = new TopologiaConhecida([]);

        topologia.Consumidores.Should().BeEmpty();
        topologia.ExigeRota("pedido.criado").Should().BeFalse();
    }

    [Fact]
    public void Um_consumidor_novo_passa_a_exigir_rota_so_por_estar_registrado()
    {
        var topologia = new TopologiaConhecida([new DescritorFalso("estoque", ["pedido.criado"]), new DescritorFalso("auditoria", ["entrega.criada"])]);

        topologia.ExigeRota("entrega.criada").Should().BeTrue();
    }

    [Fact]
    public void Consumidores_mantem_o_nome_e_os_tipos_de_cada_descritor()
    {
        Atual.Consumidores.Select(c => (c.Consumidor, string.Join(",", c.Tipos))).Should().Equal(
            ("estoque", "pedido.criado"),
            ("pagamento", "estoque.reservado"),
            ("entrega", "pagamento.aprovado"));
    }

    [Fact]
    public void A_comparacao_do_tipo_e_ordinal()
    {
        Atual.ExigeRota("Pedido.Criado").Should().BeFalse();
    }

    private sealed record DescritorFalso(string Consumidor, IReadOnlyCollection<string> Tipos) : IDescritorConsumidor;
}
