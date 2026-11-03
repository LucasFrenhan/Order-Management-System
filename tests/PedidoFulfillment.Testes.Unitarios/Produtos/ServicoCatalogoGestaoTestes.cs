using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>REQ-22, User Story 2: cadastro, SKU único e alteração com novas tentativas.</summary>
public sealed class ServicoCatalogoGestaoTestes : ServicoCatalogoTestesBase
{
    private static CadastrarProdutoRequestDTO Cadastro(string sku = "CAF-250") =>
        new() { Sku = sku, Nome = "Café torrado", Descricao = "Grãos", Preco = 32.50m, EstoqueInicial = 40 };

    [Fact]
    public async Task Cadastro_cria_ativo_com_a_visao_da_operacao_e_loga_12001()
    {
        var criado = (await Servico().CadastrarAsync(Cadastro(" CAF-250 "), Admin, CancellationToken.None)).Valor!;

        (criado.Sku, criado.Ativo, criado.QuantidadeEstoque, criado.Preco).Should().Be(("CAF-250", true, 40, 32.50m));
        Armazenamento.Produtos.Should().ContainSingle();
        IdsDeLog().Should().Equal(12001);
    }

    [Fact]
    public async Task Sku_repetido_com_outra_caixa_da_409()
    {
        Armazenamento.Guardar("CAF-250", "Outro");

        var resultado = await Servico().CadastrarAsync(Cadastro("caf-250"), Admin, CancellationToken.None);

        resultado.Falha.Should().BeEquivalentTo(new
        {
            Tipo = TipoFalha.Conflito,
            Codigo = CodigosErro.ProdutoSkuDuplicado,
            Detalhe = "Já existe um produto com o SKU caf-250."
        });
    }

    [Fact]
    public async Task Sku_gravado_por_outro_cadastro_no_meio_da_o_mesmo_409()
    {
        Armazenamento.SkuDuplicadoNaProximaGravacao = true;

        var resultado = await Servico().CadastrarAsync(Cadastro(), Admin, CancellationToken.None);

        resultado.Falha!.Codigo.Should().Be(CodigosErro.ProdutoSkuDuplicado);
        Armazenamento.Produtos.Should().BeEmpty();
        IdsDeLog().Should().BeEmpty();
    }

    [Fact]
    public async Task Alteracao_muda_nome_descricao_e_preco_e_loga_os_dois_precos()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", preco: 30m, estoque: 9);

        var alterado = (await Servico().AlterarAsync(produto.Id, new AlterarProdutoRequestDTO { Nome = "Café especial", Preco = 35m }, Admin, CancellationToken.None)).Valor!;

        (alterado.Nome, alterado.Descricao, alterado.Preco, alterado.Sku, alterado.QuantidadeEstoque).Should().Be(("Café especial", null, 35m, "CAF-250", 9));
        var log = Logger.Eventos.Single(e => e.Id.Id == 12002);
        (log.Propriedades["PrecoAnterior"], log.Propriedades["PrecoNovo"]).Should().Be((30m, 35m));
    }

    [Fact]
    public async Task Alteracao_sem_mudanca_responde_sem_gravar_nem_logar()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", preco: 30m);

        var resultado = await Servico().AlterarAsync(produto.Id, new AlterarProdutoRequestDTO { Nome = "Café", Preco = 30m }, Admin, CancellationToken.None);

        resultado.Sucesso.Should().BeTrue();
        Armazenamento.Gravacoes.Should().Be(0);
        IdsDeLog().Should().BeEmpty();
    }

    [Fact]
    public async Task Conflito_e_reavaliado_e_depois_de_3_vezes_da_409()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café", preco: 30m);
        Armazenamento.ConflitosRestantes = 3;

        var resultado = await Servico().AlterarAsync(produto.Id, new AlterarProdutoRequestDTO { Nome = "Novo", Preco = 31m }, Admin, CancellationToken.None);

        resultado.Falha!.Codigo.Should().Be(CodigosErro.ProdutoAlteradoPorOutraOperacao);
        Armazenamento.Leituras.Should().Be(3);
        IdsDeLog().Should().Equal(12006, 12006, 12006);
    }

    [Fact]
    public async Task Produto_inexistente_da_404()
    {
        var resultado = await Servico().AlterarAsync(Guid.NewGuid(), new AlterarProdutoRequestDTO { Nome = "X", Preco = 1m }, Admin, CancellationToken.None);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
    }
}
