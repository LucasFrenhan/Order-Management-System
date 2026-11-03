using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PedidoFulfillment.Application.Comum;
using PedidoFulfillment.Application.Pedidos;
using PedidoFulfillment.Domain.Entregas;
using PedidoFulfillment.Domain.Pedidos;
using PedidoFulfillment.Infrastructure.Persistencia;
using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Persistencia;

/// <summary>
/// Conflitos que só aparecem na gravação do <see cref="ArmazenamentoPedidos"/>: outro pedido
/// gravou o mesmo rastreio ou a ordem do mesmo pedido entre a leitura e a gravação (REQ-11, R-06
/// e R-07), e o deadlock, dentro e fora de uma transação (REQ-05, R-04; REQ-08, R-07).
/// </summary>
[Collection(nameof(ColecaoBancoDados))]
public sealed class ConflitosGravacaoTestes(BancoDadosTeste banco)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static DateTimeOffset Depois => DadosPersistencia.Instante.AddMinutes(1);

    private static ContextoBancoDados ContextoComDeadlock(string stringConexao) =>
        new(new DbContextOptionsBuilder<ContextoBancoDados>()
            .UseSqlServer(stringConexao)
            .AddInterceptors(new FalhaGravacaoSimulada(ErroSqlSimulado.Criar(ErroSqlSimulado.Deadlock)))
            .Options);

    /// <summary>Um pedido pronto para envio, sem ordem de entrega, e o seu cliente.</summary>
    private static async Task<(Guid PedidoId, Guid ClienteId)> ProntoParaEnvioAsync(string stringConexao)
    {
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var clienteId = await DadosPersistencia.NovoUsuarioAsync(contexto);
        var produto = DadosPersistencia.NovoProduto();
        var pedido = DadosPersistencia.NovoPedido(clienteId, produto);
        pedido.MudarStatus(StatusPedido.ProntoParaEnvio, null, null, DadosPersistencia.Instante);
        contexto.Produtos.Add(produto);
        contexto.Pedidos.Add(pedido);
        await contexto.SaveChangesAsync(Token);
        return (pedido.Id, clienteId);
    }

    private static async Task DespacharAsync(ContextoBancoDados contexto, Guid pedidoId, Guid autorId, string codigo)
    {
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
        pedido.RegistrarRastreio("Correios", codigo, autorId, Depois);
        await armazenamento.SalvarAsync(Token);
    }

    [Theory]
    [InlineData(2601, true)]
    [InlineData(2627, true)]
    [InlineData(ErroSqlSimulado.Deadlock, false)]
    public void Indice_unico_e_chave_unica_sao_violacao_de_unicidade(int numero, bool unicidade)
    {
        // Sem banco: só a classificação dos números de erro do SQL Server.
        var excecao = new DbUpdateException("x", ErroSqlSimulado.Criar(numero));

        ErrosBancoDados.EhViolacaoUnicidade(excecao).Should().Be(unicidade);
        ErrosBancoDados.EhDeadlock(excecao).Should().Be(!unicidade);
    }

    [Fact]
    public async Task Rastreio_gravado_por_outro_pedido_antes_da_gravacao_e_duplicado()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (primeiro, autor) = await ProntoParaEnvioAsync(stringConexao);
        var (segundo, _) = await ProntoParaEnvioAsync(stringConexao);
        await using (var contexto = BancoDadosTeste.NovoContexto(stringConexao))
        {
            await DespacharAsync(contexto, primeiro, autor, "BR123");
        }

        await using var outro = BancoDadosTeste.NovoContexto(stringConexao);
        var gravar = () => DespacharAsync(outro, segundo, autor, "br123");

        await gravar.Should().ThrowAsync<RastreioDuplicadoException>();
    }

    [Fact]
    public async Task Ordem_criada_por_outra_operacao_antes_da_gravacao_e_conflito_de_concorrencia()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (pedidoId, autor) = await ProntoParaEnvioAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
        pedido.RegistrarRastreio("Correios", "BR123", autor, Depois);

        // O consumidor de entrega grava a ordem do mesmo pedido sem mudar a versão do pedido.
        await using (var consumidor = BancoDadosTeste.NovoContexto(stringConexao))
        {
            consumidor.OrdensEntrega.Add(new OrdemEntrega(pedidoId, Depois));
            await consumidor.SaveChangesAsync(Token);
        }

        var conflito = (await armazenamento.Invoking(a => a.SalvarAsync(Token)).Should().ThrowAsync<ConflitoConcorrenciaException>()).Which;
        conflito.Entidade.Should().Be(nameof(OrdemEntrega));
        conflito.Identificador.Should().Be(pedidoId.ToString());
    }

    [Fact]
    public async Task Mudanca_de_status_sem_o_historico_carregado_e_recusada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (pedidoId, _) = await ProntoParaEnvioAsync(stringConexao);
        await using var contexto = BancoDadosTeste.NovoContexto(stringConexao);
        var pedido = await contexto.Pedidos.SingleAsync(p => p.Id == pedidoId, Token);
        pedido.MudarStatus(StatusPedido.Despachado, null, null, Depois);

        var gravar = () => new ArmazenamentoPedidos(contexto).SalvarAsync(Token);

        await gravar.Should().ThrowAsync<InvalidOperationException>().WithMessage("*sem o histórico carregado*");
    }

    [Fact]
    public async Task Deadlock_fora_de_transacao_vira_conflito_da_entidade_alterada()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        var (pedidoId, autor) = await ProntoParaEnvioAsync(stringConexao);
        await using var contexto = ContextoComDeadlock(stringConexao);
        var armazenamento = new ArmazenamentoPedidos(contexto);
        var pedido = (await armazenamento.ObterPedidoAsync(pedidoId, paraAlterar: true, Token))!;
        pedido.RegistrarRastreio("Correios", "BR123", autor, Depois);

        var conflito = (await armazenamento.Invoking(a => a.SalvarAsync(Token)).Should().ThrowAsync<ConflitoConcorrenciaException>()).Which;

        conflito.Entidade.Should().Be(nameof(Pedido));
        conflito.Identificador.Should().Be(pedidoId.ToString());
    }

    [Fact]
    public async Task Deadlock_sem_entidade_alterada_vira_conflito_generico()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = ContextoComDeadlock(stringConexao);

        var conflito = (await new ArmazenamentoPedidos(contexto).Invoking(a => a.SalvarAsync(Token))
            .Should().ThrowAsync<ConflitoConcorrenciaException>()).Which;

        conflito.Entidade.Should().Be("Deadlock");
        conflito.Identificador.Should().BeEmpty();
    }

    [Fact]
    public async Task Deadlock_dentro_da_transacao_segue_para_as_novas_tentativas()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = ContextoComDeadlock(stringConexao);
        await using var transacao = await contexto.Database.BeginTransactionAsync(Token);

        var gravar = () => new ArmazenamentoPedidos(contexto).SalvarAsync(Token);

        (await gravar.Should().ThrowAsync<DbUpdateException>()).Which.Should().Match<Exception>(e => ErrosBancoDados.EhDeadlock(e));
    }

    [Fact]
    public async Task Deadlock_ao_gravar_a_chave_de_idempotencia_rele_o_registro()
    {
        Assert.SkipUnless(banco.DockerDisponivel, "Docker indisponível");
        var stringConexao = await banco.NovoBancoMigradoAsync();
        await using var contexto = ContextoComDeadlock(stringConexao);
        contexto.ChavesIdempotencia.Add(new RegistroIdempotencia(Guid.CreateVersion7(), Guid.CreateVersion7(), "impressao", Guid.CreateVersion7(), "{}", Depois));

        var gravar = () => new ArmazenamentoPedidos(contexto).SalvarAsync(Token);

        await gravar.Should().ThrowAsync<ChaveIdempotenciaRegistradaException>();
    }
}
