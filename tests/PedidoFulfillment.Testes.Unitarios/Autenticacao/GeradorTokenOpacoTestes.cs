using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using PedidoFulfillment.Application.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Autenticacao;

/// <summary>Forma e guarda dos tokens opacos (REQ-24, R-01).</summary>
public sealed class GeradorTokenOpacoTestes
{
    [Fact]
    public void Token_tem_32_bytes_em_Base64_URL_sem_caracteres_especiais()
    {
        var token = GeradorTokenOpaco.Gerar();

        Base64Url.DecodeFromChars(token).Should().HaveCount(GeradorTokenOpaco.TamanhoBytes);
        token.Should().HaveLength(GeradorTokenOpaco.TamanhoTexto).And.NotContainAny("+", "/", "=");
    }

    [Fact]
    public void Dois_tokens_sao_diferentes() =>
        GeradorTokenOpaco.Gerar().Should().NotBe(GeradorTokenOpaco.Gerar());

    [Fact]
    public void Hash_e_o_SHA256_do_texto_e_e_deterministico()
    {
        var token = GeradorTokenOpaco.Gerar();

        var hash = GeradorTokenOpaco.Hash(token);

        hash.Should().HaveCount(32).And.Equal(GeradorTokenOpaco.Hash(token)).And.Equal(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    }

    [Fact]
    public void Hash_de_texto_vazio_nao_falha() =>
        GeradorTokenOpaco.Hash("").Should().HaveCount(32);
}
