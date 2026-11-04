using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.LimiteRequisicoes;

/// <summary>Validação das opções na partida (REQ-23, FR-018, FR-019, contrato › Configuração).</summary>
public sealed class ValidadorOpcoesLimiteRequisicoesTestes
{
    private const string ChaveValida = "Y2hhdmUtZGUtdGVzdGUtYXV0b21hdGl6YWRvLTMyYnk=";

    private static OpcoesLimiteRequisicoes Validas() => new() { ChaveContagem = ChaveValida };

    private static string[] Falhas(OpcoesLimiteRequisicoes opcoes)
    {
        var resultado = new ValidadorOpcoesLimiteRequisicoes().Validate(null, opcoes);
        return resultado.Failed ? [.. resultado.Failures!] : [];
    }

    [Fact]
    public void Os_padroes_do_contrato_sao_validos()
    {
        var opcoes = Validas();

        Falhas(opcoes).Should().BeEmpty();
        (opcoes.LoginOrigemEmail.Limite, opcoes.LoginOrigemEmail.JanelaSegundos).Should().Be((10, 60));
        (opcoes.LoginOrigem.Limite, opcoes.LoginOrigem.JanelaSegundos).Should().Be((100, 60));
        (opcoes.Registro.Limite, opcoes.Registro.JanelaSegundos).Should().Be((20, 3600));
        opcoes.TempoMaximoEsperaMs.Should().Be(250);
        opcoes.PausaAposFalhaSegundos.Should().Be(5);
        opcoes.IntermediariosConfiaveis.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-base64!")]
    [InlineData("Y3VydGE=")]
    public void Chave_ausente_ou_que_nao_tem_32_bytes_e_recusada_sem_mostrar_o_valor(string chave)
    {
        var opcoes = Validas();
        opcoes.ChaveContagem = chave;

        var falha = Falhas(opcoes).Should().ContainSingle().Subject;

        falha.Should().Contain("LimiteRequisicoes:ChaveContagem");
        if (chave.Length > 0)
        {
            falha.Should().NotContain(chave);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1_000_000, true)]
    [InlineData(1_000_001, false)]
    public void Limite_de_cada_politica_respeita_a_faixa(int valor, bool valido)
    {
        var opcoes = Validas();
        opcoes.LoginOrigemEmail.Limite = valor;
        opcoes.LoginOrigem.Limite = valor;
        opcoes.Registro.Limite = valor;

        var falhas = Falhas(opcoes);

        if (valido)
        {
            falhas.Should().BeEmpty();
        }
        else
        {
            falhas.Should().HaveCount(3).And.Contain(f => f.Contains("LimiteRequisicoes:Registro:Limite"));
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(86_400, true)]
    [InlineData(86_401, false)]
    public void Janela_de_cada_politica_respeita_a_faixa(int valor, bool valido)
    {
        var opcoes = Validas();
        opcoes.LoginOrigemEmail.JanelaSegundos = valor;
        opcoes.LoginOrigem.JanelaSegundos = valor;
        opcoes.Registro.JanelaSegundos = valor;

        var falhas = Falhas(opcoes);

        if (valido)
        {
            falhas.Should().BeEmpty();
        }
        else
        {
            falhas.Should().HaveCount(3).And.Contain(f => f.Contains("LimiteRequisicoes:LoginOrigem:JanelaSegundos"));
        }
    }

    [Theory]
    [InlineData(49, false)]
    [InlineData(50, true)]
    [InlineData(5000, true)]
    [InlineData(5001, false)]
    public void Tempo_maximo_respeita_a_faixa(int valor, bool valido)
    {
        var opcoes = Validas();
        opcoes.TempoMaximoEsperaMs = valor;

        Falhas(opcoes).Should().HaveCount(valido ? 0 : 1);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(0, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public void Pausa_respeita_a_faixa(int valor, bool valido)
    {
        var opcoes = Validas();
        opcoes.PausaAposFalhaSegundos = valor;

        Falhas(opcoes).Should().HaveCount(valido ? 0 : 1);
    }

    [Theory]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::1")]
    [InlineData("10.0.0.0/8")]
    [InlineData("2001:db8::/32")]
    public void Aceita_enderecos_e_faixas_de_intermediarios(string valor)
    {
        var opcoes = Validas();
        opcoes.IntermediariosConfiaveis.Add(valor);

        Falhas(opcoes).Should().BeEmpty();
    }

    [Theory]
    [InlineData("balanceador.local")]
    [InlineData("10.0.0.0/99")]
    [InlineData("")]
    public void Recusa_intermediario_invalido_citando_a_posicao(string valor)
    {
        var opcoes = Validas();
        opcoes.IntermediariosConfiaveis.Add("10.0.0.1");
        opcoes.IntermediariosConfiaveis.Add(valor);

        Falhas(opcoes).Should().ContainSingle().Which.Should().Contain("LimiteRequisicoes:IntermediariosConfiaveis:1");
    }

    [Fact]
    public void Acumula_todas_as_falhas()
    {
        var opcoes = new OpcoesLimiteRequisicoes { TempoMaximoEsperaMs = 1, PausaAposFalhaSegundos = -1 };
        opcoes.Registro.Limite = 0;

        Falhas(opcoes).Should().HaveCount(4);
    }
}
