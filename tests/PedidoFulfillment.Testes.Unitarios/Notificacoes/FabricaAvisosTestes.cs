using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>Evento <c>pedido.status-alterado</c> e avisos de notificação (REQ-18, R-14; FR-001, FR-002 e FR-004).</summary>
public sealed class FabricaAvisosTestes
{
    private static readonly DateTimeOffset Criacao = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Mudanca = Criacao.AddMinutes(5);
    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private static Pedido NovoPedido() =>
        new(ClienteId, "PED-20261101-000042", [(new Produto("A", "Produto A", null, 10m, 5, true, Criacao), 1)], Criacao);

    [Fact]
    public void Evento_e_aviso_de_uma_mudanca_tem_o_mesmo_identificador_e_os_mesmos_dados()
    {
        var pedido = NovoPedido();
        pedido.MudarStatus(StatusPedido.Processando, null, "Pagamento aprovado.", Mudanca);
        var entrada = pedido.Historico.Last();

        var (evento, aviso) = FabricaAvisos.StatusAlterado(pedido, entrada, 2, OrigemMudancaStatus.Worker);

        evento.Tipo.Should().Be(TiposEvento.PedidoStatusAlterado);
        var envelope = SerializacaoEventos.Desserializar<PedidoStatusAlterado>(evento.Conteudo);
        envelope.IdCorrelacao.Should().Be(pedido.Id);
        envelope.Dados.Should().BeEquivalentTo(new PedidoStatusAlterado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            StatusAnterior = StatusPedido.Pendente,
            StatusNovo = StatusPedido.Processando,
            Sequencia = 2,
            Origem = OrigemMudancaStatus.Worker,
            AlteradoEm = Mudanca
        });

        aviso.IdNotificacao.Should().Be(envelope.IdEvento);
        aviso.Tipo.Should().Be("pedido.status-alterado");
        (aviso.PedidoId, aviso.ClienteId, aviso.CriadoEm).Should().Be((pedido.Id, ClienteId, Mudanca));
        using var corpo = JsonDocument.Parse(aviso.Conteudo);
        corpo.RootElement.GetProperty("idNotificacao").GetGuid().Should().Be(envelope.IdEvento);
        corpo.RootElement.GetProperty("dados").GetProperty("sequencia").GetInt32().Should().Be(2);
    }

    [Fact]
    public void Nenhum_campo_proibido_aparece_no_evento_nem_no_aviso()
    {
        var pedido = NovoPedido();
        pedido.MudarStatus(StatusPedido.Cancelado, ClienteId, "Meu CPF é 123.456.789-00, cancelem.", Mudanca);

        var (evento, aviso) = FabricaAvisos.StatusAlterado(pedido, pedido.Historico.Last(), 2, OrigemMudancaStatus.Cliente);

        foreach (var texto in new[] { evento.Conteudo, aviso.Conteudo })
        {
            texto.Should().NotContain("CPF").And.NotContain(ClienteId.ToString()).And.NotContain("motivo");
        }
    }

    [Fact]
    public void A_entrada_da_criacao_nao_gera_evento()
    {
        var pedido = NovoPedido();

        var criar = () => FabricaAvisos.StatusAlterado(pedido, pedido.Historico.Single(), 1, OrigemMudancaStatus.Cliente);

        criar.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Aviso_do_estorno_usa_o_identificador_do_pagamento_estornado()
    {
        var pedido = NovoPedido();
        var idEstornado = Guid.CreateVersion7();

        var aviso = FabricaAvisos.EstornoConfirmado(pedido, idEstornado, Mudanca);

        aviso.IdNotificacao.Should().Be(idEstornado);
        aviso.Tipo.Should().Be("pedido.estorno-confirmado");
        using var corpo = JsonDocument.Parse(aviso.Conteudo);
        corpo.RootElement.GetProperty("dados").EnumerateObject().Select(p => p.Name).Should().Equal("pedidoId", "numero", "link");
    }
}
