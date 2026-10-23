using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Serialização dos contratos de evento v1 (REQ-06, FR-001 a FR-004; REQ-08, FR-012 e FR-013; REQ-10, FR-008 e FR-009).</summary>
public sealed class SerializacaoEventosTestes
{
    internal static readonly DateTimeOffset Instante = new(2026, 10, 22, 15, 30, 0, TimeSpan.Zero);
    internal static readonly Guid PedidoId = Guid.Parse("0192b6a0-1111-7000-8000-000000000001");
    private static readonly Guid ProdutoId = Guid.Parse("0192b6a0-0000-7000-8000-000000000001");

    internal static EnvelopeEvento<PedidoCriado> PedidoCriado() => EnvelopeEvento.Criar(PedidoId, new PedidoCriado
    {
        PedidoId = PedidoId,
        Numero = "PED-20261022-000001",
        ClienteId = Guid.Parse("0192b6a0-2222-7000-8000-000000000001"),
        Itens = [new ItemPedidoCriado { ProdutoId = ProdutoId, Quantidade = 2, PrecoUnitario = 349.90m }],
        ValorTotal = 829.70m,
        Moeda = "BRL"
    }, Instante);

    internal static EnvelopeEvento<EstoqueReservado> EstoqueReservado() => EnvelopeEvento.Criar(PedidoId, new EstoqueReservado
    {
        PedidoId = PedidoId,
        Itens = [new ItemEstoqueReservado { ProdutoId = ProdutoId, Quantidade = 2 }]
    }, Instante);

    internal static EnvelopeEvento<EstoqueInsuficiente> EstoqueInsuficiente() => EnvelopeEvento.Criar(PedidoId, new EstoqueInsuficiente
    {
        PedidoId = PedidoId,
        Itens = [new ItemEstoqueInsuficiente { ProdutoId = ProdutoId, QuantidadeSolicitada = 2, QuantidadeDisponivel = 1, Inativo = false }]
    }, Instante);

    internal static EnvelopeEvento<PagamentoAprovado> PagamentoAprovado() => EnvelopeEvento.Criar(PedidoId, new PagamentoAprovado
    {
        PedidoId = PedidoId,
        Valor = 829.70m,
        IdentificadorTransacao = "TX-1"
    }, Instante);

    internal static EnvelopeEvento<PagamentoRecusado> PagamentoRecusado() => EnvelopeEvento.Criar(PedidoId, new PagamentoRecusado
    {
        PedidoId = PedidoId,
        Valor = 829.70m,
        Motivo = "Cartão recusado"
    }, Instante);

    internal static EnvelopeEvento<EntregaCriada> EntregaCriada() => EnvelopeEvento.Criar(PedidoId, new EntregaCriada
    {
        PedidoId = PedidoId,
        Numero = "PED-20261022-000001",
        OrdemEntregaId = Guid.Parse("0192b6a0-3333-7000-8000-000000000001"),
        Itens =
        [
            new ItemEntregaCriada { ProdutoId = ProdutoId, Quantidade = 2 },
            new ItemEntregaCriada { ProdutoId = Guid.Parse("0192b6a0-0000-7000-8000-000000000002"), Quantidade = 1 }
        ]
    }, Instante);

    [Fact]
    public void Envelope_traz_tipo_versao_correlacao_e_campos_em_camelCase()
    {
        using var json = JsonDocument.Parse(SerializacaoEventos.Serializar(PedidoCriado()));
        var raiz = json.RootElement;

        raiz.EnumerateObject().Select(p => p.Name).Should().Equal("idEvento", "tipo", "versao", "ocorridoEm", "idCorrelacao", "dados");
        raiz.GetProperty("tipo").GetString().Should().Be("pedido.criado");
        raiz.GetProperty("versao").GetInt32().Should().Be(1);
        raiz.GetProperty("ocorridoEm").GetString().Should().Be("2026-10-22T15:30:00+00:00");
        raiz.GetProperty("idCorrelacao").GetGuid().Should().Be(PedidoId);
        raiz.GetProperty("idEvento").GetGuid().Version.Should().Be(7);
        raiz.GetProperty("dados").GetProperty("valorTotal").GetRawText().Should().Be("829.70");
        raiz.GetProperty("dados").GetProperty("itens")[0].GetProperty("precoUnitario").GetRawText().Should().Be("349.90");
    }

