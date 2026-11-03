using System.ComponentModel.DataAnnotations;
using PedidoFulfillment.Application.Notificacoes;

namespace PedidoFulfillment.Application.Contratos.Notificacoes;

/// <summary>
/// Cadastro de um destino de webhook (REQ-18, FR-008; <c>contracts/api-notificacoes.md</c>). Aqui
/// só a forma da URL; o esquema <c>https</c> e o endereço público são conferidos pelo serviço, que
/// conhece a configuração. A URL é aparada ao chegar; o <see cref="Validate"/> só roda depois que
/// os atributos passam.
/// </summary>
public sealed record CadastrarDestinoRequestDTO : IValidatableObject
{
    [Required(ErrorMessage = "A URL é obrigatória.")]
    [MaxLength(DestinoNotificacao.TamanhoMaximoUrl, ErrorMessage = "A URL deve ter até 2048 caracteres.")]
    public string Url { get; init => field = value?.Trim() ?? ""; } = "";

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!Uri.TryCreate(Url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            yield return new ValidationResult("A URL deve ser absoluta, com https.", [nameof(Url)]);
        }
        else if (uri.UserInfo.Length > 0)
        {
            yield return new ValidationResult("A URL não pode ter usuário nem senha.", [nameof(Url)]);
        }
    }
}

/// <summary>Um destino, sem o segredo (REQ-18, FR-009 e FR-011).</summary>
public record DestinoNotificacaoResponseDTO
{
    public required Guid Id { get; init; }

    public required string Url { get; init; }

    public required bool Global { get; init; }

    public required bool Ativo { get; init; }

    public string? MotivoDesativacao { get; init; }

    public DateTimeOffset? DesativadoEm { get; init; }

    public required int FalhasSeguidas { get; init; }

    public required DateTimeOffset CriadoEm { get; init; }

    public static DestinoNotificacaoResponseDTO De(DestinoNotificacao destino) => new()
    {
        Id = destino.Id,
        Url = destino.Url,
        Global = destino.Global,
        Ativo = destino.Ativo,
        MotivoDesativacao = destino.MotivoDesativacao,
        DesativadoEm = destino.DesativadoEm,
        FalhasSeguidas = destino.FalhasSeguidas,
        CriadoEm = destino.CriadoEm
    };
}

/// <summary>O destino recém-cadastrado, com o segredo mostrado só desta vez (FR-009).</summary>
public sealed record DestinoCadastradoResponseDTO : DestinoNotificacaoResponseDTO
{
    /// <summary><c>whsec_</c> + Base64 de 32 bytes, no formato do Standard Webhooks.</summary>
    public required string Segredo { get; init; }
}

/// <summary>Uma entrega do destino (REQ-18, FR-011), sem URL completa nem corpo de resposta.</summary>
public sealed record EntregaNotificacaoResponseDTO
{
    public required Guid IdNotificacao { get; init; }

    public required string Tipo { get; init; }

    public required Guid PedidoId { get; init; }

    public required SituacaoEntrega Situacao { get; init; }

    public required int Tentativas { get; init; }

    public int? UltimoCodigoHttp { get; init; }

    public string? UltimoErro { get; init; }

    public required DateTimeOffset CriadaEm { get; init; }

    public DateTimeOffset? ProximaTentativaEm { get; init; }

    public DateTimeOffset? FinalizadaEm { get; init; }

    public static EntregaNotificacaoResponseDTO De(ResumoEntrega entrega) => new()
    {
        IdNotificacao = entrega.IdNotificacao,
        Tipo = entrega.Tipo,
        PedidoId = entrega.PedidoId,
        Situacao = entrega.Situacao,
        Tentativas = entrega.Tentativas,
        UltimoCodigoHttp = entrega.UltimoCodigoHttp,
        UltimoErro = entrega.UltimoErro,
        CriadaEm = entrega.CriadaEm,
        ProximaTentativaEm = entrega.Situacao == SituacaoEntrega.Pendente ? entrega.ProximaTentativaEm : null,
        FinalizadaEm = entrega.FinalizadaEm
    };
}
