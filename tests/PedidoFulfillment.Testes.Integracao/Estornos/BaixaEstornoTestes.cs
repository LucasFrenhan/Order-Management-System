using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Estornos.CenarioAcoesEstornos;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Estornos;

/// <summary>
/// Baixa manual do estorno pelo Admin, com SQL Server real (REQ-28, US2, FR-006 a FR-010a e FR-020).
/// </summary>
[Collection(nameof(ColecaoMensageria))]
public sealed class BaixaEstornoTestes(MensageriaTeste mensageria)
{
    private static CancellationToken Cancelamento => TestContext.Current.CancellationToken;

    private static Task<Guid> IdAdminAsync(string stringConexao) =>
        BancoDadosTeste.ConsultarAsync<Guid>(stringConexao, $"SELECT Id FROM Usuarios WHERE Email = N'{UsuariosIniciaisComBancoTestes.EmailAdmin}'");

    private static async Task<List<(Guid IdNotificacao, string Tipo)>> AvisosAsync(string stringConexao, Guid pedidoId)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var avisos = await contexto.AvisosPedido.AsNoTracking().Where(a => a.PedidoId == pedidoId)
            .Select(a => new { a.IdNotificacao, a.Tipo }).ToListAsync(Cancelamento);
        return [.. avisos.Select(a => (a.IdNotificacao, a.Tipo))];
    }

    [Fact]
    public async Task Admin_da_baixa_com_motivo_e_referencia_e_saem_o_evento_e_o_aviso_sem_pagamento_estornado()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 2);
        var (comReferencia, semReferencia) = (estornos[0], estornos[1]);
        var logs = new CapturaLogs();
        await using var api = Api(stringConexao, logs);
        var admin = await AdminAsync(api);
        var adminId = await IdAdminAsync(stringConexao);

        var baixado = await CorpoOkAsync(await BaixarAsync(admin, comReferencia.EstornoId, $"  {MotivoValido}  ", "  TED-778812  "));
        var semRef = await CorpoOkAsync(await BaixarAsync(admin, semReferencia.EstornoId));

        // US2 cenário 1: situação, autor, motivo e referência aparados.
        baixado.GetProperty("situacao").GetString().Should().Be("Baixado");
        baixado.GetProperty("tentativaForcadaEm").ValueKind.Should().Be(JsonValueKind.Null);
        var baixa = baixado.GetProperty("baixa");
        baixa.GetProperty("autorId").GetGuid().Should().Be(adminId);
        baixa.GetProperty("motivo").GetString().Should().Be(MotivoValido);
        baixa.GetProperty("referenciaExterna").GetString().Should().Be("TED-778812");
        semRef.GetProperty("baixa").GetProperty("referenciaExterna").ValueKind.Should().Be(JsonValueKind.Null);
        IdsDosItens(await CorpoOkAsync(await ListarAsync(admin))).Should().BeEmpty("os dois saíram da fila");

        // US2 cenário 5, FR-010 e FR-010a: o evento da baixa e o aviso, na mesma gravação, sem pagamento.estornado.
        var evento = (await EventosOutbox.ListarAsync<PagamentoEstornoBaixado>(stringConexao, comReferencia.PedidoId))
            .Should().ContainSingle(e => e.Tipo == TiposEvento.PagamentoEstornoBaixado).Subject;
        evento.Dados.EstornoId.Should().Be(comReferencia.EstornoId);
        evento.Dados.AutorId.Should().Be(adminId);
        evento.Dados.ReferenciaExterna.Should().Be("TED-778812");
        (await EventosOutbox.ListarAsync<PagamentoEstornoBaixado>(stringConexao, semReferencia.PedidoId))
            .Should().ContainSingle(e => e.Tipo == TiposEvento.PagamentoEstornoBaixado).Which.Dados.ReferenciaExterna.Should().BeNull();
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = N'{TiposEvento.PagamentoEstornado}'")).Should().Be(0);
        (await AvisosAsync(stringConexao, comReferencia.PedidoId)).Should()
            .Contain((evento.IdEvento, TiposNotificacao.EstornoConfirmado), "o aviso leva o id do evento da baixa");

        // FR-022: 7022 sem o motivo nem a referência.
        var log = logs.Registros.Where(r => r.Evento.Id == 7022).ToList();
        log.Should().HaveCount(2);
        log.Should().NotContain(r => r.Texto.Contains("financeiro", StringComparison.Ordinal) || r.Texto.Contains("TED-", StringComparison.Ordinal));

        // US2 cenário 4: já baixado → 409, nada muda.
        var deNovo = await BaixarAsync(admin, comReferencia.EstornoId);
        deNovo.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(deNovo)).Should().Be("ESTORNO_NAO_PENDENTE");
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = N'{TiposEvento.PagamentoEstornoBaixado}'")).Should().Be(2);
    }

    [Fact]
    public async Task Baixa_invalida_de_estorno_confirmado_por_perfil_errado_ou_por_admin_desativado_e_recusada()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 2);
        var (pendente, confirmado) = (estornos[0], estornos[1]);
        await ConfirmadoAsync(stringConexao, confirmado.EstornoId);
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);
        var operador = await OperadorAsync(api);
        var (cliente, _, _) = await ClienteAsync(api);

        // US2 cenário 3: validação.
        foreach (var (motivo, referencia, campo) in new (string?, string?, string)[]
                 {
                     (null, null, "motivo"), ("   ", null, "motivo"), ("  123456789  ", null, "motivo"),
                     (new string('m', 501), null, "motivo"), (MotivoValido, new string('r', 101), "referenciaExterna")
                 })
        {
            var resposta = await BaixarAsync(admin, pendente.EstornoId, motivo, referencia);
            resposta.StatusCode.Should().Be(HttpStatusCode.BadRequest, campo);
            (await CorpoAsync(resposta)).GetProperty("erros").EnumerateObject()
                .Should().Contain(p => p.Name.Equals(campo, StringComparison.OrdinalIgnoreCase));
        }

        // US2 cenário 4: confirmado pelo gateway → 409. Id desconhecido → 404.
        var deConfirmado = await BaixarAsync(admin, confirmado.EstornoId);
        deConfirmado.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await CodigoAsync(deConfirmado)).Should().Be("ESTORNO_NAO_PENDENTE");
        (await BaixarAsync(admin, Guid.CreateVersion7())).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // US2 cenário 6: operador, cliente e anônimo.
        (await BaixarAsync(operador, pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BaixarAsync(cliente, pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await BaixarAsync(api.CreateClient(), pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // US2 cenário 7: Admin desativado com o token ainda válido.
        await BancoDadosTeste.ExecutarAsync(stringConexao,
            $"UPDATE Usuarios SET DesativadoEm = SYSDATETIMEOFFSET() WHERE Email = N'{UsuariosIniciaisComBancoTestes.EmailAdmin}'");
        (await BaixarAsync(admin, pendente.EstornoId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        (await CorpoOkAsync(await ObterAsync(operador, pendente.EstornoId))).GetProperty("situacao").GetString()
            .Should().Be("Pendente", "nada foi gravado");
    }

    [Fact]
    public async Task Duas_baixas_ao_mesmo_tempo_gravam_uma_so()
    {
        Assert.SkipUnless(mensageria.DockerDisponivel, "Docker indisponível");
        var (stringConexao, estornos) = await PendentesAsync(mensageria, 1);
        await using var api = Api(stringConexao);
        var admin = await AdminAsync(api);

        var respostas = await Task.WhenAll(BaixarAsync(admin, estornos[0].EstornoId), BaixarAsync(admin, estornos[0].EstornoId));

        respostas.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
        (await BancoDadosTeste.ConsultarAsync<int>(stringConexao,
            $"SELECT COUNT(*) FROM EventosSaida WHERE Tipo = N'{TiposEvento.PagamentoEstornoBaixado}'")).Should().Be(1);
    }
}
