using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using static PedidoFulfillment.Testes.Unitarios.Pedidos.ServicoPedidosCriacaoTestes;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>User Story 4: transições, papéis, histórico e reavaliação em conflito (FR-016 a FR-019).</summary>
public sealed class ServicoPedidosStatusTestes
{
    private static readonly Guid Dono = Guid.CreateVersion7();
    private static readonly Guid Operador = Guid.CreateVersion7();
    private static readonly Guid PedidoId = Guid.CreateVersion7();

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoPedidos> logger = new();
    private readonly Produto produto = new("SKU-A", "Produto A", null, 10m, 10, true, Agora);

    private ServicoPedidos Servico() => new(armazenamento, new GeradorNumeroPedidoFalso(), new RelogioFixo(Agora.AddHours(1)), logger);

    /// <summary>Guarda um pedido no status pedido; cada leitura monta uma instância nova.</summary>
    private void PedidoEm(StatusPedido status, bool reservado = false) => armazenamento.GuardarPedido(PedidoId, () => Montar(status, reservado));

    private Pedido Montar(StatusPedido status, bool reservado = false)
    {
        var pedido = new Pedido(Dono, "PED-20261022-000001", [(produto, 1)], Agora);
        typeof(Pedido).GetProperty(nameof(Pedido.Id))!.SetValue(pedido, PedidoId);
        typeof(Pedido).GetProperty(nameof(Pedido.EstoqueReservado))!.SetValue(pedido, reservado);
        foreach (var passo in Caminho(status))
        {
            pedido.MudarStatus(passo, null, passo == StatusPedido.Cancelado ? "motivo" : null, Agora);
        }

        return pedido;
    }

