using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Persistência dos casos de uso de pedidos (research R-02 do REQ-05). Cada
/// <see cref="SalvarAsync"/> grava tudo o que foi alterado de uma vez, ou nada.
/// </summary>
public interface IArmazenamentoPedidos
{
    /// <summary>Produtos encontrados entre <paramref name="ids"/>, só para leitura.</summary>
    Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    /// <summary>Pedido com os itens. Com <paramref name="paraAlterar"/>, as alterações nele são gravadas no próximo <see cref="SalvarAsync"/>.</summary>
    Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken);

    Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken);

    void Adicionar(Pedido pedido, RegistroIdempotencia registro);

    /// <exception cref="ChaveIdempotenciaRegistradaException">O par (cliente, chave) já foi registrado por outra requisição.</exception>
    /// <exception cref="ConflitoConcorrenciaException">O pedido mudou desde que foi lido.</exception>
    Task SalvarAsync(CancellationToken cancellationToken);

    /// <summary>Esquece as alterações pendentes, para reler depois de um conflito.</summary>
    void DescartarAlteracoes();
}
