using FluentAssertions;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Leitura das imagens do <c>docker-compose.yml</c>, a fonte única que o Dependabot atualiza
/// (REQ-26, FR-019, research R-08). Não precisa de Docker.
/// </summary>
public sealed class ImagensDoAmbienteTestes
{
    private const string Arquivo = "/repo/docker-compose.yml";

    private const string Compose = """
        name: oms

        services:
          sqlserver:
            build:
              context: docker/sqlserver
            image: oms-sqlserver:fts
            environment:
              ACCEPT_EULA: "Y"
            healthcheck:
              test: ["CMD-SHELL", "image: falsa"]

          rabbitmq:
            # Comentário com image: falsa:1.0
            image: rabbitmq:4.3.6-management
            ports:
              - "5672:5672"

          mailpit:
            image: "axllent/mailpit:v1.33.0"

          sem-imagem:
            build:
              context: .

          sem-tag:
            image: redis

          ultima:
            image: redis:latest

        volumes:
          image:
        """;

    [Theory]
    [InlineData("rabbitmq", "rabbitmq:4.3.6-management")]
    [InlineData("mailpit", "axllent/mailpit:v1.33.0")]
    [InlineData("sqlserver", "oms-sqlserver:fts")]
    public void Devolve_a_imagem_do_servico(string servico, string esperada)
    {
        ImagensDoAmbiente.Ler(Compose, servico, Arquivo).Should().Be(esperada);
    }

    [Fact]
    public void Servico_ausente_cita_o_servico_e_o_arquivo()
    {
        var ler = () => ImagensDoAmbiente.Ler(Compose, "postgres", Arquivo);

        ler.Should().Throw<InvalidOperationException>()
            .WithMessage("*'postgres'*/repo/docker-compose.yml*");
    }

    [Fact]
    public void Servico_sem_image_cita_o_servico_e_o_arquivo()
    {
        var ler = () => ImagensDoAmbiente.Ler(Compose, "sem-imagem", Arquivo);

        ler.Should().Throw<InvalidOperationException>()
            .WithMessage("*'sem-imagem'*/repo/docker-compose.yml*");
    }

    [Theory]
    [InlineData("sem-tag")]
    [InlineData("ultima")]
    public void Imagem_precisa_de_tag_fixa(string servico)
    {
        var ler = () => ImagensDoAmbiente.Ler(Compose, servico, Arquivo);

        ler.Should().Throw<InvalidOperationException>()
            .WithMessage("*tag*latest*");
    }

    [Fact]
    public void Servicos_dos_testes_resolvem_pelo_compose_do_repositorio()
    {
        ImagensDoAmbiente.RabbitMq.Should().StartWith("rabbitmq:").And.NotEndWith(":latest");
        ImagensDoAmbiente.Mailpit.Should().StartWith("axllent/mailpit:").And.NotEndWith(":latest");
    }
}
