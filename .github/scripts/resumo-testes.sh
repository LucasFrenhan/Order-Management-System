#!/usr/bin/env bash
# Resumo dos testes e da cobertura para o job "Build e testes" (REQ-07, research R-05).
# Lê os .trx e .cobertura.xml de um diretório e escreve em $GITHUB_STEP_SUMMARY (ou na saída
# padrão). Sai com erro se não houver resultados ou se algum teste foi ignorado: no CI os
# testes de integração precisam rodar de verdade (FR-007).
set -euo pipefail

diretorio="${1:-resultados}"
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  exec 3>>"$GITHUB_STEP_SUMMARY"
else
  exec 3>&1
fi

# Valor de um atributo na primeira ocorrência de uma tag: atributo <arquivo> <tag> <nome>.
# O awk lê até o fim, em vez de "| head -1": cortar o pipe dava "grep: write error".
atributo() {
  grep -o "<$2 [^>]*" "$1" | awk -v nome="$3" '
    NR == 1 && match($0, " " nome "=\"[^\"]*\"") { valor = substr($0, RSTART + length(nome) + 3, RLENGTH - length(nome) - 4) }
    END { print valor }'
}

mapfile -t trxs < <(find "$diretorio" -name '*.trx' | sort)
if [ "${#trxs[@]}" -eq 0 ]; then
  mensagem="Nenhum resultado de teste (.trx) encontrado em '$diretorio'."
  echo "$mensagem" >&3
  echo "$mensagem" >&2
  exit 1
fi

ignorados_total=0
falhas=""
echo "## Testes" >&3
echo >&3
echo "| Projeto | Total | Aprovados | Falhos | Ignorados |" >&3
echo "|---------|------:|----------:|-------:|----------:|" >&3

for trx in "${trxs[@]}"; do
  # O nome do arquivo não traz o projeto (LogFilePrefix); ele vem da .dll de teste.
  projeto=$(basename "$(atributo "$trx" UnitTest storage | tr '\\' '/')" .dll)
  total=$(atributo "$trx" Counters total)
  aprovados=$(atributo "$trx" Counters passed)
  falhos=$(atributo "$trx" Counters failed)
  ignorados=$(atributo "$trx" Counters notExecuted)
  echo "| ${projeto:-?} | ${total:-0} | ${aprovados:-0} | ${falhos:-0} | ${ignorados:-0} |" >&3
  ignorados_total=$((ignorados_total + ${ignorados:-0}))
  falhas+=$(grep -o '<UnitTestResult [^>]*outcome="Failed"[^>]*' "$trx" | grep -o ' testName="[^"]*"' | cut -d'"' -f2 \
    | sed 's/&quot;/"/g; s/&apos;/'"'"'/g; s/&lt;/</g; s/&gt;/>/g; s/&amp;/\&/g; s/^/- /' || true)
  falhas+=$'\n'
done

falhas=$(printf '%s' "$falhas" | sed '/^$/d')
if [ -n "$falhas" ]; then
  printf '\n### Testes com falha\n\n%s\n' "$falhas" >&3
fi

# Cada projeto de teste mede os mesmos fontes, e o coletor deixa cópias dos relatórios: a
# cobertura é a união de (arquivo, linha) de todos eles, e não a soma dos totais.
echo >&3
echo "## Cobertura" >&3
echo >&3
find "$diretorio" -name '*.cobertura.xml' -print0 | xargs -0 -r cat | awk '
  /<class / { match($0, /filename="[^"]*"/); arquivo = substr($0, RSTART + 10, RLENGTH - 11) }
  /<line / {
    match($0, /number="[0-9]+"/); numero = substr($0, RSTART + 8, RLENGTH - 9)
    match($0, /hits="[0-9]+"/); acertos = substr($0, RSTART + 6, RLENGTH - 7) + 0
    chave = arquivo ":" numero
    validas[chave] = 1
    if (acertos > 0) cobertas[chave] = 1
  }
  END {
    v = length(validas); c = length(cobertas)
    if (v == 0) { print "Cobertura não encontrada."; exit }
    p = sprintf("%.1f", 100 * c / v); sub(/\./, ",", p)
    printf "Cobertura de linhas: %s%% (%d de %d linhas), sem migrations nem código gerado. Informativa.\n", p, c, v
  }' >&3

if [ "$ignorados_total" -gt 0 ]; then
  # Sem tee para /dev/fd/3: reabrir o arquivo do resumo o truncaria.
  mensagem="Há testes ignorados no CI (OMS_EXIGIR_DOCKER)."
  printf '\n%s\n' "$mensagem" >&3
  echo "$mensagem" >&2
  exit 1
fi
