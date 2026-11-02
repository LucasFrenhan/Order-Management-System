using System.Net;
using System.Net.ServerSentEvents;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Cliente de teste da conexão de eventos (REQ-18, US3): abre o stream sem esperar o fim da
/// resposta e lê os eventos com o parser de SSE do .NET.
/// </summary>
public sealed class LeitorSseTeste : IAsyncDisposable
{
    public const string Rota = "/api/v1/notificacoes/eventos";

    private readonly HttpResponseMessage resposta;
    private readonly IAsyncEnumerator<SseItem<string>>? itens;
    private readonly CancellationTokenSource fim = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private Task<bool>? pendente;

    private LeitorSseTeste(HttpResponseMessage resposta, Stream? corpo)
    {
        this.resposta = resposta;
        itens = corpo is null ? null : SseParser.Create(corpo).EnumerateAsync(fim.Token).GetAsyncEnumerator(fim.Token);
    }

    public HttpStatusCode Status => resposta.StatusCode;

    public HttpResponseMessage Resposta => resposta;

    public static async Task<LeitorSseTeste> AbrirAsync(HttpClient cliente, Guid? pedidoId = null, string? ultimoEvento = null)
    {
        var requisicao = new HttpRequestMessage(HttpMethod.Get, pedidoId is null ? Rota : $"{Rota}?pedidoId={pedidoId}");
        if (ultimoEvento is not null)
        {
            requisicao.Headers.Add("Last-Event-ID", ultimoEvento);
        }

        var resposta = await cliente.SendAsync(requisicao, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        var corpo = resposta.IsSuccessStatusCode ? await resposta.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken) : null;
        return new LeitorSseTeste(resposta, corpo);
    }

    /// <summary>O próximo evento, ou nulo se nada chegou no prazo ou o stream acabou.</summary>
    public async Task<SseItem<string>?> ProximoAsync(TimeSpan prazo)
    {
        if (itens is null)
        {
            return null;
        }

        pendente ??= itens.MoveNextAsync().AsTask();
        var terminou = await Task.WhenAny(pendente, Task.Delay(prazo, TestContext.Current.CancellationToken));
        if (terminou != pendente)
        {
            return null;
        }

        var tem = await pendente;
        pendente = null;
        return tem ? itens.Current : null;
    }

    /// <summary>O próximo evento que não é <c>sinal</c>.</summary>
    public async Task<SseItem<string>?> ProximoAvisoAsync(TimeSpan prazo)
    {
        var limite = DateTime.UtcNow + prazo;
        while (DateTime.UtcNow < limite)
        {
            var item = await ProximoAsync(limite - DateTime.UtcNow);
            if (item is null || item.Value.EventType != "sinal")
            {
                return item;
            }
        }

        return null;
    }

    /// <summary>Verdadeiro se o servidor encerrou o stream dentro do prazo.</summary>
    public async Task<bool> EncerradoAsync(TimeSpan prazo)
    {
        var limite = DateTime.UtcNow + prazo;
        while (DateTime.UtcNow < limite)
        {
            if (itens is null)
            {
                return true;
            }

            pendente ??= itens.MoveNextAsync().AsTask();
            var terminou = await Task.WhenAny(pendente, Task.Delay(limite - DateTime.UtcNow, TestContext.Current.CancellationToken));
            if (terminou != pendente)
            {
                return false;
            }

            var tem = await pendente;
            pendente = null;
            if (!tem)
            {
                return true;
            }
        }

        return false;
    }

    public async ValueTask DisposeAsync()
    {
        await fim.CancelAsync();
        if (pendente is not null)
        {
            try
            {
                await pendente;
            }
            catch (Exception)
            {
                // Leitura cancelada no descarte.
            }
        }

        if (itens is not null)
        {
            try
            {
                await itens.DisposeAsync();
            }
            catch (Exception)
            {
                // Stream já encerrado.
            }
        }

        resposta.Dispose();
        fim.Dispose();
    }
}
