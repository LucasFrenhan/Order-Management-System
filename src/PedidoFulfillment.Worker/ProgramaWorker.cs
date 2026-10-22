namespace PedidoFulfillment.Worker;

/// <summary>
/// Ponto de entrada do Worker. Main explícito, sem top-level statements: a API já gera um
/// <c>Program</c> global, e dois no projeto de testes seriam ambíguos (REQ-06, achado H1).
/// </summary>
public sealed class ProgramaWorker
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        var app = builder.Build();
        app.MapGet("/health", () => TypedResults.Ok());
        app.Run();
    }
}
