using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Decisão de um estorno pendente (REQ-13, R-04), com armazenamento e gateway falsos.</summary>
public sealed class ServicoEstornosTestes
{
    private const string Transacao = "SIM-0199f6b12c3d7e4f8a5b6c7d8e9f0a12";
    private static readonly DateTimeOffset Criacao = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Cancelamento = Criacao.AddMinutes(10);
    private static readonly ParametrosAlertaEstorno Alerta = new(TimeSpan.FromHours(24), 3);
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly GatewayPagamentoFalso gateway = new();
    private readonly LoggerFalso<ServicoEstornos> logger = new();
    private readonly Produto produto = new("SKU-A", "Produto A", null, 20m, 10, true, Criacao);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private ServicoEstornos Servico(DateTimeOffset agora) => new(armazenamento, gateway, new RelogioFixo(agora), logger);

    /// <summary>Pedido pago e cancelado, com o estorno pendente e as falhas já registradas.</summary>
    private Pedido MontarPendente(int falhasAnteriores = 0)
    {
        var pedido = new Pedido(Guid.CreateVersion7(), "PED-20261024-000001", [(produto, 2)], Criacao);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        var produtos = new[] { new Produto(produto.Sku, produto.Nome, null, produto.Preco, 10, true, Criacao, produto.Id) }.ToDictionary(p => p.Id);
        pedido.ReservarEstoque(produtos, Criacao);
        pedido.AprovarPagamento(Transacao, Criacao);
        pedido.Cancelar(Guid.CreateVersion7(), "Cliente desistiu", produtos, Cancelamento);
        for (var i = 1; i <= falhasAnteriores; i++)
        {
            pedido.RegistrarFalhaEstorno(Cancelamento.AddMinutes(i));
        }

        return pedido;
    }

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    [Fact]
    public async Task Estorno_pendente_e_confirmado_com_uma_chamada_ao_gateway()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        var agora = Cancelamento.AddMinutes(1);

