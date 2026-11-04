using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>Configuração inválida impede a partida, citando a chave e sem o valor (REQ-23, FR-018).</summary>
public sealed class ConfiguracaoLimiteTestes
{
    private const string ValorProibido = "nao-e-base64-e-nao-pode-aparecer";

    [Theory]
    [InlineData("Testes")]
    [InlineData("Production")]
    public async Task Chave_invalida_impede_a_partida_sem_mostrar_o_valor(string ambiente)
    {
        await using var fabrica = new FabricaApi
        {
            Ambiente = ambiente,
            Configuracoes = new Dictionary<string, string?> { ["LimiteRequisicoes:ChaveContagem"] = ValorProibido }
        };

        var mensagem = FalhaDaPartida(fabrica);

        mensagem.Should().Contain("LimiteRequisicoes:ChaveContagem").And.NotContain(ValorProibido);
    }

    [Theory]
    [InlineData("Testes")]
    [InlineData("Production")]
    public async Task Chave_ausente_impede_a_partida(string ambiente)
    {
        await using var fabrica = new FabricaApi
        {
            Ambiente = ambiente,
            Configuracoes = new Dictionary<string, string?> { ["LimiteRequisicoes:ChaveContagem"] = "" }
        };

        FalhaDaPartida(fabrica).Should().Contain("LimiteRequisicoes:ChaveContagem");
    }

    [Theory]
    [InlineData("LimiteRequisicoes:LoginOrigemEmail:Limite", "0")]
    [InlineData("LimiteRequisicoes:LoginOrigem:JanelaSegundos", "-1")]
    [InlineData("LimiteRequisicoes:Registro:Limite", "1000001")]
    [InlineData("LimiteRequisicoes:TempoMaximoEsperaMs", "1")]
    [InlineData("LimiteRequisicoes:PausaAposFalhaSegundos", "301")]
    [InlineData("LimiteRequisicoes:IntermediariosConfiaveis:0", "balanceador.local")]
    public async Task Valor_fora_da_faixa_impede_a_partida(string chave, string valor)
    {
        await using var fabrica = new FabricaApi { Configuracoes = new Dictionary<string, string?> { [chave] = valor } };

        FalhaDaPartida(fabrica).Should().Contain(chave);
    }

    private static string FalhaDaPartida(FabricaApi fabrica)
    {
        var excecao = Record.Exception(() => fabrica.CreateClient());
        excecao.Should().NotBeNull();
        return excecao!.ToString();
    }
}
