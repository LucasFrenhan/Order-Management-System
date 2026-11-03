using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Produtos;
using PedidoFulfillment.Application.Produtos;
using PedidoFulfillment.Domain.Usuarios;

namespace PedidoFulfillment.Testes.Unitarios.Produtos;

/// <summary>REQ-22, User Story 1: visões por perfil, 403 da situação, 404 do inativo e cursor.</summary>
public sealed class ServicoCatalogoLeituraTestes : ServicoCatalogoTestesBase
{
    private static readonly Guid Cliente = Guid.Parse("0199f000-0000-7000-8000-000000000001");

    private Task<Resultado<PaginaProdutosResponseDTO>> Listar(Guid usuario, ListarProdutosRequestDTO? requisicao = null, params PerfilUsuario[] perfis) =>
        Servico().ListarAsync(requisicao ?? new ListarProdutosRequestDTO(), usuario, perfis, CancellationToken.None);

    [Fact]
    public async Task Cliente_ve_so_os_ativos_pelo_nome_sem_os_campos_da_operacao()
    {
        Armazenamento.Guardar("B-1", "Banana");
        Armazenamento.Guardar("A-1", "Abacate", estoque: 0);
        Armazenamento.Guardar("C-1", "Caju", ativo: false);

        var pagina = (await Listar(Cliente, perfis: PerfilUsuario.Cliente)).Valor!;

        pagina.Itens.Select(i => i.Nome).Should().Equal("Abacate", "Banana");
        pagina.Itens.Should().OnlyContain(i => i.QuantidadeEstoque == null && i.Ativo == null && i.CriadoEm == null && i.AtualizadoEm == null);
        pagina.Itens.Select(i => i.Disponivel).Should().Equal(false, true);
        Armazenamento.Listagens.Single().Filtro.Situacao.Should().Be(SituacaoProduto.Ativos);
    }

    [Theory]
    [InlineData(PerfilUsuario.Admin)]
    [InlineData(PerfilUsuario.OperadorLogistica)]
    public async Task Operacao_ve_todos_com_quantidade_situacao_e_datas(PerfilUsuario perfil)
    {
        Armazenamento.Guardar("B-1", "Banana", estoque: 7);
        Armazenamento.Guardar("C-1", "Caju", ativo: false);

        var pagina = (await Listar(Admin, perfis: perfil)).Valor!;
        var inativos = (await Listar(Admin, new ListarProdutosRequestDTO { Situacao = "Inativos" }, perfil)).Valor!;

        pagina.Itens.Select(i => (i.Nome, i.QuantidadeEstoque, i.Ativo)).Should().Equal(("Banana", 7, true), ("Caju", 5, false));
        pagina.Itens.Should().OnlyContain(i => i.CriadoEm != null && i.AtualizadoEm != null);
        inativos.Itens.Select(i => i.Nome).Should().Equal("Caju");
    }

    [Fact]
    public async Task Cliente_com_filtro_de_situacao_recebe_403_sem_consultar()
    {
        var resultado = await Listar(Cliente, new ListarProdutosRequestDTO { Situacao = "Ativos" }, PerfilUsuario.Cliente);

        resultado.Falha.Should().BeEquivalentTo(new { Tipo = TipoFalha.SemPermissao, Detalhe = "O filtro por situação é exclusivo da operação." });
        Armazenamento.Listagens.Should().BeEmpty();
    }

    [Fact]
    public async Task Inativo_e_inexistente_dao_o_mesmo_404_ao_cliente_e_a_operacao_ve_o_inativo()
    {
        var inativo = Armazenamento.Guardar("C-1", "Caju", ativo: false);

        var doInativo = await Servico().ObterAsync(inativo.Id, [PerfilUsuario.Cliente], CancellationToken.None);
        var doInexistente = await Servico().ObterAsync(Guid.NewGuid(), [PerfilUsuario.Cliente], CancellationToken.None);
        var daOperacao = await Servico().ObterAsync(inativo.Id, [PerfilUsuario.OperadorLogistica], CancellationToken.None);

        doInativo.Falha.Should().BeEquivalentTo(doInexistente.Falha);
        doInativo.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
        daOperacao.Valor!.Ativo.Should().BeFalse();
    }

    [Fact]
    public async Task Navega_nos_dois_sentidos_e_cursor_de_outro_usuario_da_400()
    {
        for (var i = 1; i <= 5; i++)
        {
            Armazenamento.Guardar($"P-{i}", $"Produto {i}");
        }

        var tamanho = new ListarProdutosRequestDTO { Tamanho = "2" };
        var p1 = (await Listar(Cliente, tamanho, PerfilUsuario.Cliente)).Valor!;
        var p2 = (await Listar(Cliente, tamanho with { Cursor = p1.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var p3 = (await Listar(Cliente, tamanho with { Cursor = p2.Proximo }, PerfilUsuario.Cliente)).Valor!;
        var volta = (await Listar(Cliente, tamanho with { Cursor = p3.Anterior }, PerfilUsuario.Cliente)).Valor!;
        var deOutro = await Listar(Guid.NewGuid(), tamanho with { Cursor = p1.Proximo }, PerfilUsuario.Cliente);

        p1.Itens.Concat(p2.Itens).Concat(p3.Itens).Select(i => i.Sku).Should().Equal("P-1", "P-2", "P-3", "P-4", "P-5");
        (p1.Anterior, p3.Proximo).Should().Be((null, null));
        volta.Itens.Select(i => i.Sku).Should().Equal("P-3", "P-4");
        deOutro.Falha!.Erros.Should().ContainKey("cursor");
    }
}
