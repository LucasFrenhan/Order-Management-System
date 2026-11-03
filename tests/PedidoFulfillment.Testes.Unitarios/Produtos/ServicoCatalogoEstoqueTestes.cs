using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>REQ-22, User Story 3: ajuste por variação, limites e reavaliação sobre o estoque atual.</summary>
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
    public async Task Conflito_reavalia_a_variacao_sobre_o_estoque_atual()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 100);
        Armazenamento.ConflitosRestantes = 2;
        // A cada conflito, "a reserva" tirou uma unidade do produto gravado.
        Armazenamento.AoConflitar = p => p.AjustarEstoque(-1, Agora);

        var resultado = await Ajustar(produto.Id, 50, "Reposição do fornecedor");

        resultado.Valor!.QuantidadeEstoque.Should().Be(148, "100 - 2 reservas + 50, sem perder nenhuma");
        IdsDeLog().Should().Equal(12006, 12006, 12003);
        var log = Logger.Eventos.Single(e => e.Id.Id == 12003);
        (log.Propriedades["Variacao"], log.Propriedades["Motivo"], log.Propriedades["QuantidadeEstoque"], log.Propriedades["UsuarioId"])
            .Should().Be((50, "Reposição do fornecedor", 148, Admin));
    }

    [Fact]
    public async Task Tentativas_esgotadas_dao_409()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", estoque: 10);
        Armazenamento.ConflitosRestantes = 3;

        (await Ajustar(produto.Id, 1)).Falha!.Codigo.Should().Be(CodigosErro.ProdutoAlteradoPorOutraOperacao);
    }
}
