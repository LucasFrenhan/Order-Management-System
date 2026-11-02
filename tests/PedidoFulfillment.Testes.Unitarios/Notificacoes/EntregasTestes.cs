using FluentAssertions;
using PedidoFulfillment.Application.Notificacoes;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>Espera entre tentativas, esgotamento e estados de entrega e destino (REQ-18, FR-015; data-model).</summary>
public sealed class EntregasTestes
{
    private static readonly DateTimeOffset Inicio = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Limite = TimeSpan.FromHours(24);

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 120)]
    [InlineData(3, 600)]
    [InlineData(4, 1800)]
    [InlineData(5, 3600)]
    [InlineData(6, 7200)]
    [InlineData(20, 7200)]
    public void Espera_cresce_ate_duas_horas(int tentativas, int segundos)
    {
        EsperaEntregas.Espera(tentativas).Should().Be(TimeSpan.FromSeconds(segundos));
    }

    [Fact]
    public void A_ultima_tentativa_cai_no_limite_e_uma_falha_no_limite_esgota()
    {
        EsperaEntregas.Proxima(Inicio, 1, Inicio, Limite).Should().Be(Inicio.AddSeconds(30));
        EsperaEntregas.Proxima(Inicio, 10, Inicio.AddHours(23), Limite).Should().Be(Inicio + Limite);
        EsperaEntregas.Proxima(Inicio, 11, Inicio + Limite, Limite).Should().BeNull();
        EsperaEntregas.Proxima(Inicio, 11, Inicio.AddHours(25), Limite).Should().BeNull();
    }

    private static EntregaNotificacao NovaEntrega() => EntregaNotificacao.Criar(Guid.CreateVersion7(), 1, null, Inicio);

    [Fact]
    public void Entrega_conta_tentativas_guarda_a_primeira_e_finaliza_uma_vez()
    {
        var entrega = NovaEntrega();

        entrega.RegistrarFalha(500, "HTTP 500", Inicio.AddSeconds(30), Inicio);
        entrega.MarcarEntregue(204, Inicio.AddSeconds(31));

        (entrega.Situacao, entrega.Tentativas, entrega.PrimeiraTentativaEm, entrega.UltimoCodigoHttp, entrega.FinalizadaEm)
            .Should().Be((SituacaoEntrega.Entregue, 2, Inicio, 204, Inicio.AddSeconds(31)));
        entrega.Descartar(Inicio.AddMinutes(1)).Should().BeFalse();
        ((Action)(() => entrega.Esgotar(500, "x", Inicio.AddMinutes(1)))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Entrega_esgotada_e_descartada_preenchem_o_fim()
    {
        var esgotada = NovaEntrega();
        esgotada.Esgotar(410, "HTTP 410", Inicio);
        var descartada = NovaEntrega();

        descartada.Descartar(Inicio).Should().BeTrue();

        (esgotada.Situacao, esgotada.FinalizadaEm).Should().Be((SituacaoEntrega.Esgotada, Inicio));
        (descartada.Situacao, descartada.FinalizadaEm, descartada.Tentativas).Should().Be((SituacaoEntrega.Descartada, Inicio, 0));
    }

    [Fact]
    public void Erro_longo_e_cortado_no_tamanho_da_coluna()
    {
        var entrega = NovaEntrega();

        entrega.RegistrarFalha(null, new string('x', 500), Inicio.AddSeconds(30), Inicio);

        entrega.UltimoErro.Should().HaveLength(EntregaNotificacao.TamanhoMaximoErro);
    }

    private static DestinoNotificacao NovoDestino(Guid? clienteId = null) =>
        DestinoNotificacao.Cadastrar(clienteId, Guid.CreateVersion7(), "https://loja.exemplo.com/oms", new byte[DestinoNotificacao.TamanhoSegredoCifrado], Inicio);

    [Fact]
    public void Destino_desativa_com_motivo_e_reativa_zerando_as_falhas()
    {
        var destino = NovoDestino(Guid.CreateVersion7());
        destino.RegistrarFalha(Inicio);
        destino.RegistrarFalha(Inicio);

        destino.Desativar(DestinoNotificacao.MotivoGone, Inicio).Should().BeTrue();
        destino.Desativar("outra vez", Inicio).Should().BeFalse();
        (destino.Ativo, destino.MotivoDesativacao, destino.FalhasSeguidas).Should().Be((false, "410 Gone", 2));

        destino.Reativar(Inicio.AddHours(1)).Should().BeTrue();
        destino.Reativar(Inicio.AddHours(2)).Should().BeFalse();
        (destino.Ativo, destino.MotivoDesativacao, destino.DesativadoEm, destino.FalhasSeguidas).Should().Be((true, null, null, 0));
        destino.Global.Should().BeFalse();
    }

    [Fact]
    public void Destino_sem_Cliente_e_global_e_sucesso_zera_as_falhas()
    {
        var destino = NovoDestino();
        destino.RegistrarFalha(Inicio);

        destino.RegistrarSucesso(Inicio);

        (destino.Global, destino.FalhasSeguidas).Should().Be((true, 0));
    }

    [Fact]
    public void Segredo_cifrado_com_outro_tamanho_e_recusado()
    {
        var cadastrar = () => DestinoNotificacao.Cadastrar(null, Guid.CreateVersion7(), "https://x.exemplo.com", new byte[32], Inicio);

        cadastrar.Should().Throw<ArgumentException>();
    }
}
