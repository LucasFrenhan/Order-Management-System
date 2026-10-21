using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>
/// User Story 2: register e login públicos, validados e com respostas do contrato.
/// </summary>
public sealed class EndpointsAutenticacaoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private const string Registro = "/api/v1/auth/register";
    private const string Login = "/api/v1/auth/login";

    private static readonly object RegistroValido = new { nome = "Maria Silva", email = "maria@exemplo.com.br", senha = "SenhaForte123" };
    private static readonly object LoginValido = new { email = "maria@exemplo.com.br", senha = "SenhaForte123" };

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(Registro)]
    [InlineData(Login)]
    public async Task Rotas_publicas_sem_token_chegam_ao_servico_provisorio(string rota)
    {
        var corpo = rota == Registro ? RegistroValido : LoginValido;

        var resposta = await fabrica.CreateClient().PostAsJsonAsync(rota, corpo, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 501, rota);
    }

    [Fact]
    public async Task Registro_com_corpo_vazio_lista_todos_os_campos_obrigatorios()
    {
        var erros = await ErrosDe(await fabrica.CreateClient().PostAsJsonAsync(Registro, new { }, Cancelamento), Registro);

        erros.Should().ContainKeys("nome", "email", "senha");
    }

    [Fact]
    public async Task Login_com_corpo_vazio_lista_email_e_senha()
    {
        var erros = await ErrosDe(await fabrica.CreateClient().PostAsJsonAsync(Login, new { }, Cancelamento), Login);

        erros.Should().ContainKeys("email", "senha");
    }

    [Theory]
    [InlineData("{ isto não é json")]
    [InlineData("")]
    public async Task Corpo_ilegivel_ou_ausente_responde_400_com_erro_no_corpo_inteiro(string corpo)
    {
        var conteudo = new StringContent(corpo, Encoding.UTF8, "application/json");

        var erros = await ErrosDe(await fabrica.CreateClient().PostAsync(Registro, conteudo, Cancelamento), Registro);

        erros.Should().ContainKey("");
    }

    [Fact]
    public async Task Registro_com_sucesso_responde_201_com_usuario()
    {
        var falso = new ServicoAutenticacaoFalso
        {
            ResultadoRegistro = new UsuarioResponseDTO
            {
                Id = Guid.NewGuid(),
                Nome = "Maria Silva",
                Email = "maria@exemplo.com.br",
                Perfis = [PerfilUsuario.Cliente]
            }
        };
        await using var api = new FabricaApi { ServicoAutenticacao = falso };

        var resposta = await api.CreateClient().PostAsJsonAsync(Registro, RegistroValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta.Headers.Location.Should().BeNull();
        var usuario = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        usuario.GetProperty("perfis")[0].GetString().Should().Be("Cliente");
        falso.UltimoRegistro!.Email.Should().Be("maria@exemplo.com.br");
    }

    [Fact]
    public async Task Email_duplicado_responde_409_com_codigo()
    {
        var falso = new ServicoAutenticacaoFalso
        {
            ResultadoRegistro = new Falha(TipoFalha.Conflito, "E-mail já cadastrado.", Codigo: CodigosErro.UsuarioEmailJaCadastrado)
        };
        await using var api = new FabricaApi { ServicoAutenticacao = falso };

        var resposta = await api.CreateClient().PostAsJsonAsync(Registro, RegistroValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 409, Registro);
        corpo.GetProperty("codigo").GetString().Should().Be("USUARIO_EMAIL_JA_CADASTRADO");
    }

    [Fact]
    public async Task Login_com_sucesso_responde_200_com_token()
    {
        var falso = new ServicoAutenticacaoFalso
        {
            ResultadoLogin = new LoginResponseDTO
            {
                TokenAcesso = "token",
                ExpiraEm = DateTimeOffset.UtcNow.AddMinutes(60),
                Perfis = [PerfilUsuario.Cliente]
            }
        };
        await using var api = new FabricaApi { ServicoAutenticacao = falso };

        var resposta = await api.CreateClient().PostAsJsonAsync(Login, LoginValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento)).GetProperty("tipoToken").GetString().Should().Be("Bearer");
    }

    [Fact]
    public async Task Credenciais_invalidas_respondem_401_com_mensagem_generica()
    {
        var falso = new ServicoAutenticacaoFalso
        {
            ResultadoLogin = new Falha(TipoFalha.NaoAutenticado, "Credenciais inválidas.", "E-mail ou senha incorretos.")
        };
        await using var api = new FabricaApi { ServicoAutenticacao = falso };

        var resposta = await api.CreateClient().PostAsJsonAsync(Login, LoginValido, Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 401, Login);
        corpo.GetProperty("detail").GetString().Should().Be("E-mail ou senha incorretos.");
    }

    internal static async Task<Dictionary<string, string[]>> ErrosDe(HttpResponseMessage resposta, string instancia)
    {
        resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, 400, instancia);
        return corpo.GetProperty("erros").Deserialize<Dictionary<string, string[]>>()!;
    }
}
