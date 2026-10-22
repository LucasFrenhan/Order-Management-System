using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// Concorrência otimista do pedido (FR-021, cenário 9 da US1) e preservação do comportamento do
/// Identity, que não passa pela tradução do conflito (research R-04, FR-019).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConcorrenciaPedidoTestes(BancoDadosTeste banco)
{
    private const int Repeticoes = 50;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Segunda_mudanca_de_status_sobre_leitura_antiga_recebe_conflito()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Guid clienteId;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        }

        var conflitos = 0;
        for (var repeticao = 0; repeticao < Repeticoes; repeticao++)
        {
            var pedidoId = await NovoPedidoAsync(stringConexao, clienteId);
            await using var primeiro = BancoDadosTeste.NovoContexto(stringConexao);
            await using var segundo = BancoDadosTeste.NovoContexto(stringConexao);
            var lidoPeloPrimeiro = await primeiro.Pedidos.SingleAsync(p => p.Id == pedidoId, Cancelamento);
            var lidoPeloSegundo = await segundo.Pedidos.SingleAsync(p => p.Id == pedidoId, Cancelamento);

            lidoPeloPrimeiro.MudarStatus(StatusPedido.Processando, null, null, DadosPersistencia.Instante);
            await primeiro.SaveChangesAsync(Cancelamento);
            lidoPeloSegundo.MudarStatus(StatusPedido.Cancelado, null, "Desistência", DadosPersistencia.Instante);
            var gravar = () => segundo.SaveChangesAsync(Cancelamento);

            var conflito = (await gravar.Should().ThrowAsync<ConflitoConcorrenciaException>()).Which;
            conflito.Entidade.Should().Be("Pedido");
            conflito.Identificador.Should().Be(pedidoId.ToString());
            conflitos++;

            await using var leitura = BancoDadosTeste.NovoContexto(stringConexao);
            var relido = await leitura.Pedidos.Include(p => p.Historico).SingleAsync(p => p.Id == pedidoId, Cancelamento);
            relido.Status.Should().Be(StatusPedido.Processando);
            relido.Historico.Should().HaveCount(2);
        }

        conflitos.Should().Be(Repeticoes);
    }

    [Fact]
    public async Task Conflito_em_usuario_continua_sendo_tratado_pelo_Identity()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        Guid usuarioId;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            usuarioId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        }

        await using var api = new FabricaApi { StringConexao = stringConexao };
        await using var escopoPrimeiro = api.Services.CreateAsyncScope();
        await using var escopoSegundo = api.Services.CreateAsyncScope();
        var usuariosPrimeiro = escopoPrimeiro.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var usuariosSegundo = escopoSegundo.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var lidoPeloPrimeiro = (await usuariosPrimeiro.FindByIdAsync(usuarioId.ToString()))!;
        var lidoPeloSegundo = (await usuariosSegundo.FindByIdAsync(usuarioId.ToString()))!;

        lidoPeloPrimeiro.Nome = "Primeiro";
        (await usuariosPrimeiro.UpdateAsync(lidoPeloPrimeiro)).Succeeded.Should().BeTrue();
        lidoPeloSegundo.Nome = "Segundo";
        var resultado = await usuariosSegundo.UpdateAsync(lidoPeloSegundo);

        resultado.Succeeded.Should().BeFalse();
        resultado.Errors.Select(e => e.Code).Should().Contain("ConcurrencyFailure");
    }

    private static async Task<Guid> NovoPedidoAsync(string stringConexao, Guid clienteId)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Cancelamento);
        return pedido.Id;
    }
}
