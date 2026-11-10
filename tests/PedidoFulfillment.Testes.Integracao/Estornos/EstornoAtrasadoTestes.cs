using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioAcoesEstornos;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// O <c>pagamento.estorno-atrasado</c> sai uma única vez por estorno, com SQL Server real e o Worker
/// (REQ-28, US3, FR-015 a FR-018, SC-004).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class EstornoAtrasadoTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Alerta depois de 1 s e 2 falhas, para o limite chegar logo.</summary>
    private static readonly Dictionary<string, string?> AlertaRapido = new()
    {
        ["Estornos:LimiteAlertaSegundos"] = "1",
        ["Estornos:TentativasAlerta"] = "2"
    };

    private static Task<int> AtrasadosAsync(string stringConexao) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = N'{TiposEvento.PagamentoEstornoAtrasado}'");

    private static Task<bool> EsperarTentativasAsync(string stringConexao, Guid pedidoId, int tentativas) =>
        MensageriaTeste.EsperarAteAsync(async () => await TentativasAsync(stringConexao, pedidoId) >= tentativas, TimeSpan.FromSeconds(60));

    [Fact]
    public async Task Dez_falhas_com_dois_Workers_geram_um_unico_evento_de_atraso_sem_aviso_ao_Cliente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        var (pedidoId, estornoId) = (estornos[0].PedidoId, estornos[0].EstornoId);
        var gateway = new GatewayEstornoControlado { Modo = GatewayEstornoControlado.ModoEstorno.Falhar };
        var logs = new CapturaLogs();

        await using (var primeiro = WorkerEstornando(mensageria, stringConexao, logs, AlertaRapido, s => s.AddSingleton<IGatewayPagamento>(gateway)))
        await using (var segundo = WorkerEstornando(mensageria, stringConexao, logs, AlertaRapido, s => s.AddSingleton<IGatewayPagamento>(gateway)))
        {
            await Task.WhenAll(Task.Run(() => primeiro.CreateClient().Dispose(), Cancelamento), Task.Run(() => segundo.CreateClient().Dispose(), Cancelamento));
            (await EsperarTentativasAsync(stringConexao, pedidoId, 10)).Should().BeTrue("10 falhas seguidas");
        }

        (await AtrasadosAsync(stringConexao)).Should().Be(1, "SC-004");
        var evento = (await EventosOutbox.ListarAsync<PagamentoEstornoAtrasado>(stringConexao, pedidoId)).Should().ContainSingle().Subject;
        evento.Dados.EstornoId.Should().Be(estornoId);
        evento.Dados.Tentativas.Should().Be(2, "saiu na falha que passou do limite");
        evento.Dados.AtrasadoEm.Should().Be(await BancoDadosTeste.ConsultarAsync<DateTimeOffset>(stringConexao,
            $"SELECT EstornoAtrasadoEm FROM Pagamentos WHERE Id = '{estornoId}'"));
        logs.Registros.Count(r => r.Evento.Id == 7024).Should().Be(1);
        logs.Registros.Count(r => r.Evento.Id == 7008).Should().BeGreaterThanOrEqualTo(8, "o 7008 continua a cada falha");

        // FR-018: o evento de atraso não vira aviso ao Cliente.
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        (await contexto.AvisosPedido.AsNoTracking().AnyAsync(a => a.IdNotificacao == evento.IdEvento, Cancelamento)).Should().BeFalse();

        // US3 cenário 3 e FR-016: com outro limite, a confirmação sai sem novo evento de atraso.
        gateway.Modo = GatewayEstornoControlado.ModoEstorno.Confirmar;
        var outroLimite = new Dictionary<string, string?> { ["Estornos:LimiteAlertaSegundos"] = "2", ["Estornos:TentativasAlerta"] = "1" };
        await using (var terceiro = WorkerEstornando(mensageria, stringConexao, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway), configuracoes: outroLimite))
        {
            terceiro.CreateClient().Dispose();
            (await EsperarSituacaoAsync(stringConexao, pedidoId, "Estornado")).Should().BeTrue();
        }

        (await AtrasadosAsync(stringConexao)).Should().Be(1);
        (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, pedidoId)).Should().ContainSingle();
    }

    [Fact]
    public async Task Estorno_ja_alem_do_limite_antes_da_implantacao_gera_o_evento_na_proxima_falha()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        var (pedidoId, estornoId) = (estornos[0].PedidoId, estornos[0].EstornoId);
        // Como um estorno que já falhou 5 vezes em 2 dias antes do REQ-28: sem EstornoAtrasadoEm.
        await AjustarAsync(stringConexao, estornoId,
            "TentativasEstorno = 5, EstornoSolicitadoEm = DATEADD(DAY, -2, SYSDATETIMEOFFSET()), UltimaFalhaEstornoEm = DATEADD(HOUR, -1, SYSDATETIMEOFFSET())");
        var gateway = new GatewayEstornoControlado { Modo = GatewayEstornoControlado.ModoEstorno.Recusar };

        await using (var worker = WorkerEstornando(mensageria, stringConexao, servicos: s => s.AddSingleton<IGatewayPagamento>(gateway)))
        {
            worker.CreateClient().Dispose();
            (await EsperarTentativasAsync(stringConexao, pedidoId, 8)).Should().BeTrue();
        }

        (await AtrasadosAsync(stringConexao)).Should().Be(1, "US3 cenário 5");
        (await EventosOutbox.ListarAsync<PagamentoEstornoAtrasado>(stringConexao, pedidoId)).Single().Dados.Tentativas.Should().Be(6);
    }
}
