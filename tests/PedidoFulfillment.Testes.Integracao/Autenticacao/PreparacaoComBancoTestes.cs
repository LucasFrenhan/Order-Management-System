using FluentAssertions;
using Microsoft.Data.SqlClient;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// Preparação da inicialização com SQL Server real (FR-011, FR-017).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class PreparacaoComBancoTestes(BancoDadosTeste banco)
{
    private const string ContarPerfis = "SELECT COUNT(*) FROM Perfis";

    [Fact]
    public async Task Banco_novo_recebe_a_migration_e_os_tres_perfis()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();

        await IniciarAsync(stringConexao);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            "SELECT COUNT(*) FROM __EFMigrationsHistory WHERE MigrationId LIKE '%_InicialIdentidade'")).Should().Be(1);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            "SELECT STRING_AGG(Name, ',') WITHIN GROUP (ORDER BY Name) FROM Perfis")).Should().Be("Admin,Cliente,OperadorLogistica");
    }

    [Fact]
    public async Task Reiniciar_a_api_nao_duplica_perfis()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();

        await IniciarAsync(stringConexao);
        await IniciarAsync(stringConexao);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPerfis)).Should().Be(3);
    }

    [Fact]
    public async Task Duas_instancias_iniciando_juntas_deixam_tres_perfis()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        // Migration antes: o teste disputa só a criação dos perfis, não a do banco (analyze M1).
        await BancoDadosTeste.AplicarMigrationsAsync(stringConexao);

        var cancelamento = TestContext.Current.CancellationToken;
        await Task.WhenAll(Task.Run(() => IniciarAsync(stringConexao), cancelamento), Task.Run(() => IniciarAsync(stringConexao), cancelamento));

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarPerfis)).Should().Be(3);
    }

    [Fact]
    public async Task Fora_de_development_com_migration_pendente_a_api_nao_inicia_nem_altera_o_banco()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await using var fabrica = new FabricaApi { Ambiente = "Production", PrepararBanco = true, StringConexao = stringConexao };

        var excecao = Record.Exception(() => fabrica.CreateClient());

        excecao.Should().NotBeNull();
        PreparacaoInicializacaoTestes.Cadeia(excecao!).Should().Contain(e => e.Message ==
            "O banco de dados tem migrations pendentes. Aplique-as com o comando documentado antes de iniciar a API.");
        var nomeBanco = new SqlConnectionStringBuilder(stringConexao).InitialCatalog;
        (await banco.ConsultarServidorAsync<object>($"SELECT DB_ID('{nomeBanco}')")).Should().Be(DBNull.Value);
    }

    private static async Task IniciarAsync(string stringConexao)
    {
        await using var fabrica = new FabricaApi { PrepararBanco = true, StringConexao = stringConexao };
        fabrica.CreateClient().Dispose();
    }
}
