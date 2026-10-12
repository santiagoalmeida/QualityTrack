#!/usr/bin/env bash
# Corre los tests de una solución .NET con cobertura y reporta la corrida a QualityTrack, sin SonarQube.
#
# Uso: report-dotnet.sh <solución> [proyecto] [ruta de la solución o del proyecto de tests]
#   report-dotnet.sh tienda-online api tests/Tienda.Tests
#
# Variables: QUALITYTRACK_URL, QUALITYTRACK_API_KEY.
# Requiere curl, jq y el paquete coverlet.collector en el proyecto de tests.
set -euo pipefail

[ $# -ge 1 ] || { sed -n '2,8p' "$0"; exit 2; }
: "${QUALITYTRACK_URL:?}" "${QUALITYTRACK_API_KEY:?}"

SOLUTION="$1"
PROJECT="${2:-backend}"
TARGET="${3:-.}"
RESULTS="$(mktemp -d)"
trap 'rm -rf "$RESULTS"' EXIT

# Los tests fallidos no impiden reportar: justamente interesa que queden en el historial.
dotnet test "$TARGET" --collect:"XPlat Code Coverage" --logger "trx;LogFileName=tests.trx" --results-directory "$RESULTS" || true

attr() { grep -oE "$1=\"[^\"]*\"" | head -n 1 | cut -d'"' -f2; }

# <Counters total="30" executed="30" passed="30" failed="0" ... /> del reporte TRX.
counters="$(grep -ohE '<Counters [^>]*>' "$RESULTS"/*.trx | head -n 1)"
total="$(attr total <<<"$counters")"
passed="$(attr passed <<<"$counters")"
failed="$(attr failed <<<"$counters")"

# <coverage line-rate="0.87" branch-rate="0.71" ...> del reporte Cobertura.
coverage="$(grep -ohE '<coverage [^>]*>' "$RESULTS"/*/coverage.cobertura.xml | head -n 1)"
line_rate="$(attr line-rate <<<"$coverage")"
branch_rate="$(attr branch-rate <<<"$coverage")"

payload="$(jq -n \
  --arg solution "$SOLUTION" --arg project "$PROJECT" \
  --arg branch "${BRANCH:-$(git rev-parse --abbrev-ref HEAD)}" \
  --arg commit "$(git rev-parse HEAD)" \
  --argjson dirty "$([ -n "$(git status --porcelain)" ] && echo true || echo false)" \
  --argjson total "$total" --argjson passed "$passed" --argjson failed "$failed" \
  --argjson line "$line_rate" --argjson branchRate "$branch_rate" \
  '{solution: $solution, branch: $branch, commit: $commit, dirty: $dirty,
    projects: [{
      key: $project,
      tests: {total: $total, passed: $passed, failed: $failed, skipped: ($total - $passed - $failed)},
      metrics: {
        coverage: ($line * 100 * 10 | round / 10),
        line_coverage: ($line * 100 * 10 | round / 10),
        branch_coverage: ($branchRate * 100 * 10 | round / 10)
      }
    }]}')"

curl -fsS -X POST "${QUALITYTRACK_URL%/}/api/v1/runs" \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" -H "Content-Type: application/json" -d "$payload" |
  jq -r '"Avance: \(if .progress == null then "sin metas" else "\(.progress * 100 | round) %" end)"'
