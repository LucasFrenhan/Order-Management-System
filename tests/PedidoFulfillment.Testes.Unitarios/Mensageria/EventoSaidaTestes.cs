using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Eventos;
using PedidoFulfillment.Application.Mensageria;
using static PedidoFulfillment.Testes.Unitarios.Mensageria.SerializacaoEventosTestes;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Evento pendente do outbox (REQ-06, R-04): criação, falhas com espera crescente e envio.</summary>
public sealed class EventoSaidaTestes
{
    [Fact]
    public void De_cria_o_pendente_com_o_envelope_serializado()
    {
        var envelope = PedidoCriado();

        var evento = EventoSaida.De(envelope, Instante);

        evento.IdEvento.Should().Be(envelope.IdEvento);
        evento.Tipo.Should().Be(TiposEvento.PedidoCriado);
        evento.Conteudo.Should().Be(SerializacaoEventos.Serializar(envelope));
        evento.CriadoEm.Should().Be(Instante);
        evento.ProximaTentativaEm.Should().Be(Instante);
        evento.Tentativas.Should().Be(0);
        evento.EnviadoEm.Should().BeNull();
        evento.UltimoErro.Should().BeNull();
    }

    [Fact]
    public void Cada_falha_dobra_a_espera_ate_o_teto_de_10_segundos()
    {
        var evento = EventoSaida.De(PedidoCriado(), Instante);
        var esperas = new List<double>();

        for (var i = 0; i < 4; i++)
        {
            evento.RegistrarFalha("broker fora", Instante);
            esperas.Add((evento.ProximaTentativaEm - Instante).TotalSeconds);
        }

        esperas.Should().Equal(2, 4, 8, 10);
        evento.Tentativas.Should().Be(4);
        evento.UltimoErro.Should().Be("broker fora");
    }

    [Fact]
    public void Erro_longo_e_cortado_em_500_caracteres()
    {
        var evento = EventoSaida.De(PedidoCriado(), Instante);

        evento.RegistrarFalha(new string('e', 600), Instante);

        evento.UltimoErro.Should().HaveLength(500);
    }

    [Fact]
    public void MarcarEnviado_preenche_a_data()
    {
        var evento = EventoSaida.De(PedidoCriado(), Instante);

        evento.MarcarEnviado(Instante.AddSeconds(1));

        evento.EnviadoEm.Should().Be(Instante.AddSeconds(1));
    }
}
