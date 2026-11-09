using FluentAssertions;
using PedidoFulfillment.API.Seguranca;

namespace PedidoFulfillment.Testes.Integracao.Seguranca;

/// <summary>
/// Regras da configuração das chaves do JWT (REQ-27, FR-005 e FR-007; contracts/configuracao.md).
/// São testes unitários, sem subir a API nem fazer I/O; ficam neste projeto porque o projeto de
/// testes unitários não referencia a API (research R-07).
/// </summary>
public sealed class ValidadorOpcoesJwtTestes
{
    private const string ValorA = "valor-secreto-da-chave-a-com-mais-de-32-bytes";
    private const string ValorB = "valor-secreto-da-chave-b-com-mais-de-32-bytes";
    private const string ValorC = "valor-secreto-da-chave-c-com-mais-de-32-bytes";
    private const string ValorD = "valor-secreto-da-chave-d-com-mais-de-32-bytes";

    private static readonly ValidadorOpcoesJwt Validador = new();

    private static OpcoesJwt Opcoes(string ativa, params (string? Identificador, string? Valor)[] chaves) => new()
    {
        Emissor = "PedidoFulfillment",
        Audiencia = "PedidoFulfillment",
        ChaveAtiva = ativa,
        Chaves = chaves.Select(c => new ChaveJwtConfigurada { Identificador = c.Identificador!, Valor = c.Valor! }).ToList()
    };

    private static IReadOnlyList<string> Falhas(OpcoesJwt opcoes)
    {
        var resultado = Validador.Validate(null, opcoes);
        return resultado.Failed ? resultado.Failures.ToList() : [];
    }

    [Fact]
    public void Uma_chave_valida_e_aceita()
    {
        Falhas(Opcoes("a", ("a", ValorA))).Should().BeEmpty();
    }

    [Fact]
    public void Tres_chaves_validas_sao_aceitas()
    {
        Falhas(Opcoes("2026-12-jwt_b", ("2026-11-jwt.a", ValorA), ("2026-12-jwt_b", ValorB), ("C", ValorC))).Should().BeEmpty();
    }

    [Fact]
    public void Configuracao_antiga_falha_sozinha()
    {
        var opcoes = new OpcoesJwt { Emissor = "e", Audiencia = "a", ChaveAssinatura = ValorA };

        var falhas = Falhas(opcoes);

        falhas.Should().Contain("Jwt:ChaveAssinatura não é mais aceita. Configure Jwt:Chaves e Jwt:ChaveAtiva (docs/rotacao-chave-jwt.md).");
        falhas.Should().Contain("Jwt:Chaves deve ter ao menos uma chave.");
        falhas.Should().Contain("Jwt:ChaveAtiva é obrigatória.");
        falhas.Should().NotContain(f => f.Contains(ValorA));
    }

    [Fact]
    public void Configuracao_antiga_falha_mesmo_com_o_formato_novo()
    {
        var opcoes = Opcoes("a", ("a", ValorA));
        opcoes.ChaveAssinatura = ValorB;

        Falhas(opcoes).Should().Equal(
            "Jwt:ChaveAssinatura não é mais aceita. Configure Jwt:Chaves e Jwt:ChaveAtiva (docs/rotacao-chave-jwt.md).");
    }

    [Fact]
    public void Mais_de_tres_chaves_falha()
    {
        Falhas(Opcoes("a", ("a", ValorA), ("b", ValorB), ("c", ValorC), ("d", ValorD)))
            .Should().Equal("Jwt:Chaves aceita no máximo 3 chaves.");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("com espaço")]
    [InlineData("acentuação")]
    [InlineData("dois:pontos")]
    [InlineData("quebra\nde-linha")]
    public void Identificador_fora_do_formato_falha_citando_so_a_posicao(string? identificador)
    {
        var falhas = Falhas(Opcoes("a", ("a", ValorA), (identificador, ValorB)));

        falhas.Should().Equal(
            "Jwt:Chaves:1:Identificador deve ter de 1 a 64 caracteres entre letras sem acento, dígitos, '-', '_' e '.'.");
    }

