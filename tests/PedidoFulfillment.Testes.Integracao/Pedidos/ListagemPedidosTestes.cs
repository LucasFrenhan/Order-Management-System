using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>REQ-21, User Story 1: o Cliente lista os próprios pedidos (SQL Server real).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ListagemPedidosTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Cliente_ve_so_os_proprios_pedidos_do_mais_recente_ao_mais_antigo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var (semPedidos, _, _) = await ClienteAsync(api);
        var meus = new List<string>();
        for (var i = 0; i < 3; i++)
        {
            var (id, numero) = await PedidoAsync(dono);
            await AjustarAsync(stringConexao, id, new DateTimeOffset(2026, 11, 1 + i, 10, 0, 0, TimeSpan.Zero));
            meus.Add(numero);
        }

        await PedidoAsync(outro);
        await PedidoAsync(outro);

        var pagina = await PaginaAsync(dono);
        var vazia = await PaginaAsync(semPedidos);

        Numeros(pagina).Should().Equal(meus[2], meus[1], meus[0]);
        Proximo(pagina).Should().BeNull();
        Anterior(pagina).Should().BeNull();
        pagina.GetProperty("tamanho").GetInt32().Should().Be(20);
        Numeros(vazia).Should().BeEmpty();
        Proximo(vazia).Should().BeNull();
        Anterior(vazia).Should().BeNull();
    }

    [Fact]
    public async Task Resumo_tem_os_campos_e_os_valores_da_consulta_por_id_e_a_listagem_nao_grava_nada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var criado = await CriadoAsync(dono, (Cat001, 2), (Cat002, 1));
        var eventos = await Contar(stringConexao, "EventosSaida");
        var avisos = await Contar(stringConexao, "AvisosPedido");

        var item = (await PaginaAsync(dono)).GetProperty("itens")[0];
        var consultado = await CorpoAsync(await dono.GetAsync($"{Rota}/{criado.GetProperty("id").GetGuid()}", Cancelamento));

        item.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            ["id", "numero", "clienteId", "status", "valorTotal", "moeda", "quantidadeItens", "criadoEm", "atualizadoEm"]);
        foreach (var campo in new[] { "id", "numero", "clienteId", "status", "moeda", "criadoEm", "atualizadoEm" })
        {
            item.GetProperty(campo).GetString().Should().Be(consultado.GetProperty(campo).GetString(), campo);
        }

        item.GetProperty("valorTotal").GetDecimal().Should().Be(consultado.GetProperty("valorTotal").GetDecimal());
        item.GetProperty("quantidadeItens").GetInt32().Should().Be(2, "duas linhas de item, três unidades");
        (await Contar(stringConexao, "EventosSaida")).Should().Be(eventos, "a listagem é só leitura (FR-023)");
        (await Contar(stringConexao, "AvisosPedido")).Should().Be(avisos);
    }

    private static Task<int> Contar(string stringConexao, string tabela) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM {tabela}");
}
