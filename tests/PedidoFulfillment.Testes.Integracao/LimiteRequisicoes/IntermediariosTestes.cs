using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>
/// A origem é o cliente real, e o cabeçalho só vale de um intermediário confiável (REQ-23, FR-004, R-06).
/// Cada teste usa o limite de 1 por origem: o segundo 401 prova que as origens foram vistas como diferentes.
/// </summary>
public sealed class IntermediariosTestes
{
    private const string Balanceador = "10.0.0.1";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Dictionary<string, string?> Configuracao(params string[] intermediarios)
    {
        var valores = CenarioLimite.Limites(loginOrigem: 1);
        for (var i = 0; i < intermediarios.Length; i++)
        {
            valores[$"LimiteRequisicoes:IntermediariosConfiaveis:{i}"] = intermediarios[i];
        }

        return valores;
    }

    private static Task<HttpResponseMessage> Login(HttpClient cliente, string? encaminhado)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, CenarioLimite.RotaLogin)
        {
            Content = JsonContent.Create(new { email = "maria@exemplo.com.br", senha = "SenhaForte1" })
        };
        if (encaminhado is not null)
        {
            requisicao.Headers.Add("X-Forwarded-For", encaminhado);
        }

        return cliente.SendAsync(requisicao, Cancelamento);
    }

    [Fact]
    public async Task Sem_intermediario_configurado_o_cabecalho_e_ignorado()
    {
        await using var api = CenarioLimite.SemBanco(Configuracao());
        var cliente = api.CreateClient();

        (await Login(cliente, "198.51.100.1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await Login(cliente, "198.51.100.2")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public async Task Sem_intermediario_configurado_nem_o_loopback_e_confiavel(string conexao)
    {
        await using var api = CenarioLimite.SemBanco(Configuracao(), IPAddress.Parse(conexao));
        var cliente = api.CreateClient();
        await Login(cliente, "198.51.100.1");

        (await Login(cliente, "198.51.100.2")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("198.51.100.9")]
    public async Task Com_intermediario_configurado_quem_nao_e_ele_e_ignorado(string conexao)
    {
        await using var api = CenarioLimite.SemBanco(Configuracao(Balanceador), IPAddress.Parse(conexao));
        var cliente = api.CreateClient();
        await Login(cliente, "198.51.100.1");

        (await Login(cliente, "198.51.100.2")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Do_intermediario_confiavel_conta_o_cliente_real()
    {
        await using var api = CenarioLimite.SemBanco(Configuracao(Balanceador), IPAddress.Parse(Balanceador));
        var cliente = api.CreateClient();

        (await Login(cliente, "198.51.100.1")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(cliente, "198.51.100.2")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Login(cliente, "198.51.100.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Valor_forjado_a_esquerda_da_cadeia_e_ignorado()
    {
        await using var api = CenarioLimite.SemBanco(Configuracao(Balanceador), IPAddress.Parse(Balanceador));
        var cliente = api.CreateClient();
        await Login(cliente, "1.1.1.1, 198.51.100.1");

        (await Login(cliente, "2.2.2.2, 198.51.100.1")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Uma_faixa_confia_em_todos_os_intermediarios_da_cadeia()
    {
        await using var api = CenarioLimite.SemBanco(Configuracao("10.0.0.0/8"), IPAddress.Parse(Balanceador));
        var cliente = api.CreateClient();
        await Login(cliente, "198.51.100.1, 10.0.0.2");

        (await Login(cliente, "198.51.100.1, 10.0.0.3")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Login(cliente, "198.51.100.2, 10.0.0.2")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Enderecos_ipv6_do_mesmo_bloco_dividem_a_contagem()
    {
        var estado = new ArmazenamentoContagensMemoria();
        await using var primeiro = CenarioLimite.SemBanco(Configuracao(), IPAddress.Parse("2001:db8:1:2::1"), estado);
        await using var mesmoBloco = CenarioLimite.SemBanco(Configuracao(), IPAddress.Parse("2001:db8:1:2:ffff::9"), estado);
        await using var outroBloco = CenarioLimite.SemBanco(Configuracao(), IPAddress.Parse("2001:db8:1:3::1"), estado);
        await Login(primeiro.CreateClient(), null);

        (await Login(mesmoBloco.CreateClient(), null)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Login(outroBloco.CreateClient(), null)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
