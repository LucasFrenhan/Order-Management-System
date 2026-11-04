using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>REQ-21, User Story 3: navegação por cursor nos dois sentidos (SQL Server real).</summary>
public sealed class ListagemPedidosNavegacaoTestes(BancoDadosTeste banco)
{
    private static readonly DateTimeOffset Base = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Quarenta_e_cinco_pedidos_em_paginas_de_vinte_para_a_frente_e_para_tras()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        await PedidosAsync(stringConexao, dono, 45);

        var p1 = await PaginaAsync(dono, "tamanho=20");
        var p2 = await PaginaAsync(dono, $"tamanho=20&{Cursor(Proximo(p1))}");
        var p3 = await PaginaAsync(dono, $"tamanho=20&{Cursor(Proximo(p2))}");
        var volta2 = await PaginaAsync(dono, $"tamanho=20&{Cursor(Anterior(p3))}");
        var volta1 = await PaginaAsync(dono, $"tamanho=20&{Cursor(Anterior(volta2))}");

        (Numeros(p1).Count, Numeros(p2).Count, Numeros(p3).Count).Should().Be((20, 20, 5));
        Numeros(p1).Concat(Numeros(p2)).Concat(Numeros(p3)).Should().OnlyHaveUniqueItems().And.HaveCount(45);
        Anterior(p1).Should().BeNull();
        Proximo(p3).Should().BeNull();
        Numeros(volta2).Should().Equal(Numeros(p2));
        Proximo(volta2).Should().NotBeNull();
        Anterior(volta2).Should().NotBeNull();
        Numeros(volta1).Should().Equal(Numeros(p1));
        Anterior(volta1).Should().BeNull();
        Numeros(await PaginaAsync(dono)).Should().HaveCount(20, "sem parâmetros vem o tamanho padrão");
    }

    [Fact]
    public async Task Pedido_criado_no_meio_nao_desloca_a_navegacao_e_aparece_ao_recomecar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var existentes = await PedidosAsync(stringConexao, dono, 5);

        var p1 = await PaginaAsync(dono, "tamanho=2");
        var (_, novo) = await PedidoAsync(dono);
        var p2 = await PaginaAsync(dono, $"tamanho=2&{Cursor(Proximo(p1))}");
        var p3 = await PaginaAsync(dono, $"tamanho=2&{Cursor(Proximo(p2))}");

        Numeros(p1).Concat(Numeros(p2)).Concat(Numeros(p3)).Should().Equal(Enumerable.Reverse(existentes), "SC-002");
        Numeros(await PaginaAsync(dono, "tamanho=2"))[0].Should().Be(novo);
    }

    [Fact]
    public async Task Empate_de_criacao_na_divisa_nao_perde_nem_repete_e_tamanho_pode_mudar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var numeros = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            var (id, numero) = await PedidoAsync(dono);
            await AjustarAsync(stringConexao, id, Base);
            numeros.Add(numero);
        }

        var p1 = await PaginaAsync(dono, "tamanho=2");
        var p2 = await PaginaAsync(dono, $"tamanho=2&{Cursor(Proximo(p1))}");
        var resto = await PaginaAsync(dono, $"tamanho=10&{Cursor(Proximo(p2))}");

        Numeros(p1).Concat(Numeros(p2)).Concat(Numeros(resto)).Should().Equal(numeros.OrderDescending(StringComparer.Ordinal));
        resto.GetProperty("tamanho").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Pedido_que_sai_do_filtro_nao_quebra_o_cursor_e_pagina_vazia_tem_volta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var numeros = await PedidosAsync(stringConexao, dono, 5);
        var consulta = "status=Pendente&tamanho=2";

        var p1 = await PaginaAsync(dono, consulta);
        // O pedido que serviu de posição do cursor sai do filtro: a navegação continua.
        await Cancelar(stringConexao, numeros[3]);
        var p2 = await PaginaAsync(dono, $"{consulta}&{Cursor(Proximo(p1))}");
        Numeros(p2).Should().Equal(numeros[2], numeros[1]);

        // Os que restavam à frente saem do filtro: página vazia, sem próximo e com volta.
        await Cancelar(stringConexao, numeros[0]);
        var vazia = await PaginaAsync(dono, $"{consulta}&{Cursor(Proximo(p2))}");
        Numeros(vazia).Should().BeEmpty();
        Proximo(vazia).Should().BeNull();
        var volta = await PaginaAsync(dono, $"{consulta}&{Cursor(Anterior(vazia))}");
        Numeros(volta).Should().Equal(numeros[2], numeros[1]);
    }

    /// <summary>Cria <paramref name="quantidade"/> pedidos, um por minuto, e devolve os números do mais antigo ao mais novo.</summary>
    private static async Task<List<string>> PedidosAsync(string stringConexao, HttpClient cliente, int quantidade)
    {
        var numeros = new List<string>();
        for (var i = 0; i < quantidade; i++)
        {
            var (id, numero) = await PedidoAsync(cliente);
            await AjustarAsync(stringConexao, id, Base.AddMinutes(i));
            numeros.Add(numero);
        }

        return numeros;
    }

    private static Task Cancelar(string stringConexao, string numero) =>
        BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Pedidos SET Status = 'Cancelado' WHERE Numero = '{numero}'");
}
