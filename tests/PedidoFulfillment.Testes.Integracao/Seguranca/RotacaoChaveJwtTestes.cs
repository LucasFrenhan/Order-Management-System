using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Seguranca;

/// <summary>
/// Rotação da chave do JWT sem banco (REQ-27, US1 e US2): cada etapa é uma instância com outra configuração, e a
/// rota protegida usa o <see cref="ServicoPedidosFalso"/>, como em <c>Autenticacao/EmissorTokenTestes</c>.
/// </summary>
public sealed class RotacaoChaveJwtTestes
{
    private const string ChaveA = "chave-a";
    private const string ChaveB = "chave-b";
    private const string ValorA = "valor-secreto-da-chave-a-com-mais-de-32-bytes";
    private const string ValorB = "valor-secreto-da-chave-b-com-mais-de-32-bytes";
    private const string Rota = "/api/v1/pedidos/0f8fad5b-d9cb-469f-a165-70867728950e";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Uma instância com as chaves dadas, na ordem, e a ativa.</summary>
    private static FabricaApi Api(string ativa, (string Identificador, string Valor)[] chaves, CapturaLogs? logs = null)
    {
        var configuracoes = new Dictionary<string, string?> { ["Jwt:ChaveAtiva"] = ativa };
        for (var i = 0; i < chaves.Length; i++)
        {
            configuracoes[$"Jwt:Chaves:{i}:Identificador"] = chaves[i].Identificador;
            configuracoes[$"Jwt:Chaves:{i}:Valor"] = chaves[i].Valor;
        }

        return new FabricaApi
        {
            Configuracoes = configuracoes,
            Logs = logs,
            ServicoPedidos = new ServicoPedidosFalso { ResultadoObter = Exemplos.Pedido() }
        };
    }

    private static string Emitir(FabricaApi api) =>
        api.Services.GetRequiredService<IEmissorToken>().Emitir(Guid.NewGuid(), [PerfilUsuario.Admin]).TokenAcesso;

    private static string Kid(string token) => new JsonWebTokenHandler().ReadJsonWebToken(token).Kid;

