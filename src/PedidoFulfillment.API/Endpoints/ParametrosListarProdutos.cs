using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.Application.Contratos.Produtos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Liga os parâmetros de <c>GET /api/v1/produtos</c> aos nomes do contrato (REQ-22) e delega a
/// validação ao <see cref="ListarProdutosRequestDTO"/>, como a listagem de pedidos (REQ-21).
/// </summary>
public sealed class ParametrosListarProdutos : IValidatableObject
{
    [FromQuery(Name = "busca")]
    public string? Busca { get; init; }

    [FromQuery(Name = "situacao")]
    public string? Situacao { get; init; }

    [FromQuery(Name = "tamanho")]
    public string? Tamanho { get; init; }

    [FromQuery(Name = "cursor")]
    public string? Cursor { get; init; }

    public ListarProdutosRequestDTO ParaRequisicao() => new()
    {
        Busca = Busca,
        Situacao = Situacao,
        Tamanho = Tamanho,
        Cursor = Cursor
    };

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        ParaRequisicao().Validate(validationContext);
}
