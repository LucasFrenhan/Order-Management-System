using System.Globalization;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Pagamentos;

/// <summary>
/// Atalhos dos testes do consumidor de pagamento (REQ-09): Worker completo com a taxa do gateway
/// fixada, leitura do pagamento no banco e publicação direta de <c>estoque.reservado</c>.
/// </summary>
internal static class CenarioPagamento
{
    public const string FilaPagamento = "pagamento";
    public const string MotivoRecusa = "Pagamento recusado pelo emissor.";

    /// <summary>
    /// Sobe o Worker do estoque e do pagamento, sem a entrega, com a taxa de aprovação dada e espera os dois
    /// consumidores ligados, depois de limpar as filas deles: o broker é compartilhado, e um
    /// evento publicado antes de a fila existir não tem rota (achado M3).
    /// </summary>
    public static async Task<FabricaWorker> WorkerProntoAsync(MensageriaTeste mensageria, string stringConexao, double taxaAprovacao, CapturaLogs? logs = null)
    {
        await mensageria.LimparFilasConsumidorAsync(CenarioEstoque.FilaEstoque);
        await mensageria.LimparFilasConsumidorAsync(FilaPagamento);
        var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = new Dictionary<string, string?>
            {
                ["GatewayPagamento:TaxaAprovacaoPercentual"] = taxaAprovacao.ToString(CultureInfo.InvariantCulture)
            },
            Servicos = servicos =>
            {
                // Sem a entrega (REQ-10), o pedido aprovado continua em Processando (research R-09 do REQ-10).
                CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(servicos);
                if (logs is not null)
                {
                    servicos.AddSingleton<ILoggerProvider>(logs);
                }
            }
        };
        worker.CreateClient().Dispose();
        (await mensageria.ConsumidorLigadoAsync(CenarioEstoque.FilaEstoque, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de estoque precisa estar pronto");
        (await mensageria.ConsumidorLigadoAsync(FilaPagamento, TimeSpan.FromSeconds(10))).Should().BeTrue("o consumidor de pagamento precisa estar pronto");
        return worker;
    }

    public static Task<int> PagamentosAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    public static Task<string> SituacaoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT Situacao FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    public static Task<decimal> ValorPagoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<decimal>(stringConexao, $"SELECT Valor FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    public static Task<string> TransacaoAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao, $"SELECT IdentificadorTransacao FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    public static Task<decimal> ValorTotalAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<decimal>(stringConexao, $"SELECT ValorTotal FROM Pedidos WHERE Id = '{pedidoId}'");

    /// <summary>Motivo da última mudança de status, com o autor (nulo nas mudanças do sistema).</summary>
    public static Task<string> UltimoMotivoSemAutorAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT TOP 1 Motivo FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}' AND AutorId IS NULL ORDER BY OcorridoEm DESC, Id DESC");

    /// <summary>Espera o pedido chegar ao status, até o prazo.</summary>
    public static Task<bool> EsperarStatusAsync(string stringConexao, Guid pedidoId, string status, TimeSpan prazo) =>
        MensageriaTeste.EsperarAteAsync(async () => await CenarioEstoque.StatusAsync(stringConexao, pedidoId) == status, prazo);

    /// <summary><c>estoque.reservado</c> do pedido, com <c>idEvento</c> novo, publicado direto na troca.</summary>
    public static async Task<Guid> PublicarEstoqueReservadoAsync(MensageriaTeste mensageria, Guid pedidoId, Guid? idEvento = null)
    {
        var envelope = EnvelopeEvento.Criar(pedidoId, new EstoqueReservado
        {
            PedidoId = pedidoId,
            Itens = [new ItemEstoqueReservado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1 }]
        }, DateTimeOffset.UtcNow);
        if (idEvento is { } id)
        {
            envelope = envelope with { IdEvento = id };
        }

        await CenarioEstoque.PublicarAsync(mensageria, SerializacaoEventos.Serializar(envelope), envelope.IdEvento.ToString(), TiposEvento.EstoqueReservado);
        return envelope.IdEvento;
    }

    /// <summary>Republica, com o mesmo <c>idEvento</c>, o <c>estoque.reservado</c> gravado no outbox para o pedido.</summary>
    public static async Task RepublicarEstoqueReservadoAsync(MensageriaTeste mensageria, string stringConexao, Guid pedidoId)
    {
        var corpo = await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            $"SELECT Conteudo FROM EventosSaida WHERE Tipo = '{TiposEvento.EstoqueReservado}' AND Conteudo LIKE '%{pedidoId}%'");
        var idEvento = SerializacaoEventos.Desserializar<EstoqueReservado>(corpo).IdEvento;
        await CenarioEstoque.PublicarAsync(mensageria, corpo, idEvento.ToString(), TiposEvento.EstoqueReservado);
    }
}
