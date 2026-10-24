using PedidoFulfillment.Domain.Comum;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pagamentos;
using PedidoFulfillment.Domain.Produtos;

namespace PedidoFulfillment.Domain.Pedidos;

/// <summary>
/// Compra de um cliente (data-model). Protege só a própria consistência: total, snapshot dos
/// itens e histórico gravado junto com a mudança de status (FR-023). As transições válidas
/// são regra do REQ-05 (research R-02).
/// </summary>
public sealed class Pedido
{
    public const int TamanhoNumero = 19;
    public const string MoedaPadrao = "BRL";

    /// <summary>Motivo do histórico na mudança para Processando pelo pagamento aprovado (REQ-09, FR-008).</summary>
    public const string MotivoPagamentoAprovado = "Pagamento aprovado.";

    /// <summary>Motivo do histórico na mudança para ProntoParaEnvio pela ordem de entrega (REQ-10, FR-004).</summary>
    public const string MotivoOrdemEntregaCriada = "Ordem de entrega criada.";

    /// <summary>Motivo do histórico no despacho com rastreio (REQ-11, FR-008).</summary>
    public const string MotivoPedidoDespachado = "Pedido despachado.";

    private readonly List<ItemPedido> itens = [];
    private readonly List<HistoricoStatusPedido> historico = [];

    public Pedido(Guid clienteId, string numero, IEnumerable<(Produto Produto, int Quantidade)> itensPedido, DateTimeOffset instante)
    {
        ArgumentNullException.ThrowIfNull(itensPedido);
        var linhas = itensPedido.ToList();
        if (linhas.Count == 0)
        {
            throw new ArgumentException("O pedido precisa de ao menos um item.", nameof(itensPedido));
        }

        if (linhas.DistinctBy(l => l.Produto.Id).Count() != linhas.Count)
        {
            throw new ArgumentException("O mesmo produto não pode aparecer duas vezes no pedido.", nameof(itensPedido));
        }

        var numeroAparado = Guardas.Texto(numero, TamanhoNumero);
        if (numeroAparado.Length != TamanhoNumero)
        {
            throw new ArgumentException($"O número do pedido deve ter {TamanhoNumero} caracteres.", nameof(numero));
        }

        Id = Guid.CreateVersion7();
        ClienteId = Guardas.Identificador(clienteId);
        Numero = numeroAparado;
        Moeda = MoedaPadrao;
        Status = StatusPedido.Pendente;
        CriadoEm = Guardas.Utc(instante);
        AtualizadoEm = CriadoEm;
        itens.AddRange(linhas.Select(l => new ItemPedido(Id, l.Produto, l.Quantidade)));
        ValorTotal = itens.Sum(i => i.Subtotal);
        historico.Add(new HistoricoStatusPedido(Id, null, Status, null, null, instante));
    }

    private Pedido()
    {
        Numero = "";
        Moeda = MoedaPadrao;
    }

    public Guid Id { get; private set; }

    /// <summary>Padrão <c>PED-AAAAMMDD-NNNNNN</c>, gerado pelo REQ-05.</summary>
    public string Numero { get; private set; }

    public Guid ClienteId { get; private set; }

    public StatusPedido Status { get; private set; }

    public decimal ValorTotal { get; private set; }

    public string Moeda { get; private set; }

    public DateTimeOffset CriadoEm { get; private set; }

    public DateTimeOffset AtualizadoEm { get; private set; }

    public IReadOnlyCollection<ItemPedido> Itens => itens;

    /// <summary>Mudanças de status, na ordem em que aconteceram.</summary>
    public IReadOnlyCollection<HistoricoStatusPedido> Historico => historico;

    /// <summary>O estoque dos itens está abatido para este pedido (REQ-08, R-02).</summary>
    public bool EstoqueReservado { get; private set; }

    public Pagamento? Pagamento { get; private set; }

    public OrdemEntrega? OrdemEntrega { get; private set; }

    /// <summary>
    /// Muda o status e acrescenta a entrada no histórico, para gravar os dois juntos. Não valida
    /// a transição (REQ-05).
    /// </summary>
    public void MudarStatus(StatusPedido novo, Guid? autorId, string? motivo, DateTimeOffset instante)
    {
        if (!Enum.IsDefined(novo))
        {
            throw new ArgumentOutOfRangeException(nameof(novo), novo, "Status desconhecido.");
        }

        var entrada = new HistoricoStatusPedido(Id, Status, novo, autorId, motivo, instante);
        historico.Add(entrada);
        Status = novo;
        AtualizadoEm = instante;
    }

