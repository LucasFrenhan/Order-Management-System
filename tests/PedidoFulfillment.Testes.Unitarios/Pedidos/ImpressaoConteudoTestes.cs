using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>
/// Impressão do conteúdo da criação (FR-008, <c>contracts/idempotencia.md</c>).
/// </summary>
public sealed class ImpressaoConteudoTestes
{
    private static readonly Guid ProdutoA = Guid.Parse("0192b6a0-0000-7000-8000-000000000001");
    private static readonly Guid ProdutoB = Guid.Parse("0192b6a0-0000-7000-8000-000000000002");

    private static CriarPedidoRequestDTO Pedido(params (Guid Produto, int Quantidade)[] itens) =>
        new() { Itens = [.. itens.Select(i => new ItemPedidoDTO { ProdutoId = i.Produto, Quantidade = i.Quantidade })] };

    [Fact]
    public void Resultado_e_sha256_do_texto_canonico_em_hexadecimal()
    {
        var esperado = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{ProdutoA:D}:2;{ProdutoB:D}:1")));

        var impressao = ImpressaoConteudo.Calcular(Pedido((ProdutoB, 1), (ProdutoA, 2)));

        impressao.Should().Be(esperado).And.HaveLength(64).And.MatchRegex("^[0-9A-F]{64}$");
    }

    [Fact]
    public void Ordem_dos_itens_nao_muda_a_impressao()
    {
        ImpressaoConteudo.Calcular(Pedido((ProdutoA, 2), (ProdutoB, 1)))
            .Should().Be(ImpressaoConteudo.Calcular(Pedido((ProdutoB, 1), (ProdutoA, 2))));
    }

    [Fact]
    public void Outra_quantidade_ou_outro_produto_muda_a_impressao()
    {
        var original = ImpressaoConteudo.Calcular(Pedido((ProdutoA, 2)));

        ImpressaoConteudo.Calcular(Pedido((ProdutoA, 3))).Should().NotBe(original);
        ImpressaoConteudo.Calcular(Pedido((ProdutoB, 2))).Should().NotBe(original);
    }
}
