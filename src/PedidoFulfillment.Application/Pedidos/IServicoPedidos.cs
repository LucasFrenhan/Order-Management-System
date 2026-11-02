using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Operações de pedidos, implementadas no REQ-05 (<c>ServicoPedidos</c>).
/// </summary>
public interface IServicoPedidos
{
    /// <summary>
    /// Cria o pedido com status Pendente para o cliente do token. Idempotente por
    /// (cliente, chave) por no mínimo 24 horas: mesma chave e mesmo conteúdo devolvem o pedido
    /// original; conteúdo divergente devolve <see cref="TipoFalha.Conflito"/>
    /// (<c>IDEMPOTENCIA_CONTEUDO_DIVERGENTE</c>); chave em processamento devolve
    /// <see cref="TipoFalha.Conflito"/> (<c>IDEMPOTENCIA_EM_PROCESSAMENTO</c>); produto
    /// inexistente ou indisponível devolve <see cref="TipoFalha.RegraNegocio"/>.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta um pedido. Admin e OperadorLogistica veem qualquer pedido; um Cliente só vê os
    /// próprios. Cliente que não é dono recebe <see cref="TipoFalha.NaoEncontrado"/>, para não
    /// revelar que o pedido existe.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);

    /// <summary>
    /// A consulta com a versão do pedido para o <c>ETag</c> (REQ-18, FR-025, R-17), com as mesmas
    /// regras de <see cref="ObterAsync"/>. Sem versão conhecida, a consulta sai sem <c>ETag</c>.
    /// </summary>
    async Task<Resultado<PedidoConsultado>> ConsultarAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        var resultado = await ObterAsync(pedidoId, usuarioId, perfis, cancellationToken);
        return resultado.Sucesso ? new PedidoConsultado(resultado.Valor!, null) : resultado.Falha!;
    }

    /// <summary>
    /// Altera o status conforme as transições permitidas (FR-020):
    /// <list type="table">
    /// <listheader><term>De → Para</term><description>Perfis</description></listheader>
    /// <item><term>Pendente → Processando</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Processando → ProntoParaEnvio</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>ProntoParaEnvio → Despachado</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Pendente, Processando, ProntoParaEnvio → Cancelado</term><description>Admin, OperadorLogistica</description></item>
    /// <item><term>Pendente → Cancelado</term><description>Cliente dono do pedido</description></item>
    /// </list>
    /// Outra transição (inclusive para o mesmo status): <see cref="TipoFalha.Conflito"/> com
    /// <c>PEDIDO_TRANSICAO_INVALIDA</c>. Cliente em pedido de terceiro: <see cref="TipoFalha.NaoEncontrado"/>.
    /// Cliente em transição diferente de Pendente → Cancelado: <see cref="TipoFalha.SemPermissao"/>.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken);

    /// <summary>
    /// Registra o rastreio (REQ-11, <c>specs/011-rastreio/contracts/rastreio-http.md</c>); só a
    /// operação chega aqui (a política barra o Cliente):
    /// <list type="table">
    /// <listheader><term>Status atual</term><description>Resultado</description></listheader>
    /// <item><term>ProntoParaEnvio</term><description>ordem criada se faltar, rastreio gravado, Despachado</description></item>
    /// <item><term>Despachado, dados iguais</term><description>nada muda</description></item>
    /// <item><term>Despachado, dados diferentes</term><description>rastreio corrigido, status e data de despacho mantidos</description></item>
    /// <item><term>outro</term><description><see cref="TipoFalha.Conflito"/> com <c>PEDIDO_TRANSICAO_INVALIDA</c></description></item>
    /// </list>
    /// Rastreio usado por outro pedido da mesma transportadora (sem diferenciar caixa):
    /// <see cref="TipoFalha.Conflito"/> com <c>PEDIDO_RASTREIO_DUPLICADO</c>. Pedido inexistente:
    /// <see cref="TipoFalha.NaoEncontrado"/>.
    /// </summary>
    Task<Resultado<PedidoResponseDTO>> RegistrarRastreioAsync(Guid pedidoId, Guid usuarioId, RegistrarRastreioRequestDTO rastreio, CancellationToken cancellationToken);
}

/// <summary>O pedido consultado e a versão dele, para o <c>ETag</c> (REQ-18, R-17).</summary>
public sealed record PedidoConsultado(PedidoResponseDTO Pedido, byte[]? Versao);
