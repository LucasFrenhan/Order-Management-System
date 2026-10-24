using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Estorno do pagamento (REQ-13, R-02 e data-model).</summary>
public sealed class PagamentoEstornoTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);
    private static readonly DateTimeOffset T3 = T0.AddMinutes(3);

    private static Pagamento Aprovado()
    {
        var pagamento = new Pagamento(Guid.CreateVersion7(), 50m, T0);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, "SIM-1", T0);
        return pagamento;
    }

    private static Pagamento EstornoPendente()
    {
        var pagamento = Aprovado();
        pagamento.SolicitarEstorno(T1);
        return pagamento;
    }

    [Fact]
    public void SolicitarEstorno_leva_o_aprovado_a_estorno_pendente_e_mantem_a_transacao()
    {
        var pagamento = Aprovado();

        pagamento.SolicitarEstorno(T1);

        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        pagamento.EstornoSolicitadoEm.Should().Be(T1);
        pagamento.TentativasEstorno.Should().Be(0);
        pagamento.UltimaFalhaEstornoEm.Should().BeNull();
        pagamento.IdentificadorEstorno.Should().BeNull();
        pagamento.EstornadoEm.Should().BeNull();
        pagamento.AtualizadoEm.Should().Be(T1);
        pagamento.IdentificadorTransacao.Should().Be("SIM-1");
    }

    public static TheoryData<SituacaoPagamento> SituacoesSemEstorno => new()
    {
        SituacaoPagamento.Pendente,
        SituacaoPagamento.Recusado,
        SituacaoPagamento.NaoProcessado,
        SituacaoPagamento.EstornoPendente,
        SituacaoPagamento.Estornado
    };

    [Theory]
    [MemberData(nameof(SituacoesSemEstorno))]
    public void SolicitarEstorno_fora_de_aprovado_e_recusado_sem_alterar_nada(SituacaoPagamento situacao)
    {
        var pagamento = NaSituacao(situacao);
        var atualizadoEm = pagamento.AtualizadoEm;

        var solicitar = () => pagamento.SolicitarEstorno(T3);

        solicitar.Should().Throw<InvalidOperationException>();
        pagamento.Situacao.Should().Be(situacao);
        pagamento.AtualizadoEm.Should().Be(atualizadoEm);
    }

    [Fact]
    public void RegistrarFalhaEstorno_conta_as_tentativas_e_guarda_a_ultima()
    {
        var pagamento = EstornoPendente();

        pagamento.RegistrarFalhaEstorno(T2);
        pagamento.RegistrarFalhaEstorno(T3);

        pagamento.TentativasEstorno.Should().Be(2);
        pagamento.UltimaFalhaEstornoEm.Should().Be(T3);
        pagamento.AtualizadoEm.Should().Be(T3);
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
    }

    [Fact]
    public void RegistrarFalhaEstorno_fora_de_estorno_pendente_e_recusado()
    {
        var pagamento = Aprovado();

        var registrar = () => pagamento.RegistrarFalhaEstorno(T2);

        registrar.Should().Throw<InvalidOperationException>();
        pagamento.TentativasEstorno.Should().Be(0);
    }

    [Fact]
    public void ConfirmarEstorno_leva_a_estornado_com_identificador_aparado()
    {
        var pagamento = EstornoPendente();
        pagamento.RegistrarFalhaEstorno(T2);

        pagamento.ConfirmarEstorno("  SIM-EST-1  ", T3);

        pagamento.Situacao.Should().Be(SituacaoPagamento.Estornado);
        pagamento.IdentificadorEstorno.Should().Be("SIM-EST-1");
        pagamento.EstornadoEm.Should().Be(T3);
        pagamento.AtualizadoEm.Should().Be(T3);
        pagamento.TentativasEstorno.Should().Be(1);
        pagamento.IdentificadorTransacao.Should().Be("SIM-1");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ConfirmarEstorno_sem_identificador_e_recusado(string identificador)
    {
        var pagamento = EstornoPendente();

        var confirmar = () => pagamento.ConfirmarEstorno(identificador, T3);

        confirmar.Should().Throw<ArgumentException>();
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
    }

    [Fact]
    public void ConfirmarEstorno_com_identificador_acima_de_100_caracteres_e_recusado()
    {
        var pagamento = EstornoPendente();

        var confirmar = () => pagamento.ConfirmarEstorno(new string('e', Pagamento.TamanhoMaximoIdentificadorEstorno + 1), T3);

        confirmar.Should().Throw<ArgumentException>();
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        Pagamento.TamanhoMaximoIdentificadorEstorno.Should().Be(100);
    }

    [Fact]
    public void ConfirmarEstorno_fora_de_estorno_pendente_e_recusado()
    {
        var pagamento = Aprovado();

        var confirmar = () => pagamento.ConfirmarEstorno("SIM-EST-1", T3);

        confirmar.Should().Throw<InvalidOperationException>();
        pagamento.Situacao.Should().Be(SituacaoPagamento.Aprovado);
    }

    [Theory]
    [InlineData(SituacaoPagamento.EstornoPendente)]
    [InlineData(SituacaoPagamento.Estornado)]
    public void RegistrarResultado_nao_leva_as_situacoes_de_estorno(SituacaoPagamento situacao)
    {
        var pagamento = new Pagamento(Guid.CreateVersion7(), 10m, T0);

        var registrar = () => pagamento.RegistrarResultado(situacao, "SIM-1", T1);

        registrar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Instantes_fora_de_UTC_sao_recusados()
    {
        var foraDeUtc = new DateTimeOffset(2026, 10, 24, 9, 0, 0, TimeSpan.FromHours(-3));

        var solicitar = () => Aprovado().SolicitarEstorno(foraDeUtc);
        var falhar = () => EstornoPendente().RegistrarFalhaEstorno(foraDeUtc);
        var confirmar = () => EstornoPendente().ConfirmarEstorno("SIM-EST-1", foraDeUtc);

        solicitar.Should().Throw<ArgumentException>();
        falhar.Should().Throw<ArgumentException>();
        confirmar.Should().Throw<ArgumentException>();
    }

    private static Pagamento NaSituacao(SituacaoPagamento situacao)
    {
        switch (situacao)
        {
            case SituacaoPagamento.Pendente:
                return new Pagamento(Guid.CreateVersion7(), 10m, T0);
            case SituacaoPagamento.EstornoPendente:
                return EstornoPendente();
            case SituacaoPagamento.Estornado:
                var estornado = EstornoPendente();
                estornado.ConfirmarEstorno("SIM-EST-1", T2);
                return estornado;
            default:
                var pagamento = new Pagamento(Guid.CreateVersion7(), 10m, T0);
                pagamento.RegistrarResultado(situacao, null, T0);
                return pagamento;
        }
    }
}
