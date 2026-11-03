using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.Application.Contratos.Pedidos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Liga os parâmetros de consulta de <c>GET /api/v1/pedidos</c> aos nomes do contrato (REQ-21) e
/// delega a validação ao <see cref="ListarPedidosRequestDTO"/>, que fica na Application sem
/// depender do ASP.NET Core. Tudo como texto, para o 400 trazer todos os erros (research R-05).
/// </summary>
public sealed class ParametrosListarPedidos : IValidatableObject
{
    [FromQuery(Name = "status")]
    public string[]? Status { get; init; }

    [FromQuery(Name = "criadoDe")]
    public string? CriadoDe { get; init; }

    [FromQuery(Name = "criadoAte")]
    public string? CriadoAte { get; init; }

    [FromQuery(Name = "atualizadoDe")]
    public string? AtualizadoDe { get; init; }

    [FromQuery(Name = "atualizadoAte")]
    public string? AtualizadoAte { get; init; }

    [FromQuery(Name = "clienteId")]
    public string? ClienteId { get; init; }

    [FromQuery(Name = "tamanho")]
    public string? Tamanho { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    public ListarPedidosRequestDTO ParaRequisicao() => new()
    {
        Status = Status,
        CriadoDe = CriadoDe,
        CriadoAte = CriadoAte,
        AtualizadoDe = AtualizadoDe,
        AtualizadoAte = AtualizadoAte,
        ClienteId = ClienteId,
        Tamanho = Tamanho,
        Cursor = Cursor
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ParaRequisicao().Validate(validationContext);
}
