namespace PedidoFulfillment.API;

/// <summary>
/// PR DESCARTÁVEL (REQ-26, SC-007): caminho de arquivo montado com um parâmetro da requisição, para
/// provar que um alerta novo de segurança alta do CodeQL bloqueia o merge. Nunca entra na main.
/// </summary>
public static class DescartavelCodeQl
{
    public static string LerArquivo(HttpContext contexto)
    {
        var nome = contexto.Request.Query["arquivo"].ToString();
        return File.ReadAllText(Path.Combine("/var/oms", nome));
    }
}
