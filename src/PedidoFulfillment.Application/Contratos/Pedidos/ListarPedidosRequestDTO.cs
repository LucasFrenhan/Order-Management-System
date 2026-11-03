using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.RegularExpressions;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Contratos.Pedidos;

/// <summary>
/// Parâmetros de <c>GET /api/v1/pedidos</c> (REQ-21). Chegam como texto para que todos os
/// erros voltem de uma vez e para conferir o fuso das datas no texto original (research R-05).
/// O cursor é conferido depois, no serviço, porque depende do usuário (FR-022).
/// </summary>
public sealed partial record ListarPedidosRequestDTO : IValidatableObject
{
    public const int TamanhoPadrao = 20;
    public const int TamanhoMaximo = 100;

    public IReadOnlyList<string>? Status { get; init; }

    public string? CriadoDe { get; init; }

    public string? CriadoAte { get; init; }

    public string? AtualizadoDe { get; init; }

    public string? AtualizadoAte { get; init; }

    public string? ClienteId { get; init; }

    public string? Tamanho { get; init; }

    public string? Cursor { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var valor in Status ?? [])
        {
            if (!TentarStatus(valor, out _))
            {
                yield return new ValidationResult($"O status '{valor}' não existe.", ["status"]);
            }
        }

        var datas = new[] { ("criadoDe", CriadoDe), ("criadoAte", CriadoAte), ("atualizadoDe", AtualizadoDe), ("atualizadoAte", AtualizadoAte) };
        foreach (var (nome, texto) in datas)
        {
            if (texto is not null && !TentarData(texto, out _))
            {
                yield return new ValidationResult(
                    $"O parâmetro {nome} deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00.", [nome]);
            }
        }

        if (Invertido(CriadoDe, CriadoAte))
        {
            yield return new ValidationResult("O início do período de criação deve ser anterior ou igual ao fim.", ["criadoDe"]);
        }

        if (Invertido(AtualizadoDe, AtualizadoAte))
        {
            yield return new ValidationResult("O início do período de última alteração deve ser anterior ou igual ao fim.", ["atualizadoDe"]);
        }

        if (ClienteId is not null && !Guid.TryParse(ClienteId, out _))
        {
            yield return new ValidationResult("O parâmetro clienteId deve ser um UUID.", ["clienteId"]);
        }

        if (Tamanho is not null && !TentarTamanho(Tamanho, out _))
        {
            yield return new ValidationResult($"O tamanho deve ser um número entre 1 e {TamanhoMaximo}.", ["tamanho"]);
        }
    }

    /// <summary>Filtro convertido; só deve ser chamado depois da validação.</summary>
    public FiltroListagemPedidos ParaFiltro() => new()
    {
        Status = (Status ?? []).Select(s => TentarStatus(s, out var status) ? status : throw Invalido(nameof(Status))).ToHashSet(),
        CriadoDe = Data(CriadoDe),
        CriadoAte = Data(CriadoAte),
        AtualizadoDe = Data(AtualizadoDe),
        AtualizadoAte = Data(AtualizadoAte),
        ClienteId = ClienteId is null ? null : Guid.Parse(ClienteId)
    };

    /// <summary>Tamanho da página; o padrão quando ausente. Só depois da validação.</summary>
    public int TamanhoPagina() => Tamanho is null ? TamanhoPadrao
        : TentarTamanho(Tamanho, out var tamanho) ? tamanho : throw Invalido(nameof(Tamanho));

    private static bool TentarStatus(string texto, out StatusPedido status) =>
        Enum.TryParse(texto, ignoreCase: true, out status) && Enum.IsDefined(status) && !int.TryParse(texto, out _);

    private static bool TentarTamanho(string texto, out int tamanho) =>
        int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out tamanho) && tamanho is >= 1 and <= TamanhoMaximo;

    /// <summary>ISO 8601 com data, hora e fuso explícito (<c>Z</c> ou <c>±hh:mm</c>).</summary>
    private static bool TentarData(string texto, out DateTimeOffset data)
    {
        data = default;
        return ComFuso().IsMatch(texto)
            && DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.None, out data);
    }

    private static DateTimeOffset? Data(string? texto) =>
        texto is null ? null : TentarData(texto, out var data) ? data.ToUniversalTime() : throw Invalido("data");

    private static bool Invertido(string? inicio, string? fim) =>
        inicio is not null && fim is not null && TentarData(inicio, out var de) && TentarData(fim, out var ate) && de > ate;

    private static InvalidOperationException Invalido(string parametro) =>
        new($"O parâmetro {parametro} não foi validado antes da conversão.");

    [GeneratedRegex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}(:\d{2}(\.\d{1,7})?)?(Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ComFuso();
}
