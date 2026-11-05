using System.Net;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Testes.Integracao.Emails;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Autenticacao.CenarioAutenticacao;

namespace PedidoFulfillment.Testes.Integracao.Autenticacao;

/// <summary>
/// Um despejo do banco não expõe nenhum token utilizável (REQ-24, SC-006), e nenhum sinal traz token,
/// senha, e-mail completo ou endereço de origem (SC-008).
/// </summary>
public sealed class SigiloTokensTestes(BancoDadosTeste banco)
{
    private static readonly string[] Tabelas = ["SessoesAutenticacao", "TokensRenovacao", "TokensUsoUnico", "SolicitacoesEmail"];

    /// <summary>Todas as colunas das quatro tabelas, como texto, e os binários também em hexadecimal.</summary>
    private static async Task<string> DespejoAsync(string stringConexao)
    {
        var despejo = new StringBuilder();
        await using var conexao = new SqlConnection(stringConexao);
        await conexao.OpenAsync(Cancelamento);
        foreach (var tabela in Tabelas)
        {
            await using var comando = new SqlCommand($"SELECT * FROM {tabela}", conexao);
            await using var leitor = await comando.ExecuteReaderAsync(Cancelamento);
            while (await leitor.ReadAsync(Cancelamento))
            {
                for (var i = 0; i < leitor.FieldCount; i++)
                {
                    var valor = leitor.GetValue(i);
                    despejo.Append(valor is byte[] bytes ? Convert.ToHexString(bytes) + "|" + Convert.ToBase64String(bytes) : valor).Append(';');
                }
            }
        }

        return despejo.ToString();
    }

    [Fact]
    public async Task Os_tokens_em_claro_nao_estao_no_banco_e_o_hash_nao_autentica()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var confirmacao = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        var login = await LoginAsync(cliente);
        var renovado = await TokensAsync(await RenovarAsync(cliente, login.Renovacao));
        await PedirRecuperacaoAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var recuperacao = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);

        var despejo = await DespejoAsync(conexao);

        despejo.Should().NotBeEmpty();
        foreach (var token in new[] { confirmacao, recuperacao, login.Renovacao, renovado.Renovacao })
        {
            despejo.Should().NotContain(token);
        }

        despejo.Should().Contain(Convert.ToHexString(GeradorTokenOpaco.Hash(renovado.Renovacao)), "só o hash é gravado");
        var hashComoToken = Convert.ToHexString(GeradorTokenOpaco.Hash(renovado.Renovacao));
        (await RenovarAsync(cliente, hashComoToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized, "o hash não serve como token");
        (await RedefinirAsync(cliente, Convert.ToHexString(GeradorTokenOpaco.Hash(recuperacao)))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_solicitacao_nao_guarda_token_e_some_depois_de_processada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        await using var api = ApiComEmail(conexao, enviador);
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);

        await PedirRecuperacaoAsync(cliente);
        (await BancoDadosTeste.ConsultarAsync<int>(conexao, "SELECT COUNT(*) FROM SolicitacoesEmail")).Should().Be(2, "o registro e o pedido");
        await RodadaEmailsAsync(api, conexao);

        (await BancoDadosTeste.ConsultarAsync<int>(conexao, "SELECT COUNT(*) FROM SolicitacoesEmail")).Should().Be(0);
    }

    [Fact]
    public async Task Logs_metricas_e_spans_nao_trazem_token_senha_email_nem_origem()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        const string origem = "203.0.113.77";
        var conexao = banco.NovoBanco();
        var enviador = new EnviadorEmailFalso();
        var logs = new CapturaLogs();
        using var telemetria = new ColetorTelemetria();
        await using var api = new FabricaApi
        {
            PrepararBanco = true,
            StringConexao = conexao,
            Logs = logs,
            Telemetria = telemetria,
            EnderecoConexao = IPAddress.Parse(origem),
            Servicos = servicos => servicos.AddSingleton<IEnviadorEmail>(enviador)
        };
        var cliente = api.CreateClient();
        await RegistrarAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var confirmacao = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        (await cliente.PostAsync(RotaConfirmacao, JsonContent.Create(new { token = confirmacao }), Cancelamento)).EnsureSuccessStatusCode();
        var login = await LoginAsync(cliente);
        var renovado = await TokensAsync(await RenovarAsync(cliente, login.Renovacao));
        await RenovarAsync(cliente, login.Renovacao);
        await PedirRecuperacaoAsync(cliente);
        await RodadaEmailsAsync(api, conexao);
        var recuperacao = ProcessadorEmailsTestes.TokenDe(enviador.Mensagens[^1]);
        (await RedefinirAsync(cliente, recuperacao)).EnsureSuccessStatusCode();
        await LogoutAsync(cliente, renovado.Renovacao);

        string[] proibidos = [confirmacao, recuperacao, login.Renovacao, renovado.Renovacao, Senha, NovaSenha, Email, origem];
        var sinais = logs.Textos
            .Concat(telemetria.Logs.Select(l => l.ToString()))
            .Concat(telemetria.Spans
                .Where(s => s.OperationName.Contains("auth", StringComparison.OrdinalIgnoreCase) || s.Source.Name == "PedidoFulfillment")
                .SelectMany(s => s.TagObjects.Select(t => $"{t.Key}={t.Value}")))
            .Concat(telemetria.Medicoes.SelectMany(m => m.Atributos.Select(a => $"{a.Key}={a.Value}")))
            .ToArray();

        sinais.Should().NotBeEmpty();
        foreach (var valor in proibidos)
        {
            sinais.Should().NotContain(s => s.Contains(valor, StringComparison.OrdinalIgnoreCase), $"o valor '{valor}' não pode aparecer");
        }

        logs.Registros.Should().Contain(r => r.Evento.Id == 3010).And.Contain(r => r.Evento.Id == 3014).And.Contain(r => r.Evento.Id == 3015).And.Contain(r => r.Evento.Id == 3016);
        telemetria.Soma("oms.autenticacao.renovacoes", ("oms.resultado", "renovada")).Should().BeGreaterThanOrEqualTo(1);
        telemetria.Soma("oms.autenticacao.renovacoes", ("oms.resultado", "reutilizada")).Should().BeGreaterThanOrEqualTo(1);
        telemetria.Soma("oms.emails", ("oms.email.tipo", "RecuperacaoSenha"), ("oms.resultado", "enviado")).Should().BeGreaterThanOrEqualTo(1);
    }
}
