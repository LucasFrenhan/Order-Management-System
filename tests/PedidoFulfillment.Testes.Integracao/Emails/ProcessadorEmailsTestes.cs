using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Emails;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Emails;

/// <summary>O processador com banco real e um enviador falso (REQ-24, R-03, R-11). A rodada é chamada direto.</summary>
public sealed partial class ProcessadorEmailsTestes(BancoDadosTeste banco)
{
    private const string Email = "maria@exemplo.com.br";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [GeneratedRegex(@"Token: (\S+)")]
    private static partial Regex ExpressaoToken();

    internal static string TokenDe(MensagemEmail mensagem) => ExpressaoToken().Match(mensagem.Corpo).Groups[1].Value;

    private static ProcessadorEmails Processador(CenarioIdentidade cenario, OpcoesEmail? opcoes = null) => new(
        cenario.Escopos,
        Options.Create(opcoes ?? new OpcoesEmail()),
        Options.Create(new OpcoesSqlServer { StringConexao = cenario.StringConexao }),
        cenario.Relogio,
        cenario.Servicos.GetRequiredService<ILogger<ProcessadorEmails>>());

    private static async Task SolicitarAsync(CenarioIdentidade cenario, TipoEmail tipo, string destino = Email)
    {
        await using var escopo = cenario.Servicos.CreateAsyncScope();
        await escopo.ServiceProvider.GetRequiredService<IFilaEmails>().EnfileirarAsync(tipo, destino, Cancelamento);
    }

    private static Task<List<SolicitacaoEmail>> SolicitacoesAsync(CenarioIdentidade cenario) =>
        cenario.NoContextoAsync(c => c.SolicitacoesEmail.AsNoTracking().ToListAsync(Cancelamento));

    private static async Task<Guid?> ConsumirAsync(CenarioIdentidade cenario, FinalidadeTokenUsoUnico finalidade, string token)
    {
        await using var escopo = cenario.Servicos.CreateAsyncScope();
        return await escopo.ServiceProvider.GetRequiredService<IArmazenamentoSessoes>()
            .ConsumirTokenUsoUnicoAsync(finalidade, GeradorTokenOpaco.Hash(token), cenario.Relogio.Agora, Cancelamento);
    }

