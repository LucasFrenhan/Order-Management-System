using System.ComponentModel.DataAnnotations;
using FluentAssertions;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Domain.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>Validação dos parâmetros da listagem (REQ-21, research R-05, FR-021 e FR-022).</summary>
public sealed class ValidacaoListarPedidosTestes
{
    private static List<ValidationResult> Validar(ListarPedidosRequestDTO requisicao) =>
        [.. requisicao.Validate(new ValidationContext(requisicao))];

    private static void DeveTerUmErro(ListarPedidosRequestDTO requisicao, string membro, string mensagem)
    {
        var erros = Validar(requisicao);
        erros.Should().ContainSingle();
        erros[0].ErrorMessage.Should().Be(mensagem);
        erros[0].MemberNames.Should().Equal(membro);
    }

    [Fact]
    public void Sem_parametros_e_valido_com_tamanho_20_e_sem_filtros()
    {
        var requisicao = new ListarPedidosRequestDTO();

        Validar(requisicao).Should().BeEmpty();
        requisicao.TamanhoPagina().Should().Be(20);
        var filtro = requisicao.ParaFiltro();
        filtro.Status.Should().BeEmpty();
        filtro.NomesUsados().Should().Be("nenhum");
    }

    [Fact]
    public void Status_desconhecido_e_recusado()
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { Status = ["Enviado"] }, "status", "O status 'Enviado' não existe.");
    }

    [Theory]
    [InlineData("1")]
    [InlineData("99")]
    public void Status_numerico_e_recusado(string valor)
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { Status = [valor] }, "status", $"O status '{valor}' não existe.");
    }

    [Fact]
    public void Status_sem_diferenciar_maiusculas_e_repetido_conta_uma_vez()
    {
        var requisicao = new ListarPedidosRequestDTO { Status = ["prontoparaenvio", "ProntoParaEnvio", "PENDENTE"] };

        Validar(requisicao).Should().BeEmpty();
        requisicao.ParaFiltro().Status.Should().BeEquivalentTo([StatusPedido.ProntoParaEnvio, StatusPedido.Pendente]);
    }

    [Theory]
    [InlineData("criadoDe")]
    [InlineData("criadoAte")]
    [InlineData("atualizadoDe")]
    [InlineData("atualizadoAte")]
    public void Data_sem_fuso_ou_malformada_e_recusada(string parametro)
    {
        foreach (var valor in new[] { "2026-11-03T10:00:00", "2026-11-03", "ontem", "2026-13-40T10:00:00Z" })
        {
            DeveTerUmErro(ComData(parametro, valor), parametro,
                $"O parâmetro {parametro} deve ser uma data e hora com fuso, como 2026-11-03T10:00:00-03:00.");
        }
    }

    [Theory]
    [InlineData("2026-11-03T10:00:00Z")]
    [InlineData("2026-11-03T10:00:00-03:00")]
    [InlineData("2026-11-03T10:00:00.1234567+05:30")]
    [InlineData("2026-11-03T10:00-03:00")]
    public void Data_com_fuso_e_aceita_e_convertida_para_utc(string valor)
    {
        var requisicao = new ListarPedidosRequestDTO { CriadoDe = valor };

        Validar(requisicao).Should().BeEmpty();
        requisicao.ParaFiltro().CriadoDe!.Value.Offset.Should().Be(TimeSpan.Zero);
        requisicao.ParaFiltro().CriadoDe.Should().Be(DateTimeOffset.Parse(valor, System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public void Periodo_de_criacao_invertido_e_recusado()
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { CriadoDe = "2026-11-03T10:00:00Z", CriadoAte = "2026-11-03T06:59:59-03:00" },
            "criadoDe", "O início do período de criação deve ser anterior ou igual ao fim.");
    }

    [Fact]
    public void Periodo_de_ultima_alteracao_invertido_e_recusado()
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { AtualizadoDe = "2026-11-04T00:00:00Z", AtualizadoAte = "2026-11-03T00:00:00Z" },
            "atualizadoDe", "O início do período de última alteração deve ser anterior ou igual ao fim.");
    }

    [Fact]
    public void Periodo_com_inicio_igual_ao_fim_e_aceito()
    {
        Validar(new ListarPedidosRequestDTO { CriadoDe = "2026-11-03T10:00:00Z", CriadoAte = "2026-11-03T07:00:00-03:00" }).Should().BeEmpty();
    }

    [Fact]
    public void Cliente_que_nao_e_uuid_e_recusado()
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { ClienteId = "abc" }, "clienteId", "O parâmetro clienteId deve ser um UUID.");
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("1.5")]
    public void Tamanho_fora_do_permitido_e_recusado(string tamanho)
    {
        DeveTerUmErro(new ListarPedidosRequestDTO { Tamanho = tamanho }, "tamanho", "O tamanho deve ser um número entre 1 e 100.");
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("100", 100)]
    public void Tamanho_nos_limites_e_aceito(string tamanho, int esperado)
    {
        var requisicao = new ListarPedidosRequestDTO { Tamanho = tamanho };

        Validar(requisicao).Should().BeEmpty();
        requisicao.TamanhoPagina().Should().Be(esperado);
    }

    [Fact]
    public void Varios_erros_voltam_juntos()
    {
        var erros = Validar(new ListarPedidosRequestDTO { Tamanho = "0", Status = ["Enviado"], CriadoDe = "2026-11-03T10:00:00", ClienteId = "x" });

        erros.SelectMany(e => e.MemberNames).Should().BeEquivalentTo(["tamanho", "status", "criadoDe", "clienteId"]);
    }

    [Fact]
    public void Filtro_convertido_e_nomes_usados_sem_valores()
    {
        var cliente = Guid.NewGuid();
        var requisicao = new ListarPedidosRequestDTO
        {
            Status = ["Cancelado"],
            AtualizadoAte = "2026-11-02T23:59:59-03:00",
            ClienteId = cliente.ToString()
        };

        var filtro = requisicao.ParaFiltro();

        filtro.ClienteId.Should().Be(cliente);
        filtro.AtualizadoAte.Should().Be(new DateTimeOffset(2026, 11, 3, 2, 59, 59, TimeSpan.Zero));
        filtro.NomesUsados().Should().Be("status,atualizadoAte,clienteId").And.NotContain(cliente.ToString());
    }

    private static ListarPedidosRequestDTO ComData(string parametro, string valor) => parametro switch
    {
        "criadoDe" => new ListarPedidosRequestDTO { CriadoDe = valor },
        "criadoAte" => new ListarPedidosRequestDTO { CriadoAte = valor },
        "atualizadoDe" => new ListarPedidosRequestDTO { AtualizadoDe = valor },
        _ => new ListarPedidosRequestDTO { AtualizadoAte = valor }
    };
}
