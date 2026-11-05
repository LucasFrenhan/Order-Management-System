using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>As regras da confirmação de e-mail, com portas falsas (REQ-24, FR-018, FR-019).</summary>
public sealed class ServicoConfirmacaoEmailTestes
{
    private readonly FilaEmailsFalsa fila = new();
    private readonly GestorIdentidadeFalso gestor = new();
    private readonly LoggerFalso<ServicoConfirmacaoEmail> logger = new();

    private ServicoConfirmacaoEmail Criar() => new(fila, gestor, logger);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Token_valido_confirma_e_registra_o_log()
    {
        var usuario = Guid.NewGuid();
        gestor.ResultadoConfirmacao = usuario;

        var falha = await Criar().ConfirmarAsync(new ConfirmacaoEmailRequestDTO { Token = "token-do-email" }, Cancelamento);

        falha.Should().BeNull();
        gestor.UltimaConfirmacao.Should().Equal(GeradorTokenOpaco.Hash("token-do-email"));
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Should().Be(new EventId(3015, "EmailConfirmado"));
        evento.Propriedades["UsuarioId"].Should().Be(usuario);
        evento.Mensagem.Should().NotContain("token-do-email");
    }

    [Fact]
    public async Task Token_que_nao_vale_devolve_a_falha_do_contrato_sem_log()
    {
        var falha = await Criar().ConfirmarAsync(new ConfirmacaoEmailRequestDTO { Token = "x" }, Cancelamento);

        falha.Should().Be(new Falha(TipoFalha.Validacao, "Token inválido.", "O token é inválido ou expirou. Peça um novo e-mail.", CodigosErro.TokenInvalido));
        logger.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task O_reenvio_so_grava_a_solicitacao_sem_consultar_a_conta()
    {
        await Criar().PedirReenvioAsync(new ReenvioConfirmacaoRequestDTO { Email = "maria@exemplo.com.br" }, Cancelamento);

        fila.Solicitacoes.Should().ContainSingle().Which.Should().Be((TipoEmail.ConfirmacaoEmail, "maria@exemplo.com.br"));
        gestor.UltimaVerificacao.Should().BeNull();
        logger.Eventos.Should().BeEmpty();
    }
}
