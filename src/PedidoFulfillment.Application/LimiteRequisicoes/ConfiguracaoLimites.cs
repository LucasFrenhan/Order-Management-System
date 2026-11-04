namespace PedidoFulfillment.Application.LimiteRequisicoes;

/// <summary>
/// Configuração pronta do limite de requisições, montada pela Infrastructure a partir das opções
/// validadas na partida: a Application não depende de <c>Options</c> (plano do REQ-23).
/// </summary>
public sealed record ConfiguracaoLimites(
    PoliticaLimite LoginOrigemEmail,
    PoliticaLimite LoginOrigem,
    PoliticaLimite Registro,
    byte[] ChaveContagem,
    TimeSpan TempoMaximoEspera,
    TimeSpan PausaAposFalha);
