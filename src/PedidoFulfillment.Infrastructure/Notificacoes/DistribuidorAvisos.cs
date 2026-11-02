using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Notificacoes;

/// <summary>Uma conexão SSE aberta (REQ-18, FR-019 a FR-023): quem é, o filtro e a fila dos avisos ao vivo.</summary>
internal sealed class ConexaoEventos
{
    public const int CapacidadeFila = 100;

    public ConexaoEventos(Guid usuarioId, bool operacao, Guid? pedidoId)
    {
        UsuarioId = usuarioId;
        Operacao = operacao;
        PedidoId = pedidoId;
    }

    public Guid UsuarioId { get; }

    /// <summary>Admin ou OperadorLogistica: vê os avisos de todos os pedidos, como na consulta.</summary>
    public bool Operacao { get; }

    public Guid? PedidoId { get; }

    public Channel<AvisoLido> Fila { get; } = Channel.CreateBounded<AvisoLido>(
        new BoundedChannelOptions(CapacidadeFila) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    /// <summary>A fila encheu: a aplicação não acompanha, e a conexão será encerrada.</summary>
    public bool Lenta { get; private set; }

    public bool Aceita(AvisoLido aviso) =>
        (Operacao || aviso.ClienteId == UsuarioId) && (PedidoId is null || aviso.PedidoId == PedidoId);

    public void Entregar(AvisoLido aviso)
    {
        if (!Lenta && !Fila.Writer.TryWrite(aviso))
        {
            Lenta = true;
            Fila.Writer.TryComplete();
        }
    }
}

/// <summary>
/// As conexões SSE desta instância da API (REQ-18, research R-16): o <see cref="LeitorAvisos"/>
/// publica aqui o que leu do banco, e cada conexão recebe o que o filtro dela aceita. Também
/// aplica o limite de conexões por usuário (FR-023).
/// </summary>
internal sealed class DistribuidorAvisos(IOptions<OpcoesNotificacoes> opcoes)
{
    private readonly ConcurrentDictionary<ConexaoEventos, byte> conexoes = new();
    private readonly object trava = new();

    public int Limite => opcoes.Value.Sse.ConexoesPorUsuario;

    /// <summary>Registra a conexão, ou devolve nulo se o usuário já está no limite.</summary>
    public ConexaoEventos? Registrar(Guid usuarioId, bool operacao, Guid? pedidoId)
    {
        lock (trava)
        {
            if (conexoes.Keys.Count(c => c.UsuarioId == usuarioId) >= Limite)
            {
                return null;
            }

            var conexao = new ConexaoEventos(usuarioId, operacao, pedidoId);
            conexoes[conexao] = 0;
            EstadoMetricas.SomarConexoesSse(1);
            return conexao;
        }
    }

    public void Remover(ConexaoEventos conexao)
    {
        if (conexoes.TryRemove(conexao, out _))
        {
            conexao.Fila.Writer.TryComplete();
            EstadoMetricas.SomarConexoesSse(-1);
        }
    }

    public void Publicar(IReadOnlyList<AvisoLido> avisos)
    {
        foreach (var conexao in conexoes.Keys)
        {
            foreach (var aviso in avisos.Where(conexao.Aceita))
            {
                conexao.Entregar(aviso);
            }
        }
    }
}