    [Theory]
    [InlineData(TipoEmail.RecuperacaoSenha, FinalidadeTokenUsoUnico.RecuperacaoSenha, "Recuperação de senha")]
    [InlineData(TipoEmail.ConfirmacaoEmail, FinalidadeTokenUsoUnico.ConfirmacaoEmail, "Confirmação de e-mail")]
    public async Task Envia_com_um_token_que_vale_e_apaga_a_solicitacao(TipoEmail tipo, FinalidadeTokenUsoUnico finalidade, string assunto)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        await SolicitarAsync(cenario, tipo);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        var mensagem = cenario.Enviador.Mensagens.Should().ContainSingle().Subject;
        mensagem.Assunto.Should().Be(assunto);
        (await ConsumirAsync(cenario, finalidade, TokenDe(mensagem))).Should().Be(usuario);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
        cenario.Logs.Registros.Should().Contain(r => r.Evento.Id == 3016);
    }

    [Fact]
    public async Task O_aviso_de_senha_redefinida_nao_gera_token()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.AvisoSenhaRedefinida);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Mensagens.Should().ContainSingle().Which.Assunto.Should().Be("Sua senha foi redefinida");
        (await cenario.NoContextoAsync(c => c.TokensUsoUnico.CountAsync(Cancelamento))).Should().Be(0);
    }

    [Fact]
    public async Task Sem_conta_apaga_sem_enviar_e_sem_gerar_token()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha, "ninguem@exemplo.com.br");

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(0);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
        (await cenario.NoContextoAsync(c => c.TokensUsoUnico.CountAsync(Cancelamento))).Should().Be(0);
        cenario.Logs.Textos.Should().NotContain(t => t.Contains("ninguem@exemplo.com.br"));
    }

    [Fact]
    public async Task Reenvio_para_conta_confirmada_nao_envia()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync(confirmada: true);
        await SolicitarAsync(cenario, TipoEmail.ConfirmacaoEmail);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(0);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
    }

    [Fact]
    public async Task O_email_vai_ao_endereco_guardado_na_conta_nao_ao_digitado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha, "  MARIA@Exemplo.COM.br ");

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Mensagens.Should().ContainSingle().Which.Destino.Should().Be(Email);
    }

    [Fact]
    public async Task Falha_temporaria_reagenda_com_espera_crescente_e_so_o_ultimo_token_vale()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        var processador = Processador(cenario);
        cenario.Enviador.Proximo(ResultadoEnvioEmail.FalhaTemporaria);
        cenario.Enviador.Proximo(ResultadoEnvioEmail.FalhaTemporaria);
        var inicio = cenario.Relogio.Agora;

        await processador.ExecutarRodadaAsync(Cancelamento);
        var primeira = (await SolicitacoesAsync(cenario)).Single();
        await processador.ExecutarRodadaAsync(Cancelamento);
        cenario.Enviador.Tentativas.Should().Be(1, "ainda não venceu a espera");
        cenario.Relogio.Agora = primeira.ProximaTentativaEm;
        await processador.ExecutarRodadaAsync(Cancelamento);
        var segunda = (await SolicitacoesAsync(cenario)).Single();
        cenario.Relogio.Agora = segunda.ProximaTentativaEm;
        await processador.ExecutarRodadaAsync(Cancelamento);

        primeira.ProximaTentativaEm.Should().Be(inicio.AddSeconds(30));
        (segunda.ProximaTentativaEm - primeira.ProximaTentativaEm).Should().Be(TimeSpan.FromMinutes(1));
        segunda.Tentativas.Should().Be(2);
        cenario.Logs.Registros.Count(r => r.Evento.Id == 3017).Should().Be(2);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
        var mensagem = cenario.Enviador.Mensagens.Should().ContainSingle().Subject;
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, TokenDe(mensagem))).Should().Be(usuario);
        (await cenario.NoContextoAsync(c => c.TokensUsoUnico.CountAsync(Cancelamento))).Should().Be(1, "cada tentativa invalida o token anterior");
    }

    [Fact]
    public async Task Falha_de_conexao_termina_a_rodada_sem_processar_as_seguintes()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await cenario.ContaAsync("joao@exemplo.com.br");
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha, "joao@exemplo.com.br");
        cenario.Enviador.Proximo(ResultadoEnvioEmail.FalhaConexao);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(1);
        (await cenario.NoContextoAsync(c => c.TokensUsoUnico.CountAsync(Cancelamento))).Should().Be(1, "não gasta um token por linha");
        (await SolicitacoesAsync(cenario)).Should().HaveCount(2);
    }

    [Fact]
    public async Task Recusa_permanente_apaga_sem_insistir()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        cenario.Enviador.Proximo(ResultadoEnvioEmail.RecusaPermanente);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
        var log = cenario.Logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3018).Subject;
        log.Texto.Should().NotContain(Email);
    }

    [Fact]
    public async Task Mais_de_24_h_descarta_sem_enviar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        cenario.Relogio.Agora += TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(0);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
        cenario.Logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3018).Which.Texto.Should().NotContain(Email);
    }

    [Fact]
    public async Task Queda_entre_o_envio_e_a_remocao_gera_outro_email_e_so_o_ultimo_token_vale()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        var usuario = await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        var processador = Processador(cenario);
        cenario.Enviador.QuedaDepoisDeEnviar();

        await processador.ExecutarRodadaAsync(Cancelamento);
        await processador.ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Mensagens.Should().HaveCount(2);
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, TokenDe(cenario.Enviador.Mensagens[0]))).Should().BeNull();
        (await ConsumirAsync(cenario, FinalidadeTokenUsoUnico.RecuperacaoSenha, TokenDe(cenario.Enviador.Mensagens[1]))).Should().Be(usuario);
        (await SolicitacoesAsync(cenario)).Should().BeEmpty();
    }

    [Fact]
    public async Task Respeita_o_tamanho_do_lote()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.AvisoSenhaRedefinida);
        await SolicitarAsync(cenario, TipoEmail.AvisoSenhaRedefinida);

        await Processador(cenario, new OpcoesEmail { TamanhoLote = 1 }).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(1);
        (await SolicitacoesAsync(cenario)).Should().ContainSingle();
    }

    [Fact]
    public async Task Com_o_lock_em_outra_instancia_a_rodada_nao_faz_nada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var cenario = await CenarioIdentidade.CriarAsync(banco);
        await cenario.ContaAsync();
        await SolicitarAsync(cenario, TipoEmail.RecuperacaoSenha);
        await using var conexao = new SqlConnection(cenario.StringConexao);
        await conexao.OpenAsync(Cancelamento);
        await using var comando = new SqlCommand(
            $"EXEC sp_getapplock @Resource = '{ProcessadorEmails.RecursoLock}', @LockMode = 'Exclusive', @LockOwner = 'Session';", conexao);
        await comando.ExecuteNonQueryAsync(Cancelamento);

        await Processador(cenario).ExecutarRodadaAsync(Cancelamento);

        cenario.Enviador.Tentativas.Should().Be(0);
        (await SolicitacoesAsync(cenario)).Should().ContainSingle();
    }
}
