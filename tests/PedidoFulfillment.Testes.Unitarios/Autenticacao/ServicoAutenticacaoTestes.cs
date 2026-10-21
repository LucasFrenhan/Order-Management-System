using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

public sealed class ServicoAutenticacaoTestes
{
    private const string Senha = "SenhaForte1";

    private readonly GestorIdentidadeFalso gestor = new();
    private readonly LoggerFalso<ServicoAutenticacao> logger = new();
    private readonly ServicoAutenticacao servico;

    public ServicoAutenticacaoTestes()
    {
        servico = new ServicoAutenticacao(gestor, logger);
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

    private static void DeveSerSemDadosSensiveis(IEnumerable<EventoLog> eventos)
    {
        foreach (var evento in eventos)
        {
            var texto = evento.Mensagem + string.Concat(evento.Propriedades.Values);
            texto.Should().NotContain(Senha).And.NotContainEquivalentOf("maria@exemplo.com.br");
        }
    }
}
