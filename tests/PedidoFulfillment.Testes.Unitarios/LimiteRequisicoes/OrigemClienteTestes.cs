using System.Net;
using FluentAssertions;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Testes.Unitarios.LimiteRequisicoes;

/// <summary>A origem que forma a contagem (REQ-23, FR-004, R-06).</summary>
public sealed class OrigemClienteTestes
{
    [Fact]
    public void Ipv4_conta_pelo_endereco_exato() =>
        OrigemCliente.Normalizar(IPAddress.Parse("203.0.113.7")).Should().Be("203.0.113.7");

    [Fact]
    public void Ipv4_mapeado_em_ipv6_conta_como_ipv4() =>
        OrigemCliente.Normalizar(IPAddress.Parse("::ffff:203.0.113.7")).Should().Be("203.0.113.7");

    [Fact]
    public void Enderecos_ipv6_do_mesmo_bloco_64_dividem_a_contagem() =>
        OrigemCliente.Normalizar(IPAddress.Parse("2001:db8:1:2:aaaa:bbbb:cccc:dddd"))
            .Should().Be(OrigemCliente.Normalizar(IPAddress.Parse("2001:db8:1:2:1:1:1:1")));

    [Fact]
    public void Enderecos_ipv6_de_blocos_diferentes_nao_dividem_a_contagem() =>
        OrigemCliente.Normalizar(IPAddress.Parse("2001:db8:1:2::1"))
            .Should().NotBe(OrigemCliente.Normalizar(IPAddress.Parse("2001:db8:1:3::1")));

    [Fact]
    public void Sem_endereco_a_origem_e_unica() =>
        OrigemCliente.Normalizar(null).Should().Be(OrigemCliente.Desconhecida);
}
