using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Usuarios;
using PedidoFulfillment.Application.Usuarios;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Gestão de usuários e perfis (REQ-25). Todas as rotas exigem um Admin ativo, conferido no banco a
/// cada chamada (R-02), e ficam fora do limite de requisições (FR-023). Só roteiam: validam, delegam
/// ao serviço e traduzem o resultado.
/// </summary>
public static class EndpointsUsuarios
{
    public static WebApplication MapearEndpointsUsuarios(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/admin/usuarios").WithTags("Usuarios")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaGestaoUsuarios);

        grupo.MapGet("", async (
                [AsParameters] ParametrosListarUsuarios parametros,
                ClaimsPrincipal usuario,
                IServicoGestaoUsuarios servico,
                CancellationToken cancellationToken) =>
                (await servico.ListarAsync(parametros.ParaRequisicao(), usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(pagina => TypedResults.Ok(pagina)))
            .WithName("ListarUsuarios")
            .WithSummary("Lista os usuários pelo nome, com filtros por perfil, situação e trecho do nome ou do e-mail.")
            .AddEndpointFilter<FiltroValidacao<ParametrosListarUsuarios>>()
            .AddOpenApiOperationTransformer(ParametrosListagemUsuariosOpenApi.AplicarAsync)
            .Produces<PaginaUsuariosResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest);

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (REQ-05, R-10).
        grupo.MapGet("/{id}", async (Guid id, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.ObterAsync(id, cancellationToken)).ParaResposta(conta => TypedResults.Ok(conta)))
            .WithName("ObterUsuario")
            .WithSummary("Consulta um usuário, sem nenhum dado de credencial.")
            .Produces<UsuarioGestaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        return app;
    }
}
