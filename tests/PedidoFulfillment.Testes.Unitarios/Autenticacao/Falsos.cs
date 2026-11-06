using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Emails;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>
/// Gestor de identidade falso: devolve o resultado configurado e guarda os argumentos recebidos.
/// </summary>
public sealed class GestorIdentidadeFalso : IGestorIdentidade
{
    public Resultado<UsuarioResponseDTO> ResultadoCriacao { get; set; } = new UsuarioResponseDTO
    {
        Id = Guid.NewGuid(),
        Nome = "Maria Silva",
        Email = "Maria@Exemplo.com.br",
        Perfis = [PerfilUsuario.Cliente]
    };

    public ResultadoCredenciais ResultadoVerificacao { get; set; } = ResultadoCredenciais.Invalida(null);

    public (string Nome, string Email, string Senha)? UltimaCriacao { get; private set; }

    public (string Email, string Senha)? UltimaVerificacao { get; private set; }

    public Task<Resultado<UsuarioResponseDTO>> CriarAsync(string nome, string email, string senha, CancellationToken cancellationToken)
    {
        UltimaCriacao = (nome, email, senha);
        return Task.FromResult(ResultadoCriacao);
    }

    public Task<ResultadoCredenciais> VerificarCredenciaisAsync(string email, string senha, CancellationToken cancellationToken)
    {
        UltimaVerificacao = (email, senha);
        return Task.FromResult(ResultadoVerificacao);
    }

    public IReadOnlyList<PerfilUsuario> PerfisAtuais { get; set; } = [PerfilUsuario.Cliente];

    public Task<IReadOnlyList<PerfilUsuario>> ObterPerfisAsync(Guid usuarioId, CancellationToken cancellationToken) =>
        Task.FromResult(PerfisAtuais);

    public ResultadoTrocaSenha ResultadoTroca { get; set; } = ResultadoTrocaSenha.Trocada;

    public Task<ResultadoTrocaSenha> TrocarSenhaAsync(Guid usuarioId, string senhaAtual, string novaSenha, CancellationToken cancellationToken) =>
        Task.FromResult(ResultadoTroca);

    public Guid? ResultadoConfirmacao { get; set; }

    public byte[]? UltimaConfirmacao { get; private set; }

    public Task<Guid?> ConfirmarEmailAsync(byte[] hashToken, CancellationToken cancellationToken)
    {
        UltimaConfirmacao = hashToken;
        return Task.FromResult(ResultadoConfirmacao);
    }

    public Guid? ResultadoRedefinicao { get; set; }

    public int ConsultasConta { get; private set; }

    public (byte[] Hash, string Senha)? UltimaRedefinicao { get; private set; }

    public bool RedefinicaoPeloConvite { get; set; }

    public Task<RedefinicaoConcluida?> RedefinirSenhaAsync(byte[] hashToken, string novaSenha, CancellationToken cancellationToken)
    {
        ConsultasConta++;
        UltimaRedefinicao = (hashToken, novaSenha);
        return Task.FromResult(ResultadoRedefinicao is { } id ? new RedefinicaoConcluida(id, RedefinicaoPeloConvite) : null);
    }
}

public sealed class FilaEmailsFalsa : IFilaEmails
{
    public List<(TipoEmail Tipo, string Destino)> Solicitacoes { get; } = [];

    public Task EnfileirarAsync(TipoEmail tipo, string destino, CancellationToken cancellationToken)
    {
        Solicitacoes.Add((tipo, destino));
        return Task.CompletedTask;
    }
}

/// <summary>Armazenamento de sessões falso: devolve a rotação pedida e guarda o que recebeu.</summary>
public sealed class ArmazenamentoSessoesFalso : IArmazenamentoSessoes
{
    public static readonly DateTimeOffset ValidadeToken = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);

    public ResultadoRotacao ResultadoRotacao { get; set; } = new(SituacaoRotacao.Recusada);

    public Guid? ResultadoEncerramento { get; set; }

    public List<(Guid UsuarioId, byte[] Hash)> SessoesCriadas { get; } = [];

    public List<(byte[] Apresentado, byte[] Novo)> Rotacoes { get; } = [];

    public List<byte[]> Encerramentos { get; } = [];

    public Task<DateTimeOffset> CriarSessaoAsync(Guid usuarioId, byte[] hashToken, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken)
    {
        SessoesCriadas.Add((usuarioId, hashToken));
        return Task.FromResult(ValidadeToken);
    }

    public Task<ResultadoRotacao> RotacionarAsync(byte[] hashApresentado, byte[] hashNovo, DateTimeOffset agora, ConfiguracaoSessoes validades, CancellationToken cancellationToken)
    {
        Rotacoes.Add((hashApresentado, hashNovo));
        return Task.FromResult(ResultadoRotacao);
    }

    public Task<Guid?> EncerrarSessaoAsync(byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken)
    {
        Encerramentos.Add(hashToken);
        return Task.FromResult(ResultadoEncerramento);
    }

    public Task EncerrarSessoesDaContaAsync(Guid usuarioId, DateTimeOffset agora, CancellationToken cancellationToken) => Task.CompletedTask;

    public Task CriarTokenUsoUnicoAsync(Guid usuarioId, FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset expiraEm, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<Guid?> ConsumirTokenUsoUnicoAsync(FinalidadeTokenUsoUnico finalidade, byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken) =>
        Task.FromResult<Guid?>(null);

    public Task<TokenSenhaConsumido?> ConsumirTokenSenhaAsync(byte[] hashToken, DateTimeOffset agora, CancellationToken cancellationToken) =>
        Task.FromResult<TokenSenhaConsumido?>(null);
}

public sealed class MetricasAutenticacaoFalsas : IMetricasAutenticacao
{
    public List<SituacaoRotacao> Renovacoes { get; } = [];

    public void RenovacaoConcluida(SituacaoRotacao situacao) => Renovacoes.Add(situacao);
}

public sealed class EmissorTokenFalso : IEmissorToken
{
    public LoginResponseDTO Emitir(Guid usuarioId, IReadOnlyList<PerfilUsuario> perfis) => new()
    {
        TokenAcesso = $"token-de-{usuarioId}",
        ExpiraEm = new DateTimeOffset(2026, 10, 21, 13, 0, 0, TimeSpan.Zero),
        Perfis = perfis
    };
}

/// <summary>Um evento registrado: id, nível, texto formatado e propriedades estruturadas.</summary>
public sealed record EventoLog(EventId Id, LogLevel Nivel, string Mensagem, IReadOnlyDictionary<string, object?> Propriedades);

public sealed class LoggerFalso<T> : ILogger<T>
{
    public List<EventoLog> Eventos { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        var propriedades = (state as IEnumerable<KeyValuePair<string, object?>> ?? [])
            .ToDictionary(p => p.Key, p => p.Value);
        Eventos.Add(new EventoLog(eventId, logLevel, formatter(state, exception), propriedades));
    }
}
