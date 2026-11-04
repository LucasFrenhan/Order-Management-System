using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>REQ-21, User Story 2: a operação lista e filtra todos os pedidos (SQL Server real).</summary>
public sealed class ListagemPedidosFiltrosTestes(BancoDadosTeste banco)
{
    private static readonly DateTimeOffset Dia = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Filtros_de_status_periodos_e_cliente_combinados()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var api = Api(stringConexao);
        var (clienteA, _, _) = await ClienteAsync(api);
        var (clienteB, _, _) = await ClienteAsync(api);

        // A: pendente há 7 dias (a1), pronto desde ontem (a2), pronto desde hoje (a3), cancelado (a4).
        var a1 = await Pedido(stringConexao, clienteA, Dia.AddDays(-7), null, "Pendente");
        var a2 = await Pedido(stringConexao, clienteA, Dia.AddDays(-7).AddHours(1), Dia.AddDays(-1), "ProntoParaEnvio");
        var a3 = await Pedido(stringConexao, clienteA, Dia.AddDays(-2), Dia.AddHours(1), "ProntoParaEnvio");
        var a4 = await Pedido(stringConexao, clienteA, Dia.AddDays(-1), null, "Cancelado");
        // B: processando hoje (b1), pagamento rejeitado ontem (b2).
        var b1 = await Pedido(stringConexao, clienteB, Dia, null, "Processando");
        var b2 = await Pedido(stringConexao, clienteB, Dia.AddDays(-1).AddHours(2), null, "PagamentoRejeitado");
        var idB = await BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT ClienteId FROM Pedidos WHERE Numero = '{b1}'");

        foreach (var operacao in new[] { await AdminAsync(api), await OperadorAsync(api) })
        {
            Numeros(await PaginaAsync(operacao)).Should().Equal(b1, b2, a4, a3, a2, a1);
            Numeros(await PaginaAsync(operacao, "status=ProntoParaEnvio")).Should().Equal(a3, a2);
            Numeros(await PaginaAsync(operacao, "status=Pendente&status=Processando")).Should().Equal(b1, a1);
            Numeros(await PaginaAsync(operacao, "status=PagamentoRejeitado&status=Cancelado&status=cancelado")).Should().Equal(b2, a4);

            // Criação: limites inclusivos, só início, só fim e início igual ao fim.
            Numeros(await PaginaAsync(operacao, $"criadoDe={Data(Dia.AddDays(-2))}&criadoAte={Data(Dia.AddDays(-1))}")).Should().Equal(a4, a3);
            Numeros(await PaginaAsync(operacao, $"criadoDe={Data(Dia.AddDays(-1))}")).Should().Equal(b1, b2, a4);
            Numeros(await PaginaAsync(operacao, $"criadoAte={Data(Dia.AddDays(-7).AddHours(1))}")).Should().Equal(a2, a1);
            Numeros(await PaginaAsync(operacao, $"criadoDe={Data(Dia)}&criadoAte={Data(Dia.ToOffset(TimeSpan.FromHours(-3)))}")).Should().Equal(b1);

            // Parados: em ProntoParaEnvio sem alteração desde o fim de ontem (acceptance 6 da US2).
            var fimDeOntem = new DateTimeOffset(2026, 11, 2, 23, 59, 59, TimeSpan.FromHours(-3));
            Numeros(await PaginaAsync(operacao, $"status=ProntoParaEnvio&atualizadoAte={Data(fimDeOntem)}")).Should().Equal(a2);
            Numeros(await PaginaAsync(operacao, $"atualizadoDe={Data(Dia.AddHours(1))}")).Should().Equal(a3);

            // Cliente: um existente, um inexistente (vazio, não 404) e tudo junto.
            Numeros(await PaginaAsync(operacao, $"clienteId={idB}")).Should().Equal(b1, b2);
            Numeros(await PaginaAsync(operacao, $"clienteId={Guid.NewGuid()}")).Should().BeEmpty();
            Numeros(await PaginaAsync(operacao,
                    $"clienteId={idB}&status=PagamentoRejeitado&status=Processando&criadoAte={Data(Dia.AddHours(-1))}&atualizadoAte={Data(Dia)}"))
                .Should().Equal(b2);
        }
    }

    private static async Task<string> Pedido(string stringConexao, HttpClient cliente, DateTimeOffset criadoEm, DateTimeOffset? atualizadoEm, string status)
    {
        var (id, numero) = await PedidoAsync(cliente);
        await AjustarAsync(stringConexao, id, criadoEm, atualizadoEm, status);
        return numero;
    }
}
