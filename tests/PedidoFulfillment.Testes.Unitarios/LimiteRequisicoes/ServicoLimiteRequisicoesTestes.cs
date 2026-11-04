using System.Collections.Concurrent;
using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Testes.Unitarios.LimiteRequisicoes;

/// <summary>A regra do limite, sem banco (REQ-23, FR-002, FR-012, FR-015, FR-020, FR-021, R-03 a R-05).</summary>
public sealed class ServicoLimiteRequisicoesTestes
{
    private const string Email = "maria@exemplo.com.br";

    private static readonly IPAddress Origem = IPAddress.Parse("203.0.113.7");

    private readonly ArmazenamentoFalso armazenamento = new();
    private readonly BloqueiosFalsos bloqueios = new();
    private readonly MetricasFalsas metricas = new();
    private readonly LogsFalsos logs = new();
    private readonly RelogioManual relogio = new();

    private ServicoLimiteRequisicoes Criar(TimeSpan? pausa = null, TimeSpan? tempoMaximo = null) => new(
        new ConfiguracaoLimites(
            new PoliticaLimite(PoliticaLimite.LoginOrigemEmail, 10, 60),
            new PoliticaLimite(PoliticaLimite.LoginOrigem, 100, 60),
            new PoliticaLimite(PoliticaLimite.Registro, 20, 3600),
            new byte[32],
            tempoMaximo ?? TimeSpan.FromSeconds(5),
            pausa ?? TimeSpan.FromSeconds(5)),
        armazenamento,
        bloqueios,
        metricas,
        relogio,
        logs);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Login_com_email_avalia_as_duas_politicas()
    {
        var decisao = await Criar().AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        decisao.Aceita.Should().BeTrue();
        armazenamento.Chamadas.Should().ContainSingle().Which.Select(c => c.Limite).Should().Equal(10, 100);
    }

    [Fact]
    public async Task Login_sem_email_avalia_so_a_politica_por_origem()
    {
        await Criar().AvaliarAsync(RotaLimitada.Login, Origem, null, Cancelamento);

        armazenamento.Chamadas.Should().ContainSingle().Which.Select(c => c.Limite).Should().Equal(100);
    }

    [Fact]
    public async Task Registro_avalia_so_a_politica_de_registro()
    {
        await Criar().AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        armazenamento.Chamadas.Should().ContainSingle().Which.Select(c => c.Limite).Should().Equal(20);
    }

