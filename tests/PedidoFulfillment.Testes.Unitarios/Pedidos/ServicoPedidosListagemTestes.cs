using FluentAssertions;
using Microsoft.Extensions.Logging;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Pedidos;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;

namespace PedidoFulfillment.Testes.Unitarios.Pedidos;

/// <summary>Regras da listagem (REQ-21): escopo, 403 do filtro por Cliente, cursores e log.</summary>
public sealed class ServicoPedidosListagemTestes
{
    private static readonly Guid Dono = Guid.Parse("0199f000-0000-7000-8000-000000000001");
    private static readonly Guid Outro = Guid.Parse("0199f000-0000-7000-8000-000000000002");
    private static readonly DateTimeOffset Base = new(2026, 11, 3, 12, 0, 0, TimeSpan.Zero);

    private readonly ArmazenamentoPedidosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoListagemPedidos> logger = new();
    private readonly CodificadorCursorPedidos codificador = new(new CodificadorCursorPedidosTestes.ChaveFixa("chave-de-teste-com-mais-de-32-bytes!"));

    private ServicoListagemPedidos Servico() => new(armazenamento, codificador, logger);

    private Task<Resultado<PaginaPedidosResponseDTO>> Listar(Guid usuario, ListarPedidosRequestDTO? requisicao = null, params PerfilUsuario[] perfis) =>
        Servico().ListarAsync(requisicao ?? new ListarPedidosRequestDTO(), usuario, perfis, CancellationToken.None);

    /// <summary>Pedidos 1..n do dono, um por minuto; o maior número é o mais recente.</summary>
    private void Pedidos(Guid cliente, int quantidade, int inicio = 1)
    {
        for (var i = inicio; i < inicio + quantidade; i++)
        {
            armazenamento.Resumos.Add(Resumo(cliente, i, Base.AddMinutes(i)));
        }
    }

    private static ResumoPedidoResponseDTO Resumo(Guid cliente, int numero, DateTimeOffset criadoEm, StatusPedido status = StatusPedido.Pendente) => new()
    {
        Id = Guid.CreateVersion7(),
        Numero = $"PED-20261103-{numero:D6}",
        ClienteId = cliente,
        Status = status,
        ValorTotal = 10m,
        QuantidadeItens = 1,
        CriadoEm = criadoEm,
        AtualizadoEm = criadoEm
    };

    private static IEnumerable<string> Numeros(PaginaPedidosResponseDTO pagina) => pagina.Itens.Select(i => i.Numero[^2..]);

