using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Reconciliacao;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Reconciliacao;

/// <summary>Decisão por pedido da reconciliação, com regras falsas (REQ-12, data-model, R-06, R-07).</summary>
public sealed class ServicoReconciliacaoTestes
{
    private static readonly DateTimeOffset Criacao = new(2026, 10, 21, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Agora = Criacao.AddDays(2);
    private static readonly ParametrosEsgotamento Parametros = new(TimeSpan.FromHours(24), 3);
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly RegistroFalhasReconciliacao registro = new();
    private readonly LoggerFalso<ServicoReconciliacao> logger = new();
    private readonly List<(Guid IdOrigem, Guid PedidoId)> chamadas = [];
    private readonly Produto a = new("SKU-A", "Produto A", null, 10m, 7, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000001"));
    private readonly Produto b = new("SKU-B", "Produto B", null, 12.5m, 1, true, Criacao, Guid.Parse("0192b6a0-0000-7000-8000-000000000002"));

    public ServicoReconciliacaoTestes() => armazenamento.Produtos.AddRange([a, b]);

    private ServicoReconciliacao Servico(EtapaFluxoPedido etapa, Func<Task<bool>> regra) =>
        new([new EtapaFalsa(etapa, regra, chamadas)], armazenamento, registro, logger);

    private static Func<Task<bool>> Falha => () => throw new TimeoutException("gateway sem resposta");

    /// <summary>Pedido de 3 A e 2 B, criado há 2 dias, como fica guardado; cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status = StatusPedido.Pendente, bool reservado = false) =>
        armazenamento.GuardarPedido(PedidoId, () =>
        {
            var pedido = new Pedido(Guid.CreateVersion7(), "PED-20261021-000001", [(a, 3), (b, 2)], Criacao);
            typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
            typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
            if (status != StatusPedido.Pendente)
            {
                pedido.MudarStatus(status, Guid.CreateVersion7(), null, Criacao);
            }

            return pedido;
        });

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    private static PedidoParado Parado(EtapaFluxoPedido etapa, DateTimeOffset? atualizadoEm = null) => new(PedidoId, etapa, atualizadoEm ?? Criacao);

    private Task<ResultadoReconciliacao> Reconciliar(ServicoReconciliacao servico, PedidoParado parado) =>
        servico.ReconciliarAsync(parado, Agora, Parametros, CancellationToken.None);

    private void FalhasAnteriores(int quantidade)
    {
        for (var i = 0; i < quantidade; i++)
        {
            registro.RegistrarFalha(PedidoId, Agora.AddMinutes(-5));
        }
    }

    [Fact]
    public async Task Regra_que_grava_recupera_o_pedido_sem_evento_de_origem()
    {
        FalhasAnteriores(1);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Entrega, () => Task.FromResult(true)), Parado(EtapaFluxoPedido.Entrega));

        resultado.Should().Be(ResultadoReconciliacao.Recuperado);
        chamadas.Should().Equal((Guid.Empty, PedidoId));
        registro.ComFalha.Should().BeEmpty();
        var log = logger.Eventos.Should().ContainSingle().Subject;
        (log.Id.Id, log.Nivel).Should().Be((9001, LogLevel.Warning));
    }

