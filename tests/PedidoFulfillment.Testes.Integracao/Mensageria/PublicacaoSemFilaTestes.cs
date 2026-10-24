using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Publicação num broker sem as filas dos consumidores (REQ-12, US1, FR-018, SC-001): as filas
/// existem antes da primeira publicação, e um evento com consumidor que o broker devolve sem
/// rota fica pendente no outbox, em vez de ser dado como enviado.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PublicacaoSemFilaTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(15);

    private static readonly string[] Consumidores = [CenarioEstoque.FilaEstoque, CenarioPagamento.FilaPagamento, CenarioEntrega.FilaEntrega];

    [Fact]
    public async Task Broker_sem_filas_recebe_as_filas_antes_da_primeira_publicacao()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await ApagarTodasAsync();
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        try
        {
            // Sem nenhum consumidor no ar: só o despachante declara a topologia.
            await using var worker = SoDespachante(stringConexao);
            worker.CreateClient().Dispose();

            (await EsperarEnviadoAsync(stringConexao, TiposEvento.PedidoCriado, pedidoId)).Should().BeTrue();
            foreach (var consumidor in Consumidores)
            {
                (await mensageria.MensagensNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor))).Should().NotBeNull($"a fila de {consumidor} precisa existir");
                (await mensageria.MensagensNaFilaAsync(TopologiaMensageria.FilaMortas(consumidor))).Should().NotBeNull();
            }

            (await mensageria.MensagensNaFilaAsync(TopologiaMensageria.FilaPrincipal(CenarioEstoque.FilaEstoque))).Should().Be(1);
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    [Fact]
    public async Task Worker_completo_num_broker_sem_filas_leva_o_pedido_ate_pronto_para_envio()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await ApagarTodasAsync();
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        using var logs = new CapturaLogs();

        await using var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Servicos = servicos => servicos.AddSingleton<ILoggerProvider>(logs)
        };
        worker.CreateClient().Dispose();

        (await CenarioPagamento.EsperarStatusAsync(stringConexao, pedidoId, "ProntoParaEnvio", Prazo)).Should().BeTrue();
        // A declaração do despachante e a do consumidor são idênticas (FR-002).
        logs.Textos.Should().NotContain(t => t.Contains("PRECONDITION_FAILED", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Fila_apagada_deixa_o_evento_pendente_sem_rota_ate_ser_recriada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        using var logs = new CapturaLogs();

        try
        {
            await using var worker = SoDespachante(stringConexao, logs);
            worker.CreateClient().Dispose();
            var principal = TopologiaMensageria.FilaPrincipal(CenarioEstoque.FilaEstoque);
            (await MensageriaTeste.EsperarAteAsync(async () => await mensageria.MensagensNaFilaAsync(principal) is not null, Prazo)).Should().BeTrue();
            await mensageria.LimparFilasConsumidorAsync(CenarioEstoque.FilaEstoque);

            // Fila removida com o Worker no ar (cenários 3 e 4 da US1).
            await mensageria.ApagarFilasConsumidorAsync(CenarioEstoque.FilaEstoque, soPrincipal: true);
            var primeiro = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            var segundo = (await CriadoAsync(cliente, (Cat002, 1))).GetProperty("id").GetGuid();

            (await MensageriaTeste.EsperarAteAsync(async () =>
                await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                    $"SELECT COUNT(*) FROM EventosSaida WHERE Conteudo LIKE '%{primeiro}%' AND Tipo = 'pedido.criado' AND UltimoErro LIKE 'SemRota:%'") == 1, Prazo))
                .Should().BeTrue("o broker devolve o evento sem rota");
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                $"SELECT COUNT(*) FROM EventosSaida WHERE Conteudo LIKE '%{primeiro}%' AND Tipo = 'pedido.criado' AND EnviadoEm IS NULL")).Should().Be(1);

            // O log 5002 traz o evento, o tipo e o motivo (US3, cenário 3).
            logs.Registros.Should().Contain(r => r.Evento.Id == 5002
                && r.Texto.Contains(TiposEvento.PedidoCriado, StringComparison.Ordinal)
                && r.Texto.Contains(DespachanteEventosSaida.MotivoSemRota, StringComparison.Ordinal));

            // O canal novo recria a fila, e o evento sai na tentativa seguinte, antes do segundo.
            (await EsperarEnviadoAsync(stringConexao, TiposEvento.PedidoCriado, primeiro)).Should().BeTrue();
            (await EsperarEnviadoAsync(stringConexao, TiposEvento.PedidoCriado, segundo)).Should().BeTrue();
            (await mensageria.MensagensNaFilaAsync(principal)).Should().Be(2);
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                $"""
                SELECT COUNT(*) FROM EventosSaida p JOIN EventosSaida s ON s.Id > p.Id
                WHERE p.Conteudo LIKE '%{primeiro}%' AND s.Conteudo LIKE '%{segundo}%' AND s.EnviadoEm >= p.EnviadoEm AND s.Tentativas = 0
                """)).Should().Be(1, "o evento seguinte espera atrás do que falhou");
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    [Fact]
    public async Task Tipo_sem_consumidor_e_enviado_sem_fila_ligada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var pedidoId = Guid.CreateVersion7();
        var agora = DateTimeOffset.UtcNow;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var envelope = EnvelopeEvento.Criar(pedidoId, new EntregaCriada
            {
                PedidoId = pedidoId,
                Numero = "PED-2026-000001",
                OrdemEntregaId = Guid.CreateVersion7(),
                Itens = [new ItemEntregaCriada { ProdutoId = Cat001, Quantidade = 1 }]
            }, agora);
            contexto.EventosSaida.Add(EventoSaida.De(envelope, agora));
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var worker = SoDespachante(stringConexao);
        worker.CreateClient().Dispose();

        (await EsperarEnviadoAsync(stringConexao, TiposEvento.EntregaCriada, pedidoId)).Should().BeTrue();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT Tentativas FROM EventosSaida")).Should().Be(0);
    }

    /// <summary>Worker sem os três consumidores: as filas só podem vir do despachante.</summary>
    private FabricaWorker SoDespachante(string stringConexao, CapturaLogs? logs = null) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Servicos = servicos =>
        {
            CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(servicos);
            CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(servicos);
            CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(servicos);
            if (logs is not null)
            {
                servicos.AddSingleton<ILoggerProvider>(logs);
            }
        }
    };

    private static Task<bool> EsperarEnviadoAsync(string stringConexao, string tipo, Guid pedidoId) =>
        MensageriaTeste.EsperarAteAsync(async () => await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = '{tipo}' AND Conteudo LIKE '%{pedidoId}%' AND EnviadoEm IS NOT NULL") == 1, Prazo);

    private async Task ApagarTodasAsync()
    {
        foreach (var consumidor in Consumidores)
        {
            await mensageria.ApagarFilasConsumidorAsync(consumidor);
        }
    }

    /// <summary>O broker é compartilhado pela coleção: não deixa mensagens para os próximos testes.</summary>
    private async Task LimparTodasAsync()
    {
        foreach (var consumidor in Consumidores)
        {
            await mensageria.LimparFilasConsumidorAsync(consumidor);
        }
    }
}
