using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Administracao;

namespace PedidoFulfillment.Application.Mensageria;

/// <summary>Registro e consulta dos pedidos de reenvio da DLQ pela API (REQ-14, R-03). Nunca fala com o broker.</summary>
public interface IServicoReenvioDlq
{
    Task<Resultado<PedidoReenvioDlqResponseDTO>> RegistrarAsync(Guid autorId, string consumidor, RegistrarReenvioDlqRequestDTO filtro, CancellationToken cancellationToken);

    Task<Resultado<PedidoReenvioDlqResponseDTO>> ObterAsync(Guid id, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed class ServicoReenvioDlq(IArmazenamentoReenvios armazenamento, TimeProvider relogio, ILogger<ServicoReenvioDlq> logger) : IServicoReenvioDlq
{
    private static readonly Falha ConsumidorDesconhecido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.", $"O consumidor deve ser {string.Join(", ", ConsumidoresConhecidos.Nomes.SkipLast(1))} ou {ConsumidoresConhecidos.Nomes[^1]}.");

    private static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Pedido de reenvio não encontrado.", "Não existe pedido de reenvio com este identificador.");

    public async Task<Resultado<PedidoReenvioDlqResponseDTO>> RegistrarAsync(
        Guid autorId, string consumidor, RegistrarReenvioDlqRequestDTO filtro, CancellationToken cancellationToken)
    {
        if (!ConsumidoresConhecidos.Conhecido(consumidor))
        {
            return ConsumidorDesconhecido;
        }

        var pedido = PedidoReenvioDlq.Registrar(autorId, consumidor, filtro.ParaFiltro(), relogio.GetUtcNow());
        armazenamento.Adicionar(pedido);
        await armazenamento.SalvarAsync(cancellationToken);
        EventosReenvio.PedidoReenvioRegistrado(logger, pedido.Id, autorId, consumidor);
        return PedidoReenvioDlqResponseDTO.De(pedido);
    }

    public async Task<Resultado<PedidoReenvioDlqResponseDTO>> ObterAsync(Guid id, CancellationToken cancellationToken) =>
        await armazenamento.ObterAsync(id, cancellationToken) is { } pedido ? PedidoReenvioDlqResponseDTO.De(pedido) : NaoEncontrado;
}

internal static partial class EventosReenvio
{
    [LoggerMessage(EventId = 4006, EventName = "PedidoReenvioRegistrado", Level = LogLevel.Information,
        Message = "Pedido de reenvio {PedidoReenvioId} registrado por {AutorId} para a DLQ de {Consumidor}.")]
    public static partial void PedidoReenvioRegistrado(ILogger logger, Guid pedidoReenvioId, Guid autorId, string consumidor);
}
