using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Estornos;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Ações da operação sobre os estornos (REQ-28).</summary>
public interface IServicoAcoesEstornos
{
    Task<Resultado<PaginaEstornosResponseDTO>> ListarPendentesAsync(ListarEstornosRequestDTO requisicao, Guid atorId, CancellationToken cancellationToken);

    Task<Resultado<EstornoResponseDTO>> ObterAsync(Guid estornoId, CancellationToken cancellationToken);

    Task<Resultado<EstornoResponseDTO>> DarBaixaAsync(Guid autorId, Guid estornoId, BaixaEstornoRequestDTO baixa, CancellationToken cancellationToken);

    Task<Resultado<EstornoResponseDTO>> PedirTentativaAsync(Guid autorId, Guid estornoId, CancellationToken cancellationToken);
}

/// <summary>
/// Ações da operação sobre os estornos (REQ-28): visibilidade (US1), baixa manual (US2) e tentativa
/// forçada (US4). A autorização
/// vem das políticas da API; a regra do alerta é a mesma do log 7008 (<see cref="RegrasAlertaEstorno"/>,
/// R-09); a baixa só grava com o lock da rodada (R-03).
/// </summary>
public sealed class ServicoAcoesEstornos(
    IConsultaEstornos consulta,
    IArmazenamentoPedidos armazenamento,
    IBloqueioRodadaEstornos bloqueio,
    CodificadorCursorEstornos codificador,
    ParametrosAlertaEstorno parametros,
    TimeProvider relogio,
    ILogger<ServicoAcoesEstornos> logger) : IServicoAcoesEstornos
{
    /// <summary>Quanto a baixa espera a rodada terminar (REQ-28, clarify).</summary>
    public static readonly TimeSpan EsperaRodada = TimeSpan.FromSeconds(5);

    /// <summary>Releituras num conflito de versão com outra ação da API (a rodada está bloqueada).</summary>
    private const int Reavaliacoes = 3;

    public static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Estorno não encontrado.", "Nenhum estorno com o identificador informado.");

    public static readonly Falha NaoPendente = new(
        TipoFalha.Conflito, "Estorno não está pendente.", "O estorno já foi confirmado pelo gateway ou baixado.", CodigosErro.EstornoNaoPendente);

    public static readonly Falha RodadaEmAndamento = new(
        TipoFalha.Conflito, "Rodada de estornos em andamento.", "Rodada de estornos em andamento; tente de novo em instantes.",
        CodigosErro.EstornoRodadaEmAndamento);

    private static readonly Falha CursorInvalido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.",
        Erros: new Dictionary<string, string[]> { [ServicoListagemPedidos.ChaveCursor] = [ServicoListagemPedidos.MensagemCursorInvalido] });

    public async Task<Resultado<PaginaEstornosResponseDTO>> ListarPendentesAsync(
        ListarEstornosRequestDTO requisicao, Guid atorId, CancellationToken cancellationToken)
    {
        PosicaoCursorEstornos? posicao = null;
        if (requisicao.Cursor is not null && !codificador.TentarDecodificar(requisicao.Cursor, atorId, out posicao))
        {
            return CursorInvalido;
        }

        var tamanho = requisicao.TamanhoPagina();
        var lidos = await consulta.ListarPendentesAsync(posicao, tamanho + 1, cancellationToken);
        return MontarPagina(lidos, tamanho, posicao, atorId);
    }

    public async Task<Resultado<EstornoResponseDTO>> ObterAsync(Guid estornoId, CancellationToken cancellationToken) =>
        await consulta.ObterAsync(estornoId, cancellationToken) is { } lido ? Resposta(lido) : NaoEncontrado;

    /// <summary>
    /// Baixa manual (REQ-28, FR-006 a FR-010a): com o lock da rodada (até 5 s), relê o estorno, leva a
    /// <c>EstornoBaixado</c> e grava, na mesma transação, o <c>pagamento.estorno-baixado</c> (o aviso ao
    /// Cliente vem do armazenamento). O gateway nunca é chamado.
    /// </summary>
    public async Task<Resultado<EstornoResponseDTO>> DarBaixaAsync(
        Guid autorId, Guid estornoId, BaixaEstornoRequestDTO baixa, CancellationToken cancellationToken)
    {
        if (await consulta.ObterAsync(estornoId, cancellationToken) is not { } lido)
        {
            return NaoEncontrado;
        }

        if (lido.Situacao != SituacaoPagamento.EstornoPendente)
        {
            return NaoPendente;
        }

        Falha? falha = null;
        var conseguiu = await bloqueio.ExecutarAsync(EsperaRodada, async ct =>
        {
            falha = await GravarBaixaAsync(lido.PedidoId, autorId, baixa, ct);
        }, cancellationToken);

        if (!conseguiu)
        {
            EventosPagamento.BaixaEstornoRecusadaRodadaEmAndamento(logger, estornoId, autorId, (int)EsperaRodada.TotalSeconds);
            return RodadaEmAndamento;
        }

        if (falha is not null)
        {
            return falha;
        }

        EventosPagamento.EstornoBaixado(logger, estornoId, lido.PedidoId, lido.Valor, autorId);
        return await ObterAsync(estornoId, cancellationToken);
    }

    /// <summary>
    /// Tentativa forçada (REQ-28, FR-011 a FR-014): só marca o estorno; a rodada o trata primeiro, com o
    /// lock e o circuito de sempre. Repetir o pedido não muda a marca. Um conflito com a rodada (que
    /// gravou uma falha ao mesmo tempo) relê e tenta de novo.
    /// </summary>
    public async Task<Resultado<EstornoResponseDTO>> PedirTentativaAsync(Guid autorId, Guid estornoId, CancellationToken cancellationToken)
    {
        if (await consulta.ObterAsync(estornoId, cancellationToken) is not { } lido)
        {
            return NaoEncontrado;
        }

        for (var reavaliacao = 1; ; reavaliacao++)
        {
            var pedido = await armazenamento.ObterPedidoAsync(lido.PedidoId, paraAlterar: true, cancellationToken);
            if (pedido?.Pagamento is not { Situacao: SituacaoPagamento.EstornoPendente })
            {
                return NaoPendente;
            }

            if (!pedido.PedirTentativaEstorno(relogio.GetUtcNow()))
            {
                break;
            }

            try
            {
                await armazenamento.SalvarAsync(cancellationToken);
                break;
            }
            catch (ConflitoConcorrenciaException) when (reavaliacao < Reavaliacoes)
            {
                armazenamento.DescartarAlteracoes();
            }
        }

        EventosPagamento.TentativaEstornoForcada(logger, estornoId, lido.PedidoId, autorId);
        return await ObterAsync(estornoId, cancellationToken);
    }

    /// <summary>Dentro do lock: a rodada não grava; um conflito só vem de outra ação da API, e relê.</summary>
    private async Task<Falha?> GravarBaixaAsync(Guid pedidoId, Guid autorId, BaixaEstornoRequestDTO baixa, CancellationToken cancellationToken)
    {
        for (var reavaliacao = 1; ; reavaliacao++)
        {
            var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken);
            if (pedido?.Pagamento is not { Situacao: SituacaoPagamento.EstornoPendente } pagamento)
            {
                return NaoPendente;
            }

            var agora = relogio.GetUtcNow();
            pedido.DarBaixaEstorno(autorId, baixa.Motivo, baixa.ReferenciaExterna, agora);
            armazenamento.Adicionar(EventoSaida.De(EnvelopeEvento.Criar(pedido.Id, new PagamentoEstornoBaixado
            {
                EstornoId = pagamento.Id,
                PedidoId = pedido.Id,
                Numero = pedido.Numero,
                Valor = pagamento.Valor,
                AutorId = autorId,
                BaixadoEm = agora,
                ReferenciaExterna = pagamento.ReferenciaExternaBaixaEstorno
            }, agora), agora));
            try
            {
                await armazenamento.SalvarAsync(cancellationToken);
                return null;
            }
            catch (ConflitoConcorrenciaException) when (reavaliacao < Reavaliacoes)
            {
                armazenamento.DescartarAlteracoes();
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                return NaoPendente;
            }
        }
    }

    /// <summary>Monta o contrato a partir do estorno lido, com o alerta calculado agora.</summary>
    public EstornoResponseDTO Resposta(EstornoLido lido)
    {
        var situacao = lido.Situacao switch
        {
            SituacaoPagamento.EstornoPendente => SituacaoEstorno.Pendente,
            SituacaoPagamento.Estornado => SituacaoEstorno.Confirmado,
            SituacaoPagamento.EstornoBaixado => SituacaoEstorno.Baixado,
            _ => throw new InvalidOperationException($"O pagamento {lido.EstornoId} não tem estorno: {lido.Situacao}.")
        };

        return new EstornoResponseDTO
        {
            EstornoId = lido.EstornoId,
            PedidoId = lido.PedidoId,
            Numero = lido.Numero,
            Valor = lido.Valor,
            Situacao = situacao,
            SolicitadoEm = lido.SolicitadoEm,
            Tentativas = lido.Tentativas,
            UltimaFalhaEm = lido.UltimaFalhaEm,
            UltimoErro = lido.UltimoErro,
            AlemDoLimiteAlerta = situacao == SituacaoEstorno.Pendente
                && RegrasAlertaEstorno.AlemDoLimite(lido.SolicitadoEm, lido.Tentativas, relogio.GetUtcNow(), parametros),
            AtrasadoEm = lido.AtrasadoEm,
            TentativaForcadaEm = lido.TentativaForcadaEm,
            ConfirmadoEm = lido.ConfirmadoEm,
            Baixa = lido is { BaixadoEm: { } baixadoEm, BaixadoPorId: { } autor, MotivoBaixa: { } motivo }
                ? new BaixaEstornoResponseDTO { AutorId = autor, BaixadoEm = baixadoEm, Motivo = motivo, ReferenciaExterna = lido.ReferenciaExternaBaixa }
                : null
        };
    }

    private PaginaEstornosResponseDTO MontarPagina(IReadOnlyList<EstornoLido> lidos, int tamanho, PosicaoCursorEstornos? posicao, Guid atorId)
    {
        var haMais = lidos.Count > tamanho;
        var paraTras = posicao?.Sentido == SentidoCursor.Anterior;
        var estornos = paraTras ? lidos.Skip(haMais ? 1 : 0).ToList() : lidos.Take(tamanho).ToList();

        string Cursor(SentidoCursor sentido, EstornoLido estorno) =>
            codificador.Codificar(new PosicaoCursorEstornos(sentido, estorno.SolicitadoEm, estorno.EstornoId), atorId);

        string? Volta(SentidoCursor sentido) => posicao is null ? null
            : codificador.Codificar(posicao with { Sentido = sentido, Inclusiva = true }, atorId);

        string? proximo;
        string? anterior;
        if (estornos.Count == 0)
        {
            proximo = paraTras ? Volta(SentidoCursor.Proxima) : null;
            anterior = paraTras ? null : Volta(SentidoCursor.Anterior);
        }
        else if (paraTras)
        {
            proximo = Cursor(SentidoCursor.Proxima, estornos[^1]);
            anterior = haMais ? Cursor(SentidoCursor.Anterior, estornos[0]) : null;
        }
        else
        {
            proximo = haMais ? Cursor(SentidoCursor.Proxima, estornos[^1]) : null;
            anterior = posicao is null ? null : Cursor(SentidoCursor.Anterior, estornos[0]);
        }

        return new PaginaEstornosResponseDTO
        {
            Itens = [.. estornos.Select(Resposta)],
            Tamanho = tamanho,
            Proximo = proximo,
            Anterior = anterior
        };
    }
}
