#!/usr/bin/env bash
# Reporta a QualityTrack las métricas de uno o más proyectos de SonarQube, como proyectos de una solución.
#
# Uso: report-sonarqube.sh <solución> <proyecto>=<clave-sonar>[:<carpeta>] [...]
#   report-sonarqube.sh tienda-online api=tienda-api web=tienda-web:web
#
# Con python3 y sonar_findings.py junto a este script, envía además el detalle de cada incidencia (archivo,
# línea, código y explicación de la regla). <carpeta> es dónde está, dentro del repositorio, la raíz que
# Sonar analizó para ese proyecto; solo hace falta para ese detalle.
#
# Variables: QUALITYTRACK_URL, QUALITYTRACK_API_KEY, SONAR_HOST_URL, SONAR_TOKEN.
# Requiere curl y jq. Se corre después del análisis de Sonar, desde el repositorio analizado.
set -euo pipefail

[ $# -ge 2 ] || { sed -n '2,12p' "$0"; exit 2; }
: "${QUALITYTRACK_URL:?}" "${QUALITYTRACK_API_KEY:?}" "${SONAR_HOST_URL:?}" "${SONAR_TOKEN:?}"

SOLUTION="$1"; shift
METRICS="alert_status,coverage,line_coverage,branch_coverage,bugs,vulnerabilities,security_hotspots,security_hotspots_reviewed,code_smells,reliability_rating,security_rating,security_review_rating,sqale_rating,sqale_index,sqale_debt_ratio,duplicated_lines_density,duplicated_lines,duplicated_blocks,complexity,cognitive_complexity,violations,blocker_violations,critical_violations,ncloc,tests,test_failures,test_errors,skipped_tests"

sonar() { curl -fsS -u "$SONAR_TOKEN:" "$SONAR_HOST_URL/api/$1"; }

projects="[]"
for pair in "$@"; do
  project="${pair%%=*}"
  sonar_key="${pair#*=}"
  sonar_key="${sonar_key%%:*}"
  measures="$(sonar "measures/component?component=$sonar_key&metricKeys=$METRICS")"
  project_json="$(jq -n --arg key "$project" --argjson measures "$measures" '
    ($measures.component.measures | map({(.metric): .value}) | add) as $m
    | def num($k): $m[$k] | if . == null then null else tonumber end;
      {
        key: $key,
        metrics: ({
          # Calidad
          quality_gate: (if $m.alert_status == "OK" then 1 else 0 end),
          reliability_rating: num("reliability_rating"),
          bugs: num("bugs"),
          coverage: num("coverage"),
          line_coverage: num("line_coverage"),
          branch_coverage: num("branch_coverage"),
          # Seguridad
          security_rating: num("security_rating"),
          vulnerabilities: num("vulnerabilities"),
          security_review_rating: num("security_review_rating"),
          security_hotspots: num("security_hotspots"),
          security_hotspots_reviewed: num("security_hotspots_reviewed"),
          # Mantenibilidad y deuda técnica
          maintainability_rating: num("sqale_rating"),
          code_smells: num("code_smells"),
          technical_debt_minutes: num("sqale_index"),
          technical_debt_ratio: num("sqale_debt_ratio"),
          duplicated_lines_density: num("duplicated_lines_density"),
          duplicated_lines: num("duplicated_lines"),
          duplicated_blocks: num("duplicated_blocks"),
          open_issues: num("violations"),
          blocker_issues: num("blocker_violations"),
          critical_issues: num("critical_violations"),
          complexity: num("complexity"),
          cognitive_complexity: num("cognitive_complexity"),
          lines_of_code: num("ncloc")
        # Sonar omite las medidas que no aplican (p. ej. hotspots revisados sin hotspots): no se envían.
        } | with_entries(select(.value != null)))
      }
      # Sonar solo conoce los tests si el análisis importó sus reportes de ejecución.
      + (if $m.tests == null then {} else
          (($m.test_failures // "0" | tonumber) + ($m.test_errors // "0" | tonumber)) as $failed
          | ($m.skipped_tests // "0" | tonumber) as $skipped
          | {tests: {total: ($m.tests | tonumber), failed: $failed, skipped: $skipped,
                     passed: (($m.tests | tonumber) - $failed - $skipped)}}
        end)')"
  projects="$(jq --argjson p "$project_json" '. + [$p]' <<<"$projects")"
done

payload="$(jq -n \
  --arg solution "$SOLUTION" \
  --arg branch "${BRANCH:-$(git rev-parse --abbrev-ref HEAD)}" \
  --arg commit "$(git rev-parse HEAD)" \
  --argjson dirty "$([ -n "$(git status --porcelain)" ] && echo true || echo false)" \
  --arg source "${CI:+ci}" \
  --argjson projects "$projects" \
  '{solution: $solution, branch: $branch, commit: $commit, dirty: $dirty,
    source: (if $source == "" then "local" else $source end), projects: $projects}')"

# El detalle pesa varios MB, así que la corrida viaja en un archivo y no como argumento.
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
echo "$payload" > "$work/run.json"
helper="$(dirname "${BASH_SOURCE[0]}")/sonar_findings.py"
if command -v python3 >/dev/null && [ -f "$helper" ] && python3 "$helper" "$@" > "$work/findings.json" 2>/dev/null; then
  jq --slurpfile detail "$work/findings.json" \
    '.rules = $detail[0].rules | .projects |= map(. + {findings: ($detail[0].projects[.key] // [])})' \
    "$work/run.json" > "$work/run-with-findings.json"
  mv "$work/run-with-findings.json" "$work/run.json"
fi

curl -fsS -X POST "${QUALITYTRACK_URL%/}/api/v1/runs" \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" -H "Content-Type: application/json" --data-binary "@$work/run.json" |
  jq -r '"Avance: \(if .progress == null then "sin metas" else "\(.progress * 100 | round) %" end)"
       + (if .previousProgress == null then "" else " (antes \(.previousProgress * 100 | round) %)" end)'
