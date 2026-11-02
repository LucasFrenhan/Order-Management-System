using System.Diagnostics;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pagamentos.CenarioPagamento;
using static PedidoFulfillment.Testes.Integracao.PagamentosHttp.CenarioPagamentoHttp;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.PagamentosHttp;

/// <summary>
/// Classificação e novas tentativas da cobrança pela rede (REQ-19, US3, T029; FR-010 a FR-014,
/// FR-019 e SC-003), com a API simulada real, banco e broker.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class NovasTentativasHttpTestes(MensageriaTeste mensageria)
{
    private async Task<(string StringConexao, Guid PedidoId, CapturaLogs Logs)> CobrarComAsync(FabricaPagamentos pagamentos, params string[] roteiro)
    {
        var stringConexao = mensageria.NovoBanco();
        if (roteiro.Length > 0)
        {
            await pagamentos.RoteiroAsync(roteiro);
        }

        var api = Api(stringConexao);
        await using (api)
        {
            var (cliente, _, _) = await ClienteAsync(api);
            var logs = new CapturaLogs();
            await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);
            var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
            return (stringConexao, pedidoId, logs);
        }
    }

    [Theory]
    [InlineData("indisponivel")]
    [InlineData("limite-taxa")]
    [InlineData("erro-interno")]
    public async Task Falha_passageira_isolada_e_resolvida_na_mesma_chamada_sem_a_nova_tentativa_da_base(string falha)
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();

        var (_, pedidoId, logs) = await CobrarComAsync(pagamentos, falha, "aprovar");

        (await CobrancaAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 1),
            "a chamada com falha não guarda nada; a segunda, com a mesma chave, aprova");
        (await pagamentos.ChamadasRecebidasAsync()).Should().Be(2);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 7013).Which.Texto.Should().Contain(falha);
        Contar(logs, 5003).Should().Be(0, "SC-003: a falha isolada não chega à nova tentativa da base");
    }

    [Fact]
    public async Task Limite_de_taxa_espera_o_Retry_After_pedido_pela_API()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.AlterarComportamentoAsync(new { retryAfterSegundos = 1 });
        // O tempo total dos testes (1,5 s) comporta a espera de 1 s pedida pela API.
        var (_, pedidoId, logs) = await CobrarComAsync(pagamentos, "limite-taxa", "aprovar");

        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
        Contar(logs, 5003).Should().Be(0);
    }

    [Fact]
    public async Task Latencia_alta_dentro_do_limite_nao_e_repetida()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.AlterarComportamentoAsync(new { latenciaMinimaMs = 200, latenciaMaximaMs = 250 });

        var (_, pedidoId, logs) = await CobrarComAsync(pagamentos);

        (await CobrancaAsync(pagamentos, pedidoId)).Should().Be(new PagamentosSimulados.Simulacao.RespostaOperacaoControle("aprovada", 1, 1));
        Contar(logs, 7013).Should().Be(0);
        Contar(logs, 7014).Should().Be(0);
    }

    [Fact]
    public async Task Esgotadas_as_tentativas_nada_e_gravado_e_a_base_tenta_de_novo_com_a_mesma_chave()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        using var pagamentos = FabricaPagamentos.Iniciar();

        var (stringConexao, pedidoId, logs) = await CobrarComAsync(pagamentos, "erro-interno", "erro-interno", "erro-interno", "aprovar");

        Contar(logs, 5003).Should().Be(1, "FR-011: esgotada a chamada, segue a nova tentativa da base (REQ-06)");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(1);
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }

    [Fact]
    public async Task Chave_reutilizada_e_erro_permanente_sem_repeticao_e_sem_cobrar()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        // Com o Worker parado, o pedido espera no outbox; a chave dele é usada antes com outro valor.
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        using (var direto = pagamentos.ClienteApi())
        {
            await ApiPagamentosSimuladaTestes.CobrarAsync(direto, pedidoId, 0.01m);
        }

        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        (await EsperarLogAsync(logs, 7015)).Should().BeTrue();
        logs.Registros.First(r => r.Evento.Id == 7015).Texto.Should().Contain("chave-reutilizada");
        (await EsperarLogAsync(logs, 5004)).Should().BeTrue("erro permanente segue as novas tentativas da base e a DLQ");
        Contar(logs, 7013).Should().Be(0, "erro permanente não é repetido dentro da chamada");
        (await PagamentosAsync(stringConexao, pedidoId)).Should().Be(0);
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1, "só a cobrança de antes, sem segunda");
    }

    [Fact]
    public async Task Chamada_sem_resposta_libera_a_mensagem_no_tempo_total_e_nao_prende_o_pedido()
    {
        // FR-019 e R-10: a chamada fica dentro da transação, limitada pelo tempo total (1,5 s nos testes).
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        using var pagamentos = FabricaPagamentos.Iniciar();
        await pagamentos.RoteiroAsync("sem-resposta", "sem-resposta", "sem-resposta");
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var logs = new CapturaLogs();
        await using var worker = await WorkerAsync(mensageria, stringConexao, pagamentos, logs);

        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await EsperarLogAsync(logs, 7014)).Should().BeTrue("a primeira tentativa esgotou o tempo: resultado incerto");
        var cronometro = Stopwatch.StartNew();
        await using (var conexao = new SqlConnection(stringConexao))
        {
            await conexao.OpenAsync(TestContext.Current.CancellationToken);
            await using var comando = new SqlCommand($"UPDATE Pedidos SET AtualizadoEm = AtualizadoEm WHERE Id = '{pedidoId}'", conexao) { CommandTimeout = 10 };
            await comando.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        cronometro.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(3), "o pedido não fica preso além do tempo total da chamada");
        (await EsperarStatusAsync(stringConexao, pedidoId, "Processando", Prazo)).Should().BeTrue();
        Contar(logs, 5003).Should().Be(1);
        (await CobrancaAsync(pagamentos, pedidoId))!.Efetivacoes.Should().Be(1);
    }
}
