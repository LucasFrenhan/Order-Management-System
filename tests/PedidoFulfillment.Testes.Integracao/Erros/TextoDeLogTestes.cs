using FluentAssertions;
using PedidoFulfillment.API.Erros;

namespace PedidoFulfillment.Testes.Integracao.Erros;

/// <summary>Valores da requisição sem quebras de linha nos logs (REQ-26, <c>cs/log-forging</c>).</summary>
public sealed class TextoDeLogTestes
{
    [Theory]
    [InlineData("/v1/pedidos", "/v1/pedidos")]
    [InlineData("/x\n[ERRO] falso", "/x[ERRO] falso")]
    [InlineData("/x\r\n[ERRO] falso", "/x[ERRO] falso")]
    [InlineData("GET\r", "GET")]
    [InlineData("", "")]
    [InlineData(null, "")]
    public void Tira_as_quebras_de_linha(string? valor, string esperado)
    {
        TextoDeLog.SemQuebrasDeLinha(valor).Should().Be(esperado);
    }
}
