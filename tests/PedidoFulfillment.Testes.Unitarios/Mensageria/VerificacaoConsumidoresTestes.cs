using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PedidoFulfillment.Infrastructure.Mensageria;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Saude;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Entrada <c>consumidores</c> da prontidão do Worker (REQ-17, FR-013).</summary>
public sealed class VerificacaoConsumidoresTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private readonly EstadoConsumidores estado = new([new Descritor("estoque"), new Descritor("pagamento"), new Descritor("entrega")]);

    [Fact]
    public async Task Antes_da_primeira_inscricao_nao_esta_pronta()
    {
        DeveSerSemDetalhes(await VerificarAsync(), HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task Com_todos_inscritos_esta_pronta()
    {
        InscreverTodos();

        DeveSerSemDetalhes(await VerificarAsync(), HealthStatus.Healthy);
    }

    [Fact]
    public async Task Um_consumidor_sem_inscricao_deixa_nao_pronta_na_hora()
    {
        InscreverTodos();

        estado.MarcarSemInscricao("pagamento", T0);

        DeveSerSemDetalhes(await VerificarAsync(), HealthStatus.Unhealthy);
        estado.MarcarInscrito("pagamento");
        DeveSerSemDetalhes(await VerificarAsync(), HealthStatus.Healthy);
    }

    [Fact]
    public async Task Sem_consumidores_registrados_nao_esta_pronta()
    {
        var vazio = new VerificacaoConsumidores(new EstadoConsumidores([]));

        (await vazio.CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken)).Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public void Estado_espelha_a_inscricao_para_a_metrica_7()
    {
        // Nome único: o espelho é estático e outros testes rodam em paralelo.
        var consumidor = $"teste-{Guid.NewGuid():N}";
        var proprio = new EstadoConsumidores([new Descritor(consumidor)]);
        _ = proprio.TodosInscritos;
        Inscrito(consumidor).Should().Be(0, "começa sem inscrição");

        proprio.MarcarInscrito(consumidor);
        Inscrito(consumidor).Should().Be(1);

        proprio.MarcarSemInscricao(consumidor, T0);
        Inscrito(consumidor).Should().Be(0);
        proprio.MarcarSemInscricao(consumidor, T0.AddSeconds(5));
        proprio.SemInscricaoDesde(consumidor).Should().Be(T0, "guarda o instante da primeira perda");
    }

    private static long? Inscrito(string consumidor) =>
        EstadoMetricas.ConsumidoresInscritos.Where(i => i.Key == consumidor).Select(i => (long?)i.Value).SingleOrDefault();

    private void InscreverTodos()
    {
        foreach (var consumidor in new[] { "estoque", "pagamento", "entrega" })
        {
            estado.MarcarInscrito(consumidor);
        }
    }

    private Task<HealthCheckResult> VerificarAsync() =>
        new VerificacaoConsumidores(estado).CheckHealthAsync(new HealthCheckContext(), TestContext.Current.CancellationToken);

    /// <summary>Nada de descrição, exceção ou dados: a resposta só mostra nomes e estados (REQ-02).</summary>
    private static void DeveSerSemDetalhes(HealthCheckResult resultado, HealthStatus esperado)
    {
        resultado.Status.Should().Be(esperado);
        resultado.Description.Should().BeNull();
        resultado.Exception.Should().BeNull();
        resultado.Data.Should().BeEmpty();
    }

    private sealed class Descritor(string consumidor) : IDescritorConsumidor
    {
        public string Consumidor => consumidor;

        public IReadOnlyCollection<string> Tipos => [];
    }
}
