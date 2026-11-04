using System.Net;

namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>Resultado da verificação: aceita, ou recusada com a espera em segundos inteiros.</summary>
public readonly record struct DecisaoLimite(bool Aceita, int RetryAfterSegundos)
{
    public static DecisaoLimite Aceitar => new(true, 0);
}

/// <summary>Decide se uma requisição a uma rota limitada segue ou recebe 429 (REQ-23).</summary>
public interface IServicoLimiteRequisicoes
{
    /// <param name="rota">A rota chamada.</param>
    /// <param name="endereco">O endereço do cliente, depois dos intermediários confiáveis.</param>
    /// <param name="email">O e-mail do login; nulo sem um e-mail utilizável (FR-003).</param>
    /// <param name="cancellationToken">Cancelamento pelo cliente: propaga, e não conta como falha.</param>
    Task<DecisaoLimite> AvaliarAsync(RotaLimitada rota, IPAddress? endereco, string? email, CancellationToken cancellationToken);
}
