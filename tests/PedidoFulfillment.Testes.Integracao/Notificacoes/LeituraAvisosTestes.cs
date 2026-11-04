using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Persistencia;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Leitura dos avisos por cursor de <c>rowversion</c>, sem pular o aviso de uma transação lenta
/// (REQ-18, research R-13; caso de borda "transação lenta"; FR-024).
/// </summary>
public sealed class LeituraAvisosTestes(BancoDadosTeste banco)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<(Guid ClienteId, Guid PedidoId)> SemearPedidoAsync(string stringConexao, Guid? clienteExistente = null)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = clienteExistente ?? await DadosPersistencia.NovoUsuarioAsync(contexto);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Token);
        return (clienteId, pedido.Id);
    }

    private static AvisoPedido NovoAviso(Guid clienteId, Guid pedidoId) =>
        AvisoPedido.Criar(Guid.CreateVersion7(), "pedido.status-alterado", pedidoId, clienteId, "{\"teste\":true}", DadosPersistencia.Instante);

    private static async Task<AvisoPedido> GravarAvisoAsync(string stringConexao, Guid clienteId, Guid pedidoId)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var aviso = NovoAviso(clienteId, pedidoId);
        contexto.AvisosPedido.Add(aviso);
        await contexto.SaveChangesAsync(Token);
        return aviso;
    }

    [Fact]
    public async Task Le_em_ordem_so_depois_do_cursor_e_ate_o_lote()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (clienteId, pedidoId) = await SemearPedidoAsync(stringConexao);
        var avisos = new List<AvisoPedido>();
        for (var i = 0; i < 4; i++)
        {
            avisos.Add(await GravarAvisoAsync(stringConexao, clienteId, pedidoId));
        }

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var leitura = new LeituraAvisos(contexto);

        var primeiros = await leitura.LerAsync(0, 3, Token);
        primeiros.Select(a => a.IdNotificacao).Should().Equal(avisos.Take(3).Select(a => a.IdNotificacao));
        primeiros.Select(a => a.Posicao).Should().BeInAscendingOrder();
        var resto = await leitura.LerAsync(primeiros[^1].Posicao, 3, Token);
        resto.Select(a => a.IdNotificacao).Should().Equal(avisos[3].IdNotificacao);
        (await leitura.LerAsync(resto[0].Posicao, 3, Token)).Should().BeEmpty();
        resto[0].Conteudo.Should().Be("{\"teste\":true}");
        resto[0].ClienteId.Should().Be(clienteId);
    }

    [Fact]
    public async Task Transacao_lenta_segura_os_avisos_seguintes_ate_confirmar_e_nenhum_e_pulado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (clienteId, pedidoId) = await SemearPedidoAsync(stringConexao);

        // A: inserido numa transação que fica aberta.
        await using var lento = BancoDadosTeste.NovoContexto(stringConexao);
        await using var transacao = await lento.Database.BeginTransactionAsync(Token);
        var avisoA = NovoAviso(clienteId, pedidoId);
        lento.AvisosPedido.Add(avisoA);
        await lento.SaveChangesAsync(Token);

        // B: inserido e confirmado depois, com posição maior.
        var avisoB = await GravarAvisoAsync(stringConexao, clienteId, pedidoId);

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            (await new LeituraAvisos(contexto).LerAsync(0, 10, Token)).Should().BeEmpty("B está acima do MIN_ACTIVE_ROWVERSION enquanto A não confirma");
        }

        await transacao.CommitAsync(Token);

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            (await new LeituraAvisos(contexto).LerAsync(0, 10, Token)).Select(a => a.IdNotificacao)
                .Should().Equal(avisoA.IdNotificacao, avisoB.IdNotificacao);
        }
    }

    [Fact]
    public async Task Leituras_por_Cliente_e_por_pedido_filtram()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (clienteA, pedidoA1) = await SemearPedidoAsync(stringConexao);
        var (_, pedidoA2) = await SemearPedidoAsync(stringConexao, clienteA);
        var (clienteB, pedidoB) = await SemearPedidoAsync(stringConexao);
        var a1 = await GravarAvisoAsync(stringConexao, clienteA, pedidoA1);
        await GravarAvisoAsync(stringConexao, clienteB, pedidoB);
        var a2 = await GravarAvisoAsync(stringConexao, clienteA, pedidoA2);

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var leitura = new LeituraAvisos(contexto);

        (await leitura.LerDoClienteAsync(clienteA, 0, 10, Token)).Select(a => a.IdNotificacao).Should().Equal(a1.IdNotificacao, a2.IdNotificacao);
        (await leitura.LerDoPedidoAsync(pedidoA2, 0, 10, Token)).Select(a => a.IdNotificacao).Should().Equal(a2.IdNotificacao);
    }

    [Fact]
    public async Task Posicao_de_um_aviso_e_a_ultima_confirmada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        await using (var vazio = BancoDadosTeste.NovoContexto(stringConexao))
        {
            (await new LeituraAvisos(vazio).UltimaConfirmadaAsync(Token)).Should().BeNull();
        }

        var (clienteId, pedidoId) = await SemearPedidoAsync(stringConexao);
        await GravarAvisoAsync(stringConexao, clienteId, pedidoId);
        var ultimo = await GravarAvisoAsync(stringConexao, clienteId, pedidoId);

        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var leitura = new LeituraAvisos(contexto);
        var posicao = await leitura.PosicaoDeAsync(ultimo.IdNotificacao, Token);

        posicao.Should().NotBeNull();
        (await leitura.PosicaoDeAsync(Guid.CreateVersion7(), Token)).Should().BeNull();
        (await leitura.UltimaConfirmadaAsync(Token)).Should().Be((posicao!.Value, ultimo.IdNotificacao));
        (await leitura.PosicaoAtualAsync(Token)).Should().BeGreaterThanOrEqualTo(posicao.Value);
        (await leitura.LerAsync(await leitura.PosicaoAtualAsync(Token), 10, Token)).Should().BeEmpty();
    }
}
