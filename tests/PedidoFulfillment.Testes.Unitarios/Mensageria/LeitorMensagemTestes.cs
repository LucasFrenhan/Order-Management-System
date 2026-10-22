using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Infrastructure.Mensageria;
using static PedidoFulfillment.Testes.Unitarios.Mensageria.SerializacaoEventosTestes;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Mensagens ilegíveis viram "irrecuperável" e vão direto para a DLQ (REQ-06, R-03, FR-014).</summary>
public sealed class LeitorMensagemTestes
{
    private static readonly string[] Aceitos = [TiposEvento.PedidoCriado];

    private static ResultadoLeitura<PedidoCriado> Ler(string corpo) =>
        LeitorMensagem.Ler<PedidoCriado>(Encoding.UTF8.GetBytes(corpo), Aceitos);

    [Fact]
    public void Mensagem_valida_e_lida()
    {
        var envelope = PedidoCriado();

        var resultado = Ler(SerializacaoEventos.Serializar(envelope));

        resultado.Envelope.Should().BeEquivalentTo(envelope);
        resultado.Motivo.Should().BeNull();
    }

    [Theory]
    [InlineData("", "Corpo vazio")]
    [InlineData("{ nao e json", "JSON inválido")]
    [InlineData("""{"tipo":"pedido.cancelado","versao":1}""", "Tipo desconhecido")]
    [InlineData("""{"tipo":"pedido.criado","versao":2}""", "Versão não suportada")]
    [InlineData("""{"versao":1}""", "Envelope sem tipo")]
    public void Envelope_invalido_e_irrecuperavel(string corpo, string motivo)
    {
        var resultado = Ler(corpo);

        resultado.Envelope.Should().BeNull();
        resultado.Motivo.Should().StartWith(motivo);
    }

    [Fact]
    public void Dados_sem_campo_obrigatorio_sao_irrecuperaveis()
    {
        var json = SerializacaoEventos.Serializar(PedidoCriado()).Replace("\"numero\":\"PED-20261022-000001\",", "", StringComparison.Ordinal);

        Ler(json).Motivo.Should().StartWith("Conteúdo fora do contrato");
    }

    [Fact]
    public void Tipo_que_o_consumidor_nao_consome_e_irrecuperavel()
    {
        var resultado = LeitorMensagem.Ler<PedidoCriado>(Encoding.UTF8.GetBytes(SerializacaoEventos.Serializar(PedidoCriado())), [TiposEvento.PagamentoAprovado]);

        resultado.Motivo.Should().StartWith("Tipo não consumido");
    }
}
