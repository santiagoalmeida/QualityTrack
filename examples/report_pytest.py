#!/usr/bin/env python3
"""Reporta a QualityTrack una corrida de pytest con cobertura. Solo usa la biblioteca estándar.

Antes:  pytest --junitxml=junit.xml --cov --cov-report=xml
Uso:    report_pytest.py <proyecto> [componente] [junit.xml] [coverage.xml]

Variables: QUALITYTRACK_URL, QUALITYTRACK_API_KEY.
"""
import json
import os
import subprocess
import sys
import urllib.request
import xml.etree.ElementTree as ET


def git(*args: str) -> str:
    return subprocess.run(["git", *args], capture_output=True, text=True, check=True).stdout.strip()


def read_tests(path: str) -> dict:
    root = ET.parse(path).getroot()
    # pytest writes <testsuites><testsuite .../></testsuites>; older versions write a bare <testsuite>.
    suites = [root] if root.tag == "testsuite" else root.findall("testsuite")
    total = sum(int(s.get("tests", 0)) for s in suites)
    failed = sum(int(s.get("failures", 0)) + int(s.get("errors", 0)) for s in suites)
    skipped = sum(int(s.get("skipped", 0)) for s in suites)
    duration = sum(float(s.get("time", 0)) for s in suites)
    return {
        "total": total,
        "passed": total - failed - skipped,
        "failed": failed,
        "skipped": skipped,
        "durationSeconds": round(duration, 2),
    }


def read_coverage(path: str) -> dict:
    root = ET.parse(path).getroot()
    metrics = {"coverage": round(float(root.get("line-rate", 0)) * 100, 1)}
    metrics["line_coverage"] = metrics["coverage"]
    # branch-rate is only meaningful when coverage ran with --cov-branch.
    if int(root.get("branches-valid", 0)) > 0:
        metrics["branch_coverage"] = round(float(root.get("branch-rate", 0)) * 100, 1)
    return metrics


def main() -> int:
    if len(sys.argv) < 2:
        print(__doc__)
        return 2

    project = sys.argv[1]
    component = sys.argv[2] if len(sys.argv) > 2 else "backend"
    junit_path = sys.argv[3] if len(sys.argv) > 3 else "junit.xml"
    coverage_path = sys.argv[4] if len(sys.argv) > 4 else "coverage.xml"

    payload = {
        "project": project,
        "branch": os.environ.get("BRANCH") or git("rev-parse", "--abbrev-ref", "HEAD"),
        "commit": git("rev-parse", "HEAD"),
        "dirty": bool(git("status", "--porcelain")),
        "source": "ci" if os.environ.get("CI") else "local",
        "components": [
            {"key": component, "tests": read_tests(junit_path), "metrics": read_coverage(coverage_path)}
        ],
    }

    request = urllib.request.Request(
        os.environ["QUALITYTRACK_URL"].rstrip("/") + "/api/v1/runs",
        data=json.dumps(payload).encode(),
        headers={"X-Api-Key": os.environ["QUALITYTRACK_API_KEY"], "Content-Type": "application/json"},
        method="POST",
    )
    with urllib.request.urlopen(request) as response:
        result = json.load(response)

    progress = result.get("progress")
    print("Avance:", "sin metas" if progress is None else f"{round(progress * 100)} %")
    return 0


if __name__ == "__main__":
    sys.exit(main())
