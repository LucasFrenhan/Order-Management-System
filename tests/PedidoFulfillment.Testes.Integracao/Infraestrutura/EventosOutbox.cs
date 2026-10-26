using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>Eventos gravados no outbox para um pedido, desserializados (REQ-15).</summary>
public static class EventosOutbox
{
    public static async Task<IReadOnlyList<EnvelopeEvento<T>>> ListarAsync<T>(string stringConexao, Guid pedidoId)
    {
        var tipo = TiposEvento.TipoDe<T>();
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var conteudos = await contexto.EventosSaida.AsNoTracking()
            .Where(e => e.Tipo == tipo)
            .OrderBy(e => e.Id)
            .Select(e => e.Conteudo)
            .ToListAsync(TestContext.Current.CancellationToken);
        return [.. conteudos.Select(SerializacaoEventos.Desserializar<T>).Where(e => e.IdCorrelacao == pedidoId)];
    }

    /// <summary>O único <c>pedido.cancelado</c> do pedido.</summary>
    public static async Task<PedidoCancelado> CanceladoAsync(string stringConexao, Guid pedidoId)
    {
        var eventos = await ListarAsync<PedidoCancelado>(stringConexao, pedidoId);
        if (eventos.Count != 1)
        {
            throw new InvalidOperationException($"Esperado 1 pedido.cancelado do pedido {pedidoId}, encontrados {eventos.Count}.");
        }

        return eventos[0].Dados;
    }
}
