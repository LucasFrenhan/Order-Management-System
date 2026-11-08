#!/usr/bin/env pwsh
# Confere verificar-vulnerabilidades.ps1 contra as saídas gravadas desta pasta (REQ-26,
# specs/026-dependabot-codeql/quickstart.md §2). Sai com 1 se algum caso der outro código.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$roteiro = Join-Path $PSScriptRoot '..' 'verificar-vulnerabilidades.ps1'
$casos = @(
    @{ Entrada = 'sem-vulnerabilidades.json'; Excecoes = $null; Esperado = 0 }
    @{ Entrada = 'so-moderada.json'; Excecoes = $null; Esperado = 0 }
    @{ Entrada = 'alta-transitiva.json'; Excecoes = $null; Esperado = 1 }
    @{ Entrada = 'alta-transitiva.json'; Excecoes = 'excecoes-vigente.json'; Esperado = 0 }
    @{ Entrada = 'alta-transitiva.json'; Excecoes = 'excecoes-vencida.json'; Esperado = 1 }
    @{ Entrada = 'alta-transitiva.json'; Excecoes = 'excecoes-invalida.json'; Esperado = 2 }
    @{ Entrada = 'com-problema.json'; Excecoes = $null; Esperado = 2 }
)

$falhas = 0
foreach ($caso in $casos) {
    # Sem exceções: um caminho que não existe equivale à lista vazia.
    $excecoes = if ($caso.Excecoes) { Join-Path $PSScriptRoot $caso.Excecoes } else { Join-Path $PSScriptRoot 'sem-excecoes.json' }
    # Sem GITHUB_STEP_SUMMARY: os casos não escrevem no resumo do job.
    $resumo = $env:GITHUB_STEP_SUMMARY
    $env:GITHUB_STEP_SUMMARY = $null
    $null = & pwsh -NoProfile -File $roteiro -Entrada (Join-Path $PSScriptRoot $caso.Entrada) -Excecoes $excecoes -Hoje 2026-11-08 6>&1
    $codigo = $LASTEXITCODE
    $env:GITHUB_STEP_SUMMARY = $resumo
    $descricao = "$($caso.Entrada)$(if ($caso.Excecoes) { " + $($caso.Excecoes)" })"
    if ($codigo -eq $caso.Esperado) {
        Write-Host "ok     $descricao -> $codigo"
    }
    else {
        Write-Host "::error title=Exemplos de vulnerabilidades::$descricao deu $codigo, esperado $($caso.Esperado)."
        $falhas++
    }
}

if ($falhas -gt 0) { exit 1 }
Write-Host "$($casos.Count) casos conferidos."
