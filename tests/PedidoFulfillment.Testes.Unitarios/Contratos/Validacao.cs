using System.ComponentModel.DataAnnotations;

namespace PedidoFulfillment.Testes.Unitarios.Contratos;

/// <summary>
/// Executa a validação de DataAnnotations + <see cref="IValidatableObject"/> de um DTO,
/// como o filtro da API faz, e devolve as mensagens por campo.
/// </summary>
internal static class Validacao
{
    public static Dictionary<string, List<string>> Validar(object dto)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(dto, new ValidationContext(dto), resultados, validateAllProperties: true);

        var erros = new Dictionary<string, List<string>>();
        foreach (var resultado in resultados)
        {
            foreach (var membro in resultado.MemberNames.DefaultIfEmpty(""))
            {
                if (!erros.TryGetValue(membro, out var lista))
                {
                    erros[membro] = lista = [];
                }

                lista.Add(resultado.ErrorMessage!);
            }
        }

        return erros;
    }
}
