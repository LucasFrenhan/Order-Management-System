using FluentAssertions;
using PedidoFulfillment.Domain.Pagamentos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>Ações da operação sobre o estorno pendente (REQ-28, data-model).</summary>
public sealed class PagamentoAcoesEstornoTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 10, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T1 = T0.AddMinutes(1);
    private static readonly DateTimeOffset T2 = T0.AddMinutes(2);
    private static readonly DateTimeOffset T3 = T0.AddMinutes(3);
    private static readonly Guid Autor = Guid.CreateVersion7();
    private const string MotivoValido = "Devolvido por transferência pelo financeiro.";

    private static Pagamento EstornoPendente()
    {
        var pagamento = new Pagamento(Guid.CreateVersion7(), 50m, T0);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, "SIM-1", T0);
        pagamento.SolicitarEstorno(T0);
        return pagamento;
    }

    private static Pagamento Baixado()
    {
        var pagamento = EstornoPendente();
        pagamento.DarBaixaEstorno(Autor, MotivoValido, null, T1);
        return pagamento;
    }

    private static Pagamento Estornado()
    {
        var pagamento = EstornoPendente();
        pagamento.ConfirmarEstorno("SIM-EST-1", T1);
        return pagamento;
    }

    [Fact]
    public void PedirTentativaEstorno_grava_a_marca_so_na_primeira_vez()
    {
        var pagamento = EstornoPendente();

        pagamento.PedirTentativaEstorno(T1).Should().BeTrue();
        pagamento.PedirTentativaEstorno(T2).Should().BeFalse();

        pagamento.TentativaEstornoForcadaEm.Should().Be(T1);
        pagamento.AtualizadoEm.Should().Be(T1);
    }

    [Fact]
    public void RegistrarFalhaEstorno_grava_o_ultimo_erro_e_limpa_a_marca()
    {
        var pagamento = EstornoPendente();
        pagamento.PedirTentativaEstorno(T1);

        pagamento.RegistrarFalhaEstorno(T2, "Recusado: Cobrança desconhecida.");

        pagamento.TentativasEstorno.Should().Be(1);
        pagamento.UltimaFalhaEstornoEm.Should().Be(T2);
        pagamento.UltimoErroEstorno.Should().Be("Recusado: Cobrança desconhecida.");
        pagamento.TentativaEstornoForcadaEm.Should().BeNull();
    }

    [Fact]
    public void RegistrarFalhaEstorno_com_erro_acima_de_200_caracteres_e_recusado()
    {
        var pagamento = EstornoPendente();

        var registrar = () => pagamento.RegistrarFalhaEstorno(T2, new string('e', Pagamento.TamanhoMaximoUltimoErroEstorno + 1));

        registrar.Should().Throw<ArgumentException>();
        pagamento.TentativasEstorno.Should().Be(0);
        Pagamento.TamanhoMaximoUltimoErroEstorno.Should().Be(200);
    }

    [Fact]
    public void MarcarEstornoAtrasado_grava_uma_unica_vez()
    {
        var pagamento = EstornoPendente();

        pagamento.MarcarEstornoAtrasado(T1).Should().BeTrue();
        pagamento.MarcarEstornoAtrasado(T2).Should().BeFalse();

        pagamento.EstornoAtrasadoEm.Should().Be(T1);
    }

    [Fact]
    public void ConfirmarEstorno_limpa_a_marca_de_tentativa_forcada()
    {
        var pagamento = EstornoPendente();
        pagamento.PedirTentativaEstorno(T1);

        pagamento.ConfirmarEstorno("SIM-EST-1", T2);

        pagamento.TentativaEstornoForcadaEm.Should().BeNull();
    }

    [Fact]
    public void DarBaixaEstorno_leva_a_estorno_baixado_com_autor_motivo_e_referencia()
    {
        var pagamento = EstornoPendente();
        pagamento.PedirTentativaEstorno(T1);

        pagamento.DarBaixaEstorno(Autor, $"  {MotivoValido}  ", "  TED-778812  ", T2);

        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoBaixado);
        pagamento.EstornoBaixadoEm.Should().Be(T2);
        pagamento.EstornoBaixadoPorId.Should().Be(Autor);
        pagamento.MotivoBaixaEstorno.Should().Be(MotivoValido);
        pagamento.ReferenciaExternaBaixaEstorno.Should().Be("TED-778812");
        pagamento.TentativaEstornoForcadaEm.Should().BeNull();
        pagamento.IdentificadorEstorno.Should().BeNull();
        pagamento.EstornadoEm.Should().BeNull();
        pagamento.AtualizadoEm.Should().Be(T2);
    }

    [Fact]
    public void DarBaixaEstorno_sem_referencia_guarda_nulo()
    {
        var pagamento = EstornoPendente();

        pagamento.DarBaixaEstorno(Autor, MotivoValido, "   ", T2);

        pagamento.ReferenciaExternaBaixaEstorno.Should().BeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    [InlineData("curto 9 c")]
    [InlineData("  123456789  ")]
    public void DarBaixaEstorno_com_motivo_ausente_ou_curto_e_recusado(string? motivo)
    {
        var pagamento = EstornoPendente();

        var baixar = () => pagamento.DarBaixaEstorno(Autor, motivo!, null, T2);

        baixar.Should().Throw<ArgumentException>();
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
    }

    [Fact]
    public void DarBaixaEstorno_com_motivo_de_501_caracteres_ou_referencia_de_101_e_recusado()
    {
        var motivoLongo = () => EstornoPendente().DarBaixaEstorno(Autor, new string('m', Pagamento.TamanhoMaximoMotivoBaixa + 1), null, T2);
        var referenciaLonga = () => EstornoPendente().DarBaixaEstorno(
            Autor, MotivoValido, new string('r', Pagamento.TamanhoMaximoReferenciaExternaBaixa + 1), T2);
        var semAutor = () => EstornoPendente().DarBaixaEstorno(Guid.Empty, MotivoValido, null, T2);

        motivoLongo.Should().Throw<ArgumentException>();
        referenciaLonga.Should().Throw<ArgumentException>();
        semAutor.Should().Throw<ArgumentException>();
        Pagamento.TamanhoMinimoMotivoBaixa.Should().Be(10);
        Pagamento.TamanhoMaximoMotivoBaixa.Should().Be(500);
        Pagamento.TamanhoMaximoReferenciaExternaBaixa.Should().Be(100);
    }

    [Fact]
    public void DarBaixaEstorno_aceita_motivo_de_10_e_de_500_caracteres()
    {
        var curto = EstornoPendente();
        var longo = EstornoPendente();

        curto.DarBaixaEstorno(Autor, "1234567890", null, T2);
        longo.DarBaixaEstorno(Autor, new string('m', 500), null, T2);

        curto.Situacao.Should().Be(SituacaoPagamento.EstornoBaixado);
        longo.Situacao.Should().Be(SituacaoPagamento.EstornoBaixado);
    }

    public static TheoryData<string> Finais => new() { "baixado", "estornado" };

    [Theory]
    [MemberData(nameof(Finais))]
    public void Fora_de_estorno_pendente_nenhuma_acao_e_aceita(string final)
    {
        var pagamento = final == "baixado" ? Baixado() : Estornado();
        var situacao = pagamento.Situacao;

        Action[] acoes =
        [
            () => pagamento.PedirTentativaEstorno(T3),
            () => pagamento.RegistrarFalhaEstorno(T3, "Erro inesperado: Exception."),
            () => pagamento.MarcarEstornoAtrasado(T3),
            () => pagamento.ConfirmarEstorno("SIM-EST-2", T3),
            () => pagamento.DarBaixaEstorno(Autor, MotivoValido, null, T3)
        ];

        foreach (var acao in acoes)
        {
            acao.Should().Throw<InvalidOperationException>();
        }

        pagamento.Situacao.Should().Be(situacao);
        pagamento.TentativasEstorno.Should().Be(0);
        pagamento.TentativaEstornoForcadaEm.Should().BeNull();
        pagamento.EstornoAtrasadoEm.Should().BeNull();
    }
}
