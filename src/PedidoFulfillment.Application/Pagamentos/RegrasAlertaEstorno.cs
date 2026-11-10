namespace PedidoFulfillment.Application.Pagamentos;

/// <summary>Quando a falha de um estorno vira erro para a operação (REQ-13, FR-003).</summary>
public sealed record ParametrosAlertaEstorno(TimeSpan LimiteAlerta, int TentativasAlerta);

/// <summary>
/// Regra única do alerta do estorno (REQ-28, R-09): a mesma para o log 7008, o evento
/// <c>pagamento.estorno-atrasado</c>, a listagem da operação e o medidor.
/// </summary>
public static class RegrasAlertaEstorno
{
    /// <summary>Pendente há mais que o limite e com pelo menos as tentativas de alerta.</summary>
    public static bool AlemDoLimite(DateTimeOffset solicitadoEm, int tentativas, DateTimeOffset agora, ParametrosAlertaEstorno parametros) =>
        agora - solicitadoEm > parametros.LimiteAlerta && tentativas >= parametros.TentativasAlerta;
}
