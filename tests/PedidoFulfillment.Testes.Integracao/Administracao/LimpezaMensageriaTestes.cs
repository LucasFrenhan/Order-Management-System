using System.Net;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// Limpeza de mensageria e idempotência, com SQL Server real (REQ-14, US1). A rodada é chamada
/// direto, com os dados datados em relação ao instante atual.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class LimpezaMensageriaTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static DateTimeOffset Agora => DateTimeOffset.UtcNow;

    private static OpcoesLimpeza Opcoes(int tamanhoLote = 1000, int lotesPorRodada = 10) =>
        new() { TamanhoLote = tamanhoLote, LotesPorRodada = lotesPorRodada };

    private FabricaWorker Worker(string stringConexao, CapturaLogs? logs = null, Action<IServiceCollection>? servicos = null) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Configuracoes = new Dictionary<string, string?> { ["Logging:LogLevel:PedidoFulfillment"] = "Debug" },
        Servicos = colecao =>
        {
            if (logs is not null)
            {
                colecao.AddSingleton<ILoggerProvider>(logs);
            }

            servicos?.Invoke(colecao);
        }
    };

    private static LimpezaMensageria Limpeza(FabricaWorker worker) =>
        worker.Services.GetServices<IHostedService>().OfType<LimpezaMensageria>().Single();

    private static async Task<Guid> EventoAsync(string stringConexao, TimeSpan idade, bool enviado)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var instante = Agora - idade;
        var envelope = EnvelopeEvento.Criar(Guid.CreateVersion7(), new PedidoCriado
        {
            PedidoId = Guid.CreateVersion7(),
            Numero = "PED-20261024-000001",
            ClienteId = Guid.CreateVersion7(),
            ValorTotal = 10m,
            Moeda = "BRL",
            Itens = [new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 10m }]
        }, instante);
        var evento = EventoSaida.De(envelope, instante);
        if (enviado)
        {
            evento.MarcarEnviado(instante);
        }

        contexto.EventosSaida.Add(evento);
        await contexto.SaveChangesAsync(Cancelamento);
        return envelope.IdEvento;
    }

    private static async Task<Guid> ProcessadoAsync(string stringConexao, TimeSpan idade)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var id = Guid.CreateVersion7();
        contexto.EventosProcessados.Add(new EventoProcessado("estoque", id, Agora - idade));
        await contexto.SaveChangesAsync(Cancelamento);
        return id;
    }

    private static async Task<Guid> PedidoReenvioAsync(string stringConexao, TimeSpan idade, SituacaoReenvioDlq situacao)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var instante = Agora - idade;
        var pedido = PedidoReenvioDlq.Registrar(Guid.CreateVersion7(), "estoque", new FiltroReenvio(null, null, null, null), instante);
        if (situacao != SituacaoReenvioDlq.Pendente)
        {
            pedido.Iniciar(instante);
        }

        if (situacao == SituacaoReenvioDlq.Concluido)
        {
            pedido.Concluir(new ContagensReenvio(0, 0, 0), instante);
        }
        else if (situacao == SituacaoReenvioDlq.Falhou)
        {
            pedido.Falhar(new ContagensReenvio(0, 0, 0), "queda", instante);
        }

        contexto.PedidosReenvioDlq.Add(pedido);
        await contexto.SaveChangesAsync(Cancelamento);
        return pedido.Id;
    }

    private static Task<int> ContarAsync(string stringConexao, string tabela, string condicao) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM {tabela} WHERE {condicao}");

    [Fact]
    public async Task So_o_que_passou_da_retencao_e_apagado_e_os_pendentes_ficam()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var enviadoAntigo = await EventoAsync(stringConexao, TimeSpan.FromDays(8), enviado: true);
        var enviadoNovo = await EventoAsync(stringConexao, TimeSpan.FromDays(6), enviado: true);
        var pendenteAntigo = await EventoAsync(stringConexao, TimeSpan.FromDays(30), enviado: false);
        var processadoAntigo = await ProcessadoAsync(stringConexao, TimeSpan.FromDays(8));
        var processadoNovo = await ProcessadoAsync(stringConexao, TimeSpan.FromDays(6));
        var reenvioConcluido = await PedidoReenvioAsync(stringConexao, TimeSpan.FromDays(91), SituacaoReenvioDlq.Concluido);
        var reenvioFalhou = await PedidoReenvioAsync(stringConexao, TimeSpan.FromDays(91), SituacaoReenvioDlq.Falhou);
        var reenvioPendente = await PedidoReenvioAsync(stringConexao, TimeSpan.FromDays(91), SituacaoReenvioDlq.Pendente);
        var reenvioEmExecucao = await PedidoReenvioAsync(stringConexao, TimeSpan.FromDays(91), SituacaoReenvioDlq.EmExecucao);
        var reenvioRecente = await PedidoReenvioAsync(stringConexao, TimeSpan.FromDays(10), SituacaoReenvioDlq.Concluido);
        var logs = new CapturaLogs();
        await using var worker = Worker(stringConexao, logs);

        await Limpeza(worker).ExecutarRodadaAsync(Opcoes(), Cancelamento);

        (await ContarAsync(stringConexao, "EventosSaida", $"IdEvento = '{enviadoAntigo}'")).Should().Be(0, "US1 cenário 1");
        (await ContarAsync(stringConexao, "EventosSaida", $"IdEvento IN ('{enviadoNovo}', '{pendenteAntigo}')")).Should().Be(2, "US1 cenário 2: pendente nunca é apagado");
        (await ContarAsync(stringConexao, "EventosProcessados", $"IdEvento = '{processadoAntigo}'")).Should().Be(0, "US1 cenário 3");
        (await ContarAsync(stringConexao, "EventosProcessados", $"IdEvento = '{processadoNovo}'")).Should().Be(1);
        (await ContarAsync(stringConexao, "PedidosReenvioDlq", $"Id IN ('{reenvioConcluido}', '{reenvioFalhou}')")).Should().Be(0, "FR-002");
        (await ContarAsync(stringConexao, "PedidosReenvioDlq", $"Id IN ('{reenvioPendente}', '{reenvioEmExecucao}', '{reenvioRecente}')")).Should().Be(3);
        var resumo = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5013).Subject;
        resumo.Nivel.Should().Be(LogLevel.Information);
        resumo.Texto.Should().Contain("1 evento(s) enviado(s)").And.Contain("1 registro(s) de processado").And.Contain("2 pedido(s) de reenvio");
    }

    [Fact]
    public async Task Chave_de_idempotencia_so_e_apagada_depois_da_retencao_e_a_recente_continua_valendo()
    {
        // US1 cenário 4: a garantia de 24 h do REQ-05.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var chaveAntiga = Guid.NewGuid();
        var chaveRecente = Guid.NewGuid();
        (await CriarAsync(cliente, chaveAntiga, (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);
        var criado = await CriarAsync(cliente, chaveRecente, (Cat001, 1));
        var pedidoRecente = (await CorpoAsync(criado)).GetProperty("id").GetGuid();
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE ChavesIdempotencia SET CriadoEm = DATEADD(hour, -49, SYSDATETIMEOFFSET()) WHERE Chave = '{chaveAntiga}'");
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE ChavesIdempotencia SET CriadoEm = DATEADD(hour, -23, SYSDATETIMEOFFSET()) WHERE Chave = '{chaveRecente}'");
        await using var worker = Worker(stringConexao);

        await Limpeza(worker).ExecutarRodadaAsync(Opcoes(), Cancelamento);

        (await ContarAsync(stringConexao, "ChavesIdempotencia", $"Chave = '{chaveAntiga}'")).Should().Be(0);
        (await ContarAsync(stringConexao, "ChavesIdempotencia", $"Chave = '{chaveRecente}'")).Should().Be(1);
        var repetido = await CriarAsync(cliente, chaveRecente, (Cat001, 1));
        (await CorpoAsync(repetido)).GetProperty("id").GetGuid().Should().Be(pedidoRecente, "a repetição dentro das 24 h devolve o mesmo pedido");
    }

    [Fact]
    public async Task Apaga_em_lotes_e_deixa_o_resto_para_a_rodada_seguinte()
    {
        // US1 cenário 5.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        for (var i = 0; i < 5; i++)
        {
            await ProcessadoAsync(stringConexao, TimeSpan.FromDays(8));
        }

        await using var worker = Worker(stringConexao);
        var limpeza = Limpeza(worker);

        await limpeza.ExecutarRodadaAsync(Opcoes(tamanhoLote: 2, lotesPorRodada: 1), Cancelamento);
        (await ContarAsync(stringConexao, "EventosProcessados", "1 = 1")).Should().Be(3);

        await limpeza.ExecutarRodadaAsync(Opcoes(tamanhoLote: 2, lotesPorRodada: 1), Cancelamento);
        await limpeza.ExecutarRodadaAsync(Opcoes(tamanhoLote: 2, lotesPorRodada: 1), Cancelamento);
        (await ContarAsync(stringConexao, "EventosProcessados", "1 = 1")).Should().Be(0);
    }

    [Fact]
    public async Task Com_o_lock_ocupado_a_rodada_e_pulada_e_sem_nada_a_apagar_o_resumo_e_debug()
    {
        // US1 cenários 6 e 7.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var antigo = await ProcessadoAsync(stringConexao, TimeSpan.FromDays(8));
        var logs = new CapturaLogs();
        await using var worker = Worker(stringConexao, logs);
        var limpeza = Limpeza(worker);
        var semPool = new SqlConnectionStringBuilder(stringConexao) { Pooling = false }.ConnectionString;

        await using (var conexao = new SqlConnection(semPool))
        {
            await conexao.OpenAsync(Cancelamento);
            await using (var comando = new SqlCommand(
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;", conexao))
            {
                comando.Parameters.AddWithValue("@recurso", LimpezaMensageria.RecursoLock);
                ((int)(await comando.ExecuteScalarAsync(Cancelamento))!).Should().BeGreaterThanOrEqualTo(0);
            }

            await limpeza.ExecutarRodadaAsync(Opcoes(), Cancelamento);

            logs.Registros.Should().Contain(r => r.Evento.Id == 5015 && r.Texto.Contains("limpeza", StringComparison.Ordinal));
            (await ContarAsync(stringConexao, "EventosProcessados", $"IdEvento = '{antigo}'")).Should().Be(1);

            // Libera explicitamente: fechar a conexão encerra a sessão de forma assíncrona no
            // servidor, e a rodada seguinte poderia ainda encontrar o lock ocupado.
            await using var liberar = new SqlCommand("EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';", conexao);
            liberar.Parameters.AddWithValue("@recurso", LimpezaMensageria.RecursoLock);
            await liberar.ExecuteNonQueryAsync(Cancelamento);
        }

        await limpeza.ExecutarRodadaAsync(Opcoes(), Cancelamento);
        await limpeza.ExecutarRodadaAsync(Opcoes(), Cancelamento);

        (await ContarAsync(stringConexao, "EventosProcessados", $"IdEvento = '{antigo}'")).Should().Be(0);
        logs.Registros.Count(r => r.Evento.Id == 5015).Should().Be(1, "só a rodada com o lock ocupado é pulada");
        var resumos = logs.Registros.Where(r => r.Evento.Id == 5013).ToList();
        resumos.Should().HaveCount(2);
        resumos[0].Nivel.Should().Be(LogLevel.Information);
        resumos[1].Nivel.Should().Be(LogLevel.Debug, "rodada sem nada a apagar não gera aviso");
    }

    [Fact]
    public async Task Falha_da_rodada_e_registrada_e_a_seguinte_roda()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var logs = new CapturaLogs();
        await using var worker = Worker(stringConexao, logs);
        var limpeza = Limpeza(worker);
        await BancoDadosTeste.ExecutarAsync(stringConexao, "EXEC sp_rename 'EventosProcessados', 'EventosProcessadosRenomeada'");

        await limpeza.ExecutarRodadaAsync(Opcoes(), Cancelamento);
        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 5014);

        await BancoDadosTeste.ExecutarAsync(stringConexao, "EXEC sp_rename 'EventosProcessadosRenomeada', 'EventosProcessados'");
        await limpeza.ExecutarRodadaAsync(Opcoes(), Cancelamento);
        logs.Registros.Should().Contain(r => r.Evento.Id == 5013);
    }
}
