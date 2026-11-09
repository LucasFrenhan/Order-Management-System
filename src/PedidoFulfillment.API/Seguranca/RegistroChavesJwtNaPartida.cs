namespace PedidoFulfillment.API.Seguranca;

/// <summary>
/// Registra na partida qual chave a instância usa para emitir e quais só aceita (REQ-27, FR-015, log 3026). Durante
/// uma rotação, mostra se cada instância já recebeu a configuração da etapa.
/// </summary>
public sealed class RegistroChavesJwtNaPartida(ConjuntoChavesJwt chaves, ILogger<RegistroChavesJwtNaPartida> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var aceitas = chaves.IdentificadoresAceitos.Count == 0 ? "-" : string.Join(", ", chaves.IdentificadoresAceitos);
        EventosChavesJwt.ChavesJwtCarregadas(logger, chaves.Ativa.KeyId, aceitas);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
