using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using static PedidoFulfillment.Testes.Unitarios.Pedidos.ServicoPedidosCriacaoTestes;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>User Story 2: repetição, conteúdo divergente e corrida de chaves (FR-007 a FR-013).</summary>
public sealed class ServicoPedidosIdempotenciaTestes
{
    private static readonly Guid ClienteId = Guid.CreateVersion7();
    private static readonly JsonSerializerOptions Opcoes = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoPedidos> logger = new();
    private readonly Produto produtoA;
    private readonly Produto produtoB;

    public ServicoPedidosIdempotenciaTestes()
    {
        produtoA = new Produto("SKU-A", "Produto A", null, 10m, 10, true, Agora);
        produtoB = new Produto("SKU-B", "Produto B", null, 5m, 10, true, Agora);
        armazenamento.Produtos.AddRange(produtoA, produtoB);
    }

    private ServicoPedidos Servico() => new(armazenamento, new GeradorNumeroPedidoFalso(), new RelogioFixo(Agora), logger);

    private static PedidoResponseDTO RespostaOriginal(Guid pedidoId) => new()
    {
        Id = pedidoId,
        Numero = "PED-20261022-000099",
        ClienteId = ClienteId,
        Status = StatusPedido.Pendente,
        Itens = [],
        ValorTotal = 10m,
        CriadoEm = Agora,
        AtualizadoEm = Agora
    };

    private RegistroIdempotencia Registro(Guid chave, CriarPedidoRequestDTO conteudo, PedidoResponseDTO resposta) =>
        new(ClienteId, chave, ImpressaoConteudo.Calcular(conteudo), resposta.Id, JsonSerializer.Serialize(resposta, Opcoes), Agora);

    [Fact]
    public async Task Mesma_chave_e_mesmo_conteudo_devolvem_a_resposta_original_sem_gravar()
    {
        var chave = Guid.NewGuid();
        var conteudo = Requisicao((produtoA.Id, 1), (produtoB.Id, 2));
        var original = RespostaOriginal(Guid.CreateVersion7());
        armazenamento.Registros.Add(Registro(chave, conteudo, original));

        // Mesmos itens em outra ordem: mesmo conteúdo (FR-008).
        var resultado = await Servico().CriarAsync(ClienteId, chave, Requisicao((produtoB.Id, 2), (produtoA.Id, 1)), CancellationToken.None);

        resultado.Valor.Should().BeEquivalentTo(original);
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty(); // a repetição não gera evento (REQ-06, FR-009)
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4002).Which.Propriedades["PedidoId"].Should().Be(original.Id);
    }

    [Fact]
    public async Task Mesma_chave_e_conteudo_diferente_respondem_409_divergente()
    {
        var chave = Guid.NewGuid();
        armazenamento.Registros.Add(Registro(chave, Requisicao((produtoA.Id, 1)), RespostaOriginal(Guid.CreateVersion7())));

        var resultado = await Servico().CriarAsync(ClienteId, chave, Requisicao((produtoA.Id, 2)), CancellationToken.None);

        DeveSerConflito(resultado, CodigosErro.IdempotenciaConteudoDivergente);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Corrida_com_vencedor_de_mesmo_conteudo_devolve_a_resposta_dele()
    {
        var chave = Guid.NewGuid();
        var conteudo = Requisicao((produtoA.Id, 1));
        var vencedor = RespostaOriginal(Guid.CreateVersion7());
        armazenamento.CorridaNaProximaGravacao = true;
        armazenamento.VencedorDaCorrida = Registro(chave, conteudo, vencedor);

        var resultado = await Servico().CriarAsync(ClienteId, chave, conteudo, CancellationToken.None);

        resultado.Valor.Should().BeEquivalentTo(vencedor);
        armazenamento.Descartes.Should().Be(1);
        armazenamento.PedidosGravados.Should().BeEmpty();
    }

    [Fact]
    public async Task Corrida_com_vencedor_de_outro_conteudo_responde_409_divergente()
    {
        var chave = Guid.NewGuid();
        armazenamento.CorridaNaProximaGravacao = true;
        armazenamento.VencedorDaCorrida = Registro(chave, Requisicao((produtoB.Id, 1)), RespostaOriginal(Guid.CreateVersion7()));

        var resultado = await Servico().CriarAsync(ClienteId, chave, Requisicao((produtoA.Id, 1)), CancellationToken.None);

        DeveSerConflito(resultado, CodigosErro.IdempotenciaConteudoDivergente);
        armazenamento.Descartes.Should().Be(1);
    }

    [Fact]
    public async Task Corrida_sem_vencedor_visivel_responde_409_em_processamento()
    {
        armazenamento.CorridaNaProximaGravacao = true;

        var resultado = await Servico().CriarAsync(ClienteId, Guid.NewGuid(), Requisicao((produtoA.Id, 1)), CancellationToken.None);

        DeveSerConflito(resultado, CodigosErro.IdempotenciaEmProcessamento);
        armazenamento.Descartes.Should().Be(1);
    }

    [Fact]
    public async Task Tentativa_que_terminou_em_422_nao_reserva_a_chave()
    {
        var chave = Guid.NewGuid();
        var falha = await Servico().CriarAsync(ClienteId, chave, Requisicao((Guid.CreateVersion7(), 1)), CancellationToken.None);

        var resultado = await Servico().CriarAsync(ClienteId, chave, Requisicao((produtoA.Id, 1)), CancellationToken.None);

        falha.Falha!.Tipo.Should().Be(TipoFalha.RegraNegocio);
        resultado.Sucesso.Should().BeTrue();
        armazenamento.Registros.Should().ContainSingle();
    }

    private static void DeveSerConflito(Resultado<PedidoResponseDTO> resultado, string codigo)
    {
        resultado.Falha.Should().NotBeNull();
        resultado.Falha!.Tipo.Should().Be(TipoFalha.Conflito);
        resultado.Falha.Codigo.Should().Be(codigo);
    }
}
