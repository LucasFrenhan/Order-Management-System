using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Observabilidade;

/// <summary>
/// Grava o contexto de rastreamento ativo nas linhas novas de <c>EventosSaida</c>,
/// <c>Pedidos</c> e <c>PedidosReenvioDlq</c> (REQ-16, R-03, data-model). Só nas linhas
/// <c>Added</c>: o contexto é o da criação e nunca é trocado. Assim nenhuma fábrica do Domain
/// ou do Application precisa conhecer rastreamento, e nenhum ponto que grava evento muda.
/// </summary>
internal sealed class InterceptorContextoRastreio : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Preencher(eventData.Context);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Preencher(eventData.Context);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void Preencher(DbContext? contexto)
    {
        // Sem activity (telemetria desligada), nada a gravar: a coluna fica nula, como antes.
        if (contexto is null || ContextoRastreio.Atual() is not { } atual)
        {
            return;
        }

        foreach (var entrada in contexto.ChangeTracker.Entries())
        {
            if (entrada.State == EntityState.Added && entrada.Entity is EventoSaida or Pedido or PedidoReenvioDlq)
            {
                entrada.Property(ContextoBancoDados.ContextoRastreio).CurrentValue = atual;
            }
        }
    }
}
