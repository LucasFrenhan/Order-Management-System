using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Notificacoes;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>Filtro, limite e fila das conexões SSE (REQ-18, FR-020, FR-023; research R-16).</summary>
public sealed class DistribuidorAvisosTestes
{
    private static readonly Guid ClienteA = Guid.CreateVersion7();
    private static readonly Guid ClienteB = Guid.CreateVersion7();
    private static readonly Guid PedidoA1 = Guid.CreateVersion7();
    private static readonly Guid PedidoA2 = Guid.CreateVersion7();
    private static readonly Guid PedidoB = Guid.CreateVersion7();

    private static DistribuidorAvisos Distribuidor(int limite = 3) =>
        new(Options.Create(new OpcoesNotificacoes { Sse = new OpcoesSseNotificacoes { ConexoesPorUsuario = limite } }));

    private static AvisoLido Aviso(ulong posicao, Guid clienteId, Guid pedidoId) =>
        new(1, posicao, Guid.CreateVersion7(), "pedido.status-alterado", pedidoId, clienteId, "{}", DateTimeOffset.UtcNow, null);

    private static List<ulong> Lidos(ConexaoEventos conexao)
    {
        var lidos = new List<ulong>();
        while (conexao.Fila.Reader.TryRead(out var aviso))
        {
            lidos.Add(aviso.Posicao);
        }

        return lidos;
    }

    [Fact]
    public void Cliente_ve_so_os_proprios_operacao_ve_todos_e_a_conexao_de_um_pedido_so_ele()
    {
        var distribuidor = Distribuidor();
        var cliente = distribuidor.Registrar(ClienteA, operacao: false, pedidoId: null)!;
        var doPedido = distribuidor.Registrar(ClienteA, operacao: false, pedidoId: PedidoA2)!;
        var operacao = distribuidor.Registrar(Guid.CreateVersion7(), operacao: true, pedidoId: null)!;

        distribuidor.Publicar([Aviso(1, ClienteA, PedidoA1), Aviso(2, ClienteB, PedidoB), Aviso(3, ClienteA, PedidoA2)]);

        Lidos(cliente).Should().Equal(1UL, 3UL);
        Lidos(doPedido).Should().Equal(3UL);
        Lidos(operacao).Should().Equal(1UL, 2UL, 3UL);
    }

    [Fact]
    public void O_limite_por_usuario_recusa_a_quarta_e_libera_a_vaga_quando_uma_fecha()
    {
        var distribuidor = Distribuidor(limite: 3);
        var abertas = Enumerable.Range(0, 3).Select(_ => distribuidor.Registrar(ClienteA, false, null)).ToList();

        distribuidor.Registrar(ClienteA, false, null).Should().BeNull();
        distribuidor.Registrar(ClienteB, false, null).Should().NotBeNull("o limite é por usuário");

        distribuidor.Remover(abertas[0]!);
        distribuidor.Registrar(ClienteA, false, null).Should().NotBeNull();
    }

    [Fact]
    public void Fila_cheia_marca_a_conexao_como_lenta_e_fecha_a_fila()
    {
        var distribuidor = Distribuidor();
        var conexao = distribuidor.Registrar(ClienteA, false, null)!;

        distribuidor.Publicar([.. Enumerable.Range(1, ConexaoEventos.CapacidadeFila + 1).Select(i => Aviso((ulong)i, ClienteA, PedidoA1))]);

        conexao.Lenta.Should().BeTrue();
        Lidos(conexao).Should().HaveCount(ConexaoEventos.CapacidadeFila);
        conexao.Fila.Reader.Completion.IsCompleted.Should().BeTrue();
    }

    [Fact]
    public void Conexao_removida_nao_recebe_mais_nada()
    {
        var distribuidor = Distribuidor();
        var conexao = distribuidor.Registrar(ClienteA, false, null)!;

        distribuidor.Remover(conexao);
        distribuidor.Publicar([Aviso(1, ClienteA, PedidoA1)]);

        Lidos(conexao).Should().BeEmpty();
    }
}
