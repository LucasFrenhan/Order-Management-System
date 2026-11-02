namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>O que a API faz com uma chamada (REQ-19, FR-004, research R-03).</summary>
public enum Comportamento
{
    Aprovar = 1,
    Recusar,

    /// <summary><c>503</c>, sem processar.</summary>
    Indisponivel,

    /// <summary><c>429</c> com <c>Retry-After</c>, sem processar.</summary>
    LimiteTaxa,

    /// <summary><c>500</c>, sem processar.</summary>
    ErroInterno,

    /// <summary>Segura a resposta por <c>SemRespostaMs</c>, sem processar; depois, <c>504</c>.</summary>
    SemResposta,

    /// <summary>Efetiva e guarda, mas só responde depois de <c>SemRespostaMs</c>.</summary>
    ProcessarSemResponder
}

/// <summary>Os nomes do contrato (kebab-case), usados na rota de controle e nos logs.</summary>
public static class NomesComportamento
{
    private static readonly Dictionary<Comportamento, string> Nomes = new()
    {
        [Comportamento.Aprovar] = "aprovar",
        [Comportamento.Recusar] = "recusar",
        [Comportamento.Indisponivel] = "indisponivel",
        [Comportamento.LimiteTaxa] = "limite-taxa",
        [Comportamento.ErroInterno] = "erro-interno",
        [Comportamento.SemResposta] = "sem-resposta",
        [Comportamento.ProcessarSemResponder] = "processar-sem-responder"
    };

    private static readonly Dictionary<string, Comportamento> PorNome =
        Nomes.ToDictionary(par => par.Value, par => par.Key, StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyCollection<string> Todos => Nomes.Values;

    public static string Nome(this Comportamento comportamento) => Nomes[comportamento];

    public static bool TentarLer(string? nome, out Comportamento comportamento) =>
        PorNome.TryGetValue(nome ?? "", out comportamento);

    /// <summary>Falha técnica: a operação não é efetivada (FR-004).</summary>
    public static bool EhFalha(this Comportamento comportamento) =>
        comportamento is Comportamento.Indisponivel or Comportamento.LimiteTaxa or Comportamento.ErroInterno or Comportamento.SemResposta;
}
