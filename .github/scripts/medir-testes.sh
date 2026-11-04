#!/usr/bin/env bash
# Tempo dos testes a partir dos .trx (issue #70, docs/otimizacao-ci.md): para cada projeto, a
# parede (do primeiro teste ao último), a soma das durações, a concorrência média (soma ÷
# parede), a maior lacuna sem nenhum teste rodando (por exemplo, à espera de um contêiner) e
# as classes que mais pesam. Não é usado pelo CI; serve para medir antes e depois de mexer no
# paralelismo ou nos fixtures.
#
# Uso: bash .github/scripts/medir-testes.sh <diretório com .trx> [classes no topo, padrão 15]
set -euo pipefail
export LC_ALL=C

diretorio="${1:-resultados}"
topo="${2:-15}"

mapfile -t trxs < <(find "$diretorio" -name '*.trx' | sort)
if [ "${#trxs[@]}" -eq 0 ]; then
  echo "Nenhum resultado de teste (.trx) encontrado em '$diretorio'." >&2
  exit 1
fi

# Segundos desde 1970 de um horário ISO 8601 do .trx (o fuso é o mesmo em todo o arquivo e é
# ignorado). Dias pelo algoritmo days_from_civil de Howard Hinnant.
read -r -d '' FUNCOES_AWK <<'AWK' || true
function epoca(s,    y, m, d, era, yoe, doy, doe, seg) {
  y = substr(s, 1, 4) + 0; m = substr(s, 6, 2) + 0; d = substr(s, 9, 2) + 0
  match(substr(s, 18), /^[0-9.]+/); seg = substr(s, 18, RLENGTH) + 0
  y -= (m <= 2)
  era = int(y / 400); yoe = y - era * 400
  doy = int((153 * (m + (m > 2 ? -3 : 9)) + 2) / 5) + d - 1
  doe = yoe * 365 + int(yoe / 4) - int(yoe / 100) + doy
  return (era * 146097 + doe - 719468) * 86400 + substr(s, 12, 2) * 3600 + substr(s, 15, 2) * 60 + seg
}
function atributo(linha, nome) {
  if (match(linha, " " nome "=\"[^\"]*\"")) return substr(linha, RSTART + length(nome) + 3, RLENGTH - length(nome) - 4)
  return ""
}
AWK

for trx in "${trxs[@]}"; do
  projeto=$(grep -o '<UnitTest [^>]*storage="[^"]*"' "$trx" | awk '{ match($0, /storage="[^"]*"/); s = substr($0, RSTART + 9, RLENGTH - 10) } END { print s }' \
    | tr '\\' '/' | xargs -0 basename | sed 's/\.dll$//')
  execucao=$(grep -o '<Times [^>]*' "$trx" | awk "$FUNCOES_AWK"'
    { ini = atributo($0, "start"); fim = atributo($0, "finish") }
    END { if (ini != "" && fim != "") printf "%.0f", epoca(fim) - epoca(ini) }')

  echo "## ${projeto:-$(basename "$trx")}"
  echo

  # Uma linha por teste: início, fim, duração, resultado e classe; ordenada pelo início.
  grep -o '<UnitTestResult [^>]*' "$trx" | awk "$FUNCOES_AWK"'
    {
      nome = atributo($0, "testName"); sub(/\(.*/, "", nome)
      classe = nome; sub(/\.[^.]*$/, "", classe); sub(/.*\./, "", classe)
      split(atributo($0, "duration"), p, ":")
      printf "%.3f %.3f %.3f %s %s\n", epoca(atributo($0, "startTime")), epoca(atributo($0, "endTime")),
        p[1] * 3600 + p[2] * 60 + p[3], atributo($0, "outcome"), classe
    }' | sort -n -k1,1 | awk -v topo="$topo" -v execucao="$execucao" '
    NR == 1 { t0 = $1; fim = $2 }
    {
      if ($1 - fim > lacuna) { lacuna = $1 - fim; lacuna_ini = fim - t0; lacuna_fim = $1 - t0 }
      if ($2 > fim) fim = $2
      soma += $3; n++
      if ($4 != "Passed") { falhas++; lista = lista "- " $5 " (" $4 ")\n" }
      c = $5; cn[c]++; cs[c] += $3
      if (!(c in ci) || $1 < ci[c]) ci[c] = $1
      if ($2 > cf[c]) cf[c] = $2
    }
    END {
      parede = fim - t0
      printf "| Testes | Falhas | Execução | Parede dos testes | Soma das durações | Concorrência média | Maior lacuna |\n"
      printf "|---:|---:|---:|---:|---:|---:|---|\n"
      faixa = (lacuna >= 1 ? sprintf(" (de %.0f s a %.0f s)", lacuna_ini, lacuna_fim) : "")
      printf "| %d | %d | %s s | %.0f s | %.0f s | %.2f | %.0f s%s |\n\n",
        n, falhas, (execucao == "" ? "?" : execucao), parede, soma, (parede > 0 ? soma / parede : 0), lacuna, faixa
      if (falhas > 0) printf "Falhas:\n\n%s\n", lista
      printf "Classes que mais pesam (janela em segundos desde o primeiro teste):\n\n"
      printf "| Classe | Testes | Soma | Janela |\n|---|---:|---:|---|\n"
      # Seleção das maiores somas, sem asort (o awk do runner é o mawk).
      for (k = 0; k < topo; k++) {
        maior = ""
        for (c in cs) if (!(c in usada) && (maior == "" || cs[c] > cs[maior])) maior = c
        if (maior == "") break
        usada[maior] = 1
        printf "| %s | %d | %.1f s | %.0f–%.0f s |\n", maior, cn[maior], cs[maior], ci[maior] - t0, cf[maior] - t0
      }
    }'
  echo
done