    private static string TokenDe(string identificador, string valor) =>
        GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Admin], chave: valor, identificadorChave: identificador);

    private static async Task<HttpStatusCode> StatusAsync(FabricaApi api, string token) =>
        (await api.CreateClient().ComToken(token).GetAsync(Rota, Cancelamento)).StatusCode;

    [Fact]
    public async Task Etapa_1_emite_com_a_ativa_e_aceita_tambem_a_nova()
    {
        await using var api = Api(ChaveA, [(ChaveA, ValorA), (ChaveB, ValorB)]);

        Kid(Emitir(api)).Should().Be(ChaveA);
        (await StatusAsync(api, TokenDe(ChaveA, ValorA))).Should().Be(HttpStatusCode.OK);
        (await StatusAsync(api, TokenDe(ChaveB, ValorB))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Etapa_2_emite_com_a_nova_e_ainda_aceita_a_antiga()
    {
        await using var api = Api(ChaveB, [(ChaveA, ValorA), (ChaveB, ValorB)]);

        Kid(Emitir(api)).Should().Be(ChaveB);
        (await StatusAsync(api, TokenDe(ChaveA, ValorA))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Etapa_3_so_com_a_nova_aceita_os_tokens_dela()
    {
        await using var api = Api(ChaveB, [(ChaveB, ValorB)]);

        (await StatusAsync(api, TokenDe(ChaveB, ValorB))).Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task As_tres_etapas_em_sequencia_nao_recusam_nenhum_token_valido()
    {
        var respostas = new List<HttpStatusCode>();
        await using var antes = Api(ChaveA, [(ChaveA, ValorA)]);
        var tokenA = Emitir(antes);

        await using (var etapa1 = Api(ChaveA, [(ChaveA, ValorA), (ChaveB, ValorB)]))
        {
            respostas.Add(await StatusAsync(etapa1, tokenA));
        }

        string tokenB;
        await using (var etapa2 = Api(ChaveB, [(ChaveA, ValorA), (ChaveB, ValorB)]))
        {
            respostas.Add(await StatusAsync(etapa2, tokenA));
            tokenB = Emitir(etapa2);
            respostas.Add(await StatusAsync(etapa2, tokenB));
        }

        await using (var etapa3 = Api(ChaveB, [(ChaveB, ValorB)]))
        {
            respostas.Add(await StatusAsync(etapa3, tokenB));
        }

        Kid(tokenA).Should().Be(ChaveA);
        Kid(tokenB).Should().Be(ChaveB);
        respostas.Should().AllBeEquivalentTo(HttpStatusCode.OK, "nenhum token válido é recusado na rotação (SC-001)");
    }

    [Fact]
    public async Task Token_de_chave_retirada_recebe_o_mesmo_401_de_um_token_invalido()
    {
        await using var api = Api(ChaveB, [(ChaveB, ValorB)]);
        var cliente = api.CreateClient();

        var retirada = await cliente.ComToken(TokenDe(ChaveA, ValorA)).GetAsync(Rota, Cancelamento);
        var invalido = await api.CreateClient()
            .ComToken(GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Admin], assinaturaInvalida: true, identificadorChave: ChaveB))
            .GetAsync(Rota, Cancelamento);

        retirada.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        retirada.Headers.WwwAuthenticate.ToString().Should().Be("Bearer");
        var corpo = await Erros.FormatoErroTestes.DeveSerProblemDetails(retirada, 401, Rota);
        corpo.GetProperty("title").GetString().Should().Be("Não autenticado.");
        corpo.GetProperty("detail").GetString().Should().Be("Informe um token de acesso válido.");
        var corpoInvalido = await Erros.FormatoErroTestes.DeveSerProblemDetails(invalido, 401, Rota);
        corpoInvalido.GetProperty("title").GetString().Should().Be(corpo.GetProperty("title").GetString());
        corpoInvalido.GetProperty("detail").GetString().Should().Be(corpo.GetProperty("detail").GetString());
    }

    public static TheoryData<string, string?, string> TokensRecusados => new()
    {
        // Chave retirada; forjado depois da retirada é o mesmo caso: quem tem o valor antigo não passa.
        { "chave retirada", ChaveA, ValorA },
        // Sem kid, mesmo assinado com a chave ativa (FR-014).
        { "sem kid", null, ValorB },
        { "kid desconhecido", "chave-x", ValorB },
        // A comparação do kid é ordinal: só a caixa diferente já é outra chave.
        { "kid em outra caixa", "CHAVE-B", ValorB },
        // kid configurado, mas assinado com outro valor.
        { "assinatura errada", ChaveB, ValorA }
    };

    [Theory]
    [MemberData(nameof(TokensRecusados))]
    public async Task Token_fora_do_conjunto_recebe_401(string caso, string? identificador, string valor)
    {
        await using var api = Api(ChaveB, [(ChaveB, ValorB)]);

        (await StatusAsync(api, TokenDe(identificador!, valor))).Should().Be(HttpStatusCode.Unauthorized, caso);
    }

    [Theory]
    [InlineData(ChaveA, "chave-a")]
    [InlineData(null, "(ausente)")]
    [InlineData("quebra\nde-linha", "(inválido)")]
    [InlineData("x-com-65-caracteres-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx", "(inválido)")]
    public async Task Recusa_por_chave_fora_do_conjunto_registra_o_3027(string? kid, string registrado)
    {
        var logs = new CapturaLogs();
        await using var api = Api(ChaveB, [(ChaveB, ValorB)], logs);
        var token = TokenDe(kid!, ValorA);

        (await StatusAsync(api, token)).Should().Be(HttpStatusCode.Unauthorized);

        var registro = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3027).Subject;
        registro.Texto.Should().StartWith($"Token recusado: chave {registrado} fora da configuração.");
        registro.Nivel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
        NenhumRegistroComSegredo(logs, token);
    }

    [Fact]
    public async Task Sem_3027_para_assinatura_errada_com_kid_conhecido_nem_para_token_aceito()
    {
        var logs = new CapturaLogs();
        await using var api = Api(ChaveB, [(ChaveB, ValorB)], logs);
        var errado = TokenDe(ChaveB, ValorA);
        var certo = TokenDe(ChaveB, ValorB);

        (await StatusAsync(api, errado)).Should().Be(HttpStatusCode.Unauthorized);
        (await StatusAsync(api, certo)).Should().Be(HttpStatusCode.OK);

        logs.Registros.Should().NotContain(r => r.Evento.Id == 3027);
        NenhumRegistroComSegredo(logs, errado);
        NenhumRegistroComSegredo(logs, certo);
    }

    /// <summary>Nenhum log, nem os da biblioteca (Microsoft.*, mensagens IDX), traz o token, o sub ou um valor de chave.</summary>
    private static void NenhumRegistroComSegredo(CapturaLogs logs, string token)
    {
        var sub = new JsonWebTokenHandler().ReadJsonWebToken(token).Subject;
        var assinatura = token[(token.LastIndexOf('.') + 1)..];
        logs.Textos.Should().NotContain(t => t.Contains(ValorA) || t.Contains(ValorB) || t.Contains(assinatura) || t.Contains(sub),
            "FR-006 e FR-016: nenhum valor de chave, token ou usuário nos logs");
    }

    [Fact]
    public async Task Partida_registra_a_chave_ativa_sem_aceitas()
    {
        var logs = new CapturaLogs();
        await using var api = Api(ChaveA, [(ChaveA, ValorA)], logs);
        _ = api.CreateClient();

        var registro = logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3026).Subject;
        registro.Texto.Should().StartWith("Chaves do JWT: ativa chave-a; aceitas -.");
        logs.Textos.Should().NotContain(t => t.Contains(ValorA));
    }

    [Fact]
    public async Task Partida_registra_a_ativa_e_as_aceitas_na_ordem_da_configuracao()
    {
        const string ChaveC = "chave-c";
        const string ValorC = "valor-secreto-da-chave-c-com-mais-de-32-bytes";
        var logs = new CapturaLogs();
        await using var api = Api(ChaveB, [(ChaveA, ValorA), (ChaveB, ValorB), (ChaveC, ValorC)], logs);
        _ = api.CreateClient();

        logs.Registros.Should().ContainSingle(r => r.Evento.Id == 3026)
            .Which.Texto.Should().StartWith("Chaves do JWT: ativa chave-b; aceitas chave-a, chave-c.");
        logs.Textos.Should().NotContain(t => t.Contains(ValorA) || t.Contains(ValorB) || t.Contains(ValorC));
    }
}
