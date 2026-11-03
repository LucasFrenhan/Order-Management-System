using System.Net;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Endpoints;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioListagem;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Pedidos;

/// <summary>REQ-21, User Story 3: 400 com as mensagens da research R-05, nunca 500 (SC-006).</summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ListagemPedidosErrosTestes(BancoDadosTeste banco)
{
    private const string MensagemCursor = "O cursor é inválido para esta consulta; recomece da primeira página.";

    [Fact]
    public async Task Parametros_invalidos_dao_400_com_as_mensagens_e_todos_os_erros_juntos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var operador = await OperadorAsync(api);
        var casos = new (string Consulta, string Chave, string Mensagem)[]
        {
            ("tamanho=0", "tamanho", "O tamanho deve ser um número entre 1 e 100."),
            ("tamanho=101", "tamanho", "O tamanho deve ser um número entre 1 e 100."),
            ("tamanho=abc", "tamanho", "O tamanho deve ser um número entre 1 e 100."),
            ("status=Enviado", "status", "O status 'Enviado' não existe."),
            ("criadoDe=2026-11-03T10:00:00", "criadoDe", "O parâmetro criadoDe deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00."),
            ("atualizadoAte=ontem", "atualizadoAte", "O parâmetro atualizadoAte deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00."),
            ($"criadoDe={Data(DateTimeOffset.Parse("2026-11-03T10:00:00Z"))}&criadoAte={Data(DateTimeOffset.Parse("2026-11-02T10:00:00Z"))}",
                "criadoDe", "O início do período de criação deve ser anterior ou igual ao fim."),
            ("clienteId=abc", "clienteId", "O parâmetro clienteId deve ser um UUID.")
        };

        foreach (var (consulta, chave, mensagem) in casos)
        {
            var erros = await EndpointsAutenticacaoTestes.ErrosDe(await ListarAsync(operador, consulta), Rota);
            erros.Should().ContainKey(chave).WhoseValue.Should().Equal([mensagem], consulta);
        }

        var juntos = await EndpointsAutenticacaoTestes.ErrosDe(
            await ListarAsync(operador, "tamanho=0&status=Enviado&criadoDe=2026-11-03T10:00:00"), Rota);
        juntos.Keys.Should().BeEquivalentTo(["tamanho", "status", "criadoDe"]);
    }

    [Fact]
    public async Task Cursor_malformado_adulterado_de_outros_filtros_ou_de_outro_usuario_da_400()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        for (var i = 0; i < 3; i++)
        {
            await PedidoAsync(dono);
        }

        var cursor = Proximo(await PaginaAsync(dono, "tamanho=1"))!;
        var trocado = cursor[..^1] + (cursor[^1] == 'A' ? 'B' : 'A');
        var tentativas = new (HttpClient Cliente, string Consulta)[]
        {
            (dono, "tamanho=1&cursor=nao-e-um-cursor"),
            (dono, $"tamanho=1&{Cursor(trocado)}"),
            (dono, $"tamanho=1&status=Pendente&{Cursor(cursor)}"),
            (outro, $"tamanho=1&{Cursor(cursor)}"),
            (await AdminAsync(api), $"tamanho=1&{Cursor(cursor)}")
        };

        foreach (var (cliente, consulta) in tentativas)
        {
            var resposta = await ListarAsync(cliente, consulta);
            resposta.StatusCode.Should().NotBe(HttpStatusCode.InternalServerError);
            var erros = await EndpointsAutenticacaoTestes.ErrosDe(resposta, Rota);
            erros.Should().ContainKey("cursor").WhoseValue.Should().Equal([MensagemCursor], consulta);
        }

        (await ListarAsync(dono, $"tamanho=5&{Cursor(cursor)}")).StatusCode.Should().Be(HttpStatusCode.OK, "o tamanho pode mudar");
    }
}
