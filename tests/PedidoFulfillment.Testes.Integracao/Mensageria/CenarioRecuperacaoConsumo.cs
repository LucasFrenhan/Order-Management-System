using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Administracao.CenarioReenvio;
using static PedidoFulfillment.Testes.Integracao.Estoque.CenarioEstoque;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Auxiliares dos testes de recuperação do consumo (REQ-17): os testes ficam em duas classes,
/// cada uma numa coleção com o próprio broker, para rodarem em paralelo (estudo
/// docs/otimizacao-ci.md, proposta 4).
/// </summary>
public abstract class CenarioRecuperacaoConsumo(MensageriaTeste mensageria)
{
    protected MensageriaTeste Mensageria { get; } = mensageria;

    /// <summary>FR-003: até 1 min depois de o broker voltar a aceitar conexões.</summary>
    protected static readonly TimeSpan PrazoVolta = TimeSpan.FromSeconds(60);

    /// <summary>Pedido do começo ao fim depois da volta: até 1 min para voltar, mais o fluxo.</summary>
    protected static readonly TimeSpan PrazoFluxo = TimeSpan.FromSeconds(90);

    protected static readonly string[] Consumidores = ["estoque", "pagamento", "entrega"];

    protected static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Espera cada fila principal ter exatamente um consumidor, até o prazo.</summary>
    protected async Task<bool> UmConsumidorEmCadaFilaAsync(TimeSpan prazo) =>
        await MensageriaTeste.EsperarAteAsync(async () =>
        {
            foreach (var consumidor in Consumidores)
            {
                if (await Mensageria.ConsumidoresNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor)) != 1)
                {
                    return false;
                }
            }

            return true;
        }, prazo);

    protected static Task<bool> ProntoParaEnvioAsync(string stringConexao, Guid pedidoId) =>
        MensageriaTeste.EsperarAteAsync(async () => await StatusAsync(stringConexao, pedidoId) == "ProntoParaEnvio", PrazoFluxo);

    protected static async Task<(HttpStatusCode Codigo, string Corpo, string? Consumidores)> ProntidaoAsync(HttpClient http)
    {
        var resposta = await http.GetAsync("/health/pronto", Cancelamento);
        var corpo = await resposta.Content.ReadAsStringAsync(Cancelamento);
        using var json = JsonDocument.Parse(corpo);
        var consumidores = json.RootElement.GetProperty("dependencias").TryGetProperty("consumidores", out var valor) ? valor.GetString() : null;
        return (resposta.StatusCode, corpo, consumidores);
    }

    /// <summary>Como <see cref="CenarioEntrega.WorkerProntoAsync"/>, com serviços extras.</summary>
    protected async Task<FabricaWorker> WorkerCompletoAsync(string stringConexao, CapturaLogs logs, Action<IServiceCollection> servicos)
    {
        foreach (var consumidor in Consumidores)
        {
            await Mensageria.LimparFilasConsumidorAsync(consumidor);
        }

        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = Mensageria.ConfiguracoesBroker,
            Servicos = colecao =>
            {
                colecao.AddSingleton<ILoggerProvider>(logs);
                servicos(colecao);
            }
        };
        return await worker.ProntoAsync(async () =>
            (await UmConsumidorEmCadaFilaAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("os três consumidores precisam estar prontos"));
    }

    /// <summary>Segura a primeira gravação até o teste liberar.</summary>
    protected sealed class PortaoSalvar
    {
        private int usado;

        public TaskCompletionSource Segurando { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Liberar { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Primeira() => Interlocked.Exchange(ref usado, 1) == 0;
    }

    /// <summary>
    /// O armazenamento real; a primeira gravação (a da reserva) acontece e segura antes de voltar
    /// ao <c>ConsumidorEventos</c>, que só confirma a transação depois (analyze U1).
    /// </summary>
    protected sealed class ArmazenamentoQueSegura(IArmazenamentoPedidos real, PortaoSalvar portao) : IArmazenamentoPedidos
    {
        public Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, bool paraAlterar, CancellationToken cancellationToken) =>
            real.ObterProdutosAsync(ids, paraAlterar, cancellationToken);

        public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken) =>
            real.ObterPedidoAsync(id, paraAlterar, cancellationToken);

        public Task<bool> RastreioEmUsoAsync(string transportadora, string codigoRastreio, Guid pedidoId, CancellationToken cancellationToken) =>
            real.RastreioEmUsoAsync(transportadora, codigoRastreio, pedidoId, cancellationToken);

        public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
            real.ObterRegistroIdempotenciaAsync(clienteId, chave, cancellationToken);

        public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento) => real.Adicionar(pedido, registro, evento);

        public void Adicionar(EventoSaida evento) => real.Adicionar(evento);

        public async Task SalvarAsync(CancellationToken cancellationToken)
        {
            await real.SalvarAsync(cancellationToken);
            if (portao.Primeira())
            {
                portao.Segurando.TrySetResult();
                await portao.Liberar.Task;
            }
        }

        public void DescartarAlteracoes() => real.DescartarAlteracoes();
    }
}
