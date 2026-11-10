namespace PedidoFulfillment.Application.Contratos.Erros;

/// <summary>
/// Catálogo estável de códigos de erro de negócio (FR-011). Códigos existentes não podem
/// ser renomeados nem reaproveitados (Princípio II).
/// </summary>
public static class CodigosErro
{
    public const string UsuarioEmailJaCadastrado = "USUARIO_EMAIL_JA_CADASTRADO";
    public const string UsuarioUltimoAdmin = "USUARIO_ULTIMO_ADMIN";
    public const string UsuarioPropriaConta = "USUARIO_PROPRIA_CONTA";
    public const string UsuarioUltimoPerfil = "USUARIO_ULTIMO_PERFIL";
    public const string UsuarioPerfisIncompativeis = "USUARIO_PERFIS_INCOMPATIVEIS";
    public const string LimiteRequisicoesExcedido = "LIMITE_REQUISICOES_EXCEDIDO";
    public const string TokenInvalido = "TOKEN_INVALIDO";
    public const string IdempotenciaConteudoDivergente = "IDEMPOTENCIA_CONTEUDO_DIVERGENTE";
    public const string IdempotenciaEmProcessamento = "IDEMPOTENCIA_EM_PROCESSAMENTO";
    public const string ProdutoInexistente = "PRODUTO_INEXISTENTE";
    public const string ProdutoIndisponivel = "PRODUTO_INDISPONIVEL";
    public const string ProdutoSkuDuplicado = "PRODUTO_SKU_DUPLICADO";
    public const string ProdutoAlteradoPorOutraOperacao = "PRODUTO_ALTERADO_POR_OUTRA_OPERACAO";
    public const string EstoqueInsuficiente = "ESTOQUE_INSUFICIENTE";
    public const string EstoqueAcimaDoLimite = "ESTOQUE_ACIMA_DO_LIMITE";
    public const string PedidoTransicaoInvalida = "PEDIDO_TRANSICAO_INVALIDA";
    public const string PedidoRastreioDuplicado = "PEDIDO_RASTREIO_DUPLICADO";
    public const string NotificacaoDestinoEnderecoProibido = "NOTIFICACAO_DESTINO_ENDERECO_PROIBIDO";
    public const string NotificacaoDestinoLimite = "NOTIFICACAO_DESTINO_LIMITE";
    public const string NotificacaoSseLimiteConexoes = "NOTIFICACAO_SSE_LIMITE_CONEXOES";
    public const string EstornoNaoPendente = "ESTORNO_NAO_PENDENTE";
    public const string EstornoRodadaEmAndamento = "ESTORNO_RODADA_EM_ANDAMENTO";
}
