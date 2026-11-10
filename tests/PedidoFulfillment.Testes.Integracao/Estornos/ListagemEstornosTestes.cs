using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioAcoesEstornos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Listagem e consulta dos estornos pela operação, com SQL Server real (REQ-28, US1, FR-001 a FR-005 e
/// FR-019).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class ListagemEstornosTestes(MensageriaTeste mensageria)
{
    private static readonly DateTimeOffset Base = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Lista_so_os_pendentes_do_mais_antigo_ao_mais_novo_com_tentativas_erro_alerta_e_paginas()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 4);
        var (a, b, c, confirmado) = (estornos[0], estornos[1], estornos[2], estornos[3]);
        // C é o mais antigo, depois A, depois B: a ordem não é a de criação.
        await SolicitadoEmAsync(stringConexao, c.EstornoId, Base);
        await SolicitadoEmAsync(stringConexao, a.EstornoId, Base.AddMinutes(1));
        await SolicitadoEmAsync(stringConexao, b.EstornoId, Base.AddMinutes(2));
        await ConfirmadoAsync(stringConexao, confirmado.EstornoId);
        // C passou do limite (mais de 24 h e 3 falhas); A tem 2 falhas, a última uma recusa.
        await AjustarAsync(stringConexao, c.EstornoId, "TentativasEstorno = 3, UltimaFalhaEstornoEm = SYSDATETIMEOFFSET(), UltimoErroEstorno = N'Falha passageira do gateway.'");
        await AjustarAsync(stringConexao, a.EstornoId, "TentativasEstorno = 2, UltimaFalhaEstornoEm = SYSDATETIMEOFFSET(), UltimoErroEstorno = N'Recusado: Cobrança desconhecida.'");
        await using var api = Api(stringConexao);
        var operador = await OperadorAsync(api);

        var pagina = await CorpoOkAsync(await ListarAsync(operador));

        IdsDosItens(pagina).Should().Equal(c.EstornoId, a.EstornoId, b.EstornoId);
        var itens = pagina.GetProperty("itens").EnumerateArray().ToList();
        itens[0].GetProperty("alemDoLimiteAlerta").GetBoolean().Should().BeTrue("US1 cenário 3");
        itens[1].GetProperty("tentativas").GetInt32().Should().Be(2);
        itens[1].GetProperty("ultimoErro").GetString().Should().Be("Recusado: Cobrança desconhecida.");
        itens[1].GetProperty("ultimaFalhaEm").ValueKind.Should().Be(JsonValueKind.String);
        itens[1].GetProperty("alemDoLimiteAlerta").GetBoolean().Should().BeFalse("só 2 falhas");
        itens[1].GetProperty("pedidoId").GetGuid().Should().Be(a.PedidoId);
        itens[1].GetProperty("numero").GetString().Should().MatchRegex(@"^PED-\d{8}-\d{6}$");
        itens[1].GetProperty("situacao").GetString().Should().Be("Pendente");
        itens[2].GetProperty("ultimoErro").ValueKind.Should().Be(JsonValueKind.Null);
        var texto = pagina.GetRawText();
        texto.Should().NotContain("identificadorTransacao").And.NotContain("identificadorEstorno").And.NotContain("SIM-");
        pagina.GetProperty("proximo").ValueKind.Should().Be(JsonValueKind.Null);

        // US1 cenário 4: duas páginas, e o anterior volta à primeira.
        var primeira = await CorpoOkAsync(await ListarAsync(operador, "?tamanho=2"));
        IdsDosItens(primeira).Should().Equal(c.EstornoId, a.EstornoId);
        primeira.GetProperty("anterior").ValueKind.Should().Be(JsonValueKind.Null);
        var segunda = await CorpoOkAsync(await ListarAsync(operador, $"?tamanho=2&cursor={Uri.EscapeDataString(primeira.GetProperty("proximo").GetString()!)}"));
        IdsDosItens(segunda).Should().Equal(b.EstornoId);
        segunda.GetProperty("proximo").ValueKind.Should().Be(JsonValueKind.Null);
        var volta = await CorpoOkAsync(await ListarAsync(operador, $"?tamanho=2&cursor={Uri.EscapeDataString(segunda.GetProperty("anterior").GetString()!)}"));
        IdsDosItens(volta).Should().Equal(c.EstornoId, a.EstornoId);

        // O cursor é de quem o recebeu (REQ-21): o Admin não o usa.
        var admin = await AdminAsync(api);
        (await ListarAsync(admin, $"?cursor={Uri.EscapeDataString(primeira.GetProperty("proximo").GetString()!)}")).StatusCode
            .Should().Be(HttpStatusCode.BadRequest);
        (await ListarAsync(admin)).StatusCode.Should().Be(HttpStatusCode.OK, "FR-019: o Admin também lista");
    }

    [Fact]
    public async Task Consulta_mostra_o_estorno_em_qualquer_situacao_e_recusa_o_que_nao_e_estorno()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 3);
        var (pendente, confirmado, baixado) = (estornos[0], estornos[1], estornos[2]);
        var autor = Guid.CreateVersion7();
        await ConfirmadoAsync(stringConexao, confirmado.EstornoId);
        await BaixadoAsync(stringConexao, baixado.EstornoId, autor);
        await using var api = Api(stringConexao);
        var operador = await OperadorAsync(api);

        var lidoPendente = await CorpoOkAsync(await ObterAsync(operador, pendente.EstornoId));
        var lidoConfirmado = await CorpoOkAsync(await ObterAsync(operador, confirmado.EstornoId));
        var lidoBaixado = await CorpoOkAsync(await ObterAsync(operador, baixado.EstornoId));

        lidoPendente.GetProperty("situacao").GetString().Should().Be("Pendente");
        lidoPendente.GetProperty("baixa").ValueKind.Should().Be(JsonValueKind.Null);
        lidoConfirmado.GetProperty("situacao").GetString().Should().Be("Confirmado");
        lidoConfirmado.GetProperty("confirmadoEm").ValueKind.Should().Be(JsonValueKind.String);
        lidoConfirmado.GetRawText().Should().NotContain("SIM-EST", "o identificador do gateway não sai");
        lidoBaixado.GetProperty("situacao").GetString().Should().Be("Baixado");
        var baixa = lidoBaixado.GetProperty("baixa");
        baixa.GetProperty("autorId").GetGuid().Should().Be(autor);
        baixa.GetProperty("motivo").GetString().Should().Be(MotivoValido);
        baixa.GetProperty("referenciaExterna").GetString().Should().Be("TED-1");
        IdsDosItens(await CorpoOkAsync(await ListarAsync(operador))).Should().Equal(pendente.EstornoId);

        var desconhecido = await ObterAsync(operador, Guid.CreateVersion7());
        desconhecido.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await CorpoAsync(desconhecido)).GetProperty("title").GetString().Should().Be("Estorno não encontrado.");
        (await ObterAsync(operador, "nao-e-guid")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Lista_vazia_parametros_invalidos_e_acesso()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var stringConexao = mensageria.NovoBanco();
        await using var api = Api(stringConexao);
        var operador = await OperadorAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);
        var anonimo = api.CreateClient();

        var vazia = await CorpoOkAsync(await ListarAsync(operador));
        vazia.GetProperty("itens").GetArrayLength().Should().Be(0);
        vazia.GetProperty("tamanho").GetInt32().Should().Be(20);

        foreach (var consulta in new[] { "?tamanho=0", "?tamanho=101", "?tamanho=abc" })
        {
            var resposta = await ListarAsync(operador, consulta);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, consulta);
            (await CorpoAsync(resposta)).GetProperty("erros").TryGetProperty("tamanho", out _).Should().BeTrue();
        }

        var adulterado = await ListarAsync(operador, "?cursor=abc");
        adulterado.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await CorpoAsync(adulterado)).GetProperty("erros").TryGetProperty("cursor", out _).Should().BeTrue();

        (await ListarAsync(anonimo)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ObterAsync(anonimo, Guid.CreateVersion7())).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ListarAsync(cliente)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ObterAsync(cliente, Guid.CreateVersion7())).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
