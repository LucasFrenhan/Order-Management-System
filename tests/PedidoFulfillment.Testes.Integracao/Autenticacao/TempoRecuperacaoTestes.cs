using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// O tempo não revela se o e-mail tem conta (REQ-24, SC-005). Serial com os demais testes de tempo,
/// para um não disputar o banco com o outro.
/// </summary>
[Collection(nameof(ColecaoDesempenhoBanco))]
public sealed class TempoRecuperacaoTestes(BancoDadosTeste banco)
{
    private const int Pedidos = 100;
    private const double DiferencaMaximaMs = 50;
    private const string SemConta = "ninguem@exemplo.com.br";

    [Theory]
    [InlineData(RotaRecuperacao)]
    [InlineData(RotaReenvio)]
    public async Task Com_e_sem_conta_status_e_corpo_iguais_e_medianas_proximas(string rota)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);

        var tempos = new Dictionary<string, List<double>> { [Email] = [], [SemConta] = [] };
        var respostas = new HashSet<string>();
        for (var i = 0; i < Pedidos; i++)
        {
            // Alternados: o que mudar no ambiente durante o teste afeta os dois casos.
            foreach (var email in tempos.Keys)
            {
                var inicio = Stopwatch.GetTimestamp();
                var resposta = await cliente.PostAsJsonAsync(rota, new { email }, Cancelamento);
                tempos[email].Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
                resposta.StatusCode.Should().Be(HttpStatusCode.Accepted);
                respostas.Add(await resposta.Content.ReadAsStringAsync(Cancelamento));
            }
        }

        respostas.Should().ContainSingle("o corpo é idêntico");
        Math.Abs(Mediana(tempos[Email]) - Mediana(tempos[SemConta])).Should().BeLessThan(DiferencaMaximaMs);
    }

    private static double Mediana(List<double> valores)
    {
        valores.Sort();
        return valores[valores.Count / 2];
    }
}
