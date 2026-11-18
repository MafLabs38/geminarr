#!/usr/bin/env python3
"""Says, in the log and in the summary of the CI run, what the SonarQube server found: why the quality gate failed, the open issues and the
hotspots still to review, for the pull request (or the branch) that was just analysed. The server's answer is only printed, never run.

The token comes from the environment and is never printed. It reads the SonarQube server of this project, nothing else.
"""
import base64
import json
import os
import sys
import urllib.error
import urllib.parse
import urllib.request

PAGE = 100


def call(host, token, path, **query):
    url = f"{host}/api/{path}?{urllib.parse.urlencode(query)}"
    request = urllib.request.Request(url, headers={"Authorization": "Basic " + base64.b64encode(f"{token}:".encode()).decode()})
    with urllib.request.urlopen(request, timeout=30) as response:
        return json.load(response)


def scope(pull_request, branch):
    if pull_request:
        return {"pullRequest": pull_request}
    return {"branch": branch} if branch else {}


def gate_lines(host, token, key, where):
    status = call(host, token, "qualitygates/project_status", projectKey=key, **where)["projectStatus"]
    lines = [f"Quality gate: {status['status']}"]
    for condition in status.get("conditions", []):
        if condition["status"] != "OK":
            lines.append(f"  - {condition['metricKey']}: {condition.get('actualValue')} (threshold {condition.get('errorThreshold')}, {condition['comparator']})")
    return lines


def issue_lines(host, token, key, where):
    found = call(host, token, "issues/search", componentKeys=key, resolved="false", ps=PAGE, **where)
    lines = [f"Open issues: {found['total']}"]
    for issue in found["issues"]:
        lines.append(f"  - {issue['severity']} {issue['rule']} {issue['component'].split(':', 1)[-1]}:{issue.get('line', '?')} {issue['message']}")
    return lines


def hotspot_lines(host, token, key, where):
    found = call(host, token, "hotspots/search", projectKey=key, status="TO_REVIEW", ps=PAGE, **where)
    lines = [f"Hotspots to review: {found['paging']['total']}"]
    for spot in found["hotspots"]:
        lines.append(f"  - {spot['vulnerabilityProbability']} {spot['ruleKey']} {spot['component'].split(':', 1)[-1]}:{spot.get('line', '?')} {spot['message']}")
    return lines


def main():
    host, token, key = os.environ["SONAR_HOST_URL"].rstrip("/"), os.environ["SONAR_TOKEN"], os.environ["SONAR_PROJECT_KEY"]
    where = scope(os.environ.get("SONAR_PULL_REQUEST", ""), os.environ.get("SONAR_BRANCH", ""))
    lines = []
    for part in (gate_lines, issue_lines, hotspot_lines):
        try:
            lines += part(host, token, key, where)
        except urllib.error.HTTPError as error:  # a part that cannot be read says so, and why; the others are still told
            lines.append(f"{part.__name__}: could not be read (HTTP {error.code}: {error.read(300).decode('utf-8', 'replace')})")
        except Exception as error:
            lines.append(f"{part.__name__}: could not be read ({type(error).__name__})")
    text = "\n".join(lines)
    print(text)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write("### SonarQube\n```\n" + text + "\n```\n")
    return 0


if __name__ == "__main__":
    sys.exit(main())
