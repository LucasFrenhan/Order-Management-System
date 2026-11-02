using FluentAssertions;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// A base de consumo recusa uma mudança de status que não passou pelo <c>SalvarAsync</c>, e a que
/// passou segue normalmente (REQ-18, R-14). No banco real, porque a guarda olha o contexto do EF.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class GuardaMudancaStatusTestes(BancoDadosTeste banco)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static async Task<Guid> SemearAsync(string stringConexao)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Token);
        return pedido.Id;
    }

    [Fact]
    public async Task Mudanca_fora_do_SalvarAsync_e_recusada_e_a_do_SalvarAsync_passa_com_evento_e_aviso()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;

        pedido.MudarStatus(StatusPedido.Processando, null, "Pagamento aprovado.", DadosPersistencia.Instante.AddMinutes(1));
        var guarda = () => GuardaMudancaStatus.ExigirGravadaPeloArmazenamento(contexto);

        guarda.Should().Throw<InvalidOperationException>().WithMessage("*REQ-18*");

        await armazenamento.SalvarAsync(Token);
        guarda.Should().NotThrow();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM EventosSaida WHERE Tipo = 'pedido.status-alterado'")).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM AvisosPedido WHERE PedidoId = '{pedidoId}'")).Should().Be(1);
    }

    [Fact]
    public async Task Gravacao_repetida_sem_descarte_nao_duplica_o_aviso()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var pedidoId = await SemearAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
        pedido.MudarStatus(StatusPedido.Processando, null, "Pagamento aprovado.", DadosPersistencia.Instante.AddMinutes(1));

        await armazenamento.SalvarAsync(Token);
        await armazenamento.SalvarAsync(Token);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM AvisosPedido WHERE PedidoId = '{pedidoId}'")).Should().Be(1);
    }
}
