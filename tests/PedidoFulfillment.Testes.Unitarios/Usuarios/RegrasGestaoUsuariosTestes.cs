using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Usuarios;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Usuarios;

/// <summary>A ordem das regras das alterações de usuário (REQ-25, R-04).</summary>
public sealed class RegrasGestaoUsuariosTestes
{
    private static readonly Guid Ator = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid OutraConta = Guid.Parse("0199f000-0000-7000-8000-000000000002");

    private static readonly AlteracaoUsuario RetirarAdmin = new(TipoAlteracaoUsuario.RetirarPerfil, PerfilUsuario.Admin);
    private static readonly AlteracaoUsuario Desativar = new(TipoAlteracaoUsuario.Desativar);

    private static ContaGestao Conta(Guid id, bool desativada = false, bool convitePendente = false, params PerfilUsuario[] perfis) =>
        new(id, "Nome", "conta@exemplo.com.br", perfis, desativada, EmailConfirmado: true, convitePendente);

    private static CenarioAlteracao Cenario(ContaGestao? alvo, int adminsAtivos = 2, bool atorEhAdmin = true) =>
        new(Ator, atorEhAdmin, alvo, adminsAtivos);

    private static void DeveRecusar(DecisaoAlteracao decisao, string codigo)
    {
        decisao.Desfecho.Should().Be(DesfechoAlteracao.Recusar);
        decisao.Falha!.Codigo.Should().Be(codigo);
    }

    [Fact]
    public void Quem_deixou_de_ser_Admin_ativo_recebe_sem_permissao_antes_de_qualquer_outra_regra()
    {
        var decisao = RegrasGestaoUsuarios.Avaliar(Cenario(null, atorEhAdmin: false), Desativar);

        decisao.Falha.Should().Be(RegrasGestaoUsuarios.SemPermissao);
    }

    [Fact]
    public void Conta_inexistente_recebe_nao_encontrado() =>
        RegrasGestaoUsuarios.Avaliar(Cenario(null), Desativar).Falha.Should().Be(RegrasGestaoUsuarios.NaoEncontrado);

    public static TheoryData<AlteracaoUsuario, ContaGestao> RepeticoesSemEfeito => new()
    {
        { new(TipoAlteracaoUsuario.AtribuirPerfil, PerfilUsuario.OperadorLogistica), Conta(OutraConta, perfis: PerfilUsuario.OperadorLogistica) },
        { new(TipoAlteracaoUsuario.RetirarPerfil, PerfilUsuario.Admin), Conta(OutraConta, perfis: PerfilUsuario.OperadorLogistica) },
        { new(TipoAlteracaoUsuario.Desativar), Conta(OutraConta, desativada: true, perfis: PerfilUsuario.Cliente) },
        { new(TipoAlteracaoUsuario.Reativar), Conta(OutraConta, perfis: PerfilUsuario.Cliente) },
    };

    [Theory]
    [MemberData(nameof(RepeticoesSemEfeito))]
    public void Repetir_o_que_ja_vale_nao_tem_efeito(AlteracaoUsuario alteracao, ContaGestao alvo) =>
        RegrasGestaoUsuarios.Avaliar(Cenario(alvo), alteracao).Desfecho.Should().Be(DesfechoAlteracao.SemEfeito);

    [Fact]
    public void Unico_Admin_tirando_o_proprio_perfil_recebe_ultimo_admin_mesmo_com_um_so_perfil() =>
        DeveRecusar(RegrasGestaoUsuarios.Avaliar(Cenario(Conta(Ator, perfis: PerfilUsuario.Admin), adminsAtivos: 1), RetirarAdmin), CodigosErro.UsuarioUltimoAdmin);

    [Fact]
    public void Unico_Admin_se_desativando_recebe_ultimo_admin() =>
        DeveRecusar(RegrasGestaoUsuarios.Avaliar(Cenario(Conta(Ator, perfis: PerfilUsuario.Admin), adminsAtivos: 1), Desativar), CodigosErro.UsuarioUltimoAdmin);

