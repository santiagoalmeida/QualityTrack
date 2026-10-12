#!/usr/bin/env bash
# Corre los tests de un proyecto JavaScript/TypeScript con Jest y reporta la corrida a QualityTrack.
#
# Uso: report-jest.sh <solución> [proyecto]
#   report-jest.sh tienda-online web
#
# Variables: QUALITYTRACK_URL, QUALITYTRACK_API_KEY.
# Requiere curl y jq. Se corre desde la carpeta del paquete (donde está package.json).
set -euo pipefail

[ $# -ge 1 ] || { sed -n '2,8p' "$0"; exit 2; }
: "${QUALITYTRACK_URL:?}" "${QUALITYTRACK_API_KEY:?}"

SOLUTION="$1"
PROJECT="${2:-frontend}"

# jest-results.json trae los conteos de tests; coverage/coverage-summary.json, los porcentajes.
npx jest --coverage --coverageReporters=json-summary --json --outputFile=jest-results.json || true

payload="$(jq -n \
  --arg solution "$SOLUTION" --arg project "$PROJECT" \
  --arg branch "${BRANCH:-$(git rev-parse --abbrev-ref HEAD)}" \
  --arg commit "$(git rev-parse HEAD)" \
  --argjson dirty "$([ -n "$(git status --porcelain)" ] && echo true || echo false)" \
  --slurpfile results jest-results.json \
  --slurpfile summary coverage/coverage-summary.json \
  '$results[0] as $r | $summary[0].total as $c
   | {solution: $solution, branch: $branch, commit: $commit, dirty: $dirty,
      projects: [{
        key: $project,
        tests: {total: $r.numTotalTests, passed: $r.numPassedTests, failed: $r.numFailedTests,
                skipped: ($r.numPendingTests + ($r.numTodoTests // 0))},
        metrics: {
          coverage: $c.lines.pct,
          line_coverage: $c.lines.pct,
          branch_coverage: $c.branches.pct,
          # Clave fuera del catálogo: se guarda y se grafica igual, como conteo neutro.
          test_suites: $r.numTotalTestSuites
        }
      }]}')"

curl -fsS -X POST "${QUALITYTRACK_URL%/}/api/v1/runs" \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" -H "Content-Type: application/json" -d "$payload" |
  jq -r '"Avance: \(if .progress == null then "sin metas" else "\(.progress * 100 | round) %" end)"'