    [Fact]
    public void Identificador_com_65_caracteres_falha_e_com_64_e_aceito()
    {
        var com64 = new string('x', 64);

        Falhas(Opcoes(com64, (com64, ValorA))).Should().BeEmpty();
        Falhas(Opcoes("a", ("a", ValorA), (com64 + "x", ValorB))).Should().Equal(
            "Jwt:Chaves:1:Identificador deve ter de 1 a 64 caracteres entre letras sem acento, dígitos, '-', '_' e '.'.");
    }

    [Fact]
    public void Identificador_repetido_falha_uma_vez_citando_o_identificador()
    {
        Falhas(Opcoes("a", ("a", ValorA), ("a", ValorB), ("a", ValorC)))
            .Should().Equal("Jwt:Chaves: o identificador 'a' está repetido.");
    }

    [Fact]
    public void Identificadores_que_diferem_so_em_maiusculas_sao_distintos()
    {
        Falhas(Opcoes("chave", ("chave", ValorA), ("CHAVE", ValorB))).Should().BeEmpty();
    }

    [Fact]
    public void Valor_com_31_bytes_falha_sem_mostrar_o_valor()
    {
        var curto = new string('k', 31);

        var falhas = Falhas(Opcoes("a", ("a", curto)));

        falhas.Should().Equal("Jwt:Chaves: a chave 'a' deve ter ao menos 32 bytes.");
        falhas.Should().NotContain(f => f.Contains(curto));
    }

    [Fact]
    public void Valor_vazio_de_identificador_invalido_falha_citando_a_posicao()
    {
        Falhas(Opcoes("a", ("a", ValorA), ("x y", null))).Should().Equal(
            "Jwt:Chaves:1:Identificador deve ter de 1 a 64 caracteres entre letras sem acento, dígitos, '-', '_' e '.'.",
            "Jwt:Chaves:1:Valor deve ter ao menos 32 bytes.");
    }

    [Fact]
    public void Tamanho_e_medido_em_bytes_utf8()
    {
        // 31 caracteres, mas 32 bytes em UTF-8 por causa do "ç".
        var comAcento = "ç" + new string('k', 30);

        Falhas(Opcoes("a", ("a", comAcento))).Should().BeEmpty();
    }

    [Fact]
    public void Chave_ativa_vazia_falha()
    {
        Falhas(Opcoes("", ("a", ValorA))).Should().Equal("Jwt:ChaveAtiva é obrigatória.");
    }

    [Fact]
    public void Chave_ativa_fora_da_lista_falha_citando_o_identificador()
    {
        Falhas(Opcoes("b", ("a", ValorA))).Should().Equal("Jwt:ChaveAtiva 'b' não está entre as chaves de Jwt:Chaves.");
    }

    [Fact]
    public void Chave_ativa_com_outra_caixa_nao_corresponde()
    {
        Falhas(Opcoes("A", ("a", ValorA))).Should().Equal("Jwt:ChaveAtiva 'A' não está entre as chaves de Jwt:Chaves.");
    }

    [Fact]
    public void Chave_ativa_fora_do_formato_nao_e_citada()
    {
        Falhas(Opcoes(ValorB + " colado", ("a", ValorA)))
            .Should().Equal("Jwt:ChaveAtiva deve ser o identificador de uma das chaves de Jwt:Chaves.");
    }

    [Fact]
    public void Emissor_e_audiencia_sao_obrigatorios()
    {
        var opcoes = Opcoes("a", ("a", ValorA));
        opcoes.Emissor = "";

        Falhas(opcoes).Should().Equal("Jwt:Emissor e Jwt:Audiencia são obrigatórios.");
    }

    [Fact]
    public void Todas_as_falhas_saem_juntas_e_sem_valores()
    {
        var curto = "curto-demais";
        var opcoes = Opcoes("z", ("a", curto), ("a", ValorB), ("x y", ValorC));
        opcoes.Audiencia = "";

        var falhas = Falhas(opcoes);

        falhas.Should().HaveCount(5);
        falhas.Should().NotContain(f => f.Contains(curto) || f.Contains(ValorB) || f.Contains(ValorC));
    }
}
