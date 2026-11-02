using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

/// <summary>Validação da seção Notificacoes na partida (REQ-18, research R-18; analyze A1).</summary>
public sealed class OpcoesNotificacoesTestes
{
    private static readonly string ChaveValida = Convert.ToBase64String(new byte[32]);

    [Fact]
    public void Os_padroes_com_a_chave_sao_validos()
    {
        var opcoes = new OpcoesNotificacoes { ChaveCifra = ChaveValida };

        Falhas(opcoes).Should().BeEmpty();
        (opcoes.DestinosPorCliente, opcoes.DestinosGlobais).Should().Be((5, 5));
        (opcoes.Envio.Intervalo, opcoes.Envio.TempoLimite, opcoes.Envio.Limite, opcoes.Envio.Paralelismo)
            .Should().Be((TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10), TimeSpan.FromHours(24), 8));
        (opcoes.Sse.IntervaloLeitura, opcoes.Sse.SinalDeVida, opcoes.Sse.ConexoesPorUsuario, opcoes.Sse.MaximoReenvio)
            .Should().Be((TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30), 3, 500));
        opcoes.PermitirRedeLocal.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("nao-e-base64")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAA==")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void Chave_ausente_invalida_ou_com_outro_tamanho_falha_sem_mostrar_o_valor(string chave)
    {
        Falhas(new OpcoesNotificacoes { ChaveCifra = chave }).Should().Equal("Notificacoes:ChaveCifra deve ser o Base64 de 32 bytes.");
    }

    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testes", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void Rede_local_so_e_aceita_em_Development_e_nos_testes(string ambiente, bool aceita)
    {
        var falhas = Falhas(new OpcoesNotificacoes { ChaveCifra = ChaveValida, PermitirRedeLocal = true }, ambiente);

        if (aceita)
        {
            falhas.Should().BeEmpty();
        }
        else
        {
            falhas.Should().Equal("Notificacoes:PermitirRedeLocal só é aceito em Development e nos testes.");
        }
    }

    [Theory]
    [InlineData("DestinosPorCliente", 1, 50)]
    [InlineData("DestinosGlobais", 1, 50)]
    [InlineData("Envio:IntervaloSegundos", 1, 300)]
    [InlineData("Envio:TempoLimiteSegundos", 1, 60)]
    [InlineData("Envio:Paralelismo", 1, 64)]
    [InlineData("Envio:LimiteHoras", 1, 168)]
    [InlineData("Envio:TamanhoLote", 1, 10000)]
    [InlineData("Sse:IntervaloLeituraSegundos", 1, 60)]
    [InlineData("Sse:ConexoesPorUsuario", 1, 100)]
    [InlineData("Sse:SinalDeVidaSegundos", 5, 300)]
    [InlineData("Sse:MaximoReenvio", 1, 10000)]
    public void Limites_das_faixas_sao_aceitos_e_fora_deles_falham(string chave, int minimo, int maximo)
    {
        var esperado = $"Notificacoes:{chave} deve estar entre {minimo} e {maximo}.";

        Falhas(Com(chave, minimo)).Should().BeEmpty();
        Falhas(Com(chave, maximo)).Should().BeEmpty();
        Falhas(Com(chave, minimo - 1)).Should().Equal(esperado);
        Falhas(Com(chave, maximo + 1)).Should().Equal(esperado);
    }

    [Fact]
    public void Varias_falhas_sao_acumuladas_numa_so_validacao()
    {
        var opcoes = new OpcoesNotificacoes { ChaveCifra = "", DestinosGlobais = 0 };
        opcoes.Sse.ConexoesPorUsuario = 0;

        Falhas(opcoes).Should().HaveCount(3);
    }

    private static OpcoesNotificacoes Com(string chave, int valor)
    {
        var opcoes = new OpcoesNotificacoes { ChaveCifra = ChaveValida };
        switch (chave)
        {
            case "DestinosPorCliente": opcoes.DestinosPorCliente = valor; break;
            case "DestinosGlobais": opcoes.DestinosGlobais = valor; break;
            case "Envio:IntervaloSegundos": opcoes.Envio.IntervaloSegundos = valor; break;
            case "Envio:TempoLimiteSegundos": opcoes.Envio.TempoLimiteSegundos = valor; break;
            case "Envio:Paralelismo": opcoes.Envio.Paralelismo = valor; break;
            case "Envio:LimiteHoras": opcoes.Envio.LimiteHoras = valor; break;
            case "Envio:TamanhoLote": opcoes.Envio.TamanhoLote = valor; break;
            case "Sse:IntervaloLeituraSegundos": opcoes.Sse.IntervaloLeituraSegundos = valor; break;
            case "Sse:ConexoesPorUsuario": opcoes.Sse.ConexoesPorUsuario = valor; break;
            case "Sse:SinalDeVidaSegundos": opcoes.Sse.SinalDeVidaSegundos = valor; break;
            case "Sse:MaximoReenvio": opcoes.Sse.MaximoReenvio = valor; break;
            default: throw new ArgumentOutOfRangeException(nameof(chave));
        }

        return opcoes;
    }

    private static IEnumerable<string> Falhas(OpcoesNotificacoes opcoes, string ambiente = "Production") =>
        new ValidadorOpcoesNotificacoes(new Ambiente(ambiente)).Validate(null, opcoes).Failures ?? [];

    private sealed class Ambiente(string nome) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = nome;

        public string ApplicationName { get; set; } = "Testes";

        public string ContentRootPath { get; set; } = "";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
