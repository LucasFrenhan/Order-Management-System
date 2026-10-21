using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Configuracao;

[CollectionDefinition(nameof(ColecaoVariaveisAmbiente), DisableParallelization = true)]
public sealed class ColecaoVariaveisAmbiente;

/// <summary>
/// Origem e validação das configurações de banco e broker (FR-010 a FR-013). Roda isolado
/// porque mexe em variáveis de ambiente do processo.
/// </summary>
[Collection(nameof(ColecaoVariaveisAmbiente))]
public sealed class ConfiguracaoAusenteTestes
{
    [Fact]
    public async Task Fora_de_development_sem_configuracao_a_api_nao_inicia_e_nomeia_as_chaves()
    {
        await using var fabrica = new FabricaApi { Ambiente = "Production", ComConfiguracaoInfraestrutura = false };

        var excecao = Record.Exception(() => fabrica.CreateClient());

        excecao.Should().NotBeNull();

        // Com as duas opções inválidas, a inicialização lança um AggregateException com uma
        // OptionsValidationException por opção; cada uma é verificada separadamente.
        var falhas = ExcecoesValidacao(excecao!).ToArray();
        falhas.Should().ContainSingle(e => e.OptionsType == typeof(OpcoesSqlServer))
            .Which.Failures.Should().Contain("A configuração ConnectionStrings:SqlServer é obrigatória.");
        falhas.Should().ContainSingle(e => e.OptionsType == typeof(OpcoesRabbitMQ))
            .Which.Failures.Should().Contain("A configuração RabbitMQ:Servidor é obrigatória.");
        foreach (var falha in falhas)
        {
            falha.Message.Should().NotContain("Password=").And.NotContain("oms-dev-local");
        }
    }

    [Fact]
    public async Task Em_development_os_valores_vem_do_arquivo_de_desenvolvimento()
    {
        await using var fabrica = new FabricaApi { Ambiente = "Development", ComConfiguracaoInfraestrutura = false };

        var rabbit = fabrica.Services.GetRequiredService<IOptions<OpcoesRabbitMQ>>().Value;
        var sql = fabrica.Services.GetRequiredService<IOptions<OpcoesSqlServer>>().Value;

        rabbit.Servidor.Should().Be("localhost");
        rabbit.Porta.Should().Be(5672);
        rabbit.Usuario.Should().Be("oms");
        sql.StringConexao.Should().Contain("Server=localhost,1433").And.Contain("Database=PedidoFulfillment");
    }

    [Fact]
    public async Task Variaveis_de_ambiente_prevalecem_sobre_o_arquivo()
    {
        const string stringConexao = "Server=servidor-da-variavel;Database=PedidoFulfillment";
        Environment.SetEnvironmentVariable("RabbitMQ__Servidor", "servidor-da-variavel");
        Environment.SetEnvironmentVariable("ConnectionStrings__SqlServer", stringConexao);
        try
        {
            await using var fabrica = new FabricaApi { Ambiente = "Development", ComConfiguracaoInfraestrutura = false };

            fabrica.Services.GetRequiredService<IOptions<OpcoesRabbitMQ>>().Value.Servidor.Should().Be("servidor-da-variavel");
            fabrica.Services.GetRequiredService<IOptions<OpcoesSqlServer>>().Value.StringConexao.Should().Be(stringConexao);
        }
        finally
        {
            Environment.SetEnvironmentVariable("RabbitMQ__Servidor", null);
            Environment.SetEnvironmentVariable("ConnectionStrings__SqlServer", null);
        }
    }

    private static IEnumerable<OptionsValidationException> ExcecoesValidacao(Exception excecao) => excecao switch
    {
        OptionsValidationException validacao => [validacao],
        AggregateException agregada => agregada.InnerExceptions.SelectMany(ExcecoesValidacao),
        { InnerException: { } interna } => ExcecoesValidacao(interna),
        _ => []
    };
}
