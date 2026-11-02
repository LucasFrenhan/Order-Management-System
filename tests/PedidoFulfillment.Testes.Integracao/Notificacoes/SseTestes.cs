using System.Net;
using System.Text.Json;
using FluentAssertions;
using PedidoFulfillment.Domain.Usuarios;
using PedidoFulfillment.Testes.Integracao.Autenticacao;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;
using static PedidoFulfillment.Testes.Integracao.Pedidos.CenarioPedidos;

namespace PedidoFulfillment.Testes.Integracao.Notificacoes;

/// <summary>
/// Eventos em tempo real pela API, lidos do banco, sem broker (REQ-18, US3 cenários 1 a 14;
/// FR-019 a FR-024; analyze G1 e U1).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class SseTestes(BancoDadosTeste banco)
{
    /// <summary>Leitura a cada 1 s: o aviso chega em poucos segundos.</summary>
    private static readonly TimeSpan Prazo = TimeSpan.FromSeconds(8);

    private static FabricaApi ApiSse(string stringConexao, IReadOnlyDictionary<string, string?>? extras = null)
    {
        var configuracoes = new Dictionary<string, string?>(UsuariosIniciaisComBancoTestes.Configuracao())
        {
            ["Notificacoes:Sse:IntervaloLeituraSegundos"] = "1",
            ["Notificacoes:Sse:SinalDeVidaSegundos"] = "5"
        };
        foreach (var (chave, valor) in extras ?? new Dictionary<string, string?>())
        {
            configuracoes[chave] = valor;
        }

        return new FabricaApi { PrepararBanco = true, StringConexao = stringConexao, Configuracoes = configuracoes };
    }

    private static Guid Pedido(string data)
    {
        using var corpo = JsonDocument.Parse(data);
        return corpo.RootElement.GetProperty("dados").GetProperty("pedidoId").GetGuid();
    }

    [Fact]
    public async Task Sem_token_401_sem_perfil_403_pedido_invalido_400_e_de_outro_Cliente_404()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var id = (await CriadoAsync(dono, (Cat001, 1))).GetProperty("id").GetGuid();

        (await api.CreateClient().GetAsync(LeitorSseTeste.Rota, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await api.CreateClient().ComToken().GetAsync(LeitorSseTeste.Rota, TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await outro.GetAsync($"{LeitorSseTeste.Rota}?pedidoId=nao-e-guid", TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await using var deOutro = await LeitorSseTeste.AbrirAsync(outro, id);
        deOutro.Status.Should().Be(HttpStatusCode.NotFound);
        await using var inexistente = await LeitorSseTeste.AbrirAsync(dono, Guid.CreateVersion7());
        inexistente.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Conectado_vem_primeiro_e_o_Cliente_so_recebe_os_proprios_pedidos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (dono, _, _) = await ClienteAsync(api);
        var (outro, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        await using var doDono = await LeitorSseTeste.AbrirAsync(dono);
        await using var daOperacao = await LeitorSseTeste.AbrirAsync(operador);

        var conectado = (await doDono.ProximoAsync(Prazo))!.Value;
        conectado.EventType.Should().Be("conectado");
        conectado.EventId.Should().Be(Guid.Empty.ToString(), "a tabela de avisos está vazia");
        (await daOperacao.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");

        var pedidoOutro = (await CriadoAsync(outro, (Cat001, 1))).GetProperty("id").GetGuid();
        var pedidoDono = (await CriadoAsync(dono, (Cat002, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(operador, pedidoOutro, "Processando")).EnsureSuccessStatusCode();
        (await MudarStatusAsync(operador, pedidoDono, "Processando")).EnsureSuccessStatusCode();

        var aviso = (await doDono.ProximoAvisoAsync(Prazo))!.Value;
        aviso.EventType.Should().Be("pedido.status-alterado");
        Pedido(aviso.Data).Should().Be(pedidoDono, "o aviso do outro Cliente não chega");
        SchemaNotificacaoTestes.DeveSerValido(aviso.Data).Should().BeTrue();
        using (var corpo = JsonDocument.Parse(aviso.Data))
        {
            aviso.EventId.Should().Be(corpo.RootElement.GetProperty("idNotificacao").GetString());
        }

        var paraOperacao = new[] { (await daOperacao.ProximoAvisoAsync(Prazo))!.Value, (await daOperacao.ProximoAvisoAsync(Prazo))!.Value };
        paraOperacao.Select(a => Pedido(a.Data)).Should().Equal(pedidoOutro, pedidoDono);
    }

    [Fact]
    public async Task Conexao_de_um_pedido_so_recebe_esse_pedido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var a = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var b = (await CriadoAsync(cliente, (Cat002, 1))).GetProperty("id").GetGuid();
        await using var deB = await LeitorSseTeste.AbrirAsync(cliente, b);
        (await deB.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");

        (await MudarStatusAsync(operador, a, "Processando")).EnsureSuccessStatusCode();
        (await MudarStatusAsync(operador, b, "Processando")).EnsureSuccessStatusCode();

        Pedido((await deB.ProximoAvisoAsync(Prazo))!.Value.Data).Should().Be(b);
    }

    [Fact]
    public async Task Mudanca_entre_o_conectado_e_o_GET_chega_pela_conexao()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente);
        (await conexao.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");

        (await MudarStatusAsync(operador, id, "Processando")).EnsureSuccessStatusCode();
        (await CorpoAsync(await cliente.GetAsync($"{Rota}/{id}", TestContext.Current.CancellationToken))).GetProperty("status").GetString().Should().Be("Processando");

        Pedido((await conexao.ProximoAvisoAsync(Prazo))!.Value.Data).Should().Be(id);
    }

    [Fact]
    public async Task Reconexao_recebe_os_perdidos_em_ordem_antes_dos_novos_sem_repetir()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var a = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var b = (await CriadoAsync(cliente, (Cat002, 1))).GetProperty("id").GetGuid();
        string ultimo;
        await using (var primeira = await LeitorSseTeste.AbrirAsync(cliente))
        {
            (await primeira.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");
            (await MudarStatusAsync(operador, a, "Processando")).EnsureSuccessStatusCode();
            ultimo = (await primeira.ProximoAvisoAsync(Prazo))!.Value.EventId!;
        }

        (await MudarStatusAsync(operador, a, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        (await MudarStatusAsync(operador, b, "Processando")).EnsureSuccessStatusCode();

        await using var segunda = await LeitorSseTeste.AbrirAsync(cliente, ultimoEvento: ultimo);
        var perdidos = new[] { (await segunda.ProximoAvisoAsync(Prazo))!.Value, (await segunda.ProximoAvisoAsync(Prazo))!.Value };
        (await MudarStatusAsync(operador, b, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        var novo = (await segunda.ProximoAvisoAsync(Prazo))!.Value;

        perdidos.Select(p => Pedido(p.Data)).Should().Equal(a, b);
        perdidos[0].Data.Should().Contain("ProntoParaEnvio");
        Pedido(novo.Data).Should().Be(b);
        novo.Data.Should().Contain("\"statusNovo\":\"ProntoParaEnvio\"");
        (await segunda.ProximoAvisoAsync(TimeSpan.FromSeconds(3))).Should().BeNull("nada se repete");
    }

    [Fact]
    public async Task Reconexao_de_um_pedido_conta_so_os_avisos_dele()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco(), new Dictionary<string, string?> { ["Notificacoes:Sse:MaximoReenvio"] = "2" });
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var alvo = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        var outros = new List<Guid>();
        for (var i = 0; i < 3; i++)
        {
            outros.Add((await CriadoAsync(cliente, (Cat002, 1))).GetProperty("id").GetGuid());
        }

        foreach (var outro in outros)
        {
            (await MudarStatusAsync(operador, outro, "Processando")).EnsureSuccessStatusCode();
        }

        (await MudarStatusAsync(operador, alvo, "Processando")).EnsureSuccessStatusCode();

        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente, alvo, Guid.Empty.ToString());
        var item = (await conexao.ProximoAvisoAsync(Prazo))!.Value;

        item.EventType.Should().Be("pedido.status-alterado", "só um aviso do pedido: abaixo do máximo de 2");
        Pedido(item.Data).Should().Be(alvo);
    }

    [Theory]
    [InlineData("desconhecido", "AvisoDesconhecido")]
    [InlineData("acima-do-limite", "LimiteReenvio")]
    public async Task Sem_como_reenviar_chega_sincronizacao_necessaria_e_depois_so_os_novos(string caso, string motivo)
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco(), new Dictionary<string, string?> { ["Notificacoes:Sse:MaximoReenvio"] = "1" });
        var (cliente, _, _) = await ClienteAsync(api);
        var operador = await OperadorAsync(api);
        var a = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        (await MudarStatusAsync(operador, a, "Processando")).EnsureSuccessStatusCode();
        (await MudarStatusAsync(operador, a, "ProntoParaEnvio")).EnsureSuccessStatusCode();
        var ultimo = caso == "desconhecido" ? Guid.CreateVersion7().ToString() : Guid.Empty.ToString();

        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente, ultimoEvento: ultimo);
        var sincronizacao = (await conexao.ProximoAvisoAsync(Prazo))!.Value;
        (await MudarStatusAsync(operador, a, "Despachado")).EnsureSuccessStatusCode();
        var novo = (await conexao.ProximoAvisoAsync(Prazo))!.Value;

        sincronizacao.EventType.Should().Be("sincronizacao-necessaria");
        sincronizacao.Data.Should().Contain(motivo);
        novo.Data.Should().Contain("\"statusNovo\":\"Despachado\"");
    }

    [Fact]
    public async Task Last_Event_ID_que_nao_e_GUID_e_tratado_como_desconhecido()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);

        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente, ultimoEvento: "abc");

        (await conexao.ProximoAvisoAsync(Prazo))!.Value.EventType.Should().Be("sincronizacao-necessaria");
    }

    [Fact]
    public async Task Sinal_de_vida_chega_sem_avisos()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente);
        (await conexao.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");

        (await conexao.ProximoAsync(TimeSpan.FromSeconds(10)))!.Value.EventType.Should().Be("sinal");
    }

    [Fact]
    public async Task Quarta_conexao_do_mesmo_usuario_recebe_429()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var (cliente, _, _) = await ClienteAsync(api);
        var abertas = new List<LeitorSseTeste>();
        try
        {
            for (var i = 0; i < 3; i++)
            {
                abertas.Add(await LeitorSseTeste.AbrirAsync(cliente));
                abertas[^1].Status.Should().Be(HttpStatusCode.OK);
            }

            await using var quarta = await LeitorSseTeste.AbrirAsync(cliente);

            quarta.Status.Should().Be(HttpStatusCode.TooManyRequests);
            (await CodigoAsync(quarta.Resposta)).Should().Be("NOTIFICACAO_SSE_LIMITE_CONEXOES");
        }
        finally
        {
            foreach (var aberta in abertas)
            {
                await aberta.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task A_conexao_termina_no_exp_do_token()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        await using var api = ApiSse(banco.NovoBanco());
        var cliente = api.CreateClient().ComToken(GeradorTokenTeste.Gerar(perfis: [PerfilUsuario.Cliente], validade: TimeSpan.FromSeconds(3)));

        await using var conexao = await LeitorSseTeste.AbrirAsync(cliente);

        conexao.Status.Should().Be(HttpStatusCode.OK);
        (await conexao.EncerradoAsync(TimeSpan.FromSeconds(10))).Should().BeTrue();
    }

    [Fact]
    public async Task Com_o_broker_inalcancavel_o_aviso_chega_e_duas_instancias_recebem()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = banco.NovoBanco();
        var semBroker = new Dictionary<string, string?> { ["RabbitMQ:Porta"] = "1" };
        await using var primeira = ApiSse(stringConexao, semBroker);
        await using var segunda = ApiSse(stringConexao, semBroker);
        var operador = await OperadorAsync(primeira);
        var (cliente, _, _) = await ClienteAsync(primeira);
        var id = (await CriadoAsync(cliente, (Cat001, 1))).GetProperty("id").GetGuid();
        await using var naPrimeira = await LeitorSseTeste.AbrirAsync(operador);
        await using var naSegunda = await LeitorSseTeste.AbrirAsync(segunda.CreateClient().ComToken(PerfilUsuario.OperadorLogistica));
        (await naPrimeira.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");
        (await naSegunda.ProximoAsync(Prazo))!.Value.EventType.Should().Be("conectado");

        (await MudarStatusAsync(operador, id, "Processando")).EnsureSuccessStatusCode();

        Pedido((await naPrimeira.ProximoAvisoAsync(Prazo))!.Value.Data).Should().Be(id);
        Pedido((await naSegunda.ProximoAvisoAsync(Prazo))!.Value.Data).Should().Be(id);
    }
}
