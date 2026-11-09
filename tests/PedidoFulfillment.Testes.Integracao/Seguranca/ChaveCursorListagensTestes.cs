using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Seguranca;

/// <summary>
/// Chave dos cursores das listagens (REQ-27, FR-008, SC-006; research R-06). A chave é o único fator do HMAC do
/// cursor: a mesma chave em duas instâncias aceita os mesmos cursores, e uma chave diferente os recusa (400).
/// </summary>
public sealed class ChaveCursorListagensTestes
{
    private const string ValorA = "valor-secreto-da-chave-a-com-mais-de-32-bytes";
    private const string ValorB = "valor-secreto-da-chave-b-com-mais-de-32-bytes";
    private const string ChaveCursor = "chave-propria-dos-cursores-com-mais-de-32-bytes";

    private static byte[] ChaveDe(string ativa, string valorAtiva, string? chaveCursor = null)
    {
        var configuracoes = new Dictionary<string, string?>
        {
            ["Jwt:Chaves:0:Identificador"] = ativa,
            ["Jwt:Chaves:0:Valor"] = valorAtiva,
            ["Jwt:ChaveAtiva"] = ativa
        };
        if (chaveCursor is not null)
        {
            configuracoes["Listagens:ChaveCursor"] = chaveCursor;
        }

        using var api = new FabricaApi { Configuracoes = configuracoes };
        return api.Services.GetRequiredService<IChaveCursor>().Chave;
    }

    private static byte[] Hkdf(string valor) =>
        HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(valor), 32, info: "oms/listagem-pedidos/cursor/v1"u8.ToArray());

    [Fact]
    public void Com_chave_propria_a_troca_da_chave_ativa_nao_muda_a_chave_do_cursor()
    {
        var antes = ChaveDe("chave-a", ValorA, ChaveCursor);
        var depois = ChaveDe("chave-b", ValorB, ChaveCursor);

        depois.Should().Equal(antes, "os cursores em uso continuam válidos depois da rotação (SC-006)");
        antes.Should().Equal(Hkdf(ChaveCursor));
    }

    [Fact]
    public void Sem_chave_propria_a_chave_do_cursor_muda_com_a_chave_ativa()
    {
        var antes = ChaveDe("chave-a", ValorA);
        var depois = ChaveDe("chave-b", ValorB);

        depois.Should().NotEqual(antes, "sem chave própria, a troca da ativa invalida os cursores em uso");
    }

    [Fact]
    public void Sem_chave_propria_a_derivacao_e_a_mesma_de_antes_do_REQ_27()
    {
        // Antes do REQ-27, a chave vinha do HKDF da Jwt:ChaveAssinatura; o mesmo valor na chave ativa mantém os cursores.
        ChaveDe("qualquer-identificador", FabricaApi.ChaveTeste).Should().Equal(Hkdf(FabricaApi.ChaveTeste));
    }
}
