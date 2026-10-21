using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;

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
