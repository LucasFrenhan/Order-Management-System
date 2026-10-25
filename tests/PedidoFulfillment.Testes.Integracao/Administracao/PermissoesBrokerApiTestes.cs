using System.Net;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Administracao;

/// <summary>
/// O usuário da API no broker só conecta (REQ-14, US4, FR-017 a FR-019): publicar, ler e
/// declarar são recusados, e a prontidão continua funcionando.
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class PermissoesBrokerApiTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private async Task DeveSerRecusadoAsync(Func<IChannel, Task> operacao, string descricao)
    {
        await using var conexao = await mensageria.ConectarComoApiAsync();
        // Com confirmações, a publicação espera a resposta do broker e a recusa aparece na chamada.
        await using var canal = await conexao.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), Cancelamento);

        var executar = () => operacao(canal);

        var excecao = (await executar.Should().ThrowAsync<OperationInterruptedException>(descricao)).Which;
        excecao.ShutdownReason!.ReplyCode.Should().Be(403, $"{descricao}: ACCESS_REFUSED");
    }

    [Fact]
    public async Task Com_as_credenciais_da_API_publicar_ler_e_declarar_sao_recusados()
    {
        // US4 cenário 2, SC-008.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await CenarioReenvio.PrepararFilasAsync(mensageria);

        await using (var conexao = await mensageria.ConectarComoApiAsync())
        {
            conexao.IsOpen.Should().BeTrue("o usuário da API pode abrir a conexão da prontidão");
        }

        await DeveSerRecusadoAsync(c => c.ExchangeDeclareAsync("oms.teste-api", ExchangeType.Topic, cancellationToken: Cancelamento), "declarar troca");
        await DeveSerRecusadoAsync(c => c.QueueDeclareAsync("oms.teste-api", durable: false, exclusive: false, autoDelete: true, cancellationToken: Cancelamento), "declarar fila");
        await DeveSerRecusadoAsync(c => c.BasicPublishAsync(TopologiaMensageria.TrocaEventos, "pedido.criado", Encoding.UTF8.GetBytes("{}"), Cancelamento).AsTask(), "publicar");
        await DeveSerRecusadoAsync(c => c.BasicGetAsync(TopologiaMensageria.FilaPrincipal("estoque"), autoAck: true, Cancelamento), "ler");
        await DeveSerRecusadoAsync(c => c.QueuePurgeAsync(TopologiaMensageria.FilaMortas("estoque"), Cancelamento), "esvaziar");
    }

    [Fact]
    public async Task A_prontidao_da_API_com_o_usuario_dela_mostra_o_broker_saudavel()
    {
        // US4 cenário 3.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        await using var api = new FabricaApi
        {
            PrepararBanco = true,
            StringConexao = mensageria.NovoBanco(),
            Configuracoes = mensageria.ConfiguracoesBrokerApi
        };

        var resposta = await api.CreateClient().GetAsync("/health/pronto", Cancelamento);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        (await resposta.Content.ReadAsStringAsync(Cancelamento)).Should().Contain("\"rabbitmq\":\"Healthy\"");
    }

    [Fact]
    public async Task Com_a_senha_errada_a_prontidao_mostra_o_broker_indisponivel_e_a_criacao_de_pedidos_continua()
    {
        // US4 cenário 4.
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var configuracoes = new Dictionary<string, string?>(mensageria.ConfiguracoesBrokerApi) { ["RabbitMQ:Senha"] = "senha-errada-de-teste" };
        foreach (var (chave, valor) in Autenticacao.UsuariosIniciaisComBancoTestes.Configuracao())
        {
            configuracoes[chave] = valor;
        }

        await using var api = new FabricaApi { PrepararBanco = true, StringConexao = mensageria.NovoBanco(), Configuracoes = configuracoes };

        var prontidao = await api.CreateClient().GetAsync("/health/pronto", Cancelamento);
        var corpo = await prontidao.Content.ReadAsStringAsync(Cancelamento);

        prontidao.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        corpo.Should().Contain("\"rabbitmq\":\"Unhealthy\"").And.NotContain("senha-errada-de-teste");
        var (cliente, _, _) = await ClienteAsync(api);
        (await CriarAsync(cliente, Guid.NewGuid(), (Cat001, 1))).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public void A_API_nao_registra_nenhum_servico_que_fale_com_o_broker()
    {
        // FR-019: só o processo de workers usa o broker.
        using var api = new FabricaApi();

        var servicos = api.Services;

        servicos.GetService<ConexaoRabbitMQ>().Should().BeNull();
        servicos.GetService<ReenvioDlq>().Should().BeNull();
        servicos.GetServices<IHostedService>().Select(s => s.GetType().Namespace)
            .Should().NotContain(typeof(ConexaoRabbitMQ).Namespace, "nenhum serviço da mensageria roda na API");
    }
}
