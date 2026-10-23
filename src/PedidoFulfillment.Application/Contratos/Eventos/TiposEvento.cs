namespace PedidoFulfillment.Application.Contratos.Eventos;

/// <summary>
/// Catálogo dos eventos: nome no passado (chave de roteamento), versão e tipo do conteúdo
/// (REQ-06, FR-001; REQ-08, FR-012; REQ-10, FR-008; constituição: eventos no passado).
/// </summary>
public static class TiposEvento
{
    public const string PedidoCriado = "pedido.criado";
    public const string EstoqueReservado = "estoque.reservado";
    public const string EstoqueInsuficiente = "estoque.insuficiente";
    public const string PagamentoAprovado = "pagamento.aprovado";
    public const string PagamentoRecusado = "pagamento.recusado";
    public const string EntregaCriada = "entrega.criada";

    public const int VersaoAtual = 1;

    public static readonly IReadOnlyDictionary<string, Type> TipoDados = new Dictionary<string, Type>
    {
        [PedidoCriado] = typeof(Eventos.PedidoCriado),
        [EstoqueReservado] = typeof(Eventos.EstoqueReservado),
        [EstoqueInsuficiente] = typeof(Eventos.EstoqueInsuficiente),
        [PagamentoAprovado] = typeof(Eventos.PagamentoAprovado),
        [PagamentoRecusado] = typeof(Eventos.PagamentoRecusado),
        [EntregaCriada] = typeof(Eventos.EntregaCriada)
    };

    public static string TipoDe<T>() =>
        TipoDados.Single(par => par.Value == typeof(T)).Key;
}
