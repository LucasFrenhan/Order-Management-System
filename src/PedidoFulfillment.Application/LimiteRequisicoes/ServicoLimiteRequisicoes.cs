using System.Net;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// A regra do limite de requisições (REQ-23): monta as contagens da rota, responde pelas recusas já
/// conhecidas (R-05) e consulta o estado único. Sem o armazenamento, a requisição segue sem o limite,
/// com log e métrica (FR-012), e a instância não tenta de novo durante a pausa (R-04).
/// </summary>
public sealed class ServicoLimiteRequisicoes(
    ConfiguracaoLimites configuracao,
    IArmazenamentoContagens armazenamento,
    IBloqueiosLocais bloqueios,
    IMetricasLimite metricas,
    TimeProvider relogio,
    ILogger<ServicoLimiteRequisicoes> logger) : IServicoLimiteRequisicoes
{
    private const string SemEmail = "-";
    private const int TentativasNaDisputa = 2;

    /// <summary>Fim da pausa, em ticks UTC; por instância, pois este serviço é único no processo.</summary>
    private long pausaAte;

    public async Task<DecisaoLimite> AvaliarAsync(RotaLimitada rota, IPAddress? endereco, string? email, CancellationToken cancellationToken)
    {
        var contagens = Montar(rota, OrigemCliente.Normalizar(endereco), email);

        if (RecusaLocal(contagens) is { } decisaoLocal)
        {
            return decisaoLocal;
        }

        if (EmPausa())
        {
            metricas.RequisicaoSemLimite();
            return DecisaoLimite.Aceitar;
        }

        IReadOnlyList<RecusaContagem> recusas;
        try
        {
            recusas = await ContarAsync(contagens, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (DisputaContagemException excecao)
        {
            // Disputa momentânea que persistiu: segue sem o limite, mas o armazenamento está no ar.
            SeguirSemLimite(excecao, abrirPausa: false);
            return DecisaoLimite.Aceitar;
        }
        catch (Exception excecao)
        {
            // Qualquer falha do armazenamento, inclusive o tempo máximo, não pode derrubar o login (FR-012).
            SeguirSemLimite(excecao, abrirPausa: true);
            return DecisaoLimite.Aceitar;
        }

        return recusas.Count == 0 ? DecisaoLimite.Aceitar : Recusar(contagens, recusas, email);
    }

    private IReadOnlyList<(PoliticaLimite Politica, ContagemSolicitada Contagem)> Montar(RotaLimitada rota, string origem, string? email)
    {
        (PoliticaLimite Politica, ContagemSolicitada Contagem) Contagem(PoliticaLimite politica, string? emailDaContagem) =>
            (politica, new ContagemSolicitada(
                GeradorChaveContagem.Gerar(configuracao.ChaveContagem, politica.Nome, origem, emailDaContagem),
                politica.Limite,
                politica.JanelaSegundos));

        if (rota == RotaLimitada.Registro)
        {
            return [Contagem(configuracao.Registro, null)];
        }

        return email is null
            ? [Contagem(configuracao.LoginOrigem, null)]
            : [Contagem(configuracao.LoginOrigemEmail, email), Contagem(configuracao.LoginOrigem, null)];
    }

    private DecisaoLimite? RecusaLocal(IReadOnlyList<(PoliticaLimite Politica, ContagemSolicitada Contagem)> contagens)
    {
        TimeSpan? maiorEspera = null;
        foreach (var (politica, contagem) in contagens)
        {
            if (bloqueios.TentarObter(contagem.Chave, out var espera))
            {
                metricas.RequisicaoRecusada(politica.Nome);
                maiorEspera = maiorEspera is { } atual && atual > espera ? atual : espera;
            }
        }

        return maiorEspera is { } tempo ? new DecisaoLimite(false, Segundos(tempo)) : null;
    }

    private async Task<IReadOnlyList<RecusaContagem>> ContarAsync(
        IReadOnlyList<(PoliticaLimite Politica, ContagemSolicitada Contagem)> contagens, CancellationToken cancellationToken)
    {
        IReadOnlyList<ContagemSolicitada> solicitadas = [.. contagens.Select(c => c.Contagem)];
        for (var tentativa = 1; ; tentativa++)
        {
            try
            {
                using var limite = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                limite.CancelAfter(configuracao.TempoMaximoEspera);
                // O WaitAsync garante o tempo máximo mesmo se o acesso ao banco demorar a atender ao cancelamento.
                return await armazenamento.ContarAsync(solicitadas, limite.Token).WaitAsync(limite.Token);
            }
            catch (DisputaContagemException) when (tentativa < TentativasNaDisputa)
            {
                // Uma nova tentativa (R-03).
            }
        }
    }

    private DecisaoLimite Recusar(
        IReadOnlyList<(PoliticaLimite Politica, ContagemSolicitada Contagem)> contagens, IReadOnlyList<RecusaContagem> recusas, string? email)
    {
        var maiorEspera = TimeSpan.Zero;
        foreach (var recusa in recusas)
        {
            var (politica, contagem) = contagens[recusa.Indice];
            bloqueios.Registrar(contagem.Chave, recusa.Espera);
            metricas.RequisicaoRecusada(politica.Nome);
            if (recusa.PrimeiroBloqueio)
            {
                var emailMascarado = politica.Nome == PoliticaLimite.LoginOrigemEmail ? MascaraEmail.Mascarar(email?.Trim()) : SemEmail;
                EventosLimiteRequisicoes.LimiteExcedido(logger, politica.Nome, emailMascarado);
            }

            maiorEspera = recusa.Espera > maiorEspera ? recusa.Espera : maiorEspera;
        }

        return new DecisaoLimite(false, Segundos(maiorEspera));
    }

    private bool EmPausa() => relogio.GetUtcNow().UtcTicks < Volatile.Read(ref pausaAte);

    private void SeguirSemLimite(Exception excecao, bool abrirPausa)
    {
        metricas.RequisicaoSemLimite();
        var agora = relogio.GetUtcNow().UtcTicks;
        var jaEmPausa = false;
        if (abrirPausa && configuracao.PausaAposFalha > TimeSpan.Zero)
        {
            // Várias requisições podem falhar juntas: só quem abre a pausa registra o log.
            jaEmPausa = Interlocked.Exchange(ref pausaAte, agora + configuracao.PausaAposFalha.Ticks) > agora;
        }

        if (!jaEmPausa)
        {
            EventosLimiteRequisicoes.FalhaArmazenamento(logger, excecao.GetType().Name);
        }
    }

    /// <summary>Para cima, com o mínimo de 1 s: esperar esse tempo basta para a janela acabar (FR-015).</summary>
    private static int Segundos(TimeSpan espera) => Math.Max(1, (int)Math.Ceiling(espera.TotalSeconds));
}
