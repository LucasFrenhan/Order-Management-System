using System.Globalization;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Filtros da listagem já convertidos (REQ-21, data-model). Os períodos são inclusivos e em
/// UTC. Para o Cliente, <see cref="ClienteId"/> é sempre o do token (FR-004).
/// </summary>
public sealed record FiltroListagemPedidos
{
    public IReadOnlySet<StatusPedido> Status { get; init; } = new HashSet<StatusPedido>();

    public DateTimeOffset? CriadoDe { get; init; }

    public DateTimeOffset? CriadoAte { get; init; }

    public DateTimeOffset? AtualizadoDe { get; init; }

    public DateTimeOffset? AtualizadoAte { get; init; }

    public Guid? ClienteId { get; init; }

    /// <summary>
    /// Texto único para os mesmos filtros (research R-03): status ordenados e sem repetição,
    /// datas em UTC e o Cliente. Entra no <c>mac</c> do cursor, para ele valer só nesta consulta.
    /// </summary>
    public string TextoCanonico() => string.Join('|',
        string.Join(',', Status.Order().Select(s => s.ToString())),
        Data(CriadoDe),
        Data(CriadoAte),
        Data(AtualizadoDe),
        Data(AtualizadoAte),
        ClienteId?.ToString("D") ?? "");

    /// <summary>Nomes dos filtros informados, sem os valores, para o log 4006 (research R-10).</summary>
    public string NomesUsados()
    {
        var nomes = new List<string>();
        Adicionar(nomes, Status.Count > 0, "status");
        Adicionar(nomes, CriadoDe is not null, "criadoDe");
        Adicionar(nomes, CriadoAte is not null, "criadoAte");
        Adicionar(nomes, AtualizadoDe is not null, "atualizadoDe");
        Adicionar(nomes, AtualizadoAte is not null, "atualizadoAte");
        Adicionar(nomes, ClienteId is not null, "clienteId");
        return nomes.Count == 0 ? "nenhum" : string.Join(',', nomes);
    }

    private static void Adicionar(List<string> nomes, bool usado, string nome)
    {
        if (usado)
        {
            nomes.Add(nome);
        }
    }

    private static string Data(DateTimeOffset? data) =>
        data?.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture) ?? "";
}
