using FluentAssertions;
using PedidoFulfillment.Application.Comum;

namespace PedidoFulfillment.Testes.Unitarios;

public sealed class TesteQualityGateBugTestes
{
    [Fact]
    public void Conta_os_itens() => TesteQualityGateBug.Contar([1, 2, 3]).Should().Be(3);
}
