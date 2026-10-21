using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace PedidoFulfillment.Testes.Integracao.Contrato;

/// <summary>
/// Carrega o contrato aprovado (YAML) e o documento gerado pela API (JSON) no mesmo modelo
/// de objetos do Microsoft.OpenApi (research R-11).
/// </summary>
public static class LeitorContrato
{
    public static string CaminhoContrato => Path.Combine(AppContext.BaseDirectory, "Contrato", "openapi-v1.yaml");

    public static async Task<OpenApiDocument> CarregarContratoAsync()
    {
        await using var arquivo = File.OpenRead(CaminhoContrato);
        return await CarregarAsync(arquivo, OpenApiConstants.Yaml);
    }

    public static async Task<OpenApiDocument> CarregarGeradoAsync(HttpClient cliente)
    {
        await using var conteudo = await cliente.GetStreamAsync("/openapi/v1.json");
        return await CarregarAsync(conteudo, OpenApiConstants.Json);
    }

    private static async Task<OpenApiDocument> CarregarAsync(Stream conteudo, string formato)
    {
        var configuracao = new OpenApiReaderSettings();
        configuracao.AddYamlReader();
        var resultado = await OpenApiDocument.LoadAsync(conteudo, formato, configuracao);
        if (resultado.Document is null || resultado.Diagnostic?.Errors.Count > 0)
        {
            var erros = string.Join("; ", resultado.Diagnostic?.Errors.Select(e => $"{e.Pointer}: {e.Message}") ?? []);
            throw new InvalidOperationException($"Documento OpenAPI ({formato}) inválido: {erros}");
        }

        return resultado.Document;
    }
}
