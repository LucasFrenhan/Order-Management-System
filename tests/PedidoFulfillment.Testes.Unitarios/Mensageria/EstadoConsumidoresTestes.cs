using FluentAssertions;
using PedidoFulfillment.Infrastructure.Mensageria;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Estado de inscrição por consumidor (REQ-17, data-model; pausa do REQ-19).</summary>
public sealed class EstadoConsumidoresTestes
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Consumidor_desconhecido_nao_tem_pausa_nem_perda()
    {
        var estado = new EstadoConsumidores([new Descritor("estoque")]);

        estado.PausadoAte("outro").Should().BeNull();
        estado.SemInscricaoDesde("outro").Should().BeNull();
    }

    [Fact]
    public void Perda_de_um_consumidor_fora_dos_descritores_passa_a_ser_acompanhada()
    {
        var estado = new EstadoConsumidores([new Descritor("estoque")]);

        estado.MarcarSemInscricao("avulso", T0);
        estado.MarcarSemInscricao("avulso", T0.AddMinutes(1));

        estado.SemInscricaoDesde("avulso").Should().Be(T0, "o instante da primeira perda não é adiado");
    }

    [Fact]
    public void Sem_descritores_nunca_esta_pronto()
    {
        new EstadoConsumidores([]).TodosInscritos.Should().BeFalse();
    }

    private sealed class Descritor(string consumidor) : IDescritorConsumidor
    {
        public string Consumidor => consumidor;

        public IReadOnlyCollection<string> Tipos => [];
    }
}
