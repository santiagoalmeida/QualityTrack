#!/usr/bin/env python3
"""Exporta de SonarQube el detalle detrás de los conteos: cada bug, vulnerabilidad, code smell y hotspot
abierto, con las líneas de código que lo rodean y la explicación de su regla. Solo usa la biblioteca estándar.

Uso:    sonar_findings.py <proyecto>=<clave-sonar>[:<carpeta>] [...] > findings.json
        sonar_findings.py backend=tienda-api web=tienda-web:web

<carpeta> es dónde está, dentro del repositorio, la raíz que Sonar analizó para ese proyecto (por defecto
la carpeta actual). El código se lee del árbol de trabajo, así que se corre justo después del análisis.

Variables: SONAR_HOST_URL, SONAR_TOKEN.
Salida:    {"rules": [...], "projects": {"<proyecto>": [<findings>]}} con los campos que espera
           POST /api/v1/runs en `rules` y en `projects[].findings`.
"""
import base64
import html.parser
import json
import os
import posixpath
import sys
import urllib.error
import urllib.parse
import urllib.request

CONTEXT_LINES = 4          # lines shown above and below the flagged ones
MAX_FLAGGED_LINES = 30     # a finding that spans a whole class is cut here
METRIC_BY_TYPE = {"BUG": "bugs", "VULNERABILITY": "vulnerabilities", "CODE_SMELL": "code_smells"}
BLOCK_TAGS = {"p", "div", "pre", "ul", "ol", "table", "tr", "h1", "h2", "h3", "h4", "h5", "blockquote"}


class HtmlToText(html.parser.HTMLParser):
    """Sonar describes rules in HTML; QualityTrack stores plain text."""

    def __init__(self) -> None:
        super().__init__()
        self.parts: list[str] = []
        self.in_pre = False

    def handle_starttag(self, tag, attrs):
        if tag == "pre":
            self.in_pre = True
            self.parts.append("\n")
        elif tag == "li":
            self.parts.append("\n- ")
        elif tag == "br" or tag in BLOCK_TAGS:
            self.parts.append("\n")

    def handle_endtag(self, tag):
        if tag == "pre":
            self.in_pre = False
        if tag in BLOCK_TAGS:
            self.parts.append("\n")

    def handle_data(self, data):
        self.parts.append(data if self.in_pre else " ".join(data.split()) + (" " if data[-1:].isspace() else ""))


def html_to_text(markup: str | None) -> str | None:
    if not markup:
        return None
    parser = HtmlToText()
    parser.feed(markup)
    lines = [line.rstrip() for line in "".join(parser.parts).split("\n")]
    text, blank = [], False
    for line in lines:  # collapse runs of blank lines
        if line.strip():
            text.append(line)
            blank = False
        elif text and not blank:
            text.append("")
            blank = True
    return "\n".join(text).strip() or None


class Sonar:
    def __init__(self, host: str, token: str) -> None:
        self.host = host.rstrip("/")
        self.auth = "Basic " + base64.b64encode(f"{token}:".encode()).decode()

    def get(self, path: str, **params) -> dict:
        url = f"{self.host}/api/{path}?{urllib.parse.urlencode(params)}"
        request = urllib.request.Request(url, headers={"Authorization": self.auth})
        with urllib.request.urlopen(request) as response:
            return json.load(response)

    def pages(self, path: str, key: str, **params):
        """Every item of a paginated search. Sonar serves at most 10,000 results per query."""
        page = 1
        while page <= 20:
            data = self.get(path, ps=500, p=page, **params)
            items = data.get(key, [])
            yield from items
            total = data.get("paging", {}).get("total", data.get("total", 0))
            if not items or page * 500 >= total:
                return
            page += 1


def snippet_for(root: str, path: str, start: int | None, end: int | None, cache: dict) -> dict | None:
    """Lines around [start, end] read from the working tree; None if the file is not there."""
    if start is None:
        return None
    full = os.path.join(root, path)
    if full not in cache:
        try:
            with open(full, encoding="utf-8", errors="replace") as handle:
                cache[full] = handle.read().splitlines()
        except OSError:
            cache[full] = None
    lines = cache[full]
    if not lines:
        return None
    end = min(end or start, start + MAX_FLAGGED_LINES - 1)
    first = max(1, start - CONTEXT_LINES)
    last = min(len(lines), end + CONTEXT_LINES)
    return {"startLine": first, "lines": [line.expandtabs(4) for line in lines[first - 1:last]]}


