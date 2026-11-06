using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Usuarios;

/// <summary>
/// O tempo do login não revela a conta desativada nem a sem senha (REQ-25, SC-004). A base de senha errada
/// troca de conta antes do bloqueio, porque a conta bloqueada nem calcula o hash. Os limites já vêm altos
/// da <see cref="FabricaApi"/>. Serial com os demais testes de tempo.
/// </summary>
[Collection(nameof(ColecaoDesempenhoBanco))]
public sealed class ContaDesativadaTempoTestes(BancoDadosTeste banco)
{
    private const int Tentativas = 100;
    private const int TentativasPorConta = 4;
    private const double DiferencaMaximaMs = 50;
    private const string SenhaErrada = "SenhaErrada9";

    [Fact]
    public async Task Status_e_corpo_iguais_aos_de_senha_errada_e_medianas_proximas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = Api(banco.NovoBanco());
        var cliente = api.CreateClient();
        var erradas = new List<string>();
        for (var i = 0; i < Tentativas / TentativasPorConta; i++)
        {
            var email = $"errada{i}@exemplo.com.br";
            await CenarioGestaoUsuarios.ContaAsync(api, email, perfis: PerfilUsuario.Cliente);
            erradas.Add(email);
        }

        const string Desativada = "desativada@exemplo.com.br";
        const string SemSenha = "semsenha@exemplo.com.br";
        var desativada = await CenarioGestaoUsuarios.ContaAsync(api, Desativada, perfis: PerfilUsuario.Cliente);
        await using (var escopo = api.Services.CreateAsyncScope())
        {
            await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().Users.Where(u => u.Id == desativada)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.DesativadoEm, DateTimeOffset.UtcNow), Cancelamento);
            var conta = new Usuario { UserName = SemSenha, Email = SemSenha, Nome = "Sem Senha" };
            (await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().CreateAsync(conta)).Succeeded.Should().BeTrue();
        }

        // Aquece o caminho: a primeira chamada paga JIT e conexões.
        await TentarLoginAsync(cliente, "aquecimento@exemplo.com.br", SenhaErrada);

        var tempos = new Dictionary<string, List<double>> { ["errada"] = [], [Desativada] = [], [SemSenha] = [] };
        var assinaturas = new HashSet<string>();
        for (var i = 0; i < Tentativas; i++)
        {
            // Alternados: o que mudar no ambiente durante o teste afeta os três casos.
            foreach (var caso in tempos.Keys)
            {
                var email = caso == "errada" ? erradas[i / TentativasPorConta] : caso;
                var inicio = Stopwatch.GetTimestamp();
                var resposta = await TentarLoginAsync(cliente, email, SenhaErrada);
                tempos[caso].Add(Stopwatch.GetElapsedTime(inicio).TotalMilliseconds);
                assinaturas.Add(await AssinaturaAsync(resposta));
            }
        }

        assinaturas.Should().ContainSingle("status e corpo são idênticos nos três casos");
        var mediana = Mediana(tempos["errada"]);
        Math.Abs(Mediana(tempos[Desativada]) - mediana).Should().BeLessThan(DiferencaMaximaMs);
        Math.Abs(Mediana(tempos[SemSenha]) - mediana).Should().BeLessThan(DiferencaMaximaMs);
    }

    private static double Mediana(List<double> valores)
    {
        valores.Sort();
        return valores[valores.Count / 2];
    }
}