    private static StatusPedido[] Caminho(StatusPedido status) => status switch
    {
        StatusPedido.Pendente => [],
        StatusPedido.Processando => [StatusPedido.Processando],
        StatusPedido.ProntoParaEnvio => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio],
        StatusPedido.Despachado => [StatusPedido.Processando, StatusPedido.ProntoParaEnvio, StatusPedido.Despachado],
        _ => [status]
    };

    private Task<Resultado<PedidoResponseDTO>> Mudar(Guid usuarioId, StatusPedido novo, string? motivo, params PerfilUsuario[] perfis) =>
        Servico().AtualizarStatusAsync(PedidoId, usuarioId, perfis, new AtualizarStatusPedidoRequestDTO { Status = novo, Motivo = motivo }, CancellationToken.None);

    public static TheoryData<StatusPedido, StatusPedido> TransicoesDaOperacao => new()
    {
        { StatusPedido.Pendente, StatusPedido.Processando },
        { StatusPedido.Processando, StatusPedido.ProntoParaEnvio },
        { StatusPedido.ProntoParaEnvio, StatusPedido.Despachado },
        { StatusPedido.Pendente, StatusPedido.Cancelado },
        { StatusPedido.Processando, StatusPedido.Cancelado },
        { StatusPedido.ProntoParaEnvio, StatusPedido.Cancelado }
    };

    [Theory]
    [MemberData(nameof(TransicoesDaOperacao))]
    public async Task Operacao_aplica_as_transicoes_da_tabela(StatusPedido de, StatusPedido para)
    {
        PedidoEm(de);

        var resultado = await Mudar(Operador, para, "  Motivo  ", PerfilUsuario.OperadorLogistica);

        resultado.Valor!.Status.Should().Be(para);
        resultado.Valor.AtualizadoEm.Should().Be(Agora.AddHours(1));
        var gravado = (await armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None))!;
        var entrada = gravado.Historico.Last();
        (entrada.StatusAnterior, entrada.StatusNovo, entrada.AutorId, entrada.Motivo).Should().Be((de, para, Operador, "Motivo"));
        var evento = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4003).Subject;
        evento.Propriedades["PapelAutor"].Should().Be("Operacao");
    }

    [Theory]
    [InlineData(StatusPedido.Despachado, StatusPedido.Pendente)]
    [InlineData(StatusPedido.Pendente, StatusPedido.Pendente)]
    [InlineData(StatusPedido.Cancelado, StatusPedido.Processando)]
    [InlineData(StatusPedido.Pendente, StatusPedido.Despachado)]
    public async Task Transicao_fora_da_tabela_responde_409_sem_gravar(StatusPedido de, StatusPedido para)
    {
        PedidoEm(de);

        var resultado = await Mudar(Operador, para, null, PerfilUsuario.Admin);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.Conflito);
        resultado.Falha.Codigo.Should().Be(CodigosErro.PedidoTransicaoInvalida);
        resultado.Falha.Detalhe.Should().Be($"O pedido está em {de} e não pode passar para {para}.");
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Cliente_dono_cancela_pedido_pendente()
    {
        PedidoEm(StatusPedido.Pendente);

        var resultado = await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        logger.Eventos.Single(e => e.Id.Id == 4003).Propriedades["PapelAutor"].Should().Be("Cliente");
    }

    [Theory]
    [InlineData(StatusPedido.Processando, StatusPedido.Cancelado)]
    [InlineData(StatusPedido.Pendente, StatusPedido.Processando)]
    [InlineData(StatusPedido.Despachado, StatusPedido.Pendente)]
    public async Task Cliente_dono_em_outra_transicao_recebe_403(StatusPedido de, StatusPedido para)
    {
        PedidoEm(de);

        var resultado = await Mudar(Dono, para, "motivo", PerfilUsuario.Cliente);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.SemPermissao);
        armazenamento.Gravacoes.Should().Be(0);
    }

    [Fact]
    public async Task Outro_cliente_e_pedido_inexistente_recebem_404()
    {
        PedidoEm(StatusPedido.Pendente);

        var deOutro = await Mudar(Guid.CreateVersion7(), StatusPedido.Cancelado, "x", PerfilUsuario.Cliente);
        var inexistente = await Servico().AtualizarStatusAsync(
            Guid.CreateVersion7(), Operador, [PerfilUsuario.Admin], new AtualizarStatusPedidoRequestDTO { Status = StatusPedido.Processando }, CancellationToken.None);

        deOutro.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
        inexistente.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
    }

    [Fact]
    public async Task Conflito_reavalia_sobre_o_estado_novo_e_cliente_recebe_403()
    {
        PedidoEm(StatusPedido.Pendente);
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => PedidoEm(StatusPedido.Processando);

        var resultado = await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.SemPermissao);
        armazenamento.Descartes.Should().Be(1);
    }

    [Fact]
    public async Task Conflito_reavalia_e_operacao_cancela_o_pedido_que_avancou()
    {
        PedidoEm(StatusPedido.Pendente);
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => PedidoEm(StatusPedido.Processando);

        var resultado = await Mudar(Operador, StatusPedido.Cancelado, "Sem estoque", PerfilUsuario.OperadorLogistica);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        var gravado = (await armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None))!;
        gravado.Historico.Last().StatusAnterior.Should().Be(StatusPedido.Processando);
        armazenamento.Descartes.Should().Be(1);
    }

    [Fact]
    public async Task Tres_conflitos_seguidos_respondem_409()
    {
        PedidoEm(StatusPedido.Pendente);
        armazenamento.ConflitosRestantes = 3;

        var resultado = await Mudar(Operador, StatusPedido.Processando, null, PerfilUsuario.Admin);

        resultado.Falha!.Codigo.Should().Be(CodigosErro.PedidoTransicaoInvalida);
        resultado.Falha.Detalhe.Should().Be("O pedido foi alterado por outra operação; tente novamente.");
        armazenamento.Descartes.Should().Be(3);
        armazenamento.Gravacoes.Should().Be(0);
    }

    private int EstoqueGravado() => armazenamento.Produtos.Single(p => p.Id == produto.Id).QuantidadeEstoque;

    private Pedido Gravado() => armazenamento.ObterPedidoAsync(PedidoId, false, CancellationToken.None).Result!;

    [Fact]
    public async Task Cliente_cancela_pedido_reservado_e_o_estoque_volta()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente, reservado: true);

        var resultado = await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        EstoqueGravado().Should().Be(11);
        Gravado().EstoqueReservado.Should().BeFalse();
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6005);
    }

    [Theory]
    [InlineData(StatusPedido.Processando)]
    [InlineData(StatusPedido.ProntoParaEnvio)]
    public async Task Operacao_cancela_pedido_reservado_e_o_estoque_volta(StatusPedido status)
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(status, reservado: true);

        var resultado = await Mudar(Operador, StatusPedido.Cancelado, "Sem pagamento", PerfilUsuario.Admin);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        EstoqueGravado().Should().Be(11);
        Gravado().EstoqueReservado.Should().BeFalse();
    }

    [Fact]
    public async Task Cancelar_pedido_sem_reserva_nao_mexe_no_estoque()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente);

        (await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        armazenamento.LeiturasProdutosParaAlterar.Should().Be(0);
        EstoqueGravado().Should().Be(10);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 6005);
    }

    [Fact]
    public async Task Outra_transicao_de_pedido_reservado_nao_devolve()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente, reservado: true);

        (await Mudar(Operador, StatusPedido.Processando, null, PerfilUsuario.OperadorLogistica)).Valor!.Status.Should().Be(StatusPedido.Processando);

        EstoqueGravado().Should().Be(10);
        Gravado().EstoqueReservado.Should().BeTrue();
    }

    [Fact]
    public async Task Conflito_no_cancelamento_rele_e_devolve_uma_vez()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente, reservado: true);
        armazenamento.ConflitosRestantes = 1;

        (await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        armazenamento.Descartes.Should().Be(1);
        armazenamento.LeiturasProdutosParaAlterar.Should().Be(2);
        EstoqueGravado().Should().Be(11);
    }

    /// <summary>Reservado, pago e em Processando, como o consumidor de pagamento deixa (REQ-09).</summary>
    private Pedido MontarPago()
    {
        var pedido = Montar(StatusPedido.Pendente, reservado: true);
        pedido.AprovarPagamento("SIM-1", Agora);
        return pedido;
    }

    [Fact]
    public async Task Operacao_cancela_pedido_pago_com_estorno_pendente_sem_chamar_o_gateway()
    {
        armazenamento.Produtos.Add(produto);
        armazenamento.GuardarPedido(PedidoId, MontarPago);

        var resultado = await Mudar(Operador, StatusPedido.Cancelado, "Cliente desistiu", PerfilUsuario.OperadorLogistica);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        var gravado = Gravado();
        gravado.Pagamento!.Situacao.Should().Be(Domain.Pagamentos.SituacaoPagamento.EstornoPendente);
        gravado.Pagamento.EstornoSolicitadoEm.Should().Be(Agora.AddHours(1));
        gravado.EstoqueReservado.Should().BeFalse();
        EstoqueGravado().Should().Be(11);
        var cancelado = EventosPedidoTestes.DadosCancelado(armazenamento.EventosGravados.Should().ContainSingle().Subject);
        (cancelado.Motivo, cancelado.EstoqueDevolvido, cancelado.EstornoPendente, cancelado.OrdemEntregaCancelada)
            .Should().Be((MotivoCancelamento.Operacao, true, true, false), "REQ-15: o cancelamento sai como pedido.cancelado");
        var estorno = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7005).Subject;
        estorno.Propriedades["Origem"]!.ToString().Should().Be("Operacao");
        estorno.Propriedades["PedidoId"].Should().Be(PedidoId);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 6005);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4003);
    }

    [Fact]
    public async Task Cliente_continua_sem_poder_cancelar_pedido_pago()
    {
        armazenamento.GuardarPedido(PedidoId, MontarPago);

        var resultado = await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.SemPermissao);
        Gravado().Pagamento!.Situacao.Should().Be(Domain.Pagamentos.SituacaoPagamento.Aprovado);
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7005);
    }

    [Fact]
    public async Task Conflito_com_a_cobranca_rele_o_pedido_pago_e_pede_o_estorno()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente, reservado: true);
        armazenamento.ConflitosRestantes = 1;
        armazenamento.AoConflitar = () => armazenamento.GuardarPedido(PedidoId, MontarPago);

        var resultado = await Mudar(Operador, StatusPedido.Cancelado, "Cliente desistiu", PerfilUsuario.Admin);

        resultado.Valor!.Status.Should().Be(StatusPedido.Cancelado);
        Gravado().Pagamento!.Situacao.Should().Be(Domain.Pagamentos.SituacaoPagamento.EstornoPendente);
        Gravado().Historico.Last().StatusAnterior.Should().Be(StatusPedido.Processando);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 7005);
    }

    [Fact]
    public async Task Cancelar_pedido_sem_pagamento_nao_pede_estorno()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Processando, reservado: true);

        (await Mudar(Operador, StatusPedido.Cancelado, "Sem pagamento", PerfilUsuario.Admin)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        Gravado().Pagamento.Should().BeNull();
        logger.Eventos.Should().NotContain(e => e.Id.Id == 7005);
    }

    // REQ-15, US1: o cancelamento pela API grava um pedido.cancelado na mesma gravação.

    [Fact]
    public async Task Cliente_que_cancela_gera_pedido_cancelado_com_motivo_Cliente()
    {
        armazenamento.Produtos.Add(produto);
        PedidoEm(StatusPedido.Pendente, reservado: true);

        (await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        var evento = armazenamento.EventosGravados.Should().ContainSingle().Subject;
        evento.Tipo.Should().Be(TiposEvento.PedidoCancelado);
        var dados = EventosPedidoTestes.DadosCancelado(evento);
        dados.PedidoId.Should().Be(PedidoId);
        dados.Numero.Should().Be("PED-20261022-000001");
        dados.Motivo.Should().Be(MotivoCancelamento.Cliente);
        dados.EtapaEsgotada.Should().BeNull();
        dados.CanceladoEm.Should().Be(Agora.AddHours(1));
        (dados.EstoqueDevolvido, dados.EstornoPendente, dados.OrdemEntregaCancelada).Should().Be((true, false, false));
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Operacao_que_cancela_gera_pedido_cancelado_com_motivo_Operacao(PerfilUsuario perfil)
    {
        PedidoEm(StatusPedido.Processando);

        (await Mudar(Operador, StatusPedido.Cancelado, "Sem pagamento", perfil)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        var dados = EventosPedidoTestes.DadosCancelado(armazenamento.EventosGravados.Should().ContainSingle().Subject);
        dados.Motivo.Should().Be(MotivoCancelamento.Operacao);
        (dados.EstoqueDevolvido, dados.EstornoPendente, dados.OrdemEntregaCancelada).Should().Be((false, false, false));
    }

    [Fact]
    public async Task Operador_que_cancela_o_proprio_pedido_gera_motivo_Operacao()
    {
        PedidoEm(StatusPedido.Pendente);

        // O dono também tem perfil de operação: vale o perfil (spec, Edge Cases).
        (await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente, PerfilUsuario.Admin)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        EventosPedidoTestes.DadosCancelado(armazenamento.EventosGravados.Should().ContainSingle().Subject).Motivo.Should().Be(MotivoCancelamento.Operacao);
    }

    [Fact]
    public async Task Conflito_no_cancelamento_grava_um_unico_pedido_cancelado()
    {
        PedidoEm(StatusPedido.Pendente);
        armazenamento.ConflitosRestantes = 1;

        (await Mudar(Dono, StatusPedido.Cancelado, "Desisti", PerfilUsuario.Cliente)).Valor!.Status.Should().Be(StatusPedido.Cancelado);

        armazenamento.EventosGravados.Should().ContainSingle(e => e.Tipo == TiposEvento.PedidoCancelado);
    }

    [Fact]
    public async Task Tres_conflitos_no_cancelamento_nao_gravam_evento()
    {
        PedidoEm(StatusPedido.Pendente);
        armazenamento.ConflitosRestantes = 3;

        var resultado = await Mudar(Operador, StatusPedido.Cancelado, "x", PerfilUsuario.Admin);

        resultado.Falha!.Detalhe.Should().Be("O pedido foi alterado por outra operação; tente novamente.");
        armazenamento.EventosGravados.Should().BeEmpty("US1 cenário 9: cancelamento que não grava não gera evento");
    }

    [Theory]
    [InlineData(StatusPedido.Pendente, StatusPedido.Processando, PerfilUsuario.Admin)]
    [InlineData(StatusPedido.Despachado, StatusPedido.Cancelado, PerfilUsuario.Admin)]
    [InlineData(StatusPedido.Cancelado, StatusPedido.Cancelado, PerfilUsuario.Admin)]
    [InlineData(StatusPedido.Processando, StatusPedido.Cancelado, PerfilUsuario.Cliente)]
    public async Task Outras_transicoes_e_recusas_nao_geram_pedido_cancelado(StatusPedido de, StatusPedido para, PerfilUsuario perfil)
    {
        PedidoEm(de);

        await Mudar(perfil == PerfilUsuario.Cliente ? Dono : Operador, para, "x", perfil);

        armazenamento.EventosGravados.Should().NotContain(e => e.Tipo == TiposEvento.PedidoCancelado);
    }

    [Fact]
    public void O_servico_de_pedidos_nao_depende_do_gateway_de_pagamento()
    {
        // SC-006: o cancelamento não depende do gateway; o estorno é do processo de workers.
        typeof(ServicoPedidos).GetConstructors()
            .SelectMany(c => c.GetParameters())
            .Should().NotContain(p => p.ParameterType == typeof(Application.Pagamentos.IGatewayPagamento));
    }
}
