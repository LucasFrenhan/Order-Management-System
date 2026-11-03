using DotNet.Testcontainers.Builders;
using Testcontainers.MsSql;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// SQL Server dos testes com Full-Text Search (REQ-22, research R-02): a mesma imagem do compose,
/// montada a partir de <c>docker/sqlserver/Dockerfile</c> uma vez por execução e compartilhada
/// pelos fixtures. Sem Docker, a montagem lança <see cref="DockerUnavailableException"/>, que os
/// fixtures já tratam como antes (REQ-07).
/// </summary>
internal static class ImagemSqlServerTeste
{
    public const string Nome = "oms-sqlserver-testes:2022-CU26-fts";

    private static readonly Lazy<Task> Montagem = new(MontarAsync, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Espera a imagem existir e devolve o construtor do contêiner.</summary>
    public static async Task<MsSqlBuilder> ConstrutorAsync()
    {
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
