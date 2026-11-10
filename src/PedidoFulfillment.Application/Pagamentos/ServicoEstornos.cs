using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Resultado de um estorno pendente numa rodada (REQ-13, R-04).</summary>
public enum ResultadoRodadaEstorno
{
    Confirmado = 1,

    /// <summary>O pedido não existe ou o estorno já não está pendente.</summary>
    NadaAFazer,

    /// <summary>Recusa ou falha do gateway; o estorno continua pendente.</summary>
    Falhou,

    /// <summary>
    /// Circuito do gateway aberto (REQ-19, FR-016a): nada gravado, tentativa não contada; a rodada
    /// termina e o estorno é tentado na seguinte.
    /// </summary>
    Adiado
}

/// <summary>
/// Decide um estorno pendente (REQ-13, R-04): pede o estorno ao gateway e grava a confirmação ou
/// a falha. O estorno nunca desiste; depois do limite de alerta, cada falha é um erro no log.
/// A confirmação grava junto o <c>pagamento.estornado</c> (REQ-15, R-05). Um escopo por pedido: a
/// rodada, no Infrastructure, cria o escopo.
/// </summary>
public sealed class ServicoEstornos(
    IArmazenamentoPedidos armazenamento, IGatewayPagamento gateway, TimeProvider relogio, ILogger<ServicoEstornos> logger)
{
    /// <summary>Como os consumidores (REQ-09, R-05).</summary>
    private const int Reavaliacoes = 3;

    /// <summary>Quando o gateway volta a ser testado, se o último estorno ficou <see cref="ResultadoRodadaEstorno.Adiado"/>.</summary>
    public DateTimeOffset? AdiadoAte { get; private set; }

    public async Task<ResultadoRodadaEstorno> EstornarAsync(Guid pedidoId, ParametrosAlertaEstorno parametros, CancellationToken cancellationToken)
    {
        if (await LerPendenteAsync(pedidoId, cancellationToken) is not { } pedido)
        {
            return ResultadoRodadaEstorno.NadaAFazer;
        }

        var pagamento = pedido.Pagamento!;
        ResultadoEstorno resultado;
        try
        {
            resultado = await gateway.EstornarAsync(pedidoId, pagamento.IdentificadorTransacao!, pagamento.Valor, cancellationToken);
        }
        catch (GatewayPagamentoIndisponivelException excecao)
        {
            // Sem chamada e sem contar tentativa (REQ-19, FR-016a).
            armazenamento.DescartarAlteracoes();
            AdiadoAte = excecao.DisponivelEm;
            return ResultadoRodadaEstorno.Adiado;
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return await RegistrarFalhaAsync(
                pedidoId, new Falha($"{excecao.GetType().Name}: {excecao.Message}", DescricaoFalhaEstorno.Falha(excecao)), parametros, cancellationToken);
        }

        return resultado.Confirmado
            ? await ConfirmarAsync(pedido, resultado.IdentificadorEstorno!, cancellationToken)
            : await RegistrarFalhaAsync(
                pedidoId, new Falha($"Recusado: {resultado.Motivo}", DescricaoFalhaEstorno.Recusado(resultado.Motivo)), parametros, cancellationToken);
    }

    /// <summary>
    /// O estorno confirmado vale para as reavaliações: o gateway não é chamado de novo (R-04). Se
    /// outra instância confirmou antes, nada a gravar.
    /// </summary>
    private async Task<ResultadoRodadaEstorno> ConfirmarAsync(Pedido pedido, string identificadorEstorno, CancellationToken cancellationToken)
    {
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            var tentativas = pedido.Pagamento!.TentativasEstorno;
            var agora = relogio.GetUtcNow();
            pedido.ConfirmarEstorno(identificadorEstorno, agora);
            armazenamento.Adicionar(EventoEstornado(pedido, agora));
            try
            {
                await armazenamento.SalvarAsync(cancellationToken);
                EventosPagamento.EstornoConfirmado(logger, pedido.Id, pedido.Pagamento.Valor, tentativas);
                return ResultadoRodadaEstorno.Confirmado;
            }
            catch (ConflitoConcorrenciaException) when (reavaliacao < Reavaliacoes)
            {
                armazenamento.DescartarAlteracoes();
                if (await LerPendenteAsync(pedido.Id, cancellationToken) is not { } relido)
                {
                    return ResultadoRodadaEstorno.NadaAFazer;
                }

                pedido = relido;
            }
            catch (ConflitoConcorrenciaException)
            {
                // Esgotadas as reavaliações: a próxima rodada chama o gateway de novo, que devolve
                // o mesmo estorno (R-03). A tentativa não é contada: o gateway confirmou.
                armazenamento.DescartarAlteracoes();
                EventosPagamento.FalhaEstorno(logger, pedido.Id, tentativas, "Conflito de concorrência ao gravar a confirmação.");
                return ResultadoRodadaEstorno.Falhou;
            }
        }
    }

    /// <summary>
    /// A falha de uma tentativa: o texto do log (como no REQ-13, sem identificadores do gateway) e o
    /// texto gravado para a operação, só a categoria (REQ-28, R-05).
    /// </summary>
    private sealed record Falha(string TextoLog, string TextoGravado);

    /// <summary>
    /// Conta a tentativa sobre o pedido relido. Se a gravação der conflito, a tentativa não é
    /// contada; a próxima rodada trata o pedido de novo (R-04, passo 4). A falha que leva o estorno
    /// além do limite grava, na mesma gravação e uma única vez, o <c>pagamento.estorno-atrasado</c>
    /// (REQ-28, R-06); num conflito ele vai embora com o resto, e a próxima falha tenta de novo.
    /// </summary>
    private async Task<ResultadoRodadaEstorno> RegistrarFalhaAsync(
        Guid pedidoId, Falha falha, ParametrosAlertaEstorno parametros, CancellationToken cancellationToken)
    {
        var erro = falha.TextoLog;
        armazenamento.DescartarAlteracoes();
        if (await LerPendenteAsync(pedidoId, cancellationToken) is not { } pedido)
        {
            return ResultadoRodadaEstorno.NadaAFazer;
        }

        var agora = relogio.GetUtcNow();
        var pagamento = pedido.Pagamento!;
        var solicitadoEm = pagamento.EstornoSolicitadoEm!.Value;
        var tentativas = pagamento.TentativasEstorno + 1;
        pedido.RegistrarFalhaEstorno(agora, DescricaoFalhaEstorno.Limitar(falha.TextoGravado));
        var atrasou = RegrasAlertaEstorno.AlemDoLimite(solicitadoEm, tentativas, agora, parametros) && pedido.MarcarEstornoAtrasado(agora);
        if (atrasou)
        {
            armazenamento.Adicionar(EventoAtrasado(pedido, tentativas, agora));
        }

        try
        {
            await armazenamento.SalvarAsync(cancellationToken);
        }
        catch (ConflitoConcorrenciaException)
        {
            armazenamento.DescartarAlteracoes();
            tentativas--;
            atrasou = false;
        }

        var pendente = agora - solicitadoEm;
        if (RegrasAlertaEstorno.AlemDoLimite(solicitadoEm, tentativas, agora, parametros))
        {
            EventosPagamento.EstornoPendenteAlemDoLimite(logger, pedidoId, tentativas, (int)pendente.TotalHours, erro);
        }
        else
        {
            EventosPagamento.FalhaEstorno(logger, pedidoId, tentativas, erro);
        }

        if (atrasou)
        {
            EventosPagamento.EstornoAtrasadoPublicado(logger, pagamento.Id, pedidoId, tentativas, (int)pendente.TotalHours);
        }

        return ResultadoRodadaEstorno.Falhou;
    }

    /// <summary><c>pagamento.estorno-atrasado</c> da falha que passou do limite (REQ-28, FR-017), sem dados do gateway.</summary>
    private static EventoSaida EventoAtrasado(Pedido pedido, int tentativas, DateTimeOffset agora)
    {
        var pagamento = pedido.Pagamento!;
        return EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoEstornoAtrasado
        {
            EstornoId = pagamento.Id,
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            Valor = pagamento.Valor,
            SolicitadoEm = pagamento.EstornoSolicitadoEm!.Value,
            Tentativas = tentativas,
            AtrasadoEm = agora
        }, agora), agora);
    }

    private async Task<Pedido?> LerPendenteAsync(Guid pedidoId, CancellationToken cancellationToken)
    {
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken);
        return pedido?.Pagamento?.Situacao == SituacaoPagamento.EstornoPendente ? pedido : null;
    }

    /// <summary><c>pagamento.estornado</c> do estorno recém-confirmado, com o instante gravado no pagamento (REQ-15, R-05).</summary>
    private static EventoSaida EventoEstornado(Pedido pedido, DateTimeOffset agora)
    {
        var pagamento = pedido.Pagamento!;
        return EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoEstornado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            Valor = pagamento.Valor,
            IdentificadorTransacao = pagamento.IdentificadorTransacao!,
            IdentificadorEstorno = pagamento.IdentificadorEstorno!,
            EstornadoEm = agora
        }, agora), agora);
    }
}
