using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Testes.Unitarios.Contratos;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>Guardas das entidades de notificação e a forma do cadastro de destino (REQ-18, FR-008).</summary>
public sealed class GuardasNotificacoesTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static AvisoPedido Aviso(Guid? id = null, string tipo = "pedido.status-alterado", DateTimeOffset? instante = null) =>
        AvisoPedido.Criar(id ?? Guid.CreateVersion7(), tipo, Guid.CreateVersion7(), Guid.CreateVersion7(), "{}", instante ?? T0);

    [Fact]
    public void Aviso_valido_guarda_o_tipo_aparado()
    {
        Aviso(tipo: "  pedido.status-alterado  ").Tipo.Should().Be("pedido.status-alterado");
    }

    [Fact]
    public void Aviso_recusa_identificador_vazio_tipo_vazio_ou_longo_e_instante_fora_de_utc()
    {
        FluentActions.Invoking(() => Aviso(id: Guid.Empty)).Should().Throw<ArgumentException>().WithMessage("O identificador é obrigatório.*");
        FluentActions.Invoking(() => Aviso(tipo: "  ")).Should().Throw<ArgumentException>().WithMessage("O valor é obrigatório.*");
        FluentActions.Invoking(() => Aviso(tipo: new string('t', AvisoPedido.TamanhoMaximoTipo + 1)))
            .Should().Throw<ArgumentException>().WithMessage("O valor deve ter no máximo*");
        FluentActions.Invoking(() => Aviso(instante: new DateTimeOffset(2026, 11, 1, 9, 0, 0, TimeSpan.FromHours(-3))))
            .Should().Throw<ArgumentException>().WithMessage("O instante deve estar em UTC*");
    }

    [Fact]
    public void Entrega_recusa_aviso_sem_identificador()
    {
        FluentActions.Invoking(() => EntregaNotificacao.Criar(Guid.CreateVersion7(), 0, null, T0))
            .Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Segredo_e_lido_com_ou_sem_o_prefixo()
    {
        var segredo = SegredoWebhook.Gerar();

        SegredoWebhook.Bytes(SegredoWebhook.Texto(segredo)).Should().Equal(segredo);
        SegredoWebhook.Bytes(Convert.ToBase64String(segredo)).Should().Equal(segredo);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Destino_sem_url_e_recusado(string url)
    {
        Validacao.Validar(new CadastrarDestinoRequestDTO { Url = url })["Url"].Should().Equal("A URL é obrigatória.");
    }

    [Fact]
    public void Destino_com_url_longa_demais_e_recusado()
    {
        Validacao.Validar(new CadastrarDestinoRequestDTO { Url = "https://exemplo.com/" + new string('a', 2048) })["Url"]
            .Should().Equal("A URL deve ter até 2048 caracteres.");
    }

    [Theory]
    [InlineData("/relativa", "A URL deve ser absoluta, com https.")]
    [InlineData("ftp://exemplo.com/x", "A URL deve ser absoluta, com https.")]
    [InlineData("https://usuario:senha@exemplo.com/x", "A URL não pode ter usuário nem senha.")]
    public void Destino_com_url_fora_da_forma_e_recusado(string url, string mensagem)
    {
        Validacao.Validar(new CadastrarDestinoRequestDTO { Url = url })["Url"].Should().Equal(mensagem);
    }

    [Fact]
    public void Destino_com_url_valida_vem_aparado()
    {
        var dto = new CadastrarDestinoRequestDTO { Url = "  https://exemplo.com/x  " };

        Validacao.Validar(dto).Should().BeEmpty();
        dto.Url.Should().Be("https://exemplo.com/x");
    }
}
