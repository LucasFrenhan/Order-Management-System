using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 3: pagamento e ordem de entrega pertencem ao pedido, no máximo um de cada
/// (FR-012, FR-013, SC-004).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class PagamentoEntregaTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static DateTimeOffset Depois => DadosPersistencia.Instante.AddMinutes(1);

    [Fact]
    public async Task Pagamento_gravado_e_lido_a_partir_do_pedido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        var pagamento = new Pagamento(pedidoId, 99.90m, DadosPersistencia.Instante);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, "TX-1", Depois);
        await GravarAsync(stringConexao, pagamento);

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var relido = (await leitura.Pedidos.Include(p => p.Pagamento).SingleAsync(Cancelamento)).Pagamento!;
        relido.Valor.Should().Be(99.90m);
        relido.Situacao.Should().Be(SituacaoPagamento.Aprovado);
        relido.IdentificadorTransacao.Should().Be("TX-1");
        relido.ProcessadoEm.Should().Be(Depois);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, "SELECT Situacao FROM Pagamentos")).Should().Be("Aprovado");
    }

    [Fact]
    public async Task Estorno_do_pagamento_e_gravado_e_lido_com_todos_os_campos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        var pagamento = new Pagamento(pedidoId, 99.90m, DadosPersistencia.Instante);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, "TX-1", Depois);
        pagamento.SolicitarEstorno(Depois.AddMinutes(1));
        pagamento.RegistrarFalhaEstorno(Depois.AddMinutes(2));
        await GravarAsync(stringConexao, pagamento);

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var pendente = (await contexto.Pedidos.Include(p => p.Pagamento).SingleAsync(Cancelamento)).Pagamento!;
            pendente.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
            pendente.EstornoSolicitadoEm.Should().Be(Depois.AddMinutes(1));
            pendente.TentativasEstorno.Should().Be(1);
            pendente.UltimaFalhaEstornoEm.Should().Be(Depois.AddMinutes(2));
            pendente.ConfirmarEstorno("SIM-EST-1", Depois.AddMinutes(3));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var estornado = await leitura.Set<Pagamento>().SingleAsync(Cancelamento);
        estornado.Situacao.Should().Be(SituacaoPagamento.Estornado);
        estornado.IdentificadorEstorno.Should().Be("SIM-EST-1");
        estornado.EstornadoEm.Should().Be(Depois.AddMinutes(3));
        estornado.IdentificadorTransacao.Should().Be("TX-1");
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, "SELECT Situacao FROM Pagamentos")).Should().Be("Estornado");
    }

    [Fact]
    public async Task Pagamento_antigo_ganha_zero_tentativas_e_ordem_aceita_o_instante_do_cancelamento()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        await GravarAsync(stringConexao, new Pagamento(pedidoId, 10m, DadosPersistencia.Instante));
        await GravarAsync(stringConexao, new OrdemEntrega(pedidoId, DadosPersistencia.Instante));

        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE OrdensEntrega SET CanceladaEm = '2026-10-24T12:00:00+00:00'");

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT TentativasEstorno FROM Pagamentos")).Should().Be(0);
        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        (await leitura.OrdensEntrega.SingleAsync(Cancelamento)).CanceladaEm
            .Should().Be(new DateTimeOffset(2026, 10, 24, 12, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Segundo_pagamento_do_mesmo_pedido_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        await GravarAsync(stringConexao, new Pagamento(pedidoId, 10m, DadosPersistencia.Instante));

        var gravar = () => GravarAsync(stringConexao, new Pagamento(pedidoId, 10m, DadosPersistencia.Instante));

        await DeveViolarUnicidadeAsync(gravar);
    }

    [Fact]
    public async Task Ordem_de_entrega_nasce_sem_despacho_e_recebe_o_despacho()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        await GravarAsync(stringConexao, new OrdemEntrega(pedidoId, DadosPersistencia.Instante));

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var pedido = await contexto.Pedidos.Include(p => p.OrdemEntrega).SingleAsync(Cancelamento);
            var ordem = pedido.OrdemEntrega!;
            ordem.Transportadora.Should().BeNull();
            ordem.CodigoRastreio.Should().BeNull();
            ordem.DespachadoEm.Should().BeNull();

            ordem.RegistrarDespacho("Transportadora X", "BR123", Depois);
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var despachada = await leitura.OrdensEntrega.SingleAsync(Cancelamento);
        despachada.Transportadora.Should().Be("Transportadora X");
        despachada.CodigoRastreio.Should().Be("BR123");
        despachada.DespachadoEm.Should().Be(Depois);
    }

    [Fact]
    public async Task Segunda_ordem_de_entrega_do_mesmo_pedido_e_recusada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        await GravarAsync(stringConexao, new OrdemEntrega(pedidoId, DadosPersistencia.Instante));

        var gravar = () => GravarAsync(stringConexao, new OrdemEntrega(pedidoId, DadosPersistencia.Instante));

        await DeveViolarUnicidadeAsync(gravar);
    }

    [Fact]
    public async Task Codigo_de_rastreio_e_unico_por_transportadora()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, primeiroPedido) = await PedidoGravadoAsync();
        var outrosPedidos = new List<Guid>();
        for (var i = 0; i < 4; i++)
        {
            outrosPedidos.Add(await NovoPedidoAsync(stringConexao));
        }

        await GravarAsync(stringConexao, Despachada(primeiroPedido, "Transportadora X", "BR123"));

        var mesmaTransportadora = () => GravarAsync(stringConexao, Despachada(outrosPedidos[0], "Transportadora X", "BR123"));
        await DeveViolarUnicidadeAsync(mesmaTransportadora);
        await GravarAsync(stringConexao, Despachada(outrosPedidos[1], "Transportadora Y", "BR123"));
        await GravarAsync(stringConexao, new OrdemEntrega(outrosPedidos[2], DadosPersistencia.Instante));
        await GravarAsync(stringConexao, new OrdemEntrega(outrosPedidos[3], DadosPersistencia.Instante));
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM OrdensEntrega WHERE CodigoRastreio IS NULL")).Should().Be(2);
    }

    [Fact]
    public async Task Pagamento_e_ordem_de_pedido_inexistente_sao_recusados()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        var pagamento = () => GravarAsync(stringConexao, new Pagamento(Guid.CreateVersion7(), 10m, DadosPersistencia.Instante));
        var ordem = () => GravarAsync(stringConexao, new OrdemEntrega(Guid.CreateVersion7(), DadosPersistencia.Instante));

        (await pagamento.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoRestricao(e));
        (await ordem.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoRestricao(e));
    }

    [Theory]
    [InlineData("UPDATE Pagamentos SET Situacao = N'Cancelado'", "CK_Pagamentos_SituacaoValida")]
    [InlineData("UPDATE Pagamentos SET Valor = -1", "CK_Pagamentos_ValorNaoNegativo")]
    [InlineData("UPDATE Pagamentos SET TentativasEstorno = -1", "CK_Pagamentos_TentativasEstornoNaoNegativas")]
    public async Task Pagamento_invalido_gravado_por_fora_do_dominio_e_recusado(string sql, string restricao)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId) = await PedidoGravadoAsync();
        await GravarAsync(stringConexao, new Pagamento(pedidoId, 10m, DadosPersistencia.Instante));

        var executar = () => BancoDadosTeste.ExecutarAsync(stringConexao, sql);

        var excecao = (await executar.Should().ThrowAsync<SqlException>()).Which;
        excecao.Number.Should().Be(547);
        excecao.Message.Should().Contain(restricao);
    }

    private static OrdemEntrega Despachada(Guid pedidoId, string transportadora, string codigo)
    {
        var ordem = new OrdemEntrega(pedidoId, DadosPersistencia.Instante);
        ordem.RegistrarDespacho(transportadora, codigo, Depois);
        return ordem;
    }

    private static async Task DeveViolarUnicidadeAsync(Func<Task> gravar) =>
        (await gravar.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoUnicidade(e));

    private static async Task GravarAsync(string stringConexao, object entidade)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        contexto.Add(entidade);
        await contexto.SaveChangesAsync(Cancelamento);
    }

    private async Task<(string StringConexao, Guid PedidoId)> PedidoGravadoAsync()
    {
        var stringConexao = await banco.NovoBancoMigradoAsync();
        return (stringConexao, await NovoPedidoAsync(stringConexao));
    }

    private static async Task<Guid> NovoPedidoAsync(string stringConexao)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), produto);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Cancelamento);
        return pedido.Id;
    }
}