    [Theory]
    [InlineData(100, 1)]
    [InlineData(1000, 1)]
    [InlineData(1001, 2)]
    [InlineData(41500, 42)]
    public async Task A_espera_e_arredondada_para_cima_com_minimo_de_1(int milissegundos, int segundos)
    {
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromMilliseconds(milissegundos), true)];

        var decisao = await Criar().AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Should().Be(new DecisaoLimite(false, segundos));
    }

    [Fact]
    public async Task Com_duas_recusas_vale_a_maior_espera()
    {
        armazenamento.Recusas =
        [
            new RecusaContagem(0, TimeSpan.FromSeconds(10), false),
            new RecusaContagem(1, TimeSpan.FromSeconds(40), false)
        ];

        var decisao = await Criar().AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        decisao.RetryAfterSegundos.Should().Be(40);
    }

    [Fact]
    public async Task A_recusa_fica_em_memoria_e_a_seguinte_nao_vai_ao_armazenamento()
    {
        var servico = Criar();
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), true)];
        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        var decisao = await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Should().Be(new DecisaoLimite(false, 30));
        armazenamento.Chamadas.Should().HaveCount(1);
        metricas.Recusadas.Should().Equal(PoliticaLimite.Registro, PoliticaLimite.Registro);
    }

    [Fact]
    public async Task Com_um_bloqueio_local_do_email_outro_email_vai_ao_armazenamento()
    {
        var servico = Criar();
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), true)];
        await servico.AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);
        armazenamento.Recusas = [];

        var decisao = await servico.AvaliarAsync(RotaLimitada.Login, Origem, "joao@exemplo.com.br", Cancelamento);

        decisao.Aceita.Should().BeTrue();
        armazenamento.Chamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Falha_libera_a_requisicao_abre_a_pausa_e_registra_um_log()
    {
        var servico = Criar();
        armazenamento.Falha = new InvalidOperationException("Server=segredo");

        var decisao = await servico.AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);
        var naPausa = await servico.AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        decisao.Aceita.Should().BeTrue();
        naPausa.Aceita.Should().BeTrue();
        armazenamento.Chamadas.Should().HaveCount(1);
        metricas.SemLimite.Should().Be(2);
        logs.Eventos.Where(e => e.Id == 3008).Should().ContainSingle()
            .Which.Texto.Should().Contain(nameof(InvalidOperationException)).And.NotContain("segredo");
    }

    [Fact]
    public async Task Depois_da_pausa_volta_a_chamar_o_armazenamento()
    {
        var servico = Criar(pausa: TimeSpan.FromSeconds(5));
        armazenamento.Falha = new InvalidOperationException();
        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);
        armazenamento.Falha = null;
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), false)];

        relogio.Advance(TimeSpan.FromSeconds(5));
        var decisao = await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Aceita.Should().BeFalse();
        armazenamento.Chamadas.Should().HaveCount(2);
    }

    [Fact]
    public async Task Na_pausa_o_bloqueio_local_continua_valendo()
    {
        var servico = Criar();
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), false)];
        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);
        armazenamento.Recusas = [];
        armazenamento.Falha = new InvalidOperationException();
        await servico.AvaliarAsync(RotaLimitada.Registro, IPAddress.Parse("203.0.113.8"), null, Cancelamento);

        var decisao = await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Aceita.Should().BeFalse();
    }

    [Fact]
    public async Task Sem_pausa_configurada_cada_falha_vai_ao_armazenamento_e_ao_log()
    {
        var servico = Criar(pausa: TimeSpan.Zero);
        armazenamento.Falha = new InvalidOperationException();

        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);
        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        armazenamento.Chamadas.Should().HaveCount(2);
        logs.Eventos.Where(e => e.Id == 3008).Should().HaveCount(2);
    }

    [Fact]
    public async Task Acima_do_tempo_maximo_segue_sem_o_limite()
    {
        var servico = Criar(tempoMaximo: TimeSpan.FromMilliseconds(50));
        armazenamento.Demora = Timeout.InfiniteTimeSpan;

        var decisao = await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Aceita.Should().BeTrue();
        metricas.SemLimite.Should().Be(1);
        logs.Eventos.Should().Contain(e => e.Id == 3008);
    }

    [Fact]
    public async Task Deadlock_repete_uma_vez_e_aceita_o_resultado()
    {
        armazenamento.Disputas = 1;

        var decisao = await Criar().AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Aceita.Should().BeTrue();
        armazenamento.Chamadas.Should().HaveCount(2);
        metricas.SemLimite.Should().Be(0);
    }

    [Fact]
    public async Task Deadlock_que_persiste_segue_sem_o_limite_e_nao_abre_a_pausa()
    {
        var servico = Criar();
        armazenamento.Disputas = 2;

        var decisao = await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);
        armazenamento.Disputas = 0;
        await servico.AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);

        decisao.Aceita.Should().BeTrue();
        metricas.SemLimite.Should().Be(1);
        armazenamento.Chamadas.Should().HaveCount(3, "duas tentativas e, sem pausa, a requisição seguinte");
    }

    [Fact]
    public async Task Cancelamento_pelo_cliente_propaga_sem_log_nem_metrica()
    {
        using var cancelamento = new CancellationTokenSource();
        armazenamento.Demora = Timeout.InfiniteTimeSpan;
        var chamada = Criar().AvaliarAsync(RotaLimitada.Registro, Origem, null, cancelamento.Token);

        await cancelamento.CancelAsync();

        await FluentActions.Awaiting(() => chamada).Should().ThrowAsync<OperationCanceledException>();
        metricas.SemLimite.Should().Be(0);
        logs.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task So_o_primeiro_bloqueio_gera_log_com_o_email_mascarado()
    {
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), true)];

        await Criar().AvaliarAsync(RotaLimitada.Login, Origem, "  maria@exemplo.com.br ", Cancelamento);
        await Criar().AvaliarAsync(RotaLimitada.Registro, Origem, null, Cancelamento);
        armazenamento.Recusas = [new RecusaContagem(0, TimeSpan.FromSeconds(30), false)];
        await Criar().AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        var eventos = logs.Eventos.Where(e => e.Id == 3007).ToArray();
        eventos.Should().HaveCount(2);
        eventos[0].Texto.Should().Contain(PoliticaLimite.LoginOrigemEmail).And.Contain("m***@exemplo.com.br").And.NotContain(Email);
        eventos[1].Texto.Should().Contain(PoliticaLimite.Registro).And.NotContain("@");
    }

    [Fact]
    public async Task Politica_por_origem_nao_cita_o_email_no_log()
    {
        armazenamento.Recusas = [new RecusaContagem(1, TimeSpan.FromSeconds(30), true)];

        await Criar().AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        logs.Eventos.Should().ContainSingle(e => e.Id == 3007)
            .Which.Texto.Should().Contain(PoliticaLimite.LoginOrigem).And.NotContain("@");
    }

    [Fact]
    public async Task A_metrica_conta_cada_politica_que_recusou()
    {
        armazenamento.Recusas =
        [
            new RecusaContagem(0, TimeSpan.FromSeconds(1), false),
            new RecusaContagem(1, TimeSpan.FromSeconds(1), false)
        ];

        await Criar().AvaliarAsync(RotaLimitada.Login, Origem, Email, Cancelamento);

        metricas.Recusadas.Should().Equal(PoliticaLimite.LoginOrigemEmail, PoliticaLimite.LoginOrigem);
    }

    private sealed class RelogioManual : TimeProvider
    {
        private DateTimeOffset agora = new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => agora;

        public void Advance(TimeSpan tempo) => agora += tempo;
    }

    private sealed class ArmazenamentoFalso : IArmazenamentoContagens
    {
        public List<IReadOnlyList<ContagemSolicitada>> Chamadas { get; } = [];

        public IReadOnlyList<RecusaContagem> Recusas { get; set; } = [];

        public Exception? Falha { get; set; }

        public TimeSpan? Demora { get; set; }

        public int Disputas { get; set; }

        public async Task<IReadOnlyList<RecusaContagem>> ContarAsync(IReadOnlyList<ContagemSolicitada> contagens, CancellationToken cancellationToken)
        {
            Chamadas.Add(contagens);
            if (Demora is { } demora)
            {
                await Task.Delay(demora, cancellationToken);
            }

            if (Disputas > 0)
            {
                Disputas--;
                throw new DisputaContagemException("Deadlock simulado.", new InvalidOperationException());
            }

            return Falha is null ? Recusas : throw Falha;
        }
    }

    private sealed class BloqueiosFalsos : IBloqueiosLocais
    {
        private readonly ConcurrentDictionary<string, TimeSpan> registros = new();

        public void Registrar(byte[] chave, TimeSpan espera) => registros[Convert.ToHexString(chave)] = espera;

        public bool TentarObter(byte[] chave, out TimeSpan espera) => registros.TryGetValue(Convert.ToHexString(chave), out espera);
    }

    private sealed class MetricasFalsas : IMetricasLimite
    {
        public List<string> Recusadas { get; } = [];

        public int SemLimite { get; private set; }

        public void RequisicaoRecusada(string politica) => Recusadas.Add(politica);

        public void RequisicaoSemLimite() => SemLimite++;
    }

    private sealed class LogsFalsos : ILogger<ServicoLimiteRequisicoes>
    {
        public List<(int Id, string Texto)> Eventos { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullLogger.Instance.BeginScope(state);

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Eventos.Add((eventId.Id, formatter(state, exception)));
    }
}
