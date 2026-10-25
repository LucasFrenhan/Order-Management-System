using FluentAssertions;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Contratos.Administracao;
using PedidoFulfillment.Application.Mensageria;
using PedidoFulfillment.Testes.Unitarios.Autenticacao;
using PedidoFulfillment.Testes.Unitarios.Pedidos;

namespace PedidoFulfillment.Testes.Unitarios.Mensageria;

/// <summary>Registro e consulta dos pedidos de reenvio pela API (REQ-14, R-03).</summary>
public sealed class ServicoReenvioDlqTestes
{
    private static readonly DateTimeOffset Agora = new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Admin = Guid.CreateVersion7();

    private readonly ArmazenamentoReenviosFalso armazenamento = new();
    private readonly LoggerFalso<ServicoReenvioDlq> logger = new();

    private ServicoReenvioDlq Servico() => new(armazenamento, new RelogioFixo(Agora), logger);

    [Fact]
    public async Task Registra_o_pedido_pendente_com_o_autor_e_o_filtro()
    {
        var filtro = new RegistrarReenvioDlqRequestDTO { TipoEvento = "pedido.criado", TrechoMotivo = " Timeout " };

        var resultado = await Servico().RegistrarAsync(Admin, "estoque", filtro, CancellationToken.None);

        var pedido = resultado.Valor!;
        (pedido.Situacao, pedido.AutorId, pedido.Consumidor, pedido.RegistradoEm).Should().Be((SituacaoReenvioDlq.Pendente, Admin, "estoque", Agora));
        (pedido.TipoEvento, pedido.TrechoMotivo).Should().Be(("pedido.criado", "Timeout"));
        armazenamento.Gravados.Should().ContainSingle(p => p.Id == pedido.Id);
        logger.Eventos.Should().ContainSingle(e => e.Id.Id == 4006).Which.Propriedades["AutorId"].Should().Be(Admin);
    }

    [Fact]
    public async Task Consumidor_desconhecido_e_erro_de_validacao_sem_gravar()
    {
        var resultado = await Servico().RegistrarAsync(Admin, "desconhecido", new RegistrarReenvioDlqRequestDTO(), CancellationToken.None);

        resultado.Falha!.Tipo.Should().Be(TipoFalha.Validacao);
        resultado.Falha.Detalhe.Should().Be("O consumidor deve ser estoque, pagamento ou entrega.");
        armazenamento.Gravados.Should().BeEmpty();
    }

    [Fact]
    public void O_DTO_valida_periodo_e_tamanhos()
    {
        var invalido = new RegistrarReenvioDlqRequestDTO
        {
            FalhouDe = Agora,
            FalhouAte = Agora.AddSeconds(-1),
            TipoEvento = new string('t', 101),
            TrechoMotivo = new string('m', 201)
        };

        invalido.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(invalido)).SelectMany(r => r.MemberNames)
            .Should().BeEquivalentTo("FalhouAte", "TipoEvento", "TrechoMotivo");
        new RegistrarReenvioDlqRequestDTO().Validate(new System.ComponentModel.DataAnnotations.ValidationContext(new object())).Should().BeEmpty();
    }

    [Fact]
    public async Task Consulta_devolve_o_pedido_ou_nao_encontrado()
    {
        var registrado = (await Servico().RegistrarAsync(Admin, "pagamento", new RegistrarReenvioDlqRequestDTO(), CancellationToken.None)).Valor!;

        (await Servico().ObterAsync(registrado.Id, CancellationToken.None)).Valor!.Id.Should().Be(registrado.Id);
        var inexistente = await Servico().ObterAsync(Guid.CreateVersion7(), CancellationToken.None);
        inexistente.Falha!.Tipo.Should().Be(TipoFalha.NaoEncontrado);
        inexistente.Falha.Titulo.Should().Be("Pedido de reenvio não encontrado.");
    }

    private sealed class ArmazenamentoReenviosFalso : IArmazenamentoReenvios
    {
        private readonly List<PedidoReenvioDlq> pendentes = [];

        public List<PedidoReenvioDlq> Gravados { get; } = [];

        public void Adicionar(PedidoReenvioDlq pedido) => pendentes.Add(pedido);

        public Task<PedidoReenvioDlq?> ObterAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Gravados.SingleOrDefault(p => p.Id == id));

        public Task<IReadOnlyList<PedidoReenvioDlq>> ListarParaExecucaoAsync(int quantidade, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PedidoReenvioDlq>>([.. Gravados.Take(quantidade)]);

        public Task SalvarAsync(CancellationToken cancellationToken)
        {
            Gravados.AddRange(pendentes);
            pendentes.Clear();
            return Task.CompletedTask;
        }
    }
}
