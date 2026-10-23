namespace PedidoFulfillment.Application.Contratos.Eventos;

// Conteúdo dos eventos v1 (REQ-06, FR-002). Mudanças seguem o Princípio II: na mesma versão,
// só campos opcionais novos.

/// <summary><c>pedido.criado</c>: publicado pela criação de pedido, via outbox.</summary>
public sealed record PedidoCriado
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required Guid ClienteId { get; init; }

    public required IReadOnlyList<ItemPedidoCriado> Itens { get; init; }

    public required decimal ValorTotal { get; init; }

    public required string Moeda { get; init; }
}

public sealed record ItemPedidoCriado
{
    public required Guid ProdutoId { get; init; }

    public required int Quantidade { get; init; }

    public required decimal PrecoUnitario { get; init; }
}

/// <summary><c>estoque.reservado</c>: publicado pelo worker de estoque (issue #8).</summary>
public sealed record EstoqueReservado
{
    public required Guid PedidoId { get; init; }

    public required IReadOnlyList<ItemEstoqueReservado> Itens { get; init; }
}

public sealed record ItemEstoqueReservado
{
    public required Guid ProdutoId { get; init; }

    public required int Quantidade { get; init; }
}

/// <summary>
/// <c>estoque.insuficiente</c>: publicado pelo worker de estoque quando algum item faltou; o
/// pedido foi cancelado e nenhum estoque mudou (REQ-08, FR-012).
/// </summary>
public sealed record EstoqueInsuficiente
{
    public required Guid PedidoId { get; init; }

    /// <summary>Só os itens que faltaram.</summary>
    public required IReadOnlyList<ItemEstoqueInsuficiente> Itens { get; init; }
}

public sealed record ItemEstoqueInsuficiente
{
    public required Guid ProdutoId { get; init; }

    public required int QuantidadeSolicitada { get; init; }

    /// <summary>Estoque lido no momento da reserva.</summary>
    public required int QuantidadeDisponivel { get; init; }

    public required bool Inativo { get; init; }
}

/// <summary><c>pagamento.aprovado</c>: publicado pelo worker de pagamento (issue #9).</summary>
public sealed record PagamentoAprovado
{
    public required Guid PedidoId { get; init; }

    public required decimal Valor { get; init; }

    public required string IdentificadorTransacao { get; init; }
}

/// <summary><c>pagamento.recusado</c>: publicado pelo worker de pagamento (issue #9); dispara a compensação.</summary>
public sealed record PagamentoRecusado
{
    public required Guid PedidoId { get; init; }

    public required decimal Valor { get; init; }

    public required string Motivo { get; init; }
}
