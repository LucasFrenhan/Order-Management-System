using Microsoft.Extensions.Options;
using PedidoFulfillment.PagamentosSimulados.Operacoes;
using PedidoFulfillment.PagamentosSimulados.Seguranca;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.PagamentosSimulados;

/// <summary>
/// API de pagamento simulada (REQ-19, research R-01 e R-02): um serviço externo do OMS, que o
/// Worker chama pela rede para cobrar, estornar e consultar pela chave do pedido. Guarda tudo em
/// memória e não exporta telemetria; só leva o <c>traceparent</c> recebido para o escopo do log
/// (FR-020). Main explícito, como a API e o Worker, para os testes subirem as três aplicações.
/// </summary>
public sealed class ProgramaPagamentos
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<IValidateOptions<OpcoesSeguranca>, ValidadorOpcoesSeguranca>();
        builder.Services.AddOptions<OpcoesSeguranca>().BindConfiguration(OpcoesSeguranca.Secao).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<OpcoesSimulacao>, ValidadorOpcoesSimulacao>();
        builder.Services.AddOptions<OpcoesSimulacao>().BindConfiguration(OpcoesSimulacao.Secao).ValidateOnStart();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ArmazenamentoOperacoes>();
        builder.Services.AddSingleton<ControleSimulacao>();
        builder.Services.AddSingleton<ServicoPagamentosSimulados>();

        var app = builder.Build();
        app.Use(async (contexto, proximo) =>
        {
            // Só o contexto do trace recebido, no escopo dos logs (FR-020): sem spans próprios.
            var logger = contexto.RequestServices.GetRequiredService<ILogger<ProgramaPagamentos>>();
            using var escopo = contexto.Request.Headers.TryGetValue("traceparent", out var traceparent)
                ? logger.BeginScope(new Dictionary<string, object?> { ["traceparent"] = traceparent.ToString() })
                : null;
            await proximo(contexto);
        });
        app.UseMiddleware<AutenticacaoChaves>();

        app.MapGet("/health", () => Results.Ok(new { situacao = "saudavel" }));

        var v1 = app.MapGroup("/v1");
        v1.MapPost("/cobrancas", (HttpContext contexto, CorpoCobranca? corpo, ServicoPagamentosSimulados servico) =>
            servico.CobrarAsync(contexto, corpo));
        v1.MapGet("/cobrancas/{chave}", (HttpContext contexto, string chave, ServicoPagamentosSimulados servico) =>
            servico.ConsultarAsync(contexto, TipoOperacao.Cobranca, chave));
        v1.MapPost("/estornos", (HttpContext contexto, CorpoEstorno? corpo, ServicoPagamentosSimulados servico) =>
            servico.EstornarAsync(contexto, corpo));
        v1.MapGet("/estornos/{chave}", (HttpContext contexto, string chave, ServicoPagamentosSimulados servico) =>
            servico.ConsultarAsync(contexto, TipoOperacao.Estorno, chave));

        app.MapearControle();
        app.Run();
    }
}
