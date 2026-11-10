using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.Application.Contratos.Estornos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Liga os parâmetros de <c>GET /api/v1/estornos/pendentes</c> aos nomes do contrato (REQ-28) e delega
/// a validação ao <see cref="ListarEstornosRequestDTO"/>, como as demais listagens.
/// </summary>
public sealed class ParametrosListarEstornos : IValidatableObject
{
    [FromQuery(Name = "tamanho")]
    public string? Tamanho { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    public ListarEstornosRequestDTO ParaRequisicao() => new() { Tamanho = Tamanho, Cursor = Cursor };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ParaRequisicao().Validate(validationContext);
}
