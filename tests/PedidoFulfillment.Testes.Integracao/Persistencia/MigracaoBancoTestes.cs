using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// User Story 4: a migration <c>EntidadesNegocio</c> atualiza um banco do REQ-03 sem perder
/// contas (FR-014, SC-003) e segue as regras de aplicação do REQ-03 (FR-015). O cenário 4
/// (modelo sem migration pendente) está em <see cref="ModeloSincronizadoTestes"/>.
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class MigracaoBancoTestes(BancoDadosTeste banco)
{
    private const string Senha = "SenhaForte1";
    private const string InicialIdentidade = "InicialIdentidade";

    private const string ContarTabelasNegocio =
        "SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME IN " +
        "('Produtos', 'Pedidos', 'ItensPedido', 'HistoricosStatusPedido', 'Pagamentos', 'OrdensEntrega')";

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Banco_do_REQ_03_com_usuarios_e_atualizado_sem_perder_contas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await BancoDadosTeste.MigrarAteAsync(stringConexao, InicialIdentidade);
        var emails = Enumerable.Range(1, 3).Select(i => $"cliente{i}@exemplo.com.br").ToList();
        await using (var api = new FabricaApi { StringConexao = stringConexao })
        {
            // Os perfis que a preparação do REQ-03 teria criado nesse banco; ela fica desligada
            // porque aplicaria também a migration nova.
            await using (var escopo = api.Services.CreateAsyncScope())
            {
                var perfis = escopo.ServiceProvider.GetRequiredService<RoleManager<Perfil>>();
                foreach (var nome in Enum.GetNames<PerfilUsuario>())
                {
                    (await perfis.CreateAsync(new Perfil(nome))).Succeeded.Should().BeTrue();
                }
            }

            var cliente = api.CreateClient();
            foreach (var email in emails)
            {
                var registro = await cliente.PostAsJsonAsync("/api/v1/auth/register", new { nome = "Cliente", email, senha = Senha }, Cancelamento);
                registro.StatusCode.Should().Be(HttpStatusCode.Created);
            }
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarTabelasNegocio)).Should().Be(0);
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            await contexto.Database.MigrateAsync(Cancelamento);
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarTabelasNegocio)).Should().Be(6);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, "SELECT COUNT(*) FROM Usuarios")).Should().Be(3);
        await using var apiAtualizada = new FabricaApi { StringConexao = stringConexao };
        var clienteAtualizado = apiAtualizada.CreateClient();
        foreach (var email in emails)
        {
            var login = await clienteAtualizado.PostAsJsonAsync("/api/v1/auth/login", new { email, senha = Senha }, Cancelamento);
            login.StatusCode.Should().Be(HttpStatusCode.OK, email);
        }
    }

    [Fact]
    public async Task Banco_vazio_recebe_o_esquema_completo()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();

        await using (var api = new FabricaApi { PrepararBanco = true, StringConexao = stringConexao })
        {
            api.CreateClient().Dispose();
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarTabelasNegocio)).Should().Be(6);
        (await BancoDadosTeste.ConsultarAsync<string>(stringConexao,
            "SELECT STRING_AGG(SUBSTRING(MigrationId, 16, 100), ',') WITHIN GROUP (ORDER BY MigrationId) FROM __EFMigrationsHistory"))
            .Should().Be("InicialIdentidade,EntidadesNegocio,ChavesIdempotencia");
    }

    [Fact]
    public async Task Fora_de_development_a_api_recusa_a_migration_pendente_e_inicia_depois_do_comando()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        await BancoDadosTeste.MigrarAteAsync(stringConexao, InicialIdentidade);

        await using (var fabrica = new FabricaApi { Ambiente = "Production", PrepararBanco = true, StringConexao = stringConexao })
        {
            var excecao = Record.Exception(() => fabrica.CreateClient());

            excecao.Should().NotBeNull();
            PreparacaoInicializacaoTestes.Cadeia(excecao!).Should().Contain(e => e.Message ==
                "O banco de dados tem migrations pendentes. Aplique-as com o comando documentado antes de iniciar a API.");
        }

        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao, ContarTabelasNegocio)).Should().Be(0);

        // O equivalente ao "dotnet ef database update" documentado.
        await BancoDadosTeste.AplicarMigrationsAsync(stringConexao);
        await using var depois = new FabricaApi { Ambiente = "Production", PrepararBanco = true, StringConexao = stringConexao };
        var resposta = await depois.CreateClient().GetAsync("/health", Cancelamento);
        resposta.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
