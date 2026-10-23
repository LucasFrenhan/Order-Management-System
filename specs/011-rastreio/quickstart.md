# Quickstart: validação do REQ-11

**Spec**: [spec.md](./spec.md) | **Contrato**: [contracts/rastreio-http.md](./contracts/rastreio-http.md)

## 1. Testes

```powershell
dotnet test PedidoFulfillment.sln
```

**Esperado**: tudo verde, com os testes de integração contra SQL Server e RabbitMQ reais:
- consulta com `historico` e `entrega` para o Cliente dono, o Admin e o OperadorLogistica;
  Cliente em pedido de outro: 404;
- despacho, correção, repetição sem mudança, criação da ordem no despacho, recusa por
  status, perfil, dados inválidos e rastreio duplicado (inclusive com outra caixa);
- corrida entre despacho e cancelamento;
- conformidade com o OpenAPI e percentil 95 da consulta e do rastreio abaixo de 500 ms.

## 2. Contrato

```powershell
dotnet test PedidoFulfillment.sln --filter "FullyQualifiedName~ConformidadeOpenApi"
```

**Esperado**: verde, com a operação `PATCH /api/v1/pedidos/{id}/rastreio` na lista de
conformidade. No PR, o workflow "Compatibilidade do contrato" passa (sem mudança
incompatível).

## 3. Local: fluxo completo até o despacho

Com o ambiente local, a API e o Worker no ar e a taxa de aprovação em 100%, como no
[quickstart do REQ-10](../010-fulfillment/quickstart.md) §2, crie um pedido como Cliente
e espere `ProntoParaEnvio`.

Consulte o pedido como Cliente (`GET /api/v1/pedidos/{id}`).

**Esperado**: `historico` com `Pendente`, `Processando` e `ProntoParaEnvio`, sem autor;
`entrega` com `criadoEm` e sem transportadora, código e data de despacho.

Despache como `operador@oms.local`:

```http
PATCH /api/v1/pedidos/{id}/rastreio
{ "transportadora": "Correios", "codigoRastreio": "BR123456789BR" }
```

**Esperado**: 200, `status: Despachado`, a entrada `ProntoParaEnvio → Despachado` com
`Pedido despachado.` e a entrega com os dados e `despachadoEm`.

## 4. Correção, repetição e duplicado

- Repita o mesmo corpo: 200, nada muda (`atualizadoEm` igual).
- Envie `"codigoRastreio": "BR999999999BR"`: 200, código novo, `despachadoEm` e histórico
  iguais.
- Despache outro pedido com `"transportadora": "correios"` e
  `"codigoRastreio": "br999999999br"`: 409 `PEDIDO_RASTREIO_DUPLICADO`.

## 5. Recusas

- Como Cliente: 403.
- Num pedido `Pendente` ou `Cancelado`: 409 `PEDIDO_TRANSICAO_INVALIDA`.
- Com `transportadora` vazia ou só espaços: 400 indicando o campo.
