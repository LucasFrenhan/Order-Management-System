using Microsoft.Extensions.Logging;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>
/// Logs do catálogo (REQ-22, research R-11), faixa 12001–12006. O log do ajuste é o histórico de
/// estoque (clarify): leva a variação, o motivo, a quantidade nova e quem ajustou.
/// </summary>
internal static partial class EventosCatalogo
{
    [LoggerMessage(EventId = 12001, EventName = "ProdutoCadastrado", Level = LogLevel.Information,
        Message = "Produto {ProdutoId} ({Sku}) cadastrado pelo usuário {UsuarioId} com {QuantidadeEstoque} unidade(s).")]
    public static partial void ProdutoCadastrado(ILogger logger, Guid produtoId, string sku, Guid usuarioId, int quantidadeEstoque);

    [LoggerMessage(EventId = 12002, EventName = "ProdutoAlterado", Level = LogLevel.Information,
        Message = "Produto {ProdutoId} alterado pelo usuário {UsuarioId}; preço de {PrecoAnterior} para {PrecoNovo}.")]
    public static partial void ProdutoAlterado(ILogger logger, Guid produtoId, Guid usuarioId, decimal precoAnterior, decimal precoNovo);

    [LoggerMessage(EventId = 12003, EventName = "EstoqueAjustado", Level = LogLevel.Information,
        Message = "Estoque do produto {ProdutoId} ajustado em {Variacao} pelo usuário {UsuarioId} ({Motivo}); agora {QuantidadeEstoque}.")]
    public static partial void EstoqueAjustado(ILogger logger, Guid produtoId, int variacao, Guid usuarioId, string motivo, int quantidadeEstoque);

    [LoggerMessage(EventId = 12004, EventName = "ProdutoDesativado", Level = LogLevel.Information,
        Message = "Produto {ProdutoId} desativado pelo usuário {UsuarioId}.")]
    public static partial void ProdutoDesativado(ILogger logger, Guid produtoId, Guid usuarioId);

    [LoggerMessage(EventId = 12005, EventName = "ProdutoReativado", Level = LogLevel.Information,
        Message = "Produto {ProdutoId} reativado pelo usuário {UsuarioId}.")]
    public static partial void ProdutoReativado(ILogger logger, Guid produtoId, Guid usuarioId);

    [LoggerMessage(EventId = 12006, EventName = "ConflitoNoProduto", Level = LogLevel.Debug,
        Message = "Produto {ProdutoId} alterado por outra operação; reavaliando (tentativa {Tentativa}).")]
    public static partial void ConflitoNoProduto(ILogger logger, Guid produtoId, int tentativa);
}
