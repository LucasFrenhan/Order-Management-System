using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Notificacoes;
using PedidoFulfillment.Application.Notificacoes;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Infrastructure.Notificacoes;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas das notificações de mudança de status (REQ-18, <c>contracts/api-notificacoes.md</c>). Só
/// roteiam: validam, delegam ao serviço e traduzem o resultado.
/// </summary>
public static class EndpointsNotificacoes
{
    public static WebApplication MapearEndpointsNotificacoes(this WebApplication app)
    {
        var destinos = app.MapGroup("/api/v1/notificacoes/destinos").WithTags("Notificacoes")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaDestinosNotificacao);

        destinos.MapPost("", async (
                CadastrarDestinoRequestDTO destino,
                ClaimsPrincipal usuario,
                IServicoDestinosNotificacao servico,
                CancellationToken cancellationToken) =>
                (await servico.CadastrarAsync(usuario.ObterUsuarioId(), usuario.ObterPerfis(), destino, cancellationToken))
                    .ParaResposta(criado => TypedResults.Created($"/api/v1/notificacoes/destinos/{criado.Id}", criado)))
            .WithName("CadastrarDestinoNotificacao")
            .WithSummary("Cadastra um destino de webhook; o segredo de assinatura aparece só nesta resposta.")
            .AddEndpointFilter<FiltroValidacao<CadastrarDestinoRequestDTO>>()
            .Produces<DestinoCadastradoResponseDTO>(StatusCodes.Status201Created)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict);

        destinos.MapGet("", async (ClaimsPrincipal usuario, IServicoDestinosNotificacao servico, CancellationToken cancellationToken) =>
                (await servico.ListarAsync(usuario.ObterUsuarioId(), usuario.ObterPerfis(), cancellationToken))
                    .ParaResposta(lista => TypedResults.Ok(lista)))
            .WithName("ListarDestinosNotificacao")
            .WithSummary("Lista os destinos do usuário: os do Cliente, ou os globais para o Admin.")
            .Produces<IReadOnlyList<DestinoNotificacaoResponseDTO>>();

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (REQ-05, R-10).
        destinos.MapDelete("/{id}", async (Guid id, ClaimsPrincipal usuario, IServicoDestinosNotificacao servico, CancellationToken cancellationToken) =>
                (await servico.RemoverAsync(usuario.ObterUsuarioId(), usuario.ObterPerfis(), id, cancellationToken))
                    .ParaResposta(_ => TypedResults.NoContent()))
            .WithName("RemoverDestinoNotificacao")
            .WithSummary("Remove um destino; as entregas pendentes dele são descartadas.")
            .Produces(StatusCodes.Status204NoContent)
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        destinos.MapPost("/{id}/reativacao", async (Guid id, ClaimsPrincipal usuario, IServicoDestinosNotificacao servico, CancellationToken cancellationToken) =>
                (await servico.ReativarAsync(usuario.ObterUsuarioId(), usuario.ObterPerfis(), id, cancellationToken))
                    .ParaResposta(destino => TypedResults.Ok(destino)))
            .WithName("ReativarDestinoNotificacao")
            .WithSummary("Reativa um destino desativado e zera as falhas; as entregas esgotadas não são reenviadas.")
            .Produces<DestinoNotificacaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        destinos.MapGet("/{id}/entregas", async (
                Guid id,
                ClaimsPrincipal usuario,
                IServicoDestinosNotificacao servico,
                CancellationToken cancellationToken,
                int limite = ServicoDestinosNotificacao.LimiteEntregasPadrao) =>
                (await servico.ListarEntregasAsync(usuario.ObterUsuarioId(), usuario.ObterPerfis(), id, limite, cancellationToken))
                    .ParaResposta(entregas => TypedResults.Ok(entregas)))
            .WithName("ListarEntregasNotificacao")
            .WithSummary("Lista as últimas entregas do destino, da mais nova para a mais antiga.")
            .Produces<IReadOnlyList<EntregaNotificacaoResponseDTO>>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        app.MapGet("/api/v1/notificacoes/eventos", async (
                ClaimsPrincipal usuario,
                IServicoPedidos pedidos,
                FluxoEventosTempoReal fluxo,
                [FromHeader(Name = "Last-Event-ID")] string? ultimoEvento,
                CancellationToken cancellationToken,
                Guid? pedidoId = null) =>
            {
                var perfis = usuario.ObterPerfis();
                if (pedidoId is { } id && (await pedidos.ObterAsync(id, usuario.ObterUsuarioId(), perfis, cancellationToken)).Falha is { } falha)
                {
                    return falha.ParaProblema();
                }

                var abertura = fluxo.Abrir(usuario.ObterUsuarioId(), EhOperacao(perfis), pedidoId, ultimoEvento, usuario.ObterExpiracao());
                return abertura.LimiteAtingido
                    ? new Falha(TipoFalha.Conflito, "Limite de conexões atingido.", $"O limite de {abertura.Limite} conexões de eventos foi atingido.",
                        CodigosErro.NotificacaoSseLimiteConexoes).ParaProblema(StatusCodes.Status429TooManyRequests)
                    : TypedResults.ServerSentEvents(abertura.Eventos!);
            })
            .WithTags("Notificacoes")
            .WithName("EventosNotificacao")
            .WithSummary("Abre a conexão de eventos (SSE) com os avisos dos pedidos que o usuário pode consultar.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaConsultaPedido)
            .Produces(StatusCodes.Status200OK, contentType: "text/event-stream")
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status429TooManyRequests);

        return app;
    }

    private static bool EhOperacao(IReadOnlyCollection<PerfilUsuario> perfis) =>
        perfis.Contains(PerfilUsuario.Admin) || perfis.Contains(PerfilUsuario.OperadorLogistica);
}
