using System.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Identidade;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Emails;

/// <summary>
/// Rodada do envio de e-mail no Worker (REQ-24, R-03), no molde do <c>ProcessadorNotificacoes</c>: uma
/// instância por rodada, pelo lock de aplicação. O token é gerado aqui, na hora de enviar, e só o hash
/// é gravado: o token em claro existe só na memória e no e-mail (FR-003). Uma queda entre o envio e a
/// remoção gera um segundo e-mail, e só o token do último vale (FR-021).
/// </summary>
internal sealed class ProcessadorEmails(
    IServiceScopeFactory escopos,
    IOptions<OpcoesEmail> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<ProcessadorEmails> logger) : BackgroundService
{
    internal const string RecursoLock = "oms-emails";

    private const string SemTipo = "-";

    private static readonly TimeSpan IdadeMaxima = TimeSpan.FromHours(24);
    private static readonly TimeSpan EsperaInicial = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EsperaMaxima = TimeSpan.FromMinutes(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Habilitado)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.Intervalo, relogio);
        try
        {
            do
            {
                await ExecutarRodadaAsync(stoppingToken);
            }
            while (await temporizador.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Encerramento do Worker.
        }
    }

    internal async Task ExecutarRodadaAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Conexão própria, aberta até o fim da rodada: o lock é da sessão.
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await AdquirirLockAsync(conexao, cancellationToken))
            {
                return;
            }

            try
            {
                using var rodada = TelemetriaOms.Fonte.StartActivity(TelemetriaOms.Spans.ProcessarEmails, ActivityKind.Internal, default(ActivityContext));
                rodada?.SetTag(TelemetriaOms.Atributos.Rotina, TelemetriaOms.Rotinas.Emails);
                await ProcessarVencidasAsync(cancellationToken);
            }
            finally
            {
                await LiberarLockAsync(conexao);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora ou erro inesperado: tenta na próxima rodada, sem derrubar o processo. Só o
            // tipo do erro: a mensagem poderia repetir um endereço.
            EventosEmails.FalhaEnvioEmail(logger, SemTipo, 0, SemTipo, excecao.GetType().Name);
        }
    }

    private async Task ProcessarVencidasAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<Guid> vencidas;
        await using (var escopo = escopos.CreateAsyncScope())
        {
            var agora = relogio.GetUtcNow();
            vencidas = await escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>().SolicitacoesEmail.AsNoTracking()
                .Where(s => s.ProximaTentativaEm <= agora)
                .OrderBy(s => s.ProximaTentativaEm).ThenBy(s => s.CriadaEm)
                .Take(opcoes.Value.TamanhoLote)
                .Select(s => s.Id)
                .ToListAsync(cancellationToken);
        }

        foreach (var id in vencidas)
        {
            // Sem conexão, as seguintes falhariam igual: não gasta um token nem o tempo limite por linha (analyze U2).
            if (await ProcessarAsync(id, cancellationToken) == ResultadoEnvioEmail.FalhaConexao)
            {
                return;
            }
        }
    }

    /// <summary>Uma solicitação num escopo próprio. Devolve o resultado do envio, se houve envio.</summary>
    private async Task<ResultadoEnvioEmail?> ProcessarAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        if (await contexto.SolicitacoesEmail.SingleOrDefaultAsync(s => s.Id == id, cancellationToken) is not { } solicitacao)
        {
            return null;
        }

        if (relogio.GetUtcNow() - solicitacao.CriadaEm > IdadeMaxima)
        {
            EventosEmails.EmailDescartado(logger, solicitacao.Tipo, MascaraEmail.Mascarar(solicitacao.Destino), "mais de 24 h sem conseguir enviar");
            await ApagarAsync(contexto, solicitacao, TelemetriaOms.Resultados.Descartado, cancellationToken);
            return null;
        }

        var usuario = await escopo.ServiceProvider.GetRequiredService<UserManager<Usuario>>().FindByEmailAsync(solicitacao.Destino);
        if (usuario is null)
        {
            await ApagarAsync(contexto, solicitacao, TelemetriaOms.Resultados.SemConta, cancellationToken);
            return null;
        }

        // O convite é só para quem ainda não tem senha: a recuperação pode ter chegado antes (REQ-25, R-05).
        if ((solicitacao.Tipo == TipoEmail.ConfirmacaoEmail && usuario.EmailConfirmed)
            || (solicitacao.Tipo == TipoEmail.ConviteUsuario && usuario.PasswordHash is not null))
        {
            await ApagarAsync(contexto, solicitacao, TelemetriaOms.Resultados.Descartado, cancellationToken);
            return null;
        }

        var mensagem = await MontarAsync(escopo.ServiceProvider, solicitacao.Tipo, usuario, cancellationToken);
        var resultado = await escopo.ServiceProvider.GetRequiredService<IEnviadorEmail>().EnviarAsync(mensagem, cancellationToken);
        var emailMascarado = MascaraEmail.Mascarar(usuario.Email);
        switch (resultado)
        {
            case ResultadoEnvioEmail.Enviado:
                EventosEmails.EmailEnviado(logger, solicitacao.Tipo, usuario.Id, emailMascarado);
                await ApagarAsync(contexto, solicitacao, TelemetriaOms.Resultados.Enviado, cancellationToken);
                break;
            case ResultadoEnvioEmail.RecusaPermanente:
                EventosEmails.EmailDescartado(logger, solicitacao.Tipo, emailMascarado, "destinatário recusado pelo servidor");
                await ApagarAsync(contexto, solicitacao, TelemetriaOms.Resultados.Descartado, cancellationToken);
                break;
            default:
                await ReagendarAsync(contexto, solicitacao, emailMascarado, resultado, cancellationToken);
                break;
        }

        return resultado;
    }

    /// <summary>O token vale a partir de agora: um e-mail atrasado pela queda do SMTP ainda chega com a validade completa (R-03).</summary>
    private async Task<MensagemEmail> MontarAsync(IServiceProvider servicos, TipoEmail tipo, Usuario usuario, CancellationToken cancellationToken)
    {
        var configuracao = opcoes.Value;
        var destino = usuario.Email!;
        switch (tipo)
        {
            case TipoEmail.RecuperacaoSenha:
                var recuperacao = await GerarTokenAsync(servicos, usuario.Id, FinalidadeTokenUsoUnico.RecuperacaoSenha, configuracao.ValidadeRecuperacao, cancellationToken);
                return ModelosEmail.Recuperacao(destino, recuperacao, configuracao.ValidadeRecuperacao, configuracao.UrlBaseAplicacao);
            case TipoEmail.ConfirmacaoEmail:
                var confirmacao = await GerarTokenAsync(servicos, usuario.Id, FinalidadeTokenUsoUnico.ConfirmacaoEmail, configuracao.ValidadeConfirmacao, cancellationToken);
                return ModelosEmail.Confirmacao(destino, confirmacao, configuracao.ValidadeConfirmacao, configuracao.UrlBaseAplicacao);
            case TipoEmail.ConviteUsuario:
                var convite = await GerarTokenAsync(servicos, usuario.Id, FinalidadeTokenUsoUnico.ConviteUsuario, configuracao.ValidadeConvite, cancellationToken);
                return ModelosEmail.Convite(destino, convite, configuracao.ValidadeConvite, configuracao.UrlBaseAplicacao);
            default:
                return ModelosEmail.AvisoSenhaRedefinida(destino);
        }
    }

    /// <summary>Confirma o hash antes de enviar: nunca sai um e-mail com um token que não vale.</summary>
    private async Task<string> GerarTokenAsync(
        IServiceProvider servicos, Guid usuarioId, FinalidadeTokenUsoUnico finalidade, TimeSpan validade, CancellationToken cancellationToken)
    {
        var token = GeradorTokenOpaco.Gerar();
        await servicos.GetRequiredService<IArmazenamentoSessoes>().CriarTokenUsoUnicoAsync(
            usuarioId, finalidade, GeradorTokenOpaco.Hash(token), relogio.GetUtcNow() + validade, cancellationToken);
        return token;
    }

    private async Task ReagendarAsync(
        ContextoBancoDados contexto, SolicitacaoEmail solicitacao, string emailMascarado, ResultadoEnvioEmail resultado, CancellationToken cancellationToken)
    {
        solicitacao.ProximaTentativaEm = relogio.GetUtcNow() + Espera(solicitacao.Tentativas);
        solicitacao.Tentativas++;
        await contexto.SaveChangesAsync(cancellationToken);
        EventosEmails.FalhaEnvioEmail(logger, solicitacao.Tipo.ToString(), solicitacao.Tentativas, emailMascarado, resultado.ToString());
        Contar(solicitacao.Tipo, TelemetriaOms.Resultados.Falha);
    }

    private static async Task ApagarAsync(ContextoBancoDados contexto, SolicitacaoEmail solicitacao, string resultado, CancellationToken cancellationToken)
    {
        contexto.SolicitacoesEmail.Remove(solicitacao);
        await contexto.SaveChangesAsync(cancellationToken);
        Contar(solicitacao.Tipo, resultado);
    }

    private static void Contar(TipoEmail tipo, string resultado) =>
        TelemetriaOms.Emails.Add(1, new(TelemetriaOms.Atributos.EmailTipo, tipo.ToString()), new(TelemetriaOms.Atributos.Resultado, resultado));

    /// <summary>Dobra de 30 s até 2 min, o que cumpre o SC-007 (até 5 min depois da volta).</summary>
    internal static TimeSpan Espera(int tentativasAnteriores)
    {
        var espera = EsperaInicial * Math.Pow(2, Math.Min(tentativasAnteriores, 4));
        return espera < EsperaMaxima ? espera : EsperaMaxima;
    }

    private static async Task<bool> AdquirirLockAsync(SqlConnection conexao, CancellationToken cancellationToken)
    {
        await using var comando = new SqlCommand(
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
            conexao);
        comando.Parameters.AddWithValue("@recurso", RecursoLock);
        return (int)(await comando.ExecuteScalarAsync(cancellationToken))! >= 0;
    }

    private static async Task LiberarLockAsync(SqlConnection conexao)
    {
        try
        {
            await using var comando = new SqlCommand("EXEC sp_releaseapplock @Resource = @recurso, @LockOwner = 'Session';", conexao);
            comando.Parameters.AddWithValue("@recurso", RecursoLock);
            await comando.ExecuteNonQueryAsync(CancellationToken.None);
        }
        catch (SqlException)
        {
            // Sessão já encerrada: o lock foi junto.
        }
    }
}
