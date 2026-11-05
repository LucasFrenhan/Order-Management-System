using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Autenticacao;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Emails;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>As regras de senha, com portas falsas (REQ-24, FR-010 a FR-016).</summary>
public sealed class ServicoSenhasTestes
{
    private const string NovaSenha = "OutraSenha2";

    private readonly FilaEmailsFalsa fila = new();
    private readonly GestorIdentidadeFalso gestor = new();
    private readonly LoggerFalso<ServicoSenhas> logger = new();

    private ServicoSenhas Criar() => new(fila, gestor, logger);

    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    [Fact]
    public async Task O_pedido_so_grava_a_solicitacao_sem_consultar_a_conta_e_sem_log()
    {
        await Criar().PedirRecuperacaoAsync(new RecuperacaoSenhaRequestDTO { Email = "maria@exemplo.com.br" }, Cancelamento);

        fila.Solicitacoes.Should().ContainSingle().Which.Should().Be((TipoEmail.RecuperacaoSenha, "maria@exemplo.com.br"));
        gestor.ConsultasConta.Should().Be(0);
        gestor.UltimaVerificacao.Should().BeNull();
        logger.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Token_que_nao_vale_devolve_a_falha_do_contrato_sem_log()
    {
        gestor.ResultadoRedefinicao = null;

        var falha = await Criar().RedefinirAsync(new RedefinicaoSenhaRequestDTO { Token = "x", NovaSenha = NovaSenha }, Cancelamento);

        falha.Should().Be(new Falha(TipoFalha.Validacao, "Token inválido.", "O token é inválido ou expirou. Peça uma nova recuperação.", CodigosErro.TokenInvalido));
        logger.Eventos.Should().BeEmpty();
    }

    [Fact]
    public async Task Redefinicao_concluida_passa_o_hash_e_registra_o_log()
    {
        var usuario = Guid.NewGuid();
        gestor.ResultadoRedefinicao = usuario;

        var falha = await Criar().RedefinirAsync(new RedefinicaoSenhaRequestDTO { Token = "token-do-email", NovaSenha = NovaSenha }, Cancelamento);

        falha.Should().BeNull();
        gestor.UltimaRedefinicao!.Value.Hash.Should().Equal(GeradorTokenOpaco.Hash("token-do-email"));
        gestor.UltimaRedefinicao!.Value.Senha.Should().Be(NovaSenha);
        var evento = logger.Eventos.Should().ContainSingle().Subject;
        evento.Id.Should().Be(new EventId(3014, "SenhaRedefinida"));
        evento.Propriedades["UsuarioId"].Should().Be(usuario);
        evento.Mensagem.Should().NotContain("token-do-email").And.NotContain(NovaSenha);
    }
}
