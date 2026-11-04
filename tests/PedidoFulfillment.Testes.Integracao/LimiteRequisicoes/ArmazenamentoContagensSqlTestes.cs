using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.LimiteRequisicoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.LimiteRequisicoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>O estado único no SQL Server real (REQ-23, FR-002, FR-007, FR-008, FR-010, R-03).</summary>
public sealed class ArmazenamentoContagensSqlTestes(BancoDadosTeste banco)
{
    private const int JanelaLonga = 3600;

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task<(ArmazenamentoContagensSql Armazenamento, string StringConexao)> CriarAsync()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        return (Criar(stringConexao), stringConexao);
    }

    private static ArmazenamentoContagensSql Criar(string stringConexao) =>
        new(Options.Create(new OpcoesSqlServer { StringConexao = stringConexao }));

    private static byte[] Chave() => RandomNumberGenerator.GetBytes(32);

    private static ContagemSolicitada[] Uma(byte[] chave, int limite, int janela = JanelaLonga) => [new(chave, limite, janela)];

    [Fact]
    public async Task Aceita_ate_o_limite_e_recusa_a_seguinte()
    {
        var (armazenamento, _) = await CriarAsync();
        var chave = Chave();

        (await armazenamento.ContarAsync(Uma(chave, 2), Cancelamento)).Should().BeEmpty();
        (await armazenamento.ContarAsync(Uma(chave, 2), Cancelamento)).Should().BeEmpty();
        var recusas = await armazenamento.ContarAsync(Uma(chave, 2), Cancelamento);

        var recusa = recusas.Should().ContainSingle().Subject;
        recusa.Indice.Should().Be(0);
        recusa.Espera.Should().BeGreaterThan(TimeSpan.FromSeconds(JanelaLonga - 60)).And.BeLessThanOrEqualTo(TimeSpan.FromSeconds(JanelaLonga));
    }

    [Fact]
    public async Task Com_32_simultaneas_aceita_exatamente_o_limite()
    {
        var (armazenamento, _) = await CriarAsync();
        var chave = Chave();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 200).Select(_ => Task.Run(() => armazenamento.ContarAsync(Uma(chave, 25), Cancelamento), Cancelamento)));

        resultados.Count(r => r.Count == 0).Should().Be(25);
    }

    [Fact]
    public async Task Com_duas_contagens_simultaneas_a_contagem_compartilhada_para_no_limite()
    {
        var (armazenamento, _) = await CriarAsync();
        var origem = Chave();

        var resultados = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(
            () => armazenamento.ContarAsync([new(Chave(), 10, JanelaLonga), new(origem, 20, JanelaLonga)], Cancelamento), Cancelamento)));

        resultados.Count(r => r.Count == 0).Should().Be(20);
        resultados.Where(r => r.Count > 0).Should().OnlyContain(r => r.Single().Indice == 1);
    }

    [Fact]
    public async Task Barrada_por_uma_contagem_nao_consome_a_outra()
    {
        var (armazenamento, stringConexao) = await CriarAsync();
        var email = Chave();
        var origem = Chave();
        ContagemSolicitada[] contagens = [new(email, 1, JanelaLonga), new(origem, 2, JanelaLonga)];

        (await armazenamento.ContarAsync(contagens, Cancelamento)).Should().BeEmpty();
        for (var i = 0; i < 5; i++)
        {
            (await armazenamento.ContarAsync(contagens, Cancelamento)).Should().ContainSingle().Which.Indice.Should().Be(0);
        }

        (await Contador(stringConexao, origem)).Should().Be(1);
        (await armazenamento.ContarAsync([new(Chave(), 1, JanelaLonga), new(origem, 2, JanelaLonga)], Cancelamento)).Should().BeEmpty();
    }

    [Fact]
    public async Task Com_as_duas_excedidas_devolve_as_duas_recusas_nas_posicoes_pedidas()
    {
        var (armazenamento, _) = await CriarAsync();
        var a = Chave();
        var b = Chave();
        await armazenamento.ContarAsync([new(a, 1, 100), new(b, 1, 200)], Cancelamento);

        var recusas = await armazenamento.ContarAsync([new(a, 1, 100), new(b, 1, 200)], Cancelamento);

        recusas.Should().HaveCount(2);
        recusas.Single(r => r.Indice == 0).Espera.Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(100));
        recusas.Single(r => r.Indice == 1).Espera.Should().BeGreaterThan(TimeSpan.FromSeconds(100));
    }

    [Fact]
    public async Task Primeiro_bloqueio_so_uma_vez_por_janela()
    {
        var (armazenamento, _) = await CriarAsync();
        var chave = Chave();
        await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento);

        (await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento)).Single().PrimeiroBloqueio.Should().BeTrue();
        (await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento)).Single().PrimeiroBloqueio.Should().BeFalse();
    }

    [Fact]
    public async Task Janela_vencida_recomeca_e_zera_o_bloqueio_registrado()
    {
        var (armazenamento, stringConexao) = await CriarAsync();
        var chave = Chave();
        await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento);
        (await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento)).Single().PrimeiroBloqueio.Should().BeTrue();

        await BancoDadosTeste.ExecutarAsync(stringConexao, "UPDATE ContagensLimite SET FimJanela = DATEADD(SECOND, -1, SYSUTCDATETIME())");

        (await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento)).Should().BeEmpty();
        (await Contador(stringConexao, chave)).Should().Be(1);
        (await armazenamento.ContarAsync(Uma(chave, 1), Cancelamento)).Single().PrimeiroBloqueio.Should().BeTrue();
    }

    [Fact]
    public async Task Duas_instancias_do_armazenamento_dividem_o_estado()
    {
        var (a, stringConexao) = await CriarAsync();
        var b = Criar(stringConexao);
        var chave = Chave();

        await a.ContarAsync(Uma(chave, 1), Cancelamento);

        (await b.ContarAsync(Uma(chave, 1), Cancelamento)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_tabela_guarda_so_os_32_bytes_da_chave_sem_dados_legiveis()
    {
        var (armazenamento, stringConexao) = await CriarAsync();
        var chave = GeradorChaveContagem.Gerar(new byte[32], PoliticaLimite.LoginOrigemEmail, "203.0.113.7", "maria@exemplo.com.br");

        await armazenamento.ContarAsync(Uma(chave, 5), Cancelamento);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'ContagensLimite' AND DATA_TYPE IN ('varchar','nvarchar','char','nchar','text','ntext')"))
            .Should().Be(0);
        (await BancoDadosTeste.ConsultarAsync<byte[]>(stringConexao, "SELECT TOP 1 Chave FROM ContagensLimite")).Should().Equal(chave);
    }

    [Fact]
    public async Task O_aquecimento_abre_a_conexao_sem_contar_nada()
    {
        var (armazenamento, stringConexao) = await CriarAsync();

        await armazenamento.AquecerAsync(Cancelamento);

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM ContagensLimite")).Should().Be(0);
    }

    private static Task<int> Contador(string stringConexao, byte[] chave) =>
        BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT Contador FROM ContagensLimite WHERE Chave = 0x{Convert.ToHexString(chave)}");
}

/// <summary>O custo por requisição (REQ-23, SC-008): serial, para não disputar o banco com outros testes.</summary>
[Collection(nameof(ColecaoDesempenhoBanco))]
public sealed class DesempenhoArmazenamentoContagensTestes(BancoDadosTeste banco)
{
    private const int Operacoes = 200;

    [Fact]
    public async Task O_p95_de_chaves_novas_fica_abaixo_de_50_ms()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var armazenamento = new ArmazenamentoContagensSql(
            Options.Create(new OpcoesSqlServer { StringConexao = await banco.NovoBancoMigradoAsync() }));
        var cancelamento = TestContext.Current.CancellationToken;
        await armazenamento.AquecerAsync(cancelamento);

        var tempos = new double[Operacoes];
        for (var i = 0; i < Operacoes; i++)
        {
            var inicio = Stopwatch.GetTimestamp();
            await armazenamento.ContarAsync(
                [new(RandomNumberGenerator.GetBytes(32), 10, 60), new(RandomNumberGenerator.GetBytes(32), 100, 60)],
                cancelamento);
            tempos[i] = Stopwatch.GetElapsedTime(inicio).TotalMilliseconds;
        }

        Array.Sort(tempos);
        tempos[(int)(Operacoes * 0.95)].Should().BeLessThan(50);
    }
}
