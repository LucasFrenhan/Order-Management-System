using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using static PedidoFulfillment.Testes.Unitarios.Pedidos.ServicoPedidosCriacaoTestes;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>User Story 3: visibilidade da consulta (FR-014, FR-015); histórico e entrega (REQ-11, FR-002 a FR-004).</summary>
public sealed class ServicoPedidosConsultaTestes
{
    private static readonly Guid Dono = Guid.CreateVersion7();
    private static readonly Guid Outro = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly Pedido pedido;

    public ServicoPedidosConsultaTestes()
    {
        var produto = new Produto("SKU-A", "Produto A", null, 10m, 10, true, Agora);
        pedido = new Pedido(Dono, "PED-20261022-000001", [(produto, 1)], Agora);
        pedido.MudarStatus(StatusPedido.Processando, Outro, null, Agora.AddMinutes(5));
        armazenamento.GuardarPedido(pedido.Id, () => pedido);
    }

    private ServicoPedidos Servico() => new(armazenamento, new GeradorNumeroPedidoFalso(), new RelogioFixo(Agora), new LoggerFalso<ServicoPedidos>());

    private Task<Resultado<PedidoResponseDTO>> Obter(Guid pedidoId, Guid usuarioId, params PerfilUsuario[] perfis) =>
        Servico().ObterAsync(pedidoId, usuarioId, perfis, CancellationToken.None);

    [Fact]
    public async Task Dono_ve_o_estado_atual()
    {
        var resultado = await Obter(pedido.Id, Dono, PerfilUsuario.Cliente);

        resultado.Valor!.Id.Should().Be(pedido.Id);
        resultado.Valor.Status.Should().Be(StatusPedido.Processando);
        resultado.Valor.AtualizadoEm.Should().Be(Agora.AddMinutes(5));
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Operacao_ve_pedido_de_qualquer_cliente(PerfilUsuario perfil)
    {
        (await Obter(pedido.Id, Outro, perfil)).Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Usuario_com_cliente_e_admin_ve_qualquer_pedido()
    {
        (await Obter(pedido.Id, Outro, PerfilUsuario.Cliente, PerfilUsuario.Admin)).Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Pedido_de_outro_cliente_e_pedido_inexistente_recebem_a_mesma_falha()
    {
        var deOutro = await Obter(pedido.Id, Outro, PerfilUsuario.Cliente);
        var inexistente = await Obter(Guid.CreateVersion7(), Dono, PerfilUsuario.Cliente);

        deOutro.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
        inexistente.Falha.Should().BeEquivalentTo(deOutro.Falha);
    }

    [Fact]
    public async Task Historico_vem_em_ordem_cronologica_com_o_motivo()
    {
        pedido.MudarStatus(StatusPedido.ProntoParaEnvio, Outro, "Separado.", Agora.AddMinutes(9));

        var historico = (await Obter(pedido.Id, Dono, PerfilUsuario.Cliente)).Valor!.Historico;

        historico.Select(h => (h.StatusAnterior, h.StatusNovo, h.OcorridoEm, h.Motivo)).Should().Equal(
            ((StatusPedido?)null, StatusPedido.Pendente, Agora, (string?)null),
            (StatusPedido.Pendente, StatusPedido.Processando, Agora.AddMinutes(5), null),
            (StatusPedido.Processando, StatusPedido.ProntoParaEnvio, Agora.AddMinutes(9), "Separado."));
    }

    [Theory]
    [InlineData(PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Resposta_nao_expoe_o_autor_para_nenhum_perfil(PerfilUsuario perfil)
    {
        var resposta = (await Obter(pedido.Id, perfil == PerfilUsuario.Cliente ? Dono : Outro, perfil)).Valor!;

        var json = JsonSerializer.Serialize(resposta, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().NotContainEquivalentOf("autor").And.NotContain(Outro.ToString());
    }

    [Fact]
    public async Task Sem_ordem_a_entrega_vem_nula()
    {
        (await Obter(pedido.Id, Dono, PerfilUsuario.Cliente)).Valor!.Entrega.Should().BeNull();
    }

    [Fact]
    public async Task Ordem_sem_despacho_vem_sem_transportadora_rastreio_e_data()
    {
        var ordem = GuardarOrdem(Agora.AddMinutes(7));

        var entrega = (await Obter(pedido.Id, Dono, PerfilUsuario.Cliente)).Valor!.Entrega!;

        entrega.Should().Be(new EntregaPedidoResponseDTO { CriadoEm = ordem.CriadoEm });
    }

    [Fact]
    public async Task Ordem_despachada_vem_com_transportadora_rastreio_e_data()
    {
        var ordem = GuardarOrdem(Agora.AddMinutes(7));
        ordem.RegistrarDespacho("Correios", "BR123456789BR", Agora.AddMinutes(30));

        var entrega = (await Obter(pedido.Id, Outro, PerfilUsuario.OperadorLogistica)).Valor!.Entrega!;

        entrega.Should().Be(new EntregaPedidoResponseDTO
        {
            Transportadora = "Correios",
            CodigoRastreio = "BR123456789BR",
            DespachadoEm = Agora.AddMinutes(30),
            CriadoEm = Agora.AddMinutes(7)
        });
    }

    private OrdemEntrega GuardarOrdem(DateTimeOffset instante)
    {
        var ordem = new OrdemEntrega(pedido.Id, instante);
        typeof(Pedido).GetProperty(nameof(Pedido.OrdemEntrega))!.SetValue(pedido, ordem);
        return ordem;
    }
}
