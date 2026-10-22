using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 2: restrições do produto no SQL Server real (FR-001, FR-002, FR-004, SC-004).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class RestricoesProdutoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("UPDATE Produtos SET QuantidadeEstoque = -1", "CK_Produtos_EstoqueNaoNegativo")]
    [InlineData("UPDATE Produtos SET Preco = -0.01", "CK_Produtos_PrecoNaoNegativo")]
    public async Task Estoque_ou_preco_negativo_e_recusado(string sql, string restricao)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await GravarAsync(stringConexao, DadosPersistencia.NovoProduto());

        var executar = () => BancoDadosTeste.ExecutarAsync(stringConexao, sql);

        var excecao = (await executar.Should().ThrowAsync<SqlException>()).Which;
        excecao.Number.Should().Be(547);
        excecao.Message.Should().Contain(restricao);
    }

    [Fact]
    public async Task Produto_citado_em_pedido_nao_pode_ser_apagado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var citado = DadosPersistencia.NovoProduto();
        var avulso = DadosPersistencia.NovoProduto();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.Produtos.AddRange(citado, avulso);
            contexto.Pedidos.Add(DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), citado));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        var apagarCitado = () => BancoDadosTeste.ExecutarAsync(stringConexao, $"DELETE FROM Produtos WHERE Id = '{citado.Id}'");

        (await apagarCitado.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
        (await BancoDadosTeste.ExecutarAsync(stringConexao, $"DELETE FROM Produtos WHERE Id = '{avulso.Id}'")).Should().Be(1);
    }

    [Fact]
    public async Task Sku_e_unico_sem_diferenciar_maiusculas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await GravarAsync(stringConexao, DadosPersistencia.NovoProduto("abc-1"));

        var gravar = () => GravarAsync(stringConexao, DadosPersistencia.NovoProduto("ABC-1"));

        (await gravar.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoUnicidade(e));
    }

    private static async Task GravarAsync(string stringConexao, Domain.Produtos.Produto produto)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
    }
}
