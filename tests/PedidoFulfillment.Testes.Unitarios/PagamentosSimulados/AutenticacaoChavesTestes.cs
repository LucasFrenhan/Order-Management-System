using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.PagamentosSimulados.Seguranca;
using PedidoFulfillment.PagamentosSimulados.Simulacao;

namespace PedidoFulfillment.Testes.Unitarios.PagamentosSimulados;

/// <summary>Autenticação por chave da API simulada (REQ-19, FR-002 e FR-007a, research R-05).</summary>
public sealed class AutenticacaoChavesTestes
{
    private const string ChaveApi = "chave-api-de-teste-123";
    private const string ChaveControle = "chave-controle-de-teste-123";

    private static async Task<(int Status, bool Passou)> ChamarAsync(string rota, string? cabecalho = null, string? valor = null)
    {
        var passou = false;
        var middleware = new AutenticacaoChaves(
            _ =>
            {
                passou = true;
                return Task.CompletedTask;
            },
            Options.Create(new OpcoesSeguranca { ChaveApi = ChaveApi, ChaveControle = ChaveControle }),
            NullLogger<AutenticacaoChaves>.Instance);
        var contexto = new DefaultHttpContext { RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider() };
        contexto.Request.Method = "POST";
        contexto.Request.Path = rota;
        contexto.Response.Body = new MemoryStream();
        if (cabecalho is not null)
        {
            contexto.Request.Headers[cabecalho] = valor;
        }

        await middleware.InvokeAsync(contexto);
        return (contexto.Response.StatusCode, passou);
    }

    [Fact]
    public async Task A_chave_certa_de_cada_grupo_passa()
    {
        (await ChamarAsync("/v1/cobrancas", "X-Api-Key", ChaveApi)).Passou.Should().BeTrue();
        (await ChamarAsync("/controle/modo", "X-Chave-Controle", ChaveControle)).Passou.Should().BeTrue();
    }

    [Fact]
    public async Task Health_nao_exige_chave()
    {
        (await ChamarAsync("/health")).Passou.Should().BeTrue();
    }

    [Theory]
    [InlineData("/v1/cobrancas", null, null)]
    [InlineData("/v1/cobrancas", "X-Api-Key", "chave-errada-qualquer")]
    [InlineData("/v1/cobrancas", "X-Api-Key", "")]
    [InlineData("/v1/cobrancas", "X-Chave-Controle", ChaveControle)]
    [InlineData("/v1/estornos", "X-Api-Key", ChaveControle)]
    [InlineData("/controle/modo", "X-Api-Key", ChaveApi)]
    [InlineData("/controle/roteiro", "X-Chave-Controle", ChaveApi)]
    [InlineData("/controle/restaurar", null, null)]
    public async Task Chave_ausente_errada_ou_de_outro_grupo_da_401_sem_chegar_a_rota(string rota, string? cabecalho, string? valor)
    {
        var (status, passou) = await ChamarAsync(rota, cabecalho, valor);

        status.Should().Be(StatusCodes.Status401Unauthorized);
        passou.Should().BeFalse("a autenticação vem antes de qualquer comportamento simulado");
    }

    [Fact]
    public void A_comparacao_e_exata()
    {
        var esperada = Encoding.UTF8.GetBytes(ChaveApi);

        AutenticacaoChaves.Confere(ChaveApi, esperada).Should().BeTrue();
        AutenticacaoChaves.Confere(ChaveApi + "x", esperada).Should().BeFalse();
        AutenticacaoChaves.Confere(ChaveApi.ToUpperInvariant(), esperada).Should().BeFalse();
        AutenticacaoChaves.Confere(null, esperada).Should().BeFalse();
    }
}
