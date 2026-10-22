using PedidoFulfillment.Domain.Comum;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Compra de um cliente (data-model). Protege só a própria consistência: total, snapshot dos
/// itens e histórico gravado junto com a mudança de status (FR-023). As transições válidas
/// são regra do REQ-05 (research R-02).
/// </summary>
public sealed class Pedido
{
    public const int TamanhoNumero = 19;
    public const string MoedaPadrao = "BRL";

    private readonly List<ItemPedido> itens = [];
    private readonly List<HistoricoStatusPedido> historico = [];

    public Pedido(Guid clienteId, string numero, IEnumerable<(Produto Produto, int Quantidade)> itensPedido, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(itensPedido);
        var linhas = itensPedido.ToList();
        if (linhas.Count == 0)
        {
            throw new ArgumentException("O pedido precisa de ao menos um item.", nameof(itensPedido));
        }

        if (linhas.DistinctBy(l => l.Produto.Id).Count() != linhas.Count)
        {
            throw new ArgumentException("O mesmo produto não pode aparecer duas vezes no pedido.", nameof(itensPedido));
        }

        var numeroAparado = Guardas.Texto(numero, TamanhoNumero);
        if (numeroAparado.Length != TamanhoNumero)
        {
            throw new ArgumentException($"O número do pedido deve ter {TamanhoNumero} caracteres.", nameof(numero));
        }

        Id = Guid.CreateVersion7();
        ClienteId = Guardas.Identificador(clienteId);
        Numero = numeroAparado;
        Moeda = MoedaPadrao;
        Status = StatusPedido.Pendente;
        CriadoEm = Guardas.Utc(instante);
        AtualizadoEm = CriadoEm;
        itens.AddRange(linhas.Select(l => new ItemPedido(Id, l.Produto, l.Quantidade)));
        ValorTotal = itens.Sum(i => i.Subtotal);
        historico.Add(new HistoricoStatusPedido(Id, null, Status, null, null, instante));
    }

    private Pedido()
    {
        Numero = "";
        Moeda = MoedaPadrao;
    }

    public Guid Id { get; private set; }

    /// <summary>Padrão <c>PED-AAAAMMDD-NNNNNN</c>, gerado pelo REQ-05.</summary>
    public string Numero { get; private set; }

    public Guid ClienteId { get; private set; }

    public StatusPedido Status { get; private set; }

    public decimal ValorTotal { get; private set; }

    public string Moeda { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public IReadOnlyCollection<ItemPedido> Itens => itens;

    /// <summary>Mudanças de status, na ordem em que aconteceram.</summary>
    public IReadOnlyCollection<HistoricoStatusPedido> Historico => historico;

    public Pagamento? Pagamento { get; private set; }

    public OrdemEntrega? OrdemEntrega { get; private set; }

    /// <summary>
    /// Muda o status e acrescenta a entrada no histórico, para gravar os dois juntos. Não valida
    /// a transição (REQ-05).
    /// </summary>
    public void MudarStatus(StatusPedido novo, Guid? autorId, string? motivo, DateTimeOffset instante)
    {
        if (!Enum.IsDefined(novo))
        {
            throw new ArgumentOutOfRangeException(nameof(novo), novo, "Status desconhecido.");
        }

        var entrada = new HistoricoStatusPedido(Id, Status, novo, autorId, motivo, instante);
        historico.Add(entrada);
        Status = novo;
        AtualizadoEm = instante;
    }
}
