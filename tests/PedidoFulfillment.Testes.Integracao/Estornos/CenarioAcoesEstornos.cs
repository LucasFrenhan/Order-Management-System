using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioEstorno;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>Um estorno pendente criado pelo cenário: o pedido e o id do estorno (o do pagamento, R-01).</summary>
internal sealed record EstornoCriado(Guid PedidoId, Guid EstornoId);

/// <summary>
/// Cenário das ações da operação sobre os estornos (REQ-28): pedidos pagos e cancelados pela operação,
/// com o estorno pendente e o Worker parado, e ajustes direto no banco para as situações difíceis de
/// produzir pela API (falhas antigas, alerta, confirmado, baixado).
/// </summary>
internal static class CenarioAcoesEstornos
{
    public const string Rota = "/api/v1/estornos";
    public const string MotivoValido = "Devolvido por transferência pelo financeiro.";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    /// <summary>Banco novo com <paramref name="quantidade"/> estornos pendentes, na ordem de criação.</summary>
    public static async Task<(string StringConexao, List<EstornoCriado> Estornos)> PendentesAsync(MensageriaTeste mensageria, int quantidade)
    {
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var pedidos = await PedidosPagosAsync(mensageria, stringConexao, cliente, quantidade);
        var operador = await OperadorAsync(api);
        var estornos = new List<EstornoCriado>();
        foreach (var pedidoId in pedidos)
        {
            await CancelarComoOperacaoAsync(operador, pedidoId);
            estornos.Add(new EstornoCriado(pedidoId, await EstornoIdAsync(stringConexao, pedidoId)));
        }

        return (stringConexao, estornos);
    }

    public static Task<Guid> EstornoIdAsync(string stringConexao, Guid pedidoId) =>
        BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT Id FROM Pagamentos WHERE PedidoId = '{pedidoId}'");

    /// <summary>Atualiza colunas do pagamento do estorno, com SQL de teste.</summary>
    public static Task AjustarAsync(string stringConexao, Guid estornoId, string atribuicoes) =>
        BancoDadosTeste.ExecutarAsync(stringConexao, $"UPDATE Pagamentos SET {atribuicoes} WHERE Id = '{estornoId}'");

    /// <summary>Instante do pedido de estorno, para controlar a ordem e o alerta.</summary>
    public static Task SolicitadoEmAsync(string stringConexao, Guid estornoId, DateTimeOffset instante) =>
        AjustarAsync(stringConexao, estornoId, $"EstornoSolicitadoEm = '{instante:O}'");

    public static Task ConfirmadoAsync(string stringConexao, Guid estornoId) =>
        AjustarAsync(stringConexao, estornoId,
            "Situacao = N'Estornado', IdentificadorEstorno = N'SIM-EST-TESTE', EstornadoEm = SYSDATETIMEOFFSET()");

    public static Task BaixadoAsync(string stringConexao, Guid estornoId, Guid autorId) =>
        AjustarAsync(stringConexao, estornoId,
            $"Situacao = N'EstornoBaixado', EstornoBaixadoEm = SYSDATETIMEOFFSET(), EstornoBaixadoPorId = '{autorId}', MotivoBaixaEstorno = N'{MotivoValido}', ReferenciaExternaBaixaEstorno = N'TED-1'");

    public static Task<HttpResponseMessage> ListarAsync(HttpClient cliente, string consulta = "") =>
        cliente.GetAsync($"{Rota}/pendentes{consulta}", Cancelamento);

    public static Task<HttpResponseMessage> ObterAsync(HttpClient cliente, object id) =>
        cliente.GetAsync($"{Rota}/{id}", Cancelamento);

    public static Task<HttpResponseMessage> BaixarAsync(HttpClient cliente, object id, string? motivo = MotivoValido, string? referenciaExterna = null) =>
        cliente.PostAsJsonAsync($"{Rota}/{id}/baixa", new { motivo, referenciaExterna }, Cancelamento);

    public static Task<HttpResponseMessage> ForcarAsync(HttpClient cliente, object id) =>
        cliente.PostAsync($"{Rota}/{id}/tentativas", null, Cancelamento);

    public static async Task<JsonElement> CorpoOkAsync(HttpResponseMessage resposta, System.Net.HttpStatusCode esperado = System.Net.HttpStatusCode.OK)
    {
        resposta.StatusCode.Should().Be(esperado, await resposta.Content.ReadAsStringAsync(Cancelamento));
        return await CorpoAsync(resposta);
    }

    public static Guid[] IdsDosItens(JsonElement pagina) =>
        [.. pagina.GetProperty("itens").EnumerateArray().Select(i => i.GetProperty("estornoId").GetGuid())];
}
