using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>As regras das sessões, com portas falsas (REQ-24, FR-001 a FR-008).</summary>
public sealed class ServicoSessoesTestes
{
    private static readonly ConfiguracaoSessoes Validades = new(TimeSpan.FromDays(14), TimeSpan.FromDays(30));
    private static readonly Falha Invalida = new(TipoFalha.NaoAutenticado, "Sessão inválida.", "Faça login novamente.");

    private readonly ArmazenamentoSessoesFalso armazenamento = new();
    private readonly GestorIdentidadeFalso gestor = new();
    private readonly MetricasAutenticacaoFalsas metricas = new();
    private readonly LoggerFalso<ServicoSessoes> logger = new();

    private ServicoSessoes Criar() => new(armazenamento, gestor, new EmissorTokenFalso(), Validades, metricas, TimeProvider.System, logger);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static RenovacaoRequestDTO Renovacao(string token = "token-apresentado") => new() { TokenRenovacao = token };

    [Fact]
    public async Task Iniciar_cria_a_sessao_so_com_o_hash_e_devolve_o_token_em_claro()
    {
        var usuario = Guid.NewGuid();

        var resposta = await Criar().IniciarAsync(usuario, [PerfilUsuario.Cliente], Cancelamento);

        resposta.TokenAcesso.Should().Be($"token-de-{usuario}");
        resposta.TokenRenovacao.Should().HaveLength(GeradorTokenOpaco.TamanhoTexto);
        resposta.TokenRenovacaoExpiraEm.Should().Be(ArmazenamentoSessoesFalso.ValidadeToken);
        armazenamento.SessoesCriadas.Should().ContainSingle().Which.Hash.Should().Equal(GeradorTokenOpaco.Hash(resposta.TokenRenovacao));
    }

    [Fact]
    public async Task Renovar_devolve_um_par_novo_com_os_perfis_atuais()
    {
        var usuario = Guid.NewGuid();
        var expiraEm = DateTimeOffset.UtcNow.AddDays(14);
        armazenamento.ResultadoRotacao = new ResultadoRotacao(SituacaoRotacao.Renovada, usuario, expiraEm);
        gestor.PerfisAtuais = [PerfilUsuario.Admin, PerfilUsuario.Cliente];

        var resultado = await Criar().RenovarAsync(Renovacao(), Cancelamento);

        var resposta = resultado.Valor!;
        resposta.TokenAcesso.Should().Be($"token-de-{usuario}");
        resposta.Perfis.Should().Equal(PerfilUsuario.Admin, PerfilUsuario.Cliente);
        resposta.TokenRenovacaoExpiraEm.Should().Be(expiraEm);
        var (apresentado, novo) = armazenamento.Rotacoes.Should().ContainSingle().Subject;
        apresentado.Should().Equal(GeradorTokenOpaco.Hash("token-apresentado"));
        novo.Should().Equal(GeradorTokenOpaco.Hash(resposta.TokenRenovacao));
        metricas.Renovacoes.Should().Equal(SituacaoRotacao.Renovada);
        logger.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Reutilizacao_recusa_com_a_mesma_falha_e_registra_o_log_e_a_metrica()
    {
        var usuario = Guid.NewGuid();
        armazenamento.ResultadoRotacao = new ResultadoRotacao(SituacaoRotacao.Reutilizada, usuario);

        var resultado = await Criar().RenovarAsync(Renovacao(), Cancelamento);

        resultado.Falha.Should().Be(Invalida);
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Should().Be(new EventId(3010, "RenovacaoReutilizada"));
        evento.Nivel.Should().Be(LogLevel.Warning);
        evento.Propriedades["UsuarioId"].Should().Be(usuario);
        evento.Mensagem.Should().NotContain("token-apresentado");
        metricas.Renovacoes.Should().Equal(SituacaoRotacao.Reutilizada);
    }

    [Fact]
    public async Task Logout_de_token_valido_encerra_a_sessao_e_registra_o_log()
    {
        var usuario = Guid.NewGuid();
        armazenamento.ResultadoEncerramento = usuario;

        await Criar().EncerrarAsync(new LogoutRequestDTO { TokenRenovacao = "token-da-sessao" }, Cancelamento);

        armazenamento.Encerramentos.Should().ContainSingle().Which.Should().Equal(GeradorTokenOpaco.Hash("token-da-sessao"));
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Should().Be(new EventId(3011, "SessaoEncerrada"));
        evento.Propriedades["UsuarioId"].Should().Be(usuario);
        evento.Mensagem.Should().NotContain("token-da-sessao");
    }

    [Fact]
    public async Task Logout_de_token_que_nao_vale_termina_igual_sem_log()
    {
        armazenamento.ResultadoEncerramento = null;

        await Criar().EncerrarAsync(new LogoutRequestDTO { TokenRenovacao = "desconhecido" }, Cancelamento);

        logger.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_recusado_tem_a_mesma_falha_sem_log()
    {
        armazenamento.ResultadoRotacao = new ResultadoRotacao(SituacaoRotacao.Recusada);

        var resultado = await Criar().RenovarAsync(Renovacao(), Cancelamento);

        resultado.Falha.Should().Be(Invalida);
        logger.Eventos.Should().BeEmpty();
        metricas.Renovacoes.Should().Equal(SituacaoRotacao.Recusada);
    }
}
