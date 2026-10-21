using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using PedidoFulfillment.Application.Contratos.Autenticacao;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Infrastructure.Configuracao;

/// <summary>
/// Seção <c>UsuariosIniciais</c>: contas Admin e OperadorLogistica criadas na inicialização
/// (FR-012, contracts/configuracao.md). Vazia, nenhuma conta é criada.
/// </summary>
public sealed class OpcoesUsuariosIniciais
{
    public const string Secao = "UsuariosIniciais";

    public List<UsuarioInicial> Usuarios { get; } = [];
}

public sealed class UsuarioInicial
{
    public string Nome { get; set; } = "";

    public string Email { get; set; } = "";

    public string Senha { get; set; } = "";

    /// <summary>Texto, e não enum, para que um valor desconhecido dê uma mensagem própria.</summary>
    public string Perfil { get; set; } = "";
}

/// <summary>
/// Valida cada usuário com as regras do registro público (research R-09). As mensagens citam a
/// chave e nunca o valor (FR-012).
/// </summary>
public sealed class ValidadorOpcoesUsuariosIniciais : IValidateOptions<OpcoesUsuariosIniciais>
{
    private static readonly string[] PerfisPermitidos = [nameof(PerfilUsuario.Admin), nameof(PerfilUsuario.OperadorLogistica)];

    public ValidateOptionsResult Validate(string? name, OpcoesUsuariosIniciais options)
    {
        var falhas = new List<string>();
        for (var indice = 0; indice < options.Usuarios.Count; indice++)
        {
            Validar(options.Usuarios[indice], $"{OpcoesUsuariosIniciais.Secao}:{indice}", falhas);
        }

        return falhas.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(falhas);
    }

    private static void Validar(UsuarioInicial usuario, string chave, List<string> falhas)
    {
        var obrigatorios = new (string Campo, string Valor)[]
        {
            (nameof(UsuarioInicial.Nome), usuario.Nome),
            (nameof(UsuarioInicial.Email), usuario.Email),
            (nameof(UsuarioInicial.Senha), usuario.Senha),
            (nameof(UsuarioInicial.Perfil), usuario.Perfil)
        };
        var ausentes = obrigatorios.Where(c => string.IsNullOrWhiteSpace(c.Valor)).Select(c => c.Campo).ToArray();
        falhas.AddRange(ausentes.Select(campo => $"A configuração {chave}:{campo} é obrigatória."));
        if (!ausentes.Contains(nameof(UsuarioInicial.Perfil)) && !PerfisPermitidos.Contains(usuario.Perfil))
        {
            falhas.Add($"A configuração {chave}:Perfil deve ser Admin ou OperadorLogistica.");
        }

        if (ausentes.Except([nameof(UsuarioInicial.Perfil)]).Any())
        {
            return;
        }

        // Mesmas regras do registro público: uma só fonte para nome, e-mail e senha (research R-04).
        var registro = new RegistrarUsuarioRequestDTO { Nome = usuario.Nome, Email = usuario.Email.Trim(), Senha = usuario.Senha };
        var erros = new List<ValidationResult>();
        Validator.TryValidateObject(registro, new ValidationContext(registro), erros, validateAllProperties: true);
        falhas.AddRange(erros.SelectMany(erro => erro.MemberNames.Select(membro => $"A configuração {chave}:{membro} é inválida: {erro.ErrorMessage}")));
    }
}
