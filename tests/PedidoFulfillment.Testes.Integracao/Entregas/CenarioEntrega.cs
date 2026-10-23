using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;

namespace PedidoFulfillment.Testes.Integracao.Entregas;

/// <summary>
/// Atalhos dos testes do consumidor de entrega (REQ-10): Worker completo com a taxa do gateway em
/// 100%, leitura da ordem no banco e publicação direta de <c>pagamento.aprovado</c>.
/// </summary>
internal static class CenarioEntrega
{
    public const string FilaEntrega = "entrega";

    /// <summary>
    /// Sobe o Worker completo (estoque, pagamento e entrega) e espera os três consumidores
    /// ligados, depois de limpar as filas deles: o broker é compartilhado, e
    /// <c>pagamento.aprovado</c> de outros testes pode ter ficado em <c>oms.entrega</c> (R-09).
    /// </summary>
    public static async Task<FabricaWorker> WorkerProntoAsync(MensageriaTeste mensageria, string stringConexao, CapturaLogs? logs = null)
    {
        await mensageria.LimparFilasConsumidorAsync(CenarioEstoque.FilaEstoque);
        await mensageria.LimparFilasConsumidorAsync(CenarioPagamento.FilaPagamento);
        await mensageria.LimparFilasConsumidorAsync(FilaEntrega);
        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = new Dictionary<string, string?> { ["GatewayPagamento:TaxaAprovacaoPercentual"] = "100" },
            Servicos = logs is null ? null : servicos => servicos.AddSingleton<ILoggerProvider>(logs)
        };
        worker.CreateClient().Dispose();
        (await mensageria.ConsumidorLigadoAsync(CenarioEstoque.FilaEstoque, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de estoque precisa estar pronto");
        (await mensageria.ConsumidorLigadoAsync(CenarioPagamento.FilaPagamento, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de pagamento precisa estar pronto");
        (await mensageria.ConsumidorLigadoAsync(FilaEntrega, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de entrega precisa estar pronto");
        return worker;
    }

    public static Task<int> OrdensAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM OrdensEntrega WHERE PedidoId = '{pedidoId}'");

    /// <summary>A ordem do pedido; nula quando não há.</summary>
    public static async Task<OrdemGravada?> OrdemAsync(string stringConexao, Guid pedidoId)
    {
        if (await OrdensAsync(stringConexao, pedidoId) == 0)
        {
            return null;
        }

        var sql = $"FROM OrdensEntrega WHERE PedidoId = '{pedidoId}'";
        return new OrdemGravada(
            await BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT Id {sql}"),
            await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) {sql} AND Transportadora IS NULL AND CodigoRastreio IS NULL AND DespachadoEm IS NULL") == 1);
    }

    /// <summary><c>entrega.criada</c> gravados no outbox para o pedido.</summary>
    public static Task<int> EntregasCriadasAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{TiposEvento.EntregaCriada}' AND Conteudo LIKE '%{pedidoId}%'");

    /// <summary>Entradas do histórico do pedido.</summary>
    public static Task<int> EntradasHistoricoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}'");

    /// <summary>Espera a ordem do pedido aparecer, até o prazo.</summary>
    public static Task<bool> EsperarOrdemAsync(string stringConexao, Guid pedidoId, TimeSpan prazo) =>
        MensageriaTeste.EsperarAteAsync(async () => await OrdensAsync(stringConexao, pedidoId) > 0, prazo);

    /// <summary><c>pagamento.aprovado</c> do pedido, com <c>idEvento</c> novo, publicado direto na troca.</summary>
    public static async Task<Guid> PublicarPagamentoAprovadoAsync(MensageriaTeste mensageria, Guid pedidoId)
    {
        var envelope = EnvelopeEvento.Criar(pedidoId, new PagamentoAprovado
        {
            PedidoId = pedidoId,
            Valor = 1m,
            IdentificadorTransacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12"
        }, DateTimeOffset.UtcNow);
        await CenarioEstoque.PublicarAsync(mensageria, SerializacaoEventos.Serializar(envelope), envelope.IdEvento.ToString(), TiposEvento.PagamentoAprovado);
        return envelope.IdEvento;
    }

    /// <summary>Republica, com o mesmo <c>idEvento</c>, o <c>pagamento.aprovado</c> gravado no outbox para o pedido.</summary>
    public static async Task RepublicarPagamentoAprovadoAsync(MensageriaTeste mensageria, string stringConexao, Guid pedidoId)
    {
        var corpo = await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT Conteudo FROM EventosSaida WHERE Tipo = '{TiposEvento.PagamentoAprovado}' AND Conteudo LIKE '%{pedidoId}%'");
        var idEvento = SerializacaoEventos.Desserializar<PagamentoAprovado>(corpo).IdEvento;
        await CenarioEstoque.PublicarAsync(mensageria, corpo, idEvento.ToString(), TiposEvento.PagamentoAprovado);
    }

    public sealed record OrdemGravada(Guid Id, bool SemDespacho);
}
