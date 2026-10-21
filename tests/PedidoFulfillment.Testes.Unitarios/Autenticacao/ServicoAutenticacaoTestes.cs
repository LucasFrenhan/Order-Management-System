using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

public sealed class ServicoAutenticacaoTestes
{
    private const string Senha = "SenhaForte1";

    private readonly GestorIdentidadeFalso gestor = new();
    private readonly LoggerFalso<ServicoAutenticacao> logger = new();
    private readonly ServicoAutenticacao servico;

    public ServicoAutenticacaoTestes()
    {
        servico = new ServicoAutenticacao(gestor, new EmissorTokenFalso(), logger);
    }

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static RegistrarUsuarioRequestDTO Registro(string nome = "  Maria Silva ", string email = " Maria@Exemplo.com.br ") =>
        new() { Nome = nome, Email = email, Senha = Senha };

    [Fact]
    public async Task Registro_apara_nome_e_email_e_mantem_a_caixa_do_email()
    {
        await servico.RegistrarAsync(Registro(), Cancelamento);

        gestor.UltimaCriacao.Should().Be(("Maria Silva", "Maria@Exemplo.com.br", Senha));
    }

    [Fact]
    public async Task Registro_bem_sucedido_registra_evento_com_email_mascarado()
    {
        var resultado = await servico.RegistrarAsync(Registro(), Cancelamento);

        resultado.Sucesso.Should().BeTrue();
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Id.Should().Be(3001);
        evento.Id.Name.Should().Be("UsuarioRegistrado");
        evento.Nivel.Should().Be(LogLevel.Information);
        evento.Propriedades["UsuarioId"].Should().Be(resultado.Valor!.Id);
        evento.Propriedades["EmailMascarado"].Should().Be("M***@Exemplo.com.br");
        DeveSerSemDadosSensiveis(logger.Eventos);
    }

    [Fact]
    public async Task Email_ja_cadastrado_passa_o_conflito_adiante_sem_evento_de_sucesso()
    {
        gestor.ResultadoCriacao = new Falha(TipoFalha.Conflito, "E-mail já cadastrado.", Codigo: CodigosErro.UsuarioEmailJaCadastrado);

        var resultado = await servico.RegistrarAsync(Registro(), Cancelamento);

        resultado.Falha!.Codigo.Should().Be(CodigosErro.UsuarioEmailJaCadastrado);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 3001);
    }

    [Fact]
    public async Task Login_apara_o_email_antes_de_verificar()
    {
        await servico.AutenticarAsync(Login(" Maria@Exemplo.com.br "), Cancelamento);

        gestor.UltimaVerificacao.Should().Be(("Maria@Exemplo.com.br", Senha));
    }

    [Fact]
    public async Task Credenciais_validas_emitem_o_token_e_registram_o_login()
    {
        var usuarioId = Guid.NewGuid();
        gestor.ResultadoVerificacao = ResultadoCredenciais.Valida(usuarioId, [PerfilUsuario.Cliente]);

        var resultado = await servico.AutenticarAsync(Login(), Cancelamento);

        resultado.Valor!.TokenAcesso.Should().Be($"token-de-{usuarioId}");
        resultado.Valor.Perfis.Should().Equal(PerfilUsuario.Cliente);
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Should().Be(new EventId(3002, "LoginRealizado"));
        evento.Propriedades["UsuarioId"].Should().Be(usuarioId);
        DeveSerSemDadosSensiveis(logger.Eventos);
    }

    [Theory]
    [InlineData(SituacaoCredenciais.Invalida, false, "CredenciaisInvalidas")]
    [InlineData(SituacaoCredenciais.Invalida, true, "CredenciaisInvalidas")]
    [InlineData(SituacaoCredenciais.Bloqueada, true, "ContaBloqueada")]
    [InlineData(SituacaoCredenciais.BloqueadaAgora, true, "CredenciaisInvalidas")]
    public async Task Toda_recusa_responde_o_mesmo_401_generico(SituacaoCredenciais situacao, bool contaExiste, string motivo)
    {
        Guid? usuarioId = contaExiste ? Guid.NewGuid() : null;
        gestor.ResultadoVerificacao = new ResultadoCredenciais(situacao, usuarioId, []);

        var resultado = await servico.AutenticarAsync(Login(), Cancelamento);

        resultado.Falha.Should().Be(new Falha(TipoFalha.NaoAutenticado, "Credenciais inválidas.", "E-mail ou senha incorretos."));
        var recusa = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 3003).Subject;
        recusa.Id.Name.Should().Be("LoginRecusado");
        recusa.Nivel.Should().Be(LogLevel.Warning);
        recusa.Propriedades["EmailMascarado"].Should().Be("M***@Exemplo.com.br");
        recusa.Propriedades["UsuarioId"].Should().Be(usuarioId);
        recusa.Propriedades["Motivo"].Should().Be(motivo);
        DeveSerSemDadosSensiveis(logger.Eventos);
    }

    [Fact]
    public async Task Quinto_erro_registra_tambem_o_bloqueio_da_conta()
    {
        var usuarioId = Guid.NewGuid();
        gestor.ResultadoVerificacao = ResultadoCredenciais.BloqueadaAgora(usuarioId);

        await servico.AutenticarAsync(Login(), Cancelamento);

        var bloqueio = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 3004).Subject;
        bloqueio.Id.Name.Should().Be("ContaBloqueada");
        bloqueio.Nivel.Should().Be(LogLevel.Warning);
        bloqueio.Propriedades["UsuarioId"].Should().Be(usuarioId);
        bloqueio.Propriedades["EmailMascarado"].Should().Be("M***@Exemplo.com.br");
    }

    private static LoginRequestDTO Login(string email = "Maria@Exemplo.com.br") => new() { Email = email, Senha = Senha };

    private static void DeveSerSemDadosSensiveis(IEnumerable<EventoLog> eventos)
    {
        foreach (var evento in eventos)
        {
            var texto = evento.Mensagem + string.Concat(evento.Propriedades.Values);
            texto.Should().NotContain(Senha).And.NotContainEquivalentOf("maria@exemplo.com.br");
        }
    }
}
