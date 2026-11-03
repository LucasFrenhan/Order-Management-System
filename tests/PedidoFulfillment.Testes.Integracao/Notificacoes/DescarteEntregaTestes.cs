using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Notificacoes;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using PedidoFulfillment.Testes.Integracao.Persistencia;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Uma entrega pendente de um destino desativado depois de criada é descartada na rodada, sem
/// chamada ao destino (REQ-18, FR-013).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class DescarteEntregaTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Entrega_de_destino_desativado_e_descartada_sem_envio()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
            var produto = DadosPersistencia.NovoProduto();
            var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(pedido);
            contexto.AvisosPedido.Add(AvisoPedido.Criar(Guid.CreateVersion7(), "pedido.status-alterado", pedido.Id, clienteId, "{}", DateTimeOffset.UtcNow));
            await contexto.SaveChangesAsync(Cancelamento);
            await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
                INSERT INTO DestinosNotificacao (Id, ClienteId, CriadoPor, Url, SegredoCifrado, Ativo, FalhasSeguidas, CriadoEm, AtualizadoEm)
                VALUES ('{Guid.CreateVersion7()}', NULL, '{clienteId}', 'https://exemplo.com/a', CRYPT_GEN_RANDOM(60), 0, 3, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
                INSERT INTO EntregasNotificacao (DestinoId, AvisoId, Situacao, Tentativas, ProximaTentativaEm, CriadaEm)
                SELECT (SELECT TOP 1 Id FROM DestinosNotificacao), Id, N'Pendente', 1, DATEADD(MINUTE, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET() FROM AvisosPedido;
                UPDATE CursorNotificacoes SET Posicao = (SELECT MAX(Posicao) FROM AvisosPedido);
                """);
        }

        await using var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1", ["Notificacoes:Envio:Habilitado"] = "false" }
        };
        worker.CreateClient().Dispose();
        var processador = worker.Services.GetServices<IHostedService>().OfType<ProcessadorNotificacoes>().Single();

        await processador.ExecutarRodadaAsync(Cancelamento);

        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao, "SELECT Situacao FROM EntregasNotificacao")).Should().Be(nameof(SituacaoEntrega.Descartada));
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT Tentativas FROM EntregasNotificacao")).Should().Be(1, "nada foi enviado");
    }

    [Fact]
    public async Task Distribuicao_em_lotes_so_cria_entregas_para_os_destinos_do_dono_e_os_globais_ja_existentes()
    {
        // R-05 revista: o cursor anda em lotes; o destino de outro Cliente e o cadastrado depois do aviso não recebem.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = await mensageria.NovoBancoMigradoAsync();
        Guid dono, outro;
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            dono = await DadosPersistencia.NovoUsuarioAsync(contexto);
            outro = await DadosPersistencia.NovoUsuarioAsync(contexto);
            var produto = DadosPersistencia.NovoProduto();
            var pedido = DadosPersistencia.NovoPedido(dono, produto);
            contexto.Produtos.Add(produto);
            contexto.Pedidos.Add(pedido);
            for (var i = 0; i < 3; i++)
            {
                contexto.AvisosPedido.Add(AvisoPedido.Criar(Guid.CreateVersion7(), "pedido.status-alterado", pedido.Id, dono, "{}", DateTimeOffset.UtcNow));
            }

            await contexto.SaveChangesAsync(Cancelamento);
        }

        Guid global = Guid.CreateVersion7(), doDono = Guid.CreateVersion7(), doOutro = Guid.CreateVersion7(), globalNovo = Guid.CreateVersion7();
        await BancoDadosTeste.ExecutarAsync(stringConexao, $"""
            INSERT INTO DestinosNotificacao (Id, ClienteId, CriadoPor, Url, SegredoCifrado, Ativo, FalhasSeguidas, CriadoEm, AtualizadoEm)
            VALUES ('{global}', NULL, '{dono}', 'https://exemplo.com/g', CRYPT_GEN_RANDOM(60), 1, 0, DATEADD(HOUR, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET()),
                   ('{doDono}', '{dono}', '{dono}', 'https://exemplo.com/d', CRYPT_GEN_RANDOM(60), 1, 0, DATEADD(HOUR, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET()),
                   ('{doOutro}', '{outro}', '{outro}', 'https://exemplo.com/o', CRYPT_GEN_RANDOM(60), 1, 0, DATEADD(HOUR, -1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET()),
                   ('{globalNovo}', NULL, '{dono}', 'https://exemplo.com/n', CRYPT_GEN_RANDOM(60), 1, 0, DATEADD(HOUR, 1, SYSDATETIMEOFFSET()), SYSDATETIMEOFFSET());
            """);
        await using var worker = new FabricaWorker
        {
            StringConexao = stringConexao,
            ConfiguracoesBroker = mensageria.ConfiguracoesBroker,
            Configuracoes = new Dictionary<string, string?>
            {
                ["RabbitMQ:Porta"] = "1",
                ["Notificacoes:Envio:Habilitado"] = "false",
                ["Notificacoes:Envio:TamanhoLote"] = "2"
            }
        };
        worker.CreateClient().Dispose();
        var processador = worker.Services.GetServices<IHostedService>().OfType<ProcessadorNotificacoes>().Single();

        // O segredo gravado não decifra: as entregas falham sem nenhuma chamada de rede.
        await processador.ExecutarRodadaAsync(Cancelamento);

        async Task<int> EntregasAsync(Guid destino) =>
            await BancoDadosTeste.ConsultarAsync<int>(stringConexao, $"SELECT COUNT(*) FROM EntregasNotificacao WHERE DestinoId = '{destino}'");
        (await EntregasAsync(global)).Should().Be(3);
        (await EntregasAsync(doDono)).Should().Be(3);
        (await EntregasAsync(doOutro)).Should().Be(0);
        (await EntregasAsync(globalNovo)).Should().Be(0, "o destino novo não recebe o passado");
        (await BancoDadosTeste.ConsultarAsync<bool>(stringConexao,
            "SELECT CAST(CASE WHEN (SELECT Posicao FROM CursorNotificacoes) = (SELECT MAX(Posicao) FROM AvisosPedido) THEN 1 ELSE 0 END AS bit)"))
            .Should().BeTrue("o cursor chega ao último aviso");
    }
}
