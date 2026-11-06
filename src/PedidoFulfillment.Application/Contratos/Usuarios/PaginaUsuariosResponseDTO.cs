namespace PedidoFulfillment.Application.Contratos.Usuarios;

/// <summary>Uma página da listagem de usuários, como a de produtos (REQ-25, FR-004).</summary>
public sealed record PaginaUsuariosResponseDTO
{
    public required IReadOnlyList<UsuarioGestaoResponseDTO> Itens { get; init; }

    public required int Tamanho { get; init; }

    /// <summary>Cursor da próxima página; nulo na última.</summary>
    public required string? Proximo { get; init; }

    /// <summary>Cursor da página anterior; nulo na primeira.</summary>
    public required string? Anterior { get; init; }
}
