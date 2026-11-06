using System.Diagnostics;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>
/// Rodada dos webhooks no Worker (REQ-18, research R-05 revista), no molde do
/// <c>ProcessadorEstornos</c>: uma instância por rodada, pelo lock de aplicação. Primeiro distribui
/// os avisos novos em entregas, uma por destino, avançando o cursor na mesma transação; depois envia
/// as entregas vencidas, no máximo uma por destino, em paralelo.
/// </summary>
internal sealed class ProcessadorNotificacoes(
    IServiceScopeFactory escopos,
    IOptions<OpcoesNotificacoes> opcoes,
    IOptions<OpcoesSqlServer> sqlServer,
    TimeProvider relogio,
    ILogger<ProcessadorNotificacoes> logger) : BackgroundService
{
    internal const string RecursoLock = "oms-notificacoes";

    /// <summary>Lotes de distribuição por rodada, para uma rodada não crescer sem limite.</summary>
    private const int LotesDistribuicao = 10;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var configuracao = opcoes.Value;
        if (!configuracao.Envio.Habilitado)
        {
            return;
        }

        using var temporizador = new PeriodicTimer(configuracao.Envio.Intervalo, relogio);
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
            // Conexão própria, aberta até o fim da rodada: o lock é da sessão (REQ-12, R-08).
            await using var conexao = new SqlConnection(sqlServer.Value.StringConexao);
            await conexao.OpenAsync(cancellationToken);
            if (!await AdquirirLockAsync(conexao, cancellationToken))
            {
                return;
            }

            try
            {
                using var rodada = TelemetriaOms.Fonte.StartActivity(TelemetriaOms.Spans.ProcessarNotificacoes, ActivityKind.Internal, default(ActivityContext));
                rodada?.SetTag(TelemetriaOms.Atributos.Rotina, TelemetriaOms.Rotinas.Notificacoes);
                await DistribuirAsync(cancellationToken);
                await EnviarAsync(cancellationToken);
                await AmostrarAsync(cancellationToken);
            }
            finally
            {
                await LiberarLockAsync(conexao);
            }
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora ou erro inesperado: tenta na próxima rodada, sem derrubar o processo.
            EventosNotificacao.FalhaLeitura(logger, "webhooks", $"{excecao.GetType().Name}: {excecao.Message}");
        }
    }

    /// <summary>Avisos depois do cursor viram entregas para os destinos ativos; o cursor avança junto (R-05 revista).</summary>
    private async Task DistribuirAsync(CancellationToken cancellationToken)
    {
        var lote = opcoes.Value.Envio.TamanhoLote;
        int avisos = 0, entregas = 0;
        for (var i = 0; i < LotesDistribuicao; i++)
        {
            await using var escopo = escopos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
            var cursor = await contexto.CursorNotificacoes.SingleAsync(c => c.Id == CursorNotificacoes.IdUnico, cancellationToken);
            var lidos = await escopo.ServiceProvider.GetRequiredService<LeituraAvisos>()
                .LerAsync(LeituraAvisos.Numero(cursor.Posicao), lote, cancellationToken);
            if (lidos.Count == 0)
            {
                break;
            }

            var destinos = await contexto.DestinosNotificacao.AsNoTracking()
                .Where(d => d.Ativo)
                .Select(d => new
                {
                    d.Id,
                    d.ClienteId,
                    d.CriadoEm,
                    DonoDesativadoEm = contexto.Users.Where(u => u.Id == d.ClienteId).Select(u => u.DesativadoEm).FirstOrDefault(),
                    DonoReativadoEm = contexto.Users.Where(u => u.Id == d.ClienteId).Select(u => u.ReativadoEm).FirstOrDefault()
                })
                .ToListAsync(cancellationToken);
            var agora = relogio.GetUtcNow();
            foreach (var aviso in lidos)
            {
                // Globais e os do Cliente dono, cadastrados até o aviso (o destino novo não recebe o passado).
                foreach (var destino in destinos.Where(d => (d.ClienteId == null || d.ClienteId == aviso.ClienteId) && d.CriadoEm <= aviso.CriadoEm
                    && !ContaImpedeEntrega(d.DonoDesativadoEm, d.DonoReativadoEm, aviso.CriadoEm)))
                {
                    contexto.EntregasNotificacao.Add(EntregaNotificacao.Criar(destino.Id, aviso.Id, aviso.ContextoRastreio, agora));
                    entregas++;
                }
            }

            cursor.Posicao = LeituraAvisos.Binario(lidos[^1].Posicao);
            await contexto.SaveChangesAsync(cancellationToken);
            await transacao.CommitAsync(cancellationToken);
            avisos += lidos.Count;
            if (lidos.Count < lote)
            {
                break;
            }
        }

        if (avisos > 0)
        {
            EventosNotificacao.EntregasCriadas(logger, avisos, entregas);
        }
    }

    private async Task EnviarAsync(CancellationToken cancellationToken)
    {
        var configuracao = opcoes.Value.Envio;
        IReadOnlyList<long> vencidas;
        await using (var escopo = escopos.CreateAsyncScope())
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            var agora = relogio.GetUtcNow();
            // No máximo uma por destino: um receptor lento não segura os outros (FR-012).
            vencidas = await contexto.Database.SqlQuery<long>($"""
                SELECT TOP ({configuracao.TamanhoLote}) Id AS Value FROM (
                    SELECT Id, ProximaTentativaEm,
                        ROW_NUMBER() OVER (PARTITION BY DestinoId ORDER BY ProximaTentativaEm, Id) AS Ordem
                    FROM EntregasNotificacao
                    WHERE Situacao = N'Pendente' AND DestinoId IS NOT NULL AND ProximaTentativaEm <= {agora}) AS vencidas
                WHERE Ordem = 1
                ORDER BY ProximaTentativaEm
                """).ToListAsync(cancellationToken);
        }

        if (vencidas.Count == 0)
        {
            return;
        }

        int entregues = 0, falhas = 0, esgotadas = 0;
        await Parallel.ForEachAsync(vencidas, new ParallelOptions { MaxDegreeOfParallelism = configuracao.Paralelismo, CancellationToken = cancellationToken },
            async (id, ct) =>
            {
                switch (await EnviarAsync(id, ct))
                {
                    case SituacaoEntrega.Entregue:
                        Interlocked.Increment(ref entregues);
                        break;
                    case SituacaoEntrega.Esgotada:
                        Interlocked.Increment(ref esgotadas);
                        break;
                    case SituacaoEntrega.Pendente:
                        Interlocked.Increment(ref falhas);
                        break;
                }
            });

        EventosNotificacao.ResumoEnvio(logger, LogLevel.Information, entregues, falhas, esgotadas);
    }

    /// <summary>
    /// Destino de uma conta desativada não recebe, e o aviso anterior à reativação não sai depois dela
    /// (REQ-25, R-06). Destinos globais não têm dono: as duas datas são nulas e nada muda.
    /// </summary>
    internal static bool ContaImpedeEntrega(DateTimeOffset? donoDesativadoEm, DateTimeOffset? donoReativadoEm, DateTimeOffset avisoCriadoEm) =>
        donoDesativadoEm is not null || avisoCriadoEm < donoReativadoEm;

    /// <summary>Uma entrega num escopo próprio: a falha de uma não interrompe as demais.</summary>
    private async Task<SituacaoEntrega?> EnviarAsync(long entregaId, CancellationToken cancellationToken)
    {
        try
        {
            await using var escopo = escopos.CreateAsyncScope();
            var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
            var entrega = await contexto.EntregasNotificacao.SingleAsync(e => e.Id == entregaId, cancellationToken);
            if (entrega.Situacao != SituacaoEntrega.Pendente || entrega.DestinoId is not { } destinoId)
            {
                return null;
            }

            var destino = await contexto.DestinosNotificacao.SingleAsync(d => d.Id == destinoId, cancellationToken);
            var aviso = await contexto.AvisosPedido.AsNoTracking()
                .Where(a => a.Id == entrega.AvisoId)
                .Select(a => new { a.IdNotificacao, a.PedidoId, a.Conteudo, a.CriadoEm })
                .SingleAsync(cancellationToken);
            var host = EventosNotificacao.HostDestino(destino.Url);

            using var span = TelemetriaOms.Fonte.StartActivity(
                TelemetriaOms.Spans.EnviarNotificacao, ActivityKind.Client, default(ActivityContext),
                links: TelemetriaOms.LigacaoPara(entrega.ContextoRastreio));
            span?.SetTag(TelemetriaOms.Atributos.PedidoId, aviso.PedidoId.ToString());

            var dono = destino.ClienteId is { } clienteId
                ? await contexto.Users.AsNoTracking().Where(u => u.Id == clienteId)
                    .Select(u => new { u.DesativadoEm, u.ReativadoEm }).SingleOrDefaultAsync(cancellationToken)
                : null;
            if (!destino.Ativo || ContaImpedeEntrega(dono?.DesativadoEm, dono?.ReativadoEm, aviso.CriadoEm))
            {
                entrega.Descartar(relogio.GetUtcNow());
                await contexto.SaveChangesAsync(cancellationToken);
                return SituacaoEntrega.Descartada;
            }

            var resultado = await EnviarComSegredoAsync(escopo.ServiceProvider, destino, aviso.IdNotificacao, aviso.Conteudo, cancellationToken);
            var agora = relogio.GetUtcNow();
            SituacaoEntrega situacao;
            if (resultado.Sucesso)
            {
                entrega.MarcarEntregue(resultado.CodigoHttp!.Value, agora);
                destino.RegistrarSucesso(agora);
                EventosNotificacao.Entregue(logger, aviso.IdNotificacao, destino.Id, host, resultado.CodigoHttp.Value, entrega.Tentativas);
                TelemetriaOms.LatenciaNotificacao.Record((agora - aviso.CriadoEm).TotalSeconds);
                situacao = SituacaoEntrega.Entregue;
            }
            else
            {
                var erro = resultado.Erro ?? $"HTTP {resultado.CodigoHttp}";
                var proxima = resultado.CodigoHttp == (int)System.Net.HttpStatusCode.Gone
                    ? null
                    : EsperaEntregas.Proxima(entrega.PrimeiraTentativaEm ?? agora, entrega.Tentativas + 1, agora, opcoes.Value.Envio.Limite);
                destino.RegistrarFalha(agora);
                if (proxima is { } quando)
                {
                    entrega.RegistrarFalha(resultado.CodigoHttp, erro, quando, agora);
                    EventosNotificacao.Falha(logger, aviso.IdNotificacao, destino.Id, host, entrega.Tentativas, erro, quando);
                    situacao = SituacaoEntrega.Pendente;
                }
                else
                {
                    entrega.Esgotar(resultado.CodigoHttp, erro, agora);
                    EventosNotificacao.Esgotada(logger, aviso.IdNotificacao, destino.Id, host, entrega.Tentativas, erro);
                    var motivo = resultado.CodigoHttp == (int)System.Net.HttpStatusCode.Gone
                        ? DestinoNotificacao.MotivoGone
                        : $"Entrega {aviso.IdNotificacao} esgotada";
                    if (destino.Desativar(motivo, agora))
                    {
                        await contexto.SaveChangesAsync(cancellationToken);
                        var descartadas = await escopo.ServiceProvider.GetRequiredService<IArmazenamentoNotificacoes>()
                            .DescartarPendentesAsync(destino.Id, agora, cancellationToken);
                        EventosNotificacao.DestinoDesativado(logger, destino.Id, host, motivo, descartadas);
                    }

                    situacao = SituacaoEntrega.Esgotada;
                }

                span.MarcarErro(erro);
            }

            await contexto.SaveChangesAsync(cancellationToken);
            var valor = situacao switch
            {
                SituacaoEntrega.Entregue => TelemetriaOms.Resultados.Entregue,
                SituacaoEntrega.Esgotada => TelemetriaOms.Resultados.Esgotada,
                _ => TelemetriaOms.Resultados.Falha
            };
            span?.SetTag(TelemetriaOms.Atributos.Resultado, valor);
            TelemetriaOms.EntregasNotificacao.Add(1, new KeyValuePair<string, object?>(TelemetriaOms.Atributos.Resultado, valor));
            return situacao;
        }
        catch (Exception excecao) when (excecao is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Banco fora no meio, conflito ou erro inesperado: a próxima rodada trata a entrega de novo.
            EventosNotificacao.FalhaLeitura(logger, $"entrega {entregaId}", $"{excecao.GetType().Name}: {excecao.Message}");
            return null;
        }
    }

    /// <summary>Com a chave trocada, o segredo não decifra: falha sem enviar nada (analyze U2).</summary>
    private static async Task<ResultadoEnvio> EnviarComSegredoAsync(
        IServiceProvider servicos, DestinoNotificacao destino, Guid idNotificacao, string corpo, CancellationToken cancellationToken)
    {
        byte[] segredo;
        try
        {
            segredo = servicos.GetRequiredService<ICifraSegredos>().Decifrar(destino.SegredoCifrado);
        }
        catch (CryptographicException)
        {
            return new ResultadoEnvio(null, EnviadorWebhook.ErroSegredoIlegivel);
        }

        return await servicos.GetRequiredService<EnviadorWebhook>().EnviarAsync(destino.Url, segredo, idNotificacao, corpo, cancellationToken);
    }

    /// <summary>Entregas pendentes e destinos desativados para os gauges, só com a telemetria ligada (R-19).</summary>
    private async Task AmostrarAsync(CancellationToken cancellationToken)
    {
        if (!TelemetriaOms.Fonte.HasListeners())
        {
            return;
        }

        await using var escopo = escopos.CreateAsyncScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<ContextoBancoDados>();
        var pendentes = await contexto.EntregasNotificacao.CountAsync(e => e.Situacao == SituacaoEntrega.Pendente, cancellationToken);
        var desativados = await contexto.DestinosNotificacao.CountAsync(d => !d.Ativo, cancellationToken);
        EstadoMetricas.RegistrarNotificacoes(pendentes, desativados);
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
