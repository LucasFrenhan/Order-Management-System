#!/usr/bin/env pwsh
# Vulnerabilidades das dependências NuGet para o job "Build e testes" (REQ-26, FR-011 a FR-014;
# specs/026-dependabot-codeql/contracts/verificacao-vulnerabilidades.md).
#
# Lê a saída de "dotnet list package --vulnerable --include-transitive --format json" (ou um
# arquivo gravado, com -Entrada), aplica as exceções de .github/vulnerabilidades-aceitas.json e
# escreve o resumo em $GITHUB_STEP_SUMMARY (ou na saída padrão).
#
# Saída: 0 = nada que reprove; 1 = vulnerabilidade alta ou crítica sem exceção vigente;
#        2 = não conferiu (comando falhou, problema na consulta, JSON ou exceções inválidos).
#
# PowerShell em vez de bash + jq: o pwsh vem no runner e lê JSON sem ferramenta extra, e o
# mesmo roteiro roda na máquina do desenvolvedor (research R-02).
[CmdletBinding()]
param(
    [string] $Entrada,
    [string] $Excecoes = (Join-Path $PSScriptRoot '..' 'vulnerabilidades-aceitas.json'),
    [string] $Hoje = ([DateTime]::UtcNow.ToString('yyyy-MM-dd')),
    [string] $Solucao = (Join-Path $PSScriptRoot '..' '..' 'PedidoFulfillment.sln')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$PrazoMaximoDias = 90
$Cultura = [Globalization.CultureInfo]::InvariantCulture
$PesoSeveridade = @{ Critical = 4; High = 3; Moderate = 2; Low = 1 }

$linhas = [Collections.Generic.List[string]]::new()
function Escrever([string] $texto = '') { $linhas.Add($texto) }

function Publicar {
    $texto = ($linhas -join "`n") + "`n"
    if ($env:GITHUB_STEP_SUMMARY) {
        Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $texto -Encoding utf8
    }
    else {
        Write-Host $texto
    }
}

# Sai com 2 ("não conferiu"), com a anotação de erro e o motivo no resumo.
function Abortar([string] $motivo) {
    Write-Host "::error title=Vulnerabilidades::$motivo"
    Escrever '### Vulnerabilidades das dependências'
    Escrever
    Escrever "❌ **A conferência não foi feita**: $motivo"
    Publicar
    exit 2
}

function Ler-Data([object] $valor, [string] $campo, [int] $indice) {
    $data = [DateTime]::MinValue
    if ($valor -isnot [string] -or -not [DateTime]::TryParseExact($valor, 'yyyy-MM-dd', $Cultura, 'None', [ref] $data)) {
        Abortar "exceção $($indice + 1) de '$Excecoes': '$campo' precisa ser uma data AAAA-MM-DD."
    }
    return $data
}

function Campo([object] $objeto, [string] $nome) {
    $propriedade = $objeto.PSObject.Properties[$nome]
    if ($null -eq $propriedade) { return $null }
    # A vírgula impede que uma lista de um item só seja desembrulhada no retorno.
    return ,$propriedade.Value
}

# Os itens de um campo de lista (vazio se o campo não existir).
function Lista([object] $objeto, [string] $nome) {
    $valor = Campo $objeto $nome
    if ($null -eq $valor) { return ,@() }
    return ,@($valor | Where-Object { $null -ne $_ })
}

$dataHoje = [DateTime]::MinValue
if (-not [DateTime]::TryParseExact($Hoje, 'yyyy-MM-dd', $Cultura, 'None', [ref] $dataHoje)) {
    Abortar "-Hoje precisa ser uma data AAAA-MM-DD (recebido '$Hoje')."
}

# --- Exceções (data-model, "Exceção de vulnerabilidade") ---
$listaExcecoes = @()
if (Test-Path -LiteralPath $Excecoes) {
    try {
        $conteudoExcecoes = Get-Content -LiteralPath $Excecoes -Raw -Encoding utf8 | ConvertFrom-Json -NoEnumerate
    }
    catch {
        Abortar "o arquivo de exceções '$Excecoes' não é um JSON válido."
    }
    $itens = Campo $conteudoExcecoes 'excecoes'
    if ($null -eq $itens -or $itens -isnot [array]) {
        Abortar "o arquivo de exceções '$Excecoes' precisa ter a lista 'excecoes'."
    }
    $chaves = @{}
    for ($i = 0; $i -lt $itens.Count; $i++) {
        $item = $itens[$i]
        foreach ($obrigatorio in 'pacote', 'aviso', 'motivo') {
            $valor = Campo $item $obrigatorio
            if ($valor -isnot [string] -or [string]::IsNullOrWhiteSpace($valor)) {
                Abortar "exceção $($i + 1) de '$Excecoes': '$obrigatorio' é obrigatório e não pode ficar vazio."
            }
        }
        $registradaEm = Ler-Data (Campo $item 'registradaEm') 'registradaEm' $i
        $revisarAte = Ler-Data (Campo $item 'revisarAte') 'revisarAte' $i
        if ($revisarAte -lt $registradaEm -or ($revisarAte - $registradaEm).TotalDays -gt $PrazoMaximoDias) {
            Abortar "exceção $($i + 1) de '$Excecoes': 'revisarAte' precisa ficar entre 'registradaEm' e $PrazoMaximoDias dias depois."
        }
        $chave = "$($item.pacote.ToLowerInvariant())|$($item.aviso)"
        if ($chaves.ContainsKey($chave)) {
            Abortar "exceção $($i + 1) de '$Excecoes': '$($item.pacote)' com '$($item.aviso)' está repetida."
        }
        $chaves[$chave] = $true
        $listaExcecoes += [pscustomobject]@{
            Chave      = $chave
            Pacote     = $item.pacote
            Aviso      = $item.aviso
            Motivo     = $item.motivo
            RevisarAte = $revisarAte
            Vigente    = $revisarAte -ge $dataHoje
            Usada      = $false
        }
    }
}

# --- Saída do dotnet list package ---
if ($Entrada) {
    if (-not (Test-Path -LiteralPath $Entrada)) { Abortar "o arquivo de entrada '$Entrada' não existe." }
    $json = Get-Content -LiteralPath $Entrada -Raw -Encoding utf8
}
else {
    $json = (& dotnet list $Solucao package --vulnerable --include-transitive --format json 2>&1) -join "`n"
    if ($LASTEXITCODE -ne 0) {
        Write-Host $json
        Abortar "'dotnet list package --vulnerable' terminou com código $LASTEXITCODE."
    }
}

try {
    $saida = $json | ConvertFrom-Json -NoEnumerate
}
catch {
    Abortar 'a saída de "dotnet list package --vulnerable" não é um JSON válido.'
}
if ((Campo $saida 'version') -ne 1) {
    Abortar "formato da saída de 'dotnet list package' não reconhecido (version '$(Campo $saida 'version')', esperado 1)."
}

$problemas = Lista $saida 'problems'
$erros = @($problemas | Where-Object { (Campo $_ 'level') -eq 'error' })
if ($erros.Count -gt 0) {
    Abortar ("a consulta de vulnerabilidades falhou: " + (($erros | ForEach-Object { $_.text }) -join ' | '))
}

# --- Vulnerabilidades, uma por pacote + aviso ---
$encontradas = [ordered]@{}
foreach ($projeto in (Lista $saida 'projects')) {
    $nomeProjeto = [IO.Path]::GetFileNameWithoutExtension(([string] $projeto.path).Replace('\', '/')) -replace '^PedidoFulfillment\.', ''
    foreach ($framework in (Lista $projeto 'frameworks')) {
        foreach ($tipo in 'topLevelPackages', 'transitivePackages') {
            foreach ($pacote in (Lista $framework $tipo)) {
                foreach ($vulnerabilidade in (Lista $pacote 'vulnerabilities')) {
                    $chave = "$(([string] $pacote.id).ToLowerInvariant())|$($vulnerabilidade.advisoryurl)"
                    if (-not $encontradas.Contains($chave)) {
                        $encontradas[$chave] = [pscustomobject]@{
                            Chave      = $chave
                            Pacote     = [string] $pacote.id
                            Versoes    = [Collections.Generic.SortedSet[string]]::new()
                            Severidade = [string] $vulnerabilidade.severity
                            Aviso      = [string] $vulnerabilidade.advisoryurl
                            Tipos      = [Collections.Generic.SortedSet[string]]::new()
                            Projetos   = [Collections.Generic.SortedSet[string]]::new()
                        }
                    }
                    $registro = $encontradas[$chave]
                    [void] $registro.Versoes.Add([string] $pacote.resolvedVersion)
                    [void] $registro.Tipos.Add($(if ($tipo -eq 'topLevelPackages') { 'direta' } else { 'transitiva' }))
                    [void] $registro.Projetos.Add($nomeProjeto)
                }
            }
        }
    }
}

$resultado = foreach ($registro in $encontradas.Values) {
    $pesoItem = $PesoSeveridade[$registro.Severidade]
    if ($null -eq $pesoItem) { $pesoItem = 3 }   # severidade desconhecida conta como alta
    $excecao = $listaExcecoes | Where-Object { $_.Chave -eq $registro.Chave } | Select-Object -First 1
    if ($excecao) { $excecao.Usada = $true }
    if ($pesoItem -ge 3) {
        if ($excecao -and $excecao.Vigente) {
            $situacao = "✅ exceção até $($excecao.RevisarAte.ToString('yyyy-MM-dd'))"; $ordem = 1
        }
        elseif ($excecao) {
            $situacao = "❌ reprova (exceção vencida em $($excecao.RevisarAte.ToString('yyyy-MM-dd')))"; $ordem = 0
        }
        else {
            $situacao = '❌ reprova'; $ordem = 0
        }
    }
    else {
        $situacao = '⚠️ aviso'; $ordem = 2
    }
    $registro | Add-Member -NotePropertyName Situacao -NotePropertyValue $situacao
    $registro | Add-Member -NotePropertyName Ordem -NotePropertyValue $ordem
    $registro | Add-Member -NotePropertyName Peso -NotePropertyValue $pesoItem
    $registro
}
$resultado = @($resultado | Sort-Object Ordem, @{ Expression = 'Peso'; Descending = $true }, Pacote)
$reprovadas = @($resultado | Where-Object { $_.Ordem -eq 0 })

# --- Resumo ---
Escrever '### Vulnerabilidades das dependências'
Escrever
if ($resultado.Count -eq 0) {
    Escrever 'Nenhuma vulnerabilidade conhecida nas dependências NuGet.'
}
else {
    Escrever '| Situação | Pacote | Versão | Severidade | Aviso | Tipo | Projetos |'
    Escrever '|---|---|---|---|---|---|---|'
    foreach ($item in $resultado) {
        $id = ($item.Aviso -split '/')[-1]
        Escrever ("| {0} | {1} | {2} | {3} | [{4}]({5}) | {6} | {7} |" -f $item.Situacao, $item.Pacote,
            ($item.Versoes -join ', '), $item.Severidade, $id, $item.Aviso, ($item.Tipos -join ', '), ($item.Projetos -join ', '))
    }
    Escrever
    Escrever 'Alta e crítica reprovam, salvo exceção vigente em `.github/vulnerabilidades-aceitas.json`; baixa e moderada são aviso. Procedimento em `docs/seguranca-cadeia-build.md`.'
}

foreach ($problema in $problemas | Where-Object { (Campo $_ 'level') -ne 'error' }) {
    Escrever
    Escrever "⚠️ $($problema.text)"
}

$semUso = @($listaExcecoes | Where-Object { -not $_.Usada })
if ($semUso.Count -gt 0) {
    Escrever
    Escrever '**Exceções que podem sair** (nenhuma vulnerabilidade atual casa com elas):'
    Escrever
    foreach ($excecao in $semUso) { Escrever "- $($excecao.Pacote) — $($excecao.Aviso)" }
}

foreach ($item in $reprovadas) {
    Write-Host "::error title=Vulnerabilidade $($item.Severidade)::$($item.Pacote) $($item.Versoes -join ', ') ($($item.Tipos -join ', ')) tem $($item.Aviso). Atualize o pacote ou registre uma exceção em .github/vulnerabilidades-aceitas.json."
}

Publicar
if ($reprovadas.Count -gt 0) { exit 1 }
exit 0
