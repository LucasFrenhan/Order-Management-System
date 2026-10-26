using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Application.Reconciliacao;

/// <summary>Resultado da reconciliação de um pedido (REQ-12, data-model).</summary>
public enum ResultadoReconciliacao
{
    Recuperado = 1,
    NaoMaisParado,
    Falhou,
    Esgotado
}

/// <summary>Quando um pedido das etapas de estoque e pagamento se esgota (REQ-12, FR-012).</summary>
public sealed record ParametrosEsgotamento(TimeSpan LimiteEsgotamento, int CiclosFalha);

/// <summary>
/// Decide o que fazer com um pedido parado (REQ-12, R-04, R-06 e R-07): aplica a regra do
/// consumidor da etapa; numa falha, conta o ciclo e, cumpridas as condições, esgota o pedido.
/// Um escopo por pedido: o ciclo, no Infrastructure, cria o escopo.
/// </summary>
public sealed class ServicoReconciliacao(
    IEnumerable<IEtapaFluxoPedido> etapas,
    IArmazenamentoPedidos armazenamento,
    RegistroFalhasReconciliacao registro,
    ILogger<ServicoReconciliacao> logger)
{
    /// <summary>Motivo do histórico no esgotamento da etapa de estoque (FR-012).</summary>
    public const string MotivoEstoqueNaoReservado = "Não foi possível reservar o estoque.";

    /// <summary>Motivo do histórico no esgotamento da etapa de pagamento (FR-012).</summary>
    public const string MotivoPagamentoNaoProcessado = "Não foi possível processar o pagamento.";

    /// <summary>Motivo do histórico no esgotamento da etapa de entrega (REQ-13, FR-011).</summary>
    public const string MotivoOrdemEntregaNaoCriada = "Não foi possível criar a ordem de entrega.";

    /// <summary>Como os consumidores (R-07).</summary>
    private const int Reavaliacoes = 3;

    public async Task<ResultadoReconciliacao> ReconciliarAsync(
        PedidoParado parado, DateTimeOffset agora, ParametrosEsgotamento parametros, CancellationToken cancellationToken)
    {
        bool gravou;
        try
        {
            // O último registro da etapa vence, como no contêiner de DI.
            var etapa = etapas.LastOrDefault(e => e.Etapa == parado.Etapa)
                ?? throw new InvalidOperationException($"Nenhuma regra registrada para a etapa {parado.Etapa}.");
            gravou = await etapa.AplicarAsync(Guid.Empty, parado.PedidoId, cancellationToken);
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return await TratarFalhaAsync(parado, agora, parametros, excecao, cancellationToken);
        }

        registro.Remover(parado.PedidoId);
        if (gravou)
        {
            EventosReconciliacao.PedidoRecuperado(logger, parado.PedidoId, parado.Etapa);
            return ResultadoReconciliacao.Recuperado;
        }

        EventosReconciliacao.PedidoNaoMaisParado(logger, parado.PedidoId, parado.Etapa);
        return ResultadoReconciliacao.NaoMaisParado;
    }

    /// <summary>
    /// A falha que completa a contagem é a da tentativa atual: a regra sempre é tentada antes do
    /// esgotamento. Desde o REQ-13 (FR-011), as três etapas se esgotam; a de entrega cancela com
    /// estorno pendente.
    /// </summary>
    private async Task<ResultadoReconciliacao> TratarFalhaAsync(
        PedidoParado parado, DateTimeOffset agora, ParametrosEsgotamento parametros, Exception excecao, CancellationToken cancellationToken)
    {
        armazenamento.DescartarAlteracoes();
        var falhas = registro.RegistrarFalha(parado.PedidoId, agora);
        EventosReconciliacao.FalhaPedido(logger, parado.PedidoId, parado.Etapa, falhas, Descrever(excecao));
        if (falhas < parametros.CiclosFalha || parado.AtualizadoEm > agora - parametros.LimiteEsgotamento)
        {
            return ResultadoReconciliacao.Falhou;
        }

        try
        {
            return await EsgotarAsync(parado, falhas, agora, parametros, cancellationToken);
        }
        catch (Exception erro) when (erro is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            armazenamento.DescartarAlteracoes();
            EventosReconciliacao.FalhaPedido(logger, parado.PedidoId, parado.Etapa, falhas, Descrever(erro));
            return ResultadoReconciliacao.Falhou;
        }
    }

    /// <summary>
    /// Ler, decidir e gravar, até <see cref="Reavaliacoes"/> vezes em conflito (R-07). O que a
    /// regra deixou rastreado é descartado antes; o pedido relido precisa continuar parado na
    /// mesma etapa, senão nada é cancelado. Grava junto o <c>pedido.cancelado</c> com a etapa
    /// esgotada (REQ-15, R-02); o descarte do início de cada reavaliação leva o evento junto.
    /// </summary>
    private async Task<ResultadoReconciliacao> EsgotarAsync(
        PedidoParado parado, int falhas, DateTimeOffset agora, ParametrosEsgotamento parametros, CancellationToken cancellationToken)
    {
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            armazenamento.DescartarAlteracoes();
            try
            {
                var pedido = await armazenamento.ObterPedidoAsync(parado.PedidoId, paraAlterar: true, cancellationToken);
                if (pedido is null || !ContinuaNaEtapa(pedido, parado.Etapa))
                {
                    registro.Remover(parado.PedidoId);
                    EventosReconciliacao.PedidoNaoMaisParado(logger, parado.PedidoId, parado.Etapa);
                    return ResultadoReconciliacao.NaoMaisParado;
                }

                if (pedido.AtualizadoEm > agora - parametros.LimiteEsgotamento)
                {
                    // Mudou de lá para cá sem sair da etapa: o prazo recomeça.
                    return ResultadoReconciliacao.Falhou;
                }

                var (motivo, resultado) = await CancelarAsync(pedido, parado.Etapa, agora, cancellationToken);
                armazenamento.Adicionar(EventosPedido.Cancelado(
                    pedido, MotivoCancelamento.ReconciliacaoEsgotada, EtapaDoContrato(parado.Etapa), resultado, agora));
                await armazenamento.SalvarAsync(cancellationToken);
                registro.Remover(parado.PedidoId);
                EventosReconciliacao.PedidoEsgotado(logger, parado.PedidoId, parado.Etapa, falhas, motivo);
                if (pedido.Pagamento?.Situacao == SituacaoPagamento.EstornoPendente)
                {
                    EventosPagamento.EstornoSolicitado(logger, pedido.Id, pedido.Pagamento.Valor, OrigemEstorno.Reconciliacao);
                }

                return ResultadoReconciliacao.Esgotado;
            }
            catch (ConflitoConcorrenciaException) when (reavaliacao < Reavaliacoes)
            {
                // Outra operação gravou o pedido: relê e decide de novo.
            }
        }
    }

    private static bool ContinuaNaEtapa(Pedido pedido, EtapaFluxoPedido etapa) => etapa switch
    {
        EtapaFluxoPedido.Estoque => !pedido.EstoqueReservado
            && TransicoesStatusPedido.PermitidaParaOperacao(pedido.Status, StatusPedido.Cancelado),
        EtapaFluxoPedido.Pagamento => pedido.AptoParaPagamento,
        EtapaFluxoPedido.Entrega => pedido.AptoParaOrdemEntrega,
        _ => false
    };

    private async Task<(string Motivo, ResultadoCancelamento Resultado)> CancelarAsync(
        Pedido pedido, EtapaFluxoPedido etapa, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        if (etapa == EtapaFluxoPedido.Estoque)
        {
            // Sem reserva, sem pagamento e sem ordem: o Cancelar só valida e grava o histórico (REQ-15, R-01).
            return (MotivoEstoqueNaoReservado, pedido.Cancelar(null, MotivoEstoqueNaoReservado, new Dictionary<Guid, Produto>(), agora));
        }

        var ids = pedido.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = (await armazenamento.ObterProdutosAsync(ids, paraAlterar: true, cancellationToken)).ToDictionary(p => p.Id);
        if (etapa == EtapaFluxoPedido.Pagamento)
        {
            return (MotivoPagamentoNaoProcessado, pedido.RegistrarPagamentoNaoProcessado(MotivoPagamentoNaoProcessado, produtos, agora));
        }

        // Entrega (REQ-13, R-06): o pedido está pago; o cancelamento deixa o estorno pendente.
        return (MotivoOrdemEntregaNaoCriada, pedido.Cancelar(null, MotivoOrdemEntregaNaoCriada, produtos, agora));
    }

    /// <summary>
    /// Conversão explícita para o contrato (REQ-15, R-04): uma etapa interna nova não vira valor
    /// do contrato sem decisão.
    /// </summary>
    private static EtapaEsgotada EtapaDoContrato(EtapaFluxoPedido etapa) => etapa switch
    {
        EtapaFluxoPedido.Estoque => EtapaEsgotada.Estoque,
        EtapaFluxoPedido.Pagamento => EtapaEsgotada.Pagamento,
        EtapaFluxoPedido.Entrega => EtapaEsgotada.Entrega,
        _ => throw new ArgumentOutOfRangeException(nameof(etapa), etapa, "Etapa sem valor no contrato do pedido.cancelado.")
    };

    private static string Descrever(Exception excecao) => $"{excecao.GetType().Name}: {excecao.Message}";
}
