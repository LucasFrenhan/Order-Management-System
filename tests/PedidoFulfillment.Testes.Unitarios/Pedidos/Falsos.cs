using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>
/// Armazenamento em memória. Cada leitura de pedido devolve uma instância nova, montada pela
/// fábrica guardada; assim um conflito descartado não deixa alteração para trás.
/// </summary>
public sealed class ArmazenamentoPedidosFalso : IArmazenamentoPedidos
{
    private readonly Dictionary<Guid, Func<Pedido>> pedidos = [];
    private readonly List<(Pedido Pedido, RegistroIdempotencia Registro, EventoSaida Evento)> adicionados = [];
    private Pedido? emAlteracao;

    public List<Produto> Produtos { get; } = [];

    public List<RegistroIdempotencia> Registros { get; } = [];

    /// <summary>Pedidos gravados pelo serviço (pela criação), na ordem.</summary>
    public List<Pedido> PedidosGravados { get; } = [];

    /// <summary>Eventos do outbox gravados junto com os pedidos (REQ-06).</summary>
    public List<EventoSaida> EventosGravados { get; } = [];

    public int Gravacoes { get; private set; }

    public int Descartes { get; private set; }

    /// <summary>Na próxima gravação, simula outra requisição que registrou a mesma chave antes.</summary>
    public bool CorridaNaProximaGravacao { get; set; }

    /// <summary>Registro da requisição vencedora, visível depois da corrida; nulo simula "ainda não visível".</summary>
    public RegistroIdempotencia? VencedorDaCorrida { get; set; }

    /// <summary>Quantas gravações seguidas recebem conflito de concorrência.</summary>
    public int ConflitosRestantes { get; set; }

    /// <summary>O que a "outra operação" faz a cada conflito.</summary>
    public Action? AoConflitar { get; set; }

    /// <summary>Guarda um pedido; a fábrica é chamada a cada leitura.</summary>
    public void GuardarPedido(Guid id, Func<Pedido> fabrica) => pedidos[id] = fabrica;

    public Task<IReadOnlyList<Produto>> ObterProdutosAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<Produto>>([.. Produtos.Where(p => ids.Contains(p.Id))]);

    public Task<Pedido?> ObterPedidoAsync(Guid id, bool paraAlterar, CancellationToken cancellationToken)
    {
        var pedido = pedidos.TryGetValue(id, out var fabrica) ? fabrica() : null;
        if (paraAlterar)
        {
            emAlteracao = pedido;
        }

        return Task.FromResult(pedido);
    }

    public Task<RegistroIdempotencia?> ObterRegistroIdempotenciaAsync(Guid clienteId, Guid chave, CancellationToken cancellationToken) =>
        Task.FromResult(Registros.SingleOrDefault(r => r.ClienteId == clienteId && r.Chave == chave));

    public void Adicionar(Pedido pedido, RegistroIdempotencia registro, EventoSaida evento) => adicionados.Add((pedido, registro, evento));

    public Task SalvarAsync(CancellationToken cancellationToken)
    {
        if (CorridaNaProximaGravacao)
        {
            CorridaNaProximaGravacao = false;
            if (VencedorDaCorrida is not null)
            {
                Registros.Add(VencedorDaCorrida);
            }

            throw new ChaveIdempotenciaRegistradaException();
        }

        if (ConflitosRestantes > 0)
        {
            ConflitosRestantes--;
            AoConflitar?.Invoke();
            throw new ConflitoConcorrenciaException(nameof(Pedido), emAlteracao?.Id.ToString() ?? "");
        }

        Gravacoes++;
        foreach (var (pedido, registro, evento) in adicionados)
        {
            PedidosGravados.Add(pedido);
            Registros.Add(registro);
            EventosGravados.Add(evento);
            pedidos[pedido.Id] = () => pedido;
        }

        if (emAlteracao is { } alterado)
        {
            pedidos[alterado.Id] = () => alterado;
        }

        adicionados.Clear();
        emAlteracao = null;
        return Task.CompletedTask;
    }

    public void DescartarAlteracoes()
    {
        Descartes++;
        adicionados.Clear();
        emAlteracao = null;
    }
}

public sealed class GeradorNumeroPedidoFalso : IGeradorNumeroPedido
{
    private int proximo;

    public Task<string> ProximoAsync(DateTimeOffset instante, CancellationToken cancellationToken) =>
        Task.FromResult($"PED-{instante.UtcDateTime:yyyyMMdd}-{++proximo:D6}");
}

public sealed class RelogioFixo(DateTimeOffset agora) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => agora;
}
