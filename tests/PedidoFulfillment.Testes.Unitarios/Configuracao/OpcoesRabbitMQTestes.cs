using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

public sealed class OpcoesRabbitMQTestes
{
    private static readonly ValidadorOpcoesRabbitMQ Validador = new();

    private static OpcoesRabbitMQ Validas() => new()
    {
        Servidor = "localhost",
        Porta = 5672,
        Usuario = "oms",
        Senha = "oms-dev-local",
        VirtualHost = "/"
    };

    [Fact]
    public void Opcoes_completas_sao_validas()
    {
        Validador.Validate(null, Validas()).Succeeded.Should().BeTrue();
    }

    [Theory]
    [InlineData(nameof(OpcoesRabbitMQ.Servidor))]
    [InlineData(nameof(OpcoesRabbitMQ.Usuario))]
    [InlineData(nameof(OpcoesRabbitMQ.Senha))]
    [InlineData(nameof(OpcoesRabbitMQ.VirtualHost))]
    public void Campo_texto_vazio_falha_nomeando_o_campo(string campo)
    {
        var opcoes = Validas();
        typeof(OpcoesRabbitMQ).GetProperty(campo)!.SetValue(opcoes, " ");

        var resultado = Validador.Validate(null, opcoes);

        resultado.Failures.Should().Equal($"A configuração RabbitMQ:{campo} é obrigatória.");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(65536)]
    public void Porta_fora_do_intervalo_falha(int porta)
    {
        var opcoes = Validas();
        opcoes.Porta = porta;

        Validador.Validate(null, opcoes).Failures.Should().Equal("RabbitMQ:Porta deve estar entre 1 e 65535.");
    }

    [Fact]
    public void Todas_as_falhas_aparecem_sem_expor_valores()
    {
        var opcoes = Validas();
        opcoes.Usuario = "usuario-secreto";
        opcoes.Servidor = "";
        opcoes.Senha = "";
        opcoes.Porta = 0;

        var falhas = Validador.Validate(null, opcoes).Failures;

        falhas.Should().BeEquivalentTo(
            "A configuração RabbitMQ:Servidor é obrigatória.",
            "A configuração RabbitMQ:Senha é obrigatória.",
            "RabbitMQ:Porta deve estar entre 1 e 65535.");
        string.Concat(falhas).Should().NotContain("usuario-secreto");
    }
}
