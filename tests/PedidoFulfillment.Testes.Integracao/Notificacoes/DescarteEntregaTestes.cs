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
}
