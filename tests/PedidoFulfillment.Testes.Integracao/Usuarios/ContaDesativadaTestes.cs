using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>
/// Nada devolve o acesso a uma conta desativada, e nenhuma resposta pública revela a desativação
/// (REQ-25, FR-016 a FR-019).
/// </summary>
public sealed class ContaDesativadaTestes(BancoDadosTeste banco)
{
    private const string TokenInvalido = "400|Token inválido.|O token é inválido ou expirou. Peça uma nova recuperação.|TOKEN_INVALIDO";

    private sealed record Ambiente(FabricaApi Api, string Conexao, EnviadorEmailFalso Enviador, CapturaLogs Logs, HttpClient Admin, HttpClient Anonimo, Guid Id) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Api.DisposeAsync();

        public async Task DesativarAsync() =>
            (await Admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{Id}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        public async Task ReativarAsync() =>
            (await Admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{Id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);

        public Task RodadaAsync() => RodadaEmailsAsync(Api, Conexao);
    }

    /// <summary>Uma conta de Cliente, vinda do registro público, sem nenhum e-mail pendente.</summary>
    private async Task<Ambiente> PrepararAsync()
    {
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        var api = CenarioGestaoUsuarios.Api(conexao, logs, enviador: enviador);
        var admin = await CenarioGestaoUsuarios.ClienteDeAsync(api);
        var anonimo = api.CreateClient();
        await RegistrarAsync(anonimo);
        await RodadaEmailsAsync(api, conexao);
        enviador.Mensagens.Clear();
        return new Ambiente(api, conexao, enviador, logs, admin, anonimo, await CenarioGestaoUsuarios.IdDeAsync(api, Email));
    }

    private static Task<string> AssinaturaLoginAsync(HttpClient cliente, string email, string senha) =>
        TentarLoginAsync(cliente, email, senha).ContinueWith(t => AssinaturaAsync(t.Result), Cancelamento, TaskContinuationOptions.None, TaskScheduler.Default).Unwrap();

    [Fact]
    public async Task Login_com_senha_certa_ou_errada_da_o_mesmo_401_sem_contar_erro_e_registra_o_motivo_so_no_log()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var padrao = await AssinaturaLoginAsync(a.Anonimo, "ninguem@exemplo.com.br", Senha);

        await a.DesativarAsync();

        for (var i = 0; i < 6; i++)
        {
            (await AssinaturaLoginAsync(a.Anonimo, Email, Senha)).Should().Be(padrao);
            (await AssinaturaLoginAsync(a.Anonimo, Email, "SenhaErrada9")).Should().Be(padrao);
        }

        await using var escopo = a.Api.Services.CreateAsyncScope();
        var conta = await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.AsNoTracking().SingleAsync(u => u.Id == a.Id, Cancelamento);
        conta.AccessFailedCount.Should().Be(0);
        conta.LockoutEnd.Should().BeNull();
        a.Logs.Registros.Should().Contain(r => r.Evento.Id == 3003 && r.Texto.Contains("ContaDesativada"));
    }

    [Fact]
    public async Task Renovacao_da_o_mesmo_401()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var sessao = await LoginAsync(a.Anonimo);
        var padrao = await AssinaturaAsync(await RenovarAsync(a.Anonimo, "nao-existe"));

        await a.DesativarAsync();

