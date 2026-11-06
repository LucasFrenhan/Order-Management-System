using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Mensageria;
using PedidoFulfillment.Testes.Integracao.Persistencia;
using static PedidoFulfillment.Testes.Integracao.Notificacoes.CenarioWebhooks;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>Os webhooks da conta desativada ficam suspensos, sem reenvio; os globais não mudam (REQ-25, FR-020, R-06).</summary>
[Collection(nameof(ColecaoMensageriaEntregas))]
public sealed class ContaDesativadaWebhooksTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private sealed record CenarioBanco(string Conexao, Guid Dono, Guid Pedido, Guid DestinoDono, Guid DestinoGlobal);

    private static async Task<CenarioBanco> PrepararBancoAsync(string conexao)
    {
        Guid dono, pedidoId;
        await using (var contexto = BancoDadosTeste.NovoContexto(conexao))
        {
            dono = await DadosPersistencia.NovoUsuarioAsync(contexto);
            var produto = DadosPersistencia.NovoProduto();
            var pedido = DadosPersistencia.NovoPedido(dono, produto);
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(pedido);
            await contexto.SaveChangesAsync(Cancelamento);
            pedidoId = pedido.Id;
        }

        Guid doDono = Guid.CreateVersion7(), global = Guid.CreateVersion7();
        await BancoDadosTeste.ExecutarAsync(conexao, $"""
            INSERT INTO DestinosNotificacao (Id, ClienteId, CriadoPor, Url, SegredoCifrado, Ativo, FalhasSeguidas, CriadoEm, AtualizadoEm)
            VALUES ('{doDono}', '{dono}', '{dono}', 'https://exemplo.com/d', CRYPT_GEN_RANDOM(60), 1, 2, DATEADD(DAY, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET()),
                   ('{global}', NULL, '{dono}', 'https://exemplo.com/g', CRYPT_GEN_RANDOM(60), 1, 0, DATEADD(DAY, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());
            """);
        return new CenarioBanco(conexao, dono, pedidoId, doDono, global);
    }

    private static async Task AvisoAsync(CenarioBanco c, DateTimeOffset criadoEm)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(c.Conexao);
        contexto.AvisosPedido.Add(AvisoPedido.Criar(Guid.CreateVersion7(), "pedido.status-alterado", c.Pedido, c.Dono, "{}", criadoEm));
        await contexto.SaveChangesAsync(Cancelamento);
    }

    private FabricaWorker Worker(string conexao) => new()
    {
        StringConexao = conexao,
        ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
        Configuracoes = new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1", ["Notificacoes:Envio:Habilitado"] = "false" }
    };

    private static ProcessadorNotificacoes Processador(FabricaWorker worker)
    {
        worker.CreateClient().Dispose();
        return worker.Services.GetServices<IHostedService>().OfType<ProcessadorNotificacoes>().Single();
    }

    private static Task<int> EntregasAsync(CenarioBanco c, Guid destino) =>
        BancoDadosTeste.ConsultarAsync<int>(c.Conexao, $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destino}'");

    [Fact]
    public async Task Conta_desativada_nao_ganha_entrega_e_o_global_continua_recebendo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var c = await PrepararBancoAsync(await mensageria.NovoBancoMigradoAsync());
        await BancoDadosTeste.ExecutarAsync(c.Conexao, $"UPDATE Usuarios SET DesativadoEm = SYSDATETIMEOFFSET() WHERE Id = '{c.Dono}'");
        await AvisoAsync(c, DateTimeOffset.UtcNow);
        await using var worker = Worker(c.Conexao);

        await Processador(worker).ExecutarRodadaAsync(Cancelamento);

        (await EntregasAsync(c, c.DestinoDono)).Should().Be(0);
        (await EntregasAsync(c, c.DestinoGlobal)).Should().Be(1);
    }

    [Fact]
    public async Task Entrega_pendente_ao_desativar_e_descartada_sem_mexer_no_destino()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var c = await PrepararBancoAsync(await mensageria.NovoBancoMigradoAsync());
        await AvisoAsync(c, DateTimeOffset.UtcNow);
        await BancoDadosTeste.ExecutarAsync(c.Conexao, $"""
            INSERT INTO EntregasNotificacao (DestinoId, AvisoId, Situacao, Tentativas, ProximaTentativaEm, CriadaEm)
            SELECT '{c.DestinoDono}', Id, N'Pendente', 1, DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET() FROM AvisosPedido;
            UPDATE CursorNotificacoes SET Posicao = (SELECT MAX(Posicao) FROM AvisosPedido);
            UPDATE Usuarios SET DesativadoEm = SYSDATETIMEOFFSET() WHERE Id = '{c.Dono}';
            """);
        await using var worker = Worker(c.Conexao);

        await Processador(worker).ExecutarRodadaAsync(Cancelamento);

        (await SituacaoEntregaAsync(c.Conexao, c.DestinoDono)).Should().Be(nameof(SituacaoEntrega.Descartada));
        (await TentativasAsync(c.Conexao, c.DestinoDono)).Should().Be(1, "nada foi enviado");
        (await BancoDadosTeste.ConsultarAsync<int>(c.Conexao, $"SELECT FalhasSeguidas FROM DestinosNotificacao WHERE Id = '{c.DestinoDono}'")).Should().Be(2, "a falha antiga não muda");
        (await BancoDadosTeste.ConsultarAsync<bool>(c.Conexao, $"SELECT Ativo FROM DestinosNotificacao WHERE Id = '{c.DestinoDono}'")).Should().BeTrue();
    }

    [Fact]
    public async Task Aviso_atrasado_no_Worker_nao_sai_depois_de_reativar_e_o_novo_sai()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var c = await PrepararBancoAsync(await mensageria.NovoBancoMigradoAsync());
        var reativadoEm = DateTimeOffset.UtcNow;
        await AvisoAsync(c, reativadoEm.AddMinutes(-5));
        await BancoDadosTeste.ExecutarAsync(c.Conexao, $"UPDATE Usuarios SET ReativadoEm = '{reativadoEm:O}' WHERE Id = '{c.Dono}'");
        await using var worker = Worker(c.Conexao);

        await Processador(worker).ExecutarRodadaAsync(Cancelamento);

        (await EntregasAsync(c, c.DestinoDono)).Should().Be(0, "o aviso é do período desativado");
        (await EntregasAsync(c, c.DestinoGlobal)).Should().Be(1);

        await AvisoAsync(c, reativadoEm.AddMinutes(1));
        await Processador(worker).ExecutarRodadaAsync(Cancelamento);

        (await EntregasAsync(c, c.DestinoDono)).Should().Be(1);
    }

    [Fact]
    public async Task Pendente_anterior_a_reativacao_e_descartada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var c = await PrepararBancoAsync(await mensageria.NovoBancoMigradoAsync());
        var reativadoEm = DateTimeOffset.UtcNow;
        await AvisoAsync(c, reativadoEm.AddMinutes(-5));
        await BancoDadosTeste.ExecutarAsync(c.Conexao, $"""
            INSERT INTO EntregasNotificacao (DestinoId, AvisoId, Situacao, Tentativas, ProximaTentativaEm, CriadaEm)
            SELECT '{c.DestinoDono}', Id, N'Pendente', 1, DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET() FROM AvisosPedido;
            UPDATE CursorNotificacoes SET Posicao = (SELECT MAX(Posicao) FROM AvisosPedido);
            UPDATE Usuarios SET ReativadoEm = '{reativadoEm:O}' WHERE Id = '{c.Dono}';
            """);
        await using var worker = Worker(c.Conexao);

        await Processador(worker).ExecutarRodadaAsync(Cancelamento);

        (await SituacaoEntregaAsync(c.Conexao, c.DestinoDono)).Should().Be(nameof(SituacaoEntrega.Descartada));
    }

    [Fact]
    public async Task De_ponta_a_ponta_nada_chega_durante_a_desativacao_e_depois_chega_com_o_mesmo_segredo()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var conexao = mensageria.NovoBanco();
        await using var api = Api(conexao);
        var (cliente, email, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var admin = await AdminAsync(api);
        var id = await CenarioGestaoUsuarios.IdDeAsync(api, email);
        await using var doCliente = await ReceptorWebhookTeste.IniciarAsync();
        await using var global = await ReceptorWebhookTeste.IniciarAsync();
        var (_, segredo) = await DestinoAsync(cliente, doCliente.Url());
        await DestinoAsync(admin, global.Url());
        await using var worker = WorkerEnviando(mensageria, conexao);
        worker.CreateClient().Dispose();
        var pedido = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();

        (await admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{id}/desativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await MudarStatusAsync(operador, pedido, "Processando")).EnsureSuccessStatusCode();

        (await EsperarRecebidasAsync(global, 1)).Should().BeTrue("o destino global continua recebendo");
        await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
        doCliente.Recebidas.Should().BeEmpty();

        (await admin.PostAsync($"{CenarioGestaoUsuarios.Rota}/{id}/reativacao", null, Cancelamento)).StatusCode.Should().Be(HttpStatusCode.OK);
        await PedidoComAvisoAsync(cliente, operador);

        (await EsperarRecebidasAsync(doCliente, 1)).Should().BeTrue();
        await Task.Delay(TimeSpan.FromSeconds(2), Cancelamento);
        var requisicao = doCliente.Recebidas.Should().ContainSingle("o aviso do período desativado não é reenviado").Subject;
        var idWebhook = requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoId];
        var timestamp = requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoTimestamp];
        requisicao.Cabecalhos[AssinaturaWebhook.CabecalhoAssinatura].Should().Be(AssinaturaWebhook.Assinar(segredo, idWebhook, timestamp, requisicao.Corpo));
    }
}