    [Fact]
    public async Task Cliente_ve_so_os_proprios_pedidos_do_mais_recente_ao_mais_antigo()
    {
        Pedidos(Dono, 3);
        Pedidos(Outro, 2, inicio: 4);

        var pagina = (await Listar(Dono, perfis: PerfilUsuario.Cliente)).Valor!;

        Numeros(pagina).Should().Equal("03", "02", "01");
        pagina.Proximo.Should().BeNull();
        pagina.Anterior.Should().BeNull();
        pagina.Tamanho.Should().Be(20);
        armazenamento.Listagens.Single().Filtro.ClienteId.Should().Be(Dono);
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Operacao_ve_todos_os_clientes(PerfilUsuario perfil)
    {
        Pedidos(Dono, 2);
        Pedidos(Outro, 2, inicio: 3);

        var pagina = (await Listar(Guid.NewGuid(), perfis: perfil)).Valor!;

        pagina.Itens.Should().HaveCount(4);
        armazenamento.Listagens.Single().Filtro.ClienteId.Should().BeNull();
    }

    [Fact]
    public async Task Cliente_que_tambem_e_da_operacao_tem_o_escopo_da_operacao()
    {
        Pedidos(Outro, 2);

        var pagina = (await Listar(Dono, perfis: [PerfilUsuario.Cliente, PerfilUsuario.Admin])).Valor!;

        pagina.Itens.Should().HaveCount(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cliente_com_filtro_por_cliente_recebe_403_sem_consultar(bool proprio)
    {
        var requisicao = new ListarPedidosRequestDTO { ClienteId = (proprio ? Dono : Outro).ToString(), Cursor = "cursor-invalido" };

        var resultado = await Listar(Dono, requisicao, PerfilUsuario.Cliente);

        resultado.Falha.Should().BeEquivalentTo(new { Tipo = TipoFalha.SemPermissao, Titulo = "Operação não permitida.", Detalhe = "O filtro por cliente é exclusivo da operação." });
        armazenamento.Listagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Operacao_com_filtro_por_cliente_e_aceita()
    {
        Pedidos(Dono, 2);
        Pedidos(Outro, 3, inicio: 3);

        var pagina = (await Listar(Guid.NewGuid(), new ListarPedidosRequestDTO { ClienteId = Outro.ToString() }, PerfilUsuario.OperadorLogistica)).Valor!;

        Numeros(pagina).Should().Equal("05", "04", "03");
    }

    [Fact]
    public async Task Navega_para_a_frente_e_para_tras_sem_repetir()
    {
        Pedidos(Dono, 45);
        var tamanho = new ListarPedidosRequestDTO { Tamanho = "20" };

        var p1 = (await Listar(Dono, tamanho, PerfilUsuario.Cliente)).Valor!;
        var p2 = (await Listar(Dono, tamanho with { Cursor = p1.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var p3 = (await Listar(Dono, tamanho with { Cursor = p2.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var volta2 = (await Listar(Dono, tamanho with { Cursor = p3.Anterior }, PerfilUsuario.Cliente)).Valor!;
        var volta1 = (await Listar(Dono, tamanho with { Cursor = volta2.Anterior }, PerfilUsuario.Cliente)).Valor!;

        (p1.Itens.Count, p2.Itens.Count, p3.Itens.Count).Should().Be((20, 20, 5));
        p1.Itens.Concat(p2.Itens).Concat(p3.Itens).Select(i => i.Numero).Should().OnlyHaveUniqueItems().And.HaveCount(45);
        p1.Anterior.Should().BeNull();
        p3.Proximo.Should().BeNull();
        p2.Proximo.Should().NotBeNull();
        p2.Anterior.Should().NotBeNull();
        Numeros(volta2).Should().Equal(Numeros(p2));
        volta2.Proximo.Should().NotBeNull();
        volta2.Anterior.Should().NotBeNull();
        Numeros(volta1).Should().Equal(Numeros(p1));
        volta1.Anterior.Should().BeNull();
    }

    [Fact]
    public async Task Empate_de_criacao_na_divisa_entre_paginas_nao_perde_nem_repete()
    {
        for (var i = 1; i <= 5; i++)
        {
            armazenamento.Resumos.Add(Resumo(Dono, i, Base));
        }

        var p1 = (await Listar(Dono, new ListarPedidosRequestDTO { Tamanho = "2" }, PerfilUsuario.Cliente)).Valor!;
        var p2 = (await Listar(Dono, new ListarPedidosRequestDTO { Tamanho = "2", Cursor = p1.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var p3 = (await Listar(Dono, new ListarPedidosRequestDTO { Tamanho = "2", Cursor = p2.Proximo }, PerfilUsuario.Cliente)).Valor!;

        Numeros(p1).Concat(Numeros(p2)).Concat(Numeros(p3)).Should().Equal("05", "04", "03", "02", "01");
    }

    [Fact]
    public async Task Pagina_vazia_vinda_de_cursor_tem_volta_que_inclui_a_posicao()
    {
        Pedidos(Dono, 4);
        var filtro = new ListarPedidosRequestDTO { Tamanho = "2", Status = ["Pendente"] };
        var p1 = (await Listar(Dono, filtro, PerfilUsuario.Cliente)).Valor!;

        // Os dois mais antigos saem do filtro no meio da navegação.
        foreach (var numero in new[] { 1, 2 })
        {
            var indice = armazenamento.Resumos.FindIndex(r => r.Numero.EndsWith($"{numero:D6}", StringComparison.Ordinal));
            armazenamento.Resumos[indice] = armazenamento.Resumos[indice] with { Status = StatusPedido.Cancelado };
        }

        var vazia = (await Listar(Dono, filtro with { Cursor = p1.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var volta = (await Listar(Dono, filtro with { Cursor = vazia.Anterior }, PerfilUsuario.Cliente)).Valor!;

        vazia.Itens.Should().BeEmpty();
        vazia.Proximo.Should().BeNull();
        vazia.Anterior.Should().NotBeNull();
        Numeros(volta).Should().Equal(Numeros(p1));
    }

    [Fact]
    public async Task Cursor_invalido_da_400_com_erro_no_cursor()
    {
        Pedidos(Dono, 3);

        var resultado = await Listar(Dono, new ListarPedidosRequestDTO { Cursor = "abc.def" }, PerfilUsuario.Cliente);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.Validacao);
        resultado.Falha.Erros.Should().ContainKey("cursor").WhoseValue.Should().Equal("O cursor é inválido para esta consulta; recomece da primeira página.");
        armazenamento.Listagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Cursor_da_operacao_nao_serve_ao_cliente()
    {
        Pedidos(Dono, 3);
        var operacao = Guid.NewGuid();
        var daOperacao = (await Listar(operacao, new ListarPedidosRequestDTO { Tamanho = "1" }, PerfilUsuario.Admin)).Valor!;

        var resultado = await Listar(Dono, new ListarPedidosRequestDTO { Tamanho = "1", Cursor = daOperacao.Proximo }, PerfilUsuario.Cliente);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.Validacao);
    }

    [Fact]
    public async Task Pede_um_a_mais_que_o_tamanho()
    {
        await Listar(Dono, new ListarPedidosRequestDTO { Tamanho = "7" }, PerfilUsuario.Cliente);

        armazenamento.Listagens.Single().Quantidade.Should().Be(8);
    }

    [Fact]
    public async Task Log_4006_sai_uma_vez_sem_valores_de_filtro()
    {
        Pedidos(Outro, 3);
        var requisicao = new ListarPedidosRequestDTO
        {
            Status = ["Pendente"],
            CriadoDe = "2026-11-01T00:00:00-03:00",
            ClienteId = Outro.ToString()
        };

        await Listar(Guid.NewGuid(), requisicao, PerfilUsuario.OperadorLogistica);

        var evento = logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4006).Subject;
        evento.Nivel.Should().Be(LogLevel.Debug);
        evento.Id.Name.Should().Be("PedidosListados");
        evento.Propriedades["Escopo"].Should().Be("Operacao");
        evento.Propriedades["Filtros"].Should().Be("status,criadoDe,clienteId");
        evento.Propriedades["Sentido"].Should().Be("Inicio");
        evento.Propriedades["Quantidade"].Should().Be(3);
        evento.Mensagem.Should().NotContain(Outro.ToString()).And.NotContain("2026-11-01").And.NotContain("Pendente");
    }
}
