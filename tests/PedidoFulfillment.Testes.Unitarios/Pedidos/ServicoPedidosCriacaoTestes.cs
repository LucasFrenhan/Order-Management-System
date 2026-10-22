using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>User Story 1: criação, preços do catálogo e 422 de produto (FR-001 a FR-004).</summary>
public sealed class ServicoPedidosCriacaoTestes
{
    internal static readonly DateTimeOffset Agora = new(2026, 10, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly Guid ClienteId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoPedidos> logger = new();

    private ServicoPedidos Servico() => new(armazenamento, new GeradorNumeroPedidoFalso(), new RelogioFixo(Agora), logger);

    private Produto Produto(decimal preco, int estoque = 10, bool ativo = true)
    {
        var produto = new Produto($"SKU-{Guid.NewGuid():N}"[..20], $"Produto {preco}", null, preco, estoque, ativo, Agora.AddDays(-1));
        armazenamento.Produtos.Add(produto);
        return produto;
    }

    internal static CriarPedidoRequestDTO Requisicao(params (Guid Produto, int Quantidade)[] itens) =>
        new() { Itens = [.. itens.Select(i => new ItemPedidoDTO { ProdutoId = i.Produto, Quantidade = i.Quantidade })] };

    [Fact]
    public async Task Cria_o_pedido_pendente_com_precos_do_catalogo_numa_unica_gravacao()
    {
        var teclado = Produto(349.90m);
        var mouse = Produto(129.90m);
        var requisicao = Requisicao((teclado.Id, 2), (mouse.Id, 1));

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), requisicao, CancellationToken.None);

        resultado.Sucesso.Should().BeTrue();
        var pedido = resultado.Valor!;
        pedido.Status.Should().Be(StatusPedido.Pendente);
        pedido.ClienteId.Should().Be(ClienteId);
        pedido.Moeda.Should().Be("BRL");
        pedido.Numero.Should().Be("PED-20261022-000001");
        pedido.CriadoEm.Should().Be(Agora);
        pedido.ValorTotal.Should().Be(829.70m);
        pedido.Itens.Should().BeEquivalentTo([
            new ItemPedidoResponseDTO { ProdutoId = teclado.Id, NomeProduto = teclado.Nome, Quantidade = 2, PrecoUnitario = 349.90m, Subtotal = 699.80m },
            new ItemPedidoResponseDTO { ProdutoId = mouse.Id, NomeProduto = mouse.Nome, Quantidade = 1, PrecoUnitario = 129.90m, Subtotal = 129.90m }]);
        armazenamento.Gravacoes.Should().Be(1);
        armazenamento.PedidosGravados.Should().ContainSingle().Which.Id.Should().Be(pedido.Id);
        teclado.QuantidadeEstoque.Should().Be(10);
    }

    [Fact]
    public async Task Registra_a_chave_com_a_impressao_e_a_resposta_enviada()
    {
        var produto = Produto(10m);
        var chave = Guid.NewGuid();
        var requisicao = Requisicao((produto.Id, 1));

        var resultado = await Servico().CriarAsync(ClienteId, chave, requisicao, CancellationToken.None);

        var registro = armazenamento.Registros.Should().ContainSingle().Subject;
        registro.ClienteId.Should().Be(ClienteId);
        registro.Chave.Should().Be(chave);
        registro.Impressao.Should().Be(ImpressaoConteudo.Calcular(requisicao));
        registro.PedidoId.Should().Be(resultado.Valor!.Id);
        registro.CriadoEm.Should().Be(Agora);
        var opcoes = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
        JsonSerializer.Deserialize<PedidoResponseDTO>(registro.Resposta, opcoes).Should().BeEquivalentTo(resultado.Valor);
    }

    [Fact]
    public async Task Produto_inexistente_responde_422_sem_gravar()
    {
        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((Guid.CreateVersion7(), 1)), CancellationToken.None);

        DeveSer422(resultado, CodigosErro.ProdutoInexistente);
    }

    [Fact]
    public async Task Produto_inativo_responde_422_indisponivel()
    {
        var produto = Produto(10m, ativo: false);

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((produto.Id, 1)), CancellationToken.None);

        DeveSer422(resultado, CodigosErro.ProdutoIndisponivel);
    }

    [Fact]
    public async Task Estoque_menor_que_a_quantidade_responde_422_indisponivel()
    {
        var produto = Produto(10m, estoque: 1);

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((produto.Id, 2)), CancellationToken.None);

        DeveSer422(resultado, CodigosErro.ProdutoIndisponivel);
    }

    [Fact]
    public async Task Estoque_igual_a_quantidade_e_aceito()
    {
        var produto = Produto(10m, estoque: 2);

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((produto.Id, 2)), CancellationToken.None);

        resultado.Sucesso.Should().BeTrue();
    }

    [Fact]
    public async Task Varios_problemas_respondem_um_422_com_todos_os_produtos()
    {
        var inexistente = Guid.CreateVersion7();
        var inativo = Produto(10m, ativo: false);

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((inativo.Id, 1), (inexistente, 1)), CancellationToken.None);

        DeveSer422(resultado, CodigosErro.ProdutoInexistente);
        resultado.Falha!.Detalhe.Should().Contain(inexistente.ToString()).And.Contain(inativo.Id.ToString());
    }

    [Fact]
    public async Task Registra_o_evento_de_criacao_sem_o_corpo()
    {
        var produto = Produto(10m);

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((produto.Id, 3)), CancellationToken.None);

        var evento = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4001).Subject;
        evento.Propriedades["PedidoId"].Should().Be(resultado.Valor!.Id);
        evento.Propriedades["QuantidadeItens"].Should().Be(1);
        evento.Mensagem.Should().NotContain(produto.Id.ToString());
    }

    private void DeveSer422(Resultado<PedidoResponseDTO> resultado, string codigo)
    {
        resultado.Falha.Should().NotBeNull();
        resultado.Falha!.Tipo.Should().Be(TipoFalha.RegraNegocio);
        resultado.Falha.Codigo.Should().Be(codigo);
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.Registros.Should().BeEmpty();
    }
}
