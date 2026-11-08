using System.Text.RegularExpressions;

namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Imagens de terceiros dos testes, lidas do <c>docker-compose.yml</c> (REQ-26, FR-019, research
/// R-08). O Dependabot atualiza o compose, e os testes passam a usar a mesma versão no mesmo PR,
/// sem cópia da tag no código. A leitura é linha a linha, só do bloco <c>services:</c> (serviço
/// com 2 espaços, <c>image:</c> com 4), sem biblioteca de YAML.
/// </summary>
internal static partial class ImagensDoAmbiente
{
    private static readonly Lazy<string> Compose = new(LerArquivoCompose);

    private static readonly Lazy<string> ImagemRabbitMq = new(() => Ler(Compose.Value, "rabbitmq", CaminhoCompose()));

    private static readonly Lazy<string> ImagemMailpit = new(() => Ler(Compose.Value, "mailpit", CaminhoCompose()));

    /// <summary>Imagem do serviço <c>rabbitmq</c> do compose.</summary>
    public static string RabbitMq => ImagemRabbitMq.Value;

    /// <summary>Imagem do serviço <c>mailpit</c> do compose.</summary>
    public static string Mailpit => ImagemMailpit.Value;

    /// <summary>
    /// Imagem do <paramref name="servico"/> no texto de um compose. Lança
    /// <see cref="InvalidOperationException"/> se o serviço ou a imagem não existir, ou se a
    /// imagem não tiver uma tag fixa.
    /// </summary>
    public static string Ler(string compose, string servico, string arquivo)
    {
        var dentroDeServicos = false;
        var noServico = false;
        string? imagem = null;

        foreach (var linha in compose.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (linha.Length == 0 || linha.TrimStart().StartsWith('#'))
            {
                continue;
            }

            if (!char.IsWhiteSpace(linha[0]))
            {
                // Chave de primeiro nível: só o bloco "services:" interessa.
                dentroDeServicos = linha.TrimEnd() == "services:";
                noServico = false;
                continue;
            }

            if (!dentroDeServicos)
            {
                continue;
            }

            var nomeServico = Servico().Match(linha);
            if (nomeServico.Success)
            {
                noServico = nomeServico.Groups["nome"].Value == servico;
                continue;
            }

            var chaveImagem = Imagem().Match(linha);
            if (noServico && chaveImagem.Success)
            {
                imagem = Limpar(chaveImagem.Groups["valor"].Value);
                break;
            }
        }

        if (string.IsNullOrEmpty(imagem))
        {
            throw new InvalidOperationException($"O serviço '{servico}' não tem 'image:' em {arquivo}.");
        }

        var ultimoTrecho = imagem[(imagem.LastIndexOf('/') + 1)..];
        if (!ultimoTrecho.Contains(':') && !imagem.Contains('@'))
        {
            throw new InvalidOperationException(
                $"A imagem '{imagem}' do serviço '{servico}' em {arquivo} precisa de uma tag fixa, sem 'latest'.");
        }

        if (ultimoTrecho.EndsWith(":latest", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"A imagem '{imagem}' do serviço '{servico}' em {arquivo} precisa de uma tag fixa, sem 'latest'.");
        }

        return imagem;
    }

    /// <summary>Tira as aspas e um comentário no fim da linha.</summary>
    private static string Limpar(string valor)
    {
        var semComentario = valor.Split(" #", 2)[0].Trim();
        return semComentario.Trim('"', '\'');
    }

    private static string LerArquivoCompose() => File.ReadAllText(CaminhoCompose());

    /// <summary>Sobe a partir da saída dos testes até achar o <c>docker-compose.yml</c> da raiz.</summary>
    private static string CaminhoCompose()
    {
        for (var diretorio = new DirectoryInfo(AppContext.BaseDirectory); diretorio is not null; diretorio = diretorio.Parent)
        {
            var candidato = Path.Combine(diretorio.FullName, "docker-compose.yml");
            if (File.Exists(candidato))
            {
                return candidato;
            }
        }

        throw new InvalidOperationException("docker-compose.yml não foi encontrado acima da pasta dos testes.");
    }

    [GeneratedRegex("^  (?<nome>[A-Za-z0-9._-]+):\\s*$")]
    private static partial Regex Servico();

    [GeneratedRegex("^    image:\\s*(?<valor>\\S.*)$")]
    private static partial Regex Imagem();
}
