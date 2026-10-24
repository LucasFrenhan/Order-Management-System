using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Validação da seção Reconciliacao na partida (REQ-12, R-10, FR-014).</summary>
public sealed class OpcoesReconciliacaoTestes
{
    private static readonly ValidadorOpcoesReconciliacao Validador = new();

    [Fact]
    public void Os_padroes_sao_validos()
    {
        var opcoes = new OpcoesReconciliacao();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
        opcoes.Habilitada.Should().BeTrue();
        (opcoes.IntervaloCiclo, opcoes.LimiteParado, opcoes.LimiteEsgotamento).Should()
            .Be((TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10), TimeSpan.FromHours(24)));
        (opcoes.CiclosFalhaEsgotamento, opcoes.TamanhoLote).Should().Be((3, 100));
    }

    [Theory]
    [InlineData(nameof(OpcoesReconciliacao.IntervaloCicloSegundos), 1, 86400)]
    [InlineData(nameof(OpcoesReconciliacao.CiclosFalhaEsgotamento), 1, 100)]
    [InlineData(nameof(OpcoesReconciliacao.TamanhoLote), 1, 1000)]
    public void Limites_das_faixas_sao_aceitos_e_fora_deles_falham(string chave, int minimo, int maximo)
    {
        Validar(chave, minimo).Should().BeEmpty();
        Validar(chave, maximo).Should().BeEmpty();
        Validar(chave, minimo - 1).Should().Equal($"Reconciliacao:{chave} deve estar entre {minimo} e {maximo}.");
        Validar(chave, maximo + 1).Should().Equal($"Reconciliacao:{chave} deve estar entre {minimo} e {maximo}.");
    }

    [Fact]
    public void Limite_parado_vai_de_1_a_604800()
    {
        Valida(new OpcoesReconciliacao { LimiteParadoSegundos = 1 }).Should().BeEmpty();
        Valida(new OpcoesReconciliacao { LimiteParadoSegundos = 604800, LimiteEsgotamentoSegundos = 604801 }).Should().BeEmpty();
        Valida(new OpcoesReconciliacao { LimiteParadoSegundos = 0 }).Should()
            .Equal("Reconciliacao:LimiteParadoSegundos deve estar entre 1 e 604800.");
        Valida(new OpcoesReconciliacao { LimiteParadoSegundos = 604801, LimiteEsgotamentoSegundos = 604802 }).Should()
            .Equal("Reconciliacao:LimiteParadoSegundos deve estar entre 1 e 604800.");
    }

    [Fact]
    public void Limite_de_esgotamento_vai_ate_2592000()
    {
        Valida(new OpcoesReconciliacao { LimiteEsgotamentoSegundos = 2592000 }).Should().BeEmpty();
        Valida(new OpcoesReconciliacao { LimiteEsgotamentoSegundos = 2592001 }).Should()
            .Equal("Reconciliacao:LimiteEsgotamentoSegundos deve estar entre 1 e 2592000.");
    }

    [Theory]
    [InlineData(600)]
    [InlineData(599)]
    public void Limite_de_esgotamento_precisa_ser_maior_que_o_limite_parado(int esgotamento)
    {
        Valida(new OpcoesReconciliacao { LimiteParadoSegundos = 600, LimiteEsgotamentoSegundos = esgotamento }).Should()
            .Equal("Reconciliacao:LimiteEsgotamentoSegundos deve ser maior que Reconciliacao:LimiteParadoSegundos.");
    }

    [Fact]
    public void Varias_falhas_sao_acumuladas_numa_so_validacao()
    {
        var opcoes = new OpcoesReconciliacao { IntervaloCicloSegundos = 0, TamanhoLote = 0, CiclosFalhaEsgotamento = 0 };

        Valida(opcoes).Should().BeEquivalentTo(
            "Reconciliacao:IntervaloCicloSegundos deve estar entre 1 e 86400.",
            "Reconciliacao:TamanhoLote deve estar entre 1 e 1000.",
            "Reconciliacao:CiclosFalhaEsgotamento deve estar entre 1 e 100.");
    }

    private static IEnumerable<string> Validar(string chave, int valor)
    {
        var opcoes = new OpcoesReconciliacao();
        typeof(OpcoesReconciliacao).GetProperty(chave)!.SetValue(opcoes, valor);
        return Valida(opcoes);
    }

    private static IEnumerable<string> Valida(OpcoesReconciliacao opcoes) => Validador.Validate(null, opcoes).Failures ?? [];
}
