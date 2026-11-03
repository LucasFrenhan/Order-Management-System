using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>Listagem de pedidos por perfil (REQ-21).</summary>
public interface IServicoListagemPedidos
{
    /// <summary>Recebe a requisição já validada pelo filtro da rota (FR-021).</summary>
    Task<Resultado<PaginaPedidosResponseDTO>> ListarAsync(
        ListarPedidosRequestDTO requisicao, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);
}

/// <summary>
/// Regras da listagem (research R-02 e R-06): escopo por perfil, 403 do filtro por Cliente,
/// cursor ligado ao usuário e aos filtros e montagem dos cursores da página. Só leitura (FR-023).
/// </summary>
public sealed class ServicoListagemPedidos(
    IArmazenamentoPedidos armazenamento,
    CodificadorCursorPedidos codificador,
    ILogger<ServicoListagemPedidos> logger) : IServicoListagemPedidos
{
    public const string ChaveCursor = "cursor";
    public const string MensagemCursorInvalido = "O cursor é inválido para esta consulta; recomece da primeira página.";

    private static readonly Falha FiltroPorClienteProibido = new(
        TipoFalha.SemPermissao, "Operação não permitida.", "O filtro por cliente é exclusivo da operação.");

    private static readonly Falha CursorInvalido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.",
        Erros: new Dictionary<string, string[]> { [ChaveCursor] = [MensagemCursorInvalido] });

    public async Task<Resultado<PaginaPedidosResponseDTO>> ListarAsync(
        ListarPedidosRequestDTO requisicao, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        // FR-006: antes do cursor e do banco, qualquer que seja o valor.
        var operacao = ServicoPedidos.EhOperacao(perfis);
        if (!operacao && requisicao.ClienteId is not null)
        {
            return FiltroPorClienteProibido;
        }

        var filtro = requisicao.ParaFiltro();
        if (!operacao)
        {
            filtro = filtro with { ClienteId = usuarioId };
        }

        PosicaoCursor? posicao = null;
        if (requisicao.Cursor is not null && !codificador.TentarDecodificar(requisicao.Cursor, usuarioId, filtro, out posicao))
        {
            return CursorInvalido;
        }

        var tamanho = requisicao.TamanhoPagina();
        var lidos = await armazenamento.ListarResumosAsync(filtro, posicao, tamanho + 1, cancellationToken);
        var pagina = MontarPagina(lidos, tamanho, posicao, usuarioId, filtro);

        EventosPedidos.PedidosListados(logger, operacao ? "Operacao" : "Cliente", filtro.NomesUsados(),
            posicao?.Sentido.ToString() ?? "Inicio", pagina.Itens.Count);
        return pagina;
    }

    /// <summary>
    /// O item a mais diz se há outra página no sentido da navegação (research R-02). No sentido
    /// da anterior, o armazenamento já devolve na ordem da listagem, e o item a mais é o primeiro.
    /// </summary>
    private PaginaPedidosResponseDTO MontarPagina(
        IReadOnlyList<ResumoPedidoResponseDTO> lidos, int tamanho, PosicaoCursor? posicao, Guid usuarioId, FiltroListagemPedidos filtro)
    {
        var haMais = lidos.Count > tamanho;
        var paraTras = posicao?.Sentido == SentidoCursor.Anterior;
        var itens = paraTras ? lidos.Skip(haMais ? 1 : 0).ToList() : lidos.Take(tamanho).ToList();

        string? Cursor(SentidoCursor sentido, ResumoPedidoResponseDTO item) =>
            codificador.Codificar(new PosicaoCursor(sentido, item.CriadoEm, item.Numero), usuarioId, filtro);

        // Página vazia vinda de um cursor: o caminho de volta parte da própria posição, incluindo-a.
        string? Volta(SentidoCursor sentido) => posicao is null ? null
            : codificador.Codificar(posicao with { Sentido = sentido, Inclusiva = true }, usuarioId, filtro);

        string? proximo;
        string? anterior;
        if (itens.Count == 0)
        {
            proximo = paraTras ? Volta(SentidoCursor.Proxima) : null;
            anterior = paraTras ? null : Volta(SentidoCursor.Anterior);
        }
        else if (paraTras)
        {
            proximo = Cursor(SentidoCursor.Proxima, itens[^1]);
            anterior = haMais ? Cursor(SentidoCursor.Anterior, itens[0]) : null;
        }
        else
        {
            proximo = haMais ? Cursor(SentidoCursor.Proxima, itens[^1]) : null;
            anterior = posicao is null ? null : Cursor(SentidoCursor.Anterior, itens[0]);
        }

        return new PaginaPedidosResponseDTO { Itens = itens, Tamanho = tamanho, Proximo = proximo, Anterior = anterior };
    }
}
