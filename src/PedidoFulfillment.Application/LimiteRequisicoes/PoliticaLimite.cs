namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>As rotas públicas com limite de requisições (REQ-23, FR-005; REQ-24, FR-024).</summary>
public enum RotaLimitada
{
    Login,
    Registro,
    Renovacao,
    Logout,
    RecuperacaoSenha,
    ReenvioConfirmacao,
    RedefinicaoSenha,
    ConfirmacaoEmail
}

/// <summary>
/// Uma regra de limite: quantas requisições uma identidade pode fazer em uma janela fixa, iniciada
/// na primeira requisição da contagem (FR-008). O <see cref="Nome"/> é o valor do atributo
/// <c>oms.limite.politica</c> e dos logs.
/// </summary>
public sealed record PoliticaLimite(string Nome, int Limite, int JanelaSegundos)
{
    public const string LoginOrigemEmail = "login_origem_email";
    public const string LoginOrigem = "login_origem";
    public const string Registro = "registro";
    public const string RecuperacaoOrigemEmail = "recuperacao_origem_email";
    public const string RecuperacaoOrigem = "recuperacao_origem";
    public const string ReenvioOrigemEmail = "reenvio_origem_email";
    public const string ReenvioOrigem = "reenvio_origem";
    public const string Redefinicao = "redefinicao";
    public const string Confirmacao = "confirmacao";
    public const string Renovacao = "renovacao";
    public const string Logout = "logout";
}
