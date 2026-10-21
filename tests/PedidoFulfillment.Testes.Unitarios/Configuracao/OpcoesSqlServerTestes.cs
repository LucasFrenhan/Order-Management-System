using FluentAssertions;
using PedidoFulfillment.Infrastructure.Configuracao;

namespace PedidoFulfillment.Testes.Unitarios.Configuracao;

public sealed class OpcoesSqlServerTestes
{
    private static readonly ValidadorOpcoesSqlServer Validador = new();

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void String_de_conexao_ausente_falha_nomeando_a_chave(string? valor)
    {
        var resultado = Validador.Validate(null, new OpcoesSqlServer { StringConexao = valor! });

        resultado.Failed.Should().BeTrue();
        resultado.Failures.Should().Equal("A configuração ConnectionStrings:SqlServer é obrigatória.");
    }

    [Fact]
    public void String_de_conexao_preenchida_e_valida()
    {
        var resultado = Validador.Validate(null, new OpcoesSqlServer { StringConexao = "Server=localhost,1433;Password=segredo" });

        resultado.Succeeded.Should().BeTrue();
    }
}
