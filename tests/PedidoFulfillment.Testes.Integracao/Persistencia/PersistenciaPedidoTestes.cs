using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 1 com SQL Server real: o pedido gravado é relido com todos os dados do contrato
/// v1 (SC-001), com o snapshot do produto e o histórico de status.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class PersistenciaPedidoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_gravado_e_relido_com_todos_os_campos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Pedido gravado;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
            var teclado = DadosPersistencia.NovoProduto(preco: 349.90m);
            var mouse = DadosPersistencia.NovoProduto(preco: 129.90m);
            gravado = new Pedido(clienteId, DadosPersistencia.NovoNumero(), [(teclado, 2), (mouse, 1)], DadosPersistencia.Instante);
            contexto.Produtos.AddRange(teclado, mouse);
            contexto.Pedidos.Add(gravado);
            await contexto.SaveChangesAsync(Cancelamento);
        }

        // Um contexto novo simula a aplicação reiniciada (cenário 3).
        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var relido = await leitura.Pedidos.Include(p => p.Itens).Include(p => p.Historico).SingleAsync(Cancelamento);

        relido.Id.Should().Be(gravado.Id);
        relido.Numero.Should().Be(gravado.Numero);
        relido.ClienteId.Should().Be(gravado.ClienteId);
        relido.Status.Should().Be(StatusPedido.Pendente);
        relido.ValorTotal.Should().Be(829.70m);
        relido.Moeda.Should().Be("BRL");
        relido.CriadoEm.Should().Be(DadosPersistencia.Instante);
        relido.CriadoEm.Offset.Should().Be(TimeSpan.Zero);
        relido.AtualizadoEm.Should().Be(DadosPersistencia.Instante);
        relido.Itens.Should().BeEquivalentTo(gravado.Itens);
        var entrada = relido.Historico.Should().ContainSingle().Subject;
        entrada.StatusAnterior.Should().BeNull();
        entrada.StatusNovo.Should().Be(StatusPedido.Pendente);
    }

    [Fact]
    public async Task Item_mantem_nome_e_preco_da_compra_quando_o_catalogo_muda()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Guid produtoId;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var produto = DadosPersistencia.NovoProduto(preco: 50m);
            produtoId = produto.Id;
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), produto));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var produto = await contexto.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);
            produto.AlterarDados("Nome novo", null, 99.99m, DadosPersistencia.Instante.AddDays(1));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var item = await leitura.ItensPedido.SingleAsync(Cancelamento);
        item.NomeProduto.Should().Be("Produto de teste");
        item.PrecoUnitario.Should().Be(50m);
        item.Subtotal.Should().Be(50m);
    }

    [Fact]
    public async Task Mudancas_de_status_gravam_o_historico_em_ordem_e_o_status_como_texto()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Guid pedidoId;
        Guid autorId;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            autorId = await DadosPersistencia.NovoUsuarioAsync(contexto);
            var produto = DadosPersistencia.NovoProduto();
            var pedido = DadosPersistencia.NovoPedido(autorId, produto);
            pedidoId = pedido.Id;
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(pedido);
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await MudarStatusAsync(stringConexao, pedidoId, StatusPedido.Processando, autorId, null);
        await MudarStatusAsync(stringConexao, pedidoId, StatusPedido.Cancelado, autorId, "Cliente desistiu");

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var historico = await leitura.HistoricosStatusPedido.Where(h => h.PedidoId == pedidoId).OrderBy(h => h.Id).ToListAsync(Cancelamento);
        historico.Select(h => (h.StatusAnterior, h.StatusNovo)).Should().Equal(
            (null, StatusPedido.Pendente),
            (StatusPedido.Pendente, StatusPedido.Processando),
            (StatusPedido.Processando, StatusPedido.Cancelado));
        historico[^1].Motivo.Should().Be("Cliente desistiu");
        historico[^1].AutorId.Should().Be(autorId);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Status FROM Pedidos WHERE Id = '{pedidoId}'"))
            .Should().Be("Cancelado");
    }

    [Fact]
    public async Task Valores_no_limite_sao_relidos_sem_perda_de_precisao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var produto = DadosPersistencia.NovoProduto(preco: 999_999_999.99m);
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), produto));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        (await leitura.Pedidos.SingleAsync(Cancelamento)).ValorTotal.Should().Be(999_999_999.99m);
        (await leitura.Set<Produto>().SingleAsync(Cancelamento)).Preco.Should().Be(999_999_999.99m);
    }

    private static async Task MudarStatusAsync(string stringConexao, Guid pedidoId, StatusPedido novo, Guid autorId, string? motivo)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var pedido = await contexto.Pedidos.SingleAsync(p => p.Id == pedidoId, Cancelamento);
        pedido.MudarStatus(novo, autorId, motivo, DadosPersistencia.Instante.AddMinutes(1));
        await contexto.SaveChangesAsync(Cancelamento);
    }
}
