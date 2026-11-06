using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Application.Pedidos;

namespace PedidoFulfillment.Application.Usuarios;

/// <summary>Gestão de usuários e perfis pelo Admin (REQ-25).</summary>
public interface IServicoGestaoUsuarios
{
    Task<Resultado<PaginaUsuariosResponseDTO>> ListarAsync(ListarUsuariosRequestDTO requisicao, Guid atorId, CancellationToken cancellationToken);

    Task<Resultado<UsuarioGestaoResponseDTO>> ObterAsync(Guid usuarioId, CancellationToken cancellationToken);
}

/// <summary>
/// Regras da gestão de usuários (REQ-25). As rotas já chegam autorizadas só para o Admin ativo
/// (<c>PoliticaGestaoUsuarios</c>); as alterações conferem de novo dentro do lock (R-03).
/// </summary>
public sealed class ServicoGestaoUsuarios(
    IGestorUsuarios gestor,
    CodificadorCursorUsuarios codificador) : IServicoGestaoUsuarios
{
    private static readonly Falha CursorInvalido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.",
        Erros: new Dictionary<string, string[]> { [ServicoListagemPedidos.ChaveCursor] = [ServicoListagemPedidos.MensagemCursorInvalido] });

    public async Task<Resultado<PaginaUsuariosResponseDTO>> ListarAsync(ListarUsuariosRequestDTO requisicao, Guid atorId, CancellationToken cancellationToken)
    {
        var filtro = new FiltroListagemUsuarios
        {
            Perfil = requisicao.PerfilPedido(),
            Situacao = requisicao.SituacaoPedida(),
            Busca = requisicao.BuscaAparada()
        };

        PosicaoCursorUsuarios? posicao = null;
        if (requisicao.Cursor is not null && !codificador.TentarDecodificar(requisicao.Cursor, atorId, filtro, out posicao))
        {
            return CursorInvalido;
        }

        var tamanho = requisicao.TamanhoPagina();
        var lidas = await gestor.ListarAsync(filtro, posicao, tamanho + 1, cancellationToken);
        return MontarPagina(lidas, tamanho, posicao, atorId, filtro);
    }

    public async Task<Resultado<UsuarioGestaoResponseDTO>> ObterAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        await gestor.ObterAsync(usuarioId, cancellationToken) is { } conta ? Resposta(conta) : RegrasGestaoUsuarios.NaoEncontrado;

    private PaginaUsuariosResponseDTO MontarPagina(
        IReadOnlyList<ContaGestao> lidas, int tamanho, PosicaoCursorUsuarios? posicao, Guid atorId, FiltroListagemUsuarios filtro)
    {
        var haMais = lidas.Count > tamanho;
        var paraTras = posicao?.Sentido == SentidoCursor.Anterior;
        var contas = paraTras ? lidas.Skip(haMais ? 1 : 0).ToList() : lidas.Take(tamanho).ToList();

        string Cursor(SentidoCursor sentido, ContaGestao conta) =>
            codificador.Codificar(new PosicaoCursorUsuarios(sentido, conta.Nome, conta.Id), atorId, filtro);

        string? Volta(SentidoCursor sentido) => posicao is null ? null
            : codificador.Codificar(posicao with { Sentido = sentido, Inclusiva = true }, atorId, filtro);

        string? proximo;
        string? anterior;
        if (contas.Count == 0)
        {
            proximo = paraTras ? Volta(SentidoCursor.Proxima) : null;
            anterior = paraTras ? null : Volta(SentidoCursor.Anterior);
        }
        else if (paraTras)
        {
            proximo = Cursor(SentidoCursor.Proxima, contas[^1]);
            anterior = haMais ? Cursor(SentidoCursor.Anterior, contas[0]) : null;
        }
        else
        {
            proximo = haMais ? Cursor(SentidoCursor.Proxima, contas[^1]) : null;
            anterior = posicao is null ? null : Cursor(SentidoCursor.Anterior, contas[0]);
        }

        return new PaginaUsuariosResponseDTO
        {
            Itens = [.. contas.Select(Resposta)],
            Tamanho = tamanho,
            Proximo = proximo,
            Anterior = anterior
        };
    }

    private static UsuarioGestaoResponseDTO Resposta(ContaGestao conta) => new()
    {
        Id = conta.Id,
        Nome = conta.Nome,
        Email = conta.Email,
        Perfis = conta.Perfis,
        Situacao = conta.Desativada ? SituacaoUsuario.Desativada : SituacaoUsuario.Ativa,
        EmailConfirmado = conta.EmailConfirmado,
        ConvitePendente = conta.ConvitePendente
    };
}
