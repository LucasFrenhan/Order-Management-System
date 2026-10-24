using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Pagamentos;
using PedidoFulfillment.Infrastructure.Reconciliacao;
using PedidoFulfillment.Infrastructure.Saude;

namespace PedidoFulfillment.Worker;

/// <summary>
/// Processo dos consumidores, do despachante do outbox (REQ-06, R-10) e da reconciliação de
/// pedidos parados (REQ-12). Só expõe /health e /health/pronto. Main explícito, sem top-level
/// statements: a API já gera um <c>Program</c> global, e dois no projeto de testes seriam
/// ambíguos (achado H1).
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
        builder.Services.AdicionarConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>();
        builder.Services.AdicionarReconciliacao();

        var app = builder.Build();
        app.MapearEndpointsSaude();
        app.Run();
    }
}
