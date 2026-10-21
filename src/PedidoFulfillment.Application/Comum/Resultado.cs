namespace PedidoFulfillment.Application.Comum;

/// <summary>
/// Categoria de falha de uma operação de aplicação. A camada de API traduz cada categoria
/// para o status HTTP correspondente.
/// </summary>
public enum TipoFalha
{
    Validacao,
    NaoAutenticado,
    SemPermissao,
    NaoEncontrado,
    Conflito,
    RegraNegocio,
    NaoImplementado
}

/// <summary>
/// Descrição de uma falha. <see cref="Codigo"/> é obrigatório para conflitos e regras de
/// negócio (catálogo em <c>CodigosErro</c>).
/// </summary>
public sealed record Falha(TipoFalha Tipo, string Titulo, string? Detalhe = null, string? Codigo = null);

/// <summary>
/// Resultado de uma operação de aplicação: um valor em caso de sucesso ou uma <see cref="Falha"/>.
/// </summary>
public sealed class Resultado<T>
{
    private Resultado(T? valor, Falha? falha)
    {
        Valor = valor;
        Falha = falha;
    }

    public T? Valor { get; }

    public Falha? Falha { get; }

    public bool Sucesso => Falha is null;

    public static Resultado<T> Ok(T valor) => new(valor, null);

    public static Resultado<T> Falhou(Falha falha) => new(default, falha);

    public static implicit operator Resultado<T>(T valor) => Ok(valor);

    public static implicit operator Resultado<T>(Falha falha) => Falhou(falha);
}
