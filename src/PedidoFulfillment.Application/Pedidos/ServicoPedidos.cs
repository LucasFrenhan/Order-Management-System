using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Entregas;
using PedidoFulfillment.Application.Estoque;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Application.Pagamentos;
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

    private static readonly Falha PedidoNaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Pedido não encontrado.", "Não existe pedido com este identificador.");

    private static readonly Falha ClienteSemPermissao = new(
        TipoFalha.SemPermissao, "Operação não permitida.", "O cliente só pode cancelar o próprio pedido enquanto ele está Pendente.");

    private static readonly Falha AlteradoPorOutraOperacao = new(
        TipoFalha.Conflito, "Transição de status inválida.", "O pedido foi alterado por outra operação; tente novamente.", CodigosErro.PedidoTransicaoInvalida);

    private const int TentativasStatus = 3;

    private static readonly Falha RastreioDuplicado = new(
        TipoFalha.Conflito, "Código de rastreio já usado.",
        "Outro pedido já usa este código de rastreio nesta transportadora.", CodigosErro.PedidoRastreioDuplicado);

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
        var produtos = (await armazenamento.ObterProdutosAsync(ids, paraAlterar: false, cancellationToken)).ToDictionary(p => p.Id);
        if (ValidarProdutos(pedido, produtos) is { } falha)
        {
            return falha;
        }

        var agora = relogio.GetUtcNow();
        var numero = await geradorNumero.ProximoAsync(agora, cancellationToken);
        var novo = new Pedido(clienteId, numero, pedido.Itens.Select(i => (produtos[i.ProdutoId], i.Quantidade)), agora);
        var resposta = novo.ParaResposta();
        armazenamento.Adicionar(
            novo,
            new RegistroIdempotencia(clienteId, chaveIdempotencia, impressao, novo.Id, JsonSerializer.Serialize(resposta, OpcoesJsonRegistro), agora),
            EventoSaida.De(EventoPedidoCriado(novo, agora), agora));
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

    /// <summary>
    /// Estado atual do pedido (FR-014). Pedido de outro Cliente recebe a mesma falha de um
    /// inexistente, para não revelar que ele existe (FR-015).
    /// </summary>
    public async Task<Resultado<PedidoResponseDTO>> ObterAsync(Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: false, cancellationToken);
        return pedido is null || (!EhOperacao(perfis) && pedido.ClienteId != usuarioId)
            ? PedidoNaoEncontrado
            : pedido.ParaResposta();
    }

    /// <summary>
    /// Aplica a tabela de transições (FR-016, FR-017). Se o pedido mudou desde a leitura, a
    /// mudança é reavaliada sobre o estado atual, até <see cref="TentativasStatus"/> vezes (FR-019).
    /// O cancelamento passa por <see cref="Pedido.Cancelar"/>: devolve o estoque reservado (REQ-08)
    /// e deixa o estorno do pagamento aprovado pendente para o processo de workers, na mesma
    /// gravação e sem chamar o gateway (REQ-13, R-01). A releitura depois de um conflito decide a
    /// corrida com o worker (REQ-08, R-05): se a cobrança venceu, o cancelamento pede o estorno.
    /// O cancelamento grava junto o <c>pedido.cancelado</c>, com o motivo <c>Operacao</c> ou
    /// <c>Cliente</c> pelo perfil (REQ-15, R-02); o descarte de um conflito leva o evento junto.
    /// </summary>
    public async Task<Resultado<PedidoResponseDTO>> AtualizarStatusAsync(
        Guid pedidoId, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, AtualizarStatusPedidoRequestDTO atualizacao, CancellationToken cancellationToken)
    {
        var operacao = EhOperacao(perfis);
        for (var tentativa = 1; tentativa <= TentativasStatus; tentativa++)
        {
            var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken);
            if (pedido is null || (!operacao && pedido.ClienteId != usuarioId))
            {
                return PedidoNaoEncontrado;
            }

            var anterior = pedido.Status;
            if (AvaliarTransicao(operacao, anterior, atualizacao.Status) is { } recusa)
            {
                return recusa;
            }

            var agora = relogio.GetUtcNow();
            ResultadoCancelamento? cancelamento = null;
            if (atualizacao.Status == StatusPedido.Cancelado)
            {
                var produtos = new Dictionary<Guid, Produto>();
                if (pedido.EstoqueReservado)
                {
                    var ids = pedido.Itens.Select(i => i.ProdutoId).ToList();
                    produtos = (await armazenamento.ObterProdutosAsync(ids, paraAlterar: true, cancellationToken)).ToDictionary(p => p.Id);
                }

                cancelamento = pedido.Cancelar(usuarioId, atualizacao.Motivo, produtos, agora);
                armazenamento.Adicionar(EventosPedido.Cancelado(
                    pedido, operacao ? MotivoCancelamento.Operacao : MotivoCancelamento.Cliente, null, cancelamento.Value, agora));
            }
            else
            {
                pedido.MudarStatus(atualizacao.Status, usuarioId, atualizacao.Motivo?.Trim(), agora);
            }

            try
            {
                await armazenamento.SalvarAsync(cancellationToken);
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                continue;
            }

            if (cancelamento?.EstoqueDevolvido == true)
            {
                EventosEstoque.EstoqueDevolvido(logger, pedido.Id, pedido.Itens.Count);
            }

            if (cancelamento?.OrdemCancelada == true)
            {
                EventosEntrega.OrdemEntregaCancelada(logger, pedido.Id);
            }

            if (cancelamento?.EstornoSolicitado == true)
            {
                EventosPagamento.EstornoSolicitado(logger, pedido.Id, pedido.Pagamento!.Valor, OrigemEstorno.Operacao);
            }

            EventosPedidos.StatusPedidoAlterado(logger, pedido.Id, anterior, pedido.Status, operacao ? "Operacao" : "Cliente");
            return pedido.ParaResposta();
        }

        return AlteradoPorOutraOperacao;
    }

    /// <summary>
    /// Despacho ou correção do rastreio (REQ-11, R-05 a R-07). A cada tentativa a decisão é
    /// refeita sobre o pedido relido: se o cancelamento venceu, é transição inválida; se outro
    /// despacho venceu, vira correção. "Sem mudança" não grava.
    /// </summary>
    public async Task<Resultado<PedidoResponseDTO>> RegistrarRastreioAsync(
        Guid pedidoId, Guid usuarioId, RegistrarRastreioRequestDTO rastreio, CancellationToken cancellationToken)
    {
        var transportadora = rastreio.Transportadora.Trim();
        var codigo = rastreio.CodigoRastreio.Trim();
        for (var tentativa = 1; tentativa <= TentativasStatus; tentativa++)
        {
            var pedido = await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, cancellationToken);
            if (pedido is null)
            {
                return PedidoNaoEncontrado;
            }

            if (!pedido.PodeRegistrarRastreio)
            {
                return new Falha(TipoFalha.Conflito, "Transição de status inválida.",
                    $"O pedido está em {pedido.Status} e não pode ser despachado.", CodigosErro.PedidoTransicaoInvalida);
            }

            if (await armazenamento.RastreioEmUsoAsync(transportadora, codigo, pedido.Id, cancellationToken))
            {
                EventosPedidos.RastreioDuplicado(logger, pedido.Id, usuarioId);
                return RastreioDuplicado;
            }

            var resultado = pedido.RegistrarRastreio(transportadora, codigo, usuarioId, relogio.GetUtcNow());
            if (resultado != ResultadoRastreio.SemMudanca)
            {
                try
                {
                    await armazenamento.SalvarAsync(cancellationToken);
                }
                catch (ConflitoConcorrenciaException)
                {
                    armazenamento.DescartarAlteracoes();
                    continue;
                }
                catch (RastreioDuplicadoException)
                {
                    armazenamento.DescartarAlteracoes();
                    EventosPedidos.RastreioDuplicado(logger, pedido.Id, usuarioId);
                    return RastreioDuplicado;
                }
            }

            EventosPedidos.RastreioRegistrado(logger, pedido.Id, usuarioId, resultado);
            return pedido.ParaResposta();
        }

        return AlteradoPorOutraOperacao;
    }

    /// <summary>
    /// O Cliente só pode Pendente → Cancelado no próprio pedido; qualquer outra transição é 403,
    /// mesmo que também fosse inválida para a operação (contrato v1, research R-06).
    /// </summary>
    private static Falha? AvaliarTransicao(bool operacao, StatusPedido atual, StatusPedido solicitado)
    {
        if (!operacao)
        {
            return TransicoesStatusPedido.PermitidaParaCliente(atual, solicitado) ? null : ClienteSemPermissao;
        }

        return TransicoesStatusPedido.PermitidaParaOperacao(atual, solicitado)
            ? null
            : new Falha(TipoFalha.Conflito, "Transição de status inválida.",
                $"O pedido está em {atual} e não pode passar para {solicitado}.", CodigosErro.PedidoTransicaoInvalida);
    }

    /// <summary>
    /// <c>pedido.criado</c>, gravado no outbox junto com o pedido; o Worker publica depois
    /// (REQ-06, FR-005). A repetição idempotente não passa por aqui (FR-009).
    /// </summary>
    private static EnvelopeEvento<PedidoCriado> EventoPedidoCriado(Pedido pedido, DateTimeOffset agora) =>
        EnvelopeEvento.Criar(pedido.Id, new PedidoCriado
        {
            PedidoId = pedido.Id,
            Numero = pedido.Numero,
            ClienteId = pedido.ClienteId,
            Itens = [.. pedido.Itens.OrderBy(i => i.ProdutoId).Select(i => new ItemPedidoCriado
            {
                ProdutoId = i.ProdutoId,
                Quantidade = i.Quantidade,
                PrecoUnitario = i.PrecoUnitario
            })],
            ValorTotal = pedido.ValorTotal,
            Moeda = pedido.Moeda
        }, agora);

    /// <summary>Admin e OperadorLogistica operam qualquer pedido; os demais, só como Cliente dono.</summary>
    private static bool EhOperacao(IReadOnlyCollection<PerfilUsuario> perfis) =>
        perfis.Contains(PerfilUsuario.Admin) || perfis.Contains(PerfilUsuario.OperadorLogistica);

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
