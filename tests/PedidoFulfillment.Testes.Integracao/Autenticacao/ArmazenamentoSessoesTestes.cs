using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>Sessões e tokens com SQL Server real, inclusive as disputas (REQ-24, R-02, SC-002).</summary>
public sealed class ArmazenamentoSessoesTestes(BancoDadosTeste banco)
{
    private static readonly ConfiguracaoSessoes Validades = new(TimeSpan.FromDays(14), TimeSpan.FromDays(30));

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static byte[] Hash() => GeradorTokenOpaco.Hash(GeradorTokenOpaco.Gerar());

    private static async Task<T> ComArmazenamentoAsync<T>(CenarioIdentidade cenario, Func<IArmazenamentoSessoes, Task<T>> acao)
    {
        await using var escopo = cenario.Servicos.CreateAsyncScope();
        return await acao(escopo.ServiceProvider.GetRequiredService<IArmazenamentoSessoes>());
    }

    private static Task<DateTimeOffset> SessaoAsync(CenarioIdentidade cenario, Guid usuarioId, byte[] hash, ConfiguracaoSessoes? validades = null) =>
        ComArmazenamentoAsync(cenario, a => a.CriarSessaoAsync(usuarioId, hash, cenario.Relogio.Agora, validades ?? Validades, Cancelamento));

    private static Task<ResultadoRotacao> RotacionarAsync(CenarioIdentidade cenario, byte[] apresentado, byte[]? novo = null) =>
        ComArmazenamentoAsync(cenario, a => a.RotacionarAsync(apresentado, novo ?? Hash(), cenario.Relogio.Agora, Validades, Cancelamento));

    [Fact]
    public async Task Rotacao_troca_o_token_e_devolve_o_dono_e_a_validade()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var primeiro = Hash();
        var segundo = Hash();
        await SessaoAsync(cenario, usuario, primeiro);

        var resultado = await RotacionarAsync(cenario, primeiro, segundo);

