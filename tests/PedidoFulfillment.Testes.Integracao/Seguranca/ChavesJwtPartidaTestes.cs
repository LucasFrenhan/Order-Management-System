using FluentAssertions;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Seguranca;

/// <summary>
/// A API não sobe com configuração de chaves inválida ou antiga, e a falha não mostra o valor de nenhuma chave
/// (REQ-27, US3, FR-005, FR-007, FR-008, SC-004; contracts/configuracao.md).
/// </summary>
public sealed class ChavesJwtPartidaTestes
{
    private const string ValorA = "valor-secreto-da-chave-a-com-mais-de-32-bytes";
    private const string ValorB = "valor-secreto-da-chave-b-com-mais-de-32-bytes";

    public static TheoryData<string, Dictionary<string, string?>, string, string> Invalidas => new()
    {
        {
            "configuração antiga", new() { ["Jwt:ChaveAssinatura"] = ValorA },
            "Jwt:ChaveAssinatura não é mais aceita. Configure Jwt:Chaves e Jwt:ChaveAtiva (docs/rotacao-chave-jwt.md).", ValorA
        },
        {
            "quatro chaves", new()
            {
                ["Jwt:Chaves:1:Identificador"] = "b", ["Jwt:Chaves:1:Valor"] = ValorB,
                ["Jwt:Chaves:2:Identificador"] = "c", ["Jwt:Chaves:2:Valor"] = ValorB + "c",
                ["Jwt:Chaves:3:Identificador"] = "d", ["Jwt:Chaves:3:Valor"] = ValorB + "d"
            },
            "Jwt:Chaves aceita no máximo 3 chaves.", ValorB
        },
        {
            "identificador repetido", new() { ["Jwt:Chaves:1:Identificador"] = FabricaApi.IdentificadorChaveTeste, ["Jwt:Chaves:1:Valor"] = ValorB },
            $"Jwt:Chaves: o identificador '{FabricaApi.IdentificadorChaveTeste}' está repetido.", ValorB
        },
        {
            "valor curto", new() { ["Jwt:Chaves:1:Identificador"] = "b", ["Jwt:Chaves:1:Valor"] = "curto-demais-para-assinar" },
            "Jwt:Chaves: a chave 'b' deve ter ao menos 32 bytes.", "curto-demais-para-assinar"
        },
        {
            "ativa fora da lista", new() { ["Jwt:ChaveAtiva"] = "outra" },
            "Jwt:ChaveAtiva 'outra' não está entre as chaves de Jwt:Chaves.", FabricaApi.ChaveTeste
        },
        {
            "chave do cursor curta", new() { ["Listagens:ChaveCursor"] = "cursor-curto" },
            "Listagens:ChaveCursor deve ter ao menos 32 bytes.", "cursor-curto"
        }
    };

    [Theory]
    [MemberData(nameof(Invalidas))]
    public async Task Configuracao_invalida_impede_a_partida_sem_mostrar_o_valor(
        string caso, Dictionary<string, string?> configuracoes, string mensagem, string valor)
    {
        await using var fabrica = new FabricaApi { Configuracoes = configuracoes };

        var lancada = Record.Exception(() => fabrica.CreateClient());

        lancada.Should().NotBeNull(caso);
        var cadeia = Cadeia(lancada!).ToArray();
        cadeia.OfType<OptionsValidationException>().SelectMany(e => e.Failures).Should().Contain(mensagem, caso);
        cadeia.Select(e => e.Message).Should().NotContain(m => m.Contains(valor), caso);
        lancada!.ToString().Should().NotContain(valor, caso);
    }

    /// <summary>A validação na partida lança direto, às vezes dentro de um AggregateException (uma por opção).</summary>
    private static IEnumerable<Exception> Cadeia(Exception excecao)
    {
        yield return excecao;
        var internas = excecao is AggregateException agregada ? agregada.InnerExceptions : excecao.InnerException is { } interna ? [interna] : [];
        foreach (var item in internas.SelectMany(Cadeia))
        {
            yield return item;
        }
    }
}
