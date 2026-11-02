using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Worker;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// O Worker em memória (REQ-06, T016), com despacho e novas tentativas curtos para os testes.
/// </summary>
public sealed class FabricaWorker : WebApplicationFactory<ProgramaWorker>
{
    public required string StringConexao { get; init; }

    public required IReadOnlyDictionary<string, string?> ConfiguracoesBroker { get; init; }

    /// <summary>Registros extras, por exemplo manipuladores de verificação.</summary>
    public Action<IServiceCollection>? Servicos { get; init; }

    public IReadOnlyDictionary<string, string?> Configuracoes { get; init; } = new Dictionary<string, string?>();

    /// <summary>Liga a telemetria (REQ-16) e coleta spans, logs e métricas em memória.</summary>
    public ColetorTelemetria? Telemetria { get; init; }

    /// <summary>Recebe a exceção original de uma falha de partida (ver <see cref="Infraestrutura.FalhaPartida"/>).</summary>
    public FalhaPartida FalhaPartida { get; } = new();

    /// <summary>
    /// A API de pagamento simulada real, para os testes do REQ-19 (research R-13): o Worker usa o
    /// gateway por HTTP, com os tempos curtos dos testes. Sem ela, o <see cref="GatewayPagamentoMemoria"/>.
    /// </summary>
    public FabricaPagamentos? ApiPagamentos { get; init; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testes");
        builder.UseSetting("ConnectionStrings:SqlServer", StringConexao);
        builder.UseSetting("Banco:PrepararNaInicializacao", "false");
        builder.UseSetting("Mensageria:IntervaloDespachoMs", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:0", "100");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:1", "200");
        builder.UseSetting("Mensageria:IntervalosNovaTentativaMs:2", "400");
        // Gateway de pagamento (REQ-19, R-11 e R-13): por HTTP com a API simulada real e tempos
        // curtos quando o teste a passa; senão, o endereço e a chave só passam pela validação de
        // partida, e o gateway em memória toma o lugar (registrado abaixo).
        builder.UseSetting("GatewayPagamento:UrlBase", ApiPagamentos?.UrlBase ?? "http://127.0.0.1:9");
        builder.UseSetting("GatewayPagamento:ChaveApi", ApiPagamentos?.ChaveApi ?? FabricaPagamentos.ChaveApiTeste);
        builder.UseSetting("GatewayPagamento:TempoTentativaMs", "300");
        builder.UseSetting("GatewayPagamento:Tentativas", "3");
        builder.UseSetting("GatewayPagamento:EsperaBaseMs", "20");
        builder.UseSetting("GatewayPagamento:TempoTotalMs", "1500");
        builder.UseSetting("GatewayPagamento:Circuito:MinimoChamadas", "4");
        builder.UseSetting("GatewayPagamento:Circuito:JanelaSegundos", "5");
        builder.UseSetting("GatewayPagamento:Circuito:PausaSegundos", "1");
        // Muitos testes semeiam pedidos com datas fixas, que a reconciliação veria como parados
        // (REQ-12, R-10): desligada, salvo nos testes dela, que a ligam em Configuracoes.
        builder.UseSetting("Reconciliacao:Habilitada", "false");
        // O mesmo para o processador de estornos (REQ-13, R-09): só os testes dele o ligam.
        builder.UseSetting("Estornos:Habilitado", "false");
        // Limpeza, reenvio e aviso da DLQ (REQ-14, R-10): só os testes deles os ligam.
        builder.UseSetting("Limpeza:Habilitada", "false");
        builder.UseSetting("ReenvioDlq:Habilitado", "false");
        builder.UseSetting("ReenvioDlq:AvisoHabilitado", "false");
        // Webhooks (REQ-18): a chave da API, a rede local para o receptor de teste e o envio
        // desligado, salvo nos testes dele.
        builder.UseSetting("Notificacoes:ChaveCifra", FabricaApi.ChaveCifraTeste);
        builder.UseSetting("Notificacoes:PermitirRedeLocal", "true");
        builder.UseSetting("Notificacoes:Envio:Habilitado", "false");
        if (Telemetria is not null)
        {
            builder.UseSetting("Observabilidade:Habilitada", "true");
        }

        foreach (var (chave, valor) in ConfiguracoesBroker.Concat(Configuracoes))
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(FalhaPartida));
        if (ApiPagamentos is null)
        {
            // Antes dos registros do teste, para um gateway falso do teste prevalecer. Nenhum teste
            // depende de sorte (REQ-09, R-09): aprova tudo, salvo se o teste configurar outra taxa.
            builder.ConfigureTestServices(servicos => servicos.AddSingleton<IGatewayPagamento>(provedor =>
                new GatewayPagamentoMemoria(provedor.GetRequiredService<IConfiguration>().GetValue(GatewayPagamentoMemoria.ChaveTaxa, 100d))));
        }

        if (Servicos is not null)
        {
            builder.ConfigureTestServices(Servicos);
        }

        if (Telemetria is not null)
        {
            builder.ConfigureTestServices(Telemetria.RegistrarEm);
        }
    }
}
