using System.Collections.Concurrent;

namespace PedidoFulfillment.PagamentosSimulados.Operacoes;

/// <summary>Se a chamada criou a operação ou encontrou uma guardada com a mesma chave.</summary>
public sealed record RegistroOperacao(OperacaoPagamento Operacao, bool Nova);

/// <summary>
/// Memória idempotente da API simulada (REQ-19, FR-005, research R-04): no máximo uma operação por
/// tipo e chave, criada de forma atômica. Sem banco: reiniciar a API apaga tudo (clarify).
/// </summary>
public sealed class ArmazenamentoOperacoes
{
    private readonly ConcurrentDictionary<(TipoOperacao, Guid), OperacaoPagamento> operacoes = new();

    /// <summary>
    /// Cria a operação em andamento, ou devolve a guardada somando a chamada. Duas chamadas
    /// simultâneas com a mesma chave criam uma única operação.
    /// </summary>
    public RegistroOperacao Registrar(TipoOperacao tipo, Guid chave, decimal valor, string? identificadorCobranca = null)
    {
        var nova = new OperacaoPagamento(tipo, chave, valor, identificadorCobranca);
        if (operacoes.TryAdd((tipo, chave), nova))
        {
            return new RegistroOperacao(nova, Nova: true);
        }

        var existente = operacoes[(tipo, chave)];
        existente.ContarChamada();
        return new RegistroOperacao(existente, Nova: false);
    }

    /// <summary>Uma falha simulada sobre uma chave guardada só conta a chamada (research R-03).</summary>
    public void ContarChamada(TipoOperacao tipo, Guid chave)
    {
        if (operacoes.TryGetValue((tipo, chave), out var existente))
        {
            existente.ContarChamada();
        }
    }

    /// <summary>
    /// Libera a chave de uma operação que não foi efetivada. Uma operação concluída nunca é
    /// removida (data-model › transições).
    /// </summary>
    public bool Liberar(OperacaoPagamento operacao) =>
        !operacao.Concluida && operacoes.TryRemove(new KeyValuePair<(TipoOperacao, Guid), OperacaoPagamento>((operacao.Tipo, operacao.Chave), operacao));

    public OperacaoPagamento? Obter(TipoOperacao tipo, Guid chave) =>
        operacoes.TryGetValue((tipo, chave), out var operacao) ? operacao : null;

    /// <summary>A cobrança aprovada com o identificador informado no estorno.</summary>
    public OperacaoPagamento? CobrancaAprovada(string identificador) =>
        operacoes.Values.FirstOrDefault(o =>
            o.Tipo == TipoOperacao.Cobranca && o.Situacao == SituacaoOperacao.Aprovada && string.Equals(o.Identificador, identificador, StringComparison.Ordinal));

    public void Limpar() => operacoes.Clear();
}
