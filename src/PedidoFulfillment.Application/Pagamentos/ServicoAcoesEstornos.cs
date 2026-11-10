using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Estornos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Ações da operação sobre os estornos (REQ-28).</summary>
public interface IServicoAcoesEstornos
{
    Task<Resultado<PaginaEstornosResponseDTO>> ListarPendentesAsync(ListarEstornosRequestDTO requisicao, Guid atorId, CancellationToken cancellationToken);

    Task<Resultado<EstornoResponseDTO>> ObterAsync(Guid estornoId, CancellationToken cancellationToken);
}

/// <summary>
/// Visibilidade dos estornos para a operação (REQ-28, US1). A autorização vem das políticas da API;
/// a regra do alerta é a mesma do log 7008 (<see cref="RegrasAlertaEstorno"/>, R-09).
/// </summary>
public sealed class ServicoAcoesEstornos(
    IConsultaEstornos consulta,
    CodificadorCursorEstornos codificador,
    ParametrosAlertaEstorno parametros,
    TimeProvider relogio) : IServicoAcoesEstornos
{
    public static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Estorno não encontrado.", "Nenhum estorno com o identificador informado.");

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