    /// <summary>
    /// Tudo ou nada (REQ-08, FR-004 e FR-005): com algum item inativo ou sem estoque suficiente,
    /// devolve as faltas e não altera nada; senão, abate cada quantidade e marca a reserva.
    /// </summary>
    /// <exception cref="InvalidOperationException">Já reservado, ou falta um produto dos itens em <paramref name="produtos"/>.</exception>
    public IReadOnlyList<FaltaEstoque> ReservarEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        if (EstoqueReservado)
        {
            throw new InvalidOperationException($"O estoque do pedido {Numero} já está reservado.");
        }

        var linhas = ProdutosDosItens(produtos);
        var faltas = linhas
            .Where(l => !l.Produto.Ativo || l.Produto.QuantidadeEstoque < l.Item.Quantidade)
            .Select(l => new FaltaEstoque(l.Produto.Id, l.Item.Quantidade, l.Produto.QuantidadeEstoque, !l.Produto.Ativo))
            .ToList();
        if (faltas.Count > 0)
        {
            return faltas;
        }

        foreach (var (item, produto) in linhas)
        {
            produto.AjustarEstoque(-item.Quantidade, instante);
        }

        EstoqueReservado = true;
        AtualizadoEm = instante;
        return [];
    }

    /// <summary>Desfaz a reserva, mesmo com produto inativo (REQ-08, FR-010).</summary>
    /// <exception cref="InvalidOperationException">Sem reserva, ou falta um produto dos itens em <paramref name="produtos"/>.</exception>
    public void DevolverEstoque(IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        if (!EstoqueReservado)
        {
            throw new InvalidOperationException($"O pedido {Numero} não tem estoque reservado.");
        }

        foreach (var (item, produto) in ProdutosDosItens(produtos))
        {
            produto.AjustarEstoque(item.Quantidade, instante);
        }

        EstoqueReservado = false;
        AtualizadoEm = instante;
    }

    /// <summary>
    /// Pode ser cobrado (REQ-09, FR-002): estoque reservado, sem pagamento e em status não final
    /// (Pendente, Processando ou ProntoParaEnvio, clarify).
    /// </summary>
    public bool AptoParaPagamento =>
        EstoqueReservado && Pagamento is null
        && Status is StatusPedido.Pendente or StatusPedido.Processando or StatusPedido.ProntoParaEnvio;

    /// <summary>
    /// Registra o pagamento aprovado com o valor total e, em Pendente, leva o pedido a Processando,
    /// sem autor (REQ-09, R-03 e R-04). Pedido já movido pela operação mantém o status (clarify).
    /// A reserva continua: a mercadoria segue para o envio.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está <see cref="AptoParaPagamento"/>.</exception>
    public void AprovarPagamento(string identificadorTransacao, DateTimeOffset instante)
    {
        // RegistrarResultado aceita o identificador opcional; na aprovação ele é obrigatório (FR-007).
        var transacao = Guardas.Texto(identificadorTransacao, Pagamento.TamanhoMaximoIdentificadorTransacao);
        var pagamento = NovoPagamento(instante);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, transacao, instante);
        Pagamento = pagamento;
        if (Status == StatusPedido.Pendente)
        {
            MudarStatus(StatusPedido.Processando, null, MotivoPagamentoAprovado, instante);
        }
        else
        {
            AtualizadoEm = instante;
        }
    }

    /// <summary>
    /// Registra o pagamento recusado, devolve o estoque reservado e leva o pedido a
    /// PagamentoRejeitado, sem autor, tudo junto (REQ-09, FR-009, clarify).
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está <see cref="AptoParaPagamento"/>, ou falta um produto dos itens.</exception>
    public void RecusarPagamento(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        var motivoAparado = Guardas.Texto(motivo, HistoricoStatusPedido.TamanhoMaximoMotivo);
        var pagamento = NovoPagamento(instante);
        ProdutosDosItens(produtos);
        pagamento.RegistrarResultado(SituacaoPagamento.Recusado, null, instante);
        DevolverEstoque(produtos, instante);
        Pagamento = pagamento;
        MudarStatus(StatusPedido.PagamentoRejeitado, null, motivoAparado, instante);
    }

    /// <summary>
    /// A cobrança se esgotou na reconciliação (REQ-12, FR-012): registra o pagamento como
    /// NaoProcessado, devolve o estoque e leva o pedido a Cancelado, sem autor, tudo junto.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está <see cref="AptoParaPagamento"/>, ou falta um produto dos itens.</exception>
    public void RegistrarPagamentoNaoProcessado(string motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        var motivoAparado = Guardas.Texto(motivo, HistoricoStatusPedido.TamanhoMaximoMotivo);
        var pagamento = NovoPagamento(instante);
        ProdutosDosItens(produtos);
        pagamento.RegistrarResultado(SituacaoPagamento.NaoProcessado, null, instante);
        DevolverEstoque(produtos, instante);
        Pagamento = pagamento;
        MudarStatus(StatusPedido.Cancelado, null, motivoAparado, instante);
    }

    /// <summary>
    /// Cancela o pedido levando junto o que depende dele, numa gravação (REQ-13, R-01): devolve o
    /// estoque reservado, pede o estorno do pagamento aprovado e muda o status para Cancelado, com
    /// o autor (ou sem autor, na reconciliação) e o motivo. O gateway não é chamado aqui: o estorno
    /// fica pendente para o processo de workers (FR-001). Tudo é validado antes de qualquer mudança.
    /// </summary>
    /// <exception cref="InvalidOperationException">Status final, ou falta um produto dos itens com estoque reservado.</exception>
    /// <exception cref="ArgumentException">Motivo acima do tamanho.</exception>
    public ResultadoCancelamento Cancelar(Guid? autorId, string? motivo, IReadOnlyDictionary<Guid, Produto> produtos, DateTimeOffset instante)
    {
        if (!TransicoesStatusPedido.PermitidaParaOperacao(Status, StatusPedido.Cancelado))
        {
            throw new InvalidOperationException($"O pedido {Numero} está em {Status} e não pode ser cancelado.");
        }

        Guardas.TextoOpcional(motivo, HistoricoStatusPedido.TamanhoMaximoMotivo);
        Guardas.Utc(instante);
        var devolver = EstoqueReservado;
        if (devolver)
        {
            ProdutosDosItens(produtos);
        }

        var estornar = Pagamento?.Situacao == SituacaoPagamento.Aprovado;
        var cancelarOrdem = OrdemEntrega is not null;
        if (OrdemEntrega is { } ordem && (ordem.DespachadoEm is not null || ordem.CanceladaEm is not null))
        {
            throw new InvalidOperationException($"O pedido {Numero} tem a ordem de entrega despachada ou cancelada e não pode ser cancelado.");
        }

        if (devolver)
        {
            DevolverEstoque(produtos, instante);
        }

        if (estornar)
        {
            Pagamento!.SolicitarEstorno(instante);
        }

        if (cancelarOrdem)
        {
            OrdemEntrega!.Cancelar(instante);
        }

        MudarStatus(StatusPedido.Cancelado, autorId, motivo?.Trim(), instante);
        return new ResultadoCancelamento(devolver, estornar, cancelarOrdem);
    }

    /// <summary>
    /// A cobrança foi aprovada no gateway, mas o pedido foi cancelado antes de ela ser gravada
    /// (REQ-13, R-07): registra o pagamento aprovado e já pede o estorno, sem mudar o status.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está Cancelado, ou já tem pagamento.</exception>
    public void RegistrarCobrancaAposCancelamento(string identificadorTransacao, DateTimeOffset instante)
    {
        if (Status != StatusPedido.Cancelado || Pagamento is not null)
        {
            throw new InvalidOperationException(
                $"O pedido {Numero} não pode registrar cobrança após o cancelamento: status {Status}, já pago: {Pagamento is not null}.");
        }

        var transacao = Guardas.Texto(identificadorTransacao, Pagamento.TamanhoMaximoIdentificadorTransacao);
        var pagamento = new Pagamento(Id, ValorTotal, instante);
        pagamento.RegistrarResultado(SituacaoPagamento.Aprovado, transacao, instante);
        pagamento.SolicitarEstorno(instante);
        Pagamento = pagamento;
        AtualizadoEm = instante;
    }

    /// <summary>O gateway confirmou o estorno (REQ-13, FR-002); a versão do pedido muda (R-02).</summary>
    /// <exception cref="InvalidOperationException">Sem estorno pendente.</exception>
    public void ConfirmarEstorno(string identificadorEstorno, DateTimeOffset instante)
    {
        PagamentoComEstornoPendente().ConfirmarEstorno(identificadorEstorno, instante);
        AtualizadoEm = instante;
    }

    /// <summary>Mais uma tentativa de estorno falhou (REQ-13, FR-003); a versão do pedido muda (R-02).</summary>
    /// <exception cref="InvalidOperationException">Sem estorno pendente.</exception>
    public void RegistrarFalhaEstorno(DateTimeOffset instante)
    {
        PagamentoComEstornoPendente().RegistrarFalhaEstorno(instante);
        AtualizadoEm = instante;
    }

    /// <summary>
    /// Pode ganhar a ordem de entrega (REQ-10, FR-002, R-02): estoque reservado, pagamento aprovado,
    /// sem ordem e em Processando ou ProntoParaEnvio.
    /// </summary>
    public bool AptoParaOrdemEntrega =>
        EstoqueReservado && Pagamento?.Situacao == SituacaoPagamento.Aprovado && OrdemEntrega is null
        && Status is StatusPedido.Processando or StatusPedido.ProntoParaEnvio;

    /// <summary>
    /// Cria a ordem de entrega, sem dados de despacho, e, em Processando, leva o pedido a
    /// ProntoParaEnvio, sem autor (REQ-10, R-02 e R-03). Em ProntoParaEnvio, o status não muda, mas
    /// <see cref="AtualizadoEm"/> sim, para a versão do pedido mudar (R-04). Estoque e pagamento ficam
    /// como estão.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está <see cref="AptoParaOrdemEntrega"/>.</exception>
    public OrdemEntrega CriarOrdemEntrega(DateTimeOffset instante)
    {
        if (!AptoParaOrdemEntrega)
        {
            throw new InvalidOperationException(
                $"O pedido {Numero} não pode ganhar ordem de entrega: status {Status}, estoque reservado: {EstoqueReservado}, "
                + $"pagamento aprovado: {Pagamento?.Situacao == SituacaoPagamento.Aprovado}, já tem ordem: {OrdemEntrega is not null}.");
        }

        var ordem = new OrdemEntrega(Id, instante);
        OrdemEntrega = ordem;
        if (Status == StatusPedido.Processando)
        {
            MudarStatus(StatusPedido.ProntoParaEnvio, null, MotivoOrdemEntregaCriada, instante);
        }
        else
        {
            AtualizadoEm = instante;
        }

        return ordem;
    }

    /// <summary>O rastreio pode ser registrado (REQ-11, FR-008 e FR-009): pronto para envio ou já despachado.</summary>
    public bool PodeRegistrarRastreio => Status is StatusPedido.ProntoParaEnvio or StatusPedido.Despachado;

    /// <summary>
    /// Registra o rastreio (REQ-11, R-05). Em ProntoParaEnvio, cria a ordem de entrega se faltar,
    /// grava transportadora, código e data de despacho e leva o pedido a Despachado, com o autor.
    /// Em Despachado, dados exatamente iguais (depois de aparar) não mudam nada; diferentes,
    /// inclusive só na caixa, corrigem o rastreio sem mudar status nem histórico, e
    /// <see cref="AtualizadoEm"/> muda para a versão do pedido mudar (R-07). Os textos são
    /// validados antes de qualquer mudança.
    /// </summary>
    /// <exception cref="InvalidOperationException">O pedido não está <see cref="PodeRegistrarRastreio"/>.</exception>
    /// <exception cref="ArgumentException">Transportadora ou código vazios ou acima do tamanho.</exception>
    public ResultadoRastreio RegistrarRastreio(string transportadora, string codigoRastreio, Guid autorId, DateTimeOffset instante)
    {
        if (!PodeRegistrarRastreio)
        {
            throw new InvalidOperationException($"O pedido {Numero} não pode registrar rastreio: status {Status}.");
        }

        var transportadoraAparada = Guardas.Texto(transportadora, OrdemEntrega.TamanhoMaximoTransportadora);
        var codigoAparado = Guardas.Texto(codigoRastreio, OrdemEntrega.TamanhoMaximoCodigoRastreio);
        if (Status == StatusPedido.Despachado)
        {
            if (OrdemEntrega is { } atual
                && string.Equals(atual.Transportadora, transportadoraAparada, StringComparison.Ordinal)
                && string.Equals(atual.CodigoRastreio, codigoAparado, StringComparison.Ordinal))
            {
                return ResultadoRastreio.SemMudanca;
            }

            OrdemEntrega ??= new OrdemEntrega(Id, instante);
            OrdemEntrega.CorrigirRastreio(transportadoraAparada, codigoAparado, instante);
            AtualizadoEm = instante;
            return ResultadoRastreio.Corrigido;
        }

        OrdemEntrega ??= new OrdemEntrega(Id, instante);
        OrdemEntrega.RegistrarDespacho(transportadoraAparada, codigoAparado, instante);
        MudarStatus(StatusPedido.Despachado, autorId, MotivoPedidoDespachado, instante);
        return ResultadoRastreio.Despachado;
    }

    private Pagamento NovoPagamento(DateTimeOffset instante) =>
        AptoParaPagamento
            ? new Pagamento(Id, ValorTotal, instante)
            : throw new InvalidOperationException(
                $"O pedido {Numero} não pode ser cobrado: status {Status}, estoque reservado: {EstoqueReservado}, já pago: {Pagamento is not null}.");

    private Pagamento PagamentoComEstornoPendente() =>
        Pagamento is { Situacao: SituacaoPagamento.EstornoPendente } pagamento
            ? pagamento
            : throw new InvalidOperationException($"O pedido {Numero} não tem estorno pendente.");

    private List<(ItemPedido Item, Produto Produto)> ProdutosDosItens(IReadOnlyDictionary<Guid, Produto> produtos)
    {
        ArgumentNullException.ThrowIfNull(produtos);
        return [.. itens.Select(i => (i, produtos.TryGetValue(i.ProdutoId, out var produto)
            ? produto
            : throw new InvalidOperationException($"O produto {i.ProdutoId} do pedido {Numero} não foi informado.")))];
    }
}
