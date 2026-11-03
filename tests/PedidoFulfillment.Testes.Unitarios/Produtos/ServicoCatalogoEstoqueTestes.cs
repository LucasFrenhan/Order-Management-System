using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>
/// REQ-22, User Story 3: ajuste por variação, limites e log. A soma atômica sob concorrência é
/// provada no teste de integração (<c>EstoqueConcorrenteTestes</c>), com o banco real.
/// </summary>
public sealed class ServicoCatalogoEstoqueTestes : ServicoCatalogoTestesBase
{
    private Task<Resultado<ProdutoResponseDTO>> Ajustar(Guid id, int variacao, string motivo = "Reposição") =>
        Servico().AjustarEstoqueAsync(id, new AjustarEstoqueRequestDTO { Variacao = variacao, Motivo = motivo }, Admin, CancellationToken.None);

    [Theory]
    [InlineData(5, 15)]
    [InlineData(-3, 7)]
    [InlineData(-10, 0)]
    public async Task Reposicao_e_baixa(int variacao, int esperado)
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 10);

        (await Ajustar(produto.Id, variacao)).Valor!.QuantidadeEstoque.Should().Be(esperado);
        Armazenamento.Gravado(produto.Id).QuantidadeEstoque.Should().Be(esperado);
    }

    [Fact]
    public async Task Estoque_negativo_da_422_sem_gravar()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 2);

        var resultado = await Ajustar(produto.Id, -3);

        resultado.Falha.Should().BeEquivalentTo(new
        {
            Tipo = TipoFalha.RegraNegocio,
            Codigo = CodigosErro.EstoqueInsuficiente,
            Detalhe = "O ajuste deixaria o estoque do produto CAF-250 negativo (atual: 2, variação: -3)."
        });
        Armazenamento.Gravado(produto.Id).QuantidadeEstoque.Should().Be(2);
        Armazenamento.Gravacoes.Should().Be(0);
        IdsDeLog().Should().BeEmpty();
    }

    [Fact]
    public async Task Estoque_acima_do_limite_da_422()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 999_999_999);

        var resultado = await Ajustar(produto.Id, 2);

        resultado.Falha!.Codigo.Should().Be(CodigosErro.EstoqueAcimaDoLimite);
        resultado.Falha.Detalhe.Should().Be("O ajuste deixaria o estoque do produto CAF-250 acima de 1000000000.");
    }

    [Fact]
    public async Task Produto_inexistente_da_404()
    {
        (await Ajustar(Guid.NewGuid(), 1)).Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
    }

    [Fact]
    public async Task Log_12003_e_o_historico_do_ajuste()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 100);

        await Ajustar(produto.Id, 50, "Reposição do fornecedor");

        var log = Logger.Eventos.Single(e => e.Id.Id == 12003);
        (log.Propriedades["Variacao"], log.Propriedades["Motivo"], log.Propriedades["QuantidadeEstoque"], log.Propriedades["UsuarioId"])
            .Should().Be((50, "Reposição do fornecedor", 150, Admin));
    }
}
