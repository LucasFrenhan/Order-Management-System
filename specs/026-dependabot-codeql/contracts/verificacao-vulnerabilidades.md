# Contrato: verificação de vulnerabilidades no CI

**Requisitos**: FR-011 a FR-014 | **Research**: R-01 a R-03

## Onde roda

Job `Build e testes` (`.github/workflows/build-testes.yml`), passo **"Vulnerabilidades"**, logo
depois de "Restaurar" e antes de "Compilar", em todo PR para a `main` e no push da `main`.

## Roteiro

`.github/scripts/verificar-vulnerabilidades.ps1 [-Entrada <saida.json>] [-Excecoes <arquivo>] [-Hoje AAAA-MM-DD] [-Solucao <sln>]` (PowerShell 7)

| Opção | Padrão | Uso |
|---|---|---|
| `-Entrada` | roda `dotnet list PedidoFulfillment.sln package --vulnerable --include-transitive --format json` | ler uma saída gravada (exemplos de teste) |
| `-Excecoes` | `.github/vulnerabilidades-aceitas.json` | outro arquivo de exceções |
| `-Hoje` | data UTC atual | fixar a data nos exemplos |

O passo roda antes `exemplos-vulnerabilidades/conferir.ps1`, que confere o roteiro com as saídas
gravadas. Escreve o resumo em `$GITHUB_STEP_SUMMARY` quando a variável existe; senão, na saída padrão.

## Códigos de saída

| Código | Quando |
|---|---|
| 0 | Nenhuma vulnerabilidade, ou só `Low`/`Moderate`, ou `High`/`Critical` todas com exceção vigente |
| 1 | Alguma `High`/`Critical` sem exceção vigente (inclui exceção vencida) |
| 2 | Não conferiu: o comando falhou, a saída tem `problems` de nível `error`, o JSON é inválido, ou o arquivo de exceções é inválido |

O passo falha com 1 ou 2. Cada caso escreve uma anotação `::error::` com o motivo.

## Resumo do job

```markdown
### Vulnerabilidades das dependências

| Situação | Pacote | Versão | Severidade | Aviso | Tipo | Projetos |
|---|---|---|---|---|---|---|
| ❌ reprova | Exemplo.Pacote | 1.2.3 | High | [GHSA-…](url) | transitiva | API, Worker |
| ⚠️ aviso | Outro.Pacote | 4.5.6 | Moderate | [GHSA-…](url) | direta | Testes.Unitarios |
| ✅ exceção até 2026-12-08 | Pacote.Aceito | 7.8.9 | Critical | [GHSA-…](url) | direta | API |
```

- Sem nenhuma vulnerabilidade: "Nenhuma vulnerabilidade conhecida nas dependências NuGet."
- Exceções sem uso aparecem numa lista "Exceções que podem sair".
- Ordem: reprova, exceção, aviso; dentro de cada grupo, severidade decrescente e pacote.

## Mudança no build

`Directory.Build.props`: `<WarningsNotAsErrors>NU1901;NU1902;NU1903;NU1904</WarningsNotAsErrors>`
(R-01). O NuGet Audit continua avisando no restore; o NU1900 continua erro.
