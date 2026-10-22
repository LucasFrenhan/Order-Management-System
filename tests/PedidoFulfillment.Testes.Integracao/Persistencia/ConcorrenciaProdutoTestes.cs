using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 2: concorrência otimista do produto (FR-003, SC-002). Nenhuma alteração é
/// sobrescrita em silêncio, e o estoque nunca fica negativo.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConcorrenciaProdutoTestes(BancoDadosTeste banco)
{
    private const int Repeticoes = 50;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static DateTimeOffset Depois => DadosPersistencia.Instante.AddMinutes(1);

    [Fact]
    public async Task Segunda_gravacao_sobre_leitura_antiga_recebe_conflito_e_nao_sobrescreve()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var produtoId = await NovoProdutoAsync(stringConexao, estoque: 10);
        var versaoInicial = await VersaoAsync(stringConexao, produtoId);
        await using var primeiro = BancoDadosTeste.NovoContexto(stringConexao);
        await using var segundo = BancoDadosTeste.NovoContexto(stringConexao);
        var lidoPeloPrimeiro = await primeiro.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);
        var lidoPeloSegundo = await segundo.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);

        lidoPeloPrimeiro.AjustarEstoque(-3, Depois);
        await primeiro.SaveChangesAsync(Cancelamento);
        lidoPeloSegundo.AjustarEstoque(-5, Depois);
        var gravar = () => segundo.SaveChangesAsync(Cancelamento);

        var conflito = (await gravar.Should().ThrowAsync<ConflitoConcorrenciaException>()).Which;
        conflito.Entidade.Should().Be("Produto");
        conflito.Identificador.Should().Be(produtoId.ToString());
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(7);
        (await VersaoAsync(stringConexao, produtoId)).Should().NotEqual(versaoInicial);

        // Cenário 3: relida, a mesma alteração é aceita.
        segundo.ChangeTracker.Clear();
        var relido = await segundo.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);
        relido.AjustarEstoque(-5, Depois);
        await segundo.SaveChangesAsync(Cancelamento);
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(2);
    }

    [Fact]
    public async Task Mudanca_que_nao_mexe_no_estoque_tambem_gera_conflito()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var produtoId = await NovoProdutoAsync(stringConexao, estoque: 10);
        await using var primeiro = BancoDadosTeste.NovoContexto(stringConexao);
        await using var segundo = BancoDadosTeste.NovoContexto(stringConexao);
        var lidoPeloPrimeiro = await primeiro.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);
        var lidoPeloSegundo = await segundo.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);

        lidoPeloPrimeiro.AlterarDados("Nome novo", null, lidoPeloPrimeiro.Preco, Depois);
        await primeiro.SaveChangesAsync(Cancelamento);
        lidoPeloSegundo.AjustarEstoque(-1, Depois);
        var gravar = () => segundo.SaveChangesAsync(Cancelamento);

        await gravar.Should().ThrowAsync<ConflitoConcorrenciaException>();
        (await EstoqueAsync(stringConexao, produtoId)).Should().Be(10);
    }

    [Fact]
    public async Task Disputa_pelo_ultimo_item_tem_sempre_um_vencedor_e_um_conflito()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();

        for (var repeticao = 0; repeticao < Repeticoes; repeticao++)
        {
            var produtoId = await NovoProdutoAsync(stringConexao, estoque: 1);
            // As duas leem antes de qualquer uma gravar; sem isso a segunda poderia ler o estoque
            // zero e falhar no domínio, sem exercitar a concorrência (achado U2 do analyze).
            using var leituras = new Barrier(2);

            var resultados = await Task.WhenAll(ReservarAsync(), ReservarAsync());

            resultados.Count(r => r is null).Should().Be(1, $"repetição {repeticao}");
            resultados.Count(r => r is ConflitoConcorrenciaException).Should().Be(1, $"repetição {repeticao}");
            (await EstoqueAsync(stringConexao, produtoId)).Should().Be(0);

            Task<Exception?> ReservarAsync() => Task.Run(async () =>
            {
                await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
                var produto = await contexto.Produtos.SingleAsync(p => p.Id == produtoId, Cancelamento);
                leituras.SignalAndWait(Cancelamento);
                try
                {
                    produto.AjustarEstoque(-1, Depois);
                    await contexto.SaveChangesAsync(Cancelamento);
                    return null;
                }
                catch (Exception excecao)
                {
                    return excecao;
                }
            }, Cancelamento);
        }
    }

    private static async Task<Guid> NovoProdutoAsync(string stringConexao, int estoque)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto(estoque: estoque);
        contexto.Produtos.Add(produto);
        await contexto.SaveChangesAsync(Cancelamento);
        return produto.Id;
    }

    private static Task<int> EstoqueAsync(string stringConexao, Guid produtoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT QuantidadeEstoque FROM Produtos WHERE Id = '{produtoId}'");

    private static Task<byte[]> VersaoAsync(string stringConexao, Guid produtoId) =>
        BancoDadosTeste.ConsultarAsync<byte[]>(stringConexao, $"SELECT Versao FROM Produtos WHERE Id = '{produtoId}'");
}
