namespace PedidoFulfillment.Application.Comum;

/// <summary>
/// Um registro foi alterado por outra operação desde que foi lido (concorrência otimista,
/// FR-003 e FR-021). Nada da gravação foi salvo; quem a fez relê e decide se tenta de novo
/// (<c>specs/004-dominio-persistencia/contracts/persistencia.md</c>).
/// </summary>
public sealed class ConflitoConcorrenciaException(string entidade, string identificador, Exception? excecaoInterna = null)
    : Exception($"O registro {entidade} {identificador} foi alterado por outra operação desde que foi lido.", excecaoInterna)
{
    /// <summary>Nome do tipo da entidade, por exemplo <c>Produto</c>.</summary>
    public string Entidade { get; } = entidade;

    /// <summary>Chave primária em texto; partes de uma chave composta separadas por <c>/</c>.</summary>
    public string Identificador { get; } = identificador;
}
