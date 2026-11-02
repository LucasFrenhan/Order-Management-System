using System.Net;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Infrastructure.Configuracao;
using PedidoFulfillment.Infrastructure.Notificacoes;

namespace PedidoFulfillment.Testes.Unitarios.Notificacoes;

/// <summary>
/// Endereços proibidos, cifra do segredo e assinatura dos webhooks (REQ-18, FR-008, FR-009,
/// FR-013 e FR-016; research R-06, R-08 e R-15).
/// </summary>
public sealed class SegurancaWebhookTestes
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("127.10.0.5")]
    [InlineData("10.1.2.3")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.10")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("224.0.0.1")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("::ffff:127.0.0.1")]
    public void Enderecos_internos_sao_proibidos(string endereco)
    {
        EnderecosProibidos.Proibido(IPAddress.Parse(endereco)).Should().BeTrue();
    }

    [Theory]
    [InlineData("8.8.8.8")]
    [InlineData("172.32.0.1")]
    [InlineData("100.128.0.1")]
    [InlineData("2606:4700::1111")]
    public void Enderecos_publicos_sao_aceitos(string endereco)
    {
        EnderecosProibidos.Proibido(IPAddress.Parse(endereco)).Should().BeFalse();
    }

    [Fact]
    public void Com_a_rede_local_liberada_o_loopback_e_aceito()
    {
        EnderecosProibidos.Proibido(IPAddress.Loopback, permitirRedeLocal: true).Should().BeFalse();
    }

    private static CifraSegredos Cifra(byte valor) =>
        new(Options.Create(new OpcoesNotificacoes { ChaveCifra = Convert.ToBase64String(Enumerable.Repeat(valor, 32).ToArray()) }));

    [Fact]
    public void A_cifra_volta_ao_segredo_com_nonce_novo_a_cada_vez_e_60_bytes()
    {
        var segredo = SegredoWebhook.Gerar();
        var cifra = Cifra(7);

        var a = cifra.Cifrar(segredo);
        var b = cifra.Cifrar(segredo);

        a.Should().HaveCount(DestinoNotificacao.TamanhoSegredoCifrado);
        a.Should().NotEqual(b);
        cifra.Decifrar(a).Should().Equal(segredo);
        cifra.Decifrar(b).Should().Equal(segredo);
    }

    [Fact]
    public void Conteudo_adulterado_ou_outra_chave_nao_decifram()
    {
        var cifrado = Cifra(7).Cifrar(SegredoWebhook.Gerar());
        var adulterado = (byte[])cifrado.Clone();
        adulterado[^1] ^= 0xFF;

        ((Action)(() => Cifra(7).Decifrar(adulterado))).Should().Throw<CryptographicException>();
        ((Action)(() => Cifra(8).Decifrar(cifrado))).Should().Throw<CryptographicException>();
    }

    [Fact]
    public void A_assinatura_bate_com_o_vetor_do_Standard_Webhooks()
    {
        // Exemplo publicado pelo Standard Webhooks (especificação e bibliotecas de referência).
        var segredo = SegredoWebhook.Bytes("whsec_MfKQ9r8GKYqrTwjUPD8ILPZIo2LaLaSw");

        var assinatura = AssinaturaWebhook.Assinar(segredo, "msg_p5jXN8AQM9LWM0D4loKWxJek", "1614265330", "{\"test\": 2432232314}");

        assinatura.Should().Be("v1,g0hM9SsE+OTPJTGt/tmIKtSyZlE3uFJELVlNIOLJ1OE=");
    }

    [Fact]
    public void O_segredo_tem_32_bytes_e_o_prefixo_whsec()
    {
        var segredo = SegredoWebhook.Gerar();
        var texto = SegredoWebhook.Texto(segredo);

        segredo.Should().HaveCount(32);
        texto.Should().StartWith("whsec_");
        SegredoWebhook.Bytes(texto).Should().Equal(segredo);
    }
}
