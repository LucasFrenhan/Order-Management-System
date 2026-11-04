using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>A remoção das contagens vencidas, no SQL Server real (REQ-23, FR-011, R-07).</summary>
public sealed class LimpezaContagensLimiteTestes(BancoDadosTeste banco)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<string> NovoBancoAsync()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        return await banco.NovoBancoMigradoAsync();
    }

    private static LimpezaContagensLimite Criar(string stringConexao, OpcoesLimpeza opcoes)
    {
        var servicos = new ServiceCollection().AddDbContext<ContextoBancoDados>(o => o.UseSqlServer(stringConexao)).BuildServiceProvider();
        return new LimpezaContagensLimite(
            servicos.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(opcoes),
            Options.Create(new OpcoesSqlServer { StringConexao = stringConexao }),
            TimeProvider.System,
            NullLogger<LimpezaContagensLimite>.Instance);
    }

    /// <summary>Linhas vencidas há 1 minuto (<paramref name="segundos"/> negativo) ou dentro da janela.</summary>
    private static Task InserirAsync(string stringConexao, int quantidade, int segundos) => BancoDadosTeste.ExecutarAsync(stringConexao, $"""
        ;WITH n AS (SELECT TOP ({quantidade}) ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) i FROM sys.all_objects)
        INSERT ContagensLimite (Chave, Contador, FimJanela, BloqueioRegistrado)
        SELECT HASHBYTES('SHA2_256', CAST(NEWID() AS varchar(36))), 1, DATEADD(SECOND, {segundos}, SYSUTCDATETIME()), 0 FROM n;
        """);

    private static Task<int> ContarAsync(string stringConexao) => BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM ContagensLimite");

    [Fact]
    public async Task Apaga_as_vencidas_e_mantem_as_da_janela_atual()
    {
        var conexao = await NovoBancoAsync();
        await InserirAsync(conexao, 5, -60);
        await InserirAsync(conexao, 3, 3600);

        await Criar(conexao, new OpcoesLimpeza()).ExecutarRodadaAsync(new OpcoesLimpeza(), Cancelamento);

        (await ContarAsync(conexao)).Should().Be(3);
    }

    [Fact]
    public async Task Para_em_LotesContagensPorRodada()
    {
        var conexao = await NovoBancoAsync();
        await InserirAsync(conexao, 7, -60);
        var opcoes = new OpcoesLimpeza { TamanhoLote = 2, LotesContagensPorRodada = 2 };

        await Criar(conexao, opcoes).ExecutarRodadaAsync(opcoes, Cancelamento);

        (await ContarAsync(conexao)).Should().Be(3);
    }

    [Fact]
    public async Task Outra_instancia_com_o_lock_pula_a_rodada()
    {
        var conexao = await NovoBancoAsync();
        await InserirAsync(conexao, 2, -60);
        await using var outraInstancia = new SqlConnection(conexao);
        await outraInstancia.OpenAsync(Cancelamento);
        (await LockAplicacao.AdquirirAsync(outraInstancia, LimpezaContagensLimite.RecursoLock, Cancelamento)).Should().BeTrue();

        await Criar(conexao, new OpcoesLimpeza()).ExecutarRodadaAsync(new OpcoesLimpeza(), Cancelamento);

        (await ContarAsync(conexao)).Should().Be(2);
    }

    [Fact]
    public async Task Desligada_por_Limpeza_Habilitada_nao_apaga()
    {
        var conexao = await NovoBancoAsync();
        await InserirAsync(conexao, 2, -60);
        var limpeza = Criar(conexao, new OpcoesLimpeza { Habilitada = false });

        await limpeza.StartAsync(Cancelamento);
        await limpeza.ExecuteTask!;
        await limpeza.StopAsync(Cancelamento);

        (await ContarAsync(conexao)).Should().Be(2);
    }

    [Fact]
    public async Task Com_a_limpeza_rodando_a_contagem_continua_atomica()
    {
        var conexao = await NovoBancoAsync();
        await InserirAsync(conexao, 500, -60);
        var armazenamento = new ArmazenamentoContagensSql(Options.Create(new OpcoesSqlServer { StringConexao = conexao }));
        var opcoes = new OpcoesLimpeza { TamanhoLote = 50, LotesContagensPorRodada = 20 };
        var limpeza = Criar(conexao, opcoes).ExecutarRodadaAsync(opcoes, Cancelamento);
        var chave = RandomNumberGenerator.GetBytes(32);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(
            () => armazenamento.ContarAsync([new ContagemSolicitada(chave, 10, 3600)], Cancelamento), Cancelamento)));
        await limpeza;

        resultados.Count(r => r.Count == 0).Should().Be(10);
        (await ContarAsync(conexao)).Should().Be(1, "só a contagem da janela atual fica");
    }
}
