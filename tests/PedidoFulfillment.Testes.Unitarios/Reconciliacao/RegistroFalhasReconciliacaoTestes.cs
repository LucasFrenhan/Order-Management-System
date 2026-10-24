using FluentAssertions;
using PedidoFulfillment.Application.Reconciliacao;

namespace PedidoFulfillment.Testes.Unitarios.Reconciliacao;

/// <summary>Contagem de falhas em memória da reconciliação (REQ-12, R-06).</summary>
public sealed class RegistroFalhasReconciliacaoTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly RegistroFalhasReconciliacao registro = new();

    [Fact]
    public void Conta_os_ciclos_com_falha_de_cada_pedido_separadamente()
    {
        var a = Guid.CreateVersion7();
        var b = Guid.CreateVersion7();

        registro.RegistrarFalha(a, Agora).Should().Be(1);
        registro.RegistrarFalha(a, Agora.AddMinutes(5)).Should().Be(2);
        registro.RegistrarFalha(b, Agora).Should().Be(1);
        registro.RegistrarFalha(a, Agora.AddMinutes(10)).Should().Be(3);

        registro.ComFalha.Should().BeEquivalentTo([a, b]);
    }

    [Fact]
    public void Remover_zera_a_contagem()
    {
        var a = Guid.CreateVersion7();
        registro.RegistrarFalha(a, Agora);
        registro.RegistrarFalha(a, Agora);

        registro.Remover(a);

        registro.ComFalha.Should().BeEmpty();
        registro.RegistrarFalha(a, Agora).Should().Be(1);
    }

    [Fact]
    public void Podar_remove_so_as_entradas_com_a_ultima_falha_antes_do_limite()
    {
        var antiga = Guid.CreateVersion7();
        var recente = Guid.CreateVersion7();
        registro.RegistrarFalha(antiga, Agora.AddDays(-2));
        registro.RegistrarFalha(recente, Agora.AddDays(-2));
        registro.RegistrarFalha(recente, Agora);

        registro.Podar(Agora.AddDays(-1));

        registro.ComFalha.Should().Equal(recente);
    }

    [Fact]
    public void Remover_pedido_sem_entrada_nao_lanca()
    {
        registro.Invoking(r => r.Remover(Guid.CreateVersion7())).Should().NotThrow();
    }

    [Fact]
    public void Falhas_concorrentes_nao_se_perdem()
    {
        var a = Guid.CreateVersion7();

        Parallel.For(0, 1000, _ => registro.RegistrarFalha(a, Agora));

        registro.RegistrarFalha(a, Agora).Should().Be(1001);
    }
}
