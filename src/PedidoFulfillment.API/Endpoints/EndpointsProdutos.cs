using System.Security.Claims;
using PedidoFulfillment.API.Erros;
using PedidoFulfillment.API.OpenApi;
using PedidoFulfillment.API.Seguranca;
using PedidoFulfillment.Application.Contratos.Produtos;
using PedidoFulfillment.Application.Produtos;

namespace PedidoFulfillment.API.Endpoints;

/// <summary>
/// Rotas do catálogo de produtos (REQ-22, research R-09). Só roteiam: validam, delegam ao
/// serviço e traduzem o resultado. Leitura para os três perfis; gestão só para o Admin.
/// </summary>
public static class EndpointsProdutos
{
    public static WebApplication MapearEndpointsProdutos(this WebApplication app)
    {
        var grupo = app.MapGroup("/api/v1/produtos").WithTags("Produtos");

        grupo.MapGet("", async (
                [AsParameters] ParametrosListarProdutos parametros,
                ClaimsPrincipal usuario,
                IServicoCatalogo servico,
                CancellationToken cancellationToken) =>
                (await servico.ListarAsync(parametros.ParaRequisicao(), usuario.ObterUsuarioId(), usuario.ObterPerfis(), cancellationToken))
                    .ParaResposta(pagina => TypedResults.Ok(pagina)))
            .WithName("ListarProdutos")
            .WithSummary("Lista os produtos pelo nome, com busca em português; o Cliente vê só os ativos.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaConsultaPedido)
            .AddEndpointFilter<FiltroValidacao<ParametrosListarProdutos>>()
            .AddOpenApiOperationTransformer(ParametrosListagemProdutosOpenApi.AplicarAsync)
            .Produces<PaginaProdutosResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest);

        // Sem a restrição {id:guid}: um identificador inválido deve dar 400, e não 404 (REQ-05, R-10).
        grupo.MapGet("/{id}", async (Guid id, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.ObterAsync(id, usuario.ObterPerfis(), cancellationToken))
                    .ParaResposta(produto => TypedResults.Ok(produto)))
            .WithName("ObterProduto")
            .WithSummary("Consulta um produto; para o Cliente, um inativo responde como inexistente.")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaConsultaPedido)
            .Produces<ProdutoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound);

        grupo.MapPost("", async (CadastrarProdutoRequestDTO produto, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.CadastrarAsync(produto, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(criado => TypedResults.Created($"/api/v1/produtos/{criado.Id}", criado)))
            .WithName("CadastrarProduto")
            .WithSummary("Cadastra um produto ativo (somente Admin).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCatalogo)
            .AddEndpointFilter<FiltroValidacao<CadastrarProdutoRequestDTO>>()
            .Produces<ProdutoResponseDTO>(StatusCodes.Status201Created)
            .ComCabecalhoLocation()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status409Conflict);

        grupo.MapPut("/{id}", async (Guid id, AlterarProdutoRequestDTO alteracao, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.AlterarAsync(id, alteracao, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(produto => TypedResults.Ok(produto)))
            .WithName("AlterarProduto")
            .WithSummary("Altera nome, descrição e preço; o preço novo vale só para pedidos novos (somente Admin).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCatalogo)
            .AddEndpointFilter<FiltroValidacao<AlterarProdutoRequestDTO>>()
            .Produces<ProdutoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapPost("/{id}/ajustes-estoque", async (Guid id, AjustarEstoqueRequestDTO ajuste, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.AjustarEstoqueAsync(id, ajuste, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(produto => TypedResults.Ok(produto)))
            .WithName("AjustarEstoqueProduto")
            .WithSummary("Soma uma variação ao estoque, com motivo, sem perder unidades reservadas ao mesmo tempo (somente Admin).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCatalogo)
            .AddEndpointFilter<FiltroValidacao<AjustarEstoqueRequestDTO>>()
            .Produces<ProdutoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status422UnprocessableEntity);

        grupo.MapPost("/{id}/desativacao", async (Guid id, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.DesativarAsync(id, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(produto => TypedResults.Ok(produto)))
            .WithName("DesativarProduto")
            .WithSummary("Desativa o produto sem apagá-lo; repetir não muda nada (somente Admin).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCatalogo)
            .Produces<ProdutoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        grupo.MapPost("/{id}/ativacao", async (Guid id, ClaimsPrincipal usuario, IServicoCatalogo servico, CancellationToken cancellationToken) =>
                (await servico.AtivarAsync(id, usuario.ObterUsuarioId(), cancellationToken))
                    .ParaResposta(produto => TypedResults.Ok(produto)))
            .WithName("AtivarProduto")
            .WithSummary("Reativa o produto; repetir não muda nada (somente Admin).")
            .RequireAuthorization(PoliticasAutorizacao.PoliticaCatalogo)
            .Produces<ProdutoResponseDTO>()
            .ProduzErros(StatusCodes.Status400BadRequest, StatusCodes.Status404NotFound, StatusCodes.Status409Conflict);

        return app;
    }
}
