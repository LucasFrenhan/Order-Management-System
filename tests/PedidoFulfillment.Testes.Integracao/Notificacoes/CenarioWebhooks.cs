using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>Montagem comum dos testes de webhook (REQ-18, US2 e US4).</summary>
internal static class CenarioWebhooks
{
    public static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Worker com o envio ligado a cada 1 s e tempo limite curto; o resto como nas fábricas.</summary>
    public static FabricaWorker WorkerEnviando(
        MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null,
        IReadOnlyDictionary<string, string?>? configuracoes = null, IReadOnlyDictionary<string, string?>? broker = null)
    {
        var todas = new Dictionary<string, string?>
        {
            ["Notificacoes:Envio:Habilitado"] = "true",
            ["Notificacoes:Envio:IntervaloSegundos"] = "1",
            ["Notificacoes:Envio:TempoLimiteSegundos"] = "2"
        };
        foreach (var (chave, valor) in configuracoes ?? new Dictionary<string, string?>())
        {
            todas[chave] = valor;
        }

        return new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = broker ?? mensageria.ConfiguracoesBroker,
            Configuracoes = todas,
            Servicos = colecao =>
            {
                // Só a rotina das notificações: os consumidores levariam o pedido adiante e gerariam
                // avisos que o teste não espera, e o despachante deixaria mensagens no broker.
                foreach (var rotina in colecao.Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType != typeof(ProcessadorNotificacoes)).ToList())
                {
                    colecao.Remove(rotina);
                }

                if (logs is not null)
                {
                    colecao.AddSingleton<ILoggerProvider>(logs);
                }
            }
        };
    }

    /// <summary>Tentativas da última entrega do destino, zero enquanto ela não existe.</summary>
    public static Task<int> TentativasAsync(string stringConexao, Guid destinoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT ISNULL(MAX(Tentativas), 0) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}'");

    /// <summary>Cadastra o destino e devolve o identificador e os bytes do segredo.</summary>
    public static async Task<(Guid Id, byte[] Segredo)> DestinoAsync(HttpClient usuario, string url)
    {
        var resposta = await DestinosCadastroTestes.CadastrarAsync(usuario, url);
        resposta.StatusCode.Should().Be(HttpStatusCode.Created, await resposta.Content.ReadAsStringAsync(Token));
        var corpo = await CorpoAsync(resposta);
        var segredo = corpo.GetProperty("segredo").GetString()!;
        return (corpo.GetProperty("id").GetGuid(), Convert.FromBase64String(segredo["whsec_".Length..]));
    }

    /// <summary>Um pedido do Cliente, levado a <c>Processando</c> pela operação: um aviso de status.</summary>
    public static async Task<Guid> PedidoComAvisoAsync(HttpClient cliente, HttpClient operador)
    {
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(operador, id, "Processando")).EnsureSuccessStatusCode();
        return id;
    }

    public static Task<bool> EsperarRecebidasAsync(ReceptorWebhookTeste receptor, int quantidade, TimeSpan? prazo = null) =>
        MensageriaTeste.EsperarAteAsync(() => Task.FromResult(receptor.Recebidas.Count >= quantidade), prazo ?? Prazo);

    public static Task<string> SituacaoEntregaAsync(string stringConexao, Guid destinoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT TOP 1 Situacao FROM EntregasNotificacao WHERE DestinoId = '{destinoId}' ORDER BY Id DESC");

    public static Task<bool> EsperarSituacaoAsync(string stringConexao, Guid destinoId, string situacao, TimeSpan? prazo = null) =>
        MensageriaTeste.EsperarAteAsync(async () =>
            await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destinoId}' AND Situacao = N'{situacao}'") > 0, prazo ?? Prazo);

    /// <summary>Simula o tempo passando: a próxima tentativa vence agora.</summary>
    public static Task VencerAsync(string stringConexao, Guid destinoId) =>
        BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE EntregasNotificacao SET ProximaTentativaEm = DATEADD(SECOND, -1, SYSDATETIMEOFFSET()) WHERE DestinoId = '{destinoId}' AND Situacao = N'Pendente'");

    public static Guid IdNotificacao(RequisicaoWebhook requisicao)
    {
        using var corpo = JsonDocument.Parse(requisicao.Corpo);
        return corpo.RootElement.GetProperty("idNotificacao").GetGuid();
    }
}
