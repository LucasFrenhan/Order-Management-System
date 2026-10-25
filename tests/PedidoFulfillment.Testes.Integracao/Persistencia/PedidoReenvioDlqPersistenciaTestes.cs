using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>Pedidos de reenvio da DLQ gravados e lidos (REQ-14, R-07 e data-model).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class PedidoReenvioDlqPersistenciaTestes(BancoDadosTeste banco)
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_e_gravado_e_lido_em_cada_situacao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var autor = Guid.CreateVersion7();
        var pendente = PedidoReenvioDlq.Registrar(autor, "estoque", new FiltroReenvio("pedido.criado", T0.AddDays(-1), T0, "Timeout"), T0);
        var concluido = PedidoReenvioDlq.Registrar(autor, "pagamento", new FiltroReenvio(null, null, null, null), T0);
        concluido.Iniciar(T0.AddMinutes(1));
        concluido.Concluir(new ContagensReenvio(3, 1, 2), T0.AddMinutes(2));
        var falhou = PedidoReenvioDlq.Registrar(autor, "entrega", new FiltroReenvio(null, null, null, null), T0);
        falhou.Iniciar(T0.AddMinutes(1));
        falhou.Falhar(new ContagensReenvio(1, 0, 0), "Canal fechado pelo broker.", T0.AddMinutes(2));

        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.PedidosReenvioDlq.AddRange(pendente, concluido, falhou);
            await contexto.SaveChangesAsync(Cancelamento);
        }

        await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
        var relidos = await leitura.PedidosReenvioDlq.AsNoTracking().ToDictionaryAsync(p => p.Id, Cancelamento);
        relidos[pendente.Id].Should().BeEquivalentTo(pendente);
        relidos[concluido.Id].Should().BeEquivalentTo(concluido);
        relidos[falhou.Id].Should().BeEquivalentTo(falhou);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM PedidosReenvioDlq WHERE Id = '{falhou.Id}'"))
            .Should().Be("Falhou");
    }

    [Theory]
    [InlineData("UPDATE PedidosReenvioDlq SET Situacao = N'Cancelado'", "CK_PedidosReenvioDlq_SituacaoValida")]
    [InlineData("UPDATE PedidosReenvioDlq SET Reenviadas = -1", "CK_PedidosReenvioDlq_ContagensNaoNegativas")]
    public async Task Valores_invalidos_gravados_por_fora_sao_recusados(string sql, string restricao)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.PedidosReenvioDlq.Add(PedidoReenvioDlq.Registrar(Guid.CreateVersion7(), "estoque", new FiltroReenvio(null, null, null, null), T0));
            await contexto.SaveChangesAsync(Cancelamento);
        }

        var executar = () => BancoDadosTeste.ExecutarAsync(stringConexao, sql);

        var excecao = (await executar.Should().ThrowAsync<SqlException>()).Which;
        excecao.Number.Should().Be(547);
        excecao.Message.Should().Contain(restricao);
    }
}
