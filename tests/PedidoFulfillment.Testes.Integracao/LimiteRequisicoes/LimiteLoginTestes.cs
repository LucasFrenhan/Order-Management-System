using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.LimiteRequisicoes;

/// <summary>
/// O limite do login vale em todas as instâncias (REQ-23, US1): cada <see cref="FabricaApi"/> é uma
/// instância da API, e todas usam o mesmo SQL Server real.
/// </summary>
public sealed class LimiteLoginTestes(BancoDadosTeste banco)
{
    private const string Email = "maria@exemplo.com.br";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private string NovoBanco()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        return banco.NovoBanco();
    }

    [Fact]
    public async Task Esgotado_na_instancia_A_a_instancia_B_recusa_sem_avaliar_a_senha()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 3);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clienteA = apiA.CreateClient();
        var clienteB = apiB.CreateClient();
        (await CenarioLimite.RegistroAsync(clienteA, Email)).StatusCode.Should().Be(HttpStatusCode.Created);

        for (var i = 0; i < 3; i++)
        {
            (await CenarioLimite.LoginAsync(clienteA)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        // SC-002: todas as tentativas seguintes na outra instância, mesmo com a senha certa.
        for (var i = 0; i < 5; i++)
        {
            var resposta = await CenarioLimite.LoginAsync(clienteB, senha: CenarioLimite.SenhaValida);
            resposta.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            CenarioLimite.RetryAfter(resposta).Should().BePositive();
        }

        // FR-014: as barradas não contam para o bloqueio de conta (5 erros seguidos).
        (await BancoDadosTeste.ConsultarAsync<int>(conexao, "SELECT AccessFailedCount FROM Usuarios")).Should().Be(3);
    }

    [Fact]
    public async Task Rajada_simultanea_repartida_entre_as_instancias_nao_passa_do_limite()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 10);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clientes = new[] { apiA.CreateClient(), apiB.CreateClient() };

        var respostas = await Task.WhenAll(Enumerable.Range(0, 100).Select(i => CenarioLimite.LoginAsync(clientes[i % 2])));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(10);
        respostas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(90);
    }

    [Fact]
    public async Task Um_email_por_tentativa_e_contido_pelo_limite_por_origem()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigem: 10);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clientes = new[] { apiA.CreateClient(), apiB.CreateClient() };

        var respostas = await Task.WhenAll(Enumerable.Range(0, 40).Select(i => CenarioLimite.LoginAsync(clientes[i % 2], $"conta{i}@exemplo.com.br")));

        respostas.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(10);
        respostas.Count(r => r.StatusCode == HttpStatusCode.TooManyRequests).Should().Be(30);
    }

    [Fact]
    public async Task Outro_email_e_outra_origem_nao_sao_barrados()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 1);
        await using var api = CenarioLimite.Instancia(conexao, limites);
        await using var outraOrigem = CenarioLimite.Instancia(conexao, limites, CenarioLimite.OutraOrigem, prepararBanco: false);
        var cliente = api.CreateClient();
        await CenarioLimite.LoginAsync(cliente);
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        (await CenarioLimite.LoginAsync(cliente, "joao@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CenarioLimite.LoginAsync(outraOrigem.CreateClient())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [InlineData("MARIA@EXEMPLO.COM.BR")]
    [InlineData("  maria@exemplo.com.br ")]
    public async Task Email_com_outra_caixa_ou_espacos_e_o_mesmo(string variacao)
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(loginOrigemEmail: 1));
        var cliente = api.CreateClient();
        await CenarioLimite.LoginAsync(cliente);

        (await CenarioLimite.LoginAsync(cliente, variacao)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Insistir_em_um_email_barrado_nao_consome_o_limite_por_origem()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(loginOrigemEmail: 1, loginOrigem: 2));
        var cliente = api.CreateClient();
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        for (var i = 0; i < 5; i++)
        {
            (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        }

        (await CenarioLimite.LoginAsync(cliente, "joao@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await CenarioLimite.LoginAsync(cliente, "ana@exemplo.com.br")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Corpo_invalido_conta_pela_origem_e_segue_respondendo_400()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(loginOrigem: 2));
        var cliente = api.CreateClient();

        Task<HttpResponseMessage> Invalido() => cliente.PostAsync(
            CenarioLimite.RotaLogin, new StringContent("{ invalido", System.Text.Encoding.UTF8, "application/json"), Cancelamento);

        (await Invalido()).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await cliente.PostAsJsonAsync(CenarioLimite.RotaLogin, new { senha = "x" }, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Invalido()).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Depois_do_retry_after_aceita_em_qualquer_instancia(bool naOutraInstancia)
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 1, janela: "2");
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clienteA = apiA.CreateClient();
        await CenarioLimite.LoginAsync(clienteA);
        var barrada = await CenarioLimite.LoginAsync(clienteA);
        barrada.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        await Task.Delay(TimeSpan.FromSeconds(CenarioLimite.RetryAfter(barrada)), Cancelamento);

        var cliente = naOutraInstancia ? apiB.CreateClient() : clienteA;
        (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_contagem_sobrevive_ao_descarte_de_uma_instancia()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 1);
        await using (var api = CenarioLimite.Instancia(conexao, limites))
        {
            await CenarioLimite.LoginAsync(api.CreateClient());
        }

        await using var nova = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);

        (await CenarioLimite.LoginAsync(nova.CreateClient())).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task O_retry_after_difere_no_maximo_1_s_entre_as_instancias()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 1);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var apiB = CenarioLimite.Instancia(conexao, limites, prepararBanco: false);
        var clienteA = apiA.CreateClient();
        await CenarioLimite.LoginAsync(clienteA);

        var deA = CenarioLimite.RetryAfter(await CenarioLimite.LoginAsync(clienteA));
        var deB = CenarioLimite.RetryAfter(await CenarioLimite.LoginAsync(apiB.CreateClient()));

        Math.Abs(deA - deB).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Instancia_com_o_relogio_errado_decide_igual()
    {
        var conexao = NovoBanco();
        var limites = CenarioLimite.Limites(loginOrigemEmail: 1);
        await using var apiA = CenarioLimite.Instancia(conexao, limites);
        await using var adiantada = CenarioLimite.Instancia(conexao, limites, relogio: new RelogioAdiantado(TimeSpan.FromMinutes(10)), prepararBanco: false);
        var clienteA = apiA.CreateClient();
        var clienteAdiantado = adiantada.CreateClient();
        await CenarioLimite.LoginAsync(clienteA);

        var deA = await CenarioLimite.LoginAsync(clienteA);
        var primeira = await CenarioLimite.LoginAsync(clienteAdiantado);
        var segunda = await CenarioLimite.LoginAsync(clienteAdiantado);

        primeira.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        segunda.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        Math.Abs(CenarioLimite.RetryAfter(deA) - CenarioLimite.RetryAfter(primeira)).Should().BeLessThanOrEqualTo(1);
        Math.Abs(CenarioLimite.RetryAfter(deA) - CenarioLimite.RetryAfter(segunda)).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Dentro_do_limite_o_bloqueio_de_conta_continua_como_esta()
    {
        await using var api = CenarioLimite.Instancia(NovoBanco(), CenarioLimite.Limites(loginOrigemEmail: 10));
        var cliente = api.CreateClient();
        await CenarioLimite.RegistroAsync(cliente, Email);
        (await CenarioLimite.LoginAsync(cliente, senha: CenarioLimite.SenhaValida)).StatusCode.Should().Be(HttpStatusCode.OK);
        for (var i = 0; i < 5; i++)
        {
            (await CenarioLimite.LoginAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        (await CenarioLimite.LoginAsync(cliente, senha: CenarioLimite.SenhaValida)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class RelogioAdiantado(TimeSpan diferenca) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => base.GetUtcNow() + diferenca;
    }
}
