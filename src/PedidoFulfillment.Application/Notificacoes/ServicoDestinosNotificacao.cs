using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Notificacoes;

/// <summary>Limites e regras de rede dos destinos, vindos da configuração (REQ-18, FR-008 e FR-010).</summary>
public sealed record ConfiguracaoDestinos(int DestinosPorCliente, int DestinosGlobais, bool PermitirRedeLocal);

/// <summary>Cadastro e gestão dos destinos de webhook pela API (REQ-18, US2 e US4; research R-15).</summary>
public interface IServicoDestinosNotificacao
{
    Task<Resultado<DestinoCadastradoResponseDTO>> CadastrarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CadastrarDestinoRequestDTO requisicao, CancellationToken cancellationToken);

    Task<Resultado<IReadOnlyList<DestinoNotificacaoResponseDTO>>> ListarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);

    Task<Resultado<bool>> RemoverAsync(Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, CancellationToken cancellationToken);

    Task<Resultado<DestinoNotificacaoResponseDTO>> ReativarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, CancellationToken cancellationToken);

    Task<Resultado<IReadOnlyList<EntregaNotificacaoResponseDTO>>> ListarEntregasAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, int limite, CancellationToken cancellationToken);
}

/// <inheritdoc />
public sealed partial class ServicoDestinosNotificacao(
    IArmazenamentoNotificacoes armazenamento,
    IVerificadorEndereco verificador,
    ICifraSegredos cifra,
    ConfiguracaoDestinos configuracao,
    TimeProvider relogio,
    ILogger<ServicoDestinosNotificacao> logger) : IServicoDestinosNotificacao
{
    public const int LimiteEntregasPadrao = 50;
    public const int LimiteEntregasMaximo = 200;

    private static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Destino não encontrado.", "Não existe destino de notificação com este identificador.");

    private static readonly Falha SemHttps = Invalido("A URL deve ser absoluta, com https.");

    private static readonly Falha EnderecoProibido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.", "O endereço do destino não é público.", CodigosErro.NotificacaoDestinoEnderecoProibido);

    private static readonly Falha LimiteInvalido = Invalido($"O limite deve estar entre 1 e {LimiteEntregasMaximo}.");

    public async Task<Resultado<DestinoCadastradoResponseDTO>> CadastrarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CadastrarDestinoRequestDTO requisicao, CancellationToken cancellationToken)
    {
        var url = new Uri(requisicao.Url.Trim(), UriKind.Absolute);
        if (url.Scheme != Uri.UriSchemeHttps && !configuracao.PermitirRedeLocal)
        {
            return SemHttps;
        }

        if (!await verificador.EnderecoPublicoAsync(url, cancellationToken))
        {
            return EnderecoProibido;
        }

        var dono = Dono(usuarioId, perfis);
        var limite = dono is null ? configuracao.DestinosGlobais : configuracao.DestinosPorCliente;
        var segredo = SegredoWebhook.Gerar();
        var destino = await armazenamento.ExecutarComBloqueioDoDonoAsync(dono, async ct =>
        {
            if (await armazenamento.ContarDestinosAsync(dono, ct) >= limite)
            {
                return null;
            }

            var novo = DestinoNotificacao.Cadastrar(dono, usuarioId, url.OriginalString, cifra.Cifrar(segredo), relogio.GetUtcNow());
            armazenamento.Adicionar(novo);
            await armazenamento.SalvarAsync(ct);
            return novo;
        }, cancellationToken);
        if (destino is null)
        {
            return new Falha(TipoFalha.Conflito, "Limite de destinos atingido.", $"O limite de {limite} destinos foi atingido.", CodigosErro.NotificacaoDestinoLimite);
        }

        DestinoCadastrado(logger, destino.Id, url.GetLeftPart(UriPartial.Authority), destino.Global, usuarioId);
        var resposta = DestinoNotificacaoResponseDTO.De(destino);
        return new DestinoCadastradoResponseDTO
        {
            Id = resposta.Id,
            Url = resposta.Url,
            Global = resposta.Global,
            Ativo = resposta.Ativo,
            MotivoDesativacao = resposta.MotivoDesativacao,
            DesativadoEm = resposta.DesativadoEm,
            FalhasSeguidas = resposta.FalhasSeguidas,
            CriadoEm = resposta.CriadoEm,
            Segredo = SegredoWebhook.Texto(segredo)
        };
    }

    public async Task<Resultado<IReadOnlyList<DestinoNotificacaoResponseDTO>>> ListarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken) =>
        Resultado<IReadOnlyList<DestinoNotificacaoResponseDTO>>.Ok(
            [.. (await armazenamento.ListarDestinosAsync(Dono(usuarioId, perfis), cancellationToken)).Select(DestinoNotificacaoResponseDTO.De)]);

    public async Task<Resultado<bool>> RemoverAsync(Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, CancellationToken cancellationToken)
    {
        if (await armazenamento.ObterDestinoAsync(destinoId, Dono(usuarioId, perfis), cancellationToken) is not { } destino)
        {
            return NaoEncontrado;
        }

        var descartadas = await armazenamento.DescartarPendentesAsync(destino.Id, relogio.GetUtcNow(), cancellationToken);
        armazenamento.Remover(destino);
        await armazenamento.SalvarAsync(cancellationToken);
        DestinoRemovido(logger, destino.Id, HostDestino(destino.Url), usuarioId, descartadas);
        return true;
    }

    public async Task<Resultado<DestinoNotificacaoResponseDTO>> ReativarAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, CancellationToken cancellationToken)
    {
        if (await armazenamento.ObterDestinoAsync(destinoId, Dono(usuarioId, perfis), cancellationToken) is not { } destino)
        {
            return NaoEncontrado;
        }

        if (destino.Reativar(relogio.GetUtcNow()))
        {
            await armazenamento.SalvarAsync(cancellationToken);
            DestinoReativado(logger, destino.Id, HostDestino(destino.Url), usuarioId);
        }

        return DestinoNotificacaoResponseDTO.De(destino);
    }

    public async Task<Resultado<IReadOnlyList<EntregaNotificacaoResponseDTO>>> ListarEntregasAsync(
        Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, Guid destinoId, int limite, CancellationToken cancellationToken)
    {
        if (limite is < 1 or > LimiteEntregasMaximo)
        {
            return LimiteInvalido;
        }

        if (await armazenamento.ObterDestinoAsync(destinoId, Dono(usuarioId, perfis), cancellationToken) is null)
        {
            return NaoEncontrado;
        }

        var entregas = await armazenamento.ListarEntregasAsync(destinoId, limite, cancellationToken);
        return Resultado<IReadOnlyList<EntregaNotificacaoResponseDTO>>.Ok([.. entregas.Select(EntregaNotificacaoResponseDTO.De)]);
    }

    /// <summary>
    /// O escopo do usuário: o Admin trata dos destinos globais (nulo), de qualquer Admin; o Cliente,
    /// dos próprios. Quem tem os dois perfis age como Admin (research R-15; analyze U5).
    /// </summary>
    private static Guid? Dono(Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis) =>
        perfis.Contains(PerfilUsuario.Admin) ? null : usuarioId;

    private static string HostDestino(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.GetLeftPart(UriPartial.Authority) : "(url inválida)";

    private static Falha Invalido(string mensagem) =>
        new(TipoFalha.Validacao, "Um ou mais campos são inválidos.", mensagem);

    [LoggerMessage(EventId = 10001, EventName = "DestinoNotificacaoCadastrado", Level = LogLevel.Information,
        Message = "Destino de notificação {DestinoId} cadastrado ({Host}, global: {Global}) por {UsuarioId}.")]
    private static partial void DestinoCadastrado(ILogger logger, Guid destinoId, string host, bool global, Guid usuarioId);

    [LoggerMessage(EventId = 10002, EventName = "DestinoNotificacaoRemovido", Level = LogLevel.Information,
        Message = "Destino de notificação {DestinoId} ({Host}) removido por {UsuarioId}; {Descartadas} entrega(s) pendente(s) descartada(s).")]
    private static partial void DestinoRemovido(ILogger logger, Guid destinoId, string host, Guid usuarioId, int descartadas);

    [LoggerMessage(EventId = 10003, EventName = "DestinoNotificacaoReativado", Level = LogLevel.Information,
        Message = "Destino de notificação {DestinoId} ({Host}) reativado por {UsuarioId}.")]
    private static partial void DestinoReativado(ILogger logger, Guid destinoId, string host, Guid usuarioId);
}
