using FluentAssertions;
using Microsoft.Extensions.Configuration;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Validação da seção Observabilidade na partida e regra de ligar (REQ-16, data-model › Configuração).</summary>
public sealed class OpcoesObservabilidadeTestes
{
    private static readonly ValidadorOpcoesObservabilidade Validador = new();

    [Fact]
    public void Os_padroes_sao_validos()
    {
        var opcoes = new OpcoesObservabilidade();

        Validador.Validate(null, opcoes).Succeeded.Should().BeTrue();
        (opcoes.Habilitada, opcoes.TaxaAmostragem, opcoes.IntervaloAmostragemOutbox).Should()
            .Be((false, 1.0, TimeSpan.FromSeconds(30)));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.25)]
    [InlineData(1.0)]
    public void Taxa_dentro_da_faixa_e_aceita(double taxa)
    {
        Validador.Validate(null, new OpcoesObservabilidade { TaxaAmostragem = taxa }).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(-0.01)]
    [InlineData(1.01)]
    [InlineData(double.NaN)]
    public void Taxa_fora_da_faixa_e_recusada(double taxa)
    {
        Validador.Validate(null, new OpcoesObservabilidade { TaxaAmostragem = taxa }).Failures.Should()
            .Equal("Observabilidade:TaxaAmostragem deve estar entre 0 e 1.");
    }

    [Theory]
    [InlineData(5, true)]
    [InlineData(300, true)]
    [InlineData(4, false)]
    [InlineData(301, false)]
    public void Intervalo_do_outbox_tem_faixa(int segundos, bool valido)
    {
        var resultado = Validador.Validate(null, new OpcoesObservabilidade { IntervaloAmostragemOutboxSegundos = segundos });

        if (valido)
        {
            resultado.Succeeded.Should().BeTrue();
        }
        else
        {
            resultado.Failures.Should().Equal("Observabilidade:IntervaloAmostragemOutboxSegundos deve estar entre 5 e 300.");
        }
    }

    [Fact]
    public void Falhas_sao_acumuladas()
    {
        Validador.Validate(null, new OpcoesObservabilidade { TaxaAmostragem = 2, IntervaloAmostragemOutboxSegundos = 0 })
            .Failures.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(null, null, false)]
    [InlineData("", null, false)]
    [InlineData("http://localhost:4317", null, true)]
    [InlineData(null, "true", true)]
    [InlineData(null, "false", false)]
    public void Liga_com_destino_ou_com_a_chave(string? destino, string? habilitada, bool esperado)
    {
        var configuracao = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = destino,
                ["Observabilidade:Habilitada"] = habilitada
            })
            .Build();

        OpcoesObservabilidade.Ligada(configuracao).Should().Be(esperado);
    }
}
