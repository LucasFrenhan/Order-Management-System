using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>REQ-21, User Story 4: o Cliente não alcança pedidos de outros (SC-001).</summary>
public sealed class ListagemPedidosEscopoTestes(BancoDadosTeste banco)
{
    private static readonly DateTimeOffset Base = new(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Filtro_por_cliente_da_403_ao_cliente_e_nenhuma_combinacao_vaza_pedidos_de_outro()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var meus = new HashSet<string>();
        for (var i = 0; i < 6; i++)
        {
            var (id, numero) = await PedidoAsync(dono);
            await AjustarAsync(stringConexao, id, Base.AddHours(i * 2), status: i % 2 == 0 ? "Pendente" : "Cancelado");
            meus.Add(numero);
            var (pedidoOutro, _) = await PedidoAsync(outro);
            await AjustarAsync(stringConexao, pedidoOutro, Base.AddHours((i * 2) + 1), status: i % 2 == 0 ? "Pendente" : "Cancelado");
        }

        var idDono = await BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT TOP 1 ClienteId FROM Pedidos WHERE Numero = '{meus.First()}'");
        var idOutro = await BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT TOP 1 ClienteId FROM Pedidos WHERE ClienteId <> '{idDono}'");

        foreach (var cliente in new[] { idOutro, idDono })
        {
            var resposta = await ListarAsync(dono, $"clienteId={cliente}");
            resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, Rota);
            corpo.GetProperty("detail").GetString().Should().Be("O filtro por cliente é exclusivo da operação.");
        }

        (await ListarAsync(dono, "clienteId=abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "malformado é 400 para todos");

        // Varredura: filtros, tamanhos e cursores, só com pedidos do dono.
        var consultas = new[]
        {
            "", "status=Pendente", "status=Cancelado&status=Pendente",
            $"criadoDe={Data(Base.AddHours(3))}", $"atualizadoAte={Data(Base.AddHours(8))}",
            $"status=Pendente&criadoDe={Data(Base)}&criadoAte={Data(Base.AddDays(1))}"
        };
        foreach (var consulta in consultas)
        {
            foreach (var tamanho in new[] { 1, 2, 5 })
            {
                var vistos = await PercorrerAsync(dono, $"{consulta}&tamanho={tamanho}".TrimStart('&'));
                vistos.Should().OnlyContain(n => meus.Contains(n), $"{consulta}, tamanho {tamanho}");
            }
        }

        // Cursor da operação ou de outro Cliente não serve ao dono.
        var daOperacao = Proximo(await PaginaAsync(await AdminAsync(api), "tamanho=1"));
        var doOutro = Proximo(await PaginaAsync(outro, "tamanho=1"));
        foreach (var cursor in new[] { daOperacao, doOutro })
        {
            (await ListarAsync(dono, $"tamanho=1&{Cursor(cursor)}")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }
    }

    /// <summary>Segue o cursor da próxima até o fim e devolve todos os números vistos.</summary>
    private static async Task<List<string>> PercorrerAsync(HttpClient cliente, string consulta)
    {
        var vistos = new List<string>();
        var pagina = await PaginaAsync(cliente, consulta);
        vistos.AddRange(Numeros(pagina));
        while (Proximo(pagina) is { } proximo)
        {
            pagina = await PaginaAsync(cliente, $"{consulta}&{Cursor(proximo)}");
            vistos.AddRange(Numeros(pagina));
        }

        vistos.Should().OnlyHaveUniqueItems();
        return vistos;
    }
}
