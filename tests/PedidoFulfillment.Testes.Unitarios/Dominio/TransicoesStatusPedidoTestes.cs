using FluentAssertions;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Dominio;

/// <summary>
/// Tabela de transições do contrato v1 (FR-016, SC-004): as 36 combinações de status, para a
/// operação e para o Cliente.
/// </summary>
public sealed class TransicoesStatusPedidoTestes
{
    private static readonly HashSet<(StatusPedido, StatusPedido)> PermitidasOperacao =
    [
        (StatusPedido.Pendente, StatusPedido.Processando),
        (StatusPedido.Processando, StatusPedido.ProntoParaEnvio),
        (StatusPedido.ProntoParaEnvio, StatusPedido.Despachado),
        (StatusPedido.Pendente, StatusPedido.Cancelado),
        (StatusPedido.Processando, StatusPedido.Cancelado),
        (StatusPedido.ProntoParaEnvio, StatusPedido.Cancelado)
    ];

    public static TheoryData<StatusPedido, StatusPedido> Combinacoes()
    {
        var dados = new TheoryData<StatusPedido, StatusPedido>();
        foreach (var de in Enum.GetValues<StatusPedido>())
        {
            foreach (var para in Enum.GetValues<StatusPedido>())
            {
                dados.Add(de, para);
            }
        }

        return dados;
    }

    [Fact]
    public void Ha_36_combinacoes()
    {
        Combinacoes().Should().HaveCount(36);
    }

    [Theory]
    [MemberData(nameof(Combinacoes))]
    public void Operacao_so_aceita_as_linhas_da_tabela(StatusPedido de, StatusPedido para)
    {
        TransicoesStatusPedido.PermitidaParaOperacao(de, para).Should().Be(PermitidasOperacao.Contains((de, para)));
    }

    [Theory]
    [MemberData(nameof(Combinacoes))]
    public void Cliente_so_cancela_pedido_pendente(StatusPedido de, StatusPedido para)
    {
        TransicoesStatusPedido.PermitidaParaCliente(de, para)
            .Should().Be(de == StatusPedido.Pendente && para == StatusPedido.Cancelado);
    }
}
