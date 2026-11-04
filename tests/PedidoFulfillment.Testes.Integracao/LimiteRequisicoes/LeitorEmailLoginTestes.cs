using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using PedidoFulfillment.API.LimiteRequisicoes;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>O e-mail do login, lido antes da rota e sem consumir o corpo (REQ-23, FR-003, R-09).</summary>
public sealed class LeitorEmailLoginTestes
{
    private static HttpRequest Requisicao(string corpo)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(corpo));
        return contexto.Request;
    }

    private static Task<string?> Ler(string corpo) => LeitorEmailLogin.LerAsync(Requisicao(corpo), TestContext.Current.CancellationToken);

    [Theory]
    [InlineData("""{ "email": "maria@exemplo.com.br", "senha": "x" }""")]
    [InlineData("""{ "EMAIL": "maria@exemplo.com.br" }""")]
    [InlineData("""{ "email": "  maria@exemplo.com.br " }""")]
    public async Task Le_o_email_sem_diferenciar_maiusculas_no_nome_e_sem_os_espacos(string corpo) =>
        (await Ler(corpo)).Should().Be("maria@exemplo.com.br");

    [Theory]
    [InlineData("")]
    [InlineData("{ \"email\": ")]
    [InlineData("[]")]
    [InlineData("""{ "senha": "x" }""")]
    [InlineData("""{ "email": 123 }""")]
    [InlineData("""{ "email": null }""")]
    [InlineData("""{ "email": "   " }""")]
    public async Task Corpo_sem_email_utilizavel_devolve_nulo(string corpo) =>
        (await Ler(corpo)).Should().BeNull();

    [Fact]
    public async Task Email_com_mais_de_256_caracteres_devolve_nulo() =>
        (await Ler($$"""{ "email": "{{new string('a', 257)}}" }""")).Should().BeNull();

    [Fact]
    public async Task Email_com_256_caracteres_e_aceito() =>
        (await Ler($$"""{ "email": "{{new string('a', 256)}}" }""")).Should().HaveLength(256);

    [Fact]
    public async Task Corpo_maior_que_16_KB_devolve_nulo_sem_interpretar() =>
        (await Ler($$"""{ "email": "maria@exemplo.com.br", "x": "{{new string('a', 17 * 1024)}}" }""")).Should().BeNull();

    [Theory]
    [InlineData("""{ "email": "maria@exemplo.com.br" }""")]
    [InlineData("{ invalido")]
    public async Task O_corpo_continua_legivel_para_a_rota(string corpo)
    {
        var requisicao = Requisicao(corpo);

        await LeitorEmailLogin.LerAsync(requisicao, TestContext.Current.CancellationToken);

        using var leitor = new StreamReader(requisicao.Body);
        (await leitor.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().Be(corpo);
    }

    [Fact]
    public async Task Corpo_grande_continua_inteiro_para_a_rota()
    {
        var corpo = new string('a', 64 * 1024);
        var requisicao = Requisicao(corpo);

        await LeitorEmailLogin.LerAsync(requisicao, TestContext.Current.CancellationToken);

        using var leitor = new StreamReader(requisicao.Body);
        (await leitor.ReadToEndAsync(TestContext.Current.CancellationToken)).Should().HaveLength(corpo.Length);
    }
}
