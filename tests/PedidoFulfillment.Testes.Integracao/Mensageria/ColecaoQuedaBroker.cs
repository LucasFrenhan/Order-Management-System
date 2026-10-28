using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Testes que param, reiniciam ou zeram o broker (REQ-17, R-09; analyze C1): uma instância
/// própria do <see cref="MensageriaTeste"/>, para não derrubar o broker das outras coleções e
/// rodar em paralelo com elas.
/// </summary>
[CollectionDefinition(nameof(ColecaoQuedaBroker))]
public sealed class ColecaoQuedaBroker : ICollectionFixture<MensageriaTeste>;
