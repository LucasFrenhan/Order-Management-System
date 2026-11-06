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

        grupo.MapPost("", async (CriarUsuarioInternoRequestDTO novo, ClaimsPrincipal usuario, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.CriarAsync(novo, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(criado => TypedResults.Created($"/api/v1/admin/usuarios/{criado.Id}", criado)))
            .WithName("CriarUsuarioInterno")
            .WithSummary("Cria um usuário interno sem senha; a pessoa recebe um convite por e-mail para defini-la.")
            .AddEndpointFilter<FiltroValidacao<CriarUsuarioInternoRequestDTO>>()
            .Produces<UsuarioGestaoResponseDTO>(StatusCodes.Status201Created)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict);

        // O perfil chega como texto: um valor desconhecido deve dar 400, e não 404 (R-09).
        grupo.MapPut("/{id}/perfis/{perfil}", async (Guid id, string perfil, ClaimsPrincipal usuario, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.AtribuirPerfilAsync(id, perfil, usuario.ObterUsuarioId(), cancellationToken)).ParaResposta(conta => TypedResults.Ok(conta)))
            .WithName("AtribuirPerfilUsuario")
            .WithSummary("Atribui um perfil à conta; repetir não muda nada.")
            .Produces<UsuarioGestaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapDelete("/{id}/perfis/{perfil}", async (Guid id, string perfil, ClaimsPrincipal usuario, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.RetirarPerfilAsync(id, perfil, usuario.ObterUsuarioId(), cancellationToken)).ParaResposta(conta => TypedResults.Ok(conta)))
            .WithName("RetirarPerfilUsuario")
            .WithSummary("Retira um perfil da conta; repetir não muda nada, e nunca fica sem nenhum Admin ativo.")
            .Produces<UsuarioGestaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapPost("/{id}/desativacao", async (Guid id, ClaimsPrincipal usuario, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.DesativarAsync(id, usuario.ObterUsuarioId(), cancellationToken)).ParaResposta(conta => TypedResults.Ok(conta)))
            .WithName("DesativarUsuario")
            .WithSummary("Desativa a conta sem apagá-la: encerra as sessões e impede o login; repetir não muda nada.")
            .Produces<UsuarioGestaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapPost("/{id}/reativacao", async (Guid id, ClaimsPrincipal usuario, IServicoGestaoUsuarios servico, CancellationToken cancellationToken) =>
                (await servico.ReativarAsync(id, usuario.ObterUsuarioId(), cancellationToken)).ParaResposta(conta => TypedResults.Ok(conta)))
            .WithName("ReativarUsuario")
            .WithSummary("Reativa a conta, com a mesma senha e os mesmos perfis; repetir não muda nada.")
            .Produces<UsuarioGestaoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        return app;
    }
}
