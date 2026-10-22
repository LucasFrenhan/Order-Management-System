using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// Restrições do pedido no SQL Server real (SC-004). Quando o domínio impediria o dado, a
/// gravação é feita por SQL direto: o banco precisa recusar também (FR-002).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class RestricoesPedidoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_de_usuario_inexistente_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto();
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(DadosPersistencia.NovoPedido(Guid.CreateVersion7(), produto));

        var gravar = () => contexto.SaveChangesAsync(Cancelamento);

        (await gravar.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoRestricao(e));
    }

    [Fact]
    public async Task Item_de_produto_inexistente_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId, _) = await PedidoGravadoAsync();

        var inserir = () => BancoDadosTeste.ExecutarAsync(stringConexao,
            $"INSERT INTO ItensPedido (PedidoId, ProdutoId, NomeProduto, Quantidade, PrecoUnitario, Subtotal) VALUES ('{pedidoId}', '{Guid.CreateVersion7()}', N'X', 1, 1, 1)");

        (await inserir.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
    }

    [Fact]
    public async Task Numero_repetido_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        var produto = DadosPersistencia.NovoProduto();
        var numero = DadosPersistencia.NovoNumero();
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(new Pedido(clienteId, numero, [(produto, 1)], DadosPersistencia.Instante));
        await contexto.SaveChangesAsync(Cancelamento);
        contexto.Pedidos.Add(new Pedido(clienteId, numero, [(produto, 1)], DadosPersistencia.Instante));

        var gravar = () => contexto.SaveChangesAsync(Cancelamento);

        (await gravar.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhViolacaoUnicidade(e));
    }

    [Fact]
    public async Task Mesmo_produto_duas_vezes_no_pedido_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId, produtoId) = await PedidoGravadoAsync();

        var inserir = () => BancoDadosTeste.ExecutarAsync(stringConexao,
            $"INSERT INTO ItensPedido (PedidoId, ProdutoId, NomeProduto, Quantidade, PrecoUnitario, Subtotal) VALUES ('{pedidoId}', '{produtoId}', N'X', 1, 1, 1)");

        (await inserir.Should().ThrowAsync<SqlException>()).Which.Number.Should().BeOneOf(2601, 2627);
    }

    [Theory]
    [InlineData("UPDATE ItensPedido SET Quantidade = 0", "CK_ItensPedido_QuantidadePositiva")]
    [InlineData("UPDATE Pedidos SET Status = N'Desconhecido'", "CK_Pedidos_StatusValido")]
    [InlineData("UPDATE HistoricosStatusPedido SET StatusNovo = N'Desconhecido'", "CK_HistoricosStatusPedido_StatusValido")]
    [InlineData("UPDATE HistoricosStatusPedido SET StatusAnterior = N'Desconhecido'", "CK_HistoricosStatusPedido_StatusValido")]
    [InlineData("UPDATE Pedidos SET ValorTotal = -1", "CK_Pedidos_ValorTotalNaoNegativo")]
    [InlineData("UPDATE ItensPedido SET PrecoUnitario = -1", "CK_ItensPedido_PrecoNaoNegativo")]
    [InlineData("UPDATE ItensPedido SET Subtotal = -1", "CK_ItensPedido_SubtotalNaoNegativo")]
    public async Task Dado_invalido_gravado_por_fora_do_dominio_e_recusado(string sql, string restricao)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, _, _) = await PedidoGravadoAsync();

        var executar = () => BancoDadosTeste.ExecutarAsync(stringConexao, sql);

        var excecao = (await executar.Should().ThrowAsync<SqlException>()).Which;
        excecao.Number.Should().Be(547);
        excecao.Message.Should().Contain(restricao);
    }

    [Fact]
    public async Task Status_nulo_e_recusado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, _, _) = await PedidoGravadoAsync();

        var executar = () => BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE Pedidos SET Status = NULL");

        // 515: coluna obrigatória.
        (await executar.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(515);
    }

    [Fact]
    public async Task Usuario_com_pedidos_nao_pode_ser_apagado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId, _) = await PedidoGravadoAsync();

        var apagar = () => BancoDadosTeste.ExecutarAsync(stringConexao,
            $"DELETE FROM Usuarios WHERE Id = (SELECT ClienteId FROM Pedidos WHERE Id = '{pedidoId}')");

        (await apagar.Should().ThrowAsync<SqlException>()).Which.Number.Should().Be(547);
    }

    [Fact]
    public async Task Apagar_o_pedido_apaga_itens_historico_pagamento_e_entrega()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var (stringConexao, pedidoId, _) = await PedidoGravadoAsync();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.Pagamentos.Add(new Pagamento(pedidoId, 10m, DadosPersistencia.Instante));
            contexto.OrdensEntrega.Add(new OrdemEntrega(pedidoId, DadosPersistencia.Instante));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await BancoDadosTeste.ExecutarAsync(stringConexao, $"DELETE FROM Pedidos WHERE Id = '{pedidoId}'");

        foreach (var tabela in new[] { "ItensPedido", "HistoricosStatusPedido", "Pagamentos", "OrdensEntrega" })
        {
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM {tabela}")).Should().Be(0, tabela);
        }
    }

    [Fact]
    public async Task Pedido_com_item_invalido_nao_grava_nada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        var valido = DadosPersistencia.NovoProduto();
        var removido = DadosPersistencia.NovoProduto();
        contexto.Produtos.AddRange(valido, removido);
        await contexto.SaveChangesAsync(Cancelamento);
        // O produto some entre a leitura e a gravação do pedido.
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"DELETE FROM Produtos WHERE Id = '{removido.Id}'");
        contexto.Pedidos.Add(DadosPersistencia.NovoPedido(clienteId, valido, removido));

        var gravar = () => contexto.SaveChangesAsync(Cancelamento);

        await gravar.Should().ThrowAsync<DbUpdateException>();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Pedidos")).Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM ItensPedido")).Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM HistoricosStatusPedido")).Should().Be(0);
    }

    private async Task<(string StringConexao, Guid PedidoId, Guid ProdutoId)> PedidoGravadoAsync()
    {
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), produto);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Cancelamento);
        return (stringConexao, pedido.Id, produto.Id);
    }
}
