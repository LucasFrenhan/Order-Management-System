using System.Globalization;
using System.Net;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;

namespace PedidoFulfillment.Testes.Integracao.Saude;

/// <summary>
/// SQL Server e RabbitMQ reais em contêineres (research R-07). Sem Docker, os testes são
/// ignorados em vez de falhar, e a suíte continua rodando (SC-005), exceto com
/// <c>OMS_EXIGIR_DOCKER=true</c>, quando falham (REQ-07).
/// </summary>
public sealed class ConteineresDependencias : IAsyncLifetime
{
    private const string UsuarioRabbitMQ = "oms";
    private const string SenhaRabbitMQ = "oms-teste-conteiner";

    private MsSqlContainer? sqlServer;
    private RabbitMqContainer? rabbitMQ;

    public bool DockerDisponivel { get; private set; }

    public IReadOnlyDictionary<string, string?> Configuracoes => new Dictionary<string, string?>
    {
        ["ConnectionStrings:SqlServer"] = sqlServer!.GetConnectionString(),
        ["RabbitMQ:Servidor"] = rabbitMQ!.Hostname,
        ["RabbitMQ:Porta"] = rabbitMQ.GetMappedPublicPort(5672).ToString(CultureInfo.InvariantCulture),
        ["RabbitMQ:Usuario"] = UsuarioRabbitMQ,
        ["RabbitMQ:Senha"] = SenhaRabbitMQ
    };

    public async ValueTask InitializeAsync()
    {
        try
        {
            sqlServer = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU26-ubuntu-22.04").Build();
            rabbitMQ = new RabbitMqBuilder("rabbitmq:4.3.6-management")
                .WithUsername(UsuarioRabbitMQ)
                .WithPassword(SenhaRabbitMQ)
                .Build();
            await Task.WhenAll(sqlServer.StartAsync(), rabbitMQ.StartAsync());
            DockerDisponivel = true;
        }
        catch (DockerUnavailableException excecao)
        {
            // No CI (OMS_EXIGIR_DOCKER=true), sem Docker os testes falham em vez de serem ignorados (REQ-07).
            if (AmbienteDocker.Obrigatorio())
            {
                throw new InvalidOperationException(AmbienteDocker.MensagemObrigatorio, excecao);
            }

            DockerDisponivel = false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (sqlServer is not null)
        {
            await sqlServer.DisposeAsync();
        }

        if (rabbitMQ is not null)
        {
            await rabbitMQ.DisposeAsync();
        }
    }
}

public sealed class SaudeComDependenciasTestes(ConteineresDependencias conteineres) : IClassFixture<ConteineresDependencias>
{
    [Fact]
    public async Task Prontidao_com_dependencias_no_ar_responde_200()
    {
        Assert.SkipUnless(conteineres.DockerDisponivel, "Docker indisponível");
        await using var fabrica = new FabricaApi { Configuracoes = conteineres.Configuracoes };

        var resposta = await fabrica.CreateClient().GetAsync("/health/pronto", TestContext.Current.CancellationToken);

        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        json.RootElement.GetProperty("status").GetString().Should().Be("Healthy");
        json.RootElement.GetProperty("dependencias").GetProperty("sqlserver").GetString().Should().Be("Healthy");
        json.RootElement.GetProperty("dependencias").GetProperty("rabbitmq").GetString().Should().Be("Healthy");
        json.RootElement.GetProperty("dependencias").TryGetProperty("consumidores", out _).Should().BeFalse("a API não tem consumidores (REQ-17, FR-014)");
    }
}
