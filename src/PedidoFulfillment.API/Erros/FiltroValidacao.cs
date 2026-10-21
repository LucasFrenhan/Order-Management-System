using System.Collections;
using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace PedidoFulfillment.API.Erros;

/// <summary>
/// Valida o DTO <typeparamref name="T"/> com DataAnnotations e <see cref="IValidatableObject"/>,
/// inclusive itens de coleções, e devolve 400 com <c>erros</c> (R-08, FR-010).
/// </summary>
public sealed class FiltroValidacao<T> : IEndpointFilter where T : class
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var dto = context.Arguments.OfType<T>().FirstOrDefault();
        var erros = new Dictionary<string, List<string>>();

        if (dto is null)
        {
            Adicionar(erros, "", "O corpo da requisição é obrigatório.");
        }
        else
        {
            Validar(dto, "", erros);
        }

        if (erros.Count > 0)
        {
            var problema = RespostasErro.Validacao(erros.ToDictionary(e => e.Key, e => e.Value.ToArray()));
            return TypedResults.Problem(problema);
        }

        return await next(context);
    }

    private static void Validar(object objeto, string prefixo, Dictionary<string, List<string>> erros)
    {
        var resultados = new List<ValidationResult>();
        Validator.TryValidateObject(objeto, new ValidationContext(objeto), resultados, validateAllProperties: true);

        foreach (var resultado in resultados)
        {
            var membros = resultado.MemberNames.DefaultIfEmpty("");
            foreach (var membro in membros)
            {
                Adicionar(erros, Caminho(prefixo, membro), resultado.ErrorMessage ?? "Valor inválido.");
            }
        }

        foreach (var propriedade in objeto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (propriedade.PropertyType == typeof(string) || !typeof(IEnumerable).IsAssignableFrom(propriedade.PropertyType)
                || propriedade.GetValue(objeto) is not IEnumerable itens)
            {
                continue;
            }

            var indice = 0;
            foreach (var item in itens)
            {
                if (item is not null && !item.GetType().IsPrimitive && !item.GetType().IsEnum && item is not string)
                {
                    Validar(item, $"{Caminho(prefixo, propriedade.Name)}[{indice}]", erros);
                }

                indice++;
            }
        }
    }

    private static string Caminho(string prefixo, string membro)
    {
        if (membro.Length == 0)
        {
            return prefixo;
        }

        var nome = char.ToLowerInvariant(membro[0]) + membro[1..];
        return prefixo.Length == 0 ? nome : $"{prefixo}.{nome}";
    }

    private static void Adicionar(Dictionary<string, List<string>> erros, string chave, string mensagem)
    {
        if (!erros.TryGetValue(chave, out var lista))
        {
            erros[chave] = lista = [];
        }

        if (!lista.Contains(mensagem))
        {
            lista.Add(mensagem);
        }
    }
}
