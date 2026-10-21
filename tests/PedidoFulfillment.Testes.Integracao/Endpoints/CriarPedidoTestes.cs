using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Erros;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Endpoints;

/// <summary>
/// User Story 3: POST /api/v1/pedidos restrito a Cliente, com X-Idempotency-Key obrigatório.
/// </summary>
public sealed class CriarPedidoTestes(FabricaApi fabrica) : IClassFixture<FabricaApi>
{
    private const string Rota = "/api/v1/pedidos";
    private const string CabecalhoIdempotencia = "X-Idempotency-Key";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    public static TheoryData<string> TokensInvalidos => new()
    {
        "",
        GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Cliente], expirado: true),
        GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Cliente], assinaturaInvalida: true),
        GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Cliente], audiencia: "outra-api")
    };

    [Theory]
    [MemberData(nameof(TokensInvalidos))]
    public async Task Sem_token_ou_com_token_invalido_responde_401(string token)
    {
        var cliente = fabrica.CreateClient();
        if (token.Length > 0)
        {
            cliente.ComToken(token);
        }

        var resposta = await cliente.SendAsync(Requisicao(Guid.NewGuid().ToString()), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 401, Rota);
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Perfil_diferente_de_cliente_responde_403(PerfilUsuario perfil)
    {
        var resposta = await fabrica.CreateClient().ComToken(perfil).SendAsync(Requisicao(Guid.NewGuid().ToString()), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        await FormatoErroTestes.DeveSerProblemDetails(resposta, 403, Rota);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nao-e-uuid")]
    public async Task Chave_de_idempotencia_ausente_ou_invalida_responde_400(string? chave)
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente).SendAsync(Requisicao(chave), Cancelamento);

        var erros = await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota);
        erros.Should().ContainKey(CabecalhoIdempotencia);
    }

    [Fact]
    public async Task Pedido_sem_itens_responde_400_com_erro_em_itens()
    {
        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente)
            .SendAsync(Requisicao(Guid.NewGuid().ToString(), new { itens = Array.Empty<object>() }), Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Should().ContainKey("itens");
    }

    [Fact]
    public async Task Quantidade_invalida_aponta_o_item_no_erro()
    {
        var corpo = new { itens = new[] { new { produtoId = Exemplos.ProdutoTeclado, quantidade = 0 } } };

        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente)
            .SendAsync(Requisicao(Guid.NewGuid().ToString(), corpo), Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Should().ContainKey("itens[0].quantidade");
    }

    [Fact]
    public async Task Quantidade_como_texto_aponta_o_campo_no_erro()
    {
        var corpo = new { itens = new[] { new { produtoId = Exemplos.ProdutoTeclado, quantidade = "2" } } };

        var resposta = await fabrica.CreateClient().ComToken(PerfilUsuario.Cliente)
            .SendAsync(Requisicao(Guid.NewGuid().ToString(), corpo), Cancelamento);

        (await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota)).Should().ContainKey("itens[0].quantidade");
    }

    [Fact]
    public async Task Pedido_criado_responde_201_com_location_e_repassa_cliente_e_chave_ao_servico()
    {
        var clienteId = Guid.NewGuid();
        var chave = Guid.NewGuid();
        var falso = new ServicoPedidosFalso { ResultadoCriar = Exemplos.Pedido(clienteId: clienteId) };
        await using var api = new FabricaApi { ServicoPedidos = falso };
        var cliente = api.CreateClient().ComToken(GeradorTokenTeste.Gerar(clienteId, [PerfilUsuario.Cliente]));

        var resposta = await cliente.SendAsync(Requisicao(chave.ToString()), Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.Created);
        resposta.Headers.Location!.ToString().Should().Be("/api/v1/pedidos/0f8fad5b-d9cb-469f-a165-70867728950e");
        var pedido = await resposta.Content.ReadFromJsonAsync<JsonElement>(Cancelamento);
        pedido.GetProperty("status").GetString().Should().Be("Pendente");
        falso.UltimoClienteId.Should().Be(clienteId);
        falso.UltimaChaveIdempotencia.Should().Be(chave);
        falso.UltimoPedidoCriado!.Itens.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(TipoFalha.Conflito, CodigosErro.IdempotenciaConteudoDivergente, 409)]
    [InlineData(TipoFalha.Conflito, CodigosErro.IdempotenciaEmProcessamento, 409)]
    [InlineData(TipoFalha.RegraNegocio, CodigosErro.ProdutoInexistente, 422)]
    [InlineData(TipoFalha.RegraNegocio, CodigosErro.ProdutoIndisponivel, 422)]
    public async Task Falhas_de_negocio_respondem_com_codigo_estavel(TipoFalha tipo, string codigo, int status)
    {
        var falso = new ServicoPedidosFalso { ResultadoCriar = new Falha(tipo, "Falha de negócio.", Codigo: codigo) };
        await using var api = new FabricaApi { ServicoPedidos = falso };

        var resposta = await api.CreateClient().ComToken(PerfilUsuario.Cliente).SendAsync(Requisicao(Guid.NewGuid().ToString()), Cancelamento);

        ((int)resposta.StatusCode).Should().Be(status);
        var corpo = await FormatoErroTestes.DeveSerProblemDetails(resposta, status, Rota);
        corpo.GetProperty("codigo").GetString().Should().Be(codigo);
    }

    private static HttpRequestMessage Requisicao(string? chaveIdempotencia, object? corpo = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Post, Rota) { Content = JsonContent.Create(corpo ?? Exemplos.CorpoCriarPedido()) };
        if (chaveIdempotencia is not null)
        {
            requisicao.Headers.TryAddWithoutValidation(CabecalhoIdempotencia, chaveIdempotencia);
        }

        return requisicao;
    }
}
