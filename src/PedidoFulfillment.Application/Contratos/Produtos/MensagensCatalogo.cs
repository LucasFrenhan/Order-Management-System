namespace PedidoFulfillment.Application.Contratos.Produtos;

/// <summary>Mensagens e limites da validação do catálogo (REQ-22, research R-10).</summary>
public static class MensagensCatalogo
{
    public const decimal PrecoMaximo = 999_999.99m;
    public const int EstoqueMaximo = 1_000_000_000;
    public const int VariacaoMaxima = 1_000_000;
    public const int TamanhoMaximoMotivo = 200;

    public const string Sku = "O SKU é obrigatório e deve ter até 50 caracteres.";
    public const string Nome = "O nome é obrigatório e deve ter até 200 caracteres.";
    public const string Descricao = "A descrição deve ter até 1000 caracteres.";
    public const string Preco = "O preço deve ser maior que zero, até 999999.99, com no máximo duas casas decimais.";
    public const string EstoqueInicial = "O estoque inicial deve estar entre 0 e 1000000000.";
    public const string Variacao = "A variação deve ser diferente de zero e estar entre -1000000 e 1000000.";
    public const string Motivo = "O motivo é obrigatório e deve ter até 200 caracteres.";
    public const string Busca = "A busca deve ter entre 2 e 100 caracteres e de 1 a 10 palavras.";
    public const string Situacao = "A situação deve ser Ativos, Inativos ou Todos.";

    /// <summary>Maior que zero, até o máximo e com no máximo duas casas decimais.</summary>
    public static bool PrecoValido(decimal preco) => preco > 0 && preco <= PrecoMaximo && decimal.Round(preco, 2) == preco;
}
