using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Infrastructure.Persistencia;

/// <summary>
/// Guarda da base de consumo (REQ-18, R-14): uma mudança de status ainda pendente no contexto, na
/// hora do registro de processado, não passou pelo <see cref="ArmazenamentoPedidos.SalvarAsync"/> e
/// sairia sem o <c>pedido.status-alterado</c> e sem o aviso.
/// </summary>
internal static class GuardaMudancaStatus
{
    /// <exception cref="InvalidOperationException">
    /// Há entrada nova no histórico de status. É erro de programação: nada é gravado, e a mensagem
    /// segue para as novas tentativas e a DLQ.
    /// </exception>
    public static void ExigirGravadaPeloArmazenamento(ContextoBancoDados contexto)
    {
        if (contexto.ChangeTracker.Entries<HistoricoStatusPedido>().Any(e => e.State == EntityState.Added))
        {
            throw new InvalidOperationException("Mudança de status gravada fora do ArmazenamentoPedidos.SalvarAsync (REQ-18).");
        }
    }
}
