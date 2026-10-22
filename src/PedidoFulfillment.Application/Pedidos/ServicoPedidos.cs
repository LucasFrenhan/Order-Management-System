using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Pedidos;

/// <summary>
/// Regras das operações de pedidos (research R-01 do REQ-05): validação de produtos,
/// idempotência da criação, visibilidade da consulta e transições de status.
/// </summary>
public sealed class ServicoPedidos(
    IArmazenamentoPedidos armazenamento,
    IGeradorNumeroPedido geradorNumero,
    TimeProvider relogio,
    ILogger<ServicoPedidos> logger) : IServicoPedidos
{
    // Formato da resposta guardada com a chave; só precisa ida e volta com o próprio DTO.
    private static readonly JsonSerializerOptions OpcoesJsonRegistro = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly Falha NaoImplementado = new(TipoFalha.NaoImplementado, "Operação ainda não implementada.");

    private static readonly Falha ConteudoDivergente = new(
        TipoFalha.Conflito, "Chave de idempotência já usada.", "A chave já foi usada com outro conteúdo.", CodigosErro.IdempotenciaConteudoDivergente);

    private static readonly Falha EmProcessamento = new(
        TipoFalha.Conflito, "Requisição em processamento.", "Outra requisição com a mesma chave ainda está em processamento.", CodigosErro.IdempotenciaEmProcessamento);

    /// <summary>
    /// Idempotente por (cliente, chave): a chave registrada responde pela resposta guardada; a
    /// corrida entre requisições simultâneas é decidida pela gravação (research R-04).
    /// </summary>
    public async Task<Resultado<PedidoResponseDTO>> CriarAsync(Guid clienteId, Guid chaveIdempotencia, CriarPedidoRequestDTO pedido, CancellationToken cancellationToken)
    {
        var impressao = ImpressaoConteudo.Calcular(pedido);
        if (await armazenamento.ObterRegistroIdempotenciaAsync(clienteId, chaveIdempotencia, cancellationToken) is { } existente)
        {
            return ResponderRegistro(existente, impressao);
        }

        var ids = pedido.Itens.Select(i => i.ProdutoId).ToList();
        var produtos = (await armazenamento.ObterProdutosAsync(ids, cancellationToken)).ToDictionary(p => p.Id);
        if (ValidarProdutos(pedido, produtos) is { } falha)
        {
            return falha;
        }

        var agora = relogio.GetUtcNow();
        var numero = await geradorNumero.ProximoAsync(agora, cancellationToken);
        var novo = new Pedido(clienteId, numero, pedido.Itens.Select(i => (produtos[i.ProdutoId], i.Quantidade)), agora);
        var resposta = novo.ParaResposta();
        armazenamento.Adicionar(novo, new RegistroIdempotencia(
            clienteId, chaveIdempotencia, impressao, novo.Id, JsonSerializer.Serialize(resposta, OpcoesJsonRegistro), agora));
        try
        {
            await armazenamento.SalvarAsync(cancellationToken);
        }
        catch (ChaveIdempotenciaRegistradaException)
        {
            // Outra requisição com a mesma chave gravou antes; nada desta foi salvo.
            armazenamento.DescartarAlteracoes();
            var vencedor = await armazenamento.ObterRegistroIdempotenciaAsync(clienteId, chaveIdempotencia, cancellationToken);
            return vencedor is null ? EmProcessamento : ResponderRegistro(vencedor, impressao);
        }

        EventosPedidos.PedidoCriado(logger, novo.Id, novo.Numero, novo.Itens.Count, novo.ValorTotal);
        return resposta;
    }

    // Consulta e mudança de status chegam na US3 e na US4 do REQ-05.
    public Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);

    public Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken) =>
        Task.FromResult<Resultado<PedidoResponseDTO>>(NaoImplementado);

    /// <summary>Mesma impressão devolve a resposta original (FR-010); outra impressão, 409 (FR-011).</summary>
    private Resultado<PedidoResponseDTO> ResponderRegistro(RegistroIdempotencia registro, string impressao)
    {
        if (registro.Impressao != impressao)
        {
            return ConteudoDivergente;
        }

        EventosPedidos.PedidoRepetido(logger, registro.PedidoId);
        return JsonSerializer.Deserialize<PedidoResponseDTO>(registro.Resposta, OpcoesJsonRegistro)!;
    }

    /// <summary>
    /// FR-003: um único 422 para todos os itens com problema; inexistente prevalece sobre
    /// indisponível (inativo ou com estoque menor que a quantidade, sem reservar).
    /// </summary>
    private static Falha? ValidarProdutos(CriarPedidoRequestDTO pedido, IReadOnlyDictionary<Guid, Produto> produtos)
    {
        var inexistentes = pedido.Itens.Where(i => !produtos.ContainsKey(i.ProdutoId)).Select(i => i.ProdutoId).ToList();
        var indisponiveis = pedido.Itens
            .Where(i => produtos.TryGetValue(i.ProdutoId, out var p) && (!p.Ativo || p.QuantidadeEstoque < i.Quantidade))
            .Select(i => i.ProdutoId)
            .ToList();
        if (inexistentes.Count == 0 && indisponiveis.Count == 0)
        {
            return null;
        }

        var citados = string.Join(", ", inexistentes.Concat(indisponiveis).Order());
        return inexistentes.Count > 0
            ? new Falha(TipoFalha.RegraNegocio, "Produto inexistente.", $"Produtos inexistentes ou indisponíveis: {citados}.", CodigosErro.ProdutoInexistente)
            : new Falha(TipoFalha.RegraNegocio, "Produto indisponível.", $"Produtos inativos ou sem estoque suficiente: {citados}.", CodigosErro.ProdutoIndisponivel);
    }
}
