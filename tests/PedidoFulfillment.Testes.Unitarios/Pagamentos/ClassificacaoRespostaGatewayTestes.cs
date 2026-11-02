using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using PedidoFulfillment.Application.Pagamentos;
using PedidoFulfillment.Infrastructure.Pagamentos;
using Polly;
using Polly.Timeout;

namespace PedidoFulfillment.Testes.Unitarios.Pagamentos;

/// <summary>Tabela de classificação do cliente do gateway (REQ-19, FR-010, research R-07; contrato do cliente).</summary>
public sealed class ClassificacaoRespostaGatewayTestes
{
    private static Outcome<HttpResponseMessage> Resposta(HttpStatusCode status, bool retryAfter = false)
    {
        var resposta = new HttpResponseMessage(status);
        if (retryAfter)
        {
            resposta.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1));
        }

        return Outcome.FromResult(resposta);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, false, (int)ClasseResposta.Resultado)]
    [InlineData(HttpStatusCode.Created, false, (int)ClasseResposta.Resultado)]
    [InlineData(HttpStatusCode.NotFound, false, (int)ClasseResposta.NaoEncontrada)]
    [InlineData(HttpStatusCode.ServiceUnavailable, false, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.InternalServerError, false, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.BadGateway, false, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.GatewayTimeout, false, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.TooManyRequests, true, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.Conflict, true, (int)ClasseResposta.Passageira)]
    [InlineData(HttpStatusCode.Conflict, false, (int)ClasseResposta.Permanente)]
    [InlineData(HttpStatusCode.BadRequest, false, (int)ClasseResposta.Permanente)]
    [InlineData(HttpStatusCode.Unauthorized, false, (int)ClasseResposta.Permanente)]
    [InlineData(HttpStatusCode.Forbidden, false, (int)ClasseResposta.Permanente)]
    [InlineData(HttpStatusCode.MovedPermanently, false, (int)ClasseResposta.Permanente)]
    public void Cada_status_tem_a_sua_classe(HttpStatusCode status, bool retryAfter, int esperada)
    {
        ClassificacaoRespostaGateway.Classificar(status, retryAfter).Should().Be((ClasseResposta)esperada);
    }

    [Fact]
    public void Repete_as_passageiras_o_tempo_esgotado_e_a_falha_de_conexao_mas_nao_resultado_nem_permanente()
    {
        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.ServiceUnavailable)).Should().BeTrue();
        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.Conflict, retryAfter: true)).Should().BeTrue();
        ClassificacaoRespostaGateway.Repetir(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())).Should().BeTrue();
        ClassificacaoRespostaGateway.Repetir(Outcome.FromException<HttpResponseMessage>(new HttpRequestException("recusada"))).Should().BeTrue();

        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.Created)).Should().BeFalse();
        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.NotFound)).Should().BeFalse();
        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.Conflict)).Should().BeFalse();
        ClassificacaoRespostaGateway.Repetir(Resposta(HttpStatusCode.Unauthorized)).Should().BeFalse();
        ClassificacaoRespostaGateway.Repetir(Outcome.FromException<HttpResponseMessage>(new InvalidOperationException())).Should().BeFalse();
    }

    [Fact]
    public void Para_o_circuito_contam_as_passageiras_menos_a_operacao_em_andamento()
    {
        ClassificacaoRespostaGateway.ContaParaCircuito(Resposta(HttpStatusCode.ServiceUnavailable)).Should().BeTrue();
        ClassificacaoRespostaGateway.ContaParaCircuito(Resposta(HttpStatusCode.TooManyRequests, retryAfter: true)).Should().BeTrue();
        ClassificacaoRespostaGateway.ContaParaCircuito(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())).Should().BeTrue();
        ClassificacaoRespostaGateway.ContaParaCircuito(Outcome.FromException<HttpResponseMessage>(new HttpRequestException())).Should().BeTrue();

        ClassificacaoRespostaGateway.ContaParaCircuito(Resposta(HttpStatusCode.Conflict, retryAfter: true)).Should().BeFalse("a API respondeu");
        ClassificacaoRespostaGateway.ContaParaCircuito(Resposta(HttpStatusCode.Created)).Should().BeFalse("recusa de negócio também é 201");
        ClassificacaoRespostaGateway.ContaParaCircuito(Resposta(HttpStatusCode.Unauthorized)).Should().BeFalse("erro permanente não conta");
    }

    [Fact]
    public void O_tipo_da_falha_e_o_resultado_incerto()
    {
        ClassificacaoRespostaGateway.TipoFalha(Resposta(HttpStatusCode.ServiceUnavailable)).Should().Be("indisponivel");
        ClassificacaoRespostaGateway.TipoFalha(Resposta(HttpStatusCode.TooManyRequests)).Should().Be("limite-taxa");
        ClassificacaoRespostaGateway.TipoFalha(Resposta(HttpStatusCode.InternalServerError)).Should().Be("erro-interno");
        ClassificacaoRespostaGateway.TipoFalha(Resposta(HttpStatusCode.Conflict, retryAfter: true)).Should().Be("em-andamento");
        ClassificacaoRespostaGateway.TipoFalha(Outcome.FromException<HttpResponseMessage>(new HttpRequestException())).Should().Be("conexao");
        ClassificacaoRespostaGateway.Incerto(Outcome.FromException<HttpResponseMessage>(new TimeoutRejectedException())).Should().BeTrue();
        ClassificacaoRespostaGateway.Incerto(Resposta(HttpStatusCode.ServiceUnavailable)).Should().BeFalse();
    }

    [Fact]
    public void Le_os_resultados_do_contrato()
    {
        ClassificacaoRespostaGateway.LerCobranca(new("aprovada", "PAG-1", null)).Should().Be(ResultadoCobranca.Aprovada("PAG-1"));
        ClassificacaoRespostaGateway.LerCobranca(new("recusada", null, "Pagamento recusado pelo emissor."))
            .Should().Be(ResultadoCobranca.Recusada("Pagamento recusado pelo emissor."));
        ClassificacaoRespostaGateway.LerEstorno(new("confirmado", "EST-1", null)).Should().Be(ResultadoEstorno.Confirmar("EST-1"));
        ClassificacaoRespostaGateway.LerEstorno(new("recusado", null, "Cobrança desconhecida.")).Should().Be(ResultadoEstorno.Recusar("Cobrança desconhecida."));
        ClassificacaoRespostaGateway.LerConsulta(new("aprovada", "PAG-1", null)).Should().Be(ConsultaCobranca.Aprovada("PAG-1"));
        ClassificacaoRespostaGateway.LerConsulta(new("recusada", null, "x")).Situacao.Should().Be(SituacaoConsultaCobranca.Recusada);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("aprovada", null, null)]
    [InlineData("aprovada", "", null)]
    [InlineData("recusada", null, null)]
    [InlineData("confirmado", "EST-1", null)]
    [InlineData("pendente", "PAG-1", null)]
    public void Corpo_fora_do_contrato_e_erro_permanente(string? situacao, string? identificador, string? motivo)
    {
        var ler = () => ClassificacaoRespostaGateway.LerCobranca(new CorpoOperacaoGateway(situacao, identificador, motivo));

        ler.Should().Throw<FalhaGatewayPagamentoException>().Which.Tipo.Should().Be(TipoFalhaGateway.Permanente);
        FluentActions.Invoking(() => ClassificacaoRespostaGateway.LerCobranca(null)).Should().Throw<FalhaGatewayPagamentoException>();
    }
}
