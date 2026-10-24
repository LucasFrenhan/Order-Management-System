using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// Modelo e migrations sincronizados (FR-016), sem banco: o EF compara o modelo com o snapshot.
/// </summary>
public sealed class ModeloSincronizadoTestes
{
    private static ContextoBancoDados NovoContexto() =>
        new(new DbContextOptionsBuilder<ContextoBancoDados>().UseSqlServer(FabricaApi.StringConexaoTeste).Options);

    [Fact]
    public void Nao_ha_alteracao_de_modelo_sem_migration()
    {
        using var contexto = NovoContexto();

        contexto.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public void A_migration_de_negocio_vem_depois_da_do_Identity()
    {
        using var contexto = NovoContexto();

        contexto.Database.GetMigrations().Select(m => m[(m.IndexOf('_') + 1)..])
            .Should().Equal("InicialIdentidade", "EntidadesNegocio", "ChavesIdempotencia", "MensageriaOutbox", "ReservaEstoque", "RastreioSemCaixa", "PagamentoNaoProcessado", "EstornoPagamento");
    }
}
