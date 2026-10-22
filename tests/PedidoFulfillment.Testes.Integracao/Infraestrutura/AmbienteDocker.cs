namespace PedidoFulfillment.Testes.Integracao.Infraestrutura;

/// <summary>
/// Exigência de Docker para os testes com banco ou broker reais (REQ-07, research R-06). No
/// CI, <c>OMS_EXIGIR_DOCKER=true</c> faz a falta de Docker reprovar os testes em vez de
/// ignorá-los; localmente, sem a variável, eles continuam ignorados.
/// </summary>
public static class AmbienteDocker
{
    public const string Variavel = "OMS_EXIGIR_DOCKER";

    public const string MensagemObrigatorio = "Docker obrigatório neste ambiente (OMS_EXIGIR_DOCKER=true).";

    public static bool Obrigatorio() => Obrigatorio(Environment.GetEnvironmentVariable(Variavel));

    public static bool Obrigatorio(string? valor) => string.Equals(valor, "true", StringComparison.OrdinalIgnoreCase);
}
