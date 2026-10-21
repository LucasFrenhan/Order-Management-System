using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace PedidoFulfillment.Testes.Integracao.Contrato;

/// <summary>
/// Compara semanticamente o contrato aprovado com o documento gerado (research R-11):
/// parâmetros, corpo, respostas, segurança e schemas referenciados. Descrições e exemplos
/// não entram. O tipo <c>null</c> é ignorado na comparação de <c>type</c>.
/// </summary>
public sealed class ComparadorContrato(OpenApiDocument contrato, OpenApiDocument gerado)
{
    private readonly List<string> _diferencas = [];
    private readonly HashSet<string> _schemasComparados = [];

    public IReadOnlyList<string> CompararOperacao(HttpMethod metodo, string caminho)
    {
        var local = $"{metodo.Method} {caminho}";
        var operacaoContrato = Operacao(contrato, metodo, caminho);
        var operacaoGerada = Operacao(gerado, metodo, caminho);
        if (operacaoContrato is null || operacaoGerada is null)
        {
            Diferenca(local, $"operação ausente no {(operacaoContrato is null ? "contrato" : "documento gerado")}");
            return _diferencas;
        }

        CompararParametros(local, operacaoContrato, operacaoGerada);
        CompararCorpo(local, operacaoContrato.RequestBody, operacaoGerada.RequestBody);
        CompararRespostas(local, operacaoContrato.Responses, operacaoGerada.Responses);
        CompararSeguranca(local, operacaoContrato.Security, operacaoGerada.Security);
        return _diferencas;
    }

    public IReadOnlyList<string> CompararSchemaComponente(string nome)
    {
        IOpenApiSchema? schemaContrato = null;
        IOpenApiSchema? schemaGerado = null;
        contrato.Components?.Schemas?.TryGetValue(nome, out schemaContrato);
        gerado.Components?.Schemas?.TryGetValue(nome, out schemaGerado);
        if (schemaContrato is null || schemaGerado is null)
        {
            Diferenca($"#/components/schemas/{nome}", "schema ausente no " + (schemaContrato is null ? "contrato" : "documento gerado"));
            return _diferencas;
        }

        _schemasComparados.Add(nome);
        CompararSchema($"#/components/schemas/{nome}", schemaContrato, schemaGerado);
        return _diferencas;
    }

    public static IEnumerable<(HttpMethod Metodo, string Caminho)> Operacoes(OpenApiDocument documento) =>
        documento.Paths.SelectMany(p => (p.Value.Operations ?? []).Select(o => (o.Key, p.Key)));

    private static OpenApiOperation? Operacao(OpenApiDocument documento, HttpMethod metodo, string caminho) =>
        documento.Paths.TryGetValue(caminho, out var item) && item.Operations is not null
            && item.Operations.TryGetValue(metodo, out var operacao)
            ? operacao
            : null;

    private void CompararParametros(string local, OpenApiOperation contratoOp, OpenApiOperation geradaOp)
    {
        var parametrosContrato = (contratoOp.Parameters ?? []).ToDictionary(p => $"{p.In}:{p.Name}");
        var parametrosGerados = (geradaOp.Parameters ?? []).ToDictionary(p => $"{p.In}:{p.Name}");
        CompararChaves($"{local} parâmetros", parametrosContrato.Keys, parametrosGerados.Keys);

        foreach (var (chave, parametro) in parametrosContrato)
        {
            if (!parametrosGerados.TryGetValue(chave, out var gerado))
            {
                continue;
            }

            if (parametro.Required != gerado.Required)
            {
                Diferenca($"{local} parâmetro {chave}", $"required {parametro.Required} ≠ {gerado.Required}");
            }

            CompararSchemaOpcional($"{local} parâmetro {chave}", parametro.Schema, gerado.Schema);
        }
    }

    private void CompararCorpo(string local, IOpenApiRequestBody? corpoContrato, IOpenApiRequestBody? corpoGerado)
    {
        if (corpoContrato is null || corpoGerado is null)
        {
            if (corpoContrato is not null || corpoGerado is not null)
            {
                Diferenca($"{local} corpo", corpoContrato is null ? "não existe no contrato" : "não existe no documento gerado");
            }

            return;
        }

        if (corpoContrato.Required != corpoGerado.Required)
        {
            Diferenca($"{local} corpo", $"required {corpoContrato.Required} ≠ {corpoGerado.Required}");
        }

        CompararConteudo($"{local} corpo", corpoContrato.Content, corpoGerado.Content);
    }

    private void CompararRespostas(string local, OpenApiResponses? respostasContrato, OpenApiResponses? respostasGeradas)
    {
        respostasContrato ??= [];
        respostasGeradas ??= [];
        CompararChaves($"{local} respostas", respostasContrato.Keys, respostasGeradas.Keys);
        foreach (var (codigo, resposta) in respostasContrato)
        {
            if (respostasGeradas.TryGetValue(codigo, out var gerada))
            {
                CompararConteudo($"{local} resposta {codigo}", resposta.Content, gerada.Content);
            }
        }
    }

    private void CompararSeguranca(string local, IList<OpenApiSecurityRequirement>? contratoSeg, IList<OpenApiSecurityRequirement>? geradaSeg)
    {
        static string Descrever(IList<OpenApiSecurityRequirement>? requisitos) => string.Join(" | ",
            (requisitos ?? []).Select(r => string.Join(",", r.Keys.Select(k => k.Reference?.Id ?? k.Name).Order())));

        var esperado = Descrever(contratoSeg);
        var obtido = Descrever(geradaSeg);
        if (esperado != obtido)
        {
            Diferenca($"{local} segurança", $"'{esperado}' ≠ '{obtido}'");
        }
    }

