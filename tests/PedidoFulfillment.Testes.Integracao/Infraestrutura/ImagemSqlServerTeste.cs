using System.Security.Cryptography;
using System.Text;
using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// SQL Server dos testes com Full-Text Search (REQ-22, research R-02): a mesma imagem do compose,
/// montada a partir de <c>docker/sqlserver/Dockerfile</c> uma vez por execução e compartilhada
/// pelos fixtures. Sem Docker, a montagem lança <see cref="DockerUnavailableException"/>, que os
/// fixtures já tratam como antes (REQ-07).
/// A tag é o hash do conteúdo de <c>docker/sqlserver</c> (REQ-26, research R-08): quando o
/// Dependabot troca a base do <c>Dockerfile</c>, o nome muda e a imagem é montada de novo, em vez
/// de reaproveitar uma imagem antiga com o mesmo nome.
/// </summary>
internal static class ImagemSqlServerTeste
{
    private static readonly Lazy<string> NomeCalculado = new(() => $"oms-sqlserver-testes:{HashDaPasta(DiretorioDockerfile())}");

    public static string Nome => NomeCalculado.Value;

    /// <summary>
    /// Imagem já montada (por exemplo, publicada no GHCR pelo CI): usada como está, sem montar
    /// nada (estudo docs/otimizacao-ci.md, proposta 1).
    /// </summary>
    private static readonly string? ImagemPronta = Environment.GetEnvironmentVariable("OMS_IMAGEM_SQLSERVER");

    private static readonly Lazy<Task> Montagem = new(MontarAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Espera a imagem existir e devolve o construtor do contêiner.</summary>
    public static async Task<MsSqlBuilder> ConstrutorAsync()
    {
        if (!string.IsNullOrWhiteSpace(ImagemPronta))
        {
            return new MsSqlBuilder(ImagemPronta);
        }

        await Montagem.Value;
        return new MsSqlBuilder(Nome);
    }

    private static async Task MontarAsync()
    {
        var imagem = new ImageFromDockerfileBuilder()
            .WithDockerfileDirectory(DiretorioDockerfile())
            .WithDockerfile("Dockerfile")
            .WithName(Nome)
            .WithCleanUp(false)
            .WithDeleteIfExists(false)
            .Build();
        await imagem.CreateAsync();
    }

    /// <summary>
    /// 16 primeiros caracteres hexadecimais do SHA-256 dos arquivos da pasta, em ordem de caminho
    /// relativo (caminho e conteúdo de cada um).
    /// </summary>
    internal static string HashDaPasta(string pasta)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var arquivos = Directory.GetFiles(pasta, "*", SearchOption.AllDirectories)
            .Select(arquivo => (Relativo: Path.GetRelativePath(pasta, arquivo).Replace('\\', '/'), Arquivo: arquivo))
            .OrderBy(item => item.Relativo, StringComparer.Ordinal);
        foreach (var (relativo, arquivo) in arquivos)
        {
            sha.AppendData(Encoding.UTF8.GetBytes(relativo + "\n"));
            // Fim de linha normalizado: o mesmo hash no Windows (CRLF no checkout) e no Linux.
            var conteudo = File.ReadAllText(arquivo).Replace("\r\n", "\n", StringComparison.Ordinal);
            sha.AppendData(Encoding.UTF8.GetBytes(conteudo));
        }

        return Convert.ToHexStringLower(sha.GetHashAndReset())[..16];
    }

    /// <summary>Sobe a partir da saída dos testes até achar <c>docker/sqlserver/Dockerfile</c>.</summary>
    private static string DiretorioDockerfile()
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            var candidato = Path.Combine(diretorio.FullName, "docker", "sqlserver");
            if (File.Exists(Path.Combine(candidato, "Dockerfile")))
            {
                return candidato;
            }
        }

        throw new InvalidOperationException("docker/sqlserver/Dockerfile não foi encontrado acima da pasta dos testes.");
    }
}