        resultado.Should().Be(new ResultadoRotacao(SituacaoRotacao.Renovada, usuario, cenario.Relogio.Agora + Validades.ValidadeToken));
        (await RotacionarAsync(cenario, segundo)).Situacao.Should().Be(SituacaoRotacao.Renovada);
    }

    [Fact]
    public async Task Rotacoes_simultaneas_com_o_mesmo_token_tem_um_so_vencedor()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var token = Hash();
        await SessaoAsync(cenario, usuario, token);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 50).Select(_ => Task.Run(() => RotacionarAsync(cenario, token), Cancelamento)));

        resultados.Count(r => r.Situacao == SituacaoRotacao.Renovada).Should().Be(1);
        resultados.Count(r => r.Situacao == SituacaoRotacao.Reutilizada).Should().Be(1, "só quem encerrou a sessão relata");
        (await cenario.NoContextoAsync(c => c.SessoesAutenticacao.SingleAsync(Cancelamento))).EncerradaEm.Should().NotBeNull();
    }

    [Fact]
    public async Task Reutilizacao_encerra_a_sessao_e_o_token_mais_recente_deixa_de_renovar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var primeiro = Hash();
        var segundo = Hash();
        await SessaoAsync(cenario, usuario, primeiro);
        await RotacionarAsync(cenario, primeiro, segundo);

        var reutilizacao = await RotacionarAsync(cenario, primeiro);
        var depois = await RotacionarAsync(cenario, segundo);

        reutilizacao.Should().Be(new ResultadoRotacao(SituacaoRotacao.Reutilizada, usuario));
        depois.Situacao.Should().Be(SituacaoRotacao.Recusada);
    }

    [Fact]
    public async Task Token_desconhecido_ou_vencido_e_recusado_sem_encerrar_a_sessao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var token = Hash();
        await SessaoAsync(cenario, usuario, token);

        (await RotacionarAsync(cenario, Hash())).Situacao.Should().Be(SituacaoRotacao.Recusada);
        cenario.Relogio.Agora += Validades.ValidadeToken + TimeSpan.FromSeconds(1);
        (await RotacionarAsync(cenario, token)).Situacao.Should().Be(SituacaoRotacao.Recusada);

        (await cenario.NoContextoAsync(c => c.SessoesAutenticacao.SingleAsync(Cancelamento))).EncerradaEm.Should().BeNull();
    }

    [Fact]
    public async Task O_token_novo_nunca_passa_do_limite_da_sessao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var inicio = cenario.Relogio.Agora;
        var token = Hash();
        await SessaoAsync(cenario, usuario, token);
        cenario.Relogio.Agora = inicio + TimeSpan.FromDays(10);
        var segundo = Hash();
        await RotacionarAsync(cenario, token, segundo);
        cenario.Relogio.Agora = inicio + TimeSpan.FromDays(20);

        var resultado = await RotacionarAsync(cenario, segundo);

        resultado.TokenExpiraEm.Should().Be(inicio + Validades.DuracaoMaxima, "a rotação não estende a sessão (FR-005)");
    }

    [Fact]
    public async Task Sessao_encerrada_recusa_o_token_ainda_valido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var token = Hash();
        await SessaoAsync(cenario, usuario, token);

        var dono = await ComArmazenamentoAsync(cenario, a => a.EncerrarSessaoAsync(token, cenario.Relogio.Agora, Cancelamento));

        dono.Should().Be(usuario);
        (await RotacionarAsync(cenario, token)).Situacao.Should().Be(SituacaoRotacao.Recusada);
        (await ComArmazenamentoAsync(cenario, a => a.EncerrarSessaoAsync(token, cenario.Relogio.Agora, Cancelamento))).Should().BeNull("repetir não é erro");
    }

    [Fact]
    public async Task Encerrar_as_sessoes_da_conta_nao_afeta_outra_conta()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var outro = await cenario.ContaAsync("joao@exemplo.com.br");
        var (a, b, c) = (Hash(), Hash(), Hash());
        await SessaoAsync(cenario, usuario, a);
        await SessaoAsync(cenario, usuario, b);
        await SessaoAsync(cenario, outro, c);

        await ComArmazenamentoAsync(cenario, async x =>
        {
            await x.EncerrarSessoesDaContaAsync(usuario, cenario.Relogio.Agora, Cancelamento);
            return true;
        });

        (await RotacionarAsync(cenario, a)).Situacao.Should().Be(SituacaoRotacao.Recusada);
        (await RotacionarAsync(cenario, b)).Situacao.Should().Be(SituacaoRotacao.Recusada);
        (await RotacionarAsync(cenario, c)).Situacao.Should().Be(SituacaoRotacao.Renovada);
    }

    private static Task CriarUsoUnicoAsync(CenarioIdentidade cenario, Guid usuario, FinalidadeTokenUsoUnico finalidade, byte[] hash) =>
        ComArmazenamentoAsync(cenario, async a =>
        {
            await a.CriarTokenUsoUnicoAsync(usuario, finalidade, hash, cenario.Relogio.Agora + TimeSpan.FromMinutes(30), Cancelamento);
            return true;
        });

    private static Task<Guid?> ConsumirAsync(CenarioIdentidade cenario, FinalidadeTokenUsoUnico finalidade, byte[] hash) =>
        ComArmazenamentoAsync(cenario, a => a.ConsumirTokenUsoUnicoAsync(finalidade, hash, cenario.Relogio.Agora, Cancelamento));

    [Fact]
    public async Task Consumos_simultaneos_do_token_de_uso_unico_tem_um_so_vencedor()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var hash = Hash();
        await CriarUsoUnicoAsync(cenario, usuario, FinalidadeTokenUsoUnico.RecuperacaoSenha, hash);

        var resultados = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => Task.Run(() => ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, hash), Cancelamento)));

        resultados.Where(r => r is not null).Should().ContainSingle().Which.Should().Be(usuario);
    }

    [Fact]
    public async Task Token_novo_invalida_so_os_da_mesma_conta_e_finalidade()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var outro = await cenario.ContaAsync("joao@exemplo.com.br");
        var (antigo, novo, confirmacao, deOutro) = (Hash(), Hash(), Hash(), Hash());
        await CriarUsoUnicoAsync(cenario, usuario, FinalidadeTokenUsoUnico.RecuperacaoSenha, antigo);
        await CriarUsoUnicoAsync(cenario, usuario, FinalidadeTokenUsoUnico.ConfirmacaoEmail, confirmacao);
        await CriarUsoUnicoAsync(cenario, outro, FinalidadeTokenUsoUnico.RecuperacaoSenha, deOutro);

        await CriarUsoUnicoAsync(cenario, usuario, FinalidadeTokenUsoUnico.RecuperacaoSenha, novo);

        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, antigo)).Should().BeNull();
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, novo)).Should().Be(usuario);
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.ConfirmacaoEmail, confirmacao)).Should().Be(usuario);
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, deOutro)).Should().Be(outro);
    }

    [Fact]
    public async Task Token_de_outra_finalidade_ou_vencido_nao_e_consumido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        var hash = Hash();
        await CriarUsoUnicoAsync(cenario, usuario, FinalidadeTokenUsoUnico.RecuperacaoSenha, hash);

        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.ConfirmacaoEmail, hash)).Should().BeNull();
        cenario.Relogio.Agora += TimeSpan.FromMinutes(31);
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, hash)).Should().BeNull();
    }

    [Fact]
    public async Task Apagar_a_sessao_apaga_os_tokens_em_cascata()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await SessaoAsync(cenario, await cenario.ContaAsync(), Hash());

        var tokens = await cenario.NoContextoAsync(async c =>
        {
            await c.SessoesAutenticacao.ExecuteDeleteAsync(Cancelamento);
            return await c.TokensRenovacao.CountAsync(Cancelamento);
        });

        tokens.Should().Be(0);
    }
}
