using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Fake de <see cref="IServicoAutenticacao"/> que devolve o resultado configurado e guarda a
/// última chamada, para os testes de caminho feliz das rotas.
/// </summary>
public sealed class ServicoAutenticacaoFalso : IServicoAutenticacao
{
    public Resultado<UsuarioResponseDTO>? ResultadoRegistro { get; set; }

    public Resultado<LoginResponseDTO>? ResultadoLogin { get; set; }

    public RegistrarUsuarioRequestDTO? UltimoRegistro { get; private set; }

    public LoginRequestDTO? UltimoLogin { get; private set; }

    public Task<Resultado<UsuarioResponseDTO>> RegistrarAsync(RegistrarUsuarioRequestDTO usuario, CancellationToken cancellationToken)
    {
        UltimoRegistro = usuario;
        return Task.FromResult(ResultadoRegistro ?? throw new InvalidOperationException("Resultado de registro não configurado."));
    }

    public Task<Resultado<LoginResponseDTO>> AutenticarAsync(LoginRequestDTO credenciais, CancellationToken cancellationToken)
    {
        UltimoLogin = credenciais;
        return Task.FromResult(ResultadoLogin ?? throw new InvalidOperationException("Resultado de login não configurado."));
    }
}

/// <summary>
/// Fake de <see cref="IServicoPedidos"/> que devolve o resultado configurado e guarda a última
/// chamada, para verificar o que a rota repassa ao serviço.
/// </summary>
public sealed class ServicoPedidosFalso : IServicoPedidos
{
    public Resultado<PedidoResponseDTO>? ResultadoCriar { get; set; }

    public Guid? UltimoClienteId { get; private set; }

    public Guid? UltimaChaveIdempotencia { get; private set; }

    public CriarPedidoRequestDTO? UltimoPedidoCriado { get; private set; }

    public Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken)
    {
        (UltimoClienteId, UltimaChaveIdempotencia, UltimoPedidoCriado) = (clienteId, chaveIdempotencia, pedido);
        return Task.FromResult(ResultadoCriar ?? throw new InvalidOperationException("Resultado de criação não configurado."));
    }

    public Resultado<PedidoResponseDTO>? ResultadoObter { get; set; }

    public Guid? UltimoPedidoId { get; private set; }

    public Guid? UltimoUsuarioId { get; private set; }

    public IReadOnlyCollection<PerfilUsuario>? UltimosPerfis { get; private set; }

    public Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        (UltimoPedidoId, UltimoUsuarioId, UltimosPerfis) = (pedidoId, usuarioId, perfis);
        return Task.FromResult(ResultadoObter ?? throw new InvalidOperationException("Resultado de consulta não configurado."));
    }

    public Resultado<PedidoResponseDTO>? ResultadoAtualizarStatus { get; set; }

    public AtualizarStatusPedidoRequestDTO? UltimaAtualizacao { get; private set; }

    public Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken)
    {
        (UltimoPedidoId, UltimoUsuarioId, UltimosPerfis, UltimaAtualizacao) = (pedidoId, usuarioId, perfis, atualizacao);
        return Task.FromResult(ResultadoAtualizarStatus ?? throw new InvalidOperationException("Resultado de atualização não configurado."));
    }
}