def finding_from(issue: dict, metric: str, sonar_key: str, root: str, cache: dict) -> dict:
    path = issue["component"].removeprefix(sonar_key + ":")
    text_range = issue.get("textRange") or {}
    start = text_range.get("startLine", issue.get("line"))
    end = text_range.get("endLine", start)
    finding = {
        "metric": metric,
        "rule": issue.get("rule") or issue.get("ruleKey"),
        "severity": (issue.get("severity") or issue.get("vulnerabilityProbability") or "").lower() or None,
        "message": issue.get("message") or "(sin mensaje)",
        # Stored relative to the repository root, so prompts point at the real file.
        "file": posixpath.normpath(posixpath.join(root.replace(os.sep, "/"), path)),
        "line": start,
        "endLine": end,
        "snippet": snippet_for(root, path, start, end, cache),
    }
    # Offsets are only trustworthy when no tab was expanded on the flagged lines.
    if start is not None and "startOffset" in text_range and finding["snippet"]:
        flagged = cache[os.path.join(root, path)][start - 1:(end or start)]
        if not any("\t" in line for line in flagged):
            finding["startColumn"] = text_range["startOffset"]
            finding["endColumn"] = text_range["endOffset"]
    return {key: value for key, value in finding.items() if value is not None}


def describe_rule(sonar: Sonar, key: str, name: str | None) -> dict:
    rule = {"key": key, "name": name or key}
    try:
        detail = sonar.get("rules/show", key=key)["rule"]
    except (urllib.error.HTTPError, KeyError):
        return rule  # external analyzers' rules have no page in Sonar
    rule["name"] = detail.get("name") or rule["name"]
    sections = {}
    for section in detail.get("descriptionSections", []):
        sections.setdefault(section["key"], section["content"])  # first context wins
    why = "\n".join(filter(None, (sections.get("introduction"), sections.get("root_cause"))))
    rule["description"] = html_to_text(why or sections.get("default") or detail.get("htmlDesc"))
    rule["howToFix"] = html_to_text(sections.get("how_to_fix"))
    return {k: v for k, v in rule.items() if v is not None}


def main() -> int:
    if len(sys.argv) < 2 or sys.argv[1] in ("-h", "--help"):
        print(__doc__)
        return 2

    sonar = Sonar(os.environ["SONAR_HOST_URL"], os.environ["SONAR_TOKEN"])
    projects: dict[str, list] = {}
    rule_names: dict[str, str | None] = {}
    cache: dict = {}

    for spec in sys.argv[1:]:
        project, _, target = spec.partition("=")
        sonar_key, _, root = target.partition(":")
        root = root or "."
        findings = []

        for issue in sonar.pages("issues/search", "issues", componentKeys=sonar_key, resolved="false", additionalFields="rules"):
            metric = METRIC_BY_TYPE.get(issue.get("type"))
            if metric:
                findings.append(finding_from(issue, metric, sonar_key, root, cache))
                rule_names.setdefault(issue["rule"], None)

        try:
            for hotspot in sonar.pages("hotspots/search", "hotspots", projectKey=sonar_key, status="TO_REVIEW"):
                findings.append(finding_from(hotspot, "security_hotspots", sonar_key, root, cache))
                rule_names.setdefault(hotspot["ruleKey"], None)
        except urllib.error.HTTPError:
            pass  # the token may not be allowed to browse hotspots; issues are still exported

        projects[project] = findings
        print(f"{project}: {len(findings)} incidencias", file=sys.stderr)

    rules = [describe_rule(sonar, key, name) for key, name in sorted(rule_names.items())]
    json.dump({"rules": rules, "projects": projects}, sys.stdout, ensure_ascii=False)
    return 0


if __name__ == "__main__":
    sys.exit(main())
