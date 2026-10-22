# Quickstart: validação do REQ-04

**Spec**: [spec.md](./spec.md) | **Modelo**: [data-model.md](./data-model.md) |
**Catálogo**: [contracts/catalogo-inicial.md](./contracts/catalogo-inicial.md) |
**Persistência**: [contracts/persistencia.md](./contracts/persistencia.md)

## Pré-requisitos

- SDK do .NET 10 e Docker com Compose v2 (REQ-02).
- Ferramentas locais: `dotnet tool restore` (instala o `dotnet-ef`).

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**:
- A suíte inteira fica verde.
- Com o Docker no ar, rodam os testes com banco real: persistência, restrições,
  concorrência (50 repetições), migration, catálogo e desempenho.
- Sem o Docker, esses testes aparecem como ignorados.
- Sempre rodam os testes do REQ-03, o de conformidade do contrato v1 e o de modelo
  sincronizado (FR-016, FR-018, FR-019).

## 2. Modelo sem migration pendente

```powershell
dotnet ef migrations has-pending-model-changes --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API
```

**Esperado**: "No changes have been made to the model since the last migration." (FR-016)

## 3. Atualização de um banco do REQ-03 (User Story 4)

```powershell
docker compose up -d --wait
dotnet ef database update InicialIdentidade --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API
```

1. Suba a API, registre um usuário e faça login, como no
   [quickstart do REQ-03](../003-autenticacao-identity/quickstart.md).
2. Reinicie a API em Development:

   ```powershell
   $env:ASPNETCORE_ENVIRONMENT = "Development"
   dotnet run --project src/PedidoFulfillment.API --launch-profile http
   ```

**Esperado**:
- A API aplica a migration `EntidadesNegocio`.
- As tabelas `Produtos`, `Pedidos`, `ItensPedido`, `HistoricosStatusPedido`, `Pagamentos`
  e `OrdensEntrega` passam a existir.
- O mesmo login continua funcionando (FR-014, SC-003).

## 4. Catálogo inicial (FR-020)

```powershell
docker compose exec sqlserver sh -c '/opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$MSSQL_SA_PASSWORD" -d PedidoFulfillment -Q "SELECT Sku, Preco, QuantidadeEstoque, Ativo FROM Produtos ORDER BY Sku"'
```

**Esperado**:
- Os 10 produtos do [catálogo](./contracts/catalogo-inicial.md) aparecem. `CAT-009` tem
  estoque 0, e `CAT-010` está inativo.
- Se você alterar um estoque e reiniciar a API, o valor alterado é mantido e nada é
  duplicado.

## 5. Fora de Development

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Production"
```

Com um banco onde só a `InicialIdentidade` foi aplicada, a API recusa a inicialização com
a mensagem de migrations pendentes (REQ-03 FR-017). Depois do comando abaixo, ela sobe sem
criar nenhum produto.

```powershell
dotnet ef database update --project src/PedidoFulfillment.Infrastructure --startup-project src/PedidoFulfillment.API
```

## 6. Contrato v1

As rotas de pedido continuam respondendo 501 depois da autorização (FR-018). O teste
`ConformidadeOpenApiTestes` passa sem alteração no contrato.
