namespace PedidoFulfillment.Application.Contratos.Erros;

/// <summary>
/// Catálogo estável de códigos de erro de negócio (FR-011). Códigos existentes não podem
/// ser renomeados nem reaproveitados (Princípio II).
/// </summary>
public static class CodigosErro
{
    public const string UsuarioEmailJaCadastrado = "USUARIO_EMAIL_JA_CADASTRADO";
    public const string IdempotenciaConteudoDivergente = "IDEMPOTENCIA_CONTEUDO_DIVERGENTE";
    public const string IdempotenciaEmProcessamento = "IDEMPOTENCIA_EM_PROCESSAMENTO";
    public const string ProdutoInexistente = "PRODUTO_INEXISTENTE";
    public const string ProdutoIndisponivel = "PRODUTO_INDISPONIVEL";
    public const string PedidoTransicaoInvalida = "PEDIDO_TRANSICAO_INVALIDA";
}
