using System.Data;
using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Persistencia;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>
/// Lock da rodada de estornos na transação do contexto do escopo (REQ-28, R-03):
/// <c>sp_getapplock 'oms-estornos'</c> com <c>LockOwner = 'Transaction'</c>, liberado sozinho no
/// commit, no rollback ou numa conexão perdida. A rodada pede o mesmo lock sem esperar e, enquanto a
/// ação o segura, pula (log 7011).
/// </summary>
internal sealed class BloqueioRodadaEstornos(ContextoBancoDados contexto) : IBloqueioRodadaEstornos
{
    public async Task<bool> ExecutarAsync(TimeSpan espera, Func<CancellationToken, Task> acao, CancellationToken cancellationToken)
    {
        await using var transacao = await contexto.Database.BeginTransactionAsync(cancellationToken);
        if (!await AdquirirAsync(transacao, espera, cancellationToken))
        {
            await transacao.RollbackAsync(cancellationToken);
            return false;
        }

        // Uma exceção da ação descarta a transação sem commit: nada fica gravado, e o lock sai junto.
        await acao(cancellationToken);
        await transacao.CommitAsync(cancellationToken);
        return true;
    }

    private async Task<bool> AdquirirAsync(IDbContextTransaction transacao, TimeSpan espera, CancellationToken cancellationToken)
    {
        await using var comando = contexto.Database.GetDbConnection().CreateCommand();
        comando.Transaction = transacao.GetDbTransaction();
        comando.CommandText =
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @recurso, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = @espera; SELECT @r;";
        Parametro(comando, "@recurso", ProcessadorEstornos.RecursoLock);
        Parametro(comando, "@espera", (int)espera.TotalMilliseconds);
        // Com espera, o comando pode levar até ela; o tempo do comando fica acima, para não estourar antes.
        comando.CommandTimeout = Math.Max(comando.CommandTimeout, (int)Math.Ceiling(espera.TotalSeconds) + 5);
        var resultado = Convert.ToInt32(await comando.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
        return resultado >= 0;
    }

    private static void Parametro(IDbCommand comando, string nome, object valor)
    {
        var parametro = comando.CreateParameter();
        parametro.ParameterName = nome;
        parametro.Value = valor;
        comando.Parameters.Add(parametro);
    }
}
