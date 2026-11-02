using PedidoFulfillment.Infrastructure.Observabilidade;

namespace PedidoFulfillment.Infrastructure.Pagamentos;

/// <summary>Estado do circuito do gateway (REQ-19, data-model › OMS em memória).</summary>
internal enum SituacaoCircuito
{
    Fechado = 0,
    Aberto = 1,
    MeioAberto = 2
}

/// <summary>
/// Espelho do circuito do pipeline de resiliência (REQ-19, research R-06 e R-12): o
/// <see cref="GatewayPagamentoHttp"/> lê o <see cref="DisponivelEm"/> para a exceção que pausa o
/// consumo, e a métrica <c>oms.pagamentos.circuito</c>, a situação. Por instância do Worker.
/// </summary>
internal sealed class EstadoGatewayPagamento(TimeProvider relogio)
{
    private readonly Lock trava = new();
    private SituacaoCircuito situacao = SituacaoCircuito.Fechado;
    private DateTimeOffset? disponivelEm;

    public SituacaoCircuito Situacao
    {
        get
        {
            lock (trava)
            {
                return situacao;
            }
        }
    }

    /// <summary>Até quando o circuito fica aberto; nulo se fechado.</summary>
    public DateTimeOffset? DisponivelEm
    {
        get
        {
            lock (trava)
            {
                return disponivelEm;
            }
        }
    }

    public DateTimeOffset Abrir(TimeSpan pausa)
    {
        lock (trava)
        {
            situacao = SituacaoCircuito.Aberto;
            disponivelEm = relogio.GetUtcNow() + pausa;
            EstadoMetricas.RegistrarCircuitoGateway((long)situacao);
            return disponivelEm.Value;
        }
    }

    public void MeioAbrir() => Mudar(SituacaoCircuito.MeioAberto);

    public void Fechar() => Mudar(SituacaoCircuito.Fechado);

    /// <summary>Registra o circuito fechado na métrica quando o gateway HTTP é criado.</summary>
    public void Iniciar() => EstadoMetricas.RegistrarCircuitoGateway((long)Situacao);

    private void Mudar(SituacaoCircuito nova)
    {
        lock (trava)
        {
            situacao = nova;
            if (nova == SituacaoCircuito.Fechado)
            {
                disponivelEm = null;
            }

            EstadoMetricas.RegistrarCircuitoGateway((long)situacao);
        }
    }
}
