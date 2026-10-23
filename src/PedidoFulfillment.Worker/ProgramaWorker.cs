using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Infrastructure.Saude;

namespace PedidoFulfillment.Worker;

/// <summary>
/// Processo dos consumidores e do despachante do outbox (REQ-06, R-10). Só expõe /health e
/// /health/pronto. Main explícito, sem top-level statements: a API já gera um <c>Program</c>
/// global, e dois no projeto de testes seriam ambíguos (achado H1).
/// </summary>
public sealed class ProgramaWorker
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AdicionarInfraestrutura(builder.Configuration);
        builder.Services.AdicionarMensageria();
        builder.Services.AdicionarConsumidor<ReservaEstoque, PedidoCriado>();
        builder.Services.AdicionarGatewayPagamentoSimulado();
        builder.Services.AdicionarConsumidor<ProcessamentoPagamento, EstoqueReservado>();

        var app = builder.Build();
        app.MapearEndpointsSaude();
        app.Run();
    }
}
