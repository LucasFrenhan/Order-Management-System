namespace PedidoFulfillment.Application.Mensageria;

/// <summary>Pedidos de reenvio da DLQ no banco (REQ-14, R-03): a API registra e consulta, o Worker executa.</summary>
public interface IArmazenamentoReenvios
{
    void Adicionar(PedidoReenvioDlq pedido);

    Task<PedidoReenvioDlq?> ObterAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Os pedidos a executar, para alteração: primeiro os em execução (de uma execução que caiu),
    /// depois os pendentes, os dois por ordem de registro.
    /// </summary>
    Task<IReadOnlyList<PedidoReenvioDlq>> ListarParaExecucaoAsync(int quantidade, CancellationToken cancellationToken);

    Task SalvarAsync(CancellationToken cancellationToken);
}
