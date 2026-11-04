using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>
/// Sem o armazenamento, a requisição segue sem o limite (REQ-23, FR-012, SC-010, R-04). Com um limite de
/// 1, o segundo 401 seguido prova que o limite não estava valendo.
/// </summary>
[Collection(nameof(ColecaoMetricasLimite))]
public sealed class ArmazenamentoIndisponivelTestes
{
    private const string MetricaFalhas = "oms.limite_requisicoes.falhas_armazenamento";

    private static Dictionary<string, string?> Configuracao(string pausa = "60", string tempoMaximo = "100")
    {
        var valores = CenarioLimite.Limites(loginOrigemEmail: 1);
        valores["LimiteRequisicoes:PausaAposFalhaSegundos"] = pausa;
        valores["LimiteRequisicoes:TempoMaximoEsperaMs"] = tempoMaximo;
        return valores;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task As_rotas_respondem_normalmente_com_um_log_por_pausa_e_a_metrica_por_requisicao(bool demora)
    {
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        var estado = new ArmazenamentoControlado { Falhando = !demora, Demorando = demora };
        await using var api = CenarioLimite.SemBanco(Configuracao(), armazenamento: estado, logs: logs, telemetria: telemetria);
        var cliente = api.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            var resposta = await CenarioLimite.LoginAsync(cliente);
            resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
            (await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken)).Should().NotContain("segredo");
        }

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3008).Which.Texto.Should().NotContain("segredo");
        telemetria.Soma(MetricaFalhas).Should().Be(3);
        estado.Chamadas.Should().Be(1, "durante a pausa, a instância não tenta o armazenamento");
    }

    [Fact]
    public async Task Depois_da_pausa_o_limite_volta()
    {
        var estado = new ArmazenamentoControlado { Falhando = true };
        await using var api = CenarioLimite.SemBanco(Configuracao(pausa: "1"), armazenamento: estado);
        var cliente = api.CreateClient();
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        estado.Falhando = false;
        await Task.Delay(TimeSpan.FromSeconds(1.1), TestContext.Current.CancellationToken);

        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Com_o_banco_real_fora_na_partida_a_api_sobe_e_responde_sem_o_limite()
    {
        var logs = new CapturaLogs();
        await using var api = new FabricaApi
        {
            // A porta fechada de sempre, com o armazenamento real: aquecimento e contagem falham de verdade.
            StringConexao = FabricaApi.StringConexaoTeste,
            ServicoAutenticacao = new ServicoAutenticacaoFalso
            {
                ResultadoLogin = PedidoFulfillment.Application.Comum.Resultado<PedidoFulfillment.Application.Contratos.Autenticacao.LoginResponseDTO>
                    .Falhou(new PedidoFulfillment.Application.Comum.Falha(PedidoFulfillment.Application.Comum.TipoFalha.NaoAutenticado, "Credenciais inválidas."))
            },
            Configuracoes = Configuracao(),
            EnderecoConexao = CenarioLimite.Origem,
            Logs = logs
        };
        var cliente = api.CreateClient();

        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3008);
    }

    private sealed class ArmazenamentoControlado : IArmazenamentoContagens
    {
        private readonly ArmazenamentoContagensMemoria memoria = new();

        public bool Falhando { get; set; }

        public bool Demorando { get; set; }

        public int Chamadas { get; private set; }

        public async Task<IReadOnlyList<RecusaContagem>> ContarAsync(IReadOnlyList<ContagemSolicitada> contagens, CancellationToken cancellationToken)
        {
            Chamadas++;
            if (Demorando)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }

            return Falhando
                ? throw new InvalidOperationException("Server=segredo;Password=segredo")
                : await memoria.ContarAsync(contagens, cancellationToken);
        }
    }
}
