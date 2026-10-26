using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Observabilidade;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// Contexto de rastreamento gravado na criação (REQ-16, data-model; T011). Usa uma fonte de
/// activities só do teste, com ouvinte próprio, na coleção sem paralelismo (analyze U1).
/// </summary>
[Collection(nameof(ColecaoObservabilidade))]
public sealed class ContextoRastreioPersistenciaTestes(MensageriaTeste mensageria)
{
    private const string NomeFonte = "PedidoFulfillment.Testes.ContextoRastreio";

    private static readonly ActivitySource Fonte = new(NomeFonte);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Pedido_evento_e_pedido_de_reenvio_novos_gravam_o_contexto_da_activity()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        using var ouvinte = Ouvir();

        Guid pedidoId;
        long eventoId;
        Guid reenvioId;
        string esperado;
        using (var activity = Fonte.StartActivity("gravar"))
        {
            esperado = activity!.Id!;
            (pedidoId, eventoId, reenvioId) = await GravarAsync(stringConexao);
        }

        esperado.Should().HaveLength(55);
        (await ContextoAsync(stringConexao, "Pedidos", $"Id = '{pedidoId}'")).Should().Be(esperado);
        (await ContextoAsync(stringConexao, "EventosSaida", $"Id = {eventoId}")).Should().Be(esperado);
        (await ContextoAsync(stringConexao, "PedidosReenvioDlq", $"Id = '{reenvioId}'")).Should().Be(esperado);
    }

    [Fact]
    public async Task Sem_activity_a_coluna_fica_nula()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();

        var (pedidoId, eventoId, reenvioId) = await GravarAsync(stringConexao);

        (await ContextoAsync(stringConexao, "Pedidos", $"Id = '{pedidoId}'")).Should().BeNull();
        (await ContextoAsync(stringConexao, "EventosSaida", $"Id = {eventoId}")).Should().BeNull();
        (await ContextoAsync(stringConexao, "PedidosReenvioDlq", $"Id = '{reenvioId}'")).Should().BeNull();
    }

    [Fact]
    public async Task Alterar_a_linha_com_outra_activity_nao_troca_o_contexto()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        using var ouvinte = Ouvir();
        Guid reenvioId;
        string original;
        using (var activity = Fonte.StartActivity("criar"))
        {
            original = activity!.Id!;
            (_, _, reenvioId) = await GravarAsync(stringConexao);
        }

        using (Fonte.StartActivity("alterar"))
        {
            await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
            var pedido = await contexto.PedidosReenvioDlq.SingleAsync(p => p.Id == reenvioId, Cancelamento);
            pedido.Iniciar(DateTimeOffset.UtcNow);
            await contexto.SaveChangesAsync(Cancelamento);
        }

        (await ContextoAsync(stringConexao, "PedidosReenvioDlq", $"Id = '{reenvioId}'")).Should().Be(original);
    }

    private static ActivityListener Ouvir()
    {
        var ouvinte = new ActivityListener
        {
            // Pelo nome: comparar com o campo estático aqui dispararia a criação da fonte no meio do registro.
            ShouldListenTo = fonte => fonte.Name == NomeFonte,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(ouvinte);
        return ouvinte;
    }

    private static async Task<(Guid Pedido, long Evento, Guid Reenvio)> GravarAsync(string stringConexao)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var produto = DadosPersistencia.NovoProduto();
        contexto.Produtos.Add(produto);
        var pedido = DadosPersistencia.NovoPedido(await DadosPersistencia.NovoUsuarioAsync(contexto), produto);
        contexto.Pedidos.Add(pedido);
        var evento = EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PedidoCancelado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            Motivo = MotivoCancelamento.Cliente,
            EtapaEsgotada = null,
            CanceladoEm = DadosPersistencia.Instante,
            EstoqueDevolvido = false,
            EstornoPendente = false,
            OrdemEntregaCancelada = false
        }, DadosPersistencia.Instante), DadosPersistencia.Instante);
        contexto.EventosSaida.Add(evento);
        var reenvio = PedidoReenvioDlq.Registrar(Guid.CreateVersion7(), "estoque", new FiltroReenvio(null, null, null, null), DadosPersistencia.Instante);
        contexto.PedidosReenvioDlq.Add(reenvio);
        await contexto.SaveChangesAsync(Cancelamento);
        return (pedido.Id, evento.Id, reenvio.Id);
    }

    private static async Task<string?> ContextoAsync(string stringConexao, string tabela, string condicao) =>
        await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT COALESCE({ContextoBancoDados.ContextoRastreio}, '') FROM {tabela} WHERE {condicao}") is { Length: > 0 } valor ? valor : null;
}
