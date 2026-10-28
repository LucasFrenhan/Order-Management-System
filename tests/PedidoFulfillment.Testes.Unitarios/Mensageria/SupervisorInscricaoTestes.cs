using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>
/// Laço do supervisor de inscrição sem broker (REQ-17, R-10): sessões falsas, com a perda
/// controlada pelo teste, e uma espera que só anota o intervalo pedido.
/// </summary>
public sealed class SupervisorInscricaoTestes
{
    private const string Consumidor = "estoque";

    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(10);

    private readonly EstadoConsumidores estado = new([new DescritorFalso(Consumidor)]);
    private readonly LoggerFalso<SupervisorInscricao> logger = new();
    private readonly List<TimeSpan> esperas = [];
    private readonly List<SessaoFalsa> sessoes = [];
    private readonly CancellationTokenSource parar = new();

    [Fact]
    public async Task Abre_a_sessao_marca_inscrito_e_espera_a_perda()
    {
        var execucao = Executar(_ => Abrir());

        await EsperarAsync(() => sessoes.Count == 1 && estado.TodosInscritos);
        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);

        sessoes.Should().ContainSingle().Which.Descartada.Should().BeTrue("o encerramento fecha a sessão aberta");
        esperas.Should().BeEmpty("a primeira tentativa sai na hora");
        logger.Eventos.Should().BeEmpty();
    }

    [Theory]
    [InlineData(MotivoPerda.Canal)]
    [InlineData(MotivoPerda.Cancelamento)]
    public async Task Perda_fecha_a_sessao_e_abre_outra_na_hora(string origem)
    {
        var execucao = Executar(_ => Abrir());
        await EsperarAsync(() => sessoes.Count == 1 && estado.TodosInscritos);

        sessoes[0].Perder(origem);

        await EsperarAsync(() => sessoes.Count == 2 && estado.TodosInscritos);
        sessoes[0].Descartada.Should().BeTrue();
        estado.SemInscricaoDesde(Consumidor).Should().BeNull("voltou a ficar inscrito");
        esperas.Should().BeEmpty("depois de uma perda, a primeira tentativa sai na hora");
        var perdida = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 5016).Subject;
        perdida.Propriedades["Origem"].Should().Be(origem);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 5018);
        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Perda_sinalizada_duas_vezes_abre_uma_sessao_so_e_nunca_duas_ao_mesmo_tempo()
    {
        var duasAbertas = false;
        var execucao = Executar(_ =>
        {
            // A sessão anterior fecha antes de abrir outra (FR-006).
            duasAbertas |= sessoes.Any(s => !s.Descartada);
            return Abrir();
        });
        await EsperarAsync(() => sessoes.Count == 1 && estado.TodosInscritos);

        sessoes[0].Perder(MotivoPerda.Canal);
        sessoes[0].Perder(MotivoPerda.Cancelamento);

        await EsperarAsync(() => sessoes.Count == 2 && estado.TodosInscritos);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        sessoes.Should().HaveCount(2);
        duasAbertas.Should().BeFalse("nunca há duas sessões do mesmo consumidor abertas");
        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Falhas_seguidas_esperam_1_2_4_8_e_10_s_e_dar_certo_zera_a_sequencia()
    {
        var chamadas = 0;
        var execucao = Executar(_ =>
        {
            chamadas++;
            // Seis falhas, uma sessão, uma falha depois da perda dela, outra sessão.
            return chamadas is <= 6 or 8 ? throw new InvalidOperationException("broker fora") : Abrir();
        });
        await EsperarAsync(() => sessoes.Count == 1 && estado.TodosInscritos);

        esperas.Should().Equal(Segundos(1, 2, 4, 8, 10, 10));
        logger.Eventos.Where(e => e.Id.Id == 5008).Should().HaveCount(6, "antes da primeira inscrição é a partida");
        logger.Eventos.Should().NotContain(e => e.Id.Id == 5017 || e.Id.Id == 5018);

        esperas.Clear();
        sessoes[0].Perder(MotivoPerda.Canal);

        await EsperarAsync(() => sessoes.Count == 2 && estado.TodosInscritos);
        esperas.Should().Equal(Segundos(1), "a sequência recomeça: na hora, depois 1 s");
        var falha = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 5017).Subject;
        falha.Propriedades["EsperaMs"].Should().Be(1000L);
        logger.Eventos.Where(e => e.Id.Id == 5008).Should().HaveCount(6, "depois de uma perda, a falha é 5017");
        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Sem_inscricao_desde_guarda_o_instante_da_primeira_perda()
    {
        var chamadas = 0;
        var execucao = Executar(async token =>
        {
            var chamada = Interlocked.Increment(ref chamadas);
            if (chamada == 1)
            {
                return await Abrir();
            }

            // Da quarta em diante, fica parado até o encerramento, para não girar sem fim.
            if (chamada >= 4)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            throw new InvalidOperationException("broker fora");
        });
        await EsperarAsync(() => estado.TodosInscritos);

        sessoes[0].Perder(MotivoPerda.Canal);

        await EsperarAsync(() => Volatile.Read(ref chamadas) >= 4);
        estado.TodosInscritos.Should().BeFalse();
        estado.SemInscricaoDesde(Consumidor).Should().Be(Relogio.GetUtcNow());
        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Encerrar_durante_a_espera_sai_sem_abrir_outra_sessao()
    {
        var chamadas = 0;
        var execucao = Task.Run(() => new SupervisorInscricao(
            Consumidor,
            _ => { Interlocked.Increment(ref chamadas); throw new InvalidOperationException("broker fora"); },
            (espera, token) => Task.Delay(Timeout.InfiniteTimeSpan, token),
            estado,
            Relogio,
            logger).ExecutarAsync(parar.Token), TestContext.Current.CancellationToken);
        await EsperarAsync(() => Volatile.Read(ref chamadas) == 1);

        parar.Cancel();

        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);
        chamadas.Should().Be(1);
    }

    [Fact]
    public async Task Encerrar_com_a_sessao_aberta_fecha_a_sessao_e_nao_reinscreve()
    {
        var execucao = Executar(_ => Abrir());
        await EsperarAsync(() => estado.TodosInscritos);

        parar.Cancel();
        await execucao.WaitAsync(Prazo, TestContext.Current.CancellationToken);

        sessoes.Should().ContainSingle().Which.Descartada.Should().BeTrue();
        logger.Eventos.Should().NotContain(e => e.Id.Id == 5016 || e.Id.Id == 5018);
    }

    private static readonly RelogioFixo Relogio = new(new DateTimeOffset(2026, 10, 28, 12, 0, 0, TimeSpan.Zero));

    private static TimeSpan[] Segundos(params int[] valores) => [.. valores.Select(v => TimeSpan.FromSeconds(v))];

    /// <summary>
    /// No pool de threads, como um <c>BackgroundService</c>: a espera falsa não cede, e tentativas
    /// seguidas no contexto do xUnit prenderiam o próprio teste.
    /// </summary>
    private Task Executar(Func<CancellationToken, Task<ISessaoConsumo>> abrir) =>
        Task.Run(() => new SupervisorInscricao(
            Consumidor,
            abrir,
            (espera, _) =>
            {
                lock (esperas)
                {
                    esperas.Add(espera);
                }

                return Task.CompletedTask;
            },
            estado,
            Relogio,
            logger).ExecutarAsync(parar.Token), TestContext.Current.CancellationToken);

    private Task<ISessaoConsumo> Abrir()
    {
        var sessao = new SessaoFalsa();
        sessoes.Add(sessao);
        return Task.FromResult<ISessaoConsumo>(sessao);
    }

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow + Prazo;
        while (!condicao())
        {
            DateTime.UtcNow.Should().BeBefore(limite, "a condição deveria ter ficado verdadeira");
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
    }

    private sealed class SessaoFalsa : ISessaoConsumo
    {
        private readonly TaskCompletionSource<MotivoPerda> perda = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<MotivoPerda> Perdida => perda.Task;

        public bool Descartada { get; private set; }

        public void Perder(string origem) => perda.TrySetResult(new MotivoPerda(origem, "teste"));

        public ValueTask DisposeAsync()
        {
            Descartada = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DescritorFalso(string consumidor) : IDescritorConsumidor
    {
        public string Consumidor => consumidor;

        public IReadOnlyCollection<string> Tipos => [];
    }
}
