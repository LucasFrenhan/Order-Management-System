using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Erros;
using PedidoFulfillment.Application.Contratos.Produtos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Produtos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Application.Produtos;

/// <summary>Catálogo de produtos (REQ-22).</summary>
public interface IServicoCatalogo
{
    Task<Resultado<PaginaProdutosResponseDTO>> ListarAsync(
        ListarProdutosRequestDTO requisicao, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> ObterAsync(Guid produtoId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> CadastrarAsync(CadastrarProdutoRequestDTO produto, Guid usuarioId, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> AlterarAsync(Guid produtoId, AlterarProdutoRequestDTO alteracao, Guid usuarioId, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> AjustarEstoqueAsync(Guid produtoId, AjustarEstoqueRequestDTO ajuste, Guid usuarioId, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> DesativarAsync(Guid produtoId, Guid usuarioId, CancellationToken cancellationToken);

    Task<Resultado<ProdutoResponseDTO>> AtivarAsync(Guid produtoId, Guid usuarioId, CancellationToken cancellationToken);
}

/// <summary>
/// Regras do catálogo (REQ-22): visões por perfil (R-06), listagem por cursor (R-05), SKU único
/// (R-08) e alterações com concorrência otimista, reavaliadas sobre o estado atual (R-07). As
/// rotas de gestão já chegam autorizadas só para o Admin (<c>PoliticaCatalogo</c>).
/// </summary>
public sealed class ServicoCatalogo(
    IArmazenamentoProdutos armazenamento,
    CodificadorCursorProdutos codificador,
    TimeProvider relogio,
    ILogger<ServicoCatalogo> logger) : IServicoCatalogo
{
    public const int Tentativas = 3;

    private static readonly Falha NaoEncontrado = new(
        TipoFalha.NaoEncontrado, "Produto não encontrado.", "Não existe produto com este identificador.");

    private static readonly Falha FiltroSituacaoProibido = new(
        TipoFalha.SemPermissao, "Operação não permitida.", "O filtro por situação é exclusivo da operação.");

    private static readonly Falha CursorInvalido = new(
        TipoFalha.Validacao, "Um ou mais campos são inválidos.",
        Erros: new Dictionary<string, string[]> { [ServicoListagemPedidos.ChaveCursor] = [ServicoListagemPedidos.MensagemCursorInvalido] });

    private static readonly Falha AlteradoPorOutraOperacao = new(
        TipoFalha.Conflito, "Produto alterado por outra operação.", "O produto foi alterado por outra operação; tente novamente.",
        CodigosErro.ProdutoAlteradoPorOutraOperacao);

    public async Task<Resultado<PaginaProdutosResponseDTO>> ListarAsync(
        ListarProdutosRequestDTO requisicao, Guid usuarioId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        var operacao = ServicoPedidos.EhOperacao(perfis);
        if (!operacao && requisicao.Situacao is not null)
        {
            return FiltroSituacaoProibido;
        }

        var filtro = new FiltroListagemProdutos
        {
            Palavras = requisicao.Palavras(),
            Situacao = operacao ? requisicao.SituacaoPedida() ?? SituacaoProduto.Todos : SituacaoProduto.Ativos
        };

        PosicaoCursorProdutos? posicao = null;
        if (requisicao.Cursor is not null && !codificador.TentarDecodificar(requisicao.Cursor, usuarioId, filtro, out posicao))
        {
            return CursorInvalido;
        }

        var tamanho = requisicao.TamanhoPagina();
        var lidos = await armazenamento.ListarAsync(filtro, posicao, tamanho + 1, cancellationToken);
        return MontarPagina(lidos, tamanho, posicao, usuarioId, filtro, operacao);
    }

    public async Task<Resultado<ProdutoResponseDTO>> ObterAsync(Guid produtoId, IReadOnlyCollection<PerfilUsuario> perfis, CancellationToken cancellationToken)
    {
        var operacao = ServicoPedidos.EhOperacao(perfis);
        var produto = await armazenamento.ObterAsync(produtoId, paraAlterar: false, cancellationToken);

        // Inativo para o Cliente responde como inexistente (FR-003).
        return produto is null || (!operacao && !produto.Ativo) ? NaoEncontrado : Resposta(produto, operacao);
    }

    public async Task<Resultado<ProdutoResponseDTO>> CadastrarAsync(CadastrarProdutoRequestDTO produto, Guid usuarioId, CancellationToken cancellationToken)
    {
        if (await armazenamento.SkuEmUsoAsync(produto.Sku, cancellationToken))
        {
            return SkuDuplicado(produto.Sku);
        }

        var novo = new Produto(produto.Sku, produto.Nome, produto.Descricao, produto.Preco, produto.EstoqueInicial, ativo: true, relogio.GetUtcNow());
        armazenamento.Adicionar(novo);
        try
        {
            await armazenamento.SalvarAsync(cancellationToken);
        }
        catch (SkuDuplicadoException)
        {
            // Outro cadastro com o mesmo SKU gravou antes (R-08).
            armazenamento.DescartarAlteracoes();
            return SkuDuplicado(produto.Sku);
        }

        EventosCatalogo.ProdutoCadastrado(logger, novo.Id, novo.Sku, usuarioId, novo.QuantidadeEstoque);
        return Resposta(novo, operacao: true);
    }

    public Task<Resultado<ProdutoResponseDTO>> AlterarAsync(Guid produtoId, AlterarProdutoRequestDTO alteracao, Guid usuarioId, CancellationToken cancellationToken)
    {
        var precoAnterior = 0m;
        return AlterarComTentativasAsync(produtoId, cancellationToken,
            produto =>
            {
                if (produto.Nome == alteracao.Nome && produto.Descricao == alteracao.Descricao && produto.Preco == alteracao.Preco)
                {
                    return Mudanca.Nenhuma;
                }

                precoAnterior = produto.Preco;
                produto.AlterarDados(alteracao.Nome, alteracao.Descricao, alteracao.Preco, relogio.GetUtcNow());
                return Mudanca.Feita;
            },
            produto => EventosCatalogo.ProdutoAlterado(logger, produto.Id, usuarioId, precoAnterior, produto.Preco));
    }

    /// <summary>
    /// O banco soma a variação ao estoque do momento da gravação, numa instrução só (FR-016,
    /// research R-07): com reservas do Worker gravando ao mesmo tempo, nenhuma unidade se perde e
    /// não há conflito a reavaliar. Os limites são conferidos na mesma instrução.
    /// </summary>
    public async Task<Resultado<ProdutoResponseDTO>> AjustarEstoqueAsync(Guid produtoId, AjustarEstoqueRequestDTO ajuste, Guid usuarioId, CancellationToken cancellationToken)
    {
        var resultado = await armazenamento.AjustarEstoqueAsync(produtoId, ajuste.Variacao, MensagensCatalogo.EstoqueMaximo, relogio.GetUtcNow(), cancellationToken);
        var produto = await armazenamento.ObterAsync(produtoId, paraAlterar: false, cancellationToken);
        if (resultado == ResultadoAjusteEstoque.Inexistente || produto is null)
        {
            return NaoEncontrado;
        }

        switch (resultado)
        {
            case ResultadoAjusteEstoque.Insuficiente:
                return new Falha(TipoFalha.RegraNegocio, "Estoque insuficiente.",
                    $"O ajuste deixaria o estoque do produto {produto.Sku} negativo (atual: {produto.QuantidadeEstoque}, variação: {ajuste.Variacao}).",
                    CodigosErro.EstoqueInsuficiente);
            case ResultadoAjusteEstoque.AcimaDoLimite:
                return new Falha(TipoFalha.RegraNegocio, "Estoque acima do limite.",
                    $"O ajuste deixaria o estoque do produto {produto.Sku} acima de {MensagensCatalogo.EstoqueMaximo}.",
                    CodigosErro.EstoqueAcimaDoLimite);
            default:
                // A quantidade lida logo depois pode já incluir reservas feitas em seguida.
                EventosCatalogo.EstoqueAjustado(logger, produto.Id, ajuste.Variacao, usuarioId, ajuste.Motivo, produto.QuantidadeEstoque);
                return Resposta(produto, operacao: true);
        }
    }

    /// <summary>Idempotente: um produto já inativo volta como está, sem gravar nem logar (FR-018).</summary>
    public Task<Resultado<ProdutoResponseDTO>> DesativarAsync(Guid produtoId, Guid usuarioId, CancellationToken cancellationToken) =>
        AlterarComTentativasAsync(produtoId, cancellationToken,
            produto =>
            {
                if (!produto.Ativo)
                {
                    return Mudanca.Nenhuma;
                }

                produto.Desativar(relogio.GetUtcNow());
                return Mudanca.Feita;
            },
            produto => EventosCatalogo.ProdutoDesativado(logger, produto.Id, usuarioId));

    public Task<Resultado<ProdutoResponseDTO>> AtivarAsync(Guid produtoId, Guid usuarioId, CancellationToken cancellationToken) =>
        AlterarComTentativasAsync(produtoId, cancellationToken,
            produto =>
            {
                if (produto.Ativo)
                {
                    return Mudanca.Nenhuma;
                }

                produto.Ativar(relogio.GetUtcNow());
                return Mudanca.Feita;
            },
            produto => EventosCatalogo.ProdutoReativado(logger, produto.Id, usuarioId));

    /// <summary>
    /// Lê o produto para alterar, aplica a mudança e grava. Se outra operação (uma reserva, uma
    /// devolução ou outra alteração) mudou o produto desde a leitura, descarta e reavalia sobre o
    /// estado atual, até <see cref="Tentativas"/> vezes (R-07, FR-016).
    /// </summary>
    private async Task<Resultado<ProdutoResponseDTO>> AlterarComTentativasAsync(
        Guid produtoId, CancellationToken cancellationToken, Func<Produto, ResultadoMudanca> mudar, Action<Produto> registrar)
    {
        for (var tentativa = 1; tentativa <= Tentativas; tentativa++)
        {
            var produto = await armazenamento.ObterAsync(produtoId, paraAlterar: true, cancellationToken);
            if (produto is null)
            {
                return NaoEncontrado;
            }

            var resultado = mudar(produto);
            if (resultado.Falha is { } falha)
            {
                armazenamento.DescartarAlteracoes();
                return falha;
            }

            if (resultado.Mudanca == Mudanca.Nenhuma)
            {
                armazenamento.DescartarAlteracoes();
                return Resposta(produto, operacao: true);
            }

            try
            {
                await armazenamento.SalvarAsync(cancellationToken);
            }
            catch (ConflitoConcorrenciaException)
            {
                armazenamento.DescartarAlteracoes();
                EventosCatalogo.ConflitoNoProduto(logger, produtoId, tentativa);
                continue;
            }

            registrar(produto);
            return Resposta(produto, operacao: true);
        }

        return AlteradoPorOutraOperacao;
    }

    private PaginaProdutosResponseDTO MontarPagina(
        IReadOnlyList<Produto> lidos, int tamanho, PosicaoCursorProdutos? posicao, Guid usuarioId, FiltroListagemProdutos filtro, bool operacao)
    {
        var haMais = lidos.Count > tamanho;
        var paraTras = posicao?.Sentido == SentidoCursor.Anterior;
        var produtos = paraTras ? lidos.Skip(haMais ? 1 : 0).ToList() : lidos.Take(tamanho).ToList();

        string Cursor(SentidoCursor sentido, Produto produto) =>
            codificador.Codificar(new PosicaoCursorProdutos(sentido, produto.Nome, produto.Sku), usuarioId, filtro);

        string? Volta(SentidoCursor sentido) => posicao is null ? null
            : codificador.Codificar(posicao with { Sentido = sentido, Inclusiva = true }, usuarioId, filtro);

        string? proximo;
        string? anterior;
        if (produtos.Count == 0)
        {
            proximo = paraTras ? Volta(SentidoCursor.Proxima) : null;
            anterior = paraTras ? null : Volta(SentidoCursor.Anterior);
        }
        else if (paraTras)
        {
            proximo = Cursor(SentidoCursor.Proxima, produtos[^1]);
            anterior = haMais ? Cursor(SentidoCursor.Anterior, produtos[0]) : null;
        }
        else
        {
            proximo = haMais ? Cursor(SentidoCursor.Proxima, produtos[^1]) : null;
            anterior = posicao is null ? null : Cursor(SentidoCursor.Anterior, produtos[0]);
        }

        return new PaginaProdutosResponseDTO
        {
            Itens = [.. produtos.Select(p => Resposta(p, operacao))],
            Tamanho = tamanho,
            Proximo = proximo,
            Anterior = anterior
        };
    }

    /// <summary>Visão do Cliente sem os campos da operação (FR-005, clarify).</summary>
    internal static ProdutoResponseDTO Resposta(Produto produto, bool operacao) => new()
    {
        Id = produto.Id,
        Sku = produto.Sku,
        Nome = produto.Nome,
        Descricao = produto.Descricao,
        Preco = produto.Preco,
        Moeda = "BRL",
        Disponivel = produto.QuantidadeEstoque > 0,
        QuantidadeEstoque = operacao ? produto.QuantidadeEstoque : null,
        Ativo = operacao ? produto.Ativo : null,
        CriadoEm = operacao ? produto.CriadoEm : null,
        AtualizadoEm = operacao ? produto.AtualizadoEm : null
    };

    private static Falha SkuDuplicado(string sku) => new(
        TipoFalha.Conflito, "SKU já cadastrado.", $"Já existe um produto com o SKU {sku}.", CodigosErro.ProdutoSkuDuplicado);

    private enum Mudanca
    {
        Nenhuma,
        Feita
    }

    /// <summary>O que a mudança fez: nada, mudou, ou recusou com uma falha.</summary>
    private readonly record struct ResultadoMudanca(Mudanca Mudanca, Falha? Falha)
    {
        public static implicit operator ResultadoMudanca(Mudanca mudanca) => new(mudanca, null);

        public static implicit operator ResultadoMudanca(Falha falha) => new(Mudanca.Nenhuma, falha);
    }
}
