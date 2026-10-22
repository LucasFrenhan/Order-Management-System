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

    /// <summary>O estoque dos itens está abatido para este pedido (REQ-08, R-02).</summary>
    public bool EstoqueReservado { get; private set; }

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

    /// <summary>
    /// Tudo ou nada (REQ-08, FR-004 e FR-005): com algum item inativo ou sem estoque suficiente,
    /// devolve as faltas e não altera nada; senão, abate cada quantidade e marca a reserva.
    /// </summary>
    /// <exception cref="InvalidOperationException">Já reservado, ou falta um produto dos itens em <paramref name="produtos"/>.</exception>
    public IReadOnlyList<FaltaEstoque> ReservarEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        if (EstoqueReservado)
        {
            throw new InvalidOperationException($"O estoque do pedido {Numero} já está reservado.");
        }

        var linhas = ProdutosDosItens(produtos);
        var faltas = linhas
            .Where(l => !l.Produto.Ativo || l.Produto.QuantidadeEstoque < l.Item.Quantidade)
            .Select(l => new FaltaEstoque(l.Produto.Id, l.Item.Quantidade, l.Produto.QuantidadeEstoque, !l.Produto.Ativo))
            .ToList();
        if (faltas.Count > 0)
        {
            return faltas;
        }

        foreach (var (item, produto) in linhas)
        {
            produto.AjustarEstoque(-item.Quantidade, instante);
        }

        EstoqueReservado = true;
        AtualizadoEm = instante;
        return [];
    }

    /// <summary>Desfaz a reserva, mesmo com produto inativo (REQ-08, FR-010).</summary>
    /// <exception cref="InvalidOperationException">Sem reserva, ou falta um produto dos itens em <paramref name="produtos"/>.</exception>
    public void DevolverEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        if (!EstoqueReservado)
        {
            throw new InvalidOperationException($"O pedido {Numero} não tem estoque reservado.");
        }

        foreach (var (item, produto) in ProdutosDosItens(produtos))
        {
            produto.AjustarEstoque(item.Quantidade, instante);
        }

        EstoqueReservado = false;
        AtualizadoEm = instante;
    }

    private List<(ItemPedido Item, Produto Produto)> ProdutosDosItens(IReadOnlyDictionary<Guid, Produto> produtos)
    {
        ArgumentNullException.ThrowIfNull(produtos);
        return [.. itens.Select(i => (i, produtos.TryGetValue(i.ProdutoId, out var produto)
            ? produto
            : throw new InvalidOperationException($"O produto {i.ProdutoId} do pedido {Numero} não foi informado.")))];
    }
}
