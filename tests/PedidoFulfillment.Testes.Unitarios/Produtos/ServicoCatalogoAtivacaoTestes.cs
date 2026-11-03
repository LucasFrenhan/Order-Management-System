using FluentAssertions;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>REQ-22, User Story 4: desativação e reativação idempotentes.</summary>
public sealed class ServicoCatalogoAtivacaoTestes : ServicoCatalogoTestesBase
{
    [Fact]
    public async Task Desativar_e_reativar_mudam_a_situacao_e_logam()
    {
        var produto = Armazenamento.Guardar("CAF-250", "Café");

        var desativado = (await Servico().DesativarAsync(produto.Id, Admin, CancellationToken.None)).Valor!;
        var reativado = (await Servico().AtivarAsync(produto.Id, Admin, CancellationToken.None)).Valor!;

        (desativado.Ativo, reativado.Ativo).Should().Be((false, true));
        IdsDeLog().Should().Equal(12004, 12005);
    }

    [Fact]
    public async Task Repetir_nao_grava_nem_loga_e_responde_200()
    {
        var ativo = Armazenamento.Guardar("A-1", "Ativo");
        var inativo = Armazenamento.Guardar("I-1", "Inativo", ativo: false);

        var ativado = await Servico().AtivarAsync(ativo.Id, Admin, CancellationToken.None);
        var desativado = await Servico().DesativarAsync(inativo.Id, Admin, CancellationToken.None);

        (ativado.Valor!.Ativo, desativado.Valor!.Ativo).Should().Be((true, false));
        Armazenamento.Gravacoes.Should().Be(0);
        IdsDeLog().Should().BeEmpty();
    }
}
