using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Entregas;
using PedidoFulfillment.Testes.Integracao.Estoque;
using PedidoFulfillment.Testes.Integracao.Estornos;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using PedidoFulfillment.Testes.Integracao.Pagamentos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Toda mudança de status, por qualquer caminho, grava um <c>pedido.status-alterado</c> e um aviso
/// na mesma gravação; a criação e a correção do rastreio não gravam; o estorno confirmado grava o
/// aviso <c>pedido.estorno-confirmado</c> (REQ-18, US1 cenários 1 a 7; FR-001 a FR-005).
/// </summary>
[Collection(nameof(ColecaoMensageriaEntregas))]
public sealed class AvisosPorCaminhoTestes(MensageriaTeste mensageria)
{
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(20);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>Os <c>pedido.status-alterado</c> do pedido, em ordem de gravação.</summary>
    private static async Task<IReadOnlyList<EnvelopeEvento<PedidoStatusAlterado>>> MudancasAsync(string stringConexao, Guid pedidoId) =>
        await EventosOutbox.ListarAsync<PedidoStatusAlterado>(stringConexao, pedidoId);

    private static async Task<List<(Guid IdNotificacao, string Tipo, Guid ClienteId, string Conteudo)>> AvisosAsync(string stringConexao, Guid pedidoId)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var avisos = await contexto.AvisosPedido.AsNoTracking().Where(a => a.PedidoId == pedidoId).OrderBy(a => a.Id)
            .Select(a => new { a.IdNotificacao, a.Tipo, a.ClienteId, a.Conteudo })
            .ToListAsync(Token);
        return [.. avisos.Select(a => (a.IdNotificacao, a.Tipo, a.ClienteId, a.Conteudo))];
    }

    /// <summary>
    /// Confere o invariante do REQ-18 para o pedido: uma mudança por entrada do histórico além da
    /// criação, com sequência 2..n, e um aviso de status com o mesmo identificador de cada evento,
    /// válidos pelos schemas.
    /// </summary>
    private static async Task<IReadOnlyList<PedidoStatusAlterado>> ConferirAsync(string stringConexao, Guid pedidoId)
    {
        var mudancas = await MudancasAsync(stringConexao, pedidoId);
        var entradas = await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM HistoricosStatusPedido WHERE PedidoId = '{pedidoId}'");
        mudancas.Should().HaveCount(entradas - 1);
        mudancas.Select(m => m.Dados.Sequencia).Should().Equal(Enumerable.Range(2, mudancas.Count));

        var avisos = (await AvisosAsync(stringConexao, pedidoId)).Where(a => a.Tipo == TiposEvento.PedidoStatusAlterado).ToList();
        avisos.Select(a => a.IdNotificacao).Should().Equal(mudancas.Select(m => m.IdEvento));
        foreach (var aviso in avisos)
        {
            SchemaNotificacaoTestes.DeveSerValido(aviso.Conteudo).Should().BeTrue(aviso.Conteudo);
        }

        foreach (var mudanca in mudancas)
        {
            SchemaEventosTestes.DeveSerValido(TiposEvento.PedidoStatusAlterado, SerializacaoEventos.Serializar(mudanca)).Should().BeTrue();
        }

        return [.. mudancas.Select(m => m.Dados)];
    }

    [Fact]
    public async Task Mudancas_pela_api_tem_origem_Operacao_e_a_criacao_e_a_correcao_do_rastreio_nao_geram_aviso()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await MudancasAsync(stringConexao, id)).Should().BeEmpty("a criação não gera pedido.status-alterado (cenário 4)");
        (await AvisosAsync(stringConexao, id)).Should().BeEmpty();

        (await MudarStatusAsync(operador, id, "Processando")).EnsureSuccessStatusCode();
        (await MudarStatusAsync(operador, id, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        (await RegistrarRastreioAsync(operador, id, "Correios", "AA123456789BR")).EnsureSuccessStatusCode();
        (await RegistrarRastreioAsync(operador, id, "Correios", "AA987654321BR")).EnsureSuccessStatusCode();

        var mudancas = await ConferirAsync(stringConexao, id);
        mudancas.Select(m => (m.StatusAnterior, m.StatusNovo, m.Origem)).Should().Equal(
            (StatusPedido.Pendente, StatusPedido.Processando, OrigemMudancaStatus.Operacao),
            (StatusPedido.Processando, StatusPedido.ProntoParaEnvio, OrigemMudancaStatus.Operacao),
            (StatusPedido.ProntoParaEnvio, StatusPedido.Despachado, OrigemMudancaStatus.Operacao));
        (await AvisosAsync(stringConexao, id)).Should().HaveCount(3, "a correção do rastreio não gera aviso (cenário 5)");
    }

    [Fact]
    public async Task Cancelamento_pelo_Cliente_grava_pedido_cancelado_e_status_alterado_com_origem_Cliente()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await MudarStatusAsync(cliente, id, "Cancelado", "Meu CPF é 123.456.789-00")).EnsureSuccessStatusCode();

        (await EventosOutbox.CanceladoAsync(stringConexao, id)).Motivo.Should().Be(MotivoCancelamento.Cliente);
        var mudanca = (await ConferirAsync(stringConexao, id)).Should().ContainSingle().Subject;
        (mudanca.StatusNovo, mudanca.Origem).Should().Be((StatusPedido.Cancelado, OrigemMudancaStatus.Cliente));
        var aviso = (await AvisosAsync(stringConexao, id)).Single();
        aviso.Conteudo.Should().NotContain("CPF");
        using var corpo = JsonDocument.Parse(aviso.Conteudo);
        corpo.RootElement.GetProperty("dados").GetProperty("link").GetString().Should().Be($"/api/v1/pedidos/{id}");
    }

    [Fact]
    public async Task Admin_que_tambem_e_Cliente_muda_o_proprio_pedido_com_origem_Operacao()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (_, email, _) = await ClienteAsync(api);
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            INSERT INTO UsuariosPerfis (UserId, RoleId)
            SELECT u.Id, p.Id FROM Usuarios u CROSS JOIN Perfis p WHERE u.Email = '{email}' AND p.Name = 'Admin'
            """);
        var ambos = api.CreateClient().ComToken(await LoginComBancoTestes.TokenAsync(api.CreateClient(), email, Senha));
        var id = (await CriadoAsync(ambos, (Cat001, 1))).GetProperty("id").GetGuid();

        (await MudarStatusAsync(ambos, id, "Cancelado", "Teste")).EnsureSuccessStatusCode();

        (await ConferirAsync(stringConexao, id)).Single().Origem.Should().Be(OrigemMudancaStatus.Operacao);
    }

    [Fact]
    public async Task Fluxo_pelo_Worker_tem_origem_Worker_e_o_despacho_chega_a_fila_de_um_sistema_de_fora()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var fila = await mensageria.FilaTesteAsync(TiposEvento.PedidoStatusAlterado);
        try
        {
            var id = await CenarioEstorno.PedidoProntoParaEnvioAsync(mensageria, stringConexao, cliente);
            (await RegistrarRastreioAsync(operador, id, "Correios", "BB123456789BR")).EnsureSuccessStatusCode();

            var mudancas = await ConferirAsync(stringConexao, id);
            mudancas.Select(m => (m.StatusNovo, m.Origem)).Should().Equal(
                (StatusPedido.Processando, OrigemMudancaStatus.Worker),
                (StatusPedido.ProntoParaEnvio, OrigemMudancaStatus.Worker),
                (StatusPedido.Despachado, OrigemMudancaStatus.Operacao));

            // Independent Test da US1: publicado pelo despachante a quem ligar a fila (analyze I2).
            await using var worker = await CenarioEntrega.WorkerProntoAsync(mensageria, stringConexao);
            (await MensageriaTeste.EsperarAteAsync(() => Task.FromResult(CenarioEstoque.DoPedido(fila, id).Count >= 3), Prazo)).Should().BeTrue();
            // A entrega é pelo menos uma vez: um evento publicado e não marcado como enviado (o
            // Worker do cenário encerrado antes do commit, ou a confirmação atrasada) sai de novo,
            // com o mesmo MessageId, e quem consome descarta o repetido (issue #70).
            CenarioEstoque.DoPedido(fila, id).DistinctBy(m => m.Propriedades.MessageId)
                .Select(m => SerializacaoEventos.Desserializar<PedidoStatusAlterado>(m.Texto).Dados.Sequencia)
                .Should().BeEquivalentTo(new[] { 2, 3, 4 });
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    [Fact]
    public async Task Pagamento_recusado_gera_aviso_de_PagamentoRejeitado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        try
        {
            await using var worker = await CenarioPagamento.WorkerProntoAsync(mensageria, stringConexao, taxaAprovacao: 0);
            var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
            (await CenarioPagamento.EsperarStatusAsync(stringConexao, id, "PagamentoRejeitado", Prazo)).Should().BeTrue();

            var mudanca = (await ConferirAsync(stringConexao, id)).Should().ContainSingle().Subject;
            (mudanca.StatusNovo, mudanca.Origem).Should().Be((StatusPedido.PagamentoRejeitado, OrigemMudancaStatus.Worker));
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    [Fact]
    public async Task Estorno_confirmado_grava_o_aviso_com_o_identificador_do_pagamento_estornado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        try
        {
            var id = await CenarioEstorno.PedidoPagoAsync(mensageria, stringConexao, cliente);
            await CenarioEstorno.CancelarComoOperacaoAsync(operador, id);
            await using var worker = CenarioEstorno.WorkerEstornando(mensageria, stringConexao);
            worker.CreateClient().Dispose();
            (await CenarioEstorno.EsperarSituacaoAsync(stringConexao, id, "Estornado")).Should().BeTrue();

            var estornado = (await EventosOutbox.ListarAsync<PagamentoEstornado>(stringConexao, id)).Should().ContainSingle().Subject;
            var aviso = (await AvisosAsync(stringConexao, id)).Where(a => a.Tipo == "pedido.estorno-confirmado").Should().ContainSingle().Subject;
            aviso.IdNotificacao.Should().Be(estornado.IdEvento);
            SchemaNotificacaoTestes.DeveSerValido(aviso.Conteudo).Should().BeTrue();
            await ConferirAsync(stringConexao, id);
        }
        finally
        {
            await LimparTodasAsync();
        }
    }

    private async Task LimparTodasAsync()
    {
        foreach (var consumidor in new[] { CenarioEstoque.FilaEstoque, CenarioPagamento.FilaPagamento, CenarioEntrega.FilaEntrega })
        {
            await mensageria.LimparFilasConsumidorAsync(consumidor);
        }
    }
}
