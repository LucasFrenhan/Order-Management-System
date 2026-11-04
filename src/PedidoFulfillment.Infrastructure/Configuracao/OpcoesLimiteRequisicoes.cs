using System.Net;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.LimiteRequisicoes;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>Limite e janela de uma política (FR-018).</summary>
public sealed class OpcoesPoliticaLimite
{
    public int Limite { get; set; }

    public int JanelaSegundos { get; set; }
}

/// <summary>
/// Configuração da seção <c>LimiteRequisicoes</c> (REQ-23, contrato › Configuração). Só a
/// <see cref="ChaveContagem"/> não tem padrão. Não existe opção para desligar o limite (FR-018).
/// </summary>
public sealed class OpcoesLimiteRequisicoes
{
    public const string Secao = "LimiteRequisicoes";

    /// <summary>Base64 de 32 bytes, igual em todas as instâncias. Trocá-la só zera as contagens (R-08).</summary>
    public string ChaveContagem { get; set; } = "";

    public OpcoesPoliticaLimite LoginOrigemEmail { get; set; } = new() { Limite = 10, JanelaSegundos = 60 };

    public OpcoesPoliticaLimite LoginOrigem { get; set; } = new() { Limite = 100, JanelaSegundos = 60 };

    public OpcoesPoliticaLimite Registro { get; set; } = new() { Limite = 20, JanelaSegundos = 3600 };

    /// <summary>Acima disso, a requisição segue sem o limite (FR-012, R-04).</summary>
    public int TempoMaximoEsperaMs { get; set; } = 250;

    /// <summary>Tempo sem tentar o armazenamento depois de uma falha; zero tenta sempre (R-04).</summary>
    public int PausaAposFalhaSegundos { get; set; } = 5;

    /// <summary>IPs e faixas CIDR que podem informar a origem por <c>X-Forwarded-For</c> (FR-004, R-06).</summary>
    public List<string> IntermediariosConfiaveis { get; set; } = [];

    /// <summary>A configuração que a Application usa. Só depois de validada.</summary>
    public ConfiguracaoLimites ParaConfiguracao() => new(
        new PoliticaLimite(PoliticaLimite.LoginOrigemEmail, LoginOrigemEmail.Limite, LoginOrigemEmail.JanelaSegundos),
        new PoliticaLimite(PoliticaLimite.LoginOrigem, LoginOrigem.Limite, LoginOrigem.JanelaSegundos),
        new PoliticaLimite(PoliticaLimite.Registro, Registro.Limite, Registro.JanelaSegundos),
        Convert.FromBase64String(ChaveContagem),
        TimeSpan.FromMilliseconds(TempoMaximoEsperaMs),
        TimeSpan.FromSeconds(PausaAposFalhaSegundos));
}

/// <summary>Acumula todas as falhas, nomeando cada chave sem mostrar valores.</summary>
public sealed class ValidadorOpcoesLimiteRequisicoes : IValidateOptions<OpcoesLimiteRequisicoes>
{
    private const int TamanhoChave = 32;
    private const int LimiteMaximo = 1_000_000;
    private const int JanelaMaxima = 86_400;

    public ValidateOptionsResult Validate(string? name, OpcoesLimiteRequisicoes options)
    {
        var faixas = new FaixasOpcoes(OpcoesLimiteRequisicoes.Secao);
        if (!ChaveValida(options.ChaveContagem))
        {
            faixas.Falhar($"{OpcoesLimiteRequisicoes.Secao}:{nameof(OpcoesLimiteRequisicoes.ChaveContagem)} deve ser o Base64 de {TamanhoChave} bytes.");
        }

        Politica(faixas, options.LoginOrigemEmail, nameof(OpcoesLimiteRequisicoes.LoginOrigemEmail));
        Politica(faixas, options.LoginOrigem, nameof(OpcoesLimiteRequisicoes.LoginOrigem));
        Politica(faixas, options.Registro, nameof(OpcoesLimiteRequisicoes.Registro));
        faixas.Limitar(options.TempoMaximoEsperaMs, 50, 5000, nameof(OpcoesLimiteRequisicoes.TempoMaximoEsperaMs));
        faixas.Limitar(options.PausaAposFalhaSegundos, 0, 300, nameof(OpcoesLimiteRequisicoes.PausaAposFalhaSegundos));

        for (var i = 0; i < options.IntermediariosConfiaveis.Count; i++)
        {
            if (!IntermediarioValido(options.IntermediariosConfiaveis[i]))
            {
                faixas.Falhar($"{OpcoesLimiteRequisicoes.Secao}:{nameof(OpcoesLimiteRequisicoes.IntermediariosConfiaveis)}:{i} deve ser um endereço IP ou uma faixa CIDR.");
            }
        }

        return faixas.Resultado();
    }

    private static void Politica(FaixasOpcoes faixas, OpcoesPoliticaLimite politica, string nome)
    {
        faixas.Limitar(politica.Limite, 1, LimiteMaximo, $"{nome}:{nameof(OpcoesPoliticaLimite.Limite)}");
        faixas.Limitar(politica.JanelaSegundos, 1, JanelaMaxima, $"{nome}:{nameof(OpcoesPoliticaLimite.JanelaSegundos)}");
    }

    private static bool ChaveValida(string? chave)
    {
        Span<byte> bytes = stackalloc byte[TamanhoChave + 1];
        return Convert.TryFromBase64String(chave ?? "", bytes, out var escritos) && escritos == TamanhoChave;
    }

    private static bool IntermediarioValido(string? valor) =>
        IPAddress.TryParse(valor, out _) || IPNetwork.TryParse(valor, out _);
}
