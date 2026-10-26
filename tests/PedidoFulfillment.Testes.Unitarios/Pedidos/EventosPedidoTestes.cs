using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>Montagem do <c>pedido.cancelado</c> (REQ-15, R-02, data-model).</summary>
public sealed class EventosPedidoTestes
{
    private static readonly DateTimeOffset Criacao = new(2026, 10, 26, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cancelamento = Criacao.AddHours(2);

    private readonly Pedido pedido = new(Guid.CreateVersion7(), "PED-20261026-000042",
        [(new Produto("A", "Produto A", null, 10m, 10, true, Criacao), 2)], Criacao);

    /// <summary>Conteúdo de um <c>pedido.cancelado</c> do outbox; usado também pelos testes dos serviços.</summary>
    internal static PedidoCancelado DadosCancelado(EventoSaida evento)
    {
        evento.Tipo.Should().Be(TiposEvento.PedidoCancelado);
        return SerializacaoEventos.Desserializar<PedidoCancelado>(evento.Conteudo).Dados;
    }

    [Fact]
    public void Monta_o_envelope_v1_correlacionado_ao_pedido()
    {
        var evento = EventosPedido.Cancelado(pedido, MotivoCancelamento.Operacao, null, new ResultadoCancelamento(true, true, true), Cancelamento);

        evento.Tipo.Should().Be(TiposEvento.PedidoCancelado);
        evento.CriadoEm.Should().Be(Cancelamento);
        var envelope = SerializacaoEventos.Desserializar<PedidoCancelado>(evento.Conteudo);
        envelope.IdEvento.Should().Be(evento.IdEvento);
        envelope.Versao.Should().Be(1);
        envelope.IdCorrelacao.Should().Be(pedido.Id);
        envelope.OcorridoEm.Should().Be(Cancelamento);
        envelope.Dados.Should().BeEquivalentTo(new PedidoCancelado
        {
            PedidoId = pedido.Id,
            Numero = "PED-20261026-000042",
            Motivo = MotivoCancelamento.Operacao,
            EtapaEsgotada = null,
            CanceladoEm = Cancelamento,
            EstoqueDevolvido = true,
            EstornoPendente = true,
            OrdemEntregaCancelada = true
        });
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, false, false)]
    public void As_flags_vem_do_resultado_do_cancelamento(bool estoque, bool estorno, bool ordem)
    {
        var dados = DadosCancelado(EventosPedido.Cancelado(pedido, MotivoCancelamento.Cliente, null, new ResultadoCancelamento(estoque, estorno, ordem), Cancelamento));

        (dados.EstoqueDevolvido, dados.EstornoPendente, dados.OrdemEntregaCancelada).Should().Be((estoque, estorno, ordem));
    }

    [Theory]
    [InlineData(EtapaEsgotada.Estoque)]
    [InlineData(EtapaEsgotada.Pagamento)]
    [InlineData(EtapaEsgotada.Entrega)]
    public void A_reconciliacao_leva_a_etapa(EtapaEsgotada etapa)
    {
        var dados = DadosCancelado(EventosPedido.Cancelado(pedido, MotivoCancelamento.ReconciliacaoEsgotada, etapa, default, Cancelamento));

        (dados.Motivo, dados.EtapaEsgotada).Should().Be((MotivoCancelamento.ReconciliacaoEsgotada, etapa));
    }

    [Theory]
    [InlineData(MotivoCancelamento.Cliente, EtapaEsgotada.Estoque)]
    [InlineData(MotivoCancelamento.Operacao, EtapaEsgotada.Entrega)]
    [InlineData(MotivoCancelamento.EstoqueInsuficiente, EtapaEsgotada.Estoque)]
    [InlineData(MotivoCancelamento.ReconciliacaoEsgotada, null)]
    public void Etapa_incoerente_com_o_motivo_e_recusada(MotivoCancelamento motivo, EtapaEsgotada? etapa)
    {
        var montar = () => EventosPedido.Cancelado(pedido, motivo, etapa, default, Cancelamento);

        montar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Nao_leva_cliente_valor_itens_autor_nem_o_motivo_digitado()
    {
        var evento = EventosPedido.Cancelado(pedido, MotivoCancelamento.Cliente, null, default, Cancelamento);

        using var json = JsonDocument.Parse(evento.Conteudo);
        json.RootElement.GetProperty("dados").EnumerateObject().Select(p => p.Name).Should().Equal(
            "pedidoId", "numero", "motivo", "etapaEsgotada", "canceladoEm", "estoqueDevolvido", "estornoPendente", "ordemEntregaCancelada");
        evento.Conteudo.Should().NotContain(pedido.ClienteId.ToString());
    }
}