    [Fact]
    public void Cada_contrato_tem_o_seu_tipo()
    {
        PedidoCriado().Tipo.Should().Be(TiposEvento.PedidoCriado);
        EstoqueReservado().Tipo.Should().Be(TiposEvento.EstoqueReservado);
        PagamentoAprovado().Tipo.Should().Be(TiposEvento.PagamentoAprovado);
        PagamentoRecusado().Tipo.Should().Be(TiposEvento.PagamentoRecusado);
        EstoqueInsuficiente().Tipo.Should().Be(TiposEvento.EstoqueInsuficiente);
        TiposEvento.EstoqueInsuficiente.Should().Be("estoque.insuficiente");
        EntregaCriada().Tipo.Should().Be(TiposEvento.EntregaCriada);
        TiposEvento.EntregaCriada.Should().Be("entrega.criada");
        TiposEvento.TipoDados.Should().HaveCount(6);
    }

    [Fact]
    public void Ida_e_volta_preserva_os_valores_dos_seis_eventos()
    {
        DeveVoltarIgual(PedidoCriado());
        DeveVoltarIgual(EstoqueReservado());
        DeveVoltarIgual(PagamentoAprovado());
        DeveVoltarIgual(PagamentoRecusado());
        DeveVoltarIgual(EstoqueInsuficiente());
        DeveVoltarIgual(EntregaCriada());
    }

    [Fact]
    public void Entrega_criada_traz_a_ordem_e_os_itens_em_camelCase()
    {
        using var json = JsonDocument.Parse(SerializacaoEventos.Serializar(EntregaCriada()));
        var raiz = json.RootElement;

        raiz.GetProperty("tipo").GetString().Should().Be("entrega.criada");
        raiz.GetProperty("versao").GetInt32().Should().Be(1);
        var dados = raiz.GetProperty("dados");
        dados.EnumerateObject().Select(p => p.Name).Should().Equal("pedidoId", "numero", "ordemEntregaId", "itens");
        dados.GetProperty("pedidoId").GetGuid().Should().Be(PedidoId);
        dados.GetProperty("numero").GetString().Should().Be("PED-20261022-000001");
        dados.GetProperty("ordemEntregaId").GetGuid().Should().Be(Guid.Parse("0192b6a0-3333-7000-8000-000000000001"));
        var item = dados.GetProperty("itens")[0];
        item.EnumerateObject().Select(p => p.Name).Should().Equal("produtoId", "quantidade");
        item.GetProperty("produtoId").GetGuid().Should().Be(ProdutoId);
        item.GetProperty("quantidade").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Estoque_insuficiente_traz_os_itens_em_falta_em_camelCase()
    {
        using var json = JsonDocument.Parse(SerializacaoEventos.Serializar(EstoqueInsuficiente()));
        var raiz = json.RootElement;

        raiz.GetProperty("tipo").GetString().Should().Be("estoque.insuficiente");
        raiz.GetProperty("versao").GetInt32().Should().Be(1);
        var item = raiz.GetProperty("dados").GetProperty("itens")[0];
        item.EnumerateObject().Select(p => p.Name).Should().Equal("produtoId", "quantidadeSolicitada", "quantidadeDisponivel", "inativo");
        item.GetProperty("quantidadeSolicitada").GetInt32().Should().Be(2);
        item.GetProperty("quantidadeDisponivel").GetInt32().Should().Be(1);
        item.GetProperty("inativo").GetBoolean().Should().BeFalse();
    }

    private static void DeveVoltarIgual<T>(EnvelopeEvento<T> envelope)
    {
        var lido = SerializacaoEventos.Desserializar<T>(SerializacaoEventos.Serializar(envelope));

        lido.Should().BeEquivalentTo(envelope);
        lido.OcorridoEm.Offset.Should().Be(TimeSpan.Zero);
    }
}
