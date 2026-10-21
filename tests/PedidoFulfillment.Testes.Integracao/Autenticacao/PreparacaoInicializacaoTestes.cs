using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// Preparação da inicialização sem Docker (FR-018): o banco da <see cref="FabricaApi"/> aponta
/// para uma porta fechada.
/// </summary>
public sealed class PreparacaoInicializacaoTestes
{
    private const string MensagemBancoInacessivel =
        "Não foi possível acessar o banco de dados (ConnectionStrings:SqlServer) para preparar a inicialização.";

    [Fact]
    public async Task Banco_inacessivel_impede_a_api_de_iniciar_sem_expor_dados_de_conexao()
    {
        await using var fabrica = new FabricaApi { PrepararBanco = true };

        var excecao = Record.Exception(() => fabrica.CreateClient());

        excecao.Should().NotBeNull();
        var cadeia = Cadeia(excecao!).ToArray();
        cadeia.Should().Contain(e => e.Message == MensagemBancoInacessivel);
        foreach (var item in cadeia)
        {
            item.Message.Should().NotContain("Password=").And.NotContain("127.0.0.1").And.NotContain("senha-de-teste");
        }
    }

    [Fact]
    public async Task Usuario_inicial_com_senha_fora_da_regra_impede_a_api_de_iniciar_sem_mostrar_a_senha()
    {
        await using var fabrica = new FabricaApi
        {
            Configuracoes = new Dictionary<string, string?>
            {
                ["UsuariosIniciais:0:Nome"] = "Administrador",
                ["UsuariosIniciais:0:Email"] = "admin@oms.teste",
                ["UsuariosIniciais:0:Senha"] = "senhafraca",
                ["UsuariosIniciais:0:Perfil"] = "Admin"
            }
        };

        var excecao = Record.Exception(() => fabrica.CreateClient());

        excecao.Should().NotBeNull();
        var mensagens = Cadeia(excecao!).Select(e => e.Message).ToArray();
        mensagens.Should().Contain(m => m.Contains("UsuariosIniciais:0:Senha"));
        mensagens.Should().NotContain(m => m.Contains("senhafraca"));
    }

    [Fact]
    public async Task Preparacao_desligada_inicia_sem_consultar_o_banco()
    {
        await using var fabrica = new FabricaApi();

        var resposta = await fabrica.CreateClient().GetAsync("/health", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    internal static IEnumerable<Exception> Cadeia(Exception excecao)
    {
        yield return excecao;
        IEnumerable<Exception> internas = excecao switch
        {
            AggregateException agregada => agregada.InnerExceptions,
            { InnerException: { } interna } => [interna],
            _ => []
        };
        foreach (var item in internas.SelectMany(Cadeia))
        {
            yield return item;
        }
    }
}