    [Fact]
    public void Com_outro_Admin_ativo_tirar_o_proprio_Admin_recebe_propria_conta() =>
        DeveRecusar(
            RegrasGestaoUsuarios.Avaliar(Cenario(Conta(Ator, perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica])), RetirarAdmin),
            CodigosErro.UsuarioPropriaConta);

    [Fact]
    public void Com_outro_Admin_ativo_se_desativar_recebe_propria_conta() =>
        DeveRecusar(RegrasGestaoUsuarios.Avaliar(Cenario(Conta(Ator, perfis: PerfilUsuario.Admin)), Desativar), CodigosErro.UsuarioPropriaConta);

    [Fact]
    public void Retirar_de_si_um_perfil_que_nao_e_Admin_e_permitido() =>
        RegrasGestaoUsuarios.Avaliar(
                Cenario(Conta(Ator, perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica])),
                new AlteracaoUsuario(TipoAlteracaoUsuario.RetirarPerfil, PerfilUsuario.OperadorLogistica))
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Fact]
    public void Atribuir_a_si_um_perfil_e_permitido() =>
        RegrasGestaoUsuarios.Avaliar(
                Cenario(Conta(Ator, perfis: PerfilUsuario.Admin)),
                new AlteracaoUsuario(TipoAlteracaoUsuario.AtribuirPerfil, PerfilUsuario.OperadorLogistica))
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Fact]
    public void Retirar_o_unico_perfil_recebe_ultimo_perfil() =>
        DeveRecusar(RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta, perfis: PerfilUsuario.Admin)), RetirarAdmin), CodigosErro.UsuarioUltimoPerfil);

    [Fact]
    public void Retirar_Admin_de_quem_tem_outro_perfil_e_aplicado() =>
        RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta, perfis: [PerfilUsuario.Admin, PerfilUsuario.OperadorLogistica])), RetirarAdmin)
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Fact]
    public void Admin_convidado_nao_conta_como_ativo_e_pode_ser_desativado_sem_a_regra_do_ultimo_admin() =>
        RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta, convitePendente: true, perfis: PerfilUsuario.Admin), adminsAtivos: 1), Desativar)
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Fact]
    public void Reativar_conta_desativada_e_aplicado() =>
        RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta, desativada: true, perfis: PerfilUsuario.Cliente)), new AlteracaoUsuario(TipoAlteracaoUsuario.Reativar))
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Theory]
    [InlineData(PerfilUsuario.Admin, PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.OperadorLogistica, PerfilUsuario.Cliente)]
    [InlineData(PerfilUsuario.Cliente, PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.Cliente, PerfilUsuario.OperadorLogistica)]
    public void Atribuicao_que_mistura_grupos_recebe_perfis_incompativeis(PerfilUsuario pedido, PerfilUsuario atual) =>
        DeveRecusar(
            RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta, perfis: atual)), new AlteracaoUsuario(TipoAlteracaoUsuario.AtribuirPerfil, pedido)),
            CodigosErro.UsuarioPerfisIncompativeis);

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    [InlineData(PerfilUsuario.Cliente)]
    public void Conta_sem_perfil_aceita_qualquer_grupo(PerfilUsuario pedido) =>
        RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta)), new AlteracaoUsuario(TipoAlteracaoUsuario.AtribuirPerfil, pedido))
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);

    [Fact]
    public void Conta_com_grupos_ja_misturados_aceita_retirada_e_recusa_atribuicao()
    {
        var misturada = Conta(OutraConta, perfis: [PerfilUsuario.Cliente, PerfilUsuario.OperadorLogistica]);

        RegrasGestaoUsuarios.Avaliar(Cenario(misturada), new AlteracaoUsuario(TipoAlteracaoUsuario.RetirarPerfil, PerfilUsuario.Cliente))
            .Desfecho.Should().Be(DesfechoAlteracao.Aplicar);
        DeveRecusar(
            RegrasGestaoUsuarios.Avaliar(Cenario(misturada), new AlteracaoUsuario(TipoAlteracaoUsuario.AtribuirPerfil, PerfilUsuario.Admin)),
            CodigosErro.UsuarioPerfisIncompativeis);
    }

    [Fact]
    public void Atribuicao_sem_perfil_e_erro_de_programacao()
    {
        var avaliar = () => RegrasGestaoUsuarios.Avaliar(Cenario(Conta(OutraConta)), new AlteracaoUsuario(TipoAlteracaoUsuario.AtribuirPerfil));

        avaliar.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Entram_na_auditoria_so_as_recusas_de_ultimo_admin_e_de_propria_conta()
    {
        RegrasGestaoUsuarios.RecusaAuditada(RegrasGestaoUsuarios.UltimoAdmin).Should().BeTrue();
        RegrasGestaoUsuarios.RecusaAuditada(RegrasGestaoUsuarios.PropriaConta).Should().BeTrue();
        RegrasGestaoUsuarios.RecusaAuditada(RegrasGestaoUsuarios.UltimoPerfil).Should().BeFalse();
        RegrasGestaoUsuarios.RecusaAuditada(RegrasGestaoUsuarios.PerfisIncompativeis).Should().BeFalse();
        RegrasGestaoUsuarios.RecusaAuditada(RegrasGestaoUsuarios.NaoEncontrado).Should().BeFalse();
    }
}