    private void CompararConteudo(string local, IDictionary<string, OpenApiMediaType>? contratoConteudo, IDictionary<string, OpenApiMediaType>? geradoConteudo)
    {
        contratoConteudo ??= new Dictionary<string, OpenApiMediaType>();
        geradoConteudo ??= new Dictionary<string, OpenApiMediaType>();
        CompararChaves($"{local} content-types", contratoConteudo.Keys, geradoConteudo.Keys);
        foreach (var (tipo, midia) in contratoConteudo)
        {
            if (geradoConteudo.TryGetValue(tipo, out var gerada))
            {
                CompararSchemaOpcional($"{local} {tipo}", midia.Schema, gerada.Schema);
            }
        }
    }

    private void CompararSchemaOpcional(string local, IOpenApiSchema? schemaContrato, IOpenApiSchema? schemaGerado)
    {
        if (schemaContrato is null || schemaGerado is null)
        {
            if (schemaContrato is not null || schemaGerado is not null)
            {
                Diferenca(local, "schema presente só em um dos documentos");
            }

            return;
        }

        CompararSchema(local, schemaContrato, schemaGerado);
    }

    private void CompararSchema(string local, IOpenApiSchema schemaContrato, IOpenApiSchema schemaGerado)
    {
        var refContrato = (schemaContrato as OpenApiSchemaReference)?.Reference.Id;
        var refGerado = (schemaGerado as OpenApiSchemaReference)?.Reference.Id;
        if (refContrato != refGerado)
        {
            Diferenca(local, $"$ref '{refContrato ?? "(inline)"}' ≠ '{refGerado ?? "(inline)"}'");
            return;
        }

        if (refContrato is not null)
        {
            if (!_schemasComparados.Add(refContrato))
            {
                return;
            }

            local = $"#/components/schemas/{refContrato}";
        }

        Comparar(local, "type", SemNulo(schemaContrato.Type), SemNulo(schemaGerado.Type));
        Comparar(local, "format", schemaContrato.Format, schemaGerado.Format);
        Comparar(local, "pattern", schemaContrato.Pattern, schemaGerado.Pattern);
        Comparar(local, "minLength", schemaContrato.MinLength, schemaGerado.MinLength);
        Comparar(local, "maxLength", schemaContrato.MaxLength, schemaGerado.MaxLength);
        Comparar(local, "minItems", schemaContrato.MinItems, schemaGerado.MinItems);
        Comparar(local, "maxItems", schemaContrato.MaxItems, schemaGerado.MaxItems);
        Comparar(local, "minimum", Numero(schemaContrato.Minimum), Numero(schemaGerado.Minimum));
        Comparar(local, "maximum", Numero(schemaContrato.Maximum), Numero(schemaGerado.Maximum));
        Comparar(local, "required", Conjunto(schemaContrato.Required), Conjunto(schemaGerado.Required));
        Comparar(local, "enum", Conjunto(schemaContrato.Enum?.Select(ValorEnum)), Conjunto(schemaGerado.Enum?.Select(ValorEnum)));

        var propriedadesContrato = schemaContrato.Properties ?? new Dictionary<string, IOpenApiSchema>();
        var propriedadesGeradas = schemaGerado.Properties ?? new Dictionary<string, IOpenApiSchema>();
        CompararChaves($"{local} propriedades", propriedadesContrato.Keys, propriedadesGeradas.Keys);
        foreach (var (nome, propriedade) in propriedadesContrato)
        {
            if (propriedadesGeradas.TryGetValue(nome, out var gerada))
            {
                CompararSchema($"{local}.{nome}", propriedade, gerada);
            }
        }

        CompararSchemaOpcional($"{local}.items", schemaContrato.Items, schemaGerado.Items);
        CompararSchemaOpcional($"{local}.additionalProperties", schemaContrato.AdditionalProperties, schemaGerado.AdditionalProperties);

        var allOfContrato = schemaContrato.AllOf ?? [];
        var allOfGerado = schemaGerado.AllOf ?? [];
        Comparar(local, "allOf.count", allOfContrato.Count, allOfGerado.Count);
        for (var i = 0; i < Math.Min(allOfContrato.Count, allOfGerado.Count); i++)
        {
            CompararSchema($"{local}.allOf[{i}]", allOfContrato[i], allOfGerado[i]);
        }
    }

    private static JsonSchemaType? SemNulo(JsonSchemaType? tipo) => tipo is null ? null : tipo & ~JsonSchemaType.Null;

    private static string? Numero(string? valor) => valor is null ? null : decimal.Parse(valor, System.Globalization.CultureInfo.InvariantCulture).ToString(System.Globalization.CultureInfo.InvariantCulture);

    private static string ValorEnum(JsonNode? valor) => valor?.ToString() ?? "null";

    private static string Conjunto(IEnumerable<string>? valores) => string.Join(",", (valores ?? []).Order(StringComparer.Ordinal));

    private void CompararChaves(string local, IEnumerable<string> esperadas, IEnumerable<string> obtidas)
    {
        var faltando = esperadas.Except(obtidas).ToArray();
        var sobrando = obtidas.Except(esperadas).ToArray();
        if (faltando.Length > 0)
        {
            Diferenca(local, $"faltando no gerado: {string.Join(", ", faltando)}");
        }

        if (sobrando.Length > 0)
        {
            Diferenca(local, $"a mais no gerado: {string.Join(", ", sobrando)}");
        }
    }

    private void Comparar<T>(string local, string atributo, T esperado, T obtido)
    {
        if (!EqualityComparer<T>.Default.Equals(esperado, obtido))
        {
            Diferenca(local, $"{atributo}: contrato '{esperado}' ≠ gerado '{obtido}'");
        }
    }

    private void Diferenca(string local, string mensagem) => _diferencas.Add($"{local}: {mensagem}");
}