        (await AssinaturaAsync(await RenovarAsync(a.Anonimo, sessao.Renovacao))).Should().Be(padrao);
    }

    [Fact]
    public async Task Sessao_criada_depois_de_encerrar_as_sessoes_e_recusada_pela_situacao_da_conta()
    {
        // O login já avaliado cria a sessão logo depois da desativação (R-08).
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var sessao = await LoginAsync(a.Anonimo);
        await using (var escopo = a.Api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.Where(u => u.Id == a.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, DateTimeOffset.UtcNow), Cancelamento);
        }

        (await RenovarAsync(a.Anonimo, sessao.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await a.ReativarAsync();
        (await RenovarAsync(a.Anonimo, sessao.Renovacao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a reativação encerra de novo as sessões");
    }

    [Fact]
    public async Task Recuperacao_e_reenvio_dao_202_sem_nenhum_email()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await a.DesativarAsync();

        (await PedirRecuperacaoAsync(a.Anonimo)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        (await a.Anonimo.PostAsJsonAsync(RotaReenvio, new { email = Email }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.Accepted);
        await a.RodadaAsync();

        a.Enviador.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Email_pendente_ao_desativar_nao_e_enviado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        (await PedirRecuperacaoAsync(a.Anonimo)).StatusCode.Should().Be(HttpStatusCode.Accepted);

        await a.DesativarAsync();
        await a.RodadaAsync();

        a.Enviador.Mensagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Tokens_anteriores_a_desativacao_recebem_token_invalido_e_a_conta_segue_desativada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await PedirRecuperacaoAsync(a.Anonimo);
        await a.RodadaAsync();
        var recuperacao = ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);
        await a.Anonimo.PostAsJsonAsync(RotaReenvio, new { email = Email }, Cancelamento);
        await a.RodadaAsync();
        var confirmacao = ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);

        await a.DesativarAsync();

        (await AssinaturaAsync(await RedefinirAsync(a.Anonimo, recuperacao))).Should().Be(TokenInvalido);
        (await a.Anonimo.PostAsJsonAsync(RotaConfirmacao, new { token = confirmacao }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var conta = await CenarioGestaoUsuarios.LerContaAsync(a.Api, a.Id);
        conta.Desativada.Should().BeTrue();
        conta.EmailConfirmado.Should().BeFalse();
        (await TentarLoginAsync(a.Anonimo, senha: NovaSenha)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_de_convite_anterior_a_desativacao_recebe_token_invalido_e_reativar_nao_o_devolve()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var resposta = await a.Admin.PostAsJsonAsync(CenarioGestaoUsuarios.Rota, new { nome = "Ana Lima", email = "ana@exemplo.com.br", perfil = "OperadorLogistica" }, Cancelamento);
        var id = (await CenarioGestaoUsuarios.CorpoAsync(resposta)).GetProperty("id").GetGuid();
        await a.RodadaAsync();
        var convite = ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]);

        (await a.Admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{id}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AssinaturaAsync(await RedefinirAsync(a.Anonimo, convite))).Should().Be(TokenInvalido);

        (await a.Admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await AssinaturaAsync(await RedefinirAsync(a.Anonimo, convite))).Should().Be(TokenInvalido);
        (await CenarioGestaoUsuarios.LerContaAsync(a.Api, id)).ConvitePendente.Should().BeTrue();

        // A recuperação resolve, como na spec.
        await PedirRecuperacaoAsync(a.Anonimo, "ana@exemplo.com.br");
        await a.RodadaAsync();
        (await RedefinirAsync(a.Anonimo, ProcessadorEmailsTestes.TokenDe(a.Enviador.Mensagens[^1]))).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Theory]
    [InlineData(FinalidadeTokenUsoUnico.RecuperacaoSenha)]
    [InlineData(FinalidadeTokenUsoUnico.ConviteUsuario)]
    public async Task Token_gerado_no_instante_da_desativacao_nao_redefine_e_nao_e_gasto(FinalidadeTokenUsoUnico finalidade)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await a.DesativarAsync();
        var token = GeradorTokenOpaco.Gerar();
        await using (var escopo = a.Api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<IArmazenamentoSessoes>()
                .CriarTokenUsoUnicoAsync(a.Id, finalidade, GeradorTokenOpaco.Hash(token), DateTimeOffset.UtcNow.AddHours(1), Cancelamento);
        }

        (await AssinaturaAsync(await RedefinirAsync(a.Anonimo, token))).Should().Be(TokenInvalido);

        (await TentarLoginAsync(a.Anonimo, senha: NovaSenha)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Confirmacao_gerada_no_instante_da_desativacao_nao_confirma()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await a.DesativarAsync();
        var token = GeradorTokenOpaco.Gerar();
        await using (var escopo = a.Api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<IArmazenamentoSessoes>()
                .CriarTokenUsoUnicoAsync(a.Id, FinalidadeTokenUsoUnico.ConfirmacaoEmail, GeradorTokenOpaco.Hash(token), DateTimeOffset.UtcNow.AddHours(1), Cancelamento);
        }

        (await a.Anonimo.PostAsJsonAsync(RotaConfirmacao, new { token }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        (await CenarioGestaoUsuarios.LerContaAsync(a.Api, a.Id)).EmailConfirmado.Should().BeFalse();
    }

    [Fact]
    public async Task Troca_de_senha_com_token_valido_da_400_de_senha_atual_sem_contar_erro()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var sessao = await LoginAsync(a.Anonimo);
        var conectado = a.Api.CreateClient().ComToken(sessao.Acesso);
        await a.DesativarAsync();

        var troca = await conectado.PostAsJsonAsync(RotaTroca, new { senhaAtual = Senha, novaSenha = NovaSenha }, Cancelamento);

        troca.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CenarioGestaoUsuarios.CorpoAsync(troca)).GetProperty("erros").TryGetProperty("senhaAtual", out _).Should().BeTrue();
        await a.ReativarAsync();
        (await TentarLoginAsync(a.Anonimo)).StatusCode.Should().Be(HttpStatusCode.OK, "a senha não mudou");
    }

    [Fact]
    public async Task Registro_com_o_mesmo_email_da_o_409_de_sempre()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        await a.DesativarAsync();

        var registro = await a.Anonimo.PostAsJsonAsync(RotaRegistro, new { nome = "Maria Silva", email = Email, senha = Senha }, Cancelamento);

        registro.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Redefinicao_nao_reativa_e_reativacao_nao_mexe_no_bloqueio()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        for (var i = 0; i < 5; i++)
        {
            await TentarLoginAsync(a.Anonimo, senha: "SenhaErrada9");
        }

        await a.DesativarAsync();
        await a.ReativarAsync();

        (await TentarLoginAsync(a.Anonimo)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a conta continua bloqueada pelos erros de senha");
        await using var escopo = a.Api.Services.CreateAsyncScope();
        var conta = await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.AsNoTracking().SingleAsync(u => u.Id == a.Id, Cancelamento);
        conta.LockoutEnd.Should().NotBeNull();
    }

    [Fact]
    public async Task Token_de_acesso_segue_valendo_fora_da_gestao_ate_expirar()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var a = await PrepararAsync();
        var sessao = await LoginAsync(a.Anonimo);
        var conectado = a.Api.CreateClient().ComToken(sessao.Acesso);

        await a.DesativarAsync();

        (await conectado.GetAsync("/api/v1/pedidos", Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Conta_da_carga_inicial_continua_desativada_depois_de_rodar_a_preparacao_de_novo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        Guid inicial;
        await using (var api = CenarioGestaoUsuarios.Api(conexao))
        {
            inicial = await CenarioGestaoUsuarios.IdDeAsync(api, CenarioGestaoUsuarios.EmailAdmin);
            await CenarioGestaoUsuarios.ContaAsync(api, "outro@exemplo.com.br", perfis: Domain.Usuarios.PerfilUsuario.Admin);
            var outro = await CenarioGestaoUsuarios.ClienteDeAsync(api, "outro@exemplo.com.br");
            (await outro.PostAsync($"{CenarioGestaoUsuarios.Rota}/{inicial}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        }

        await using var novaPartida = CenarioGestaoUsuarios.Api(conexao);

        (await CenarioGestaoUsuarios.LerContaAsync(novaPartida, inicial)).Desativada.Should().BeTrue();
        (await TentarLoginAsync(novaPartida.CreateClient(), CenarioGestaoUsuarios.EmailAdmin, CenarioGestaoUsuarios.SenhaPadrao)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
