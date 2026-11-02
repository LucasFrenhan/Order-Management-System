using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>Corpo <c>notificacao.v1</c> dos dois tipos (REQ-18, R-09; FR-002 e FR-004).</summary>
public sealed class CorpoNotificacaoTestes
{
    private static readonly DateTimeOffset Instante = new(2026, 11, 1, 14, 0, 7, TimeSpan.Zero);
    private static readonly Guid PedidoId = Guid.Parse("0193f0a4-0000-7000-8000-000000000001");
    private static readonly Guid IdNotificacao = Guid.Parse("0193f0a4-1d2c-7e3f-8a9b-0c1d2e3f4a5b");

    [Fact]
    public void Status_alterado_traz_os_campos_do_schema_e_o_link_da_consulta()
    {
        using var json = JsonDocument.Parse(SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosStatusAlterado>
        {
            IdNotificacao = IdNotificacao,
            Tipo = TiposNotificacao.StatusAlterado,
            Versao = TiposNotificacao.VersaoAtual,
            OcorridoEm = Instante,
            Dados = new DadosStatusAlterado
            {
                PedidoId = PedidoId,
                Numero = "PED-20261101-000042",
                StatusAnterior = StatusPedido.Pendente,
                StatusNovo = StatusPedido.Processando,
                Sequencia = 2,
                Link = SerializacaoNotificacoes.Link(PedidoId)
            }
        }));
        var raiz = json.RootElement;

        raiz.EnumerateObject().Select(p => p.Name).Should().Equal("idNotificacao", "tipo", "versao", "ocorridoEm", "dados");
        raiz.GetProperty("tipo").GetString().Should().Be("pedido.status-alterado");
        raiz.GetProperty("versao").GetInt32().Should().Be(1);
        var dados = raiz.GetProperty("dados");
        dados.EnumerateObject().Select(p => p.Name).Should().Equal("pedidoId", "numero", "statusAnterior", "statusNovo", "sequencia", "link");
        dados.GetProperty("statusNovo").GetString().Should().Be("Processando");
        dados.GetProperty("link").GetString().Should().Be($"/api/v1/pedidos/{PedidoId}");
    }

    [Fact]
    public void Estorno_confirmado_nao_traz_status_nem_sequencia()
    {
        using var json = JsonDocument.Parse(SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosEstornoConfirmado>
        {
            IdNotificacao = IdNotificacao,
            Tipo = TiposNotificacao.EstornoConfirmado,
            Versao = TiposNotificacao.VersaoAtual,
            OcorridoEm = Instante,
            Dados = new DadosEstornoConfirmado { PedidoId = PedidoId, Numero = "PED-20261101-000042", Link = SerializacaoNotificacoes.Link(PedidoId) }
        }));

        json.RootElement.GetProperty("tipo").GetString().Should().Be("pedido.estorno-confirmado");
        json.RootElement.GetProperty("dados").EnumerateObject().Select(p => p.Name).Should().Equal("pedidoId", "numero", "link");
    }
}
