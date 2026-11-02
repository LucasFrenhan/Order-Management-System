using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.PagamentosSimulados.Operacoes;

namespace PedidoFulfillment.PagamentosSimulados.Simulacao;

/// <summary>Corpo de <c>PUT /controle/comportamento</c>: só os campos informados mudam.</summary>
public sealed record CorpoComportamento(
    int? LatenciaMinimaMs,
    int? LatenciaMaximaMs,
    double? CaudaPercentual,
    int? CaudaMinimoMs,
    int? CaudaMaximoMs,
    double? TaxaAprovacaoPercentual,
    double? IndisponivelPercentual,
    double? LimiteTaxaPercentual,
    int? RetryAfterSegundos,
    double? ErroInternoPercentual,
    double? SemRespostaPercentual,
    double? ProcessarSemResponderPercentual,
    int? SemRespostaMs);

public sealed record CorpoModo(string? Modo);

public sealed record CorpoRoteiro(IReadOnlyList<string>? Comportamentos);

/// <summary>Situação de uma operação guardada, para os testes conferirem as efetivações (SC-002).</summary>
public sealed record RespostaOperacaoControle(string Situacao, int Efetivacoes, int Chamadas);

/// <summary>Comportamento em vigor, devolvido por <c>GET /controle/comportamento</c>.</summary>
public sealed record RespostaComportamento(OpcoesSimulacao Parametros, string? Modo, int TamanhoRoteiro, long ChamadasRecebidas);

/// <summary>
/// Rota de controle (REQ-19, FR-007a; contrato › Controle), protegida pela chave de controle
/// (<see cref="Seguranca.AutenticacaoChaves"/>). Muda o comportamento com a API no ar.
/// </summary>
internal static class EndpointsControle
{
    public static void MapearControle(this IEndpointRouteBuilder rotas)
    {
        var controle = rotas.MapGroup("/controle");

        controle.MapGet("/comportamento", (ControleSimulacao simulacao) => Results.Ok(Situacao(simulacao)));

        controle.MapPut("/comportamento", (CorpoComportamento corpo, ControleSimulacao simulacao, ILogger<ControleSimulacao> logger) =>
        {
            var erros = simulacao.Alterar(parametros => Aplicar(corpo, parametros));
            if (erros.Count > 0)
            {
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["comportamento"] = [.. erros] });
            }

            Logs.ComportamentoAlterado(logger, "chances e latência");
            return Results.Ok(Situacao(simulacao));
        });

        controle.MapPut("/modo", (CorpoModo corpo, ControleSimulacao simulacao, ILogger<ControleSimulacao> logger) =>
        {
            if (!NomesComportamento.TentarLer(corpo.Modo, out var modo))
            {
                return Invalido("modo");
            }

            simulacao.ForcarModo(modo);
            Logs.ComportamentoAlterado(logger, $"modo forçado {modo.Nome()}");
            return Results.NoContent();
        });

        controle.MapDelete("/modo", (ControleSimulacao simulacao, ILogger<ControleSimulacao> logger) =>
        {
            simulacao.DesfazerModo();
            Logs.ComportamentoAlterado(logger, "modo forçado desfeito");
            return Results.NoContent();
        });

        controle.MapPost("/roteiro", (CorpoRoteiro corpo, ControleSimulacao simulacao, ILogger<ControleSimulacao> logger) =>
        {
            var comportamentos = new List<Comportamento>();
            foreach (var nome in corpo.Comportamentos ?? [])
            {
                if (!NomesComportamento.TentarLer(nome, out var comportamento))
                {
                    return Invalido("comportamentos");
                }

                comportamentos.Add(comportamento);
            }

            if (comportamentos.Count == 0)
            {
                return Invalido("comportamentos");
            }

            simulacao.Enfileirar(comportamentos);
            Logs.ComportamentoAlterado(logger, $"roteiro com mais {comportamentos.Count} comportamento(s)");
            return Results.NoContent();
        });

        controle.MapDelete("/roteiro", (ControleSimulacao simulacao, ILogger<ControleSimulacao> logger) =>
        {
            simulacao.EsvaziarRoteiro();
            Logs.ComportamentoAlterado(logger, "roteiro esvaziado");
            return Results.NoContent();
        });

        controle.MapPost("/restaurar", (ControleSimulacao simulacao, ArmazenamentoOperacoes armazenamento, ILogger<ControleSimulacao> logger) =>
        {
            simulacao.Restaurar();
            armazenamento.Limpar();
            Logs.ComportamentoAlterado(logger, "restaurado à configuração, operações apagadas");
            return Results.NoContent();
        });

        controle.MapGet("/operacoes/{tipo}/{chave:guid}", (string tipo, Guid chave, ArmazenamentoOperacoes armazenamento) =>
        {
            TipoOperacao? lido = tipo switch
            {
                "cobranca" => TipoOperacao.Cobranca,
                "estorno" => TipoOperacao.Estorno,
                _ => null
            };
            if (lido is null)
            {
                return Invalido("tipo");
            }

            return armazenamento.Obter(lido.Value, chave) is { } operacao
                ? Results.Ok(new RespostaOperacaoControle(ServicoPagamentosSimulados.Situacao(operacao), operacao.Efetivacoes, operacao.Chamadas))
                : ServicoPagamentosSimulados.Problema(StatusCodes.Status404NotFound, "Operação não encontrada.", "nao-encontrada");
        });
    }

    private static RespostaComportamento Situacao(ControleSimulacao simulacao) =>
        new(simulacao.Parametros, simulacao.ModoForcado?.Nome(), simulacao.TamanhoRoteiro, simulacao.ChamadasRecebidas);

    private static IResult Invalido(string campo) =>
        Results.ValidationProblem(new Dictionary<string, string[]>
        {
            [campo] = [$"Use um destes comportamentos: {string.Join(", ", NomesComportamento.Todos)}."]
        });

    private static void Aplicar(CorpoComportamento corpo, OpcoesSimulacao parametros)
    {
        parametros.LatenciaMinimaMs = corpo.LatenciaMinimaMs ?? parametros.LatenciaMinimaMs;
        parametros.LatenciaMaximaMs = corpo.LatenciaMaximaMs ?? parametros.LatenciaMaximaMs;
        parametros.CaudaPercentual = corpo.CaudaPercentual ?? parametros.CaudaPercentual;
        parametros.CaudaMinimoMs = corpo.CaudaMinimoMs ?? parametros.CaudaMinimoMs;
        parametros.CaudaMaximoMs = corpo.CaudaMaximoMs ?? parametros.CaudaMaximoMs;
        parametros.TaxaAprovacaoPercentual = corpo.TaxaAprovacaoPercentual ?? parametros.TaxaAprovacaoPercentual;
        parametros.IndisponivelPercentual = corpo.IndisponivelPercentual ?? parametros.IndisponivelPercentual;
        parametros.LimiteTaxaPercentual = corpo.LimiteTaxaPercentual ?? parametros.LimiteTaxaPercentual;
        parametros.RetryAfterSegundos = corpo.RetryAfterSegundos ?? parametros.RetryAfterSegundos;
        parametros.ErroInternoPercentual = corpo.ErroInternoPercentual ?? parametros.ErroInternoPercentual;
        parametros.SemRespostaPercentual = corpo.SemRespostaPercentual ?? parametros.SemRespostaPercentual;
        parametros.ProcessarSemResponderPercentual = corpo.ProcessarSemResponderPercentual ?? parametros.ProcessarSemResponderPercentual;
        parametros.SemRespostaMs = corpo.SemRespostaMs ?? parametros.SemRespostaMs;
    }
}
