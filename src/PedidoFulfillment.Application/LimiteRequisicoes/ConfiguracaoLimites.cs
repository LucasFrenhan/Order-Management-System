namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// Configuração pronta do limite de requisições, montada pela Infrastructure a partir das opções
/// validadas na partida: a Application não depende de <c>Options</c> (plano do REQ-23).
/// </summary>
public sealed record ConfiguracaoLimites(
    PoliticaLimite LoginOrigemEmail,
    PoliticaLimite LoginOrigem,
    PoliticaLimite Registro,
    byte[] ChaveContagem,
    TimeSpan TempoMaximoEspera,
    TimeSpan PausaAposFalha)
{
    /// <summary>Políticas das rotas do ciclo de autenticação (REQ-24, FR-024).</summary>
    public PoliticasCicloAutenticacao CicloAutenticacao { get; init; } = PoliticasCicloAutenticacao.Padrao;
}

/// <summary>As 8 políticas das rotas do ciclo de autenticação, com os padrões da spec (REQ-24, FR-024).</summary>
public sealed record PoliticasCicloAutenticacao(
    PoliticaLimite RecuperacaoOrigemEmail,
    PoliticaLimite RecuperacaoOrigem,
    PoliticaLimite ReenvioOrigemEmail,
    PoliticaLimite ReenvioOrigem,
    PoliticaLimite Redefinicao,
    PoliticaLimite Confirmacao,
    PoliticaLimite Renovacao,
    PoliticaLimite Logout)
{
    private const int Hora = 3600;
    private const int Minuto = 60;

    public static PoliticasCicloAutenticacao Padrao { get; } = new(
        new PoliticaLimite(PoliticaLimite.RecuperacaoOrigemEmail, 3, Hora),
        new PoliticaLimite(PoliticaLimite.RecuperacaoOrigem, 20, Hora),
        new PoliticaLimite(PoliticaLimite.ReenvioOrigemEmail, 3, Hora),
        new PoliticaLimite(PoliticaLimite.ReenvioOrigem, 20, Hora),
        new PoliticaLimite(PoliticaLimite.Redefinicao, 10, Minuto),
        new PoliticaLimite(PoliticaLimite.Confirmacao, 10, Minuto),
        new PoliticaLimite(PoliticaLimite.Renovacao, 120, Minuto),
        new PoliticaLimite(PoliticaLimite.Logout, 120, Minuto));
}
