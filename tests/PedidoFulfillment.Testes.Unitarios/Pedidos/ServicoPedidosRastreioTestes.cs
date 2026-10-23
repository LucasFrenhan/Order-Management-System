using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using static PedidoFulfillment.Testes.Unitarios.Pedidos.ServicoPedidosCriacaoTestes;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>Despacho e correção do rastreio pelo serviço (REQ-11, research R-05 a R-07, R-10).</summary>
public sealed class ServicoPedidosRastreioTestes
{
    private static readonly Guid Dono = Guid.CreateVersion7();
    private static readonly Guid Operador = Guid.CreateVersion7();
    private static readonly Guid PedidoId = Guid.CreateVersion7();
    private static readonly DateTimeOffset Instante = Agora.AddHours(1);

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoPedidos> logger = new();
    private readonly Produto produto = new("SKU-A", "Produto A", null, 10m, 10, true, Agora);

    private ServicoPedidos Servico() => new(armazenamento, new GeradorNumeroPedidoFalso(), new RelogioFixo(Instante), logger);

    /// <summary>Guarda o pedido no status pedido; cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status, bool comOrdem = false, string? transportadora = null, string? codigo = null) =>
        armazenamento.GuardarPedido(PedidoId, () => Montar(status, comOrdem, transportadora, codigo));

    private Pedido Montar(StatusPedido status, bool comOrdem, string? transportadora, string? codigo)
    {
        var pedido = new Pedido(Dono, "PED-20261022-000001", [(produto, 1)], Agora);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        StatusPedido[] caminho = status switch
        {
            StatusPedido.Pendente => [],
            StatusPedido.Processando => [StatusPedido.Processando],
            StatusPedido.ProntoParaEnvio => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio],
            StatusPedido.Despachado => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado],
            _ => [status]
        };
        foreach (var passo in caminho)
        {
            pedido.MudarStatus(passo, Operador, passo == StatusPedido.Cancelado ? "motivo" : null, Agora);
        }

        if (comOrdem)
        {
            var ordem = new OrdemEntrega(PedidoId, Agora);
            if (transportadora is not null && codigo is not null)
            {
                ordem.RegistrarDespacho(transportadora, codigo, Agora);
            }

            typeof(Pedido).GetProperty(nameof(Pedido.OrdemEntrega))!.SetValue(pedido, ordem);
        }

        return pedido;
    }

    private Task<Resultado<PedidoResponseDTO>> Registrar(string transportadora = "Correios", string codigo = "BR123", Guid? pedidoId = null) =>
        Servico().RegistrarRastreioAsync(pedidoId ?? PedidoId, Operador,
            new RegistrarRastreioRequestDTO { Transportadora = transportadora, CodigoRastreio = codigo }, CancellationToken.None);

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    [Fact]
    public async Task Pedido_pronto_e_despachado_com_o_rastreio_aparado()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio, comOrdem: true);

        var resultado = await Registrar("  Correios  ", " BR123 ");

        resultado.Valor!.Status.Should().Be(StatusPedido.Despachado);
        resultado.Valor.Entrega!.Should().Be(new EntregaPedidoResponseDTO
        {
            Transportadora = "Correios",
            CodigoRastreio = "BR123",
            DespachadoEm = Instante,
            CriadoEm = Agora
        });
        resultado.Valor.Historico.Last().Motivo.Should().Be(Pedido.MotivoPedidoDespachado);
        armazenamento.Gravacoes.Should().Be(1);
        Gravado().Status.Should().Be(StatusPedido.Despachado);
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4004).Subject;
        log.Propriedades["Resultado"].Should().Be(ResultadoRastreio.Despachado);
        log.Propriedades["UsuarioId"].Should().Be(Operador);
        log.Mensagem.Should().NotContain("BR123").And.NotContain("Correios");
    }

    [Fact]
    public async Task Pedido_inexistente_nao_e_encontrado()
    {
        (await Registrar(pedidoId: Guid.CreateVersion7())).Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Theory]
    [InlineData(StatusPedido.Pendente)]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.PagamentoRejeitado)]
    public async Task Pedido_fora_de_pronto_ou_despachado_e_transicao_invalida(StatusPedido status)
    {
        PedidoEm(status);

        var falha = (await Registrar()).Falha!;

        (falha.Tipo, falha.Codigo).Should().Be((TipoFalha.Conflito, CodigosErro.PedidoTransicaoInvalida));
        falha.Detalhe.Should().Contain(status.ToString());
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Rastreio_em_uso_por_outro_pedido_e_duplicado_sem_diferenciar_caixa()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio);
        armazenamento.RastreiosEmUso.Add(("correios", "br123", Guid.CreateVersion7()));

        var falha = (await Registrar("Correios", "BR123")).Falha!;

        (falha.Tipo, falha.Codigo).Should().Be((TipoFalha.Conflito, CodigosErro.PedidoRastreioDuplicado));
        armazenamento.Gravacoes.Should().Be(0);
        Gravado().Status.Should().Be(StatusPedido.ProntoParaEnvio);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4005);
    }

    [Fact]
    public async Task Rastreio_gravado_por_outro_pedido_na_corrida_e_duplicado()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio);
        armazenamento.DuplicadoNaProximaGravacao = true;

        var falha = (await Registrar()).Falha!;

        falha.Codigo.Should().Be(CodigosErro.PedidoRastreioDuplicado);
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.Descartes.Should().Be(1);
        Gravado().Status.Should().Be(StatusPedido.ProntoParaEnvio);
    }

    [Fact]
    public async Task Conflito_com_o_cancelamento_e_transicao_invalida_sem_gravar()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio);
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => PedidoEm(StatusPedido.Cancelado);

        var falha = (await Registrar()).Falha!;

        falha.Codigo.Should().Be(CodigosErro.PedidoTransicaoInvalida);
        armazenamento.Gravacoes.Should().Be(0);
        Gravado().Status.Should().Be(StatusPedido.Cancelado);
    }

    /// <summary>Analyze C1: o consumidor de entrega criou a ordem ao mesmo tempo; o despacho usa a ordem dele.</summary>
    [Fact]
    public async Task Conflito_com_a_ordem_criada_pelo_consumidor_despacha_sobre_ela()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio);
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => PedidoEm(StatusPedido.ProntoParaEnvio, comOrdem: true);

        var resultado = await Registrar();

        resultado.Valor!.Status.Should().Be(StatusPedido.Despachado);
        armazenamento.Descartes.Should().Be(1);
        var ordem = Gravado().OrdemEntrega!;
        (ordem.CriadoEm, ordem.CodigoRastreio, ordem.DespachadoEm).Should().Be((Agora, "BR123", (DateTimeOffset?)Instante));
    }

    [Fact]
    public async Task Conflitos_nas_3_tentativas_respondem_alterado_por_outra_operacao()
    {
        PedidoEm(StatusPedido.ProntoParaEnvio);
        armazenamento.ConflitosRestantes = 3;

        var falha = (await Registrar()).Falha!;

        falha.Codigo.Should().Be(CodigosErro.PedidoTransicaoInvalida);
        falha.Detalhe.Should().Contain("outra operação");
        armazenamento.Gravacoes.Should().Be(0);
    }
}
