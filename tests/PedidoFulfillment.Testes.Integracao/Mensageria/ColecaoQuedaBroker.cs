using PedidoFulfillment.Testes.Integracao.Infraestrutura;

namespace PedidoFulfillment.Testes.Integracao.Mensageria;

/// <summary>
/// Testes que param, reiniciam ou zeram o broker (REQ-17, R-09; analyze C1): uma instância
/// própria do <see cref="MensageriaTeste"/>, para não derrubar o broker das outras coleções e
/// rodar em paralelo com elas.
/// </summary>
[CollectionDefinition(nameof(ColecaoQuedaBroker))]
public sealed class ColecaoQuedaBroker : ICollectionFixture<MensageriaTeste>;

/// <summary>
/// A segunda metade dos testes de queda do broker, com outra instância do
/// <see cref="MensageriaTeste"/>: as duas metades rodam em paralelo (estudo
/// docs/otimizacao-ci.md, proposta 4).
/// </summary>
[CollectionDefinition(nameof(ColecaoQuedaBrokerReinicio))]
public sealed class ColecaoQuedaBrokerReinicio : ICollectionFixture<MensageriaTeste>;
