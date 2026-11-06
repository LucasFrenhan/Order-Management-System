using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.Application.Contratos.Usuarios;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Liga os parâmetros de <c>GET /api/v1/admin/usuarios</c> aos nomes do contrato (REQ-25) e delega a
/// validação ao <see cref="ListarUsuariosRequestDTO"/>, como as demais listagens.
/// </summary>
public sealed class ParametrosListarUsuarios : IValidatableObject
{
    [FromQuery(Name = "perfil")]
    public string? Perfil { get; init; }

    [FromQuery(Name = "situacao")]
    public string? Situacao { get; init; }

    [FromQuery(Name = "busca")]
    public string? Busca { get; init; }

    [FromQuery(Name = "tamanho")]
    public string? Tamanho { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    public ListarUsuariosRequestDTO ParaRequisicao() => new()
    {
        Perfil = Perfil,
        Situacao = Situacao,
        Busca = Busca,
        Tamanho = Tamanho,
        Cursor = Cursor
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ParaRequisicao().Validate(validationContext);
}
