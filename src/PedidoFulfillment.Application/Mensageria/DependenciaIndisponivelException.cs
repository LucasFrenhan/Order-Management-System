namespace PedidoFulfillment.Application.Mensageria;

/// <summary>
/// Uma dependência externa está fora do ar e não deve ser chamada até <see cref="DisponivelEm"/>
/// (REQ-19, FR-016a, research R-09). A base de consumo devolve a mensagem à fila sem contar como
/// nova tentativa e pausa o consumo até lá, em vez de levar a mensagem às novas tentativas e à DLQ.
/// </summary>
public class DependenciaIndisponivelException(string message, DateTimeOffset disponivelEm, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Quando a dependência pode ser testada de novo.</summary>
    public DateTimeOffset DisponivelEm { get; } = disponivelEm;
}
