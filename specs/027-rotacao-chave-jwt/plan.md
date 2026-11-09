# Implementation Plan: Rotação da chave de assinatura do JWT

**Branch**: `feature/req-27-rotacao-chave-jwt` | **Date**: 2026-11-09 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/027-rotacao-chave-jwt/spec.md`

## Summary

A chave única `Jwt:ChaveAssinatura` dá lugar a um conjunto de 1 a 3 chaves HMAC (`Jwt:Chaves`),
cada uma com um identificador, mais a `Jwt:ChaveAtiva`, que é a que emite. O token passa a levar o
`kid` da chave ativa no cabeçalho; a validação aceita qualquer chave configurada e recusa com 401 o
token sem `kid` ou com `kid` fora do conjunto, por um resolvedor próprio com
`TryAllIssuerSigningKeys = false` (research R-02). Um validador de opções barra a configuração
inválida e a antiga na partida, sem mostrar valores (R-04). Dois logs novos, 3026 na partida e 3027
na recusa, apoiam a operação (R-05). Os cursores das listagens ganham a chave opcional
`Listagens:ChaveCursor`; sem ela, a chave deriva da chave ativa (R-06). O procedimento de rotação e o
estudo de RS256/ES256 e cofre de segredos vão para `docs/rotacao-chave-jwt.md` (R-08, R-09).

## Technical Context

**Language/Version**: C# / .NET 10

**Primary Dependencies**: Microsoft.AspNetCore.Authentication.JwtBearer 10.0.14 (já em uso, traz a
Microsoft.IdentityModel). Nenhuma dependência nova.

**Storage**: N/A (as chaves vêm da configuração; nada é persistido)

**Testing**: xUnit v3, FluentAssertions, `WebApplicationFactory` (`FabricaApi`) com `CapturaLogs`;
Testcontainers (SQL Server) só no cenário de emergência com renovação

**Target Platform**: API ASP.NET Core em contêiner Linux (e local em Windows)

**Project Type**: web-service (Minimal API) com Worker separado, que não é afetado

**Performance Goals**: sem regressão: a validação continua com uma só verificação HMAC por token,
porque o resolvedor escolhe a chave pelo `kid`

**Constraints**: nenhum valor de chave em mensagem, log ou exceção; no máximo 3 chaves; contrato v1
inalterado (o `kid` é um campo novo do cabeçalho)

**Scale/Scope**: ~8 arquivos de produção na API, a configuração de desenvolvimento, a infraestrutura
de testes e 5 documentos

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Princípio / regra | Situação |
|-------------------|----------|
| I. Specification-First | ✅ Spec e clarify aprovados (REQ-27, #60) |
| II. Contratos imutáveis | ✅ Resposta de login, claims e OpenAPI inalterados; o `kid` é aditivo no cabeçalho ([contrato](./contracts/token-acesso.md)). O 401 único na implantação é decisão do usuário e fica documentado |
| III. Segurança por padrão | ✅ Reforça: chave vazada pode ser retirada; token sem `kid` recusado; valores nunca exibidos; `kid` sanitizado no log |
| IV. Desacoplamento assíncrono | ✅ Não se aplica (sem efeitos entre domínios) |
| V. Qualidade verificável | ✅ Testes da validação, da emissão, da partida, dos logs, da renovação e do cursor (R-07); os 401 dos endpoints protegidos continuam cobertos |
| Endpoint só roteia | ✅ Nenhum endpoint muda |
| Dependência NuGet nova | ✅ Nenhuma. As do estudo (KeyPerFile, Key Vault) ficam para um REQ futuro |
| Nomes em português | ✅ `ConjuntoChavesJwt`, `ValidadorOpcoesJwt`, `ChaveCursorListagens`, `EventosChavesJwt` |
| Commits `tipo(escopo): ... [REQ-27]` | ✅ Um por etapa e por fase |

**Pós-design**: sem violações; a tabela de Complexity Tracking não se aplica.

## Project Structure

### Documentation (this feature)

```text
specs/027-rotacao-chave-jwt/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── configuracao.md
│   └── token-acesso.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/PedidoFulfillment.API/
├── Program.cs                                 # registro de ChaveCursorListagens e OpcoesListagens
├── appsettings.Development.json               # Jwt:Chaves[0] = desenvolvimento-local, Jwt:ChaveAtiva
└── Seguranca/
    ├── ConfiguracaoJwt.cs                     # OpcoesJwt (Chaves, ChaveAtiva), validação com resolvedor por kid, OnAuthenticationFailed → 3027
    ├── ValidadorOpcoesJwt.cs                  # novo: regras do FR-005/FR-007 (IValidateOptions)
    ├── ConjuntoChavesJwt.cs                   # novo: chaves montadas uma vez (ativa, todas, contém)
    ├── EventosChavesJwt.cs                    # novo: logs 3026 e 3027
    ├── RegistroChavesJwtNaPartida.cs          # novo: IHostedService que emite o 3026
    ├── EmissorTokenJwt.cs                     # assina com a ativa, kid no cabeçalho
    ├── OpcoesListagens.cs                     # novo: Listagens:ChaveCursor e validação
    └── ChaveCursorListagens.cs                # substitui ChaveCursorDerivadaDoJwt.cs
