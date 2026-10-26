using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Os eventos de cancelamento e de estorno chegam a um sistema de fora, que liga a própria fila a
/// <c>oms.eventos</c>, e não chegam aos consumidores internos (REQ-15, US1, US2 e US3; FR-008 a
/// FR-010).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class EventosCancelamentoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(15);

    private static readonly string[] Consumidores = [CenarioEstoque.FilaEstoque, CenarioPagamento.FilaPagamento, CenarioEntrega.FilaEntrega];

    [Fact]
    public async Task Cancelamento_pela_api_chega_uma_vez_a_fila_de_um_sistema_de_fora()
    {
        // US1 (ponta a ponta): da gravação do cancelamento à fila ligada pela chave pedido.cancelado.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoCancelado);
        var pedidoId = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(cliente, pedidoId, "Cancelado", "Desisti")).EnsureSuccessStatusCode();

        try
        {
            await using var worker = SoDespachante(stringConexao);
            worker.CreateClient().Dispose();

            (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(CenarioEstoque.DoPedido(fila, pedidoId).Count > 0), Prazo)).Should().BeTrue();
            var mensagem = CenarioEstoque.DoPedido(fila, pedidoId).Should().ContainSingle().Subject;
            mensagem.Propriedades.Type.Should().Be(TiposEvento.PedidoCancelado);
            SchemaEventosTestes.DeveSerValido(TiposEvento.PedidoCancelado, mensagem.Texto).Should().BeTrue();
            SerializacaoEventos.Desserializar<PedidoCancelado>(mensagem.Texto).Dados.Motivo.Should().Be(MotivoCancelamento.Cliente);
            mensagem.Texto.Should().NotContain("Desisti", "FR-010: o motivo digitado fica só no histórico");
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    [Fact]
    public async Task Os_dois_tipos_chegam_a_fila_de_fora_e_nao_as_filas_internas()
    {
        // US2 e US3 cenário 2: sem consumidor interno, saem sem rota obrigatória e não entram em
        // oms.estoque, oms.pagamento nem oms.entrega.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        var pedidoId = Guid.CreateVersion7();
        var agora = DateTimeOffset.UtcNow;
        await LimparTodasAsync();
        await using var cancelamentos = await mensageria.FilaTesteAsync(TiposEvento.PedidoCancelado);
        await using var estornos = await mensageria.FilaTesteAsync(TiposEvento.PagamentoEstornado);
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            contexto.EventosSaida.Add(EventoSaida.De(EnvelopeEvento.Criar(pedidoId, new PedidoCancelado
            {
                PedidoId = pedidoId,
                Numero = "PED-20261026-000001",
                Motivo = MotivoCancelamento.Operacao,
                EtapaEsgotada = null,
                CanceladoEm = agora,
                EstoqueDevolvido = true,
                EstornoPendente = true,
                OrdemEntregaCancelada = false
            }, agora), agora));
            contexto.EventosSaida.Add(EventoSaida.De(EnvelopeEvento.Criar(pedidoId, new PagamentoEstornado
            {
                PedidoId = pedidoId,
                Numero = "PED-20261026-000001",
                Valor = 149.70m,
                IdentificadorTransacao = "SIM-1",
                IdentificadorEstorno = "SIM-EST-1",
                EstornadoEm = agora
            }, agora), agora));
            await contexto.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        try
        {
            await using var worker = SoDespachante(stringConexao);
            worker.CreateClient().Dispose();

            (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(!cancelamentos.Mensagens.IsEmpty && !estornos.Mensagens.IsEmpty), Prazo))
                .Should().BeTrue();
            (await MensageriaTeste.EsperarAteAsync(async () => await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
                "SELECT COUNT(*) FROM EventosSaida WHERE EnviadoEm IS NOT NULL") == 2, Prazo)).Should().BeTrue();
            ChavesDosDados(cancelamentos.Mensagens.Should().ContainSingle().Subject.Texto).Should().Equal(
                "pedidoId", "numero", "motivo", "etapaEsgotada", "canceladoEm", "estoqueDevolvido", "estornoPendente", "ordemEntregaCancelada");
            var estorno = estornos.Mensagens.Should().ContainSingle().Subject.Texto;
            ChavesDosDados(estorno).Should().Equal(
                "pedidoId", "numero", "valor", "identificadorTransacao", "identificadorEstorno", "estornadoEm");
            SchemaEventosTestes.DeveSerValido(TiposEvento.PagamentoEstornado, estorno).Should().BeTrue();
            (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT SUM(Tentativas) FROM EventosSaida")).Should().Be(0, "sem rota obrigatória, não há retorno");

            foreach (var consumidor in Consumidores)
            {
                (await mensageria.MensagensNaFilaAsync(TopologiaMensageria.FilaPrincipal(consumidor))).Should().Be(0, $"{consumidor} não consome os tipos novos");
                (await mensageria.MensagensNaFilaAsync(TopologiaMensageria.FilaMortas(consumidor))).Should().Be(0);
            }
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    private static List<string> ChavesDosDados(string json)
    {
        using var documento = JsonDocument.Parse(json);
        return [.. documento.RootElement.GetProperty("dados").EnumerateObject().Select(p => p.Name)];
    }

    /// <summary>Worker sem os três consumidores, como em <see cref="PublicacaoSemFilaTestes"/>: só o despachante publica.</summary>
    private FabricaWorker SoDespachante(string stringConexao) => new()
    {
        StringConexao = stringConexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Servicos = servicos =>
        {
            CenarioEstoque.RemoverConsumidor<ReservaEstoque, PedidoCriado>(servicos);
            CenarioEstoque.RemoverConsumidor<ProcessamentoPagamento, EstoqueReservado>(servicos);
            CenarioEstoque.RemoverConsumidor<CriacaoOrdemEntrega, PagamentoAprovado>(servicos);
        }
    };

    /// <summary>O broker é compartilhado pela coleção: não deixa mensagens para os próximos testes.</summary>
    private async Task LimparTodasAsync()
    {
        foreach (var consumidor in Consumidores)
        {
            await mensageria.LimparFilasConsumidorAsync(consumidor);
        }
    }
}
