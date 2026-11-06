using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Emails;

/// <summary>Validação das opções de envio na partida (REQ-24, FR-023, R-04).</summary>
public sealed class ValidadorOpcoesEmailTestes
{
    private const string SenhaSecreta = "senha-que-nao-pode-aparecer";

    private static OpcoesEmail Validas() => new() { Servidor = "smtp.exemplo.com.br", Remetente = "oms@exemplo.com.br", Senha = SenhaSecreta };

    private static string[] Falhas(OpcoesEmail opcoes, string ambiente = "Production")
    {
        var resultado = new ValidadorOpcoesEmail(ambiente).Validate(null, opcoes);
        return resultado.Failed ? [.. resultado.Failures!] : [];
    }

    [Fact]
    public void Os_padroes_com_servidor_e_remetente_sao_validos()
    {
        var opcoes = Validas();

        Falhas(opcoes).Should().BeEmpty();
        opcoes.Habilitado.Should().BeTrue();
        opcoes.Porta.Should().Be(587);
        opcoes.Seguranca.Should().Be("StartTls");
        opcoes.ValidadeRecuperacao.Should().Be(TimeSpan.FromMinutes(30));
        opcoes.ValidadeConfirmacao.Should().Be(TimeSpan.FromHours(24));
        opcoes.TempoLimite.Should().Be(TimeSpan.FromSeconds(10));
        opcoes.Intervalo.Should().Be(TimeSpan.FromSeconds(10));
        opcoes.TamanhoLote.Should().Be(50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(721)]
    public void Validade_do_convite_fora_da_faixa_cita_a_chave(int horas)
    {
        var opcoes = Validas();
        opcoes.ValidadeConviteHoras = horas;

        Falhas(opcoes).Should().ContainSingle().Which.Should().Contain("ValidadeConviteHoras");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(720)]
    public void Validade_do_convite_na_faixa_e_aceita(int horas)
    {
        var opcoes = Validas();
        opcoes.ValidadeConviteHoras = horas;

        Falhas(opcoes).Should().BeEmpty();
        opcoes.ValidadeConvite.Should().Be(TimeSpan.FromHours(horas));
    }

    [Fact]
    public void A_validade_padrao_do_convite_e_de_72_horas() => Validas().ValidadeConvite.Should().Be(TimeSpan.FromHours(72));

    [Fact]
    public void Desligado_nao_exige_servidor_nem_remetente() =>
        Falhas(new OpcoesEmail { Habilitado = false }).Should().BeEmpty();

    [Fact]
    public void Ligado_exige_servidor_e_remetente_e_mostra_todas_as_falhas_juntas() =>
        Falhas(new OpcoesEmail()).Should().HaveCount(2)
            .And.Contain(f => f.Contains("Email:Servidor"))
            .And.Contain(f => f.Contains("Email:Remetente"));

    [Fact]
    public void Remetente_mal_formado_e_recusado_sem_mostrar_o_valor()
    {
        var opcoes = Validas();
        opcoes.Remetente = "nao-e-um-email";

        Falhas(opcoes).Should().ContainSingle().Which.Should().Contain("Email:Remetente").And.NotContain("nao-e-um-email");
    }

    [Theory]
    [InlineData(nameof(OpcoesEmail.Porta), 0)]
    [InlineData(nameof(OpcoesEmail.Porta), 65536)]
    [InlineData(nameof(OpcoesEmail.ValidadeRecuperacaoMinutos), 4)]
    [InlineData(nameof(OpcoesEmail.ValidadeRecuperacaoMinutos), 1441)]
    [InlineData(nameof(OpcoesEmail.ValidadeConfirmacaoHoras), 0)]
    [InlineData(nameof(OpcoesEmail.ValidadeConfirmacaoHoras), 169)]
    [InlineData(nameof(OpcoesEmail.TempoLimiteSegundos), 0)]
    [InlineData(nameof(OpcoesEmail.TempoLimiteSegundos), 61)]
    [InlineData(nameof(OpcoesEmail.IntervaloSegundos), 0)]
    [InlineData(nameof(OpcoesEmail.IntervaloSegundos), 301)]
    [InlineData(nameof(OpcoesEmail.TamanhoLote), 0)]
    [InlineData(nameof(OpcoesEmail.TamanhoLote), 1001)]
    public void Cada_faixa_e_recusada_nas_pontas(string chave, int valor)
    {
        var opcoes = Validas();
        typeof(OpcoesEmail).GetProperty(chave)!.SetValue(opcoes, valor);

        var falha = Falhas(opcoes).Should().ContainSingle().Subject;

        falha.Should().Contain($"Email:{chave}").And.NotContain(SenhaSecreta);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Testes")]
    public void Sem_seguranca_so_em_desenvolvimento_e_testes(string ambiente)
    {
        var opcoes = Validas();
        opcoes.Seguranca = OpcoesEmail.SegurancaNenhuma;

        Falhas(opcoes, ambiente).Should().BeEmpty();
        Falhas(opcoes).Should().ContainSingle().Which.Should().Contain("Email:Seguranca");
    }

    [Theory]
    [InlineData(OpcoesEmail.SegurancaStartTls)]
    [InlineData(OpcoesEmail.SegurancaSslOnConnect)]
    public void As_opcoes_seguras_valem_em_producao(string seguranca)
    {
        var opcoes = Validas();
        opcoes.Seguranca = seguranca;

        Falhas(opcoes).Should().BeEmpty();
    }

    [Fact]
    public void Seguranca_desconhecida_e_recusada_sem_mostrar_o_valor()
    {
        var opcoes = Validas();
        opcoes.Seguranca = "valor-estranho";

        Falhas(opcoes).Should().ContainSingle().Which.Should().Contain("Email:Seguranca").And.NotContain("valor-estranho");
    }
}
