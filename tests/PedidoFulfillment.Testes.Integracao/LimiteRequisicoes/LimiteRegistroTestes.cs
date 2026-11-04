using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>O limite do registro vale em todas as instâncias (REQ-23, US2, FR-005, FR-014).</summary>
public sealed class LimiteRegistroTestes(BancoDadosTeste banco)
{
    private string NovoBanco()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        return banco.NovoBanco();
    }

    [Fact]
    public async Task Esgotado_na_instancia_A_a_instancia_B_recusa_e_nao_cria_a_conta()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(registro: 2);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clienteA = apiA.CreateClient();
        (await CenarioLimite.RegistroAsync(clienteA, "a@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CenarioLimite.RegistroAsync(clienteA, "b@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Created);

        var resposta = await CenarioLimite.RegistroAsync(apiB.CreateClient(), "c@exemplo.com.br");

        resposta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        CenarioLimite.RetryAfter(resposta).Should().BePositive();
        (await BancoDadosTeste.ConsultarAsync<int>(conexao, "SELECT COUNT(*) FROM Usuarios WHERE NormalizedEmail = 'C@EXEMPLO.COM.BR'")).Should().Be(0);
    }

    [Fact]
    public async Task Outra_origem_e_aceita()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(registro: 1);
        await using var api = CenarioLimite.Instancia(conexao, limites);
        await using var outraOrigem = CenarioLimite.Instancia(conexao, limites, CenarioLimite.OutraOrigem, prepararBanco: false);
        var cliente = api.CreateClient();
        await CenarioLimite.RegistroAsync(cliente, "a@exemplo.com.br");
        (await CenarioLimite.RegistroAsync(cliente, "b@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await CenarioLimite.RegistroAsync(outraOrigem.CreateClient(), "c@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Barrado_no_registro_o_login_da_mesma_origem_nao_e_afetado()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(registro: 1));
        var cliente = api.CreateClient();
        await CenarioLimite.RegistroAsync(cliente, "a@exemplo.com.br");
        (await CenarioLimite.RegistroAsync(cliente, "b@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await CenarioLimite.LoginAsync(cliente, "a@exemplo.com.br", CenarioLimite.SenhaValida)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Barrado_no_login_o_registro_da_mesma_origem_nao_e_afetado()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(loginOrigem: 1));
        var cliente = api.CreateClient();
        await CenarioLimite.LoginAsync(cliente);
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await CenarioLimite.RegistroAsync(cliente, "a@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Outras_rotas_nao_sao_limitadas()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(registro: 1, loginOrigem: 1));
        var cliente = api.CreateClient();
        await CenarioLimite.RegistroAsync(cliente, "a@exemplo.com.br");
        (await CenarioLimite.RegistroAsync(cliente, "b@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        for (var i = 0; i < 5; i++)
        {
            (await cliente.GetAsync("/health", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}
