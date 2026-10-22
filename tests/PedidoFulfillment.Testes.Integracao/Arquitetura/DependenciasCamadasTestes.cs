using System.Reflection;
using System.Xml.Linq;
using FluentAssertions;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure;
using PedidoFulfillment.Worker;

namespace PedidoFulfillment.Testes.Integracao.Arquitetura;

/// <summary>
/// Direção de dependências entre camadas (FR-002, FR-003). As referências vêm de duas
/// fontes: os metadados dos assemblies e os <c>ProjectReference</c> dos <c>.csproj</c>, porque
/// o compilador omite dos metadados referências de projeto não usadas (research R-06).
/// </summary>
public sealed class DependenciasCamadasTestes
{
    private const string Domain = "PedidoFulfillment.Domain";
    private const string Application = "PedidoFulfillment.Application";
    private const string Infrastructure = "PedidoFulfillment.Infrastructure";
    private const string Api = "PedidoFulfillment.API";

    // REQ-06: o Worker é uma camada de hospedagem, no mesmo nível da API.
    private const string Worker = "PedidoFulfillment.Worker";

    private static readonly Dictionary<string, string[]> Permitidas = new()
    {
        [Domain] = [],
        [Application] = [Domain],
        [Infrastructure] = [Application, Domain],
        [Api] = [Application, Infrastructure, Domain],
        [Worker] = [Application, Infrastructure, Domain]
    };

    private static readonly Dictionary<string, Assembly> Assemblies = new()
    {
        [Domain] = typeof(StatusPedido).Assembly,
        [Application] = typeof(IServicoPedidos).Assembly,
        [Infrastructure] = typeof(ExtensoesInfraestrutura).Assembly,
        [Api] = typeof(Program).Assembly,
        [Worker] = typeof(ProgramaWorker).Assembly
    };

    [Fact]
    public void A_solucao_tem_exatamente_as_cinco_camadas()
    {
        Directory.GetDirectories(Path.Combine(RaizRepositorio(), "src")).Select(Path.GetFileName)
            .Should().BeEquivalentTo(Domain, Application, Infrastructure, Api, Worker);
    }

    [Fact]
    public void API_e_Worker_nao_se_referenciam()
    {
        var referencias = ReferenciasReais();

        referencias[Api].Should().NotContain(Worker);
        referencias[Worker].Should().NotContain(Api);
    }

    [Fact]
    public void Nenhuma_camada_referencia_outra_fora_da_direcao_permitida()
    {
        Violacoes(ReferenciasReais()).Should().BeEmpty();
    }

    [Fact]
    public void Referencia_proibida_e_apontada_pelo_nome()
    {
        var referencias = new Dictionary<string, IReadOnlyCollection<string>>
        {
            [Domain] = [Infrastructure],
            [Application] = [Domain]
        };

        Violacoes(referencias).Should().Equal($"{Domain} → {Infrastructure} não é permitida");
    }

    private static IReadOnlyList<string> Violacoes(IReadOnlyDictionary<string, IReadOnlyCollection<string>> referencias) =>
        referencias
            .SelectMany(camada => camada.Value
                .Where(alvo => Permitidas.ContainsKey(alvo) && alvo != camada.Key)
                .Where(alvo => !Permitidas.TryGetValue(camada.Key, out var permitidas) || !permitidas.Contains(alvo))
                .Select(alvo => $"{camada.Key} → {alvo} não é permitida"))
            .Order(StringComparer.Ordinal)
            .ToList();

    private static Dictionary<string, IReadOnlyCollection<string>> ReferenciasReais()
    {
        var raiz = RaizRepositorio();
        return Permitidas.Keys.ToDictionary(
            camada => camada,
            IReadOnlyCollection<string> (camada) => Assemblies[camada].GetReferencedAssemblies().Select(a => a.Name!)
                .Concat(ReferenciasDeProjeto(Path.Combine(raiz, "src", camada, $"{camada}.csproj")))
                .ToHashSet());
    }

    private static IEnumerable<string> ReferenciasDeProjeto(string caminhoCsproj) =>
        XDocument.Load(caminhoCsproj).Descendants("ProjectReference")
            .Select(r => Path.GetFileNameWithoutExtension(r.Attribute("Include")!.Value.Replace('\\', Path.DirectorySeparatorChar)));

    private static string RaizRepositorio()
    {
        var diretorio = new DirectoryInfo(AppContext.BaseDirectory);
        while (diretorio is not null && !File.Exists(Path.Combine(diretorio.FullName, "PedidoFulfillment.sln")))
        {
            diretorio = diretorio.Parent;
        }

        return diretorio?.FullName ?? throw new InvalidOperationException("PedidoFulfillment.sln não encontrado.");
    }
}
