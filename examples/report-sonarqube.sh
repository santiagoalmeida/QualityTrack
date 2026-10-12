#!/usr/bin/env bash
# Reporta a QualityTrack las métricas de uno o más proyectos de SonarQube, cada uno como un componente.
#
# Uso: report-sonarqube.sh <proyecto> <componente>=<clave-sonar> [<componente>=<clave-sonar> ...]
#   report-sonarqube.sh tienda-online api=tienda-api web=tienda-web
#
# Variables: QUALITYTRACK_URL, QUALITYTRACK_API_KEY, SONAR_HOST_URL, SONAR_TOKEN.
# Requiere curl y jq. Se corre después del análisis de Sonar, desde el repositorio analizado.
set -euo pipefail

[ $# -ge 2 ] || { sed -n '2,8p' "$0"; exit 2; }
: "${QUALITYTRACK_URL:?}" "${QUALITYTRACK_API_KEY:?}" "${SONAR_HOST_URL:?}" "${SONAR_TOKEN:?}"

PROJECT="$1"; shift
METRICS="alert_status,coverage,line_coverage,branch_coverage,bugs,vulnerabilities,security_hotspots,code_smells,reliability_rating,security_rating,sqale_rating,sqale_index,duplicated_lines_density,ncloc,tests,test_failures,test_errors,skipped_tests"

sonar() { curl -fsS -u "$SONAR_TOKEN:" "$SONAR_HOST_URL/api/$1"; }

components="[]"
for pair in "$@"; do
  component="${pair%%=*}"
  sonar_key="${pair#*=}"
  measures="$(sonar "measures/component?component=$sonar_key&metricKeys=$METRICS")"
  component_json="$(jq -n --arg key "$component" --argjson measures "$measures" '
    ($measures.component.measures | map({(.metric): .value}) | add) as $m
    | def num($k): $m[$k] | if . == null then empty else tonumber end;
      {
        key: $key,
        metrics: {
          quality_gate: (if $m.alert_status == "OK" then 1 else 0 end),
          coverage: num("coverage"),
          line_coverage: num("line_coverage"),
          branch_coverage: num("branch_coverage"),
          bugs: num("bugs"),
          vulnerabilities: num("vulnerabilities"),
          security_hotspots: num("security_hotspots"),
          code_smells: num("code_smells"),
          reliability_rating: num("reliability_rating"),
          security_rating: num("security_rating"),
          maintainability_rating: num("sqale_rating"),
          technical_debt_minutes: num("sqale_index"),
          duplicated_lines_density: num("duplicated_lines_density"),
          lines_of_code: num("ncloc")
        }
      }
      # Sonar solo conoce los tests si el análisis importó sus reportes de ejecución.
      + (if $m.tests == null then {} else
          (($m.test_failures // "0" | tonumber) + ($m.test_errors // "0" | tonumber)) as $failed
          | ($m.skipped_tests // "0" | tonumber) as $skipped
          | {tests: {total: ($m.tests | tonumber), failed: $failed, skipped: $skipped,
                     passed: (($m.tests | tonumber) - $failed - $skipped)}}
        end)')"
  components="$(jq --argjson c "$component_json" '. + [$c]' <<<"$components")"
done

payload="$(jq -n \
  --arg project "$PROJECT" \
  --arg branch "${BRANCH:-$(git rev-parse --abbrev-ref HEAD)}" \
  --arg commit "$(git rev-parse HEAD)" \
  --argjson dirty "$([ -n "$(git status --porcelain)" ] && echo true || echo false)" \
  --arg source "${CI:+ci}" \
  --argjson components "$components" \
  '{project: $project, branch: $branch, commit: $commit, dirty: $dirty,
    source: (if $source == "" then "local" else $source end), components: $components}')"

curl -fsS -X POST "${QUALITYTRACK_URL%/}/api/v1/runs" \
  -H "X-Api-Key: $QUALITYTRACK_API_KEY" -H "Content-Type: application/json" -d "$payload" |
  jq -r '"Avance: \(if .progress == null then "sin metas" else "\(.progress * 100 | round) %" end)"
       + (if .previousProgress == null then "" else " (antes \(.previousProgress * 100 | round) %)" end)'
