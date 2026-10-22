using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Json.Schema;
using PedidoFulfillment.Application.Contratos.Eventos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Conformidade dos eventos v1 com os schemas publicados em
/// <c>specs/006-mensageria/contracts/eventos/</c> (REQ-06, FR-003, FR-004; constituição V).
/// </summary>
public sealed class SchemaEventosTestes
{
    private static readonly DateTimeOffset Instante = new(2026, 10, 22, 15, 30, 0, TimeSpan.Zero);
    private static readonly Guid PedidoId = Guid.CreateVersion7();
    private static readonly Guid ProdutoId = Guid.CreateVersion7();

    private static readonly EvaluationOptions Opcoes = new() { RequireFormatValidation = true, OutputFormat = OutputFormat.List };

    // Carregados uma vez: o registro de schemas é global, e o mesmo $id não pode ser registrado duas vezes.
    private static readonly Dictionary<string, JsonSchema> Schemas = TiposEvento.TipoDados.Keys.ToDictionary(
        tipo => tipo,
        tipo => JsonSchema.FromFile(Path.Combine(AppContext.BaseDirectory, "Eventos", $"{tipo}.v1.schema.json")));

    // Um pedido com 829,70: cobre multipleOf 0.01 com valores decimais.
    private static string JsonPedidoCriado() => SerializacaoEventos.Serializar(EnvelopeEvento.Criar(PedidoId, new PedidoCriado
    {
        PedidoId = PedidoId,
        Numero = "PED-20261022-000001",
        ClienteId = Guid.CreateVersion7(),
        Itens =
        [
            new ItemPedidoCriado { ProdutoId = ProdutoId, Quantidade = 2, PrecoUnitario = 349.90m },
            new ItemPedidoCriado { ProdutoId = Guid.CreateVersion7(), Quantidade = 1, PrecoUnitario = 129.90m }
        ],
        ValorTotal = 829.70m,
        Moeda = "BRL"
    }, Instante));

    public static TheoryData<string, string> EventosValidos() => new()
    {
        { TiposEvento.PedidoCriado, JsonPedidoCriado() },
        { TiposEvento.EstoqueReservado, SerializacaoEventos.Serializar(EnvelopeEvento.Criar(PedidoId, new EstoqueReservado
        {
            PedidoId = PedidoId,
            Itens = [new ItemEstoqueReservado { ProdutoId = ProdutoId, Quantidade = 2 }]
        }, Instante)) },
        { TiposEvento.PagamentoAprovado, SerializacaoEventos.Serializar(EnvelopeEvento.Criar(PedidoId, new PagamentoAprovado
        {
            PedidoId = PedidoId,
            Valor = 829.70m,
            IdentificadorTransacao = "TX-1"
        }, Instante)) },
        { TiposEvento.PagamentoRecusado, SerializacaoEventos.Serializar(EnvelopeEvento.Criar(PedidoId, new PagamentoRecusado
        {
            PedidoId = PedidoId,
            Valor = 829.70m,
            Motivo = "Cartão recusado"
        }, Instante)) }
    };

    [Theory]
    [MemberData(nameof(EventosValidos))]
    public void Evento_serializado_segue_o_schema_publicado(string tipo, string json)
    {
        DeveSerValido(tipo, json).Should().BeTrue();
    }

    public static TheoryData<string, string> Alteracoes() => new()
    {
        { "sem idEvento", "remover:idEvento" },
        { "versão 2", "versao:2" },
        { "tipo trocado", "tipo:estoque.reservado" },
        { "campo extra", "extra:1" },
        { "quantidade como texto", "quantidade:texto" }
    };

    [Theory]
    [MemberData(nameof(Alteracoes))]
    public void Mensagem_fora_do_contrato_e_recusada(string caso, string alteracao)
    {
        var json = JsonPedidoCriado();
        var raiz = JsonNode.Parse(json)!.AsObject();
        var (campo, valor) = (alteracao.Split(':')[0], alteracao.Split(':')[1]);
        switch (campo)
        {
            case "remover":
                raiz.Remove(valor);
                break;
            case "versao":
                raiz["versao"] = int.Parse(valor, System.Globalization.CultureInfo.InvariantCulture);
                break;
            case "tipo":
                raiz["tipo"] = valor;
                break;
            case "extra":
                raiz["extra"] = valor;
                break;
            case "quantidade":
                raiz["dados"]!["itens"]![0]!["quantidade"] = valor;
                break;
        }

        DeveSerValido(TiposEvento.PedidoCriado, raiz.ToJsonString()).Should().BeFalse(caso);
    }

    private static bool DeveSerValido(string tipo, string json)
    {
        using var documento = JsonDocument.Parse(json);
        return Schemas[tipo].Evaluate(documento.RootElement, Opcoes).IsValid;
    }
}