    [Fact]
    public async Task Regra_que_devolve_falso_deixa_o_pedido_como_nao_mais_parado()
    {
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Pagamento, () => Task.FromResult(false)), Parado(EtapaFluxoPedido.Pagamento));

        resultado.Should().Be(ResultadoReconciliacao.NaoMaisParado);
        registro.ComFalha.Should().BeEmpty();
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 9007);
    }

    [Fact]
    public async Task Regra_que_falha_conta_o_ciclo_sem_gravar_nada()
    {
        PedidoEm();

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        registro.RegistrarFalha(PedidoId, Agora).Should().Be(2, "a falha deste ciclo foi contada");
        armazenamento.Gravacoes.Should().Be(0);
        var log = logger.Eventos.Should().ContainSingle().Subject;
        (log.Id.Id, log.Nivel).Should().Be((9003, LogLevel.Warning));
        log.Mensagem.Should().Contain("TimeoutException").And.Contain("1 ciclo(s)");
    }

    [Fact]
    public async Task Estoque_com_tres_falhas_e_mais_de_24_horas_parado_se_esgota_e_cancela()
    {
        PedidoEm();
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.Esgotado);
        var pedido = Gravado();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        var entrada = pedido.Historico.Last();
        entrada.AutorId.Should().BeNull();
        entrada.Motivo.Should().Be(ServicoReconciliacao.MotivoEstoqueNaoReservado).And.Be("Não foi possível reservar o estoque.");
        armazenamento.Gravacoes.Should().Be(1);
        armazenamento.EventosGravados.Should().BeEmpty("não existe evento de cancelamento");
        registro.ComFalha.Should().BeEmpty();
        var log = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 9002).Subject;
        log.Nivel.Should().Be(LogLevel.Error);
        log.Propriedades["Falhas"].Should().Be(3);
    }

    [Fact]
    public async Task Pagamento_com_tres_falhas_e_mais_de_24_horas_registra_nao_processado_devolve_o_estoque_e_cancela()
    {
        PedidoEm(reservado: true);
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Pagamento, Falha), Parado(EtapaFluxoPedido.Pagamento));

        resultado.Should().Be(ResultadoReconciliacao.Esgotado);
        var pedido = Gravado();
        pedido.Pagamento!.Situacao.Should().Be(SituacaoPagamento.NaoProcessado);
        pedido.EstoqueReservado.Should().BeFalse();
        pedido.Status.Should().Be(StatusPedido.Cancelado);
        pedido.Historico.Last().Motivo.Should().Be(ServicoReconciliacao.MotivoPagamentoNaoProcessado).And.Be("Não foi possível processar o pagamento.");
        armazenamento.Produtos.Single(p => p.Id == a.Id).QuantidadeEstoque.Should().Be(10);
        armazenamento.Produtos.Single(p => p.Id == b.Id).QuantidadeEstoque.Should().Be(3);
        armazenamento.EventosGravados.Should().BeEmpty();
    }

    [Theory]
    [InlineData(3, 23)]
    [InlineData(2, 48)]
    public async Task Sem_as_duas_condicoes_o_pedido_nao_se_esgota(int falhas, int horasParado)
    {
        PedidoEm();
        FalhasAnteriores(falhas - 1);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque, Agora.AddHours(-horasParado)));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Entrega_nunca_se_esgota()
    {
        PedidoEm(StatusPedido.Processando, reservado: true);
        FalhasAnteriores(10);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Entrega, Falha), Parado(EtapaFluxoPedido.Entrega));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        armazenamento.Gravacoes.Should().Be(0);
        Gravado().Status.Should().Be(StatusPedido.Processando);
    }

    [Fact]
    public async Task Esgotamento_com_o_pedido_fora_da_etapa_nao_cancela()
    {
        // Entre a listagem e o esgotamento, o estoque foi reservado por outra via.
        PedidoEm(reservado: true);
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.NaoMaisParado);
        armazenamento.Gravacoes.Should().Be(0);
        registro.ComFalha.Should().BeEmpty();
    }

    [Theory]
    [InlineData(StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Despachado)]
    public async Task Esgotamento_de_pedido_que_nao_pode_ser_cancelado_nao_grava(StatusPedido status)
    {
        PedidoEm(status);
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.NaoMaisParado);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Esgotamento_com_conflito_nas_tres_reavaliacoes_fica_como_falha()
    {
        PedidoEm();
        FalhasAnteriores(2);
        armazenamento.ConflitosRestantes = 3;

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        armazenamento.Gravacoes.Should().Be(0);
        registro.ComFalha.Should().Equal(PedidoId);
        Gravado().Status.Should().Be(StatusPedido.Pendente);
    }

    [Fact]
    public async Task Esgotamento_com_um_conflito_rele_e_cancela()
    {
        PedidoEm();
        FalhasAnteriores(2);
        armazenamento.ConflitosRestantes = 1;

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.Esgotado);
        armazenamento.Gravacoes.Should().Be(1);
    }

    [Fact]
    public async Task Pedido_alterado_ha_menos_de_24_horas_na_releitura_nao_se_esgota()
    {
        // A listagem viu o pedido antigo; a releitura mostra que ele mudou há pouco, sem sair da etapa.
        armazenamento.GuardarPedido(PedidoId, () =>
        {
            var pedido = new Pedido(Guid.CreateVersion7(), "PED-20261021-000001", [(a, 1)], Agora.AddHours(-1));
            typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
            return pedido;
        });
        FalhasAnteriores(2);

        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, Falha), Parado(EtapaFluxoPedido.Estoque));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Etapa_sem_regra_registrada_conta_como_falha()
    {
        var resultado = await Reconciliar(Servico(EtapaFluxoPedido.Estoque, () => Task.FromResult(true)), Parado(EtapaFluxoPedido.Entrega));

        resultado.Should().Be(ResultadoReconciliacao.Falhou);
        chamadas.Should().BeEmpty();
    }

    private sealed class EtapaFalsa(EtapaFluxoPedido etapa, Func<Task<bool>> regra, List<(Guid, Guid)> chamadas) : IEtapaFluxoPedido
    {
        public EtapaFluxoPedido Etapa => etapa;

        public Task<bool> AplicarAsync(Guid idOrigem, Guid pedidoId, CancellationToken cancellationToken)
        {
            chamadas.Add((idOrigem, pedidoId));
            return regra();
        }
    }
}
