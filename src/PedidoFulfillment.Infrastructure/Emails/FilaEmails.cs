using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Emails;

/// <summary>Grava a solicitação no contexto da requisição (REQ-24, R-03), igual com ou sem conta.</summary>
internal sealed class FilaEmails(ContextoBancoDados contexto, TimeProvider relogio) : IFilaEmails
{
    public async Task EnfileirarAsync(TipoEmail tipo, string destino, CancellationToken cancellationToken)
    {
        var agora = relogio.GetUtcNow();
        contexto.SolicitacoesEmail.Add(new SolicitacaoEmail
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Destino = destino.Trim(),
            CriadaEm = agora,
            ProximaTentativaEm = agora
        });
        await contexto.SaveChangesAsync(cancellationToken);
    }
}
