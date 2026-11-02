using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Conformidade do corpo <c>notificacao.v1</c> com o schema publicado em
/// <c>specs/018-notificacao-status/contracts/notificacoes/</c> (REQ-18, FR-006; constituição V).
/// </summary>
public sealed class SchemaNotificacaoTestes
{
    private static readonly EvaluationOptions Opcoes = new() { RequireFormatValidation = true, OutputFormat = OutputFormat.List };

    // Carregado uma vez: o registro de schemas é global, e o mesmo $id não pode ser registrado duas vezes.
    private static readonly JsonSchema Schema =
        JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Notificacoes", "notificacao.v1.schema.json"));

    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private static string StatusAlterado() => SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosStatusAlterado>
    {
        IdNotificacao = Guid.CreateVersion7(),
        Tipo = TiposNotificacao.StatusAlterado,
        Versao = TiposNotificacao.VersaoAtual,
        OcorridoEm = DateTimeOffset.UtcNow,
        Dados = new DadosStatusAlterado
        {
            PedidoId = PedidoId,
            Numero = "PED-20261101-000042",
            StatusAnterior = StatusPedido.ProntoParaEnvio,
            StatusNovo = StatusPedido.Despachado,
            Sequencia = 4,
            Link = SerializacaoNotificacoes.Link(PedidoId)
        }
    });

    private static string EstornoConfirmado() => SerializacaoNotificacoes.Serializar(new CorpoNotificacao<DadosEstornoConfirmado>
    {
        IdNotificacao = Guid.CreateVersion7(),
        Tipo = TiposNotificacao.EstornoConfirmado,
        Versao = TiposNotificacao.VersaoAtual,
        OcorridoEm = DateTimeOffset.UtcNow,
        Dados = new DadosEstornoConfirmado { PedidoId = PedidoId, Numero = "PED-20261101-000042", Link = SerializacaoNotificacoes.Link(PedidoId) }
    });

    [Fact]
    public void Os_dois_tipos_seguem_o_schema()
    {
        DeveSerValido(StatusAlterado()).Should().BeTrue();
        DeveSerValido(EstornoConfirmado()).Should().BeTrue();
    }

    [Theory]
    [InlineData("sem idNotificacao")]
    [InlineData("versão 2")]
    [InlineData("tipo desconhecido")]
    [InlineData("campo a mais nos dados")]
    [InlineData("sequência 1")]
    [InlineData("link de outro lugar")]
    [InlineData("status nos dados do estorno")]
    public void Corpo_fora_do_contrato_e_recusado(string caso)
    {
        var raiz = JsonNode.Parse(caso == "status nos dados do estorno" ? EstornoConfirmado() : StatusAlterado())!.AsObject();
        var dados = raiz["dados"]!.AsObject();
        switch (caso)
        {
            case "sem idNotificacao": raiz.Remove("idNotificacao"); break;
            case "versão 2": raiz["versao"] = 2; break;
            case "tipo desconhecido": raiz["tipo"] = "pedido.despachado"; break;
            case "campo a mais nos dados": dados["email"] = "cliente@exemplo.com"; break;
            case "sequência 1": dados["sequencia"] = 1; break;
            case "link de outro lugar": dados["link"] = "https://exemplo.com/x"; break;
            case "status nos dados do estorno": dados["statusNovo"] = "Cancelado"; break;
        }

        DeveSerValido(raiz.ToJsonString()).Should().BeFalse(caso);
    }

    /// <summary>Também usado pelos testes dos avisos, dos webhooks e do SSE.</summary>
    internal static bool DeveSerValido(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return Schema.Evaluate(documento.RootElement, Opcoes).IsValid;
    }
}
