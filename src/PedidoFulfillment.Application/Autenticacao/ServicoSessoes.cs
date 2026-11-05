using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Autenticacao;

/// <summary>
/// Regras das sessões (REQ-24, R-02). O token em claro existe só aqui e na resposta: o armazenamento
/// recebe o hash. O bloqueio de conta não é consultado (FR-007): ele impede a adivinhação de senha, e
/// um atacante não derruba quem já está conectado.
/// </summary>
public sealed class ServicoSessoes(
    IArmazenamentoSessoes armazenamento,
    IGestorIdentidade gestor,
    IEmissorToken emissor,
    ConfiguracaoSessoes validades,
    IMetricasAutenticacao metricas,
    TimeProvider relogio,
    ILogger<ServicoSessoes> logger) : IServicoSessoes
{
    // Única resposta: o motivo real nunca aparece (FR-006).
    private static readonly Falha SessaoInvalida = new(TipoFalha.NaoAutenticado, "Sessão inválida.", "Faça login novamente.");

    public async Task<LoginResponseDTO> IniciarAsync(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        var token = GeradorTokenOpaco.Gerar();
        var expiraEm = await armazenamento.CriarSessaoAsync(usuarioId, GeradorTokenOpaco.Hash(token), relogio.GetUtcNow(), validades, cancellationToken);
        return emissor.Emitir(usuarioId, perfis) with { TokenRenovacao = token, TokenRenovacaoExpiraEm = expiraEm };
    }

    public async Task<Resultado<LoginResponseDTO>> RenovarAsync(RenovacaoRequestDTO renovacao, CancellationToken cancellationToken)
    {
        var novo = GeradorTokenOpaco.Gerar();
        var rotacao = await armazenamento.RotacionarAsync(
            GeradorTokenOpaco.Hash(renovacao.TokenRenovacao), GeradorTokenOpaco.Hash(novo), relogio.GetUtcNow(), validades, cancellationToken);
        metricas.RenovacaoConcluida(rotacao.Situacao);
        switch (rotacao)
        {
            case { Situacao: SituacaoRotacao.Renovada, UsuarioId: { } usuarioId, TokenExpiraEm: { } expiraEm }:
                var perfis = await gestor.ObterPerfisAsync(usuarioId, cancellationToken);
                return emissor.Emitir(usuarioId, perfis) with { TokenRenovacao = novo, TokenRenovacaoExpiraEm = expiraEm };
            case { Situacao: SituacaoRotacao.Reutilizada, UsuarioId: { } dono }:
                EventosAutenticacao.RenovacaoReutilizada(logger, dono);
                return SessaoInvalida;
            default:
                return SessaoInvalida;
        }
    }

    public async Task EncerrarAsync(LogoutRequestDTO logout, CancellationToken cancellationToken)
    {
        var dono = await armazenamento.EncerrarSessaoAsync(GeradorTokenOpaco.Hash(logout.TokenRenovacao), relogio.GetUtcNow(), cancellationToken);
        if (dono is { } usuarioId)
        {
            EventosAutenticacao.SessaoEncerrada(logger, usuarioId);
        }
    }
}
