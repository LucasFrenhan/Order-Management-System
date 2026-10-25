using FluentAssertions;
using PedidoFulfillment.Application.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Pedido de reenvio da DLQ e o ciclo de vida dele (REQ-14, data-model).</summary>
public sealed class PedidoReenvioDlqTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Autor = Guid.CreateVersion7();
    private static readonly FiltroReenvio SemFiltro = new(null, null, null, null);

    private static PedidoReenvioDlq Pendente() => PedidoReenvioDlq.Registrar(Autor, "estoque", SemFiltro, T0);

    private static PedidoReenvioDlq EmExecucao()
    {
        var pedido = Pendente();
        pedido.Iniciar(T0.AddMinutes(1));
        return pedido;
    }

    [Fact]
    public void Registrar_cria_pendente_com_autor_consumidor_e_filtro_aparado()
    {
        var filtro = new FiltroReenvio("  pedido.criado  ", T0.AddDays(-1), T0, "  Timeout  ");

        var pedido = PedidoReenvioDlq.Registrar(Autor, "pagamento", filtro, T0);

        pedido.Id.Version.Should().Be(7);
        pedido.AutorId.Should().Be(Autor);
        pedido.Consumidor.Should().Be("pagamento");
        (pedido.TipoEvento, pedido.FalhouDe, pedido.FalhouAte, pedido.TrechoMotivo).Should().Be(("pedido.criado", T0.AddDays(-1), T0, "Timeout"));
        pedido.Filtro.Should().Be(new FiltroReenvio("pedido.criado", T0.AddDays(-1), T0, "Timeout"));
        pedido.Situacao.Should().Be(SituacaoReenvioDlq.Pendente);
        pedido.RegistradoEm.Should().Be(T0);
        (pedido.IniciadoEm, pedido.ConcluidoEm, pedido.MotivoFalha).Should().Be((null, null, null));
        (pedido.Reenviadas, pedido.BloqueadasPeloLimite, pedido.ForaDoFiltro).Should().Be((0, 0, 0));
    }

    [Fact]
    public void Campos_vazios_do_filtro_viram_nulos()
    {
        var pedido = PedidoReenvioDlq.Registrar(Autor, "entrega", new FiltroReenvio("   ", null, null, ""), T0);

        pedido.TipoEvento.Should().BeNull();
        pedido.TrechoMotivo.Should().BeNull();
    }

    public static TheoryData<string, Guid, string, FiltroReenvio> Invalidos => new()
    {
        { "autor vazio", Guid.Empty, "estoque", SemFiltro },
        { "consumidor desconhecido", Autor, "desconhecido", SemFiltro },
        { "período invertido", Autor, "estoque", new FiltroReenvio(null, T0, T0.AddSeconds(-1), null) },
        { "tipo longo", Autor, "estoque", new FiltroReenvio(new string('t', 101), null, null, null) },
        { "trecho longo", Autor, "estoque", new FiltroReenvio(null, null, null, new string('m', 201)) }
    };

    [Theory]
    [MemberData(nameof(Invalidos))]
    public void Registrar_recusa_dados_invalidos(string caso, Guid autor, string consumidor, FiltroReenvio filtro)
    {
        var registrar = () => PedidoReenvioDlq.Registrar(autor, consumidor, filtro, T0);

        registrar.Should().Throw<ArgumentException>(caso);
    }

    [Fact]
    public void Iniciar_leva_a_em_execucao_e_pode_retomar_uma_execucao_que_caiu()
    {
        var pedido = Pendente();

        pedido.Iniciar(T0.AddMinutes(1));
        pedido.Situacao.Should().Be(SituacaoReenvioDlq.EmExecucao);
        pedido.IniciadoEm.Should().Be(T0.AddMinutes(1));

        pedido.Iniciar(T0.AddMinutes(5));
        pedido.IniciadoEm.Should().Be(T0.AddMinutes(5), "a retomada recomeça a execução");
    }

    [Fact]
    public void Concluir_grava_as_contagens_e_o_fim()
    {
        var pedido = EmExecucao();

        pedido.Concluir(new ContagensReenvio(4, 1, 2), T0.AddMinutes(2));

        pedido.Situacao.Should().Be(SituacaoReenvioDlq.Concluido);
        (pedido.Reenviadas, pedido.BloqueadasPeloLimite, pedido.ForaDoFiltro).Should().Be((4, 1, 2));
        pedido.ConcluidoEm.Should().Be(T0.AddMinutes(2));
        pedido.MotivoFalha.Should().BeNull();
    }

    [Fact]
    public void Falhar_grava_as_contagens_parciais_e_o_motivo_aparado()
    {
        var pedido = EmExecucao();

        pedido.Falhar(new ContagensReenvio(2, 0, 0), $"  {new string('e', 600)}  ", T0.AddMinutes(2));

        pedido.Situacao.Should().Be(SituacaoReenvioDlq.Falhou);
        pedido.Reenviadas.Should().Be(2);
        pedido.MotivoFalha.Should().HaveLength(PedidoReenvioDlq.TamanhoMaximoMotivoFalha).And.StartWith("e");
        pedido.ConcluidoEm.Should().Be(T0.AddMinutes(2));
    }

    [Fact]
    public void Transicoes_fora_do_ciclo_sao_recusadas_sem_alterar_nada()
    {
        var pendente = Pendente();
        var concluido = EmExecucao();
        concluido.Concluir(new ContagensReenvio(1, 0, 0), T0.AddMinutes(2));

        ((Action)(() => pendente.Concluir(new ContagensReenvio(1, 0, 0), T0))).Should().Throw<InvalidOperationException>();
        ((Action)(() => pendente.Falhar(new ContagensReenvio(0, 0, 0), "x", T0))).Should().Throw<InvalidOperationException>();
        ((Action)(() => concluido.Iniciar(T0.AddMinutes(3)))).Should().Throw<InvalidOperationException>();
        ((Action)(() => concluido.Falhar(new ContagensReenvio(0, 0, 0), "x", T0))).Should().Throw<InvalidOperationException>();
        pendente.Situacao.Should().Be(SituacaoReenvioDlq.Pendente);
        concluido.Situacao.Should().Be(SituacaoReenvioDlq.Concluido);
        concluido.Reenviadas.Should().Be(1);
    }

    [Fact]
    public void Contagens_negativas_sao_recusadas()
    {
        var pedido = EmExecucao();

        var concluir = () => pedido.Concluir(new ContagensReenvio(-1, 0, 0), T0);

        concluir.Should().Throw<ArgumentOutOfRangeException>();
        pedido.Situacao.Should().Be(SituacaoReenvioDlq.EmExecucao);
    }

    [Fact]
    public void Consumidores_conhecidos_sao_os_tres_do_fluxo()
    {
        ConsumidoresConhecidos.Nomes.Should().Equal("estoque", "pagamento", "entrega");
    }
}
