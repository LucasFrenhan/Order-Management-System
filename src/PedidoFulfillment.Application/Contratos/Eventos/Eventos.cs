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

/// <summary>
/// <c>entrega.criada</c>: publicado pelo worker de entrega (issue #10) quando a ordem de entrega do
/// pedido pago foi criada; o pedido está pronto para separação e despacho (REQ-10, FR-008).
/// </summary>
public sealed record EntregaCriada
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required Guid OrdemEntregaId { get; init; }

    public required IReadOnlyList<ItemEntregaCriada> Itens { get; init; }
}

public sealed record ItemEntregaCriada
{
    public required Guid ProdutoId { get; init; }

    public required int Quantidade { get; init; }
}

/// <summary>
/// Categoria do cancelamento no <c>pedido.cancelado</c> (REQ-15, R-04). Faz parte do contrato:
/// acrescentar um valor muda o contrato e exige decisão (Princípio II).
/// </summary>
public enum MotivoCancelamento
{
    /// <summary>O dono do pedido, sem perfil de operação, cancelou pela API.</summary>
    Cliente = 1,

    /// <summary>Admin ou OperadorLogistica cancelou pela API.</summary>
    Operacao,

    /// <summary>O consumidor de estoque não conseguiu reservar; sai junto com <c>estoque.insuficiente</c>.</summary>
    EstoqueInsuficiente,

    /// <summary>A reconciliação desistiu do pedido; a etapa vai em <see cref="PedidoCancelado.EtapaEsgotada"/>.</summary>
    ReconciliacaoEsgotada
}

/// <summary>
/// Etapa em que a reconciliação desistiu do pedido (REQ-15, R-04). Espelha as etapas internas,
/// mas é do contrato: acrescentar um valor muda o contrato e exige decisão (Princípio II).
/// </summary>
public enum EtapaEsgotada
{
    Estoque = 1,
    Pagamento,
    Entrega
}

/// <summary>
/// <c>pedido.cancelado</c>: publicado em toda chegada de um pedido a Cancelado, por qualquer
/// caminho, na mesma gravação do cancelamento (REQ-15, FR-001 e FR-002). Não sai para o pedido em
/// PagamentoRejeitado. Sem valor, itens, cliente, autor ou o texto do motivo (FR-010).
/// </summary>
public sealed record PedidoCancelado
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required MotivoCancelamento Motivo { get; init; }

    /// <summary>Preenchida quando o motivo é <see cref="MotivoCancelamento.ReconciliacaoEsgotada"/>; nula nos demais, mas sempre enviada.</summary>
    public required EtapaEsgotada? EtapaEsgotada { get; init; }

    /// <summary>O instante gravado no histórico de status.</summary>
    public required DateTimeOffset CanceladoEm { get; init; }

    public required bool EstoqueDevolvido { get; init; }

    public required bool EstornoPendente { get; init; }

    public required bool OrdemEntregaCancelada { get; init; }
}

/// <summary>
/// <c>pagamento.estornado</c>: publicado quando o processo de workers confirma o estorno no
/// gateway, na mesma gravação da confirmação, uma vez por pedido (REQ-15, FR-006 e FR-007). Os
/// identificadores do gateway vão no evento, como no <c>pagamento.aprovado</c>, e ficam fora dos logs.
/// </summary>
public sealed record PagamentoEstornado
{
    public required Guid PedidoId { get; init; }

    public required string Numero { get; init; }

    public required decimal Valor { get; init; }

    public required string IdentificadorTransacao { get; init; }

    public required string IdentificadorEstorno { get; init; }

    public required DateTimeOffset EstornadoEm { get; init; }
}