        var resultado = await Servico(agora).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Confirmado);
        gateway.Estornos.Should().Equal((PedidoId, Transacao, 40m));
        var pagamento = Gravado().Pagamento!;
        pagamento.Situacao.Should().Be(SituacaoPagamento.Estornado);
        pagamento.IdentificadorEstorno.Should().Be("SIM-EST-00000000000000000000000000000001");
        pagamento.EstornadoEm.Should().Be(agora);
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7006).Subject;
        log.Propriedades["PedidoId"].Should().Be(PedidoId);
        log.Mensagem.Should().NotContain("SIM-", "FR-016: o log não traz transação nem estorno");

        // REQ-15, US2 cenário 1: a confirmação grava o pagamento.estornado, com os identificadores do gateway.
        var evento = armazenamento.EventosGravados.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TiposEvento.PagamentoEstornado);
        var envelope = SerializacaoEventos.Desserializar<PagamentoEstornado>(evento.Conteudo);
        envelope.IdCorrelacao.Should().Be(PedidoId);
        envelope.OcorridoEm.Should().Be(agora);
        envelope.Dados.Should().BeEquivalentTo(new PagamentoEstornado
        {
            PedidoId = PedidoId,
            Numero = "PED-20261024-000001",
            Valor = 40m,
            IdentificadorTransacao = Transacao,
            IdentificadorEstorno = "SIM-EST-00000000000000000000000000000001",
            EstornadoEm = agora
        });
    }

    [Fact]
    public async Task Pedido_sem_estorno_pendente_nao_chama_o_gateway()
    {
        armazenamento.GuardarPedido(PedidoId, () =>
        {
            var pedido = MontarPendente();
            pedido.ConfirmarEstorno("SIM-EST-1", Cancelamento);
            return pedido;
        });

        var resultado = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, Token);
        var inexistente = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(Guid.CreateVersion7(), Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.NadaAFazer);
        inexistente.Should().Be(ResultadoRodadaEstorno.NadaAFazer);
        gateway.Estornos.Should().BeEmpty();
        armazenamento.Gravacoes.Should().Be(0);
    }

    public static TheoryData<string> Falhas => ["recusa", "exceção"];

    private void Falhar(string falha)
    {
        if (falha == "recusa")
        {
            gateway.ProximoEstorno = ResultadoEstorno.Recusar("Estorno recusado pelo emissor.");
        }
        else
        {
            gateway.FalhaEstorno = new TimeoutException("Gateway sem resposta.");
        }
    }

    [Theory]
    [MemberData(nameof(Falhas))]
    public async Task Falha_do_gateway_conta_a_tentativa_e_mantem_o_estorno_pendente(string falha)
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        Falhar(falha);
        var agora = Cancelamento.AddMinutes(1);

        var resultado = await Servico(agora).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Falhou);
        var pagamento = Gravado().Pagamento!;
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        pagamento.TentativasEstorno.Should().Be(1);
        pagamento.UltimaFalhaEstornoEm.Should().Be(agora);
        armazenamento.Descartes.Should().BeGreaterThanOrEqualTo(1);
        armazenamento.EventosGravados.Should().BeEmpty("REQ-15, FR-006: falhas não geram evento");
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7007).Subject;
        log.Nivel.Should().Be(LogLevel.Warning);
        log.Propriedades["Tentativas"].Should().Be(1);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7008);
    }

    [Fact]
    public async Task Depois_do_prazo_e_das_tentativas_a_falha_vira_erro()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente(falhasAnteriores: 2));
        Falhar("exceção");

        var resultado = await Servico(Cancelamento.AddHours(25)).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Falhou);
        Gravado().Pagamento!.TentativasEstorno.Should().Be(3);
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7008).Subject;
        log.Nivel.Should().Be(LogLevel.Error);
        log.Propriedades["HorasPendente"].Should().Be(25);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7007);
    }

    [Theory]
    [InlineData(2, 25)]
    [InlineData(5, 23)]
    public async Task Com_so_um_dos_limites_cumprido_a_falha_continua_aviso(int falhasAnteriores, int horas)
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente(falhasAnteriores - 1));
        Falhar("exceção");

        await Servico(Cancelamento.AddHours(horas)).EstornarAsync(PedidoId, Alerta, Token);

        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7007);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7008);
    }

    [Fact]
    public async Task Conflito_ao_gravar_a_confirmacao_rele_sem_chamar_o_gateway_de_novo()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        armazenamento.ConflitosRestantes = 2;

        var resultado = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Confirmado);
        gateway.Estornos.Should().ContainSingle();
        armazenamento.Descartes.Should().Be(2);
        Gravado().Pagamento!.Situacao.Should().Be(SituacaoPagamento.Estornado);
        armazenamento.EventosGravados.Should().ContainSingle(e => e.Tipo == TiposEvento.PagamentoEstornado, "o descarte leva o evento da tentativa perdida");
    }

    [Fact]
    public async Task Tres_conflitos_seguidos_na_confirmacao_deixam_o_estorno_pendente_para_a_proxima_rodada()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        armazenamento.ConflitosRestantes = 3;

        var resultado = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Falhou);
        gateway.Estornos.Should().ContainSingle();
        var pagamento = Gravado().Pagamento!;
        pagamento.Situacao.Should().Be(SituacaoPagamento.EstornoPendente);
        pagamento.TentativasEstorno.Should().Be(0, "o gateway confirmou: não é falha do estorno");
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7007);
        armazenamento.EventosGravados.Should().BeEmpty();
    }

    [Fact]
    public async Task Conflito_na_releitura_que_encontra_o_estorno_ja_confirmado_nao_grava_de_novo()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => armazenamento.GuardarPedido(PedidoId, () =>
        {
            var pedido = MontarPendente();
            pedido.ConfirmarEstorno("SIM-EST-OUTRO", Cancelamento);
            return pedido;
        });

        var resultado = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.NadaAFazer);
        Gravado().Pagamento!.IdentificadorEstorno.Should().Be("SIM-EST-OUTRO");
        armazenamento.Gravacoes.Should().Be(0);
        armazenamento.EventosGravados.Should().BeEmpty("quem confirmou antes gravou o evento");
    }

    [Fact]
    public async Task Conflito_ao_gravar_a_falha_nao_conta_a_tentativa()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        Falhar("exceção");
        armazenamento.ConflitosRestantes = 1;

        var resultado = await Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, Token);

        resultado.Should().Be(ResultadoRodadaEstorno.Falhou);
        Gravado().Pagamento!.TentativasEstorno.Should().Be(0);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7007);
    }

    [Fact]
    public async Task Cancelamento_do_token_nao_conta_como_falha()
    {
        armazenamento.GuardarPedido(PedidoId, () => MontarPendente());
        using var fonte = new CancellationTokenSource();
        await fonte.CancelAsync();
        gateway.FalhaEstorno = new OperationCanceledException(fonte.Token);

        var estornar = () => Servico(Cancelamento.AddMinutes(1)).EstornarAsync(PedidoId, Alerta, fonte.Token);

        await estornar.Should().ThrowAsync<OperationCanceledException>();
        Gravado().Pagamento!.TentativasEstorno.Should().Be(0);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7007);
    }
}