src/PedidoFulfillment.Application/Comum/IChaveCursor.cs   # só o comentário

tests/PedidoFulfillment.Testes.Integracao/
├── Infraestrutura/FabricaApi.cs               # Jwt:Chaves[0] = teste-a, Jwt:ChaveAtiva
├── Infraestrutura/GeradorTokenTeste.cs        # parâmetros identificadorChave e chave
├── Seguranca/ValidadorOpcoesJwtTestes.cs      # novo: cada regra, sem subir a API
├── Seguranca/RotacaoChaveJwtTestes.cs         # novo: transição, retirada, sem kid, kid desconhecido, logs
├── Seguranca/ChavesJwtPartidaTestes.cs        # novo: partida falha (antiga, 4 chaves, cursor curta) sem valores
├── Seguranca/ChaveCursorListagensTestes.cs    # novo: cursor válido após a troca da ativa com chave própria
├── Autenticacao/EmissorTokenTestes.cs         # kid da ativa
└── Autenticacao/RotacaoChaveSessoesTestes.cs  # novo, com banco: retirada de emergência + renovação

docs/
├── rotacao-chave-jwt.md                       # novo: procedimento, emergência, migração, geração de chave, estudo
├── fluxos/01-autenticacao.md                  # chaves, kid, recusa, partida, logs 3026/3027, limitações
├── fluxos/03-consulta-e-mudanca-de-status.md  # limitação do cursor resolvida pela #60
├── fluxos/15-catalogo-de-produtos.md          # chave do cursor
├── proximos-passos.md                         # #60 sai da fila e da pendência do cursor
└── estado-atual.md                            # linha do REQ-27
README.md                                      # só o link na linha docs/ da Estrutura (decisão do usuário)
```

**Structure Decision**: tudo na API (a autenticação já vive em `Seguranca/`); a Application muda só
o comentário da `IChaveCursor`. O Worker não emite nem valida JWT.

## Fases de implementação (para o /speckit-tasks)

1. **Setup**: migrar `appsettings.Development.json`, `FabricaApi` e `GeradorTokenTeste` para o
   formato novo, com o mesmo valor de hoje; a suíte atual deve continuar verde depois da fase 2.
2. **Fundação**: `OpcoesJwt` nova, `ValidadorOpcoesJwt`, `ConjuntoChavesJwt`.
3. **US1 e US2 (P1)**: emissão com `kid`, validação com resolvedor, `OnAuthenticationFailed` com o
   3027, `RegistroChavesJwtNaPartida` com o 3026; testes de transição, retirada, sem `kid`, `kid`
   desconhecido e emergência com renovação.
4. **US3 (P2)**: testes da partida e das mensagens sem valores; `OpcoesListagens` e
   `ChaveCursorListagens` com os testes do cursor.
5. **US4 (P2) e Polish**: `docs/rotacao-chave-jwt.md` (procedimento, R-08, R-09, R-10), fluxos 1, 3
   e 15, `proximos-passos.md`, `estado-atual.md`, a linha `docs/` do README e o roteiro do quickstart.

## Riscos

- **401 na implantação**: todo token em uso é recusado uma vez (decisão do usuário). Documentado no
  fluxo 1 e no procedimento; o cliente renova sem senha.
- **Comportamento da biblioteca com `kid`**: coberto pelo resolvedor próprio e pelos testes (R-02).
- **Volume de 3027 sob ataque**: aceito e documentado nas limitações (R-05).
- **Variáveis de ambiente de lista**: sobrescrever a posição 0 pelo ambiente deixa as demais do
  arquivo; o procedimento explica como retirar uma chave nos dois lugares.

## Complexity Tracking

Sem violações da constituição.
